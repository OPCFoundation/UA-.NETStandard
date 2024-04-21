
using System.IO;
using System;
using System.Text;

public static class Playback
{
    public static void Run(string directoryPath, bool stackTrace)
    {
        var path = Path.GetDirectoryName(directoryPath);
        var searchPattern = Path.GetFileName(directoryPath);
        foreach (var crashFile in Directory.EnumerateFiles(path, searchPattern))
        {
#if TEXTFUZZER
            var crashData = Encoding.UTF8.GetString(File.ReadAllBytes(crashFile));
            {
                try
                {
                    FuzzableCode.FuzzTarget(crashData);
                }
#else
            using (var crashStream = new FileStream(crashFile, FileMode.Open, FileAccess.Read))
            {
                try
                {
                    FuzzableCode.FuzzTarget(crashStream);
                }
#endif
                catch (Exception ex)
                {
                    Console.WriteLine("{0}:{1}", ex.GetType().Name, ex.Message);
                    if (stackTrace)
                    {
                        Console.WriteLine(ex.StackTrace);
                    }
                }
            }
        }
    }
}
