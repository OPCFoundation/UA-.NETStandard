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
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Interop;
using Microsoft.Extensions.Logging;
using Opc.Ua.CttTestRunner.Runtime.Settings;
using Opc.Ua.CttTestRunner.Runtime.Types;

namespace Opc.Ua.CttTestRunner.Runtime
{
    /// <summary>
    /// The host environment that provides CTT JavaScript API to the Jint engine.
    /// Manages OPC UA session lifecycle and maps C++ CTT objects to .NET equivalents.
    /// </summary>
    public sealed class CttHostEnvironment : IDisposable
    {
        private readonly ApplicationConfiguration _appConfig;
        private readonly CttProjectSettings _project;
        private readonly string _libraryDir;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly bool _verbose;
        private readonly CancellationToken _ct;
        private readonly HashSet<string> _includedFiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _missingApis = new();

        // Managed UA session for the current test
        private CttUaSession? _session;

        public CttTestContext TestContext { get; }

        public CttHostEnvironment(
            ApplicationConfiguration appConfig,
            CttProjectSettings project,
            string libraryDir,
            ILoggerFactory loggerFactory,
            bool verbose,
            CancellationToken ct)
        {
            _appConfig = appConfig;
            _project = project;
            _libraryDir = libraryDir;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<CttHostEnvironment>();
            _verbose = verbose;
            _ct = ct;
            TestContext = new CttTestContext(loggerFactory.CreateLogger<CttTestContext>());
        }

        public void Dispose()
        {
            _session?.Dispose();

            if (_missingApis.Count > 0)
            {
                _logger.LogWarning("Unimplemented APIs accessed during this test run:");
                foreach (var api in _missingApis.OrderBy(a => a))
                {
                    _logger.LogWarning("  ⚠ {Api}", api);
                }
            }
        }

        /// <summary>
        /// Log an unimplemented API access for telemetry.
        /// </summary>
        public void LogMissingApi(string apiName)
        {
            if (_missingApis.Add(apiName))
            {
                _logger.LogDebug("Unimplemented API: {Api}", apiName);
            }
        }

        /// <summary>
        /// Implements the CTT include() function. Loads and evaluates a JS file.
        /// Paths are resolved relative to the library directory.
        /// </summary>
        public void Include(Engine engine, string path)
        {
            // Normalize path - CTT uses ./library/... relative paths
            string resolved;
            if (path.StartsWith("./", StringComparison.Ordinal))
            {
                resolved = Path.GetFullPath(Path.Combine(_libraryDir, path.Substring(2)));
            }
            else if (Path.IsPathRooted(path))
            {
                resolved = path;
            }
            else
            {
                resolved = Path.GetFullPath(Path.Combine(_libraryDir, path));
            }

            // Deduplicate includes
            if (!_includedFiles.Add(resolved))
            {
                return;
            }

            if (!File.Exists(resolved))
            {
                _logger.LogWarning("Include file not found: {Path}", resolved);
                return;
            }

            if (_verbose)
            {
                _logger.LogDebug("include({Path})", Path.GetRelativePath(_libraryDir, resolved));
            }

            string source = File.ReadAllText(resolved);
            engine.Execute(source, resolved);
        }

        public string ReadSetting(string path)
        {
            return _project.ReadSetting(path);
        }

        public void WriteSetting(string path, string value)
        {
            _project.WriteSetting(path, value);
        }

        public void Print(object? value)
        {
            string text = value?.ToString() ?? "undefined";
            if (_verbose)
            {
                _logger.LogDebug("[JS] {Text}", text);
            }
            TestContext.AddLog(text);
        }

        public void CheckResourceError()
        {
            // CTT checks for resource starvation; we just check cancellation
            _ct.ThrowIfCancellationRequested();
        }

        public void CheckUserStop()
        {
            _ct.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Creates the Test object that CTT scripts use: Test.Execute({ Procedure: func })
        /// </summary>
        public ObjectInstance CreateTestObject(Engine engine)
        {
            var test = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());

            // Test.Session - the shared session object for tests
            var sessionObj = CreateSessionWrapper(engine);
            test.Set("Session", sessionObj);

            // Test.Execute({ Procedure: func })
            test.Set("Execute", new ClrFunction(engine, "Execute",
                (thisObj, args) =>
                {
                    if (args.Length == 0) return JsValue.Undefined;
                    var argsObj = args[0].AsObject();

                    JsValue? procedure = null;
                    if (argsObj.HasProperty("Procedure"))
                    {
                        procedure = argsObj.Get("Procedure");
                    }
                    else if (argsObj.HasProperty("TryFunction"))
                    {
                        procedure = argsObj.Get("TryFunction");
                    }

                    if (procedure == null || !procedure.IsObject())
                    {
                        TestContext.AddError("Test.Execute: No Procedure or TryFunction specified");
                        return JsValue.FromObject(engine, false);
                    }

                    try
                    {
                        var fnArgs = argsObj.HasProperty("Args")
                            ? new[] { argsObj.Get("Args") }
                            : Array.Empty<JsValue>();

                        var result = procedure.AsObject().Call(JsValue.Undefined, fnArgs);

                        if (result.IsBoolean() && !result.AsBoolean())
                        {
                            // Test returned false = failure (unless skipped)
                            if (!TestContext.WasSkipped)
                            {
                                TestContext.AddError("Test procedure returned false");
                            }
                        }

                        return result;
                    }
                    catch (JavaScriptException jsEx)
                    {
                        TestContext.AddError($"JS Exception: {jsEx.Message} at {jsEx.Location}");
                        return JsValue.FromObject(engine, false);
                    }
                    catch (OperationCanceledException)
                    {
                        TestContext.AddSkipped("Test cancelled by user");
                        return JsValue.FromObject(engine, false);
                    }
                    catch (Exception ex)
                    {
                        TestContext.AddError($"Exception: {ex.GetType().Name}: {ex.Message}");
                        return JsValue.FromObject(engine, false);
                    }
                }));

            return test;
        }

        /// <summary>
        /// Creates an Assert object with methods matching CTT assertions.
        /// </summary>
        public ObjectInstance CreateAssertObject()
        {
            return new CttAssert(TestContext, _loggerFactory.CreateLogger("Assert"));
        }

        /// <summary>
        /// Creates the ServiceRegister object (tracks which services were tested).
        /// </summary>
        public ObjectInstance CreateServiceRegister()
        {
            return new CttServiceRegister(_loggerFactory.CreateLogger("ServiceRegister"));
        }

        /// <summary>
        /// Creates the Identifier object with all OPC UA NodeId numeric constants.
        /// </summary>
        public ObjectInstance CreateIdentifierObject()
        {
            return new CttIdentifierConstants();
        }

        /// <summary>
        /// Creates the Settings hierarchy from the project file.
        /// </summary>
        public JsValue CreateSettingsObject(Engine engine)
        {
            return _project.BuildSettingsObject(engine);
        }

        /// <summary>
        /// Creates the MonitoredItem helper with static methods.
        /// </summary>
        public ObjectInstance CreateMonitoredItemHelper(Engine engine)
        {
            return new CttMonitoredItemHelper(engine, _project, _loggerFactory.CreateLogger("MonitoredItem"));
        }

        private ObjectInstance CreateSessionWrapper(Engine engine)
        {
            _session = new CttUaSession(_appConfig, _project, _loggerFactory, _verbose);
            return _session.CreateJsObject(engine);
        }
    }
}


