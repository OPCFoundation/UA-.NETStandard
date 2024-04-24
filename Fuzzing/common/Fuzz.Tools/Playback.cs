
using System.IO;
using System;
using System.Text;
using System.Diagnostics;

public static class Playback
{
    public static void Run(string directoryPath, bool stackTrace)
    {
        var path = Path.GetDirectoryName(directoryPath);
        var searchPattern = Path.GetFileName(directoryPath);
        foreach (var crashFile in Directory.EnumerateFiles(path, searchPattern))
        {
            var stopWatch = new Stopwatch();
#if TEXTFUZZER
            var crashData = Encoding.UTF8.GetString(File.ReadAllBytes(crashFile));
#else
            using (var crashData = new FileStream(crashFile, FileMode.Open, FileAccess.Read))
#endif
            {
                try
                {
                    stopWatch.Start();
                    FuzzableCode.FuzzTarget(crashData);
                    stopWatch.Stop();
                    Console.WriteLine("File: {0:20} Elapsed: {1}ms", Path.GetFileName(crashFile), stopWatch.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    stopWatch.Stop();
                    Console.WriteLine("File: {0:20} Elapsed: {1}ms", Path.GetFileName(crashFile), stopWatch.ElapsedMilliseconds);
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
