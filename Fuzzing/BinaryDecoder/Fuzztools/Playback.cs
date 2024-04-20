
using System.IO;
using System;

public static class Playback
{
    public static void Run(string directoryPath, bool stackTrace)
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
                    if (stackTrace)
                    {
                        Console.WriteLine(ex.StackTrace);
                    }
                }
            }
        };
    }
}
