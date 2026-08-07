using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ReleaseBuilder
{
    internal sealed class CertificateCheckOptions
    {
        public string File = "";
        public string? Password;

        /// <summary>Minimum days remaining before NotAfter. Zero means "just not expired".</summary>
        public int MinDays;

        public bool RequirePrivateKey = true;

        /// <summary>Required extended key usages, as resolved OIDs.</summary>
        public IReadOnlyList<string> RequiredEkus = Array.Empty<string>();

        public string? RequiredKeyAlgorithm;
        public int MinKeySize;
        public bool RequireTrustedChain;
        public bool CheckRevocation;
        public string? ExpectedThumbprint;
        public string? ExpectedSubject;

        /// <summary>Injectable so expiry tests are deterministic.</summary>
        public DateTimeOffset Now = DateTimeOffset.UtcNow;

        public IReadOnlyList<X509Certificate2> TrustRoots = Array.Empty<X509Certificate2>();
    }

    internal sealed record CertificateCheckResult(
        bool Ok,
        IReadOnlyList<string> Failures,
        IReadOnlyList<string> Warnings,
        string Subject,
        string Issuer,
        string Thumbprint,
        string SerialNumber,
        DateTime NotBeforeUtc,
        DateTime NotAfterUtc,
        int DaysRemaining,
        bool HasPrivateKey,
        string KeyAlgorithm,
        int KeySize,
        IReadOnlyList<string> Ekus,
        bool ChainValid,
        IReadOnlyList<string> ChainStatus)
    {
        public static CertificateCheckResult Failed(string failure) =>
            new(false, new[] { failure }, Array.Empty<string>(),
                "", "", "", "", default, default, 0, false, "", 0,
                Array.Empty<string>(), false, Array.Empty<string>());
    }

    internal static class CertificateChecker
    {
        public static CertificateCheckResult Check(CertificateCheckOptions o)
        {
            if (!System.IO.File.Exists(o.File))
                return CertificateCheckResult.Failed($"certificate file not found: {o.File}");

            byte[] data;
            try
            {
                data = System.IO.File.ReadAllBytes(o.File);
            }
            catch (IOException ex)
            {
                return CertificateCheckResult.Failed($"cannot read {o.File}: {ex.Message}");
            }

            X509Certificate2 cert;
            try
            {
                cert = Load(data, o.Password);
            }
            catch (CryptographicException)
            {
                // The underlying message varies by platform and can echo back what was supplied,
                // so it is replaced rather than wrapped.
                return CertificateCheckResult.Failed(
                    $"cannot open {o.File}: wrong password, or the file is not a PKCS#12 archive");
            }

            using (cert)
                return Inspect(cert, o);
        }

        private static CertificateCheckResult Inspect(X509Certificate2 cert, CertificateCheckOptions o)
        {
            var failures = new List<string>();
            var warnings = new List<string>();

            // NotBefore/NotAfter are local DateTimes. Compare in UTC — CI runners are UTC and
            // developer machines are not.
            var notBefore = cert.NotBefore.ToUniversalTime();
            var notAfter = cert.NotAfter.ToUniversalTime();
            var now = o.Now.UtcDateTime;
            var daysRemaining = (int)Math.Floor((notAfter - now).TotalDays);

            if (now < notBefore)
                failures.Add($"not valid until {Format(notBefore)}");
            else if (now > notAfter)
                failures.Add($"expired on {Format(notAfter)} ({-daysRemaining} days ago)");
            else if (o.MinDays > 0 && daysRemaining < o.MinDays)
                failures.Add($"expires on {Format(notAfter)}, {daysRemaining} days remaining (min-days is {o.MinDays})");

            if (o.RequirePrivateKey && !cert.HasPrivateKey)
                failures.Add("no private key");

            var (keyAlgorithm, keySize) = DescribeKey(cert);
            if (!string.IsNullOrEmpty(o.RequiredKeyAlgorithm) &&
                !string.Equals(keyAlgorithm, o.RequiredKeyAlgorithm, StringComparison.OrdinalIgnoreCase))
                failures.Add($"key algorithm is {(keyAlgorithm.Length == 0 ? "unrecognised" : keyAlgorithm)}, expected {o.RequiredKeyAlgorithm}");

            if (o.MinKeySize > 0 && keySize < o.MinKeySize)
                failures.Add($"key size is {keySize} bits, min-key-size is {o.MinKeySize}");

            var (hasEkuExtension, ekus) = ReadEkus(cert);
            if (o.RequiredEkus.Count > 0)
            {
                if (!hasEkuExtension)
                {
                    // RFC 5280: no EKU extension means the certificate is unconstrained, and
                    // Authenticode and friends accept it. Worth flagging, not worth failing.
                    warnings.Add("certificate declares no extended key usage, so it is unconstrained — " +
                                 $"assuming it is valid for {string.Join(", ", o.RequiredEkus.Select(CertificatePurpose.DescribeEku))}");
                }
                else if (!ekus.Contains(CertificatePurpose.AnyPurposeOid))
                {
                    foreach (var required in o.RequiredEkus)
                        if (!ekus.Contains(required))
                            failures.Add($"missing extended key usage {CertificatePurpose.DescribeEku(required)} ({required})");
                }
            }

            if (!string.IsNullOrEmpty(o.ExpectedThumbprint) &&
                !string.Equals(NormaliseThumbprint(o.ExpectedThumbprint), NormaliseThumbprint(cert.Thumbprint), StringComparison.OrdinalIgnoreCase))
                failures.Add($"thumbprint is {cert.Thumbprint}, expected {NormaliseThumbprint(o.ExpectedThumbprint)}");

            if (!string.IsNullOrEmpty(o.ExpectedSubject) &&
                cert.Subject.IndexOf(o.ExpectedSubject, StringComparison.OrdinalIgnoreCase) < 0)
                failures.Add($"subject is '{cert.Subject}', expected it to contain '{o.ExpectedSubject}'");

            var chainValid = false;
            IReadOnlyList<string> chainStatus = Array.Empty<string>();
            if (o.RequireTrustedChain)
            {
                (chainValid, chainStatus) = BuildChain(cert, o);
                if (!chainValid)
                    failures.Add($"chain is not trusted: {(chainStatus.Count > 0 ? string.Join(", ", chainStatus) : "no detail reported")}");
            }

            return new CertificateCheckResult(
                failures.Count == 0, failures, warnings,
                cert.Subject, cert.Issuer, cert.Thumbprint, cert.SerialNumber,
                notBefore, notAfter, daysRemaining,
                cert.HasPrivateKey, keyAlgorithm, keySize, ekus,
                chainValid, chainStatus);
        }

        private static (bool Valid, IReadOnlyList<string> Status) BuildChain(X509Certificate2 cert, CertificateCheckOptions o)
        {
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = o.CheckRevocation ? X509RevocationMode.Online : X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationTime = o.Now.UtcDateTime;

            foreach (var oid in o.RequiredEkus)
                chain.ChainPolicy.ApplicationPolicy.Add(new Oid(oid));

            if (o.TrustRoots.Count > 0)
            {
                // CustomRootTrust behaves identically on Windows, Linux and macOS, which is what
                // lets a chain test run anywhere without touching an OS trust store.
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                foreach (var root in o.TrustRoots)
                    chain.ChainPolicy.CustomTrustStore.Add(root);
            }

            try
            {
                var valid = chain.Build(cert);
                var status = chain.ChainStatus
                    .Where(s => s.Status != X509ChainStatusFlags.NoError)
                    .Select(s => s.Status.ToString())
                    .Distinct()
                    .ToArray();
                return (valid, status);
            }
            finally
            {
                // Each chain element holds its own native handle, separate from the chain's.
                foreach (var element in chain.ChainElements)
                    element.Certificate.Dispose();
            }
        }

        /// <summary>
        /// The one place PKCS#12 data is turned into a certificate. net8.0 has no
        /// X509CertificateLoader; on a net9+ retarget this constructor becomes SYSLIB0057-obsolete
        /// and this is the only method that has to change.
        /// </summary>
        internal static X509Certificate2 Load(byte[] pkcs12, string? password)
        {
            return new X509Certificate2(pkcs12, password, ImportFlags());
        }

        /// <summary>
        /// Loads a certificate for use as a trust root, discarding any private key it carries.
        /// Accepts DER or PEM certificate files as well as PKCS#12 archives.
        /// </summary>
        internal static X509Certificate2 LoadTrustRoot(byte[] data, string? password)
        {
            if (string.IsNullOrEmpty(password))
            {
                try
                {
                    return new X509Certificate2(data);
                }
                catch (CryptographicException)
                {
                    // Not a bare certificate — fall through and try it as a PKCS#12 archive.
                }
            }

            using var withKey = Load(data, password);
            return new X509Certificate2(withKey.Export(X509ContentType.Cert));
        }

        internal static X509KeyStorageFlags ImportFlags() =>
            // EphemeralKeySet is not supported for PKCS#12 import on macOS (it throws
            // PlatformNotSupportedException). Everywhere else it matters: without it Windows
            // writes the private key into the user's CNG/CAPI key container on disk and leaves
            // it there after the X509Certificate2 is disposed.
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? X509KeyStorageFlags.DefaultKeySet
                : X509KeyStorageFlags.EphemeralKeySet;

        internal static string NormaliseThumbprint(string? value) =>
            (value ?? "").Replace(":", "").Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();

        internal static (string Algorithm, int Size) DescribeKey(X509Certificate2 cert)
        {
            using var rsa = cert.GetRSAPublicKey();
            if (rsa != null)
                return ("rsa", rsa.KeySize);

            using var ecdsa = cert.GetECDsaPublicKey();
            if (ecdsa != null)
                return ("ecdsa", ecdsa.KeySize);

            return ("", 0);
        }

        /// <summary>
        /// Returns whether an EKU extension is present at all, and the OIDs it lists. The two are
        /// distinct: an absent extension means unconstrained, an empty one means nothing is permitted.
        /// </summary>
        internal static (bool Present, IReadOnlyList<string> Oids) ReadEkus(X509Certificate2 cert)
        {
            var present = false;
            var oids = new List<string>();
            foreach (var extension in cert.Extensions)
            {
                if (extension is not X509EnhancedKeyUsageExtension eku)
                    continue;
                present = true;
                foreach (var oid in eku.EnhancedKeyUsages)
                    if (oid.Value != null && !oids.Contains(oid.Value))
                        oids.Add(oid.Value);
            }
            return (present, oids);
        }

        internal static string Format(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ssZ");
    }
}
