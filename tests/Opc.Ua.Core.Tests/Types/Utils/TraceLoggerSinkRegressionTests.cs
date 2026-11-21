// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    /// <summary>
    /// Verifies generated log delivery and formatting respect independently configured trace sinks and masks.
    /// </summary>
    [TestFixture]
    [Category("Utils")]
    [NonParallelizable]
    public sealed partial class TraceLoggerSinkRegressionTests
    {
        /// <summary>
        /// Verifies generated messages reach each enabled file or event sink without bypassing its configured mask.
        /// </summary>
        [TestCase(true, false, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, true)]
        [TestCase(false, false, true)]
        [TestCase(false, false, false)]
        public void GeneratedLogsReachEachConfiguredSink(bool file, bool events, bool enabledMask)
        {
            string path = Path.Combine(Path.GetTempPath(), "opcua-trace-" + Guid.NewGuid().ToString("N") + ".log");
            using var provider = new TraceLoggerProvider();
            provider.SetTraceOutput(file ? Utils.TraceOutput.FileOnly : Utils.TraceOutput.Off);
            provider.SetTraceMask(enabledMask ? Utils.TraceMasks.Information : Utils.TraceMasks.None);
            if (file)
            {
                provider.SetTraceLog(path, deleteExisting: true);
            }
            var received = new List<string>();
            void OnTrace(object? sender, TraceEventArgs args)
            {
                received.Add(args.Format);
            }
            if (events)
            {
                provider.Tracing.TraceEventHandler += OnTrace;
            }
            try
            {
                ILogger logger = provider.CreateLogger("regression");
                Assert.That(logger.IsEnabled(LogLevel.Information), Is.EqualTo(events || (file && enabledMask)));
                LogRegression(logger, 739);
                Assert.That(received, Has.Count.EqualTo(events ? 1 : 0));
                if (events)
                {
                    Assert.That(received[0], Does.Contain("trace regression 739"));
                }
                if (file)
                {
                    string text = File.ReadAllText(path);
                    Assert.That(text.Contains("trace regression 739", StringComparison.Ordinal), Is.EqualTo(enabledMask));
                }
            }
            finally
            {
                if (events)
                {
                    provider.Tracing.TraceEventHandler -= OnTrace;
                }
                provider.SetTraceLog(string.Empty, deleteExisting: false);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        /// <summary>
        /// Verifies disabled messages are rejected before invoking their formatter.
        /// </summary>
        [Test]
        public void DisabledMaskDoesNotFormatRejectedMessages()
        {
            using var provider = new TraceLoggerProvider();
            provider.SetTraceOutput(Utils.TraceOutput.Off);
            provider.SetTraceMask(Utils.TraceMasks.None);
            int formatted = 0;
            provider.Log(1, null, Utils.TraceMasks.Information, (state, exception) =>
            {
                formatted++;
                return "must not be formatted";
            });
            Assert.That(formatted, Is.Zero);
        }

        /// <summary>
        /// Emits the generated information message used to verify both trace sinks.
        /// </summary>
        [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "trace regression {Value}")]
        private static partial void LogRegression(ILogger logger, int value);
    }
}
