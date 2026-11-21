// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            string? target)
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
                string parent = Path.GetDirectoryName(directoryPath)!;
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
