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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            Run(directoryPath, stackTrace, telemetry, null);
        }

        /// <summary>
        /// Replays a required input set, optionally against a single named target.
        /// </summary>
        public static void Run(
            string directoryPath,
            bool stackTrace,
            ITelemetryContext telemetry,
            string target)
        {
            _ = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            string fullPath;
            string[] files;
            if (Directory.Exists(directoryPath))
            {
                fullPath = Path.GetFullPath(directoryPath);
                files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);
            }
            else
            {
                // .NET Framework rejects wildcards in GetFullPath, so normalize only the parent.
                string parent = Path.GetDirectoryName(directoryPath);
                parent = Path.GetFullPath(string.IsNullOrEmpty(parent) ? "." : parent);
                string pattern = Path.GetFileName(directoryPath);
                fullPath = Path.Combine(parent, pattern);
                files = Directory.GetFiles(parent, pattern);
            }
            if (files.Length == 0)
            {
                throw new InvalidOperationException($"Replay input contains no files: {fullPath}");
            }
            List<Delegate> methods = string.IsNullOrEmpty(target)
                ? FindFuzzMethods(typeof(LibFuzzSpan))
                : [FindFuzzMethod(Console.Error, target)
                    ?? throw new ArgumentException($"Unknown fuzz target: {target}", nameof(target))];
            if (methods.Count == 0)
            {
                throw new InvalidOperationException("No supported fuzz targets were discovered.");
            }
            var failures = new List<Exception>();
            foreach (string file in files.OrderBy(file => file, StringComparer.Ordinal))
            {
                byte[] data = File.ReadAllBytes(file);
                foreach (Delegate method in methods)
                {
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        Replay(method, data);
                    }
                    catch (Exception exception)
                    {
                        // Continue collecting diagnostics, then fail the entire replay.
                        failures.Add(new InvalidOperationException(
                            $"Target {method.Method.Name}, input {file}", exception));
                        Console.Error.WriteLine(stackTrace ? exception.ToString() : exception.Message);
                    }
                    Console.WriteLine($"{method.Method.Name}: {file} ({stopwatch.ElapsedMilliseconds} ms)");
                }
            }
            if (failures.Count != 0)
            {
                throw new AggregateException("One or more fuzz inputs failed replay.", failures);
            }
        }
    }
}
