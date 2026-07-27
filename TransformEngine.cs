using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ReleaseBuilder
{
    public class TransformEngine
    {
        public const int DefaultKeyLength = 32;

        private const string DigitChars = "0123456789";
        private const string AlphaChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private const string AlphaNumericChars = AlphaChars + DigitChars;

        private readonly VariableStore _vars;

        public TransformEngine(VariableStore vars)
        {
            _vars = vars ?? throw new ArgumentNullException(nameof(vars));
        }

        public string Transform(string? transformation, string v)
        {
            if (string.IsNullOrEmpty(transformation))
                return v;
            // \, in the transform string is a literal comma (not an argument separator).
            const string CommaEscapePlaceholder = "\x01\x02\x01";
            var normalized = transformation.Replace("\\,", CommaEscapePlaceholder);
            var parts = normalized.Split(',').Select(p => p.Replace(CommaEscapePlaceholder, ",")).ToArray();
            if (parts.Length >= 1)
            {
                var method = parts[0];
                switch (method.ToLower())
                {
                    case "set":
                        return ExpandVars(parts[1])!;
                    case "getversion":
                        {
                            ArgCheck(parts, 2, transformation);
                            v = ExpandVars(parts[1])!;
                            var vp = v.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            v = vp.Last();
                            var rv = Regex.Match(v, @"\d+.+\d");
                            if (rv != null)
                                return rv.Value;
                            return transformation;
                        }

                    case "replace":
                        {
                            ArgCheck(parts, 3, transformation);
                            var s1 = ExpandVars(parts[1])!;
                            var s2 = ExpandVars(parts[2])!;
                            if (s1 == s2)
                                return v;
                            return v.Replace(s1, s2);
                        }
                    case "regex-replace":
                        {
                            ArgCheck(parts, 3, transformation);
                            var s1 = ExpandVars(parts[1])!;
                            var s2 = ExpandVars(parts[2])!;
                            return Regex.Replace(v, s1, s2);
                        }
                    case "random-key":
                        {
                            // random-key[,length[,charset]]; a new key each time it is evaluated.
                            if (parts.Length > 3)
                                throw new Exception(String.Format("ERROR: Transform: {0} at most 3 arguments allowed; [{1}]", transformation, string.Join(",", parts)));
                            var length = DefaultKeyLength;
                            if (parts.Length >= 2)
                            {
                                var lengthArg = ExpandVars(parts[1])!.Trim();
                                if (lengthArg.Length > 0 && (!int.TryParse(lengthArg, out length) || length <= 0))
                                    throw new Exception(String.Format("ERROR: Transform: {0} length must be a positive integer; [{1}]", transformation, parts[1]));
                            }
                            var charset = parts.Length >= 3 ? ExpandVars(parts[2])! : "";
                            return RandomKey(length, charset);
                        }
                    case "when":
                        {
                            ArgCheck(parts, 4, transformation);
                            var s1 = ExpandVars(parts[1])!;
                            var cond = parts[2];
                            var s2 = ExpandVars(parts[3])!;
                            return Compare(s1, cond, s2);
                        }
                    default:
                        throw new Exception("Unknown transform " + method);
                }
            }
            return transformation;
        }

        /// <summary>
        /// Generates a cryptographically random key of <paramref name="length"/> characters.
        /// <paramref name="charset"/> is either a named set (alnum, alpha, numeric, hex, base64,
        /// base64url, ascii) or the literal characters to choose from; empty means alnum.
        /// </summary>
        public static string RandomKey(int length, string? charset = null)
        {
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Key length must be a positive integer");

            var chars = CharacterSet(charset);
            if (chars.Length < 2)
                throw new Exception("ERROR: Transform: random-key character set must contain at least 2 distinct characters");

            return new string(RandomNumberGenerator.GetItems<char>(chars.AsSpan(), length));
        }

        private static string CharacterSet(string? charset)
        {
            if (string.IsNullOrEmpty(charset))
                return AlphaNumericChars;

            switch (charset.ToLower())
            {
                case "alnum":
                case "alphanumeric":
                    return AlphaNumericChars;
                case "alpha":
                    return AlphaChars;
                case "numeric":
                case "digits":
                    return DigitChars;
                case "hex":
                    return "0123456789abcdef";
                case "base64":
                    return AlphaNumericChars + "+/";
                case "base64url":
                    return AlphaNumericChars + "-_";
                case "ascii":
                    // All printable ASCII except space; may need escaping where the key is used.
                    return new string(Enumerable.Range(0x21, 0x7e - 0x21 + 1).Select(c => (char)c).ToArray());
                default:
                    // Literal character set; duplicates removed so each character is equally likely.
                    return new string(charset.Distinct().ToArray());
            }
        }

        public string Compare(string? s1, string cond, string? s2)
        {
            var result = false;
            switch (cond.ToLower())
            {
                case "eq":
                case "==":
                case "=":
                    result = s1 == s2;
                    break;

                case "ne":
                case "!=":
                case "<>":
                    result = s1 != s2;
                    break;
                default:
                    RLog.ErrorFormat("Unknown comparison {0}", cond);
                    break;
            }
            if (result)
                return "1";
            return "";
        }

        public string? ExpandVars(XAttribute? att)
        {
            if (att == null)
                return "";
            return ExpandVars(att.Value);
        }

        public string? ExpandVars(string? rv)
        {
            if (rv == null)
                return "";

            rv = Regex.Replace(rv, @"\$(\w+)", match =>
            {
                string variable = match.Groups[1].Value;
                var envValue = Environment.GetEnvironmentVariable(variable);
                if (envValue == null)
                    throw new Exception("Environment variable not found: " + variable);
                return envValue;
            });
            rv = Regex.Replace(rv, "\\~(.*?)\\~", match =>
            {
                string variable = match.Groups[1].Value;
                if (_vars.ContainsKey(variable))
                    return _vars[variable];
                else
                    throw new Exception("Variable not found: " + variable);
            });

            return rv;
        }

        private static void ArgCheck(string[] parts, int requiredLength, string transformation)
        {
            if (parts.Length != requiredLength)
                throw new Exception(String.Format("ERROR: Transform: {0} {1} arguments required; [{2}]", transformation, requiredLength, string.Join(",", parts)));
        }
    }
}
