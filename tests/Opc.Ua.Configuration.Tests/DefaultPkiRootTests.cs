// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
                var capturing = new CapturingLogger();
                // The paths are classified log parameters: log through a redaction-aware factory.
                using ILoggerFactory factory = LoggerFactory.Create(builder => builder
                    .EnableOpcUaRedaction()
                    .AddProvider(capturing));
                ILogger logger = factory.CreateLogger(nameof(DefaultPkiRootTests));

                string pkiRoot = DefaultPkiRoot.Get(appName, logger);

                Assert.That(capturing.Warnings, Has.Count.EqualTo(1));
                Assert.That(capturing.Warnings[0], Does.Contain(Path.Combine(legacyApplicationRoot, "pki")));
                Assert.That(capturing.Warnings[0], Does.Contain(pkiRoot));

                DefaultPkiRoot.Get("DefaultPkiRoot-" + Guid.NewGuid().ToString("N"), logger);
                Assert.That(capturing.Warnings, Has.Count.EqualTo(1), "No legacy stores, no warning.");
            }
            finally
            {
                Directory.Delete(legacyApplicationRoot, recursive: true);
            }
        }

        private sealed class CapturingLogger : ILogger, ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName)
            {
                return this;
            }

            public void Dispose()
            {
            }

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
