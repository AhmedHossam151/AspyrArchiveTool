using AspyrArchiveTool.AspyrArchive;

namespace AspyrArchiveTool
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==============================================");
            Console.WriteLine(" Aspyr Archive Tool v1.0 by Dhampir ");
            Console.WriteLine(" Supports: KOTOR 2, Jade Empire, Fahrenheit ");
            Console.WriteLine("==============================================");
            Console.ResetColor();

            if (args.Length == 0)
            {
                PrintUsage();
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
                return;
            }

            string mode = null;
            bool useCompression = false;
            bool addCrc = false;
            string selectiveListPath = null;
            List<string> paths = new List<string>();

            int i = 0;
            while (i < args.Length)
            {
                string arg = args[i];

                if (arg.StartsWith("-", StringComparison.OrdinalIgnoreCase))
                {
                    string lower = arg.ToLowerInvariant();

                    if (lower == "-u")
                        mode = "unpack";
                    else if (lower == "-p")
                        mode = "pack";
                    else if (lower == "-crc")
                        addCrc = true;
                    else if (lower == "-c")
                    {
                        useCompression = true;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.OrdinalIgnoreCase))
                        {
                            selectiveListPath = args[i + 1];
                            i++;
                        }
                    }
                }
                else
                {
                    paths.Add(arg);
                }

                i++;
            }

            if (mode == null && paths.Count > 0)
            {
                if (Directory.Exists(paths[0]))
                    mode = "pack";
                else
                    mode = "unpack";
            }

            if (mode == "unpack" && addCrc && paths.Count > 0 && File.Exists(paths[0]))
                mode = "patch_crc";

            if (paths.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: No input file or directory specified.");
                Console.ResetColor();
                return;
            }

            string inputPath = paths[0];
            string outputPath = paths.Count > 1 ? paths[1] : null;

            Console.WriteLine($"Mode: {mode.ToUpper()}");
            Console.WriteLine($"Input: {inputPath}");

            if (mode == "pack")
            {
                string compMode = useCompression
                    ? (selectiveListPath != null ? "SELECTIVE" : "ENABLED (smart)")
                    : "DISABLED";
                Console.WriteLine($"Compression: {compMode}");
                if (selectiveListPath != null)
                    Console.WriteLine($"Selective list: {Path.GetFileName(selectiveListPath)}");
                Console.WriteLine($"CRC Calculation: {(addCrc ? "ENABLED" : "DISABLED")}");
            }

            try
            {
                if (mode == "unpack")
                {
                    var unpacker = new AspyrUnpacker();
                    if (outputPath == null)
                    {
                        string dir = Path.GetDirectoryName(inputPath) ?? ".";
                        string name = Path.GetFileNameWithoutExtension(inputPath);
                        outputPath = Path.Combine(dir, name + "_extracted");
                    }
                    Console.WriteLine($"Output Directory: {outputPath}");
                    unpacker.Unpack(inputPath, outputPath);
                }
                else if (mode == "pack")
                {
                    var packer = new AspyrPacker();
                    if (outputPath == null)
                    {
                        string cleanInput = inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        outputPath = cleanInput + ".obb";
                    }
                    Console.WriteLine($"Output File: {outputPath}");
                    packer.Pack(inputPath, outputPath, useCompression, addCrc, selectiveListPath);
                }
                else if (mode == "patch_crc")
                {
                    Console.WriteLine("Patching existing file with CRC data...");
                    var packer = new AspyrPacker();
                    packer.AppendCrcData(inputPath);
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nCRITICAL ERROR: {ex.Message}");
                Console.ResetColor();
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine("\nOperation completed.");
#if DEBUG
            Console.ReadKey();
#endif
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage: AspyrArchiveTool.exe [flags] <input> [output]");
            Console.WriteLine("\nFlags:");
            Console.WriteLine(" -u                  : Unpack");
            Console.WriteLine(" -p                  : Pack");
            Console.WriteLine(" -c                  : Compress all files");
            Console.WriteLine(" -c list.txt         : Selective compression (list only)");
            Console.WriteLine(" -crc                : Add CRC");
            Console.WriteLine("\nExamples:");
            Console.WriteLine("   AspyrArchiveTool.exe -p folder                  → pack without compression");
            Console.WriteLine("   AspyrArchiveTool.exe -p folder -c               → pack + compress all files");
            Console.WriteLine("   AspyrArchiveTool.exe -p -c list.txt folder      → selective");
            Console.WriteLine("   AspyrArchiveTool.exe -u archive.obb             → unpack");
        }
    }
}