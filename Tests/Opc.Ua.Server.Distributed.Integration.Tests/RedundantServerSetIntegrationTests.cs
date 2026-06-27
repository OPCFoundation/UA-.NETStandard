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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.Distributed;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Server.Distributed.Integration.Tests
{
    /// <summary>
    /// End-to-end redundancy validation across two in-process servers.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ManagedSession")]
    [Category("Distributed")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class RedundantServerSetIntegrationTests : ClientTestFramework
    {
        private const string kMirrorNamespaceUri = "urn:opcfoundation:tests:redundant-integration";
        private const string kMirrorNodeIdentifier = "MirroredValue";
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(30);

        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = true;
            SingleSession = false;
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
        public async Task ColdRedundantManagedClientResolvesPeerAndReconnectsOnFailoverAsync(
            CancellationToken ct)
        {
            await using RedundantPair pair = await RedundantPair
                .StartAsync(RedundancySupport.Cold, Telemetry, initiallyDegradePrimary: true)
                .ConfigureAwait(false);
            await using RedundantManagedClient client = await ConnectRedundantClientAsync(
                pair.Primary,
                new RedundantManagedClientOptions(),
                tokenReuse: false,
                ct).ConfigureAwait(false);

            var handler = new DefaultServerRedundancyHandler(
                new DefaultRedundantServerEndpointResolver(Telemetry));
            ServerRedundancyInfo info = await WaitForRedundancyInfoAsync(
                handler,
                client.CurrentSession!,
                RedundancySupport.Cold,
                pair.Backup.ApplicationUri,
                ct).ConfigureAwait(false);
            Assert.That(info.Mode, Is.EqualTo(RedundancySupport.Cold));
            Assert.That(info.RedundantServers, Has.Count.EqualTo(1));
            Assert.That(info.RedundantServers[0].Endpoint, Is.Not.Null);
            Assert.That(
                info.RedundantServers[0].Endpoint!.Description.EndpointUrl,
                Does.Contain(pair.Backup.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            await client.FailoverAsync(ct).ConfigureAwait(false);
            await WaitForCurrentEndpointAsync(client, pair.Backup.ApplicationUri, ct).ConfigureAwait(false);

            Assert.That(
                client.CurrentSession!.ConfiguredEndpoint.Description.Server.ApplicationUri,
                Is.EqualTo(pair.Backup.ApplicationUri));
            DataValue stateAfter = await client.CurrentSession
                .ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(stateAfter.StatusCode), Is.True);
        }

        [Test]
        [Order(110)]
        [CancelAfter(180_000)]
        public async Task HotAndMirroredManualMaintenanceBacksOffThenMirroredFailoverPreservesSessionAsync(
            CancellationToken ct)
        {
            await using RedundantPair pair = await RedundantPair
                .StartAsync(RedundancySupport.HotAndMirrored, Telemetry)
                .ConfigureAwait(false);
            await using RedundantManagedClient client = await ConnectRedundantClientAsync(
                pair.Primary,
                new RedundantManagedClientOptions(),
                tokenReuse: true,
                ct).ConfigureAwait(false);

            ManagedSessionType sessionBefore = client.CurrentSession!;
            ManagedSessionType currentSession = client.CurrentSession!;
            DateTime estimatedReturn = DateTime.UtcNow.AddMinutes(5);
            await RequestMaintenanceAsync(currentSession, estimatedReturn, ct).ConfigureAwait(false);

            var handler = new DefaultServerRedundancyHandler(
                new DefaultRedundantServerEndpointResolver(Telemetry));
            ServerRedundancyInfo maintenanceInfo = await WaitForServiceLevelSubrangeAsync(
                handler,
                currentSession,
                ServiceLevelSubrange.Maintenance,
                ct).ConfigureAwait(false);
            Assert.That(maintenanceInfo.ServiceLevelSubrange, Is.EqualTo(ServiceLevelSubrange.Maintenance));
            Assert.That(
                handler.ShouldFailover(
                    maintenanceInfo,
                    currentSession.ConfiguredEndpoint).IsFailoverWarranted,
                Is.False,
                "Maintenance should make the client back off until EstimatedReturnTime.");

            pair.Primary.ServiceLevelController.SetServiceLevel(ServiceLevels.NoData);
            await WaitForServiceLevelSubrangeAsync(
                handler,
                currentSession,
                ServiceLevelSubrange.NoData,
                ct).ConfigureAwait(false);
            await client.FailoverAsync(ct).ConfigureAwait(false);
            await WaitForCurrentEndpointAsync(client, pair.Backup.ApplicationUri, ct).ConfigureAwait(false);

            Assert.That(
                client.CurrentRedundantSession!.Endpoint.Description.Server.ApplicationUri,
                Is.EqualTo(pair.Backup.ApplicationUri));
            Assert.That(
                client.CurrentSession,
                Is.SameAs(sessionBefore),
                "HotAndMirrored failover should reactivate the mirrored session without CreateSession.");
            DataValue stateAfter = await client.CurrentSession
                .ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(stateAfter.StatusCode), Is.True);
        }

        [Test]
        [Order(120)]
        [CancelAfter(180_000)]
        public async Task HotAndMirroredFailoverReadsMirroredAddressSpaceValueAsync(CancellationToken ct)
        {
            await using RedundantPair pair = await RedundantPair
                .StartAsync(RedundancySupport.HotAndMirrored, Telemetry)
                .ConfigureAwait(false);
            await using RedundantManagedClient client = await ConnectRedundantClientAsync(
                pair.Primary,
                new RedundantManagedClientOptions(),
                tokenReuse: true,
                ct).ConfigureAwait(false);

            NodeId mirroredNodeId = await GetMirrorNodeIdAsync(
                client.CurrentSession!,
                ct).ConfigureAwait(false);
            const int mirroredValue = 8675309;
            await WriteValueAsync(client.CurrentSession!, mirroredNodeId, mirroredValue, ct).ConfigureAwait(false);
            await WaitForValueAsync(pair.Backup, mirroredNodeId, mirroredValue, ct).ConfigureAwait(false);

            pair.Primary.ServiceLevelController.SetServiceLevel(ServiceLevels.NoData);
            var handler = new DefaultServerRedundancyHandler(
                new DefaultRedundantServerEndpointResolver(Telemetry));
            await WaitForServiceLevelSubrangeAsync(
                handler,
                client.CurrentSession!,
                ServiceLevelSubrange.NoData,
                ct).ConfigureAwait(false);
            await client.FailoverAsync(ct).ConfigureAwait(false);
            await WaitForCurrentEndpointAsync(client, pair.Backup.ApplicationUri, ct).ConfigureAwait(false);

            Assert.That(
                client.CurrentRedundantSession!.Endpoint.Description.Server.ApplicationUri,
                Is.EqualTo(pair.Backup.ApplicationUri));
            DataValue valueAfter = await client.CurrentSession!
                .ReadValueAsync(mirroredNodeId, ct)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(valueAfter.StatusCode), Is.True);
            Assert.That(valueAfter.GetValue<int>(0), Is.EqualTo(mirroredValue));
        }

        private async Task<RedundantManagedClient> ConnectRedundantClientAsync(
            RedundantServerInstance primary,
            RedundantManagedClientOptions options,
            bool tokenReuse,
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(primary.EndpointUrl, SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);

            ManagedSessionBuilder builder = new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName(TestContext.CurrentContext.Test.Name)
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .WithServerRedundancy(new DefaultServerRedundancyHandler(
                    new DefaultRedundantServerEndpointResolver(Telemetry)))
                .WithReconnectPolicy(p => p with
                {
                    Strategy = BackoffStrategy.Constant,
                    InitialDelay = TimeSpan.FromMilliseconds(50),
                    MaxRetries = 1,
                    JitterFactor = 0.0
                });

            if (tokenReuse)
            {
                builder = builder.WithTokenReuseFailover();
            }

            return await builder.ConnectRedundantAsync(options, ct).ConfigureAwait(false);
        }

        private async Task<ServerRedundancyInfo> WaitForRedundancyInfoAsync(
            DefaultServerRedundancyHandler handler,
            ManagedSessionType session,
            RedundancySupport expectedMode,
            string expectedPeerApplicationUri,
            CancellationToken ct)
        {
            ServerRedundancyInfo? last = null;
            await PollUntilAsync(
                async token =>
                {
                    last = await handler.FetchRedundancyInfoAsync(session, token).ConfigureAwait(false);
                    return last.Mode == expectedMode &&
                        last.RedundantServers.Count > 0 &&
                        last.RedundantServers[0].Endpoint != null &&
                        string.Equals(
                            last.RedundantServers[0].ServerUri,
                            expectedPeerApplicationUri,
                            StringComparison.Ordinal);
                },
                $"redundancy info for {expectedPeerApplicationUri}",
                ct).ConfigureAwait(false);

            return last!;
        }

        private async Task<ServerRedundancyInfo> WaitForServiceLevelSubrangeAsync(
            DefaultServerRedundancyHandler handler,
            ManagedSessionType session,
            ServiceLevelSubrange expectedSubrange,
            CancellationToken ct)
        {
            ServerRedundancyInfo? last = null;
            await PollUntilAsync(
                async token =>
                {
                    last = await handler.FetchRedundancyInfoAsync(session, token).ConfigureAwait(false);
                    return last.ServiceLevelSubrange == expectedSubrange;
                },
                $"ServiceLevel subrange {expectedSubrange}",
                ct).ConfigureAwait(false);

            return last!;
        }

        private static async Task WaitForCurrentEndpointAsync(
            RedundantManagedClient client,
            string expectedApplicationUri,
            CancellationToken ct)
        {
            await PollUntilAsync(
                _ =>
                {
                    string? applicationUri = client.CurrentRedundantSession?.Endpoint.Description.Server.ApplicationUri;
                    return new ValueTask<bool>(string.Equals(
                        applicationUri,
                        expectedApplicationUri,
                        StringComparison.Ordinal));
                },
                $"current endpoint {expectedApplicationUri}",
                ct).ConfigureAwait(false);
        }

        private static async Task RequestMaintenanceAsync(
            ManagedSessionType session,
            DateTime estimatedReturn,
            CancellationToken ct)
        {
            CallResponse response = await session.CallAsync(
                null,
                new[]
                {
                    new CallMethodRequest
                    {
                        ObjectId = ObjectIds.Server,
                        MethodId = MethodIds.Server_RequestServerStateChange,
                        InputArguments = new Variant[]
                        {
                            Variant.From(ServerState.Shutdown),
                            Variant.From(estimatedReturn),
                            Variant.From((uint)0),
                            Variant.From(new LocalizedText("integration-test maintenance")),
                            Variant.From(false)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);

            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0].StatusCode),
                Is.True,
                response.Results[0].StatusCode.ToString());
        }

        private static async Task<NodeId> GetMirrorNodeIdAsync(
            ManagedSessionType session,
            CancellationToken ct)
        {
            await session.FetchNamespaceTablesAsync(ct).ConfigureAwait(false);
            int namespaceIndex = session.NamespaceUris.GetIndex(kMirrorNamespaceUri);
            Assert.That(namespaceIndex, Is.GreaterThanOrEqualTo(0));
            return new NodeId(kMirrorNodeIdentifier, (ushort)namespaceIndex);
        }

        private static async Task WriteValueAsync(
            ManagedSessionType session,
            NodeId nodeId,
            int value,
            CancellationToken ct)
        {
            WriteResponse response = await session.WriteAsync(
                null,
                new[]
                {
                    new WriteValue
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(value))
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);

            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(response.Results[0]),
                Is.True,
                response.Results[0].ToString());
        }

        private async Task WaitForValueAsync(
            RedundantServerInstance server,
            NodeId nodeId,
            int expectedValue,
            CancellationToken ct)
        {
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(server.EndpointUrl, SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            await using ManagedSessionType session = await new ManagedSessionBuilder(
                    ClientFixture.Config,
                    Telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName("MirrorPoll")
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .ConnectAsync(ct)
                .ConfigureAwait(false);

            await PollUntilAsync(
                async token =>
                {
                    DataValue value = await session.ReadValueAsync(nodeId, token).ConfigureAwait(false);
                    return StatusCode.IsGood(value.StatusCode) &&
                        value.WrappedValue.TryGetValue(out int actual) &&
                        actual == expectedValue;
                },
                $"mirrored value {expectedValue} on {server.ApplicationUri}",
                ct).ConfigureAwait(false);
        }

        private static async Task PollUntilAsync(
            Func<CancellationToken, ValueTask<bool>> condition,
            string description,
            CancellationToken ct)
        {
            using var timeout = new CancellationTokenSource(ReadinessTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            while (!linked.IsCancellationRequested)
            {
                if (await condition(linked.Token).ConfigureAwait(false))
                {
                    return;
                }

                await Task.Delay(PollInterval, linked.Token).ConfigureAwait(false);
            }

            Assert.Fail($"Timed out waiting for {description}.");
        }

        private static byte[] MakeKey()
        {
            byte[] key = new byte[32];
            for (int ii = 0; ii < key.Length; ii++)
            {
                key[ii] = (byte)(ii + 11);
            }

            return key;
        }

        private sealed class RedundantPair : IAsyncDisposable
        {
            private RedundantPair(
                RedundantServerInstance primary,
                RedundantServerInstance backup,
                InMemorySharedKeyValueStore sharedStore,
                AesCbcHmacRecordProtector protector)
            {
                Primary = primary;
                Backup = backup;
                m_sharedStore = sharedStore;
                m_protector = protector;
            }

            public RedundantServerInstance Primary { get; }

            public RedundantServerInstance Backup { get; }

            public static async Task<RedundantPair> StartAsync(
                RedundancySupport mode,
                ITelemetryContext telemetry,
                bool initiallyDegradePrimary = false)
            {
                var sharedStore = new InMemorySharedKeyValueStore();
                var protector = new AesCbcHmacRecordProtector(MakeKey());
                int primaryPort = ServerFixtureUtils.GetNextFreeIPPort();
                int backupPort = ServerFixtureUtils.GetNextFreeIPPort();
                string primaryUri = "urn:localhost:OPCFoundation:RedundantIntegration:primary";
                string backupUri = "urn:localhost:OPCFoundation:RedundantIntegration:backup";
                Uri primaryUrl = new($"{Utils.UriSchemeOpcTcp}://localhost:{primaryPort}/RedundantReferenceServer");
                Uri backupUrl = new($"{Utils.UriSchemeOpcTcp}://localhost:{backupPort}/RedundantReferenceServer");

                RedundantServerInstance primary = await RedundantServerInstance
                    .StartAsync(
                        mode,
                        primaryUri,
                        primaryUrl,
                        backupUri,
                        backupUrl,
                        sharedStore,
                        protector,
                        telemetry,
                        initiallyDegradePrimary,
                        isAddressSpaceWriter: true)
                    .ConfigureAwait(false);
                RedundantServerInstance backup = await RedundantServerInstance
                    .StartAsync(
                        mode,
                        backupUri,
                        backupUrl,
                        primaryUri,
                        primaryUrl,
                        sharedStore,
                        protector,
                        telemetry,
                        initiallyDegrade: false,
                        isAddressSpaceWriter: false)
                    .ConfigureAwait(false);
                return new RedundantPair(primary, backup, sharedStore, protector);
            }

            public async ValueTask DisposeAsync()
            {
                await Primary.DisposeAsync().ConfigureAwait(false);
                await Backup.DisposeAsync().ConfigureAwait(false);
                m_protector.Dispose();
                m_sharedStore.Dispose();
            }

            private readonly InMemorySharedKeyValueStore m_sharedStore;
            private readonly AesCbcHmacRecordProtector m_protector;
        }

        private sealed class RedundantServerInstance : IAsyncDisposable
        {
            private RedundantServerInstance(
                ServerFixture<RedundantReferenceServer> fixture,
                LeaderServiceLevelProvider serviceLevelController,
                DistributedAddressSpaceStartupTask addressSpaceTask,
                string applicationUri,
                Uri endpointUrl)
            {
                m_fixture = fixture;
                ServiceLevelController = serviceLevelController;
                m_addressSpaceTask = addressSpaceTask;
                ApplicationUri = applicationUri;
                EndpointUrl = endpointUrl;
            }

            public string ApplicationUri { get; }

            public Uri EndpointUrl { get; }

            public int Port => m_fixture.Port;

            public LeaderServiceLevelProvider ServiceLevelController { get; }

            public static async Task<RedundantServerInstance> StartAsync(
                RedundancySupport mode,
                string applicationUri,
                Uri endpointUrl,
                string peerApplicationUri,
                Uri peerEndpointUrl,
                InMemorySharedKeyValueStore sharedStore,
                AesCbcHmacRecordProtector protector,
                ITelemetryContext telemetry,
                bool initiallyDegrade,
                bool isAddressSpaceWriter)
            {
                var redundancyOptions = new ServerRedundancyOptions
                {
                    Mode = mode,
                    CurrentServerId = applicationUri
                };
                redundancyOptions.AddRedundantPeer(
                    peerApplicationUri,
                    new ArrayOf<string>(new[] { peerEndpointUrl.ToString() }));

                var serviceLevelController = new LeaderServiceLevelProvider(
                    new StaticLeaderElection(true),
                    mode);
                var sessionFactory = new DistributedSessionManagerFactory(
                    sharedStore,
                    protector,
                    new DistributedSessionOptions { EnableFastReconnect = true });

                var fixture = new ServerFixture<RedundantReferenceServer>(
                    serverTelemetry => new RedundantReferenceServer(
                        serverTelemetry,
                        sessionFactory,
                        sharedStore,
                        protector,
                        redundancyOptions))
                {
                    UriScheme = Utils.UriSchemeOpcTcp,
                    SecurityNone = false,
                    AutoAccept = true,
                    OperationLimits = true
                };

                await fixture.LoadConfigurationAsync().ConfigureAwait(false);
                fixture.Config.ApplicationUri = applicationUri;
                await fixture.StartAsync(null, endpointUrl.Port).ConfigureAwait(false);

                IServerInternal server = fixture.Server.CurrentInstance;
                await new ServerRedundancyStartupTask(redundancyOptions)
                    .OnServerStartedAsync(server)
                    .ConfigureAwait(false);
                await new ServiceLevelStartupTask(serviceLevelController)
                    .OnServerStartedAsync(server)
                    .ConfigureAwait(false);
                await new RequestServerStateChangeStartupTask(
                        new RequestServerStateChangeOptions
                        {
                            AdminAccessValidator = _ => { }
                        },
                        serviceLevelController)
                    .OnServerStartedAsync(server)
                    .ConfigureAwait(false);
                AllowRequestServerStateChangeForIntegrationClient(server);

                var addressSpaceTask = new DistributedAddressSpaceStartupTask(
                    sharedStore,
                    new StaticLeaderElection(isAddressSpaceWriter),
                    protector);
                await addressSpaceTask.OnServerStartedAsync(server).ConfigureAwait(false);

                if (initiallyDegrade)
                {
                    serviceLevelController.SetServiceLevel(ServiceLevels.NoData);
                }

                return new RedundantServerInstance(
                    fixture,
                    serviceLevelController,
                    addressSpaceTask,
                    applicationUri,
                    endpointUrl);
            }

            public async ValueTask DisposeAsync()
            {
                await m_addressSpaceTask.DisposeAsync().ConfigureAwait(false);
                ServiceLevelController.Dispose();
                await m_fixture.StopAsync().ConfigureAwait(false);
            }

            private readonly ServerFixture<RedundantReferenceServer> m_fixture;
            private readonly DistributedAddressSpaceStartupTask m_addressSpaceTask;
        }

        private static void AllowRequestServerStateChangeForIntegrationClient(IServerInternal server)
        {
            MethodState? method = server.DiagnosticsNodeManager
                .FindPredefinedNode<MethodState>(MethodIds.Server_RequestServerStateChange);
            if (method == null)
            {
                return;
            }

            method.Executable = true;
            method.UserExecutable = true;
            method.RolePermissions = default;
            method.UserRolePermissions = default;
            method.AccessRestrictions = null;
            method.ClearChangeMasks(server.DefaultSystemContext, false);
        }

        private sealed class RedundantReferenceServer : ReferenceServer
        {
            public RedundantReferenceServer(
                ITelemetryContext telemetry,
                DistributedSessionManagerFactory sessionManagerFactory,
                InMemorySharedKeyValueStore sharedStore,
                AesCbcHmacRecordProtector protector,
                ServerRedundancyOptions redundancyOptions)
                : base(telemetry)
            {
                AddNodeManager(new MirroredValueNodeManagerFactory());
                SessionManagerFactory = sessionManagerFactory;
                RedundantServerSetProvider = new ConfiguredRedundantServerSetProvider(redundancyOptions);
                m_sharedStore = sharedStore;
                m_protector = protector;
            }

            protected override ISubscriptionStore? CreateSubscriptionStore(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return new SharedKeyValueSubscriptionStore(
                    m_sharedStore,
                    server.MessageContext,
                    m_protector);
            }

            private readonly InMemorySharedKeyValueStore m_sharedStore;
            private readonly AesCbcHmacRecordProtector m_protector;
        }

        private sealed class MirroredValueNodeManagerFactory : INodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris { get; } = new(new[] { kMirrorNamespaceUri });

            public INodeManager Create(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return new MirroredValueNodeManager(server);
            }
        }

        private sealed class MirroredValueNodeManager : CustomNodeManager2
        {
            public MirroredValueNodeManager(IServerInternal server)
                : base(server, kMirrorNamespaceUri)
            {
            }

            public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
            {
                if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = [];
                }

                ushort namespaceIndex = NamespaceIndexes[0];
                var variable = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId(kMirrorNodeIdentifier, namespaceIndex),
                    BrowseName = new QualifiedName(kMirrorNodeIdentifier, namespaceIndex),
                    DisplayName = new LocalizedText(kMirrorNodeIdentifier),
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentReadOrWrite,
                    UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                    Value = 0,
                    StatusCode = StatusCodes.Good,
                    Timestamp = DateTimeUtc.Now
                };
                variable.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, variable.NodeId));
                AddPredefinedNode(SystemContext, variable);
            }
        }
    }
}
