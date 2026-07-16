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
using System.IO;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.UtilsTests
{
    [TestFixture]
    [Category("Utils")]
    [Parallelizable]
    public sealed class TraceLoggerProviderTests
    {
        [Test]
        public void CreateLoggerCachesByCategory()
        {
            using var provider = new TraceLoggerProvider();

            ILogger first = provider.CreateLogger("category");
            ILogger second = provider.CreateLogger("category");
            ILogger third = provider.CreateLogger("other");

            Assert.That(first, Is.SameAs(second));
            Assert.That(third, Is.Not.SameAs(first));
        }

        [Test]
        public void SetTraceLogWritesAndCanBeDisabled()
        {
            string directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "TraceLoggerProviderTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string logFile = Path.Combine(directory, "trace.log");
            using var provider = new TraceLoggerProvider();
            provider.SetTraceMask(Utils.TraceMasks.Information | Utils.TraceMasks.Error);
            provider.SetTraceOutput(Utils.TraceOutput.FileOnly);

            provider.SetTraceLog(logFile, deleteExisting: true);
            provider.TraceWriteLine("Hello {0}", "trace");
            provider.SetTraceLog(string.Empty, deleteExisting: false);
            provider.TraceWriteLine("Not written");

            string text = File.ReadAllText(logFile);
            Assert.That(text, Does.Contain("Logging started"));
            Assert.That(text, Does.Contain("Hello trace"));
            Assert.That(text, Does.Not.Contain("Not written"));
            Directory.Delete(directory, true);
        }

        [Test]
        public void TraceExceptionMessageIncludesServiceResultExceptionAndStackTraceWhenEnabled()
        {
            using var provider = new TraceLoggerProvider();
            provider.SetTraceMask(Utils.TraceMasks.StackTrace);
            var exception = new ServiceResultException(StatusCodes.BadIdentityTokenRejected, "rejected");

            string message = provider
                .TraceExceptionMessage(exception, "prefix {0}", 42)
                .ToString();

            Assert.That(message, Does.Contain("prefix 42"));
            Assert.That(message, Does.Contain("BadIdentityTokenRejected"));
            Assert.That(message, Does.Contain("rejected"));
        }

        [Test]
        public void LoggerLogWritesFormattedExceptionWhenMaskIsEnabled()
        {
            string directory = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "TraceLoggerProviderTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string logFile = Path.Combine(directory, "trace.log");
            using var provider = new TraceLoggerProvider();
            provider.SetTraceOutput(Utils.TraceOutput.FileOnly);
            provider.SetTraceMask(Utils.TraceMasks.Error);
            provider.SetTraceLog(logFile, deleteExisting: true);
            ILogger logger = provider.CreateLogger("category");

            logger.LogError(new InvalidOperationException("boom"), "Failure {Value}", 17);

            string text = File.ReadAllText(logFile);
            Assert.That(text, Does.Contain("Failure 17"));
            Assert.That(text, Does.Contain("InvalidOperationException"));
            Assert.That(text, Does.Contain("boom"));
            Directory.Delete(directory, true);
        }
    }
}
