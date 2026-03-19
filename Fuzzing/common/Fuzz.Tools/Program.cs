/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using Microsoft.Extensions.Logging;
using System.CommandLine;

namespace Opc.Ua.Fuzzing
{
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
            string applicationName = typeof(Program).Assembly.GetName().Name;

            Console.WriteLine($"OPC UA {applicationName}");

            var playbackOption = new Option<bool>("--playback", "-p") { Description = "playback crashes found by afl-fuzz and libfuzzer" };
            var testcasesOption = new Option<bool>("--testcases", "-t") { Description = "create test cases for fuzzing" };
            var stacktraceOption = new Option<bool>("--stacktrace", "-s") { Description = "show stacktrace with playback" };

            var rootCommand = new RootCommand($"Usage: {applicationName}.exe [OPTIONS]")
            {
                playbackOption,
                testcasesOption,
                stacktraceOption
            };

            rootCommand.SetAction((parseResult) =>
            {
                bool playback = parseResult.GetValue(playbackOption);
                bool testcases = parseResult.GetValue(testcasesOption);
                bool stacktrace = parseResult.GetValue(stacktraceOption);

                var telemetry = new Logging();
                telemetry.Configure(applicationName, string.Empty, true, LogLevel.Trace);

                // TODO: this loads opc ua assembly, but this should not be needed.
                // but otherwise encoderfactory currently does not get all types.
                var temp = new AlarmConditionState(null);

                if (testcases)
                {
                    Testcases.Run(DefaultTestcasesFolder, telemetry);
                }
                else if (playback)
                {
                    foreach (string encoderType in Testcases.TestcaseEncoderSuffixes)
                    {
                        Console.WriteLine("--- Fuzzer testcases for {0} ---", encoderType[1..]);
                        Playback.Run(
                            DefaultTestcasesFolder + encoderType + Path.DirectorySeparatorChar,
                            stacktrace,
                            telemetry);
                    }
                    Console.WriteLine("--- afl-fuzz crash findings ---");
                    Playback.Run(DefaultFindingsCrashFolder, stacktrace, telemetry);
                    Console.WriteLine("--- afl-fuzz timeout findings ---");
                    Playback.Run(DefaultFindingsHangsFolder, stacktrace, telemetry);
                    Console.WriteLine("--- libfuzzer crashes ---");
                    Playback.Run(DefaultLibFuzzerCrashes, stacktrace, telemetry);
                    Console.WriteLine("--- libfuzzer timeouts ---");
                    Playback.Run(DefaultLibFuzzerHangs, stacktrace, telemetry);
                }
                else
                {
                    Console.WriteLine("No action specified. Use --help for usage information.");
                }
            });

            ParseResult parseResult = rootCommand.Parse(args);
            parseResult.Invoke(new InvocationConfiguration());
        }
    }
}
