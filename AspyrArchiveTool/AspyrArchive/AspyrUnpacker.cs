using System.IO.Compression;
using System.Text;

namespace AspyrArchiveTool.AspyrArchive
{
    public class AspyrUnpacker
    {
        private const int FooterSize = 16;

        public void Unpack(string inputPath, string outputDir)
        {
            Console.WriteLine("[Aspyr Unpacker] Reading archive...");

            using (var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                if (fs.Length < FooterSize) throw new Exception("File too small");

                long tocOffset;
                long tocCompressedSize;

                fs.Seek(-8, SeekOrigin.End);
                long crc32blockcheck = reader.ReadInt64();
                if (crc32blockcheck > 128)
                {
                    fs.Seek(-FooterSize, SeekOrigin.End);
                    tocOffset = reader.ReadInt64();
                    tocCompressedSize = fs.Length - tocOffset - FooterSize;
                }
                else
                {
                    fs.Seek(-8, SeekOrigin.End);
                    long numCrcBlocks = reader.ReadInt64();
                    long crcSectionSize = numCrcBlocks * 8;

                    long footerOffset = fs.Length - 8 - crcSectionSize - FooterSize;
                    fs.Seek(footerOffset, SeekOrigin.Begin);

                    tocOffset = reader.ReadUInt32();
                    tocCompressedSize = fs.Length - tocOffset - FooterSize - crcSectionSize;
                }

                    Console.WriteLine($"TOC Offset: {tocOffset}");

                fs.Seek(tocOffset, SeekOrigin.Begin);
                byte[] compressedToc = reader.ReadBytes((int)tocCompressedSize);
                byte[] tocData = DecompressBytes(compressedToc);

                if (tocData == null) throw new Exception("Failed to decompress TOC.");

                using (var ms = new MemoryStream(tocData))
                using (var tocReader = new BinaryReader(ms))
                {
                    long fileCount = tocReader.ReadInt64();
                    Console.WriteLine($"Files to extract: {fileCount}");

                    for (int i = 0; i < fileCount; i++)
                    {
                        if (ms.Position >= ms.Length) break;

                        long nameLen = tocReader.ReadInt64();
                        byte[] nameBytes = tocReader.ReadBytes((int)nameLen);
                        string name = Encoding.UTF8.GetString(nameBytes);

                        long offset = tocReader.ReadInt64();
                        long size = tocReader.ReadInt64();
                        long zSize = tocReader.ReadInt64();

                        string cleanName = name.Replace("\\", "/");
                        string finalPath = Path.Combine(outputDir, cleanName);

                        string? dirName = Path.GetDirectoryName(finalPath);
                        if (!string.IsNullOrEmpty(dirName)) Directory.CreateDirectory(dirName);

                        long currentPos = fs.Position;

                        if (size == 0)
                        {
                            Directory.CreateDirectory(finalPath);
                        }
                        else if (size == zSize)
                        {
                            ExtractRaw(fs, offset, size, finalPath);
                        }
                        else
                        {
                            ExtractContainer(fs, offset, zSize, finalPath);
                        }

                        fs.Seek(currentPos, SeekOrigin.Begin);

                        string status = $"\r[{i + 1}/{fileCount}] {cleanName}";

                        if (status.Length > Console.WindowWidth - 1)
                            status = status.Substring(0, Console.WindowWidth - 1);

                        Console.Write(status.PadRight(Console.WindowWidth - 1));
                    }
                }
            }
            Console.WriteLine("\nDone!");
        }
        private void ExtractRaw(FileStream fs, long offset, long size, string outputPath)
        {
            fs.Seek(offset, SeekOrigin.Begin);
            byte[] data = new byte[size];
            fs.Read(data, 0, (int)size);
            File.WriteAllBytes(outputPath, data);
        }

        private void ExtractContainer(FileStream fs, long offsetInObb, long sizeInObb, string outputPath)
        {
            fs.Seek(offsetInObb + sizeInObb - 16, SeekOrigin.Begin);

            byte[] footer = new byte[16];
            fs.Read(footer, 0, 16);

            long tocOffsetRaw = BitConverter.ToInt64(footer, 0);
            long tocSizeRaw = BitConverter.ToInt64(footer, 8);

            long finalTocOffset = tocOffsetRaw;

            if (finalTocOffset < offsetInObb)
            {
                finalTocOffset += offsetInObb;
            }

            fs.Seek(finalTocOffset, SeekOrigin.Begin);
            byte[] compressedToc = new byte[tocSizeRaw];
            fs.Read(compressedToc, 0, (int)tocSizeRaw);

            byte[] tocData = DecompressBytes(compressedToc);

            long globalDataStart = BitConverter.ToInt64(tocData, 0);
            long totalCompressedDataSize = BitConverter.ToInt64(tocData, 8);
            long totalUncompressedSize = BitConverter.ToInt64(tocData, 16);
            long numChunks = BitConverter.ToInt64(tocData, 40);

            using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                int ptr = 48;

                for (int i = 0; i < numChunks; i++)
                {
                    long chunkRelOffset = BitConverter.ToInt64(tocData, ptr);
                    long chunkLength = BitConverter.ToInt64(tocData, ptr + 8);

                    ptr += 16;

                    long chunkAbsPosition = globalDataStart + chunkRelOffset;

                    fs.Seek(chunkAbsPosition, SeekOrigin.Begin);
                    byte[] chunkBytes = new byte[chunkLength];
                    fs.Read(chunkBytes, 0, (int)chunkLength);

                    byte[] decompressedChunk = DecompressBytes(chunkBytes);

                    outStream.Write(decompressedChunk, 0, decompressedChunk.Length);
                }
            }
        }

        private byte[] DecompressBytes(byte[] data)
        {
            if (data == null || data.Length == 0) return Array.Empty<byte>();

            if (data.Length > 2 && data[0] == 0x78)
            {
                try
                {
                    using var ms = new MemoryStream(data, 2, data.Length - 2);
                    using var def = new DeflateStream(ms, CompressionMode.Decompress);
                    using var outMs = new MemoryStream();
                    def.CopyTo(outMs);
                    return outMs.ToArray();
                }
                catch { }
            }

            try
            {
                using var ms = new MemoryStream(data);
                using var def = new DeflateStream(ms, CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                def.CopyTo(outMs);
                return outMs.ToArray();
            }
            catch
            {
                return null;
            }
        }
    }
}