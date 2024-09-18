namespace FarmAPI
{
    public static class Paths
    {
        public static string GetTempFileName(string extension)
        {
            var tmpPath = Path.GetTempFileName();
            var tmpPathWithExtension = Path.ChangeExtension(tmpPath, extension);

            try
            {
                File.Move(tmpPath, tmpPathWithExtension);

                return tmpPathWithExtension;
            }
            catch (IOException ex)
            {
                File.Delete(tmpPath);
                throw new IOException($"Failed to create temporary file with extension {extension}.", ex);
            }
        }
    }
}
