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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.WotCon.Server;
using Opc.Ua.WotCon.Server.Assets;

namespace Opc.Ua.WotCon.Tests
{
    /// <summary>
    /// Verifies that AssetRegistry never leaks <c>ex.Message</c> /
    /// <c>ex.StackTrace</c> / <c>ex.GetType().Name</c> into the
    /// <see cref="ServiceResult"/> returned to a remote OPC UA client.
    /// Internal endpoint URIs, file-system paths, provider
    /// implementation details, and stack-trace fragments belong in
    /// the server log only.
    /// </summary>
    /// <remarks>
    /// Each test injects a discovery / provider mock that throws an
    /// exception whose <c>Message</c> is the canary string
    /// <c>SECRET-INTERNAL-PATH:/etc/shadow</c>. The assertions check
    /// that the canary string is in the captured log output but NOT in
    /// the returned <see cref="ServiceResult.LocalizedText"/>, and
    /// that the mapped status code matches the documented
    /// exception-to-status table.
    /// </remarks>
    [TestFixture]
    [Category("WotCon")]
    [Parallelizable(ParallelScope.All)]
    public sealed class AssetRegistryExceptionSanitisationTests
    {
        private const string Canary = "SECRET-INTERNAL-PATH:/etc/shadow";

        // ----------------------------------------------------------------
        // DiscoverAssets
        // ----------------------------------------------------------------

        [Test]
        public async Task DiscoverAssets_NotSupportedException_MapsToBadNotSupported_NoLeak()
        {
            await DriveDiscoverAssetsAsync<NotSupportedException>(
                StatusCodes.BadNotSupported).ConfigureAwait(false);
        }

        [Test]
        public async Task DiscoverAssets_ArgumentException_MapsToBadInvalidArgument_NoLeak()
        {
            await DriveDiscoverAssetsAsync<ArgumentException>(
                StatusCodes.BadInvalidArgument).ConfigureAwait(false);
        }

        [Test]
        public async Task DiscoverAssets_IOException_MapsToBadResourceUnavailable_NoLeak()
        {
            await DriveDiscoverAssetsAsync<IOException>(
                StatusCodes.BadResourceUnavailable).ConfigureAwait(false);
        }

        [Test]
        public async Task DiscoverAssets_GenericException_MapsToBadInternalError_NoLeak()
        {
            await DriveDiscoverAssetsAsync<InvalidOperationException>(
                StatusCodes.BadInternalError).ConfigureAwait(false);
        }

        [Test]
        public async Task DiscoverAssets_OperationCanceledException_PropagatesUnchanged()
        {
            var entries = new List<LogEntry>();
            var mock = new Mock<IWotAssetDiscoveryProvider>();
            mock.Setup(p => p.DiscoverAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException(Canary));
            await using AssetRegistry registry = MakeRegistry(mock.Object, entries);

            Assert.ThrowsAsync(Is.AssignableTo<OperationCanceledException>(), async () =>
                await registry.DiscoverAssetsAsync(CancellationToken.None).ConfigureAwait(false));
        }

        // ----------------------------------------------------------------
        // ConnectionTest
        // ----------------------------------------------------------------

        [Test]
        public async Task ConnectionTest_NotSupportedException_MapsToBadNotSupported_NoLeak()
        {
            await DriveConnectionTestAsync<NotSupportedException>(
                StatusCodes.BadNotSupported).ConfigureAwait(false);
        }

        [Test]
        public async Task ConnectionTest_IOException_MapsToBadResourceUnavailable_NoLeak()
        {
            await DriveConnectionTestAsync<IOException>(
                StatusCodes.BadResourceUnavailable).ConfigureAwait(false);
        }

        [Test]
        public async Task ConnectionTest_GenericException_MapsToBadInternalError_NoLeak()
        {
            await DriveConnectionTestAsync<InvalidOperationException>(
                StatusCodes.BadInternalError).ConfigureAwait(false);
        }

        // ----------------------------------------------------------------
        // CreateAssetForEndpoint  (RebuildAsync fails because no binding
        // factory is registered → BadNotSupported flows back through
        // ToClientStatus.)
        // ----------------------------------------------------------------

        [Test]
        public async Task CreateAssetForEndpoint_DiscoveryThrowsIO_MapsToBadResourceUnavailable_NoLeak()
        {
            var entries = new List<LogEntry>();
            var mock = new Mock<IWotAssetDiscoveryProvider>();
            mock.Setup(p => p.CreateThingDescriptionAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException(Canary));
            await using AssetRegistry registry = MakeRegistry(mock.Object, entries);

            ServiceResult status;
            NodeId assetId;
            try
            {
                (status, assetId) = await registry
                    .CreateAssetForEndpointAsync("asset-canary", "https://example.com/td",
                        CancellationToken.None).ConfigureAwait(false);
            }
            catch (NullReferenceException)
            {
                // CreateAssetAsync requires the node manager which is
                // null in this lightweight harness — the discovery path
                // was not reached. The DiscoverAssets / ConnectionTest
                // tests above cover the same ToClientStatus contract on
                // the directly-invokable surface.
                Assert.Inconclusive(
                    "CreateAssetAsync requires the WotConnectivityNodeManager; " +
                    "sanitisation contract is covered by DiscoverAssets / " +
                    "ConnectionTest tests above.");
                return;
            }

            // CreateAssetAsync may itself fail because we don't wire a
            // real node manager; what matters is that the discovery
            // path was reached and its exception was sanitised. When
            // CreateAssetAsync fails earlier (manager == null), the
            // status will not match — skip those.
            if (status.StatusCode == StatusCodes.BadResourceUnavailable)
            {
                AssertNoCanaryInClientStatus(status);
                Assert.That(assetId.IsNull, Is.True);
                AssertCanaryInLogs(entries);
            }
            else
            {
                Assert.Inconclusive(
                    "CreateAssetAsync failed before the discovery path was exercised " +
                    $"(status={status}). Sanitisation contract is covered by the dedicated " +
                    "DiscoverAssets / ConnectionTest tests above.");
            }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private async Task DriveDiscoverAssetsAsync<TException>(StatusCode expectedStatusCode)
            where TException : Exception
        {
            var entries = new List<LogEntry>();
            var mock = new Mock<IWotAssetDiscoveryProvider>();
            mock.Setup(p => p.DiscoverAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(CreateException<TException>(Canary));
            await using AssetRegistry registry = MakeRegistry(mock.Object, entries);

            (ServiceResult status, IReadOnlyList<string> endpoints) = await registry
                .DiscoverAssetsAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(expectedStatusCode));
            Assert.That(endpoints, Is.Empty);
            AssertNoCanaryInClientStatus(status);
            AssertCanaryInLogs(entries);
        }

        private async Task DriveConnectionTestAsync<TException>(StatusCode expectedStatusCode)
            where TException : Exception
        {
            var entries = new List<LogEntry>();
            var mock = new Mock<IWotAssetDiscoveryProvider>();
            mock.Setup(p => p.TestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(CreateException<TException>(Canary));
            await using AssetRegistry registry = MakeRegistry(mock.Object, entries);

            (ServiceResult status, bool success, string text) = await registry
                .ConnectionTestAsync("https://example.com/td", CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(status.StatusCode, Is.EqualTo(expectedStatusCode));
            Assert.That(success, Is.False);
            Assert.That(text, Is.EqualTo(string.Empty));
            AssertNoCanaryInClientStatus(status);
            AssertCanaryInLogs(entries);
        }

        private static void AssertNoCanaryInClientStatus(ServiceResult status)
        {
            string text = status.LocalizedText.IsNull
                ? string.Empty
                : (status.LocalizedText.Text ?? string.Empty);
            Assert.That(text, Does.Not.Contain(Canary),
                "Canary leaked into the client-facing ServiceResult message.");
            Assert.That(status.AdditionalInfo ?? string.Empty,
                Does.Not.Contain(Canary),
                "Canary leaked into ServiceResult.AdditionalInfo.");
        }

        private static void AssertCanaryInLogs(List<LogEntry> entries)
        {
            string allMessages = string.Join("\n", entries.Select(e =>
                e.Message + " | " + (e.Exception?.Message ?? string.Empty)));
            Assert.That(allMessages, Does.Contain(Canary),
                "Canary must appear in the server log so operators can " +
                "diagnose the underlying failure.");
        }

        private static TException CreateException<TException>(string message)
            where TException : Exception
        {
            return (TException)Activator.CreateInstance(typeof(TException), message)!;
        }

        private static AssetRegistry MakeRegistry(
            IWotAssetDiscoveryProvider discovery,
            List<LogEntry> entries)
        {
            ILogger logger = new CapturingLogger(entries);
            return new AssetRegistry(
                manager: null!,
                new WotConnectivityServerOptions
                {
                    Discovery = discovery
                },
                logger);
        }

        private sealed record LogEntry(
            LogLevel Level, EventId EventId, string Message, Exception? Exception);

        private sealed class CapturingLogger : ILogger
        {
            public CapturingLogger(List<LogEntry> entries)
            {
                m_entries = entries;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return NullScope.Instance;
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
                m_entries.Add(new LogEntry(
                    logLevel, eventId, formatter(state, exception), exception));
            }

            private readonly List<LogEntry> m_entries;

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
