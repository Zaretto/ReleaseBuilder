namespace ReleaseBuilder
{
    public class FileDetails
    {
        /// <summary>
        /// where the file was found
        /// </summary>
        private string Path { get; }
        /// <summary>
        /// full path to the file.
        /// </summary>
        public string Name { get; }
        public FileDetails(string path, string name)
        {
            Path = path;   
            Name = name;
        }
        public override bool Equals(object obj)
        {
            var aobj = obj as FileDetails;
            if (aobj != null)
                return aobj.GetHashCode() == GetHashCode();
            return false;
        }
        public override int GetHashCode()
        {
            if (Path != null && Name != null)
                return (Path+Name).GetHashCode();
            return 0;
        }

        internal string GetArchiveName()
        {
            return Name.Replace(Path, "").Trim("\\/".ToArray());
        }
    }
}
