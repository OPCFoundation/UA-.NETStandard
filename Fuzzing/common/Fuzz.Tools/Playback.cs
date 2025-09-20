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
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using static Opc.Ua.Fuzzing.FuzzMethods;

namespace Opc.Ua.Fuzzing
{
    public static class Playback
    {
        /// <summary>
        /// Test the libfuzz methods on the files in the directory.
        /// </summary>
        /// <param name="directoryPath">The directory where to find the crash data.</param>
        /// <param name="stackTrace">If the stack trace should be written to output.</param>
        /// <param name="telemetry">The telemetry context to use to create obvservability instruments</param>
        public static void Run(string directoryPath, bool stackTrace, ITelemetryContext telemetry)
        {
            string path = Path.GetDirectoryName(directoryPath);
            string searchPattern = Path.GetFileName(directoryPath);
            List<Delegate> libFuzzMethods = FindFuzzMethods(typeof(LibFuzzSpan));
            ILogger logger = telemetry.CreateLogger("Opc.Ua.Fuzzing.Playback");

            IEnumerable<string> crashFiles;
            try
            {
                crashFiles = Directory.EnumerateFiles(path, searchPattern);
            }
            catch (Exception e)
            {
                logger.LogInformation(e, "Directory not found: {FilePath}", path);
                return;
            }

            foreach (string crashFile in crashFiles)
            {
                logger.LogInformation("### Crash data {FilePath:20} ###", Path.GetFileName(crashFile));
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
                            logger.LogInformation(
                                "Target: {Name:30} Elapsed: {Elapsed}ms",
                                libFuzzMethod.Method.Name,
                                stopWatch.ElapsedMilliseconds
                            );
                        }
                        catch (Exception ex)
                        {
                            stopWatch.Stop();
                            logger.LogInformation(
                                "Target: {Name:30} Elapsed: {Elapsed}ms",
                                libFuzzMethod.Method.Name,
                                stopWatch.ElapsedMilliseconds
                            );
                            if (stackTrace)
                            {
                                logger.LogInformation(ex, "{Name}", ex.GetType().Name);
                            }
                            else
                            {
                                logger.LogInformation("{Name}:{ErrorMessage}", ex.GetType().Name, ex.Message);
                            }
                        }
                    }
                }
            }
        }
    }
}
