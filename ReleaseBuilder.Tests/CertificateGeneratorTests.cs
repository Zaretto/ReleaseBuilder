using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace ReleaseBuilder.Tests
{
    /// <summary>
    /// Covers managed PFX generation. Everything here runs in memory, so it behaves the same on
    /// Windows, Linux and macOS and needs no openssl, no fixture files and no OS trust store.
    /// </summary>
    public class CertificateGeneratorTests
    {
        private static CertificateGenerateOptions Options(string subject = "CN=Test") =>
            new()
            {
                Subject = subject,
                // 2048 keeps the tests quick; the production default is 3072.
                KeySize = 2048,
                NotBefore = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Days = 30,
            };

        [Fact]
        public void Self_signed_certificate_has_expected_subject_validity_and_signature()
        {
            var o = Options("CN=Acme Test, O=Acme Ltd, C=GB");
            var (cert, pfx) = CertificateGenerator.Create(o);
            using (cert)
            {
                Assert.Contains("CN=Acme Test", cert.Subject);
                Assert.Equal(cert.Subject, cert.Issuer);
                Assert.True(cert.HasPrivateKey);
                Assert.Equal(30, (int)Math.Round((cert.NotAfter.ToUniversalTime() - cert.NotBefore.ToUniversalTime()).TotalDays));
                // sha256RSA
                Assert.Equal("1.2.840.113549.1.1.11", cert.SignatureAlgorithm.Value);
                Assert.NotEmpty(pfx);
            }
        }

        [Fact]
        public void Token_signing_purpose_writes_no_eku_extension()
        {
            var o = Options();
            var purpose = CertificatePurpose.Resolve("token-signing")!;
            o.Ekus = purpose.Ekus;
            o.KeyUsage = purpose.KeyUsage;

            var (cert, _) = CertificateGenerator.Create(o);
            using (cert)
            {
                var (present, oids) = CertificateChecker.ReadEkus(cert);
                Assert.False(present);
                Assert.Empty(oids);

                var ku = cert.Extensions.OfType<X509KeyUsageExtension>().Single();
                Assert.True(ku.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature));
                Assert.True(ku.KeyUsages.HasFlag(X509KeyUsageFlags.KeyEncipherment));
            }
        }

        [Fact]
        public void Code_signing_purpose_writes_the_code_signing_eku_and_end_entity_constraints()
        {
            var o = Options();
            var purpose = CertificatePurpose.Resolve("code-signing")!;
            o.Ekus = purpose.Ekus;
            o.KeyUsage = purpose.KeyUsage;

            var (cert, _) = CertificateGenerator.Create(o);
            using (cert)
            {
                var (present, oids) = CertificateChecker.ReadEkus(cert);
                Assert.True(present);
                Assert.Contains(CertificatePurpose.CodeSigningOid, oids);

                var bc = cert.Extensions.OfType<X509BasicConstraintsExtension>().Single();
                Assert.False(bc.CertificateAuthority);

                Assert.Contains(cert.Extensions.OfType<X509SubjectKeyIdentifierExtension>(), _ => true);
            }
        }

        [Fact]
        public void Ca_purpose_produces_a_certificate_authority_with_key_cert_sign()
        {
            var o = Options("CN=Test Root");
            var purpose = CertificatePurpose.Resolve("ca")!;
            o.Ekus = purpose.Ekus;
            o.KeyUsage = purpose.KeyUsage;
            o.Ca = purpose.Ca;

            var (cert, _) = CertificateGenerator.Create(o);
            using (cert)
            {
                var bc = cert.Extensions.OfType<X509BasicConstraintsExtension>().Single();
                Assert.True(bc.CertificateAuthority);

                var ku = cert.Extensions.OfType<X509KeyUsageExtension>().Single();
                Assert.True(ku.KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign));
                Assert.True(ku.KeyUsages.HasFlag(X509KeyUsageFlags.CrlSign));
            }
        }

        [Fact]
        public void Ecdsa_key_produces_a_usable_private_key()
        {
            var o = Options();
            o.KeyAlgorithm = "ecdsa";
            o.KeySize = 256;

            var (cert, _) = CertificateGenerator.Create(o);
            using (cert)
            {
                using var key = cert.GetECDsaPrivateKey();
                Assert.NotNull(key);
                Assert.Equal(256, key!.KeySize);
                Assert.Equal(("ecdsa", 256), CertificateChecker.DescribeKey(cert));
            }
        }

        [Fact]
        public void Dns_names_are_written_as_subject_alternative_names()
        {
            var o = Options();
            o.DnsNames = new[] { "example.test", "www.example.test" };

            var (cert, _) = CertificateGenerator.Create(o);
            using (cert)
            {
                var san = cert.Extensions.First(e => e.Oid?.Value == "2.5.29.17");
                var text = san.Format(false);
                Assert.Contains("example.test", text);
                Assert.Contains("www.example.test", text);
            }
        }

        [Fact]
        public void Pfx_round_trips_through_the_loader_with_the_password()
        {
            var o = Options();
            o.Password = "test-password-1234";

            var (cert, pfx) = CertificateGenerator.Create(o);
            using (cert)
            using (var loaded = CertificateChecker.Load(pfx, o.Password))
            {
                Assert.Equal(cert.Thumbprint, loaded.Thumbprint);
                Assert.True(loaded.HasPrivateKey);
            }
        }

        [Fact]
        public void Pfx_round_trips_when_no_password_is_set()
        {
            var (cert, pfx) = CertificateGenerator.Create(Options());
            using (cert)
            using (var loaded = CertificateChecker.Load(pfx, null))
                Assert.Equal(cert.Thumbprint, loaded.Thumbprint);
        }

        [Fact]
        public void Loading_a_pfx_with_the_wrong_password_throws()
        {
            var o = Options();
            o.Password = "test-password-1234";
            var (cert, pfx) = CertificateGenerator.Create(o);
            using (cert)
                Assert.Throws<CryptographicException>(() => CertificateChecker.Load(pfx, "not-the-password"));
        }

        /// <summary>
        /// CertificateRequest.Create() only ever sees the public key, so the issued certificate
        /// comes back without a private key and has to have it reattached. Without that the PFX
        /// is unusable, which is invisible until something tries to sign with it.
        /// </summary>
        [Fact]
        public void Ca_signed_leaf_keeps_its_private_key_and_chains_to_the_issuer()
        {
            var caOptions = Options("CN=Test Root");
            caOptions.Ca = true;
            caOptions.KeyUsage = X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign;
            caOptions.Days = 60;
            var (ca, _) = CertificateGenerator.Create(caOptions);

            using (ca)
            {
                var leafOptions = Options("CN=Test Leaf");
                leafOptions.Issuer = ca;
                var (leaf, leafPfx) = CertificateGenerator.Create(leafOptions);

                using (leaf)
                {
                    Assert.True(leaf.HasPrivateKey);
                    Assert.Equal(ca.Subject, leaf.Issuer);
                    Assert.NotEqual(ca.SerialNumber, leaf.SerialNumber);
                    Assert.Contains(leaf.Extensions.OfType<X509AuthorityKeyIdentifierExtension>(), _ => true);

                    // The exported PFX must carry the key too, not just the in-memory object.
                    using var reloaded = CertificateChecker.Load(leafPfx, null);
                    Assert.True(reloaded.HasPrivateKey);
                }
            }
        }

        [Theory]
        [InlineData("rsa", 2048, true)]
        [InlineData("rsa", 1024, false)]
        [InlineData("ecdsa", 256, true)]
        [InlineData("ecdsa", 2048, false)]
        public void Key_size_validation_rejects_sizes_the_algorithm_cannot_use(string algorithm, int size, bool expected)
        {
            Assert.Equal(expected, CertificateGenerator.IsValidKeySize(algorithm, size));
        }

        [Fact]
        public void Unsupported_hash_is_rejected_with_a_named_error()
        {
            var ex = Assert.Throws<ArgumentException>(() => CertificateGenerator.ParseHash("md5"));
            Assert.Contains("md5", ex.Message);
        }
    }
}
