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
using System.CommandLine.Parsing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.CttTestRunner.Runtime.Settings;

namespace Opc.Ua.CttTestRunner
{
    /// <summary>
    /// Entry point for the CTT JavaScript test runner.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var settingsOption = new Option<string>("--settings", "Path to .ctt.xml project file");

            var fileOption = new Option<string>("--file", "Specific .js test script to run");

            var cttDirOption = new Option<string>("--ctt-dir", "CTT installation directory (where library/ and maintree/ are)");

            var resultOption = new Option<string>("--result", "Path for XML result output");

            var conformanceUnitOption = new Option<string>("--conformance-unit", "Run all tests in a conformance unit (e.g. 'Attribute Read')");

            var listOption = new Option<bool>("--list", "List available conformance units without running tests");

            var verboseOption = new Option<bool>("--verbose", "Enable verbose logging of JS\u2194.NET calls");

            var jsonOption = new Option<bool>("--json", "Emit JSON lines to stdout for machine-readable output");

            var rootCommand = new RootCommand("OPC UA CTT JavaScript Test Runner \u2014 executes CTT test scripts using the .NET Standard stack")
            {
                settingsOption,
                fileOption,
                cttDirOption,
                resultOption,
                conformanceUnitOption,
                listOption,
                verboseOption,
                jsonOption
            };

            rootCommand.SetAction(async (parseResult, ct) =>
            {
                string settings = parseResult.GetValue(settingsOption) ?? "";
                string? file = parseResult.GetValue(fileOption);
                string? cttDir = parseResult.GetValue(cttDirOption);
                string? result = parseResult.GetValue(resultOption);
                string? cu = parseResult.GetValue(conformanceUnitOption);
                bool list = parseResult.GetValue(listOption);
                bool verbose = parseResult.GetValue(verboseOption);
                await RunAsync(settings, file, cttDir, result, cu, list, verbose).ConfigureAwait(false);
            });

            return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);
        }

        private static async Task RunAsync(
            string settingsPath,
            string? testFile,
            string? cttDir,
            string? resultPath,
            string? conformanceUnit,
            bool list,
            bool verbose)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                if (verbose)
                {
                    builder.SetMinimumLevel(LogLevel.Debug);
                }
            });
            var logger = loggerFactory.CreateLogger("CttTestRunner");

            // Resolve CTT directory
            cttDir ??= FindCttDirectory();
            if (cttDir == null || !Directory.Exists(cttDir))
            {
                logger.LogError("CTT directory not found. Use --ctt-dir to specify the path.");
                return;
            }

            var maintreeDir = Path.Combine(cttDir, "ServerProjects", "Standard", "maintree");
            if (!Directory.Exists(maintreeDir))
            {
                logger.LogError("CTT maintree not found at {Path}", maintreeDir);
                return;
            }

            // List mode
            if (list)
            {
                ListConformanceUnits(maintreeDir);
                return;
            }

            // Load project settings
            if (!File.Exists(settingsPath))
            {
                logger.LogError("Project file not found: {Path}", settingsPath);
                return;
            }

            logger.LogInformation("Loading project: {Path}", settingsPath);
            var projectSettings = new CttProjectSettings(settingsPath);

            // Initialize OPC UA application
            var telemetry = DefaultTelemetry.Create(b => b.AddConsole());
            var application = new ApplicationInstance(telemetry) {
                ApplicationName = "OPC UA CTT Test Runner",
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = "CttTestRunner"
            };

            string configPath = Path.Combine(
                AppContext.BaseDirectory, "CttTestRunner.Config.xml");
            var config = await application.LoadApplicationConfigurationAsync(configPath, false).ConfigureAwait(false);
            config.CertificateValidator.CertificateValidation += (_, e) =>
            {
                e.Accept = true; // Auto-accept for testing
            };
            await application.CheckApplicationInstanceCertificatesAsync(false).ConfigureAwait(false);

            // Build test list
            string[] testFiles;
            if (!string.IsNullOrEmpty(testFile))
            {
                testFiles = new[] { testFile };
            }
            else if (!string.IsNullOrEmpty(conformanceUnit))
            {
                testFiles = DiscoverTests(maintreeDir, conformanceUnit);
                if (testFiles.Length == 0)
                {
                    logger.LogError("No tests found for conformance unit: {CU}", conformanceUnit);
                    return;
                }
                logger.LogInformation("Found {Count} tests for '{CU}'", testFiles.Length, conformanceUnit);
            }
            else
            {
                logger.LogError("Specify --file or --conformance-unit");
                return;
            }

            // Run tests
            var runner = new TestRunner(config, projectSettings, cttDir, loggerFactory, verbose);
            var results = await runner.RunTestsAsync(testFiles, CancellationToken.None).ConfigureAwait(false);

            // Output results
            results.PrintSummary(logger);
            if (!string.IsNullOrEmpty(resultPath))
            {
                results.WriteXml(resultPath);
                logger.LogInformation("Results written to {Path}", resultPath);
            }
        }

        private static string? FindCttDirectory()
        {
            var candidates = new[]
            {
                @"C:\Program Files\OPC Foundation\UA 1.05\Compliance Test Tool",
                @"C:\Program Files (x86)\OPC Foundation\UA 1.05\Compliance Test Tool",
            };
            foreach (var c in candidates)
            {
                if (Directory.Exists(Path.Combine(c, "ServerProjects", "Standard", "maintree")))
                {
                    return c;
                }
            }
            return null;
        }

        private static void ListConformanceUnits(string maintreeDir)
        {
            Console.WriteLine("Available Conformance Units:");
            Console.WriteLine(new string('=', 60));
            foreach (var catDir in Directory.GetDirectories(maintreeDir))
            {
                string category = Path.GetFileName(catDir);
                Console.WriteLine();
                Console.WriteLine($"  {category}");
                foreach (var cuDir in Directory.GetDirectories(catDir))
                {
                    string cu = Path.GetFileName(cuDir);
                    var testCasesDir = Path.Combine(cuDir, "Test Cases");
                    int count = Directory.Exists(testCasesDir)
                        ? Directory.GetFiles(testCasesDir, "*.js").Length
                        : 0;
                    Console.WriteLine($"    {cu,-50} ({count} tests)");
                }
            }
        }

        private static string[] DiscoverTests(string maintreeDir, string conformanceUnit)
        {
            // Search all category directories for the matching CU
            foreach (var catDir in Directory.GetDirectories(maintreeDir))
            {
                foreach (var cuDir in Directory.GetDirectories(catDir))
                {
                    if (string.Equals(Path.GetFileName(cuDir), conformanceUnit, StringComparison.OrdinalIgnoreCase))
                    {
                        var testCasesDir = Path.Combine(cuDir, "Test Cases");
                        if (Directory.Exists(testCasesDir))
                        {
                            return Directory.GetFiles(testCasesDir, "*.js");
                        }
                    }
                }
            }
            return Array.Empty<string>();
        }
    }
}
