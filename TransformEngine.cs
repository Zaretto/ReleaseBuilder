using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
                            var key = RandomKey(length, charset);
                            // Generated keys exist to be secrets; keep them out of the log.
                            RLog.RegisterSecret(key);
                            return key;
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

        /// <summary>
        /// Expands $ENVIRONMENT and ~VARIABLE~ references in a single left to right pass, so
        /// substituted values are never re-scanned. An environment variable name may itself
        /// contain ~VARIABLE~ segments (e.g. $PROJ_PORTAL_~TYPE~), allowing per-target secrets
        /// to be held in the environment.
        /// </summary>
        public string? ExpandVars(string? rv)
        {
            if (rv == null)
                return "";

            var sb = new StringBuilder(rv.Length);
            var i = 0;
            while (i < rv.Length)
            {
                var c = rv[i];

                // $NAME, where NAME may embed ~VARIABLE~ segments. The name must start with a
                // literal character so that "$~VAR~" keeps its old meaning of a literal $.
                if (c == '$' && i + 1 < rv.Length && IsNameChar(rv[i + 1]))
                {
                    sb.Append(ExpandEnvReference(rv, ref i));
                    continue;
                }

                // ~VARIABLE~
                if (c == '~')
                {
                    var close = FindClosingTilde(rv, i + 1);
                    if (close >= 0)
                    {
                        sb.Append(LookupVariable(rv.Substring(i + 1, close - i - 1)));
                        i = close + 1;
                        continue;
                    }
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Reads the environment variable reference starting at the $ in <paramref name="rv"/>[<paramref name="i"/>]
        /// and returns its value, leaving <paramref name="i"/> on the first character after the reference.
        /// </summary>
        private string ExpandEnvReference(string rv, ref int i)
        {
            i++; // skip the $
            var name = new StringBuilder();
            // Name up to the first ~VARIABLE~ segment, kept for the fallback below.
            string? literalName = null;
            var literalEnd = -1;

            while (i < rv.Length)
            {
                if (IsNameChar(rv[i]))
                {
                    name.Append(rv[i]);
                    i++;
                }
                else if (rv[i] == '~')
                {
                    var close = FindClosingTilde(rv, i + 1);
                    if (close < 0)
                        break; // unpaired ~ is literal text, not part of the name
                    if (literalName == null)
                    {
                        literalName = name.ToString();
                        literalEnd = i;
                    }
                    name.Append(LookupVariable(rv.Substring(i + 1, close - i - 1)));
                    i = close + 1;
                }
                else
                    break;
            }

            var envName = name.ToString();
            var envValue = Environment.GetEnvironmentVariable(envName);
            if (envValue != null)
                return envValue;

            // A composed name that does not exist falls back to the literal part of the name,
            // leaving the ~VARIABLE~ text to be expanded on its own as it was before.
            if (literalName != null)
            {
                var literalValue = Environment.GetEnvironmentVariable(literalName);
                if (literalValue != null)
                {
                    i = literalEnd;
                    return literalValue;
                }
            }

            throw new Exception("Environment variable not found: " + envName);
        }

        /// <summary>
        /// Finds the closing ~ of a variable reference. A reference cannot span a line, so text
        /// such as an npm version range ("~8.0.4" on one line, "~17.0.0" on the next) is literal.
        /// </summary>
        private static int FindClosingTilde(string rv, int from)
        {
            for (var j = from; j < rv.Length; j++)
            {
                if (rv[j] == '~')
                    return j;
                if (rv[j] == '\n')
                    return -1;
            }
            return -1;
        }

        private string LookupVariable(string variable)
        {
            if (_vars.ContainsKey(variable))
                return _vars[variable];
            throw new Exception("Variable not found: " + variable);
        }

        private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static void ArgCheck(string[] parts, int requiredLength, string transformation)
        {
            if (parts.Length != requiredLength)
                throw new Exception(String.Format("ERROR: Transform: {0} {1} arguments required; [{2}]", transformation, requiredLength, string.Join(",", parts)));
        }
    }
}
