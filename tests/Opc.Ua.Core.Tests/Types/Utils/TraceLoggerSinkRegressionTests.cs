/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    [TestFixture]
    [Category("Utils")]
    [NonParallelizable]
    public sealed partial class TraceLoggerSinkRegressionTests
    {
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
            void OnTrace(object sender, TraceEventArgs args)
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

        [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "trace regression {Value}")]
        private static partial void LogRegression(ILogger logger, int value);
    }
}
