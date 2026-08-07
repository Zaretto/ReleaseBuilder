using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ReleaseBuilder
{
    internal sealed class CertificateGenerateOptions
    {
        public string Subject = "";
        public string KeyAlgorithm = "rsa";
        public int KeySize = DefaultRsaKeySize;
        public string HashAlgorithm = "sha256";
        public DateTimeOffset NotBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        public int Days = DefaultDays;

        /// <summary>Resolved EKU OIDs. Empty means no EKU extension is written at all.</summary>
        public IReadOnlyList<string> Ekus = Array.Empty<string>();

        public X509KeyUsageFlags KeyUsage = X509KeyUsageFlags.DigitalSignature;
        public bool Ca;
        public IReadOnlyList<string> DnsNames = Array.Empty<string>();
        public string? Password;

        /// <summary>CA certificate to sign with. Null produces a self-signed certificate.</summary>
        public X509Certificate2? Issuer;

        public const int DefaultRsaKeySize = 3072;
        public const int DefaultEcdsaKeySize = 256;
        public const int DefaultDays = 730;
    }

    internal static class CertificateGenerator
    {
        /// <summary>
        /// Builds a certificate and its PKCS#12 encoding. The returned certificate holds the
        /// private key and is the caller's to dispose.
        /// </summary>
        public static (X509Certificate2 Certificate, byte[] Pfx) Create(CertificateGenerateOptions o)
        {
            var hash = ParseHash(o.HashAlgorithm);
            var notAfter = o.NotBefore.AddDays(o.Days);
            var subject = new X500DistinguishedName(o.Subject);

            AsymmetricAlgorithm key;
            CertificateRequest req;
            if (IsEcdsa(o.KeyAlgorithm))
            {
                var ecdsa = ECDsa.Create(ParseCurve(o.KeySize));
                key = ecdsa;
                req = new CertificateRequest(subject, ecdsa, hash);
            }
            else
            {
                var rsa = RSA.Create(o.KeySize);
                key = rsa;
                req = new CertificateRequest(subject, rsa, hash, RSASignaturePadding.Pkcs1);
            }

            try
            {
                req.CertificateExtensions.Add(o.Ca
                    ? X509BasicConstraintsExtension.CreateForCertificateAuthority(null)
                    : X509BasicConstraintsExtension.CreateForEndEntity(critical: true));

                if (o.KeyUsage != X509KeyUsageFlags.None)
                    req.CertificateExtensions.Add(new X509KeyUsageExtension(o.KeyUsage, critical: true));

                // No EKU extension when the list is empty: per RFC 5280 that is "unconstrained",
                // which is what a token-signing certificate needs.
                if (o.Ekus.Count > 0)
                {
                    var oids = new OidCollection();
                    foreach (var oid in o.Ekus)
                        oids.Add(new Oid(oid, CertificatePurpose.DescribeEku(oid)));
                    req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(oids, critical: false));
                }

                if (o.DnsNames.Count > 0)
                {
                    var san = new SubjectAlternativeNameBuilder();
                    foreach (var dns in o.DnsNames)
                        san.AddDnsName(dns);
                    req.CertificateExtensions.Add(san.Build(critical: false));
                }

                req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, critical: false));

                X509Certificate2 cert;
                if (o.Issuer == null)
                {
                    cert = req.CreateSelfSigned(o.NotBefore, notAfter);
                }
                else
                {
                    req.CertificateExtensions.Add(
                        X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                            o.Issuer, includeKeyIdentifier: true, includeIssuerAndSerial: false));

                    var serial = RandomNumberGenerator.GetBytes(16);
                    serial[0] &= 0x7F;      // keep the DER INTEGER positive

                    // req.Create() returns the signed certificate WITHOUT the private key —
                    // it only ever saw the public half. Reattach it or the PFX is unusable.
                    using var issued = req.Create(o.Issuer, o.NotBefore, notAfter, serial);
                    cert = key is ECDsa ecdsaKey
                        ? issued.CopyWithPrivateKey(ecdsaKey)
                        : issued.CopyWithPrivateKey((RSA)key);
                }

                return (cert, ExportPfx(cert, o.Password));
            }
            finally
            {
                key.Dispose();
            }
        }

        internal static byte[] ExportPfx(X509Certificate2 cert, string? password)
        {
            // net8.0 offers no control over the PKCS#12 password-based encryption. The runtime
            // writes legacy (3DES/RC2 + SHA-1) encryption, which every signtool/certutil accepts.
            // On net9.0+ the default becomes AES-256/PBES2, which older Windows tooling rejects,
            // so a retarget must change this line to:
            //   cert.ExportPkcs12(Pkcs12ExportPbeParameters.Pkcs12TripleDesSha1, password)
            // Keep this the only PFX export in the codebase so that stays a one-line change.
            return cert.Export(X509ContentType.Pkcs12, password);
        }

        internal static bool IsEcdsa(string? algorithm) =>
            string.Equals(algorithm, "ecdsa", StringComparison.OrdinalIgnoreCase);

        /// <summary>Validates a key algorithm name. Returns false for anything unsupported.</summary>
        internal static bool IsValidKeyAlgorithm(string? algorithm) =>
            string.Equals(algorithm, "rsa", StringComparison.OrdinalIgnoreCase) || IsEcdsa(algorithm);

        internal static HashAlgorithmName ParseHash(string? hash) => hash?.ToLowerInvariant() switch
        {
            "sha256" => HashAlgorithmName.SHA256,
            "sha384" => HashAlgorithmName.SHA384,
            "sha512" => HashAlgorithmName.SHA512,
            _ => throw new ArgumentException($"unsupported hash algorithm '{hash}' (expected sha256, sha384 or sha512)"),
        };

        internal static ECCurve ParseCurve(int keySize) => keySize switch
        {
            256 => ECCurve.NamedCurves.nistP256,
            384 => ECCurve.NamedCurves.nistP384,
            521 => ECCurve.NamedCurves.nistP521,
            _ => throw new ArgumentException($"unsupported ecdsa key size {keySize} (expected 256, 384 or 521)"),
        };

        /// <summary>Rejects key sizes the algorithm cannot use, so the error names the attribute rather than surfacing a crypto exception.</summary>
        internal static bool IsValidKeySize(string? algorithm, int keySize)
        {
            if (IsEcdsa(algorithm))
                return keySize is 256 or 384 or 521;
            return keySize is 2048 or 3072 or 4096;
        }
    }
}
