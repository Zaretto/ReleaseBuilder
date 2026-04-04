using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ReleaseBuilder
{
    public class TransformEngine
    {
        private readonly VariableStore _vars;

        public TransformEngine(VariableStore vars)
        {
            _vars = vars ?? throw new ArgumentNullException(nameof(vars));
        }

        public string Transform(string? transformation, string v)
        {
            if (string.IsNullOrEmpty(transformation))
                return v;
            var parts = transformation.Split(',');
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
                            var vp = v.Split('\\');
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
