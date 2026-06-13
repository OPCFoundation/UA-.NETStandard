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

#nullable enable

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Stress.Tests.Channels.Fakes;
using Opc.Ua.Stress.Tests.Channels.Helpers;
using Opc.Ua.Stress.Tests.Channels.Integration;
using Opc.Ua.Client;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Stress.Tests.Channels.Chaos
{
    /// <summary>
    /// Layer-3 TCP chaos tests where the server accepts connections but stops responding.
    /// </summary>
    [TestFixture]
    [Category("ChannelManager")]
    [Category("ManagedSession")]
    [Category("Reconnect")]
    [NonParallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class AcceptButStallChaosTests : IntegrationTestBase
    {
        /// <summary>
        /// L3-A4: server accepts TCP but stalls; session keep-alive detects it and triggers reconnect.
        /// </summary>
        [Test]
        [Category("ChaosTCP")]
        [CancelAfter(120_000)]
        public async Task SessionRecoversFromAcceptButStallAsync(CancellationToken ct)
        {
            int seed = TestRunSeed.Get();
            TestContext.Out.WriteLine(FormattableString.Invariant($"L3-A4 seed={seed}"));

            TcpChaosProxy? proxy = null;
            ClientChannelManager? manager = null;
            ManagedSessionType? session = null;

            try
            {
                proxy = await TcpChaosProxy
                    .StartAsync(ServerUrl, telemetry: Telemetry)
                    .ConfigureAwait(false);
                manager = CreateChannelManager(
                    new ExponentialBackoffChannelReconnectPolicy
                    {
                        MinDelay = TimeSpan.FromMilliseconds(500),
                        MaxDelay = TimeSpan.FromSeconds(5),
                        MaxAttempts = 10
                    });
                using MetricsCollector collector = new();

                ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None, proxy.LocalUrl)
                    .ConfigureAwait(false);
                endpoint.EndpointUrl = proxy.LocalUrl;
                endpoint.Description.Server.DiscoveryUrls = [proxy.LocalUrl.ToString()];
                endpoint.UpdateBeforeConnect = false;

                session = await ConnectManagedSessionAsync(endpoint, manager, "L3-A4", ct)
                    .ConfigureAwait(false);
                session.KeepAliveInterval = KeepAliveIntervalMilliseconds;
                ManagedChannelKey key = GetManagedChannel(session).Key;

                await session.ReadValueAsync(VariableIds.Server_ServerStatus_CurrentTime, ct)
                    .ConfigureAwait(false);

                proxy.StallForwarding = true;

                Assert.That(
                    await WaitForConnectionStateAsync(
                        session,
                        ConnectionState.Reconnecting,
                        KeepAliveDetectionTimeout,
                        ct).ConfigureAwait(false),
                    Is.True,
                    $"Session keep-alive should detect stalled TCP and enter reconnecting (seed={seed}).");
                Assert.That(
                    await WaitForChannelStateAsync(
                        manager,
                        key,
                        ChannelState.TransportReconnecting,
                        KeepAliveDetectionTimeout,
                        ct).ConfigureAwait(false),
                    Is.True,
                    $"Channel manager should start transport reconnect after keep-alive failure (seed={seed}).");

                proxy.StallForwarding = false;

                // Production observation: while stalled, the proxy drains and drops the already-sent reconnect
                // handshake. Dropping current sockets forces the next channel-manager attempt onto a clean transport.
                await proxy.DropAllConnectionsAsync().ConfigureAwait(false);

                Assert.That(
                    await WaitForConnectionStateAsync(
                        session,
                        ConnectionState.Connected,
                        RecoveryTimeout,
                        ct).ConfigureAwait(false),
                    Is.True,
                    $"Session should return to connected once forwarding resumes (seed={seed}).");

                await WaitForQuiescence.ForManagerAsync(manager, RecoveryTimeout, ct: ct)
                    .ConfigureAwait(false);
                await session.ReadValueAsync(VariableIds.Server_ServerStatus_CurrentTime, ct)
                    .ConfigureAwait(false);

                Assert.That(
                    collector.CountEvents("ReconnectCompleted"),
                    Is.GreaterThanOrEqualTo(1),
                    $"Expected at least 1 reconnect (seed={seed}).");
            }
            finally
            {
                if (proxy != null)
                {
                    proxy.StallForwarding = false;
                }

                await CloseAndDisposeAsync(session).ConfigureAwait(false);

                if (manager != null)
                {
                    await manager.DisposeAsync().ConfigureAwait(false);
                }

                if (proxy != null)
                {
                    await proxy.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static class TestRunSeed
        {
            public static int Get()
            {
                string? configured = Environment.GetEnvironmentVariable("OPCUA_TEST_SEED");
                if (int.TryParse(
                    configured,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int seed))
                {
                    return seed;
                }

                return Environment.TickCount;
            }
        }

        private static readonly TimeSpan KeepAliveDetectionTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(30);
        private const int KeepAliveIntervalMilliseconds = 2000;
    }
}
