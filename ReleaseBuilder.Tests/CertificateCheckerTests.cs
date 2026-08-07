using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace ReleaseBuilder.Tests
{
    /// <summary>
    /// Covers certificate validation. Every certificate is generated in-process and
    /// CertificateCheckOptions.Now is injected, so expiry cases are deterministic and the whole
    /// file runs identically on Windows, Linux and macOS.
    ///
    /// There is deliberately no test that installs a certificate into an X509Store: that is not
    /// portable and needs elevation on some platforms. Chain trust is covered with
    /// X509ChainTrustMode.CustomRootTrust instead.
    /// </summary>
    public class CertificateCheckerTests : IDisposable
    {
        private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        private readonly string _tempDir;

        public CertificateCheckerTests()
        {
            RLog.ResetErrorCount();
            _tempDir = Path.Combine(AppContext.BaseDirectory, "test_certcheck_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static CertificateGenerateOptions Generate(string subject = "CN=Test", int days = 90, int startOffsetDays = -1) =>
            new()
            {
                Subject = subject,
                KeySize = 2048,
                NotBefore = Now.AddDays(startOffsetDays),
                Days = days,
            };

        /// <summary>Generates a certificate and writes its PFX into the temp directory.</summary>
        private string WritePfx(CertificateGenerateOptions options, string name = "test.pfx")
        {
            var (cert, pfx) = CertificateGenerator.Create(options);
            cert.Dispose();
            var path = Path.Combine(_tempDir, name);
            File.WriteAllBytes(path, pfx);
            return path;
        }

        private CertificateCheckOptions Check(string file) => new() { File = file, Now = Now };

        // ------------------------------------------------------------------
        // Validity window
        // ------------------------------------------------------------------

        [Fact]
        public void Valid_certificate_passes_with_default_options()
        {
            var result = CertificateChecker.Check(Check(WritePfx(Generate())));
            Assert.True(result.Ok, string.Join("; ", result.Failures));
            Assert.Empty(result.Failures);
            Assert.Contains("CN=Test", result.Subject);
            Assert.True(result.HasPrivateKey);
            Assert.Equal("rsa", result.KeyAlgorithm);
            Assert.Equal(2048, result.KeySize);
        }

        [Fact]
        public void Expired_certificate_fails_with_negative_days_remaining()
        {
            var result = CertificateChecker.Check(Check(WritePfx(Generate(days: 1, startOffsetDays: -400))));
            Assert.False(result.Ok);
            Assert.True(result.DaysRemaining < 0);
            Assert.Contains(result.Failures, f => f.Contains("expired"));
        }

        [Fact]
        public void Not_yet_valid_certificate_fails()
        {
            var result = CertificateChecker.Check(Check(WritePfx(Generate(startOffsetDays: 10))));
            Assert.False(result.Ok);
            Assert.Contains(result.Failures, f => f.Contains("not valid until"));
        }

        [Fact]
        public void Min_days_boundary_is_inclusive()
        {
            // Starts 1 day ago and lasts 31, so exactly 30 days remain at Now.
            var file = WritePfx(Generate(days: 31));

            var options = Check(file);
            options.MinDays = 30;
            Assert.True(CertificateChecker.Check(options).Ok);

            options.MinDays = 31;
            var tooSoon = CertificateChecker.Check(options);
            Assert.False(tooSoon.Ok);
            Assert.Contains(tooSoon.Failures, f => f.Contains("min-days"));
        }

        // ------------------------------------------------------------------
        // Private key, algorithm, size
        // ------------------------------------------------------------------

        [Fact]
        public void Certificate_without_a_private_key_fails_unless_the_requirement_is_turned_off()
        {
            var (cert, _) = CertificateGenerator.Create(Generate());
            var path = Path.Combine(_tempDir, "public.cer");
            using (cert)
                File.WriteAllBytes(path, cert.Export(X509ContentType.Cert));

            var options = Check(path);
            var failed = CertificateChecker.Check(options);
            Assert.False(failed.Ok);
            Assert.Contains("no private key", failed.Failures);

            options.RequirePrivateKey = false;
            Assert.True(CertificateChecker.Check(options).Ok);
        }

        [Fact]
        public void Key_algorithm_and_size_requirements_are_enforced()
        {
            var options = Check(WritePfx(Generate()));

            options.RequiredKeyAlgorithm = "ecdsa";
            Assert.Contains(CertificateChecker.Check(options).Failures, f => f.Contains("key algorithm"));

            options.RequiredKeyAlgorithm = "rsa";
            options.MinKeySize = 4096;
            Assert.Contains(CertificateChecker.Check(options).Failures, f => f.Contains("min-key-size"));

            options.MinKeySize = 2048;
            Assert.True(CertificateChecker.Check(options).Ok);
        }

        // ------------------------------------------------------------------
        // Extended key usage
        // ------------------------------------------------------------------

        /// <summary>
        /// RFC 5280: an absent EKU extension means the certificate is unconstrained, and
        /// Authenticode and friends accept it. Warning rather than failing is a deliberate
        /// contract — a token-signing certificate has no EKU at all.
        /// </summary>
        [Fact]
        public void Required_eku_against_a_certificate_with_no_eku_warns_rather_than_fails()
        {
            var options = Check(WritePfx(Generate()));
            options.RequiredEkus = new[] { CertificatePurpose.CodeSigningOid };

            var result = CertificateChecker.Check(options);
            Assert.True(result.Ok, string.Join("; ", result.Failures));
            Assert.Contains(result.Warnings, w => w.Contains("unconstrained"));
        }

        [Fact]
        public void Required_eku_against_a_certificate_with_a_different_eku_fails()
        {
            var generate = Generate();
            generate.Ekus = new[] { CertificatePurpose.ServerAuthOid };
            var options = Check(WritePfx(generate));
            options.RequiredEkus = new[] { CertificatePurpose.CodeSigningOid };

            var result = CertificateChecker.Check(options);
            Assert.False(result.Ok);
            Assert.Contains(result.Failures, f => f.Contains("code-signing"));
            Assert.Empty(result.Warnings);
        }

        [Fact]
        public void Required_eku_is_satisfied_when_the_certificate_declares_it()
        {
            var generate = Generate();
            generate.Ekus = new[] { CertificatePurpose.CodeSigningOid };
            var options = Check(WritePfx(generate));
            options.RequiredEkus = new[] { CertificatePurpose.CodeSigningOid };

            var result = CertificateChecker.Check(options);
            Assert.True(result.Ok, string.Join("; ", result.Failures));
            Assert.Contains(CertificatePurpose.CodeSigningOid, result.Ekus);
        }

        [Fact]
        public void Any_purpose_eku_satisfies_every_requirement()
        {
            var generate = Generate();
            generate.Ekus = new[] { CertificatePurpose.AnyPurposeOid };
            var options = Check(WritePfx(generate));
            options.RequiredEkus = new[] { CertificatePurpose.CodeSigningOid };

            Assert.True(CertificateChecker.Check(options).Ok);
        }

        // ------------------------------------------------------------------
        // Identity
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Thumbprint_is_matched_after_normalising_separators_and_case(bool decorate)
        {
            var file = WritePfx(Generate());
            var options = Check(file);
            var actual = CertificateChecker.Check(options).Thumbprint;

            options.ExpectedThumbprint = decorate
                ? string.Join(":", Enumerable.Range(0, actual.Length / 2).Select(i => actual.Substring(i * 2, 2))).ToLowerInvariant()
                : actual;

            Assert.True(CertificateChecker.Check(options).Ok);
        }

        [Fact]
        public void Mismatched_thumbprint_fails()
        {
            var options = Check(WritePfx(Generate()));
            options.ExpectedThumbprint = new string('A', 40);
            Assert.Contains(CertificateChecker.Check(options).Failures, f => f.Contains("thumbprint"));
        }

        [Fact]
        public void Subject_is_matched_as_a_case_insensitive_substring()
        {
            var options = Check(WritePfx(Generate("CN=Acme Signing, O=Acme Ltd")));

            options.ExpectedSubject = "acme signing";
            Assert.True(CertificateChecker.Check(options).Ok);

            options.ExpectedSubject = "Contoso";
            Assert.Contains(CertificateChecker.Check(options).Failures, f => f.Contains("subject"));
        }

        // ------------------------------------------------------------------
        // Unreadable input
        // ------------------------------------------------------------------

        [Fact]
        public void Wrong_password_reports_one_normalised_failure()
        {
            var generate = Generate();
            generate.Password = "correct-password-1234";
            var options = Check(WritePfx(generate));
            options.Password = "wrong-password-1234";

            var result = CertificateChecker.Check(options);
            Assert.False(result.Ok);
            var failure = Assert.Single(result.Failures);
            Assert.Contains("wrong password", failure);
            // The supplied password must never be echoed back into the message.
            Assert.DoesNotContain("wrong-password-1234", failure);
        }

        [Fact]
        public void Random_bytes_produce_a_clean_failure_rather_than_an_exception()
        {
            var path = Path.Combine(_tempDir, "garbage.pfx");
            File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(512));

            var result = CertificateChecker.Check(Check(path));
            Assert.False(result.Ok);
            Assert.Contains(result.Failures, f => f.Contains("PKCS#12"));
        }

        [Fact]
        public void Missing_file_produces_a_clean_failure()
        {
            var result = CertificateChecker.Check(Check(Path.Combine(_tempDir, "nothing-here.pfx")));
            Assert.False(result.Ok);
            Assert.Contains(result.Failures, f => f.Contains("not found"));
        }

        // ------------------------------------------------------------------
        // Chain trust — portable via CustomRootTrust
        // ------------------------------------------------------------------

        [Fact]
        public void Leaf_chains_to_a_supplied_trust_root_but_not_to_an_empty_store()
        {
            var caOptions = Generate("CN=Test Root", days: 120);
            caOptions.Ca = true;
            caOptions.KeyUsage = X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign;
            var (ca, _) = CertificateGenerator.Create(caOptions);

            using (ca)
            using (var caPublicOnly = new X509Certificate2(ca.Export(X509ContentType.Cert)))
            {
                var leafOptions = Generate("CN=Test Leaf");
                leafOptions.Issuer = ca;
                var leafFile = WritePfx(leafOptions, "leaf.pfx");

                var trusted = Check(leafFile);
                trusted.RequireTrustedChain = true;
                trusted.TrustRoots = new[] { caPublicOnly };
                var trustedResult = CertificateChecker.Check(trusted);
                Assert.True(trustedResult.Ok, string.Join("; ", trustedResult.Failures));
                Assert.True(trustedResult.ChainValid);

                var untrusted = Check(leafFile);
                untrusted.RequireTrustedChain = true;
                var untrustedResult = CertificateChecker.Check(untrusted);
                Assert.False(untrustedResult.Ok);
                Assert.Contains(untrustedResult.ChainStatus, s => s.Contains("UntrustedRoot") || s.Contains("PartialChain"));
            }
        }

        // ------------------------------------------------------------------
        // Purpose and EKU parsing
        // ------------------------------------------------------------------

        [Fact]
        public void Friendly_eku_names_and_raw_oids_both_resolve()
        {
            Assert.True(CertificatePurpose.TryParseEkuList("code-signing, 1.3.6.1.5.5.7.3.8", out var oids, out _));
            Assert.Equal(new[] { CertificatePurpose.CodeSigningOid, CertificatePurpose.TimeStampingOid }, oids);
        }

        [Fact]
        public void Unknown_eku_name_is_reported_rather_than_silently_dropped()
        {
            Assert.False(CertificatePurpose.TryParseEkuList("code-signing,nonsense", out _, out var invalid));
            Assert.Equal("nonsense", invalid);
        }

        [Fact]
        public void Eku_none_resolves_to_an_empty_list()
        {
            Assert.True(CertificatePurpose.TryParseEkuList("none", out var oids, out _));
            Assert.Empty(oids);
        }

        [Fact]
        public void Key_usage_names_combine_into_flags()
        {
            Assert.True(CertificatePurpose.TryParseKeyUsage("digital-signature,key-encipherment", out var flags, out _));
            Assert.Equal(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, flags);

            Assert.False(CertificatePurpose.TryParseKeyUsage("digital-signature,nonsense", out _, out var invalid));
            Assert.Equal("nonsense", invalid);
        }

        [Fact]
        public void Unknown_purpose_resolves_to_null()
        {
            Assert.Null(CertificatePurpose.Resolve("nonsense"));
            Assert.NotNull(CertificatePurpose.Resolve("TOKEN-SIGNING"));
        }
    }
}
