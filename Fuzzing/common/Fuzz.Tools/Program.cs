/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using Mono.Options;

public static class Program
{
    /// <summary>
    /// Relative folder references for testcases and findings,
    /// for when Fuzz.Tools is started from a Visual Studio project.
    /// </summary>
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
