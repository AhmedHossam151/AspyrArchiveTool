namespace AspyrArchiveTool.AspyrArchive
{
    public interface IAspyrFormat
    {
        string Name { get; }
        bool CanHandle(string filePath);
        void Unpack(string inputPath, string outputDir);

        void Pack(string inputDir, string outputPath, bool useCompression = false);
    }
}