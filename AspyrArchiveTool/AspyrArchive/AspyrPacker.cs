using System.IO.Compression;
using System.Text;
using AspyrArchiveTool.Helpers;

namespace AspyrArchiveTool.AspyrArchive
{
    public class AspyrPacker
    {
        private const int ChunkSize = 4096;
        private const long MaxArchiveSize = 3L * 1024 * 1024 * 1024 + 900L * 1024 * 1024;

        private HashSet<string> _filesToCompress;
        private HashSet<string> _dirsToCompress;

        private class TocEntry
        {
            public string RelativePath { get; set; }
            public long Offset { get; set; }
            public long UncompressedSize { get; set; }
            public long CompressedSize { get; set; }
        }

        public void Pack(string inputDir, string outputPath, bool useCompression, bool addCrc, string selectiveListPath = null)
        {
            _filesToCompress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _dirsToCompress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            bool selectiveMode = !string.IsNullOrEmpty(selectiveListPath);

            if (selectiveMode)
            {
                if (!File.Exists(selectiveListPath))
                    throw new FileNotFoundException($"Selective list file not found: {selectiveListPath}");

                foreach (string originalLine in File.ReadLines(selectiveListPath))
                {
                    string line = originalLine.Trim();

                    if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                        continue;

                    bool hadTrailingSlash = line.EndsWith("/") || line.EndsWith("\\");

                    string trimmed = line.Replace("\\", "/").Trim('/');

                    if (string.IsNullOrEmpty(trimmed))
                        continue;

                    if (trimmed.Contains("*") || trimmed.Contains("?"))
                    {
                        Console.WriteLine($"Warning: Wildcards not supported yet: {originalLine}");
                        continue;
                    }

                    if (hadTrailingSlash)
                    {
                        _dirsToCompress.Add(trimmed);
                    }
                    else
                    {
                        _filesToCompress.Add(trimmed);
                    }
                }

                Console.WriteLine($"Selective compression enabled: {_filesToCompress.Count} files + {_dirsToCompress.Count} dirs");
            }

            Console.WriteLine($"[Packer] Source: {inputDir}");
            Console.WriteLine($"[Packer] Mode: {(useCompression ? "Smart Compression" : "Store (RAW)")}");
            Console.WriteLine($"[Packer] Chunk Size: {ChunkSize}");

            var allFiles = Directory.GetFiles(inputDir, "*", SearchOption.AllDirectories);
            var allDirs = Directory.GetDirectories(inputDir, "*", SearchOption.AllDirectories);
            var tocEntries = new List<TocEntry>();

            using (var fs = new FileStream(outputPath, FileMode.Create))
            using (var writer = new BinaryWriter(fs))
            {
                foreach (var filePath in allFiles)
                {
                    string relPath = Path.GetRelativePath(inputDir, filePath).Replace("\\", "/");

                    if (relPath.EndsWith(".ds_store", StringComparison.OrdinalIgnoreCase) ||
                        relPath.EndsWith("thumbs.db", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string status = $"Packing: {relPath}";
                    int maxWidth = Console.WindowWidth - 1;
                    if (status.Length > maxWidth) status = status.Substring(0, maxWidth);
                    Console.Write($"\r{status.PadRight(maxWidth)}");

                    byte[] fileData = File.ReadAllBytes(filePath);
                    long currentGlobalOffset = fs.Position;
                    byte[] dataToWrite;
                    long zSize;

                    bool shouldCompress = false;

                    if (selectiveMode)
                    {
                        string dirPart = Path.GetDirectoryName(relPath)?.Replace("\\", "/") ?? "";

                        bool inCompressDir = _dirsToCompress.Any(d =>
                            dirPart.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                            dirPart.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase));

                        bool exactFile = _filesToCompress.Contains(relPath, StringComparer.OrdinalIgnoreCase);

                        shouldCompress = inCompressDir || exactFile;
                    }
                    else
                    {
                        shouldCompress = useCompression;
                    }

                    if (shouldCompress)
                    {
                        byte[] container = CreateContainer(fileData, currentGlobalOffset);
                        if (container.Length < fileData.Length)
                        {
                            dataToWrite = container;
                            zSize = container.Length;
                        }
                        else
                        {
                            dataToWrite = fileData;
                            zSize = fileData.Length;
                        }
                    }
                    else
                    {
                        dataToWrite = fileData;
                        zSize = fileData.Length;
                    }

                    if (fs.Position + dataToWrite.LongLength > MaxArchiveSize)
                    {
                        throw new Exception($"\nArchive limit reached! Adding this file would exceed 4GB limit.\n" +
                                            $"File: {relPath}\n" +
                                            $"Current Size: {fs.Position / 1024 / 1024} MB\n" +
                                            $"File Size: {dataToWrite.Length / 1024 / 1024} MB");
                    }

                    writer.Write(dataToWrite);

                    tocEntries.Add(new TocEntry
                    {
                        RelativePath = relPath,
                        Offset = currentGlobalOffset,
                        UncompressedSize = fileData.Length,
                        CompressedSize = zSize
                    });
                }

                foreach (var dirPath in allDirs)
                {
                    string relPath = Path.GetRelativePath(inputDir, dirPath).Replace("\\", "/");
                    tocEntries.Add(new TocEntry
                    {
                        RelativePath = relPath,
                        Offset = 0,
                        UncompressedSize = 0,
                        CompressedSize = 0
                    });
                }

                Console.WriteLine($"\nFiles written. Building TOC... (Total items: {tocEntries.Count})");

                tocEntries = tocEntries.OrderBy(e => e.RelativePath.ToLowerInvariant()).ToList();

                long tocStartOffset = fs.Position;

                byte[] rawToc;
                using (var ms = new MemoryStream())
                using (var tocWriter = new BinaryWriter(ms))
                {
                    tocWriter.Write((long)tocEntries.Count);
                    foreach (var entry in tocEntries)
                    {
                        byte[] nameBytes = Encoding.UTF8.GetBytes(entry.RelativePath);
                        tocWriter.Write((long)nameBytes.Length);
                        tocWriter.Write(nameBytes);
                        tocWriter.Write(entry.Offset);
                        tocWriter.Write(entry.UncompressedSize);
                        tocWriter.Write(entry.CompressedSize);
                    }
                    rawToc = ms.ToArray();
                }

                byte[] compressedToc = SimpleZlibCompress(rawToc);
                writer.Write(compressedToc);
                writer.Write(tocStartOffset);
                writer.Write((long)compressedToc.Length);
            }

            Console.WriteLine($"Archive built. Size: {new FileInfo(outputPath).Length} bytes.");

            if (addCrc)
            {
                AppendCrcData(outputPath);
            }

            Console.WriteLine($"Done! Saved to {outputPath}");
        }

        private byte[] CreateContainer(byte[] inputData, long absoluteStartOffset)
        {
            using (var outMs = new MemoryStream())
            using (var writer = new BinaryWriter(outMs))
            {
                var chunks = new List<(long offset, long length)>();
                long totalCompressedDataSize = 0;

                using (var inputMs = new MemoryStream(inputData))
                {
                    byte[] buffer = new byte[ChunkSize];
                    int bytesRead;
                    while ((bytesRead = inputMs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        byte[] chunkToCompress = new byte[bytesRead];
                        Array.Copy(buffer, chunkToCompress, bytesRead);
                        byte[] compressedChunk = SimpleZlibCompress(chunkToCompress);

                        long relativeChunkOffset = outMs.Position;
                        writer.Write(compressedChunk);
                        chunks.Add((relativeChunkOffset, compressedChunk.Length));
                        totalCompressedDataSize += compressedChunk.Length;
                    }
                }

                long lastChunkUncompressedSize = inputData.Length % ChunkSize;
                if (lastChunkUncompressedSize == 0 && inputData.Length > 0)
                    lastChunkUncompressedSize = ChunkSize;

                byte[] rawMiniToc;
                using (var metaMs = new MemoryStream())
                using (var metaWriter = new BinaryWriter(metaMs))
                {
                    metaWriter.Write(absoluteStartOffset);
                    metaWriter.Write(totalCompressedDataSize);
                    metaWriter.Write((long)inputData.Length);
                    metaWriter.Write((long)ChunkSize);
                    metaWriter.Write(lastChunkUncompressedSize);
                    metaWriter.Write((long)chunks.Count);

                    foreach (var chunk in chunks)
                    {
                        metaWriter.Write(chunk.offset);
                        metaWriter.Write(chunk.length);
                    }

                    rawMiniToc = metaMs.ToArray();
                }

                byte[] compressedMiniToc = SimpleZlibCompress(rawMiniToc);
                long relativeMiniTocOffset = outMs.Position;
                writer.Write(compressedMiniToc);

                long absoluteMiniTocOffset = absoluteStartOffset + relativeMiniTocOffset;
                writer.Write(absoluteMiniTocOffset);
                writer.Write((long)compressedMiniToc.Length);

                return outMs.ToArray();
            }
        }

        private byte[] SimpleZlibCompress(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var zlib = new ZLibStream(ms, CompressionMode.Compress))
                {
                    zlib.Write(data, 0, data.Length);
                }
                return ms.ToArray();
            }
        }

        public void AppendCrcData(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
            {
                if (fs.Length > 16)
                {
                    fs.Seek(-8, SeekOrigin.End);
                    using (var reader = new BinaryReader(fs, Encoding.UTF8, true))
                    {
                        long checkValue = reader.ReadInt64();
                        if (checkValue > 0 && checkValue < 128)
                        {
                            long crcSectionSize = checkValue * 8 + 8;
                            if (crcSectionSize < fs.Length)
                            {
                                Console.WriteLine($"[Packer] Found existing CRC footer ({checkValue} blocks). Overwriting...");
                                fs.SetLength(fs.Length - crcSectionSize);
                            }
                        }
                    }
                }
            }

            Console.WriteLine("[Packer] Calculating CRC64 checksums...");
            List<ulong> crcList = Crc64Utility.CalculateCrcForFile(filePath);

            using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                foreach (var crc in crcList)
                {
                    writer.Write((long)crc);
                }
                writer.Write((long)crcList.Count);
            }

            Console.WriteLine($"[Packer] Appended {crcList.Count} CRC blocks.");
        }
    }
}