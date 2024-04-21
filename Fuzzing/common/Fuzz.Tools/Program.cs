
using System.IO;
using System;
using Mono.Options;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

public static class Program
{
    public static readonly string DefaultTestcasesFolder = "../../../../Fuzz/Testcases";
    public static readonly string DefaultFindingsCrashFolder = "../../../../findings/crashes/";
    public static readonly string DefaultFindingsHangsFolder = "../../../../findings/hangs/";
    public static readonly string DefaultLibFuzzerCrashes = "../../../crash-*";
    public static readonly string DefaultLibFuzzerHangs = "../../../timeout-*";

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
            Playback.Run(DefaultFindingsCrashFolder, stacktrace);
            Playback.Run(DefaultFindingsHangsFolder, stacktrace);
            Playback.Run(DefaultLibFuzzerCrashes, stacktrace);
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
