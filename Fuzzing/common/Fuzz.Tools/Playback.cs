
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using static FuzzMethods;

public static class Playback
{
    /// <summary>
    /// Test the libfuzz methods on the files in the directory.
    /// </summary>
    /// <param name="directoryPath">The directory where to find the crash data.</param>
    /// <param name="stackTrace">If the stack trace should be written to output.</param>
    public static void Run(string directoryPath, bool stackTrace)
    {
        string path = Path.GetDirectoryName(directoryPath);
        string searchPattern = Path.GetFileName(directoryPath);
        List<Delegate> libFuzzMethods = FindFuzzMethods(typeof(LibFuzzSpan));

        IEnumerable<string> crashFiles;
        try
        {
            crashFiles = Directory.EnumerateFiles(path, searchPattern);
        }
        catch (Exception)
        {
            Console.WriteLine("Directory not found: {0}", path);
            return;
        }

        foreach (string crashFile in crashFiles)
        {
            Console.WriteLine("### Crash data {0:20} ###", Path.GetFileName(crashFile));
            byte[] crashData = File.ReadAllBytes(crashFile);

            foreach (Delegate method in libFuzzMethods)
            {
                if (method is LibFuzzSpan libFuzzMethod)
                {
                    var stopWatch = new Stopwatch();
                    try
                    {
                        stopWatch.Start();
                        libFuzzMethod(crashData);
                        stopWatch.Stop();
                        Console.WriteLine("Target: {0:30} Elapsed: {1}ms", libFuzzMethod.Method.Name, stopWatch.ElapsedMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        stopWatch.Stop();
                        Console.WriteLine("Target: {0:30} Elapsed: {1}ms", libFuzzMethod.Method.Name, stopWatch.ElapsedMilliseconds);
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
}
