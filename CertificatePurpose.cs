using System.Security.Cryptography.X509Certificates;

namespace ReleaseBuilder
{
    /// <summary>
    /// What a certificate is for, expressed as the extensions that encode it.
    /// </summary>
    /// <param name="Ekus">
    /// Extended key usage OIDs. Empty means no EKU extension at all — per RFC 5280 that is
    /// "unconstrained", which is what a token-signing certificate wants.
    /// </param>
    /// <param name="WarnIfSelfSigned">
    /// Whether a self-signed certificate for this purpose deserves a warning. True only where
    /// public trust is the point (code signing, TLS server auth); a self-signed token-signing
    /// certificate is the normal and correct answer, so warning there would be noise.
    /// </param>
    internal sealed record CertificatePurposeInfo(
        IReadOnlyList<string> Ekus,
        X509KeyUsageFlags KeyUsage,
        bool Ca,
        bool WarnIfSelfSigned);

    internal static class CertificatePurpose
    {
        public const string CodeSigningOid = "1.3.6.1.5.5.7.3.3";
        public const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";
        public const string ClientAuthOid = "1.3.6.1.5.5.7.3.2";
        public const string EmailProtectionOid = "1.3.6.1.5.5.7.3.4";
        public const string TimeStampingOid = "1.3.6.1.5.5.7.3.8";
        public const string OcspSigningOid = "1.3.6.1.5.5.7.3.9";
        public const string AnyPurposeOid = "2.5.29.37.0";

        public const string Default = "token-signing";

        private static readonly Dictionary<string, string> _ekuNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["code-signing"] = CodeSigningOid,
            ["server-auth"] = ServerAuthOid,
            ["client-auth"] = ClientAuthOid,
            ["email-protection"] = EmailProtectionOid,
            ["time-stamping"] = TimeStampingOid,
            ["ocsp-signing"] = OcspSigningOid,
            ["any"] = AnyPurposeOid,
        };

        private static readonly Dictionary<string, CertificatePurposeInfo> _purposes = new(StringComparer.OrdinalIgnoreCase)
        {
            // OpenIddict/ABP authserver.pfx and friends: no EKU, signs and encrypts tokens.
            ["token-signing"] = new(Array.Empty<string>(),
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false, false),

            ["code-signing"] = new(new[] { CodeSigningOid },
                X509KeyUsageFlags.DigitalSignature, false, true),

            ["server-auth"] = new(new[] { ServerAuthOid },
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false, true),

            ["client-auth"] = new(new[] { ClientAuthOid },
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false, false),

            ["ca"] = new(Array.Empty<string>(),
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true, false),

            ["none"] = new(Array.Empty<string>(),
                X509KeyUsageFlags.DigitalSignature, false, false),
        };

        public static IEnumerable<string> Names => _purposes.Keys;

        /// <summary>Looks up a purpose preset. Returns null for an unknown name.</summary>
        public static CertificatePurposeInfo? Resolve(string? purpose)
        {
            if (string.IsNullOrEmpty(purpose))
                return null;
            return _purposes.TryGetValue(purpose, out var info) ? info : null;
        }

        /// <summary>
        /// Maps a friendly EKU name to its OID. Anything containing a dot is assumed to already
        /// be an OID and is passed through, so unlisted purposes remain expressible.
        /// </summary>
        public static string? ResolveEku(string? name)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name))
                return null;
            if (_ekuNames.TryGetValue(name, out var oid))
                return oid;
            return name.Contains('.') ? name : null;
        }

        /// <summary>Parses a comma-separated eku= attribute. Returns false with the offending entry in <paramref name="invalid"/>.</summary>
        public static bool TryParseEkuList(string? list, out string[] oids, out string? invalid)
        {
            invalid = null;
            var result = new List<string>();
            foreach (var entry in (list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                // "none" is how a config asks for no EKU extension at all.
                if (string.Equals(entry, "none", StringComparison.OrdinalIgnoreCase))
                    continue;
                var oid = ResolveEku(entry);
                if (oid == null)
                {
                    invalid = entry;
                    oids = Array.Empty<string>();
                    return false;
                }
                if (!result.Contains(oid))
                    result.Add(oid);
            }
            oids = result.ToArray();
            return true;
        }

        private static readonly Dictionary<string, X509KeyUsageFlags> _keyUsageNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["digital-signature"] = X509KeyUsageFlags.DigitalSignature,
            ["non-repudiation"] = X509KeyUsageFlags.NonRepudiation,
            ["key-encipherment"] = X509KeyUsageFlags.KeyEncipherment,
            ["data-encipherment"] = X509KeyUsageFlags.DataEncipherment,
            ["key-agreement"] = X509KeyUsageFlags.KeyAgreement,
            ["key-cert-sign"] = X509KeyUsageFlags.KeyCertSign,
            ["crl-sign"] = X509KeyUsageFlags.CrlSign,
            ["encipher-only"] = X509KeyUsageFlags.EncipherOnly,
            ["decipher-only"] = X509KeyUsageFlags.DecipherOnly,
            ["none"] = X509KeyUsageFlags.None,
        };

        /// <summary>Parses a comma-separated key-usage= attribute. Returns false with the offending entry in <paramref name="invalid"/>.</summary>
        public static bool TryParseKeyUsage(string? list, out X509KeyUsageFlags flags, out string? invalid)
        {
            invalid = null;
            flags = X509KeyUsageFlags.None;
            foreach (var entry in (list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!_keyUsageNames.TryGetValue(entry, out var flag))
                {
                    invalid = entry;
                    return false;
                }
                flags |= flag;
            }
            return true;
        }

        /// <summary>Friendly name for an OID, for log and variable output. Falls back to the OID itself.</summary>
        public static string DescribeEku(string oid)
        {
            foreach (var kv in _ekuNames)
                if (kv.Value == oid)
                    return kv.Key;
            return oid;
        }
    }
}
