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

#if NET10_0
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Pcap.DependencyInjection;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    [NonParallelizable]
    public sealed class OpcUaSessionManagerBehaviorTests
    {
        [Test]
        public async Task SharedManagerExposesConnectedSessionStateAsync()
        {
            OpcUaSessionManager manager = McpTestEnvironment.SessionManager;
            ApplicationConfiguration first = await manager
                .EnsureConfigurationAsync()
                .ConfigureAwait(false);
            ApplicationConfiguration second = await manager
                .EnsureConfigurationAsync()
                .ConfigureAwait(false);

            Assert.That(first, Is.SameAs(second));
            Assert.That(manager.Configuration, Is.SameAs(first));
            Assert.That(manager.Session, Is.Not.Null);
            Assert.That(manager.IsConnected, Is.True);
            Assert.That(manager.GetSessionOrThrow(), Is.SameAs(manager.Session));
            Assert.That(
                manager.GetSessionOrThrow(McpTestEnvironment.SessionName),
                Is.SameAs(manager.Session));
            Assert.That(
                manager.GetSessionInfo(McpTestEnvironment.SessionName.ToUpperInvariant()),
                Is.Not.Null);
            Assert.That(manager.GetAllSessions(), Has.Count.EqualTo(1));
            Assert.That(manager.GetActiveTransportChannels(), Is.Not.Empty);
            Assert.That(
                manager.GetConnectionStatus(),
                Does.Contain(McpTestEnvironment.SessionName));
        }

        [Test]
        public async Task MultipleSessionsRequireExplicitSessionNameAsync()
        {
            const string secondSessionName = "session-manager-second";
            OpcUaSessionManager manager = McpTestEnvironment.SessionManager;

            try
            {
                _ = await manager.ConnectAsync(
                    secondSessionName,
                    McpTestEnvironment.ServerUrl,
                    "None",
                    "None",
                    "Anonymous",
                    null,
                    null,
                    true,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(
                    () => manager.GetSessionOrThrow(),
                    Throws.TypeOf<InvalidOperationException>()
                        .With.Message.Contain("Multiple sessions active"));
                Assert.That(
                    await manager.DisconnectAsync().ConfigureAwait(false),
                    Does.StartWith("Multiple sessions active"));
                Assert.That(manager.GetAllSessions(), Has.Count.EqualTo(2));
            }
            finally
            {
                _ = await manager.DisconnectAsync(secondSessionName)
                    .ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ConnectAsyncGeneratesNameFromEndpointAsync()
        {
            Uri endpoint = new(McpTestEnvironment.ServerUrl);
            string generatedName = string.Format(
                CultureInfo.InvariantCulture,
                "{0}-{1}",
                endpoint.Host,
                endpoint.Port);
            OpcUaSessionManager manager = McpTestEnvironment.SessionManager;

            try
            {
                string result = await manager.ConnectAsync(
                    null,
                    McpTestEnvironment.ServerUrl,
                    "None",
                    "None",
                    "Anonymous",
                    null,
                    null,
                    true,
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(result, Does.Contain($"as '{generatedName}'"));
                Assert.That(manager.GetSessionInfo(generatedName), Is.Not.Null);
            }
            finally
            {
                _ = await manager.DisconnectAsync(generatedName).ConfigureAwait(false);
            }
        }

        [Test]
        public void InvalidSecurityModeIsRejected()
        {
            Assert.That(
                () => McpTestEnvironment.SessionManager.ConnectAsync(
                    "invalid-security-mode",
                    McpTestEnvironment.ServerUrl,
                    "invalid",
                    "None",
                    "Anonymous",
                    null,
                    null,
                    true,
                    CancellationToken.None),
                Throws.TypeOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("securityMode"));
        }

        [Test]
        public void InvalidAuthenticationTypeIsRejected()
        {
            Assert.That(
                () => McpTestEnvironment.SessionManager.ConnectAsync(
                    "invalid-authentication",
                    McpTestEnvironment.ServerUrl,
                    "None",
                    "None",
                    "invalid",
                    null,
                    null,
                    true,
                    CancellationToken.None),
                Throws.TypeOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("authType"));
        }

        [Test]
        public async Task EmptyManagerReturnsDisconnectedResultsAndDisposesIdempotentlyAsync()
        {
            await using ServiceProvider provider = CreateServiceProvider();
            OpcUaSessionManager manager = provider.GetRequiredService<OpcUaSessionManager>();

            Assert.That(manager.Session, Is.Null);
            Assert.That(manager.Configuration, Is.Null);
            Assert.That(manager.IsConnected, Is.False);
            Assert.That(manager.GetActiveTransportChannels(), Is.Empty);
            Assert.That(manager.GetAllSessions(), Is.Empty);
            Assert.That(manager.GetSessionInfo("missing"), Is.Null);
            Assert.That(manager.GetConnectionStatus(), Is.EqualTo("Not connected."));
            Assert.That(
                manager.GetConnectionStatus("missing"),
                Is.EqualTo("Session 'missing' not found."));
            Assert.That(
                await manager.DisconnectAsync().ConfigureAwait(false),
                Is.EqualTo("No active sessions to disconnect."));
            Assert.That(
                await manager.DisconnectAsync("missing").ConfigureAwait(false),
                Is.EqualTo("Session 'missing' not found."));
            Assert.That(
                () => manager.GetSessionOrThrow(),
                Throws.TypeOf<InvalidOperationException>());

            manager.Dispose();
            Assert.That(() => manager.Dispose(), Throws.Nothing);
            Assert.That(
                () => manager.DiscoverEndpointsAsync(
                    McpTestEnvironment.ServerUrl,
                    CancellationToken.None),
                Throws.TypeOf<ObjectDisposedException>());
        }

        private static ServiceProvider CreateServiceProvider()
        {
            var services = new ServiceCollection();
            McpHostBuilder.ConfigureServices(
                services,
                new PcapOptions());
            return services.BuildServiceProvider();
        }
    }
}
#endif
