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
using System.CommandLine;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Fuzzing
{
    public static class Program
    {
        /// <summary>
        /// Default artifact root, independent of the caller's working directory.
        /// </summary>
        public static readonly string RootFolder = AppContext.BaseDirectory;

        public static readonly string DefaultTestcasesFolder =
            Path.Combine(RootFolder, "Testcases");

        public static readonly string DefaultFindingsCrashFolder = RootFolder + "findings/crashes/";
        public static readonly string DefaultFindingsHangsFolder = RootFolder + "findings/hangs/";
        public static readonly string DefaultLibFuzzerCrashes = RootFolder + "crash-*";
        public static readonly string DefaultLibFuzzerHangs = RootFolder + "timeout-*";

        public static int Main(string[] args)
        {
            string applicationName = typeof(Program).Assembly.GetName().Name;

            Console.WriteLine($"OPC UA {applicationName}");

            var playbackOption = new Option<bool>("--playback", "-p")
            {
                Description = "playback crashes found by afl-fuzz and libfuzzer"
            };
            var testcasesOption = new Option<bool>("--testcases", "-t")
            {
                Description = "create test cases for fuzzing"
            };
            var stacktraceOption = new Option<bool>("--stacktrace", "-s")
            {
                Description = "show stacktrace with playback"
            };
            var outputOption = new Option<string>("--output")
            {
                Description = "Testcases path prefix for generation"
            };
            var inputOption = new Option<string>("--input")
            {
                Description = "Required replay directory, file or glob"
            };
            var targetOption = new Option<string>("--target")
            {
                Description = "Replay only this named target"
            };

            var rootCommand = new RootCommand($"Usage: {applicationName}.exe [OPTIONS]")
            {
                playbackOption,
                testcasesOption,
                stacktraceOption,
                outputOption,
                inputOption,
                targetOption
            };

            rootCommand.SetAction((parseResult) =>
            {
                bool playback = parseResult.GetValue(playbackOption);
                bool testcases = parseResult.GetValue(testcasesOption);
                bool stacktrace = parseResult.GetValue(stacktraceOption);
                if (playback == testcases)
                {
                    Console.Error.WriteLine("Specify exactly one of --testcases or --playback.");
                    return 2;
                }
                if (playback && string.IsNullOrEmpty(parseResult.GetValue(inputOption)))
                {
                    Console.Error.WriteLine("--playback requires --input.");
                    return 2;
                }
                if ((playback && parseResult.GetValue(outputOption) != null) ||
                    (testcases && (parseResult.GetValue(inputOption) != null ||
                        parseResult.GetValue(targetOption) != null)))
                {
                    Console.Error.WriteLine("--output is for generation; --input and --target are for playback.");
                    return 2;
                }

                var telemetry = new Logging();
                telemetry.Configure(applicationName, string.Empty, true, LogLevel.Trace);

                if (testcases)
                {
                    Testcases.Run(
                        Path.GetFullPath(parseResult.GetValue(outputOption) ?? DefaultTestcasesFolder),
                        telemetry);
                }
                else
                {
                    Playback.Run(
                        parseResult.GetValue(inputOption),
                        stacktrace,
                        telemetry,
                        parseResult.GetValue(targetOption));
                }
                return 0;
            });

            ParseResult parseResult = rootCommand.Parse(args);
            return parseResult.Invoke(new InvocationConfiguration());
        }
    }
}
