/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Microsoft.Extensions.Logging;
using Opc.Ua.CttTestRunner.Runtime;
using Opc.Ua.CttTestRunner.Runtime.Settings;
using Opc.Ua.CttTestRunner.Runtime.Types;

namespace Opc.Ua.CttTestRunner
{
    /// <summary>
    /// Runs CTT JavaScript test scripts using a Jint-based host environment.
    /// </summary>
    public sealed class TestRunner
    {
        private readonly ApplicationConfiguration _appConfig;
        private readonly CttProjectSettings _project;
        private readonly string _cttDir;
        private readonly string _libraryDir;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly bool _verbose;

        public TestRunner(
            ApplicationConfiguration appConfig,
            CttProjectSettings project,
            string cttDir,
            ILoggerFactory loggerFactory,
            bool verbose)
        {
            _appConfig = appConfig;
            _project = project;
            _cttDir = cttDir;
            _libraryDir = Path.Combine(cttDir, "ServerProjects", "Standard");
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<TestRunner>();
            _verbose = verbose;
        }

        public Task<TestResultCollection> RunTestsAsync(
            string[] testFiles,
            CancellationToken ct)
        {
            var results = new TestResultCollection();

            foreach (string testFile in testFiles)
            {
                if (ct.IsCancellationRequested) break;

                string testName = Path.GetFileNameWithoutExtension(testFile);
                string relPath = Path.GetRelativePath(_cttDir, testFile);
                _logger.LogInformation("Running: {Test}", relPath);

                var result = RunSingleTest(testFile, ct);
                results.Add(result);

                string status = result.Status switch {
                    TestStatus.Passed => "✅ PASS",
                    TestStatus.Failed => "❌ FAIL",
                    TestStatus.Skipped => "⏭️ SKIP",
                    TestStatus.Error => "💥 ERROR",
                    _ => "?"
                };
                _logger.LogInformation("  {Status} ({Duration:F1}ms)", status, result.Duration.TotalMilliseconds);
                if (result.ErrorMessage != null)
                {
                    _logger.LogInformation("  → {Error}", result.ErrorMessage);
                }
            }

            return Task.FromResult(results);
        }

        private TestResult RunSingleTest(string testFile, CancellationToken ct)
        {
            var result = new TestResult { TestFile = testFile };
            var startTime = DateTime.UtcNow;

            try
            {
                using var host = new CttHostEnvironment(
                    _appConfig, _project, _libraryDir, _loggerFactory, _verbose, ct);

                var engine = CreateEngine(host);

                // Load the test script
                string script = File.ReadAllText(testFile);
                engine.Execute(script, testFile);

                // Collect results from the host
                result.Status = host.TestContext.HasErrors ? TestStatus.Failed
                    : host.TestContext.WasSkipped ? TestStatus.Skipped
                    : TestStatus.Passed;
                result.Logs = host.TestContext.GetLogs();
                result.Warnings = host.TestContext.GetWarnings();
                result.Errors = host.TestContext.GetErrors();
            }
            catch (JavaScriptException jsEx)
            {
                result.Status = TestStatus.Error;
                result.ErrorMessage = $"JS Error at {jsEx.Location}: {jsEx.Message}";
                _logger.LogError("  JS Exception: {Error}", result.ErrorMessage);
            }
            catch (Exception ex)
            {
                result.Status = TestStatus.Error;
                result.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
                _logger.LogError(ex, "  .NET Exception running test");
            }

            result.Duration = DateTime.UtcNow - startTime;
            return result;
        }

        private Engine CreateEngine(CttHostEnvironment host)
        {
            var engine = new Engine(options =>
            {
                options.Strict(false);
                options.AllowClr(typeof(object).Assembly);
                options.CatchClrExceptions();
                options.TimeoutInterval(TimeSpan.FromMinutes(10));
            });

            // Register global functions
            engine.SetValue("include", new Action<string>(path => host.Include(engine, path)));
            engine.SetValue("addLog", new Action<string>(host.TestContext.AddLog));
            engine.SetValue("addError", new Action<string>(host.TestContext.AddError));
            engine.SetValue("addWarning", new Action<string>(host.TestContext.AddWarning));
            engine.SetValue("addSkipped", new Action<string>(host.TestContext.AddSkipped));
            engine.SetValue("addNotSupported", new Action<string>(host.TestContext.AddNotSupported));
            engine.SetValue("readSetting", new Func<string, string>(host.ReadSetting));
            engine.SetValue("writeSetting", new Action<string, string>(host.WriteSetting));
            engine.SetValue("print", new Action<object>(host.Print));
            engine.SetValue("isDefined", new Func<object?, bool>(CttGlobals.IsDefined));
            engine.SetValue("CheckResourceError", new Action(host.CheckResourceError));
            engine.SetValue("CheckUserStop", new Action(host.CheckUserStop));

            // Register the Test object
            engine.SetValue("Test", host.CreateTestObject(engine));

            // Register Assert
            engine.SetValue("Assert", host.CreateAssertObject());

            // Register ServiceRegister
            engine.SetValue("ServiceRegister", host.CreateServiceRegister());

            // Register enumerations
            CttEnumerations.Register(engine);

            // Register Identifier constants (OPC UA NodeId constants)
            engine.SetValue("Identifier", host.CreateIdentifierObject());

            // Register Ua* constructor functions
            CttTypeFactory.RegisterTypes(engine, host);

            // Register Settings object hierarchy
            engine.SetValue("Settings", host.CreateSettingsObject(engine));

            // Register MonitoredItem helper
            engine.SetValue("MonitoredItem", host.CreateMonitoredItemHelper(engine));

            // Register helper objects used by CTT scripts
            engine.SetValue("SETTING_UNDEFINED_SCALARSTATIC",
                "Setting undefined: /Server Test/NodeIds/Static/All Profiles/Scalar");
            engine.SetValue("MAXMONITOREDITEMLIMITS", 100);

            // Register gServerCapabilities (global cache)
            engine.Execute("var gServerCapabilities = { OperationLimits: null, _configured: false };");

            return engine;
        }
    }
}
