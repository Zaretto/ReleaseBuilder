namespace ReleaseBuilder
{
    public class VariableStore
    {
        private readonly Dictionary<string, string> _vars = new(StringComparer.OrdinalIgnoreCase);

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(value))
                RLog.ErrorFormat("{0} is null or empty", key);
            RLog.DebugFormat("{0}={1}", key, value);
            _vars[key] = value;
        }

        public void Set(string key, long? value)
        {
            if (value.HasValue)
                _vars[key] = value.Value.ToString();
        }

        public string Get(string key)
        {
            return _vars[key];
        }

        public bool TryGet(string key, out string value)
        {
            return _vars.TryGetValue(key, out value!);
        }

        public bool ContainsKey(string key)
        {
            return _vars.ContainsKey(key);
        }

        public IReadOnlyDictionary<string, string> All => _vars;

        public string this[string key]
        {
            get => _vars[key];
            set => _vars[key] = value;
        }
    }
}
