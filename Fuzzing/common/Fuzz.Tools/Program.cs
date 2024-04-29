
using System.IO;
using System;
using Mono.Options;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

public static class Program
{
    public static readonly string RootFolder = "../../../../";
    public static readonly string DefaultTestcasesFolder = RootFolder + "Fuzz/Testcases";
    public static readonly string DefaultFindingsCrashFolder = RootFolder + "findings/crashes/";
    public static readonly string DefaultFindingsHangsFolder = RootFolder + "findings/hangs/";
    public static readonly string DefaultLibFuzzerCrashes = RootFolder + "crash-*";
    public static readonly string DefaultLibFuzzerHangs = RootFolder + "timeout-*";

    public static void Main(string[] args)
    {
        var applicationName = typeof(Program).Assembly.GetName().Name;
        TextWriter output = Console.Out;

        output.WriteLine($"OPC UA {applicationName}");
        var usage = $"Usage: {applicationName}.exe [OPTIONS]";

        bool showHelp = false;
        bool playback = false;
        bool testcases = false;
        bool stacktrace = false;

        OptionSet options = new OptionSet {
                usage,
                { "h|help", "show this message and exit", h => showHelp = h != null },
                { "p|playback", "playback crashes found by afl-fuzz and libfuzzer", p => playback = p != null },
                { "t|testcases", "create test cases for fuzzing", t => testcases = t != null },
                { "s|stacktrace", "show stacktrace with playback", s => stacktrace = s != null },
            };

        Logging.Configure(applicationName, string.Empty, true, LogLevel.Trace);

        IList<string> extraArgs = null;
        try
        {
            extraArgs = options.Parse(args);
        }
        catch (OptionException e)
        {
            output.WriteLine(e.Message);
            showHelp = true;
        }

        if (testcases)
        {
            Testcases.Run(DefaultTestcasesFolder);
        }
        else if (playback)
        {
            foreach (var encoderType in Testcases.TestcaseEncoderSuffixes)
            {
                Console.WriteLine("--- Fuzzer testcases for {0} ---", encoderType.Substring(1));
                Playback.Run(DefaultTestcasesFolder + encoderType + Path.DirectorySeparatorChar, stacktrace);
            }
            Console.WriteLine("--- afl-fuzz crash findings ---");
            Playback.Run(DefaultFindingsCrashFolder, stacktrace);
            Console.WriteLine("--- afl-fuzz timeout findings ---");
            Playback.Run(DefaultFindingsHangsFolder, stacktrace);
            Console.WriteLine("--- libfuzzer crashes ---");
            Playback.Run(DefaultLibFuzzerCrashes, stacktrace);
            Console.WriteLine("--- libfuzzer timeouts ---");
            Playback.Run(DefaultLibFuzzerHangs, stacktrace);
        }
        else
        {
            showHelp = true;
        }

        if (showHelp)
        {
            options.WriteOptionDescriptions(output);
        }
    }
}
