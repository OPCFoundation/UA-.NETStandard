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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Redundancy;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Client;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// End-to-end integration test for the transparent client-redundancy facade
    /// (<see cref="RedundantClientSession"/>). Two client replicas share one store and
    /// coordinate leadership against a live server; the test drives real browse/read
    /// traffic through the facade and forces a leader change, proving that the follower
    /// blocks until promoted and that the same facade reference keeps serving after the
    /// underlying leader session is swapped.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ManagedSession")]
    [Category("Distributed")]
    [Category("ClientRedundancy")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class RedundantClientSessionFailoverIntegrationTests : ClientTestFramework
    {
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            SingleSession = false;
            return OneTimeSetUpCoreAsync(securityNone: true);
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
        [CancelAfter(120_000)]
        public async Task FacadeBlocksUntilLeaderThenSurvivesHandoffAsync(CancellationToken ct)
        {
            var timeout = TimeSpan.FromSeconds(30);
            using var store = new InMemorySharedKeyValueStore();
            var electionA = new ControllableLeaderElection();
            var electionB = new ControllableLeaderElection();

            ServerFixture<ReferenceServer>? fixture = null;
            RedundantClientSession? replicaA = null;
            RedundantClientSession? replicaB = null;
            Task<DataValue>? blockedOnB = null;
            try
            {
                (fixture, Uri url) = await StartServerAsync().ConfigureAwait(false);
                ConfiguredEndpoint endpoint = await ClientFixture
                    .GetEndpointAsync(url, SecurityPolicies.None)
                    .ConfigureAwait(false);

                replicaA = BuildReplica("replica-A", endpoint, electionA, store);
                replicaB = BuildReplica("replica-B", endpoint, electionB, store);
                await replicaA.StartAsync(ct).ConfigureAwait(false);
                await replicaB.StartAsync(ct).ConfigureAwait(false);

                // No replica is leader yet: synchronous members fault and async calls block.
                ServiceResultException? syncFault = Assert.Throws<ServiceResultException>(
                    () => _ = replicaB.SessionName);
                Assert.That(syncFault!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

#pragma warning disable CA2025 // Do not pass 'IDisposable' instances into unawaited tasks
                blockedOnB = ReadServerStateAsync(replicaB, ct);
#pragma warning restore CA2025 // Do not pass 'IDisposable' instances into unawaited tasks
                await Task.Delay(500, ct).ConfigureAwait(false);
                Assert.That(
                    blockedOnB.IsCompleted,
                    Is.False,
                    "an async call on a follower must block until this replica is promoted.");

                // Promote A: it creates its session and the facade starts serving.
                electionA.SetLeader(true);
                DataValue stateA = await ReadServerStateAsync(replicaA, ct)
                    .WaitAsync(timeout, ct).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(stateA.StatusCode), Is.True);
                Assert.That(replicaA.IsLeader, Is.True);

                BrowseResponse browseA = await BrowseObjectsAsync(replicaA, ct)
                    .WaitAsync(timeout, ct).ConfigureAwait(false);
                Assert.That(browseA.Results.Count, Is.GreaterThan(0));
                Assert.That(browseA.Results[0].References.Count, Is.GreaterThan(0));

                // Hand off leadership A -> B. B's previously-blocked read must now complete
                // against the freshly swapped-in session, through the same facade reference.
                electionA.SetLeader(false);
                electionB.SetLeader(true);

                DataValue stateB = await blockedOnB.WaitAsync(timeout, ct).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(stateB.StatusCode), Is.True);
                Assert.That(replicaB.IsLeader, Is.True);
                Assert.That(replicaA.IsLeader, Is.False);

                // The same B facade keeps serving fresh traffic after the swap.
                BrowseResponse browseB = await BrowseObjectsAsync(replicaB, ct)
                    .WaitAsync(timeout, ct).ConfigureAwait(false);
                Assert.That(browseB.Results.Count, Is.GreaterThan(0));

                DataValue stateBAgain = await ReadServerStateAsync(replicaB, ct)
                    .WaitAsync(timeout, ct).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(stateBAgain.StatusCode), Is.True);
            }
            finally
            {
                if (replicaA != null)
                {
                    await replicaA.DisposeAsync().ConfigureAwait(false);
                }
                if (blockedOnB != null)
                {
                    await blockedOnB.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                }
                if (replicaB != null)
                {
                    await replicaB.DisposeAsync().ConfigureAwait(false);
                }
                if (fixture != null)
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }
        }

        private RedundantClientSession BuildReplica(
            string nodeId,
            ConfiguredEndpoint endpoint,
            ILeaderElection election,
            ISharedKeyValueStore store)
        {
            return new RedundantClientSessionBuilder(Telemetry)
                .WithNodeId(nodeId)
                .WithStandbyMode(ClientStandbyMode.Cold)
                .UseSession(token => new ValueTask<ManagedSession>(
                    new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                        .UseEndpoint(endpoint)
                        .WithSessionName(nodeId)
                        .ConnectAsync(token)))
                .UseRedundancy(election, store, NullRecordProtector.Instance)
                .Build();
        }

        private async Task<(ServerFixture<ReferenceServer> Fixture, Uri Url)> StartServerAsync()
        {
            var fixture = new ServerFixture<ReferenceServer>(telemetry => new ReferenceServer(telemetry))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true,
                OperationLimits = true
            };
            await fixture.StartAsync().ConfigureAwait(false);
            return (fixture, new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{fixture.Port}"));
        }

        private static async Task<DataValue> ReadServerStateAsync<TSession>(TSession session, CancellationToken ct)
            where TSession : ISession
        {
            ReadValueId[] nodesToRead =
            [
                new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }
            ];
            ReadResponse response = await session
                .ReadAsync(null, 0, TimestampsToReturn.Both, nodesToRead, ct)
                .ConfigureAwait(false);
            return response.Results[0];
        }

        private static async Task<BrowseResponse> BrowseObjectsAsync<TSession>(TSession session, CancellationToken ct)
            where TSession : ISession
        {
            var description = new BrowseDescription
            {
                NodeId = ObjectIds.ObjectsFolder,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                ResultMask = (uint)BrowseResultMask.All
            };
            BrowseDescription[] nodesToBrowse = [description];
            return await session
                .BrowseAsync(null, null, 0u, nodesToBrowse, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// A leader election whose leadership can be flipped from the test so a handoff is
        /// deterministic (the shipped <c>StaticLeaderElection</c> is fixed at construction).
        /// </summary>
        private sealed class ControllableLeaderElection : ILeaderElection
        {
            public bool IsLeader => m_isLeader;

            public event Action<bool>? LeadershipChanged;

            public ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default)
            {
                return new ValueTask<bool>(m_isLeader);
            }

            public void Start()
            {
                LeadershipChanged?.Invoke(m_isLeader);
            }

            public void SetLeader(bool isLeader)
            {
                m_isLeader = isLeader;
                LeadershipChanged?.Invoke(isLeader);
            }

            public ValueTask DisposeAsync()
            {
                return default;
            }

            private volatile bool m_isLeader;
        }
    }
}
