/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.CttTestRunner.Runtime
{
    /// <summary>
    /// Tracks test state: logs, warnings, errors, pass/fail/skip status.
    /// </summary>
    public sealed class CttTestContext
    {
        private readonly ILogger _logger;
        private readonly List<string> _logs = new();
        private readonly List<string> _warnings = new();
        private readonly List<string> _errors = new();

        public bool HasErrors => _errors.Count > 0;
        public bool WasSkipped { get; private set; }

        public CttTestContext(ILogger logger)
        {
            _logger = logger;
        }

        public void AddLog(string message)
        {
            _logs.Add(message);
            _logger.LogDebug("[LOG] {Message}", message);
        }

        public void AddWarning(string message)
        {
            _warnings.Add(message);
            _logger.LogWarning("[WARN] {Message}", message);
        }

        public void AddError(string message)
        {
            _errors.Add(message);
            _logger.LogError("[ERROR] {Message}", message);
        }

        public void AddSkipped(string message)
        {
            WasSkipped = true;
            _logs.Add($"[SKIPPED] {message}");
            _logger.LogInformation("[SKIP] {Message}", message);
        }

        public void AddNotSupported(string message)
        {
            _logs.Add($"[NOT SUPPORTED] {message}");
            _logger.LogInformation("[NOT SUPPORTED] {Message}", message);
        }

        public List<string> GetLogs() => new(_logs);
        public List<string> GetWarnings() => new(_warnings);
        public List<string> GetErrors() => new(_errors);
    }
}
