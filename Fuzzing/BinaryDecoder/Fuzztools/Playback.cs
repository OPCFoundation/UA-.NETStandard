
using System.IO;
using System;
using BinaryDecoder.Fuzz;

namespace BinaryDecoder.Fuzztools
{
    public static class Playback
    {
        public static void Run(string directoryPath)
        {
            foreach (var crashFile in Directory.EnumerateFiles(directoryPath))
            {
                using (var stream = new MemoryStream(File.ReadAllBytes(crashFile)))
                {
                    try
                    {
                        FuzzableCode.FuzzTarget(stream);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("{0}:{1}", ex.GetType().Name, ex.Message);
                    }
                }
            };
        }
    }
}
