namespace ReleaseBuilder
{
    public class FileDetails
    {
        /// <summary>
        /// where the file was found
        /// </summary>
        private string FilePath { get; }
        /// <summary>
        /// full path to the file.
        /// </summary>
        public string Name { get; }

        public string NewFileName { get; }
        public FileDetails(int skipCount, string path, string name, string newName)
        {
            if (skipCount > 0)
            {
                var n1 = name.Replace(path, "").TrimStart('\\');
                var parts = Path.GetDirectoryName(n1).Replace('/', '\\').Split('\\').Take(skipCount);
                FilePath = Path.Combine(path, string.Join("\\", parts));
            }
            else
                FilePath = path;
            Name = name;
            NewFileName = newName;  
        }
        public FileDetails(string path, string name)
        {
            FilePath = path;
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
            if (FilePath != null && Name != null)
                return (FilePath+Name).GetHashCode();
            return 0;
        }

        internal string GetArchiveName()
        {
            if (!string.IsNullOrEmpty(NewFileName))
                return NewFileName;
            else
                return Name.Replace(FilePath, "").Trim("\\/".ToArray());
        }
    }
}
