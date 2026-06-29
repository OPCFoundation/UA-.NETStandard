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

// CA2000: integration-test disposables are released by helper cleanup paths.
// CA2007: tests run without a SynchronizationContext.
// CA2016: cleanup intentionally ignores the test cancellation token.
#pragma warning disable CA2000, CA2007, CA2016

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.Redundancy;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// End-to-end secured failover test for the distributed-HA feature: two
    /// servers share one session store via <see cref="DistributedSessionManager"/>,
    /// and a client with token-reuse failover fails over from the
    /// active to the standby. The standby restores the mirrored session and
    /// re-activates it with the reused authentication token, so the client's
    /// <c>SessionId</c> is preserved (a fresh re-authentication would change it).
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ManagedSession")]
    [Category("Distributed")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class DistributedSessionFailoverIntegrationTests : ClientTestFramework
    {
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            SingleSession = false;
            // Secured client fixture; the per-test HA servers are created below.
            return OneTimeSetUpCoreAsync(securityNone: false);
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        [SetUp]
        public override Task SetUpAsync()
        {
            return base.SetUpAsync();
        }

        [TearDown]
        public override Task TearDownAsync()
        {
            return base.TearDownAsync();
        }

        [Test]
        [Order(100)]
        [CancelAfter(180_000)]
        public async Task TokenReuseFailoverRestoresSessionOnStandbyAsync(CancellationToken ct)
        {
            using var sharedStore = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey());
            var factory = new DistributedSessionManagerFactory(
                sharedStore,
                protector,
                new DistributedSessionOptions { EnableFastReconnect = true });

            ServerFixture<ReferenceServer>? fixtureA = null;
            ServerFixture<ReferenceServer>? fixtureB = null;
            ManagedSessionType? session = null;

            try
            {
                (fixtureA, Uri urlA) = await StartHaServerAsync(factory).ConfigureAwait(false);
                (fixtureB, Uri urlB) = await StartHaServerAsync(factory).ConfigureAwait(false);

                ConfiguredEndpoint endpointA = await ClientFixture
                    .GetEndpointAsync(urlA, SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);
                ConfiguredEndpoint endpointB = await ClientFixture
                    .GetEndpointAsync(urlB, SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);

                var redundancyHandler = new FailoverRedundancyHandler(endpointB);
                session = await new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                    .UseEndpoint(endpointA)
                    .WithSessionName(nameof(TokenReuseFailoverRestoresSessionOnStandbyAsync))
                    .WithSessionTimeout(TimeSpan.FromSeconds(60))
                    .WithServerRedundancy(redundancyHandler)
                    .WithTokenReuseFailover()
                    .WithReconnectPolicy(p => p with
                    {
                        Strategy = BackoffStrategy.Constant,
                        InitialDelay = TimeSpan.FromMilliseconds(50),
                        MaxRetries = 1,
                        JitterFactor = 0.0
                    })
                    .ConnectAsync(ct)
                    .ConfigureAwait(false);

                // Session is live on the active server.
                DataValue stateBefore = await session
                    .ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(stateBefore.StatusCode), Is.True);

                NodeId sessionIdBefore = session.InnerSession.SessionId;
                Assert.That(sessionIdBefore.IsNull, Is.False);

                // Force a failover: make the in-place reconnect fail so the state
                // machine selects the redundant endpoint B.
                var reconnected = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                session.ConnectionStateChanged += (_, e) =>
                {
                    if (e.PreviousState == ConnectionState.Failover &&
                        e.NewState == ConnectionState.Connected)
                    {
                        reconnected.TrySetResult(true);
                    }
                };

                session.StateMachine.ReconnectWithBudgetAsync = (_, _) =>
                    Task.FromResult(new ServiceResult(StatusCodes.BadNotConnected));
                session.StateMachine.TriggerReconnect();

                Assert.That(
                    await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(60), ct).ConfigureAwait(false),
                    Is.True,
                    "the client should fail over to the standby server.");

                // The session works on the standby...
                DataValue stateAfter = await session
                    .ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(stateAfter.StatusCode), Is.True);

                // ...and its SessionId is preserved, proving the standby restored
                // the mirrored session and re-activated it with the reused token
                // rather than creating a brand-new session (re-auth fallback).
                Assert.That(
                    session.InnerSession.SessionId,
                    Is.EqualTo(sessionIdBefore),
                    "token-reuse failover must preserve the SessionId (no fresh CreateSession).");
                Assert.That(redundancyHandler.SelectCount, Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                if (session != null)
                {
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                if (fixtureA != null)
                {
                    await fixtureA.StopAsync().ConfigureAwait(false);
                }
                if (fixtureB != null)
                {
                    await fixtureB.StopAsync().ConfigureAwait(false);
                }
            }
        }

        private async Task<(ServerFixture<ReferenceServer> Fixture, Uri Url)> StartHaServerAsync(
            DistributedSessionManagerFactory factory)
        {
            var fixture = new ServerFixture<ReferenceServer>(telemetry =>
            {
                var server = new ReferenceServer(telemetry);
                server.SessionManagerFactory = factory;
                return server;
            })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = false,
                AutoAccept = true,
                OperationLimits = true
            };

            await fixture.StartAsync().ConfigureAwait(false);
            return (fixture, new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{fixture.Port}"));
        }

        private static byte[] MakeKey()
        {
            byte[] key = new byte[32];
            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (byte)(i + 7);
            }
            return key;
        }

        private sealed class FailoverRedundancyHandler : IServerRedundancyHandler
        {
            public FailoverRedundancyHandler(ConfiguredEndpoint target)
            {
                m_target = target;
            }

            public int SelectCount { get; private set; }

            public ValueTask<ServerRedundancyInfo> FetchRedundancyInfoAsync(
                Client.ISession session,
                CancellationToken ct = default)
            {
                return new ValueTask<ServerRedundancyInfo>(new ServerRedundancyInfo
                {
                    Mode = RedundancySupport.Hot,
                    ServiceLevel = 200,
                    RedundantServers =
                    [
                        new RedundantServer
                        {
                            ServerUri = m_target.EndpointUrl?.ToString() ?? "urn:standby",
                            ServerState = ServerState.Running,
                            ServiceLevel = 250
                        }
                    ]
                });
            }

            public ServerFailoverDecision ShouldFailover(
                ServerRedundancyInfo redundancyInfo,
                ConfiguredEndpoint currentEndpoint)
            {
                return new ServerFailoverDecision(
                    isFailoverWarranted: true,
                    DateTime.MinValue,
                    "Test handler warrants failover to the configured standby.");
            }

            public ConfiguredEndpoint SelectFailoverTarget(
                ServerRedundancyInfo redundancyInfo,
                ConfiguredEndpoint currentEndpoint)
            {
                SelectCount++;
                return m_target;
            }

            private readonly ConfiguredEndpoint m_target;
        }
    }
}
