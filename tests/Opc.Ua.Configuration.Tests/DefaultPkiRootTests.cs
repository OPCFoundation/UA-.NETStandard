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

namespace Opc.Ua.Configuration.Tests
{
    [TestFixture]
    [Category("ApplicationConfigurationBuilder")]
    [Parallelizable]
    public sealed class DefaultPkiRootTests
    {
        [Test]
        public void DefaultPkiRootIsPerUserAndNotInTempDirectory()
        {
            string appName = "DefaultPkiRoot-" + Guid.NewGuid().ToString("N");
            string appData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);

            string pkiRoot = DefaultPkiRoot.Get(appName);

            Assert.That(pkiRoot, Is.EqualTo(Path.Combine(appData, "OPC Foundation", appName, "pki")));
            Assert.That(pkiRoot, Does.Not.StartWith(Path.GetTempPath()));
        }

        /// <summary>
        /// Earlier versions defaulted to the temp directory; an upgrade must not
        /// silently replace the certificates stored there.
        /// </summary>
        [Test]
        public void LegacyTempStoresAreReportedWhenTheDefaultRootIsNew()
        {
            string appName = "DefaultPkiRoot-" + Guid.NewGuid().ToString("N");
            string legacyApplicationRoot = Path.Combine(Path.GetTempPath(), "OPC Foundation", appName);
            Directory.CreateDirectory(Path.Combine(legacyApplicationRoot, "pki", "own"));
            try
            {
                var logger = new CapturingLogger();

                string pkiRoot = DefaultPkiRoot.Get(appName, logger);

                Assert.That(logger.Warnings, Has.Count.EqualTo(1));
                Assert.That(logger.Warnings[0], Does.Contain(Path.Combine(legacyApplicationRoot, "pki")));
                Assert.That(logger.Warnings[0], Does.Contain(pkiRoot));

                DefaultPkiRoot.Get("DefaultPkiRoot-" + Guid.NewGuid().ToString("N"), logger);
                Assert.That(logger.Warnings, Has.Count.EqualTo(1), "No legacy stores, no warning.");
            }
            finally
            {
                Directory.Delete(legacyApplicationRoot, recursive: true);
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            public List<string> Warnings { get; } = [];

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    Warnings.Add(formatter(state, exception));
                }
            }
        }
    }
}
