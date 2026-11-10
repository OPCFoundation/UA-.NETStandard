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
 * copies of the Software, and to permit persons to whom the Software
 * is furnished to do so, subject to the following conditions:
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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Network;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Samples.Network;
using Opc.Ua.Server.Network;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Network
{
    /// <summary>
    /// Exercises the provider-backed Part 22 model through real opc.tcp services.
    /// </summary>
    [TestFixture]
    [Category("NetworkModel")]
    [NonParallelizable]
    public sealed class NetworkModelIntegrationTests
    {
        /// <summary>
        /// Starts a real server and client around the deterministic provider boundary.
        /// </summary>
        [SetUp]
        public async Task SetUpAsync()
        {
            await StartServerAsync(prependNamespace: false).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts the scenario with an optional namespace registered ahead of the network manager.
        /// </summary>
        private async Task StartServerAsync(bool prependNamespace)
        {
            m_pkiRoot = Path.Combine(Path.GetTempPath(), "OpcUaNetworkTests", Guid.NewGuid().ToString("N")[..8]);
            m_provider = NetworkScenario.Create();
            m_providerBoundary = new FaultInjectingProvider(m_provider);
            m_permitMappingEdits = 0;
            m_server = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
                if (prependNamespace)
                {
                    server.AddNodeManager(new StartupProbeFactory(_ => { }));
                }
                server.AddNodeManager(new NetworkModelNodeManagerFactory(
                    m_providerBoundary, _ => Volatile.Read(ref m_permitMappingEdits) != 0));
                return server;
            })
            {
                SecurityNone = true,
                AutoAccept = true
            };
            await m_server.LoadConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            await m_server.StartAsync(m_pkiRoot).ConfigureAwait(false);
            m_clientFixture = new ClientFixture(NUnitTelemetryContext.Create());
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);
            ConfiguredEndpoint endpoint = await m_clientFixture.GetEndpointAsync(
                new Uri("opc.tcp://localhost:" + m_server.Port.ToString(CultureInfo.InvariantCulture)),
                SecurityPolicies.None).ConfigureAwait(false);
            m_session = await m_clientFixture.ConnectAsync(endpoint).ConfigureAwait(false);
            m_client = new NetworkModelClient(m_session, NUnitTelemetryContext.Create());
        }

        /// <summary>
        /// URI-qualified variable, method and argument identifiers survive a namespace registration order change.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task DescendantIdentifiersSurviveNamespaceRegistrationOrderChanges(
            CancellationToken cancellationToken)
        {
            ushort originalNamespace = m_session.NamespaceUris.GetIndexOrAppend(NetworkModelNodeManager.NamespaceUri);
            Dictionary<NodeId, NodeClass> original = await ReadNetworkIdentifiersAsync(cancellationToken)
                .ConfigureAwait(false);
            Assert.That(original.Values, Does.Contain(NodeClass.Variable));
            Assert.That(original.Values, Does.Contain(NodeClass.Method));
            Assert.That(original.Keys.Any(id => id.IdentifierAsString.EndsWith(
                "/" + BrowseNames.InputArguments, StringComparison.Ordinal)), Is.True);
            Assert.That(original.Keys.Any(id => id.IdentifierAsString.Contains(
                "%2F", StringComparison.OrdinalIgnoreCase)), Is.False);

            await TearDownAsync().ConfigureAwait(false);
            await StartServerAsync(prependNamespace: true).ConfigureAwait(false);
            ushort reorderedNamespace = m_session.NamespaceUris.GetIndexOrAppend(NetworkModelNodeManager.NamespaceUri);
            Assert.That(reorderedNamespace, Is.Not.EqualTo(originalNamespace));
            Dictionary<NodeId, NodeClass> reordered = await ReadNetworkIdentifiersAsync(cancellationToken)
                .ConfigureAwait(false);
            Assert.That(reordered, Is.EquivalentTo(original));

            ReadResponse persisted = await m_session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                ArrayOf.Wrapped(original.Keys.Select(id => new ReadValueId
                {
                    NodeId = id.WithNamespaceIndex(reorderedNamespace),
                    AttributeId = Attributes.NodeClass
                }).ToArray()), cancellationToken).ConfigureAwait(false);
            Assert.That(persisted.Results.Count, Is.EqualTo(original.Count));
            Assert.That(persisted.Results.ToList().All(value => StatusCode.IsGood(value.StatusCode)), Is.True);
        }

        /// <summary>
        /// Reads all application-owned descendants below Resources with namespace-independent dictionary keys.
        /// </summary>
        private async Task<Dictionary<NodeId, NodeClass>> ReadNetworkIdentifiersAsync(
            CancellationToken cancellationToken)
        {
            ushort networkNamespace = m_session.NamespaceUris.GetIndexOrAppend(NetworkModelNodeManager.NamespaceUri);
            var identifiers = new Dictionary<NodeId, NodeClass>();
            var pending = new Queue<NodeId>();
            var visited = new HashSet<NodeId>();
            pending.Enqueue(ObjectIds.Resources);
            while (pending.Count != 0)
            {
                NodeId current = pending.Dequeue();
                if (!visited.Add(current))
                {
                    continue;
                }
                BrowseResponse response = await m_session.BrowseAsync(null, null, 0,
                    [
                        new BrowseDescription
                        {
                            NodeId = current,
                            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                            BrowseDirection = BrowseDirection.Forward,
                            IncludeSubtypes = true,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], cancellationToken).ConfigureAwait(false);
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[0].ContinuationPoint.IsEmpty, Is.True);
                foreach (ReferenceDescription child in response.Results[0].References.ToList())
                {
                    NodeId id = ExpandedNodeId.ToNodeId(child.NodeId, m_session.NamespaceUris);
                    if (id.NamespaceIndex == networkNamespace)
                    {
                        identifiers[id.WithNamespaceIndex(0)] = child.NodeClass;
                    }
                    if (id.NamespaceIndex == 0 || id.NamespaceIndex == networkNamespace)
                    {
                        pending.Enqueue(id);
                    }
                }
            }
            return identifiers;
        }

        /// <summary>
        /// Stops owned sessions and removes the test's generated certificate stores.
        /// </summary>
        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_session != null)
            {
                await m_session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                await m_session.DisposeAsync().ConfigureAwait(false);
            }
            if (m_clientFixture != null)
            {
                await m_clientFixture.DisposeAsync().ConfigureAwait(false);
            }
            if (m_server != null)
            {
                await m_server.StopAsync().ConfigureAwait(false);
            }
            if (Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        /// <summary>
        /// Starts observation after reference installation, even when the first watch removes the initial interfaces.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task InitialWatchRemovalCannotReinstallStartupReferences(CancellationToken cancellationToken)
        {
            string pkiRoot = Path.Combine(
                Path.GetTempPath(), "OpcUaNetworkTests", Guid.NewGuid().ToString("N")[..8]);
            ReferenceNetworkModelProvider provider = NetworkScenario.Create();
            var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var watchRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var boundary = new FaultInjectingProvider(provider)
            {
                WatchRelease = watchRelease,
                WatchTransform = snapshot => snapshot with
                {
                    Revision = snapshot.Revision + 1,
                    Timestamp = snapshot.Timestamp.AddSeconds(1),
                    Interfaces = [],
                    Streams = [],
                    Lldp = null
                },
                AfterDelivery = _ => delivered.TrySetResult(true)
            };
            bool watchedDuringCreation = false;
            var publishedReferences = new Dictionary<NodeId, ExpandedNodeId[]>();
            NodeId[] parents =
            [
                ObjectIds.NetworkInterfaces,
                ObjectTypeIds.IIetfBaseNetworkInterfaceType,
                ObjectTypeIds.IIeeeBaseEthernetPortType
            ];
            var server = new ServerFixture<StandardServer>(telemetry =>
            {
                var standardServer = new StandardServer(telemetry);
                standardServer.AddNodeManager(new NetworkModelNodeManagerFactory(boundary, _ => false));
                standardServer.AddNodeManager(new StartupProbeFactory(_ =>
                {
                    watchedDuringCreation = boundary.WatchCount != 0;
                }));
                return standardServer;
            })
            {
                SecurityNone = true,
                AutoAccept = true
            };
            ClientFixture? clientFixture = null;
            Client.ISession? session = null;
            try
            {
                await server.LoadConfigurationAsync(pkiRoot).ConfigureAwait(false);
                await server.StartAsync(pkiRoot).ConfigureAwait(false);
                Assert.That(watchedDuringCreation, Is.False);
                clientFixture = new ClientFixture(NUnitTelemetryContext.Create());
                await clientFixture.LoadClientConfigurationAsync(pkiRoot).ConfigureAwait(false);
                ConfiguredEndpoint endpoint = await clientFixture.GetEndpointAsync(
                    new Uri("opc.tcp://localhost:" + server.Port.ToString(CultureInfo.InvariantCulture)),
                    SecurityPolicies.None).ConfigureAwait(false);
                session = await clientFixture.ConnectAsync(endpoint).ConfigureAwait(false);
                ushort networkNamespace = session.NamespaceUris.GetIndexOrAppend(NetworkModelNodeManager.NamespaceUri);
                foreach (NodeId parent in parents)
                {
                    BrowseResponse initial = await session.BrowseAsync(null, null, 0,
                        [
                            new BrowseDescription
                            {
                                NodeId = parent,
                                ReferenceTypeId = ReferenceTypeIds.References,
                                BrowseDirection = BrowseDirection.Both,
                                IncludeSubtypes = true,
                                ResultMask = (uint)BrowseResultMask.All
                            }
                        ], cancellationToken).ConfigureAwait(false);
                    Assert.That(initial.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(initial.Results[0].ContinuationPoint.IsEmpty, Is.True);
                    publishedReferences[parent] = initial.Results[0].References.ToList()
                        .Select(reference => reference.NodeId)
                        .Where(id =>
                            ExpandedNodeId.ToNodeId(id, session.NamespaceUris).NamespaceIndex == networkNamespace)
                        .ToArray();
                }
                Assert.That(publishedReferences[ObjectIds.NetworkInterfaces], Has.Length.EqualTo(2));
                Assert.That(publishedReferences[ObjectTypeIds.IIetfBaseNetworkInterfaceType], Is.Not.Empty);
                Assert.That(publishedReferences[ObjectTypeIds.IIeeeBaseEthernetPortType], Is.Not.Empty);
                watchRelease.TrySetResult(true);
                await delivered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                foreach ((NodeId parent, ExpandedNodeId[] removed) in publishedReferences)
                {
                    BrowseResponse remaining = await session.BrowseAsync(null, null, 0,
                        [
                            new BrowseDescription
                            {
                                NodeId = parent,
                                ReferenceTypeId = ReferenceTypeIds.References,
                                BrowseDirection = BrowseDirection.Both,
                                IncludeSubtypes = true,
                                ResultMask = (uint)BrowseResultMask.All
                            }
                        ], cancellationToken).ConfigureAwait(false);
                    Assert.That(remaining.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(remaining.Results[0].ContinuationPoint.IsEmpty, Is.True);
                    Assert.That(remaining.Results[0].References.ToList()
                        .Select(reference => reference.NodeId).Intersect(removed), Is.Empty);
                }
                NetworkModelNodeManager manager = server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                    .OfType<NetworkModelNodeManager>().Single();
                await manager.OnServerReadyAsync(cancellationToken).ConfigureAwait(false);
                Assert.That(boundary.WatchCount, Is.EqualTo(1));
            }
            finally
            {
                if (session is not null)
                {
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                if (clientFixture is not null)
                {
                    await clientFixture.DisposeAsync().ConfigureAwait(false);
                }
                await server.StopAsync().ConfigureAwait(false);
                if (Directory.Exists(pkiRoot))
                {
                    Directory.Delete(pkiRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Aborted private replacements do not mutate diagnostics-owned LLDP values or optional properties.
        /// </summary>
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        [CancelAfter(60_000)]
        public async Task AbortedPreparationPreservesLiveLldpAsync(
            bool liveCapabilities, bool candidateCapabilities, bool initialFailure, CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NetworkLldpSnapshot lldp = initial.Lldp ?? throw new AssertionException("The scenario requires LLDP.");
            DateTime timestamp = initial.Timestamp.AddSeconds(1);
            m_provider.Publish(initial with
            {
                Timestamp = timestamp,
                Lldp = lldp with
                {
                    SystemCapabilitiesSupported = liveCapabilities ? 4u : null,
                    SystemCapabilitiesEnabled = liveCapabilities ? 4u : null
                }
            });
            await WaitForAsync(async () =>
            {
                ArrayOf<NetworkParameterValue> values = await m_client.ReadParametersAsync(
                    ObjectIds.LLDP_LocalSystemData, cancellationToken).ConfigureAwait(false);
                return values.Count == (liveCapabilities ? 6 : 4) &&
                    values.ToList().All(value =>
                    value.DataValue.SourceTimestamp == (DateTimeUtc)timestamp) &&
                    values.ToList().Any(value =>
                        value.BrowseName == new QualifiedName(BrowseNames.SystemCapabilitiesSupported, 0)) ==
                    liveCapabilities &&
                    values.ToList().Any(value =>
                        value.BrowseName == new QualifiedName(BrowseNames.SystemCapabilitiesEnabled, 0)) ==
                    liveCapabilities;
            }, cancellationToken).ConfigureAwait(false);

            ArrayOf<NetworkParameterValue> before = await m_client.ReadParametersAsync(
                ObjectIds.LLDP_LocalSystemData, cancellationToken).ConfigureAwait(false);
            LldpLocalSystemState local = m_server.Server.CurrentInstance.DiagnosticsNodeManager
                .FindPredefinedNode<LldpLocalSystemState>(ObjectIds.LLDP_LocalSystemData)
                ?? throw new AssertionException("The live LLDP local system is missing.");
            PropertyState<uint>? supported = local.SystemCapabilitiesSupported;
            PropertyState<uint>? enabled = local.SystemCapabilitiesEnabled;
            var candidate = new FaultInjectingProvider(m_provider)
            {
                ReadTransform = snapshot => initialFailure
                    ? throw new ServiceResultException(StatusCodes.BadNoCommunication)
                    : snapshot with
                    {
                        Timestamp = timestamp.AddSeconds(1),
                        Lldp = lldp with
                        {
                            ChassisId = "candidate-chassis",
                            SystemName = "candidate-system",
                            SystemDescription = "private candidate",
                            SystemCapabilitiesSupported = candidateCapabilities ? 8u : null,
                            SystemCapabilitiesEnabled = candidateCapabilities ? 8u : null
                        }
                    }
            };
            INodeManagerLifecycle lifecycle = m_server.Server.NodeManagerLifecycle;
            NodeManagerSlot slot = lifecycle.Registrations.ToList()
                .Single(registration => registration.NodeManager is NetworkModelNodeManager).Slot;
            long revision = lifecycle.Revision;
            await using ILifecycleWriter writer = await lifecycle.BeginAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await using (IPreparedChanges prepared = await writer.PrepareAsync(
                [
                    LifecycleChange.Remove(slot),
                    LifecycleChange.Add(new NetworkModelNodeManagerFactory(candidate, _ => false))
                ],
                cancellationToken).ConfigureAwait(false))
            {
                Assert.That(prepared.IsCommitted, Is.False);
                await AssertLiveLldpUnchangedAsync().ConfigureAwait(false);
            }
            await AssertLiveLldpUnchangedAsync().ConfigureAwait(false);
            Assert.That(lifecycle.Revision, Is.EqualTo(revision));
            Assert.That(candidate.WatchCount, Is.Zero);

            async Task AssertLiveLldpUnchangedAsync()
            {
                ArrayOf<NetworkParameterValue> after = await m_client.ReadParametersAsync(
                    ObjectIds.LLDP_LocalSystemData, cancellationToken).ConfigureAwait(false);
                Assert.That(after.ToList().Select(value => value.NodeId),
                    Is.EquivalentTo(before.ToList().Select(value => value.NodeId)));
                foreach (NetworkParameterValue expected in before.ToList())
                {
                    NetworkParameterValue actual = after.ToList().Single(value => value.NodeId == expected.NodeId);
                    Assert.Multiple(() =>
                    {
                        Assert.That(actual.DataValue.WrappedValue, Is.EqualTo(expected.DataValue.WrappedValue));
                        Assert.That(actual.DataValue.StatusCode, Is.EqualTo(expected.DataValue.StatusCode));
                        Assert.That(actual.DataValue.SourceTimestamp, Is.EqualTo(expected.DataValue.SourceTimestamp));
                    });
                }
                Assert.That(local.SystemCapabilitiesSupported, Is.SameAs(supported));
                Assert.That(local.SystemCapabilitiesEnabled, Is.SameAs(enabled));
            }
        }

        /// <summary>
        /// Reads and sampling use the complete acquisition even while the separate attribute cache is inconsistent.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [CancelAfter(60_000)]
        public async Task ReadsAndSamplingUseCoherentAcquisitionInsteadOfAttributeCache(
            bool lldp, CancellationToken cancellationToken)
        {
            NetworkModelSnapshot snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NetworkParameterValue parameter = lldp
                ? Parameter(await m_client.ReadParametersAsync(ObjectIds.LLDP_LocalSystemData, cancellationToken)
                    .ConfigureAwait(false), BrowseNames.SystemName)
                : (await m_client.ReadInterfaceAsync(
                    await FindInterfaceAsync("eth0", cancellationToken).ConfigureAwait(false), cancellationToken)
                    .ConfigureAwait(false)).Speed;
            BaseVariableState variable = lldp
                ? m_server.Server.CurrentInstance.NodeManager.DiagnosticsNodeManager!
                    .FindPredefinedNode<BaseVariableState>(parameter.NodeId)
                : m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                    .OfType<NetworkModelNodeManager>().Single()
                    .FindPredefinedNode<BaseVariableState>(parameter.NodeId);

            var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            DateTime timestamp = snapshot.Timestamp.AddSeconds(1);
            Variant expected = lldp ? new Variant("updated-host") : new Variant(100_000_000UL);
            m_providerBoundary.AfterDelivery = current =>
            {
                if (current.Timestamp == timestamp)
                {
                    delivered.TrySetResult(true);
                }
            };
            m_provider.Publish(snapshot with
            {
                Timestamp = timestamp,
                StatusCode = StatusCodes.UncertainDataSubNormal,
                Interfaces = ArrayOf.Wrapped(snapshot.Interfaces.ToList()
                    .Select(networkInterface => networkInterface with { Speed = 100_000_000 }).ToArray()),
                Lldp = snapshot.Lldp! with { SystemName = "updated-host" }
            });
            await delivered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            variable.Value = lldp ? new Variant("partial-cache") : new Variant(10_000_000UL);
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = snapshot.Timestamp;
            ReadResponse read = await m_session.ReadAsync(null, 0, TimestampsToReturn.Both,
                [new ReadValueId { NodeId = parameter.NodeId, AttributeId = Attributes.Value }],
                cancellationToken).ConfigureAwait(false);
            Assert.That(read.Results[0].WrappedValue, Is.EqualTo(expected));
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(read.Results[0].SourceTimestamp, Is.EqualTo((DateTimeUtc)timestamp));

            CreateSubscriptionResponse subscription = await m_session.CreateSubscriptionAsync(
                null!, 20, 3000, 10, 0, true, 0, cancellationToken).ConfigureAwait(false);
            CreateMonitoredItemsResponse created = await m_session.CreateMonitoredItemsAsync(
                null!, subscription.SubscriptionId, TimestampsToReturn.Both,
                [
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId { NodeId = parameter.NodeId, AttributeId = Attributes.Value },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1, SamplingInterval = 0, QueueSize = 10, DiscardOldest = true
                        }
                    }
                ], cancellationToken).ConfigureAwait(false);
            Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            DataValue sampled = await PublishAsync(subscription.SubscriptionId, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(sampled.WrappedValue, Is.EqualTo(expected));
            Assert.That(sampled.StatusCode, Is.EqualTo(StatusCodes.UncertainDataSubNormal));
            Assert.That(sampled.SourceTimestamp, Is.EqualTo((DateTimeUtc)timestamp));
        }

        /// <summary>
        /// Discovers the standard entry points and inherited mappings with wire-level values.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task StandardEntryPointsInterfacesUnitsAndInheritedMappingAreDiscoverable(
            CancellationToken cancellationToken)
        {
            ArrayOf<ReferenceDescription> resources = await m_client.BrowseObjectsAsync(
                ObjectIds.Resources, cancellationToken).ConfigureAwait(false);
            Assert.That(resources.ToList().Select(reference => reference.NodeId),
                Does.Contain(new ExpandedNodeId(ObjectIds.Communication)));
            ArrayOf<NodeId> interfaces = await m_client.DiscoverInterfacesAsync(cancellationToken)
                .ConfigureAwait(false);
            Assert.That(interfaces.Count, Is.EqualTo(2));
            ArrayOf<NodeId> tables = await m_client.DiscoverMappingTablesAsync(cancellationToken)
                .ConfigureAwait(false);
            Assert.That(tables.Count, Is.EqualTo(1));
            foreach (NodeId id in interfaces.ToList())
            {
                NetworkInterfaceValues values = await m_client.ReadInterfaceAsync(id, cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(values.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(values.Speed.DataValue.WrappedValue, Is.EqualTo(new Variant(1_000_000_000UL)));
                Assert.That(values.Speed.DataValue.SourceTimestamp,
                    Is.EqualTo((DateTimeUtc)(await m_provider.ReadAsync(cancellationToken)
                        .ConfigureAwait(false)).Timestamp));
                Assert.That((await m_client.GetEffectiveMappingTablesAsync(id, cancellationToken)
                    .ConfigureAwait(false)).ToArray(), Is.EqualTo(tables.ToArray()));
                ArrayOf<NetworkParameterValue> units = await m_client.ReadParametersAsync(
                    values.Speed.NodeId, cancellationToken).ConfigureAwait(false);
                NetworkParameterValue engineeringUnits = units.ToList()
                    .Single(value => value.BrowseName == new QualifiedName(BrowseNames.EngineeringUnits, 0));
                Assert.That(engineeringUnits.DataValue.WrappedValue
                    .TryGetStructure<EUInformation>(out EUInformation? unit), Is.True);
                if (unit is null)
                {
                    throw new InvalidOperationException("Decoded IETF engineering units are null.");
                }
                Assert.That(unit.DisplayName.Text, Is.EqualTo("bit/s"));
                Assert.That(unit.UnitId, Is.EqualTo(4337968));
                Assert.That(unit.NamespaceUri,
                    Is.EqualTo("http://www.opcfoundation.org/UA/units/un/cefact"));
            }
            ArrayOf<NodeId> talkers = await m_client.DiscoverStreamsAsync(true, cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<NodeId> listeners = await m_client.DiscoverStreamsAsync(false, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(talkers.Count, Is.EqualTo(1));
            Assert.That(listeners.Count, Is.EqualTo(1));
            NetworkStreamValues stream = await m_client.ReadStreamAsync(talkers[0], cancellationToken)
                .ConfigureAwait(false);
            Assert.That(stream.StreamId.DataValue.WrappedValue.TryGetValue(out ByteString identity), Is.True);
            Assert.That(identity.Length, Is.EqualTo(8));
            Assert.That(stream.State.DataValue.WrappedValue, Is.EqualTo(new Variant((int)TsnStreamState.Disabled)));
        }

        /// <summary>
        /// Maintains inverse mapping and lower-layer references when provider relationships change.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task MappingAndLowerLayerReferencesRemainBidirectionalAcrossUpdates(
            CancellationToken cancellationToken)
        {
            ArrayOf<ReferenceDescription> interfaces = await m_client.BrowseObjectsAsync(
                ObjectIds.NetworkInterfaces, cancellationToken).ConfigureAwait(false);
            NodeId physical = ExpandedNodeId.ToNodeId(interfaces.ToList()
                .Single(value => value.BrowseName.Name == "eth0").NodeId, m_session.NamespaceUris);
            NodeId vlan = ExpandedNodeId.ToNodeId(interfaces.ToList()
                .Single(value => value.BrowseName.Name == "eth0.2").NodeId, m_session.NamespaceUris);
            NodeId table = (await m_client.DiscoverMappingTablesAsync(cancellationToken).ConfigureAwait(false))[0];
            ArrayOf<BrowseDescription> requests =
            [
                new BrowseDescription
                {
                    NodeId = table,
                    ReferenceTypeId = ReferenceTypeIds.UsesPriorityMappingTable,
                    BrowseDirection = BrowseDirection.Inverse,
                    NodeClassMask = (uint)NodeClass.Object,
                    ResultMask = (uint)BrowseResultMask.All
                },
                new BrowseDescription
                {
                    NodeId = physical,
                    ReferenceTypeId = ReferenceTypeIds.HasLowerLayerInterface,
                    BrowseDirection = BrowseDirection.Inverse,
                    NodeClassMask = (uint)NodeClass.Object,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ];
            BrowseResponse initial = await m_session.BrowseAsync(
                null, null, 0, requests, cancellationToken).ConfigureAwait(false);
            Assert.That(initial.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(initial.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(initial.Results[0].References.ToList().Select(reference => reference.NodeId),
                Is.EqualTo(new[] { new ExpandedNodeId(physical) }));
            Assert.That(initial.Results[1].References.ToList().Select(reference => reference.NodeId),
                Is.EqualTo(new[] { new ExpandedNodeId(vlan) }));

            NetworkModelSnapshot snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            m_provider.Publish(snapshot with
            {
                Timestamp = snapshot.Timestamp.AddSeconds(1),
                Interfaces = ArrayOf.Wrapped(snapshot.Interfaces.ToList().Select(networkInterface =>
                    networkInterface with { MappingTableId = null, LowerLayerIds = [] }).ToArray())
            });
            await WaitForAsync(async () =>
            {
                BrowseResponse removed = await m_session.BrowseAsync(
                    null, null, 0, requests, cancellationToken).ConfigureAwait(false);
                return removed.Results[0].References.Count == 0 && removed.Results[1].References.Count == 0;
            }, cancellationToken).ConfigureAwait(false);
            Assert.That((await m_client.GetEffectiveMappingTablesAsync(physical, cancellationToken)
                .ConfigureAwait(false)).Count, Is.Zero);
            Assert.That((await m_client.GetEffectiveMappingTablesAsync(vlan, cancellationToken)
                .ConfigureAwait(false)).Count, Is.Zero);

            m_provider.Publish(snapshot with
            {
                Revision = snapshot.Revision + 1,
                Timestamp = snapshot.Timestamp.AddSeconds(2)
            });
            await WaitForAsync(async () =>
            {
                BrowseResponse restored = await m_session.BrowseAsync(
                    null, null, 0, requests, cancellationToken).ConfigureAwait(false);
                return restored.Results[0].References.Count == 1 && restored.Results[1].References.Count == 1;
            }, cancellationToken).ConfigureAwait(false);
            Assert.That((await m_client.GetEffectiveMappingTablesAsync(vlan, cancellationToken)
                .ConfigureAwait(false)).ToArray(), Is.EqualTo(new[] { table }));
        }

        /// <summary>
        /// Enforces authorization and normative edit outcomes through actual Call services.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task MappingMethodsEnforcePolicyRangesDuplicatesAndProviderConfirmation(
            CancellationToken cancellationToken)
        {
            NodeId table = (await m_client.DiscoverMappingTablesAsync(cancellationToken).ConfigureAwait(false))[0];
            const string uri = "urn:network:integration";
            CallMethodResult denied = await m_client.AddPriorityMappingEntryAsync(
                table, uri, "Control", 7, 63, cancellationToken).ConfigureAwait(false);
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That((await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false)).MappingTables[0]
                .Entries.Count, Is.EqualTo(1));

            Volatile.Write(ref m_permitMappingEdits, 1);
            CallMethodResult added = await m_client.AddPriorityMappingEntryAsync(
                table, uri, "Control", 7, 63, cancellationToken).ConfigureAwait(false);
            Assert.That(added.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That((await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false)).MappingTables[0]
                .Entries.Count, Is.EqualTo(2));
            await WaitForAsync(async () =>
                (await m_client.ReadMappingTableAsync(table, cancellationToken).ConfigureAwait(false))
                    .Entries.Count == 2, cancellationToken).ConfigureAwait(false);
            NetworkMappingValues published = await m_client.ReadMappingTableAsync(table, cancellationToken)
                .ConfigureAwait(false);
            PriorityMappingEntryType publishedEntry = published.Entries.ToList()
                .Single(entry => entry.MappingUri == uri);
            Assert.That(publishedEntry.PriorityLabel, Is.EqualTo("Control"));
            Assert.That(publishedEntry.PriorityValue_PCP, Is.EqualTo(7));
            Assert.That(publishedEntry.PriorityValue_DSCP, Is.EqualTo(63));
            CallMethodResult duplicate = await m_client.AddPriorityMappingEntryAsync(
                table, uri, "Control", 7, 63, cancellationToken).ConfigureAwait(false);
            Assert.That(duplicate.StatusCode, Is.EqualTo(StatusCodes.BadIndexRangeInvalid));
            CallMethodResult invalid = await m_client.AddPriorityMappingEntryAsync(
                table, uri, "Invalid", 8, 64, cancellationToken).ConfigureAwait(false);
            Assert.That(invalid.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            CallMethodResult deleted = await m_client.DeletePriorityMappingEntryAsync(
                table, uri, "Control", cancellationToken).ConfigureAwait(false);
            Assert.That(deleted.StatusCode, Is.EqualTo(StatusCodes.Good));
            CallMethodResult missing = await m_client.DeletePriorityMappingEntryAsync(
                table, uri, "Control", cancellationToken).ConfigureAwait(false);
            Assert.That(missing.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
            Assert.That((await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false)).MappingTables[0]
                .Entries.Count, Is.EqualTo(1));
            await WaitForAsync(async () =>
                (await m_client.ReadMappingTableAsync(table, cancellationToken).ConfigureAwait(false))
                    .Entries.Count == 1, cancellationToken).ConfigureAwait(false);
            Assert.That((await m_client.ReadMappingTableAsync(table, cancellationToken).ConfigureAwait(false))
                .Entries[0].MappingUri, Is.EqualTo("urn:opcfoundation:network-sample:priority"));
        }

        /// <summary>
        /// Publishes stream failure matrices and adds and removes LLDP neighbors.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task SampleTransitionsExposeStreamFailureAndLldpAppearanceAndRemoval(
            CancellationToken cancellationToken)
        {
            NetworkModelNodeManager manager = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single();
            NodeId talker = (await m_client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false))[0];
            NodeId listener = (await m_client.DiscoverStreamsAsync(false, cancellationToken).ConfigureAwait(false))[0];
            NodeId port = (await m_client.DiscoverLldpPortsAsync(cancellationToken).ConfigureAwait(false))[0];
            Assert.That((await m_client.DiscoverNeighborsAsync(port, cancellationToken).ConfigureAwait(false)).Count,
                Is.Zero);
            for (int step = 0; step < 4; step++)
            {
                NetworkModelSnapshot snapshot = NetworkScenario.Advance(
                    await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false), step);
                m_provider.Publish(snapshot);
                await WaitForAsync(async () =>
                {
                    NetworkStreamValues values = await m_client.ReadStreamAsync(talker, cancellationToken)
                        .ConfigureAwait(false);
                    return values.State.DataValue.WrappedValue == new Variant((int)snapshot.Streams[0].State) &&
                        values.State.DataValue.SourceTimestamp == (DateTimeUtc)snapshot.Timestamp;
                }, cancellationToken).ConfigureAwait(false);
                await WaitForAsync(async () =>
                    (await m_client.ReadStreamAsync(listener, cancellationToken).ConfigureAwait(false))
                        .State.DataValue.SourceTimestamp == (DateTimeUtc)snapshot.Timestamp,
                    cancellationToken).ConfigureAwait(false);
                NetworkStreamValues listenerValues = await m_client.ReadStreamAsync(listener, cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(listenerValues.State.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(listenerValues.State.DataValue.WrappedValue,
                    Is.EqualTo(Variant.From(snapshot.Streams[1].State)));
                int expectedNeighbors = step is 1 or 3 ? 1 : 0;
                await WaitForAsync(async () =>
                    (await m_client.DiscoverNeighborsAsync(port, cancellationToken).ConfigureAwait(false)).Count ==
                        expectedNeighbors, cancellationToken).ConfigureAwait(false);
                foreach ((NodeId id, bool isTalker) in new[] { (talker, true), (listener, false) })
                {
                    ArrayOf<NetworkParameterValue> status = await ReadParameterSetAsync(
                        id, ObjectTypeIds.IIeeeBaseTsnStatusStreamType, cancellationToken).ConfigureAwait(false);
                    Assert.That(Parameter(status, BrowseNames.FailureCode).DataValue.WrappedValue,
                        Is.EqualTo(Variant.From(step == 2
                            ? TsnFailureCode.InsufficientBandwidth : TsnFailureCode.NoFailure)));
                    Variant expectedStatus = isTalker
                        ? Variant.From(step == 2 ? TsnTalkerStatus.Failed : TsnTalkerStatus.Ready)
                        : Variant.From(step == 2 ? TsnListenerStatus.Failed : TsnListenerStatus.Ready);
                    Assert.That(Parameter(status, isTalker ? BrowseNames.TalkerStatus : BrowseNames.ListenerStatus)
                        .DataValue.WrappedValue, Is.EqualTo(expectedStatus));
                    NetworkParameterValue failure = Parameter(status, BrowseNames.FailureSystemIdentifier);
                    BaseVariableState local = manager.FindPredefinedNode<BaseVariableState>(failure.NodeId);
                    Assert.That(local.Value.TryGetValue(out MatrixOf<byte> localIdentifiers), Is.True);
                    Assert.That(localIdentifiers.Dimensions, Is.EqualTo(new[] { step == 2 ? 1 : 0, 8 }));
                    Assert.That(local.ArrayDimensions.ToArray(), Is.EqualTo(new uint[] { step == 2 ? 1u : 0u, 8 }));
                    Assert.That(failure.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(failure.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)snapshot.Timestamp));
                    if (step == 2)
                    {
                        Assert.That(failure.DataValue.WrappedValue.TryGetValue(out MatrixOf<byte> identifiers),
                            Is.True);
                        int[] dimensions = identifiers.Dimensions;
                        Assert.That(dimensions, Has.Length.EqualTo(2));
                        Assert.That(dimensions[0], Is.EqualTo(1));
                        Assert.That(dimensions[1], Is.EqualTo(8));
                        Assert.That(identifiers.Span.ToArray(),
                            Is.EqualTo(new byte[] { 2, 0, 0, 0, 0, 2, 0, 1 }));
                    }
                    else
                    {
                        Assert.That(localIdentifiers.Span.Length, Is.Zero);
                        Assert.That(failure.DataValue.WrappedValue.TryGetValue(out ArrayOf<byte> identifiers),
                            Is.True);
                        Assert.That(identifiers.Count, Is.Zero);
                    }
                }
            }
        }

        /// <summary>
        /// Preserves declared parameter interfaces, engineering units and array shapes.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task ParameterSetsExposeStandardInterfaceContractsAndWireShapes(
            CancellationToken cancellationToken)
        {
            ArrayOf<ReferenceDescription> interfaces = await m_client.BrowseObjectsAsync(
                ObjectIds.NetworkInterfaces, cancellationToken).ConfigureAwait(false);
            NodeId physical = ExpandedNodeId.ToNodeId(interfaces.ToList()
                .Single(reference => reference.BrowseName.Name == "eth0").NodeId, m_session.NamespaceUris);
            NodeId vlan = ExpandedNodeId.ToNodeId(interfaces.ToList()
                .Single(reference => reference.BrowseName.Name == "eth0.2").NodeId, m_session.NamespaceUris);
            ArrayOf<NetworkParameterValue> ethernet = await ReadParameterSetAsync(
                physical, ObjectTypeIds.IIeeeBaseEthernetPortType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue speed = Parameter(ethernet, BrowseNames.Speed);
            Assert.That(speed.DataValue.WrappedValue, Is.EqualTo(new Variant(1000UL)));
            Assert.That(Parameter(ethernet, BrowseNames.Duplex).DataValue.WrappedValue,
                Is.EqualTo(new Variant((int)Duplex.Full)));
            Assert.That(Parameter(ethernet, BrowseNames.MaxFrameLength).DataValue.WrappedValue,
                Is.EqualTo(new Variant((ushort)1522)));
            ArrayOf<NetworkParameterValue> units = await m_client.ReadParametersAsync(
                speed.NodeId, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(units, BrowseNames.EngineeringUnits).DataValue.WrappedValue
                .TryGetStructure<EUInformation>(out EUInformation? unit), Is.True);
            if (unit is null)
            {
                throw new InvalidOperationException("Decoded IEEE engineering units are null.");
            }
            Assert.That(unit.UnitId, Is.EqualTo(4534832));
            Assert.That(unit.DisplayName.Text, Is.EqualTo("Mbit/s"));
            ArrayOf<NetworkParameterValue> negotiation = await ReadParameterSetAsync(
                physical, ObjectTypeIds.IIeeeAutoNegotiationStatusType, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(negotiation, BrowseNames.NegotiationStatus).DataValue.WrappedValue,
                Is.EqualTo(new Variant((int)NegotiationStatus.Complete)));
            ArrayOf<NetworkParameterValue> capabilities = await ReadParameterSetAsync(
                physical, ObjectTypeIds.IBaseEthernetCapabilitiesType, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(capabilities, BrowseNames.VlanTagCapable).DataValue.WrappedValue,
                Is.EqualTo(new Variant(true)));
            ArrayOf<NetworkParameterValue> vlanValues = await ReadParameterSetAsync(
                vlan, ObjectTypeIds.IVlanIdType, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(vlanValues, BrowseNames.VlanId).DataValue.WrappedValue,
                Is.EqualTo(new Variant((ushort)2)));
            ArrayOf<NetworkParameterValue> srClass = await ReadParameterSetAsync(
                physical, ObjectTypeIds.ISrClassType, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(srClass, BrowseNames.Id).DataValue.WrappedValue, Is.EqualTo(new Variant((byte)6)));
            Assert.That(Parameter(srClass, BrowseNames.Priority).DataValue.WrappedValue,
                Is.EqualTo(new Variant((byte)3)));
            Assert.That(Parameter(srClass, BrowseNames.Vid).DataValue.WrappedValue,
                Is.EqualTo(new Variant((ushort)2)));

            NodeId talker = (await m_client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false))[0];
            ArrayOf<NetworkParameterValue> traffic = await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeBaseTsnTrafficSpecificationType, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(traffic, BrowseNames.MaxIntervalFrames).DataValue.WrappedValue,
                Is.EqualTo(new Variant((ushort)1)));
            Assert.That(Parameter(traffic, BrowseNames.MaxFrameSize).DataValue.WrappedValue,
                Is.EqualTo(new Variant(512u)));
            Assert.That(Parameter(traffic, BrowseNames.Interval).DataValue.WrappedValue
                .TryGetStructure<UnsignedRationalNumber>(out UnsignedRationalNumber? interval), Is.True);
            if (interval is null)
            {
                throw new InvalidOperationException("Decoded TSN traffic interval is null.");
            }
            Assert.That(interval.Numerator, Is.EqualTo(1));
            Assert.That(interval.Denominator, Is.EqualTo(8000));
            ArrayOf<NetworkParameterValue> mac = await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeTsnMacAddressType, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(mac, BrowseNames.DestinationAddress).DataValue.WrappedValue
                .TryGetValue(out ArrayOf<byte> destination), Is.True);
            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 145, 224, 240, 0, 0, 1 }));
            Assert.That(Parameter(mac, BrowseNames.SourceAddress).DataValue.WrappedValue
                .TryGetValue(out ArrayOf<byte> source), Is.True);
            Assert.That(source.ToArray(), Is.EqualTo(new byte[] { 2, 0, 0, 0, 0, 1 }));
            ArrayOf<NetworkParameterValue> tag = await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeTsnVlanTagType, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(tag, BrowseNames.VlanId).DataValue.WrappedValue,
                Is.EqualTo(new Variant((ushort)2)));
            Assert.That(Parameter(tag, BrowseNames.PriorityCodePoint).DataValue.WrappedValue,
                Is.EqualTo(new Variant((byte)3)));
            ArrayOf<NetworkParameterValue> talkerInterface = await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeTsnInterfaceConfigurationTalkerType, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(Parameter(talkerInterface, BrowseNames.TimeAwareOffset).DataValue.WrappedValue,
                Is.EqualTo(new Variant(100u)));
            NodeId listener = (await m_client.DiscoverStreamsAsync(false, cancellationToken).ConfigureAwait(false))[0];
            ArrayOf<NetworkParameterValue> listenerInterface = await ReadParameterSetAsync(
                listener, ObjectTypeIds.IIeeeTsnInterfaceConfigurationListenerType, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(Parameter(listenerInterface, BrowseNames.ReceiveOffset).DataValue.WrappedValue,
                Is.EqualTo(new Variant(100u)));
        }

        /// <summary>
        /// Delivers acquired values, timestamps and quality through real Publish notifications.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task ProviderChangesReachRealPublishWithSourceTimestampAndQuality(
            CancellationToken cancellationToken)
        {
            ArrayOf<ReferenceDescription> published = await m_client.BrowseObjectsAsync(
                ObjectIds.NetworkInterfaces, cancellationToken).ConfigureAwait(false);
            NodeId networkInterface = ExpandedNodeId.ToNodeId(published.ToList()
                .Single(value => value.BrowseName.Name == "eth0").NodeId, m_session.NamespaceUris);
            NetworkInterfaceValues initial = await m_client.ReadInterfaceAsync(networkInterface, cancellationToken)
                .ConfigureAwait(false);
            CreateSubscriptionResponse subscription = await m_session.CreateSubscriptionAsync(
                null!, 20, 3000, 10, 0, true, 0, cancellationToken).ConfigureAwait(false);
            CreateMonitoredItemsResponse created = await m_session.CreateMonitoredItemsAsync(
                null!, subscription.SubscriptionId, TimestampsToReturn.Both,
                [
                    new MonitoredItemCreateRequest
                    {
                        ItemToMonitor = new ReadValueId
                        {
                            NodeId = initial.Speed.NodeId,
                            AttributeId = Attributes.Value
                        },
                        MonitoringMode = MonitoringMode.Reporting,
                        RequestedParameters = new MonitoringParameters
                        {
                            ClientHandle = 1, SamplingInterval = 0, QueueSize = 10, DiscardOldest = true
                        }
                    }
                ], cancellationToken).ConfigureAwait(false);
            Assert.That(created.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            await PublishAsync(subscription.SubscriptionId, cancellationToken).ConfigureAwait(false);
            NetworkModelSnapshot snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NetworkInterfaceSnapshot[] interfaces = snapshot.Interfaces.Span.ToArray();
            for (int index = 0; index < interfaces.Length; index++)
            {
                interfaces[index] = interfaces[index] with
                {
                    Speed = 100_000_000,
                    Ethernet = null,
                    PhysicalAddress = null,
                    VlanTagCapable = null,
                    SrClasses = []
                };
            }
            DateTime timestamp = snapshot.Timestamp.AddSeconds(1);
            m_provider.Publish(snapshot with { Timestamp = timestamp, Interfaces = ArrayOf.Wrapped(interfaces) });
            DataValue changed;
            do
            {
                changed = await PublishAsync(subscription.SubscriptionId, cancellationToken).ConfigureAwait(false);
            }
            while (changed.WrappedValue != new Variant(100_000_000UL));
            Assert.That(changed.SourceTimestamp, Is.EqualTo((DateTimeUtc)timestamp));
            Assert.That(changed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That((await m_client.ReadInterfaceAsync(networkInterface, cancellationToken)
                .ConfigureAwait(false)).PhysicalAddress.DataValue.StatusCode,
                Is.EqualTo(StatusCodes.BadNotSupported));

            m_provider.Publish(snapshot with
            {
                Revision = snapshot.Revision + 1,
                Timestamp = timestamp.AddSeconds(1)
            });
            do
            {
                changed = await PublishAsync(subscription.SubscriptionId, cancellationToken).ConfigureAwait(false);
            }
            while (changed.WrappedValue != new Variant(1_000_000_000UL));
            Assert.That(changed.SourceTimestamp, Is.EqualTo((DateTimeUtc)timestamp.AddSeconds(1)));
            Assert.That(changed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That((await m_client.ReadInterfaceAsync(networkInterface, cancellationToken)
                .ConfigureAwait(false)).PhysicalAddress.DataValue.WrappedValue,
                Is.EqualTo(new Variant("02:00:00:00:00:01")));

            m_provider.Publish(snapshot with
            {
                Revision = snapshot.Revision + 2,
                Timestamp = timestamp.AddSeconds(2),
                StatusCode = StatusCodes.BadCommunicationError
            });
            do
            {
                changed = await PublishAsync(subscription.SubscriptionId, cancellationToken).ConfigureAwait(false);
            }
            while (!StatusCode.IsBad(changed.StatusCode));
            Assert.That(changed.StatusCode, Is.EqualTo(StatusCodes.BadCommunicationError));
            NodeId table = (await m_client.DiscoverMappingTablesAsync(cancellationToken).ConfigureAwait(false))[0];
            BrowseResponse methods = await m_session.BrowseAsync(null, null, 0,
            [
                new BrowseDescription
                {
                    NodeId = table,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    BrowseDirection = BrowseDirection.Forward,
                    NodeClassMask = (uint)NodeClass.Method,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ], cancellationToken).ConfigureAwait(false);
            Assert.That(methods.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            NodeId addMethod = ExpandedNodeId.ToNodeId(methods.Results[0].References.ToList()
                .Single(value => value.BrowseName == new QualifiedName(BrowseNames.AddPriorityMappingEntry, 0))
                .NodeId, m_session.NamespaceUris);
            ArrayOf<NetworkParameterValue> metadata = await m_client.ReadParametersAsync(
                addMethod, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue arguments = Parameter(metadata, BrowseNames.InputArguments);
            Assert.That(arguments.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(arguments.DataValue.WrappedValue.TryGetStructure(out ArrayOf<Argument> declaration), Is.True);
            Assert.That(declaration.Count, Is.EqualTo(4));
        }

        /// <summary>
        /// Removes obsolete nodes from browsing and subsequent Read requests.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task RemovedInterfaceDisappearsAndOldNodeReturnsUnknown(CancellationToken cancellationToken)
        {
            ArrayOf<NodeId> interfaces = await m_client.DiscoverInterfacesAsync(cancellationToken)
                .ConfigureAwait(false);
            NodeId oldSpeed = (await m_client.ReadInterfaceAsync(interfaces[0], cancellationToken)
                .ConfigureAwait(false)).Speed.NodeId;
            NetworkModelSnapshot snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            m_provider.Publish(snapshot with { Timestamp = snapshot.Timestamp.AddSeconds(1), Interfaces = [] });

            await WaitForAsync(async () =>
                (await m_client.DiscoverInterfacesAsync(cancellationToken).ConfigureAwait(false)).Count == 0,
                cancellationToken).ConfigureAwait(false);
            ReadResponse response = await m_session.ReadAsync(null, 0, TimestampsToReturn.Both,
                [new ReadValueId { NodeId = oldSpeed, AttributeId = Attributes.Value }], cancellationToken)
                .ConfigureAwait(false);
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// Rejects older and repeated provider revisions before changing acquired values.
        /// </summary>
        [TestCase(-1L)]
        [TestCase(0L)]
        [CancelAfter(60_000)]
        public async Task StaleCustomProviderRevisionCannotReplacePublishedAcquisition(
            long revisionOffset, CancellationToken cancellationToken)
        {
            NodeId id = (await m_client.DiscoverInterfacesAsync(cancellationToken).ConfigureAwait(false))[0];
            NetworkInterfaceValues initial = await m_client.ReadInterfaceAsync(id, cancellationToken)
                .ConfigureAwait(false);
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            DateTime nextTimestamp = current.Timestamp.AddSeconds(1);
            var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_providerBoundary.WatchTransform = snapshot => snapshot with
            {
                Revision = current.Revision + revisionOffset
            };
            m_providerBoundary.AfterDelivery = snapshot =>
            {
                if (snapshot.Timestamp == nextTimestamp)
                {
                    delivered.TrySetResult(true);
                }
            };
            NetworkInterfaceSnapshot[] interfaces = current.Interfaces.Span.ToArray();
            for (int index = 0; index < interfaces.Length; index++)
            {
                interfaces[index] = interfaces[index] with { Speed = 100_000_000 };
            }

            m_provider.Publish(current with
            {
                Timestamp = nextTimestamp,
                Interfaces = ArrayOf.Wrapped(interfaces)
            });
            await delivered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            NetworkInterfaceValues actual = await m_client.ReadInterfaceAsync(id, cancellationToken)
                .ConfigureAwait(false);

            Assert.That(actual.Speed.DataValue.WrappedValue, Is.EqualTo(initial.Speed.DataValue.WrappedValue));
            Assert.That(actual.Speed.DataValue.SourceTimestamp, Is.EqualTo(initial.Speed.DataValue.SourceTimestamp));
            m_providerBoundary.WatchTransform = null;
            m_provider.Publish(current with
            {
                Revision = current.Revision + 1,
                Timestamp = nextTimestamp.AddSeconds(1),
                Interfaces = ArrayOf.Wrapped(interfaces)
            });
            await WaitForAsync(async () =>
                (await m_client.ReadInterfaceAsync(id, cancellationToken).ConfigureAwait(false))
                    .Speed.DataValue.WrappedValue == new Variant(100_000_000UL), cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Accepts the minimum initial revision and preserves revision and timestamp ordering thereafter.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task MinimumInitialRevisionPreservesSubsequentAcquisitionOrdering(
            CancellationToken cancellationToken)
        {
            string pkiRoot = Path.Combine(
                Path.GetTempPath(), "OpcUaNetworkTests", Guid.NewGuid().ToString("N")[..8]);
            ReferenceNetworkModelProvider provider = NetworkScenario.Create();
            NetworkModelSnapshot initial = await provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            var watchRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var providerBoundary = new FaultInjectingProvider(provider)
            {
                ReadTransform = snapshot => snapshot with { Revision = long.MinValue },
                WatchTransform = snapshot => snapshot with { Revision = long.MinValue },
                WatchRelease = watchRelease
            };
            ServerFixture<StandardServer>? server = null;
            ClientFixture? clientFixture = null;
            Client.ISession? session = null;
            try
            {
                server = new ServerFixture<StandardServer>(telemetry =>
                {
                    var standardServer = new StandardServer(telemetry);
                    standardServer.AddNodeManager(new NetworkModelNodeManagerFactory(
                        providerBoundary, _ => false));
                    return standardServer;
                })
                {
                    SecurityNone = true,
                    AutoAccept = true
                };
                await server.LoadConfigurationAsync(pkiRoot).ConfigureAwait(false);
                await server.StartAsync(pkiRoot).ConfigureAwait(false);
                clientFixture = new ClientFixture(NUnitTelemetryContext.Create());
                await clientFixture.LoadClientConfigurationAsync(pkiRoot).ConfigureAwait(false);
                ConfiguredEndpoint endpoint = await clientFixture.GetEndpointAsync(
                    new Uri("opc.tcp://localhost:" + server.Port.ToString(CultureInfo.InvariantCulture)),
                    SecurityPolicies.None).ConfigureAwait(false);
                session = await clientFixture.ConnectAsync(endpoint).ConfigureAwait(false);
                var client = new NetworkModelClient(session, NUnitTelemetryContext.Create());

                ArrayOf<NodeId> interfaces = await client.DiscoverInterfacesAsync(cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(interfaces.Count, Is.EqualTo(2));
                NodeId id = interfaces[0];
                NetworkInterfaceValues published = await client.ReadInterfaceAsync(id, cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(published.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(published.Speed.DataValue.WrappedValue, Is.EqualTo(new Variant(1_000_000_000UL)));
                Assert.That(published.Speed.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)initial.Timestamp));

                var replayed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                providerBoundary.AfterDelivery = _ => replayed.TrySetResult(true);
                watchRelease.TrySetResult(true);
                await replayed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

                async Task<NetworkInterfaceValues> DeliverAsync(long revision, DateTime timestamp, ulong speed)
                {
                    var delivered = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    NetworkModelSnapshot current = await provider.ReadAsync(cancellationToken).ConfigureAwait(false);
                    DateTime sourceTimestamp = current.Timestamp.AddSeconds(1);
                    providerBoundary.WatchTransform = snapshot => snapshot with
                    {
                        Revision = revision,
                        Timestamp = timestamp
                    };
                    providerBoundary.AfterDelivery = snapshot =>
                    {
                        if (snapshot.Revision == revision && snapshot.Timestamp == timestamp)
                        {
                            delivered.TrySetResult(true);
                        }
                    };
                    NetworkInterfaceSnapshot[] updated = current.Interfaces.Span.ToArray();
                    for (int index = 0; index < updated.Length; index++)
                    {
                        updated[index] = updated[index] with { Speed = speed };
                    }
                    provider.Publish(current with
                    {
                        Timestamp = sourceTimestamp,
                        Interfaces = ArrayOf.Wrapped(updated)
                    });
                    await delivered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return await client.ReadInterfaceAsync(id, cancellationToken).ConfigureAwait(false);
                }

                NetworkInterfaceValues repeatedMinimum = await DeliverAsync(
                    long.MinValue, initial.Timestamp.AddSeconds(1), 100_000_000).ConfigureAwait(false);
                Assert.That(repeatedMinimum.Speed.DataValue.WrappedValue,
                    Is.EqualTo(published.Speed.DataValue.WrappedValue));
                Assert.That(repeatedMinimum.Speed.DataValue.SourceTimestamp,
                    Is.EqualTo(published.Speed.DataValue.SourceTimestamp));
                Assert.That(repeatedMinimum.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));

                NetworkInterfaceValues staleMinimumTimestamp = await DeliverAsync(
                    long.MinValue + 1, initial.Timestamp.AddSeconds(-1), 100_000_000).ConfigureAwait(false);
                Assert.That(staleMinimumTimestamp.Speed.DataValue.WrappedValue,
                    Is.EqualTo(published.Speed.DataValue.WrappedValue));
                Assert.That(staleMinimumTimestamp.Speed.DataValue.SourceTimestamp,
                    Is.EqualTo(published.Speed.DataValue.SourceTimestamp));
                Assert.That(staleMinimumTimestamp.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));

                DateTime newerTimestamp = initial.Timestamp.AddSeconds(2);
                NetworkInterfaceValues newer = await DeliverAsync(
                    long.MinValue + 1, newerTimestamp, 100_000_000).ConfigureAwait(false);
                Assert.That(newer.Speed.DataValue.WrappedValue, Is.EqualTo(new Variant(100_000_000UL)));
                Assert.That(newer.Speed.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)newerTimestamp));
                Assert.That(newer.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));

                NetworkInterfaceValues older = await DeliverAsync(
                    long.MinValue, newerTimestamp.AddSeconds(1), 10_000_000).ConfigureAwait(false);
                Assert.That(older.Speed.DataValue.WrappedValue, Is.EqualTo(newer.Speed.DataValue.WrappedValue));
                Assert.That(older.Speed.DataValue.SourceTimestamp, Is.EqualTo(newer.Speed.DataValue.SourceTimestamp));
                Assert.That(older.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));

                NetworkInterfaceValues equal = await DeliverAsync(
                    long.MinValue + 1, newerTimestamp.AddSeconds(2), 10_000_000).ConfigureAwait(false);
                Assert.That(equal.Speed.DataValue.WrappedValue, Is.EqualTo(newer.Speed.DataValue.WrappedValue));
                Assert.That(equal.Speed.DataValue.SourceTimestamp, Is.EqualTo(newer.Speed.DataValue.SourceTimestamp));
                Assert.That(equal.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));

                NetworkInterfaceValues staleTimestamp = await DeliverAsync(
                    long.MinValue + 2, initial.Timestamp, 10_000_000).ConfigureAwait(false);
                Assert.That(staleTimestamp.Speed.DataValue.WrappedValue,
                    Is.EqualTo(newer.Speed.DataValue.WrappedValue));
                Assert.That(staleTimestamp.Speed.DataValue.SourceTimestamp,
                    Is.EqualTo(newer.Speed.DataValue.SourceTimestamp));
                Assert.That(staleTimestamp.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));

                NetworkInterfaceValues recovered = await DeliverAsync(
                    long.MinValue + 2, newerTimestamp, 10_000_000).ConfigureAwait(false);
                Assert.That(recovered.Speed.DataValue.WrappedValue, Is.EqualTo(new Variant(10_000_000UL)));
                Assert.That(recovered.Speed.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)newerTimestamp));
                Assert.That(recovered.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            finally
            {
                if (session is not null)
                {
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                if (clientFixture is not null)
                {
                    await clientFixture.DisposeAsync().ConfigureAwait(false);
                }
                if (server is not null)
                {
                    await server.StopAsync().ConfigureAwait(false);
                }
                if (Directory.Exists(pkiRoot))
                {
                    Directory.Delete(pkiRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Validates SR-class boundaries even when custom acquisitions bypass the reference provider.
        /// </summary>
        [TestCase((byte)0, (byte)0)]
        [TestCase((byte)7, (byte)0)]
        [TestCase((byte)8, (byte)0)]
        [TestCase(byte.MaxValue, (byte)0)]
        [TestCase((byte)6, (byte)0)]
        [TestCase((byte)6, (byte)7)]
        [TestCase((byte)6, (byte)8)]
        [TestCase((byte)6, byte.MaxValue)]
        [CancelAfter(60_000)]
        public async Task CustomProviderSrClassIdentifiersAreValidatedBeforePublication(
            byte id, byte priority, CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            ArrayOf<ReferenceDescription> interfaces = await m_client.BrowseObjectsAsync(
                ObjectIds.NetworkInterfaces, cancellationToken).ConfigureAwait(false);
            NodeId physical = ExpandedNodeId.ToNodeId(interfaces.ToList()
                .Single(reference => reference.BrowseName.Name == "eth0").NodeId, m_session.NamespaceUris);
            NetworkInterfaceValues before = await m_client.ReadInterfaceAsync(physical, cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> original = await ReadParameterSetAsync(
                physical, ObjectTypeIds.ISrClassType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue beforePriority = Parameter(original, BrowseNames.Priority);
            ArrayOf<ReferenceDescription> originalChildren = await m_client.BrowseObjectsAsync(
                physical, cancellationToken).ConfigureAwait(false);
            m_providerBoundary.WatchTransform = snapshot =>
            {
                NetworkInterfaceSnapshot[] updated = snapshot.Interfaces.Span.ToArray();
                for (int index = 0; index < updated.Length; index++)
                {
                    if (updated[index].Id == "physical")
                    {
                        updated[index] = updated[index] with
                        {
                            Speed = 100_000_000,
                            SrClasses = [new NetworkSrClassSnapshot(id, priority, 1)]
                        };
                    }
                }
                return snapshot with { Interfaces = ArrayOf.Wrapped(updated) };
            };
            m_provider.Publish(initial with { Timestamp = initial.Timestamp.AddSeconds(1) });
            bool valid = id <= 7 && priority <= 7;
            await WaitForAsync(async () =>
            {
                NetworkInterfaceValues values = await m_client.ReadInterfaceAsync(physical, cancellationToken)
                    .ConfigureAwait(false);
                return valid
                    ? values.Speed.DataValue.WrappedValue == new Variant(100_000_000UL)
                    : values.Speed.DataValue.StatusCode == StatusCodes.BadOutOfRange;
            }, cancellationToken).ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> parameters = await ReadParameterSetAsync(
                physical, ObjectTypeIds.ISrClassType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue actualId = Parameter(parameters, BrowseNames.Id);
            NetworkParameterValue actualPriority = Parameter(parameters, BrowseNames.Priority);
            if (valid)
            {
                Assert.That(actualId.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(actualId.DataValue.WrappedValue, Is.EqualTo(new Variant(id)));
                Assert.That(actualId.DataValue.SourceTimestamp,
                    Is.EqualTo((DateTimeUtc)initial.Timestamp.AddSeconds(1)));
                Assert.That(actualPriority.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(actualPriority.DataValue.WrappedValue, Is.EqualTo(new Variant(priority)));
                Assert.That(actualPriority.DataValue.SourceTimestamp,
                    Is.EqualTo((DateTimeUtc)initial.Timestamp.AddSeconds(1)));
            }
            else
            {
                NetworkInterfaceValues failed = await m_client.ReadInterfaceAsync(physical, cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(failed.Speed.DataValue.WrappedValue.IsNull, Is.True);
                Assert.That(failed.Speed.DataValue.SourceTimestamp, Is.EqualTo(before.Speed.DataValue.SourceTimestamp));
                Assert.That(actualId.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(actualPriority.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(actualPriority.DataValue.WrappedValue.IsNull, Is.True);
                Assert.That(actualPriority.DataValue.SourceTimestamp,
                    Is.EqualTo(beforePriority.DataValue.SourceTimestamp));
                ArrayOf<ReferenceDescription> children = await m_client.BrowseObjectsAsync(physical, cancellationToken)
                    .ConfigureAwait(false);
                if (id > 7)
                {
                    Assert.That(children.ToList().Select(child => child.BrowseName.Name),
                        Does.Not.Contain("SrClass" + id.ToString(CultureInfo.InvariantCulture)));
                }
                Assert.That(children.ToList().Select(child => child.NodeId),
                    Is.EquivalentTo(originalChildren.ToList().Select(child => child.NodeId)));
                NetworkModelNodeManager manager = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                    .OfType<NetworkModelNodeManager>().Single();
                BaseVariableState retained = manager.FindPredefinedNode<BaseVariableState>(before.Speed.NodeId);
                Assert.That(retained.Value, Is.EqualTo(before.Speed.DataValue.WrappedValue));
                BaseVariableState retainedId = manager.FindPredefinedNode<BaseVariableState>(actualId.NodeId);
                Assert.That(retainedId.Value, Is.EqualTo(new Variant((byte)6)));
                BaseVariableState retainedPriority = manager.FindPredefinedNode<BaseVariableState>(
                    beforePriority.NodeId);
                Assert.That(retainedPriority.Value, Is.EqualTo(beforePriority.DataValue.WrappedValue));
            }

            m_providerBoundary.WatchTransform = null;
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            m_provider.Publish(current with { Timestamp = initial.Timestamp.AddSeconds(2) });
            await WaitForAsync(async () =>
                (await m_client.ReadInterfaceAsync(physical, cancellationToken).ConfigureAwait(false))
                    .Speed.DataValue.SourceTimestamp == (DateTimeUtc)initial.Timestamp.AddSeconds(2),
                cancellationToken).ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> recovered = await ReadParameterSetAsync(
                physical, ObjectTypeIds.ISrClassType, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(recovered, BrowseNames.Id).DataValue.WrappedValue,
                Is.EqualTo(new Variant((byte)6)));
            Assert.That(Parameter(recovered, BrowseNames.Id).DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(Parameter(recovered, BrowseNames.Priority).DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(Parameter(recovered, BrowseNames.Priority).DataValue.WrappedValue,
                Is.EqualTo(beforePriority.DataValue.WrappedValue));
        }

        /// <summary>
        /// Rejects undefined standard enum values from custom providers and recovers the published values.
        /// </summary>
        [TestCase("interface admin")]
        [TestCase("interface oper")]
        [TestCase("duplex")]
        [TestCase("negotiation")]
        [TestCase("stream state")]
        [TestCase("failure code")]
        [TestCase("talker status")]
        [TestCase("listener status")]
        [TestCase("local chassis subtype")]
        [TestCase("port subtype")]
        [TestCase("neighbor chassis subtype")]
        [TestCase("neighbor port subtype")]
        [CancelAfter(60_000)]
        public async Task CustomProviderEnumValuesAreValidatedBeforePublication(
            string valuePath, CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (valuePath is "failure code" or "talker status" or "listener status" or
                "neighbor chassis subtype" or "neighbor port subtype")
            {
                DateTime seedTimestamp = initial.Timestamp.AddSeconds(1);
                m_provider.Publish(WithEnumObservations(initial, valuePath) with { Timestamp = seedTimestamp });
                initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
                await WaitForAsync(async () =>
                {
                    if (!await HasEnumObservationAsync(valuePath, cancellationToken).ConfigureAwait(false))
                    {
                        return false;
                    }
                    DataValue value = (await ReadEnumParameterAsync(valuePath, cancellationToken)
                        .ConfigureAwait(false)).DataValue;
                    return value.StatusCode == StatusCodes.Good &&
                        value.SourceTimestamp == (DateTimeUtc)seedTimestamp;
                }, cancellationToken).ConfigureAwait(false);
            }

            NetworkParameterValue beforeParameter = await ReadEnumParameterAsync(valuePath, cancellationToken)
                .ConfigureAwait(false);
            DataValue before = beforeParameter.DataValue;
            Assert.That(before.StatusCode, Is.EqualTo(StatusCodes.Good));
            BaseVariableState retained;
            if (valuePath == "local chassis subtype")
            {
                retained = m_server.Server.CurrentInstance.NodeManager.DiagnosticsNodeManager?
                    .FindPredefinedNode<BaseVariableState>(beforeParameter.NodeId)
                    ?? throw new InvalidOperationException(
                        "The LLDP local value is not owned by the diagnostics manager.");
            }
            else
            {
                retained = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                    .OfType<NetworkModelNodeManager>().Single()
                    .FindPredefinedNode<BaseVariableState>(beforeParameter.NodeId);
            }

            m_providerBoundary.WatchTransform = snapshot => WithInvalidEnumValue(snapshot, valuePath);
            DateTime invalidTimestamp = initial.Timestamp.AddSeconds(1);
            m_provider.Publish(initial with { Timestamp = invalidTimestamp });
            await WaitForAsync(async () =>
            {
                DataValue value = (await ReadEnumParameterAsync(valuePath, cancellationToken)
                    .ConfigureAwait(false)).DataValue;
                return value.StatusCode == StatusCodes.BadOutOfRange;
            }, cancellationToken).ConfigureAwait(false);

            DataValue rejected = (await ReadEnumParameterAsync(valuePath, cancellationToken)
                .ConfigureAwait(false)).DataValue;
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(rejected.WrappedValue.IsNull, Is.True);
            Assert.That(rejected.SourceTimestamp, Is.EqualTo(before.SourceTimestamp));
            Assert.That(retained.Value, Is.EqualTo(before.WrappedValue));
            Assert.That(retained.Timestamp, Is.EqualTo(initial.Timestamp));

            m_providerBoundary.WatchTransform = null;
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            DateTime recoveryTimestamp = current.Timestamp.AddSeconds(2);
            m_provider.Publish(current with { Timestamp = recoveryTimestamp });
            await WaitForAsync(async () =>
            {
                DataValue value = (await ReadEnumParameterAsync(valuePath, cancellationToken)
                    .ConfigureAwait(false)).DataValue;
                return value.StatusCode == StatusCodes.Good &&
                    value.SourceTimestamp == (DateTimeUtc)recoveryTimestamp;
            }, cancellationToken).ConfigureAwait(false);
            DataValue recovered = (await ReadEnumParameterAsync(valuePath, cancellationToken)
                .ConfigureAwait(false)).DataValue;
            Assert.That(recovered.WrappedValue, Is.EqualTo(before.WrappedValue));
            Assert.That(recovered.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Validates custom-provider priority-mapping entry keys and value ranges with recovery.
        /// </summary>
        [TestCase("empty URI")]
        [TestCase("empty label")]
        [TestCase("PCP range")]
        [TestCase("DSCP range")]
        [TestCase("duplicate key")]
        [CancelAfter(60_000)]
        public async Task CustomProviderPriorityMappingEntriesAreValidatedBeforePublication(
            string invalidField, CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NodeId table = (await m_client.DiscoverMappingTablesAsync(cancellationToken).ConfigureAwait(false))[0];
            NetworkMappingValues before = await m_client.ReadMappingTableAsync(table, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(before.Acquisition.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            BaseVariableState retained = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single()
                .FindPredefinedNode<BaseVariableState>(before.Acquisition.NodeId);

            m_providerBoundary.WatchTransform = snapshot =>
                WithInvalidMappingEntry(snapshot, invalidField);
            DateTime invalidTimestamp = initial.Timestamp.AddSeconds(1);
            m_provider.Publish(initial with { Timestamp = invalidTimestamp });
            await WaitForAsync(async () =>
            {
                NetworkMappingValues values = await m_client.ReadMappingTableAsync(table, cancellationToken)
                    .ConfigureAwait(false);
                return values.Acquisition.DataValue.StatusCode == StatusCodes.BadInvalidArgument;
            }, cancellationToken).ConfigureAwait(false);

            NetworkMappingValues rejected = await m_client.ReadMappingTableAsync(table, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(rejected.Acquisition.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(rejected.Acquisition.DataValue.WrappedValue.IsNull, Is.True);
            Assert.That(rejected.Acquisition.DataValue.SourceTimestamp,
                Is.EqualTo(before.Acquisition.DataValue.SourceTimestamp));
            Assert.That(retained.Value, Is.EqualTo(before.Acquisition.DataValue.WrappedValue));
            Assert.That(retained.Timestamp, Is.EqualTo(initial.Timestamp));

            m_providerBoundary.WatchTransform = null;
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            DateTime recoveryTimestamp = current.Timestamp.AddSeconds(2);
            m_provider.Publish(current with { Timestamp = recoveryTimestamp });
            await WaitForAsync(async () =>
            {
                NetworkMappingValues values = await m_client.ReadMappingTableAsync(table, cancellationToken)
                    .ConfigureAwait(false);
                return values.Acquisition.DataValue.StatusCode == StatusCodes.Good &&
                    values.Acquisition.DataValue.SourceTimestamp == (DateTimeUtc)recoveryTimestamp;
            }, cancellationToken).ConfigureAwait(false);
            NetworkMappingValues recovered = await m_client.ReadMappingTableAsync(table, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(recovered.Entries.ToList().Select(entry =>
                (entry.MappingUri, entry.PriorityLabel, entry.PriorityValue_PCP, entry.PriorityValue_DSCP)),
                Is.EqualTo(before.Entries.ToList().Select(entry =>
                    (entry.MappingUri, entry.PriorityLabel, entry.PriorityValue_PCP, entry.PriorityValue_DSCP))));
        }

        /// <summary>
        /// Preserves valid priority-mapping boundaries and unused-value sentinels for custom providers.
        /// </summary>
        [TestCase((byte)0, 0u)]
        [TestCase((byte)7, 63u)]
        [TestCase(byte.MaxValue, 0u)]
        [TestCase((byte)0, uint.MaxValue)]
        [TestCase(byte.MaxValue, uint.MaxValue)]
        [CancelAfter(60_000)]
        public async Task CustomProviderPriorityMappingBoundariesRemainValid(
            byte pcp, uint dscp, CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NodeId table = (await m_client.DiscoverMappingTablesAsync(cancellationToken).ConfigureAwait(false))[0];
            m_providerBoundary.WatchTransform = snapshot =>
                WithMappingEntry(snapshot, new PriorityMappingEntryType
                {
                    MappingUri = "urn:test:boundary",
                    PriorityLabel = "Boundary",
                    PriorityValue_PCP = pcp,
                    PriorityValue_DSCP = dscp
                });
            DateTime timestamp = initial.Timestamp.AddSeconds(1);
            m_provider.Publish(initial with { Timestamp = timestamp });
            await WaitForAsync(async () =>
            {
                NetworkMappingValues values = await m_client.ReadMappingTableAsync(table, cancellationToken)
                    .ConfigureAwait(false);
                return values.Acquisition.DataValue.StatusCode == StatusCodes.Good &&
                    values.Acquisition.DataValue.SourceTimestamp == (DateTimeUtc)timestamp &&
                    values.Entries.ToList().Any(entry => entry.MappingUri == "urn:test:boundary");
            }, cancellationToken).ConfigureAwait(false);
            NetworkMappingValues actual = await m_client.ReadMappingTableAsync(table, cancellationToken)
                .ConfigureAwait(false);
            PriorityMappingEntryType entry = actual.Entries.ToList()
                .Single(value => value.MappingUri == "urn:test:boundary");
            Assert.That(entry.PriorityValue_PCP, Is.EqualTo(pcp));
            Assert.That(entry.PriorityValue_DSCP, Is.EqualTo(dscp));
        }

        /// <summary>
        /// Accepts a mapping table with no entries and publishes its empty list as good quality.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task CustomProviderEmptyPriorityMappingEntriesRemainValid(CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NodeId table = (await m_client.DiscoverMappingTablesAsync(cancellationToken).ConfigureAwait(false))[0];
            DateTime timestamp = initial.Timestamp.AddSeconds(1);
            m_provider.Publish(initial with
            {
                Timestamp = timestamp,
                MappingTables = [initial.MappingTables[0] with { Entries = [] }]
            });
            await WaitForAsync(async () =>
            {
                NetworkMappingValues values = await m_client.ReadMappingTableAsync(table, cancellationToken)
                    .ConfigureAwait(false);
                return values.Acquisition.DataValue.StatusCode == StatusCodes.Good &&
                    values.Acquisition.DataValue.SourceTimestamp == (DateTimeUtc)timestamp &&
                    values.Entries.Count == 0;
            }, cancellationToken).ConfigureAwait(false);
            NetworkMappingValues empty = await m_client.ReadMappingTableAsync(table, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(empty.Acquisition.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(empty.Entries.Count, Is.Zero);
        }

        /// <summary>
        /// Keeps optional negotiation and stream-status interfaces absent without rejecting the acquisition.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task CustomProviderMayOmitOptionalEnumBearingValues(CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            ArrayOf<ReferenceDescription> publishedInterfaces = await m_client.BrowseObjectsAsync(
                ObjectIds.NetworkInterfaces, cancellationToken).ConfigureAwait(false);
            NodeId physical = ExpandedNodeId.ToNodeId(publishedInterfaces.ToList()
                .Single(reference => reference.BrowseName.Name == "eth0").NodeId, m_session.NamespaceUris);
            await WaitForAsync(
                () => HasInterfaceInSubtreeAsync(
                    physical, ObjectTypeIds.IIeeeAutoNegotiationStatusType, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            Assert.That(await HasInterfaceInSubtreeAsync(
                physical, ObjectTypeIds.IIeeeAutoNegotiationStatusType, cancellationToken)
                .ConfigureAwait(false), Is.True);

            NetworkInterfaceSnapshot[] interfaces = initial.Interfaces.Span.ToArray();
            for (int index = 0; index < interfaces.Length; index++)
            {
                if (interfaces[index].Id == "physical")
                {
                    interfaces[index] = interfaces[index] with
                    {
                        Ethernet = interfaces[index].Ethernet! with { NegotiationStatus = null }
                    };
                }
            }
            NetworkStreamSnapshot[] streams = initial.Streams.Span.ToArray();
            for (int index = 0; index < streams.Length; index++)
            {
                streams[index] = streams[index] with { Status = null };
            }
            DateTime timestamp = initial.Timestamp.AddSeconds(1);
            m_provider.Publish(initial with
            {
                Timestamp = timestamp,
                Interfaces = ArrayOf.Wrapped(interfaces),
                Streams = ArrayOf.Wrapped(streams)
            });

            await WaitForAsync(async () =>
            {
                NetworkInterfaceValues values = await m_client.ReadInterfaceAsync(
                    physical, cancellationToken).ConfigureAwait(false);
                return values.Speed.DataValue.SourceTimestamp == (DateTimeUtc)timestamp &&
                    !await HasInterfaceInSubtreeAsync(
                        physical, ObjectTypeIds.IIeeeAutoNegotiationStatusType, cancellationToken)
                        .ConfigureAwait(false);
            },
                cancellationToken).ConfigureAwait(false);
            Assert.That(await HasInterfaceInSubtreeAsync(
                physical, ObjectTypeIds.IIeeeAutoNegotiationStatusType, cancellationToken)
                .ConfigureAwait(false), Is.False);

            NodeId talker = (await m_client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false))[0];
            await WaitForAsync(async () =>
            {
                NetworkStreamValues values = await m_client.ReadStreamAsync(
                    talker, cancellationToken).ConfigureAwait(false);
                return values.State.DataValue.SourceTimestamp == (DateTimeUtc)timestamp &&
                    !await HasInterfaceAsync(
                        talker, ObjectTypeIds.IIeeeBaseTsnStatusStreamType, cancellationToken)
                        .ConfigureAwait(false);
            },
                cancellationToken).ConfigureAwait(false);
            Assert.That(await HasInterfaceAsync(
                talker, ObjectTypeIds.IIeeeBaseTsnStatusStreamType, cancellationToken)
                .ConfigureAwait(false), Is.False);
        }

        /// <summary>
        /// Validates optional interface VLAN identifiers and recovery after rejected acquisitions.
        /// </summary>
        [TestCase(false, (ushort)0, true)]
        [TestCase(true, (ushort)0, true)]
        [TestCase(true, (ushort)4094, true)]
        [TestCase(true, (ushort)4095, false)]
        [TestCase(true, ushort.MaxValue, false)]
        [CancelAfter(60_000)]
        public async Task CustomProviderInterfaceVlanIdsAreValidatedBeforePublication(
            bool includeVlanId, ushort vlanId, bool valid, CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            ArrayOf<ReferenceDescription> interfaces = await m_client.BrowseObjectsAsync(
                ObjectIds.NetworkInterfaces, cancellationToken).ConfigureAwait(false);
            NodeId vlanInterface = ExpandedNodeId.ToNodeId(interfaces.ToList()
                .Single(reference => reference.BrowseName.Name == "eth0.2").NodeId, m_session.NamespaceUris);
            NetworkInterfaceValues beforeInterface = await m_client.ReadInterfaceAsync(
                vlanInterface, cancellationToken).ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> beforeParameters = await ReadParameterSetAsync(
                vlanInterface, ObjectTypeIds.IVlanIdType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue beforeVlan = Parameter(beforeParameters, BrowseNames.VlanId);
            NetworkModelNodeManager manager = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single();
            BaseVariableState retained = manager.FindPredefinedNode<BaseVariableState>(beforeVlan.NodeId);

            m_providerBoundary.WatchTransform = snapshot =>
            {
                NetworkInterfaceSnapshot[] updated = snapshot.Interfaces.Span.ToArray();
                for (int index = 0; index < updated.Length; index++)
                {
                    if (updated[index].Id == "vlan")
                    {
                        updated[index] = updated[index] with { VlanId = includeVlanId ? vlanId : null };
                    }
                }
                return snapshot with { Interfaces = ArrayOf.Wrapped(updated) };
            };
            DateTime publicationTimestamp = initial.Timestamp.AddSeconds(1);
            m_provider.Publish(initial with { Timestamp = publicationTimestamp });
            await WaitForAsync(async () =>
            {
                NetworkInterfaceValues values = await m_client.ReadInterfaceAsync(
                    vlanInterface, cancellationToken).ConfigureAwait(false);
                bool hasVlan = await HasInterfaceAsync(
                    vlanInterface, ObjectTypeIds.IVlanIdType, cancellationToken).ConfigureAwait(false);
                return valid
                    ? values.Speed.DataValue.SourceTimestamp == (DateTimeUtc)publicationTimestamp &&
                        hasVlan == includeVlanId
                    : values.Speed.DataValue.StatusCode == StatusCodes.BadOutOfRange;
            }, cancellationToken).ConfigureAwait(false);

            bool publishedVlan = await HasInterfaceAsync(
                vlanInterface, ObjectTypeIds.IVlanIdType, cancellationToken).ConfigureAwait(false);
            if (valid && !includeVlanId)
            {
                Assert.That(publishedVlan, Is.False);
                NetworkInterfaceValues actual = await m_client.ReadInterfaceAsync(
                    vlanInterface, cancellationToken).ConfigureAwait(false);
                Assert.That(actual.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(actual.Speed.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)publicationTimestamp));
            }
            else
            {
                Assert.That(publishedVlan, Is.True);
                ArrayOf<NetworkParameterValue> actualParameters = await ReadParameterSetAsync(
                    vlanInterface, ObjectTypeIds.IVlanIdType, cancellationToken).ConfigureAwait(false);
                NetworkParameterValue actualVlan = Parameter(actualParameters, BrowseNames.VlanId);
                if (valid)
                {
                    Assert.That(actualVlan.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(actualVlan.DataValue.WrappedValue, Is.EqualTo(new Variant(vlanId)));
                    Assert.That(actualVlan.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)publicationTimestamp));
                }
                else
                {
                    Assert.That(actualVlan.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                    Assert.That(actualVlan.DataValue.WrappedValue.IsNull, Is.True);
                    Assert.That(actualVlan.DataValue.SourceTimestamp,
                        Is.EqualTo(beforeVlan.DataValue.SourceTimestamp));
                    Assert.That(retained.Value, Is.EqualTo(beforeVlan.DataValue.WrappedValue));
                    Assert.That(retained.Timestamp, Is.EqualTo(initial.Timestamp));
                    NetworkInterfaceValues failed = await m_client.ReadInterfaceAsync(
                        vlanInterface, cancellationToken).ConfigureAwait(false);
                    Assert.That(failed.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                    Assert.That(failed.Speed.DataValue.WrappedValue.IsNull, Is.True);
                    Assert.That(failed.Speed.DataValue.SourceTimestamp,
                        Is.EqualTo(beforeInterface.Speed.DataValue.SourceTimestamp));
                }
            }

            m_providerBoundary.WatchTransform = null;
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            DateTime recoveryTimestamp = current.Timestamp.AddSeconds(2);
            m_provider.Publish(current with { Timestamp = recoveryTimestamp });
            await WaitForAsync(async () =>
            {
                if (!await HasInterfaceAsync(
                    vlanInterface, ObjectTypeIds.IVlanIdType, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }
                ArrayOf<NetworkParameterValue> values = await ReadParameterSetAsync(
                    vlanInterface, ObjectTypeIds.IVlanIdType, cancellationToken).ConfigureAwait(false);
                DataValue dataValue = Parameter(values, BrowseNames.VlanId).DataValue;
                return dataValue.StatusCode == StatusCodes.Good &&
                    dataValue.SourceTimestamp == (DateTimeUtc)recoveryTimestamp;
            }, cancellationToken).ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> recoveredParameters = await ReadParameterSetAsync(
                vlanInterface, ObjectTypeIds.IVlanIdType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue recoveredVlan = Parameter(recoveredParameters, BrowseNames.VlanId);
            Assert.That(recoveredVlan.DataValue.WrappedValue, Is.EqualTo(beforeVlan.DataValue.WrappedValue));
            Assert.That(recoveredVlan.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Validates optional stream-reservation VLAN identifiers and recovery after rejection.
        /// </summary>
        [TestCase(false, (ushort)0, true)]
        [TestCase(true, (ushort)0, true)]
        [TestCase(true, (ushort)4094, true)]
        [TestCase(true, (ushort)4095, false)]
        [TestCase(true, ushort.MaxValue, false)]
        [CancelAfter(60_000)]
        public async Task CustomProviderSrClassVidsAreValidatedBeforePublication(
            bool includeSrClass, ushort vid, bool valid, CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            ArrayOf<ReferenceDescription> interfaces = await m_client.BrowseObjectsAsync(
                ObjectIds.NetworkInterfaces, cancellationToken).ConfigureAwait(false);
            NodeId physical = ExpandedNodeId.ToNodeId(interfaces.ToList()
                .Single(reference => reference.BrowseName.Name == "eth0").NodeId, m_session.NamespaceUris);
            NetworkInterfaceValues beforeInterface = await m_client.ReadInterfaceAsync(
                physical, cancellationToken).ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> beforeParameters = await ReadParameterSetAsync(
                physical, ObjectTypeIds.ISrClassType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue beforeVid = Parameter(beforeParameters, BrowseNames.Vid);
            ArrayOf<ReferenceDescription> beforeChildren = await m_client.BrowseObjectsAsync(
                physical, cancellationToken).ConfigureAwait(false);
            NetworkModelNodeManager manager = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single();
            BaseVariableState retained = manager.FindPredefinedNode<BaseVariableState>(beforeVid.NodeId);

            m_providerBoundary.WatchTransform = snapshot =>
            {
                NetworkInterfaceSnapshot[] updated = snapshot.Interfaces.Span.ToArray();
                for (int index = 0; index < updated.Length; index++)
                {
                    if (updated[index].Id == "physical")
                    {
                        updated[index] = updated[index] with
                        {
                            SrClasses = includeSrClass
                                ? [new NetworkSrClassSnapshot(6, 3, vid)]
                                : []
                        };
                    }
                }
                return snapshot with { Interfaces = ArrayOf.Wrapped(updated) };
            };
            DateTime publicationTimestamp = initial.Timestamp.AddSeconds(1);
            m_provider.Publish(initial with { Timestamp = publicationTimestamp });
            await WaitForAsync(async () =>
            {
                NetworkInterfaceValues values = await m_client.ReadInterfaceAsync(
                    physical, cancellationToken).ConfigureAwait(false);
                return valid
                    ? values.Speed.DataValue.SourceTimestamp == (DateTimeUtc)publicationTimestamp
                    : values.Speed.DataValue.StatusCode == StatusCodes.BadOutOfRange;
            }, cancellationToken).ConfigureAwait(false);

            ArrayOf<ReferenceDescription> actualChildren = await m_client.BrowseObjectsAsync(
                physical, cancellationToken).ConfigureAwait(false);
            bool hasSrClass = actualChildren.ToList().Any(child => child.BrowseName.Name == "SrClass6");
            if (valid && !includeSrClass)
            {
                Assert.That(hasSrClass, Is.False);
                NetworkInterfaceValues actual = await m_client.ReadInterfaceAsync(
                    physical, cancellationToken).ConfigureAwait(false);
                Assert.That(actual.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(actual.Speed.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)publicationTimestamp));
            }
            else
            {
                Assert.That(hasSrClass, Is.True);
                ArrayOf<NetworkParameterValue> actualParameters = await ReadParameterSetAsync(
                    physical, ObjectTypeIds.ISrClassType, cancellationToken).ConfigureAwait(false);
                NetworkParameterValue actualVid = Parameter(actualParameters, BrowseNames.Vid);
                if (valid)
                {
                    Assert.That(actualVid.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(actualVid.DataValue.WrappedValue, Is.EqualTo(new Variant(vid)));
                    Assert.That(actualVid.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)publicationTimestamp));
                }
                else
                {
                    Assert.That(actualVid.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                    Assert.That(actualVid.DataValue.WrappedValue.IsNull, Is.True);
                    Assert.That(actualVid.DataValue.SourceTimestamp,
                        Is.EqualTo(beforeVid.DataValue.SourceTimestamp));
                    Assert.That(retained.Value, Is.EqualTo(beforeVid.DataValue.WrappedValue));
                    Assert.That(retained.Timestamp, Is.EqualTo(initial.Timestamp));
                    NetworkInterfaceValues failed = await m_client.ReadInterfaceAsync(
                        physical, cancellationToken).ConfigureAwait(false);
                    Assert.That(failed.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                    Assert.That(failed.Speed.DataValue.WrappedValue.IsNull, Is.True);
                    Assert.That(failed.Speed.DataValue.SourceTimestamp,
                        Is.EqualTo(beforeInterface.Speed.DataValue.SourceTimestamp));
                    Assert.That(actualChildren.ToList().Select(child => child.NodeId),
                        Is.EquivalentTo(beforeChildren.ToList().Select(child => child.NodeId)));
                }
            }

            m_providerBoundary.WatchTransform = null;
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            DateTime recoveryTimestamp = current.Timestamp.AddSeconds(2);
            m_provider.Publish(current with { Timestamp = recoveryTimestamp });
            await WaitForAsync(async () =>
            {
                NetworkInterfaceValues restoredInterface = await m_client.ReadInterfaceAsync(
                    physical, cancellationToken).ConfigureAwait(false);
                if (restoredInterface.Speed.DataValue.StatusCode != StatusCodes.Good ||
                    restoredInterface.Speed.DataValue.SourceTimestamp != (DateTimeUtc)recoveryTimestamp)
                {
                    return false;
                }
                ArrayOf<NetworkParameterValue> values = await ReadParameterSetAsync(
                    physical, ObjectTypeIds.ISrClassType, cancellationToken).ConfigureAwait(false);
                DataValue dataValue = Parameter(values, BrowseNames.Vid).DataValue;
                return dataValue.StatusCode == StatusCodes.Good &&
                    dataValue.SourceTimestamp == (DateTimeUtc)recoveryTimestamp;
            }, cancellationToken).ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> recoveredParameters = await ReadParameterSetAsync(
                physical, ObjectTypeIds.ISrClassType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue recoveredVid = Parameter(recoveredParameters, BrowseNames.Vid);
            Assert.That(recoveredVid.DataValue.WrappedValue, Is.EqualTo(beforeVid.DataValue.WrappedValue));
            Assert.That(recoveredVid.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Rejects duplicate SR-class identifiers before replacing the interface's published values.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task CustomProviderDuplicateSrClassIdsAreRejectedBeforePublication(
            CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NodeId physical = await FindInterfaceAsync("eth0", cancellationToken).ConfigureAwait(false);
            NetworkInterfaceValues beforeInterface = await m_client.ReadInterfaceAsync(
                physical, cancellationToken).ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> beforeParameters = await ReadParameterSetAsync(
                physical, ObjectTypeIds.ISrClassType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue beforeId = Parameter(beforeParameters, BrowseNames.Id);
            NetworkParameterValue beforePriority = Parameter(beforeParameters, BrowseNames.Priority);
            ArrayOf<ReferenceDescription> beforeChildren = await m_client.BrowseObjectsAsync(
                physical, cancellationToken).ConfigureAwait(false);
            NetworkModelNodeManager manager = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single();
            BaseVariableState retainedId = manager.FindPredefinedNode<BaseVariableState>(beforeId.NodeId);
            BaseVariableState retainedPriority = manager.FindPredefinedNode<BaseVariableState>(beforePriority.NodeId);

            m_providerBoundary.WatchTransform = snapshot =>
            {
                NetworkInterfaceSnapshot[] interfaces = snapshot.Interfaces.Span.ToArray();
                for (int index = 0; index < interfaces.Length; index++)
                {
                    if (interfaces[index].Id == "physical")
                    {
                        interfaces[index] = interfaces[index] with
                        {
                            Speed = 100_000_000,
                            SrClasses =
                            [
                                new NetworkSrClassSnapshot(6, 2, 100),
                                new NetworkSrClassSnapshot(6, 3, 200)
                            ]
                        };
                    }
                }
                return snapshot with { Interfaces = ArrayOf.Wrapped(interfaces) };
            };
            m_provider.Publish(initial with { Timestamp = initial.Timestamp.AddSeconds(1) });
            await WaitForAsync(async () =>
                (await m_client.ReadInterfaceAsync(physical, cancellationToken).ConfigureAwait(false))
                    .Speed.DataValue.StatusCode == StatusCodes.BadInvalidArgument,
                cancellationToken).ConfigureAwait(false);

            NetworkInterfaceValues rejected = await m_client.ReadInterfaceAsync(physical, cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> rejectedParameters = await ReadParameterSetAsync(
                physical, ObjectTypeIds.ISrClassType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue rejectedId = Parameter(rejectedParameters, BrowseNames.Id);
            NetworkParameterValue rejectedPriority = Parameter(rejectedParameters, BrowseNames.Priority);
            Assert.That(rejected.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(rejected.Speed.DataValue.WrappedValue.IsNull, Is.True);
            Assert.That(rejected.Speed.DataValue.SourceTimestamp,
                Is.EqualTo(beforeInterface.Speed.DataValue.SourceTimestamp));
            Assert.That(rejectedId.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(rejectedId.DataValue.WrappedValue.IsNull, Is.True);
            Assert.That(rejectedId.DataValue.SourceTimestamp, Is.EqualTo(beforeId.DataValue.SourceTimestamp));
            Assert.That(rejectedPriority.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(rejectedPriority.DataValue.WrappedValue.IsNull, Is.True);
            Assert.That(rejectedPriority.DataValue.SourceTimestamp,
                Is.EqualTo(beforePriority.DataValue.SourceTimestamp));
            Assert.That(retainedId.Value, Is.EqualTo(beforeId.DataValue.WrappedValue));
            Assert.That(retainedPriority.Value, Is.EqualTo(beforePriority.DataValue.WrappedValue));
            Assert.That(retainedId.Timestamp, Is.EqualTo(initial.Timestamp));
            Assert.That(retainedPriority.Timestamp, Is.EqualTo(initial.Timestamp));
            ArrayOf<ReferenceDescription> rejectedChildren = await m_client.BrowseObjectsAsync(
                physical, cancellationToken).ConfigureAwait(false);
            Assert.That(rejectedChildren.ToList().Select(child => child.NodeId),
                Is.EquivalentTo(beforeChildren.ToList().Select(child => child.NodeId)));

            m_providerBoundary.WatchTransform = null;
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            DateTime recoveryTimestamp = initial.Timestamp.AddSeconds(2);
            m_provider.Publish(current with { Timestamp = recoveryTimestamp });
            await WaitForAsync(async () =>
                (await m_client.ReadInterfaceAsync(physical, cancellationToken).ConfigureAwait(false))
                    .Speed.DataValue.SourceTimestamp == (DateTimeUtc)recoveryTimestamp,
                cancellationToken).ConfigureAwait(false);
            NetworkInterfaceValues recovered = await m_client.ReadInterfaceAsync(physical, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(recovered.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            ArrayOf<NetworkParameterValue> recoveredParameters = await ReadParameterSetAsync(
                physical, ObjectTypeIds.ISrClassType, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(recoveredParameters, BrowseNames.Id).DataValue.StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Allows the same SR-class identifier on separate interfaces.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task CustomProviderSrClassIdsAreScopedPerInterface(CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NodeId physical = await FindInterfaceAsync("eth0", cancellationToken).ConfigureAwait(false);
            NodeId vlan = await FindInterfaceAsync("eth0.2", cancellationToken).ConfigureAwait(false);
            m_providerBoundary.WatchTransform = snapshot =>
            {
                NetworkInterfaceSnapshot[] interfaces = snapshot.Interfaces.Span.ToArray();
                for (int index = 0; index < interfaces.Length; index++)
                {
                    interfaces[index] = interfaces[index] with
                    {
                        SrClasses = [new NetworkSrClassSnapshot(6, (byte)index, (ushort)(100 + index))]
                    };
                }
                return snapshot with { Interfaces = ArrayOf.Wrapped(interfaces) };
            };
            DateTime publicationTimestamp = initial.Timestamp.AddSeconds(1);
            m_provider.Publish(initial with { Timestamp = publicationTimestamp });
            await WaitForAsync(async () =>
            {
                ArrayOf<NetworkParameterValue> physicalValues = await ReadParameterSetAsync(
                    physical, ObjectTypeIds.ISrClassType, cancellationToken).ConfigureAwait(false);
                ArrayOf<NetworkParameterValue> vlanValues = await ReadParameterSetAsync(
                    vlan, ObjectTypeIds.ISrClassType, cancellationToken).ConfigureAwait(false);
                return Parameter(physicalValues, BrowseNames.Id).DataValue.SourceTimestamp ==
                        (DateTimeUtc)publicationTimestamp &&
                    Parameter(vlanValues, BrowseNames.Id).DataValue.SourceTimestamp ==
                        (DateTimeUtc)publicationTimestamp;
            }, cancellationToken).ConfigureAwait(false);
            foreach ((NodeId id, byte priority) in new[] { (physical, (byte)0), (vlan, (byte)1) })
            {
                ArrayOf<NetworkParameterValue> values = await ReadParameterSetAsync(
                    id, ObjectTypeIds.ISrClassType, cancellationToken).ConfigureAwait(false);
                Assert.That(Parameter(values, BrowseNames.Id).DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(Parameter(values, BrowseNames.Id).DataValue.WrappedValue,
                    Is.EqualTo(new Variant((byte)6)));
                Assert.That(Parameter(values, BrowseNames.Priority).DataValue.WrappedValue,
                    Is.EqualTo(new Variant(priority)));
                Assert.That(Parameter(values, BrowseNames.Priority).DataValue.SourceTimestamp,
                    Is.EqualTo((DateTimeUtc)publicationTimestamp));
            }
        }

        /// <summary>
        /// Rejects duplicate stream-interface identities before any entity in the acquisition changes.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task CustomProviderDuplicateStreamInterfaceIdsAreRejectedBeforePublication(
            CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NodeId physical = await FindInterfaceAsync("eth0", cancellationToken).ConfigureAwait(false);
            NodeId talker = (await m_client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false))[0];
            NetworkInterfaceValues beforeInterface = await m_client.ReadInterfaceAsync(
                physical, cancellationToken).ConfigureAwait(false);
            NetworkStreamValues beforeStream = await m_client.ReadStreamAsync(talker, cancellationToken)
                .ConfigureAwait(false);
            NetworkParameterValue beforeMac = Parameter(await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeTsnInterfaceConfigurationTalkerType, cancellationToken)
                .ConfigureAwait(false), BrowseNames.MacAddress);
            ArrayOf<ReferenceDescription> beforeInterfaces = await m_client.BrowseObjectsAsync(
                ObjectIds.NetworkInterfaces, cancellationToken).ConfigureAwait(false);
            ArrayOf<ReferenceDescription> beforeTalkerChildren = await m_client.BrowseObjectsAsync(
                talker, cancellationToken).ConfigureAwait(false);
            NetworkModelNodeManager manager = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single();
            BaseVariableState retainedSpeed = manager.FindPredefinedNode<BaseVariableState>(
                beforeInterface.Speed.NodeId);
            BaseVariableState retainedStreamId = manager.FindPredefinedNode<BaseVariableState>(
                beforeStream.StreamId.NodeId);
            BaseVariableState retainedMac = manager.FindPredefinedNode<BaseVariableState>(beforeMac.NodeId);

            m_providerBoundary.WatchTransform = snapshot =>
            {
                NetworkInterfaceSnapshot[] interfaces = snapshot.Interfaces.Span.ToArray();
                for (int index = 0; index < interfaces.Length; index++)
                {
                    if (interfaces[index].Id == "physical")
                    {
                        interfaces[index] = interfaces[index] with { Speed = 100_000_000 };
                    }
                }
                NetworkStreamSnapshot[] streams = snapshot.Streams.Span.ToArray();
                for (int index = 0; index < streams.Length; index++)
                {
                    if (streams[index].IsTalker)
                    {
                        streams[index] = streams[index] with
                        {
                            Interfaces =
                            [
                                new NetworkStreamInterfaceSnapshot("duplicate", "02:00:00:00:00:11")
                                {
                                    InterfaceName = "first"
                                },
                                new NetworkStreamInterfaceSnapshot("duplicate", "02:00:00:00:00:12")
                                {
                                    InterfaceName = "second"
                                }
                            ]
                        };
                    }
                }
                return snapshot with
                {
                    Interfaces = ArrayOf.Wrapped(interfaces),
                    Streams = ArrayOf.Wrapped(streams)
                };
            };
            m_provider.Publish(initial with { Timestamp = initial.Timestamp.AddSeconds(1) });
            await WaitForAsync(async () =>
                (await m_client.ReadInterfaceAsync(physical, cancellationToken).ConfigureAwait(false))
                    .Speed.DataValue.StatusCode == StatusCodes.BadInvalidArgument,
                cancellationToken).ConfigureAwait(false);

            NetworkInterfaceValues rejectedInterface = await m_client.ReadInterfaceAsync(
                physical, cancellationToken).ConfigureAwait(false);
            NetworkStreamValues rejectedStream = await m_client.ReadStreamAsync(talker, cancellationToken)
                .ConfigureAwait(false);
            NetworkParameterValue rejectedMac = Parameter(await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeTsnInterfaceConfigurationTalkerType, cancellationToken)
                .ConfigureAwait(false), BrowseNames.MacAddress);
            Assert.That(rejectedInterface.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(rejectedInterface.Speed.DataValue.WrappedValue.IsNull, Is.True);
            Assert.That(rejectedInterface.Speed.DataValue.SourceTimestamp,
                Is.EqualTo(beforeInterface.Speed.DataValue.SourceTimestamp));
            Assert.That(rejectedStream.StreamId.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(rejectedStream.StreamId.DataValue.WrappedValue.IsNull, Is.True);
            Assert.That(rejectedStream.StreamId.DataValue.SourceTimestamp,
                Is.EqualTo(beforeStream.StreamId.DataValue.SourceTimestamp));
            Assert.That(rejectedMac.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(rejectedMac.DataValue.WrappedValue.IsNull, Is.True);
            Assert.That(rejectedMac.DataValue.SourceTimestamp, Is.EqualTo(beforeMac.DataValue.SourceTimestamp));
            Assert.That(retainedSpeed.Value, Is.EqualTo(beforeInterface.Speed.DataValue.WrappedValue));
            Assert.That(retainedStreamId.Value, Is.EqualTo(beforeStream.StreamId.DataValue.WrappedValue));
            Assert.That(retainedMac.Value, Is.EqualTo(beforeMac.DataValue.WrappedValue));
            Assert.That(retainedSpeed.Timestamp, Is.EqualTo(initial.Timestamp));
            Assert.That(retainedStreamId.Timestamp, Is.EqualTo(initial.Timestamp));
            Assert.That(retainedMac.Timestamp, Is.EqualTo(initial.Timestamp));
            Assert.That((await m_client.BrowseObjectsAsync(
                ObjectIds.NetworkInterfaces, cancellationToken).ConfigureAwait(false)).ToList()
                .Select(child => child.NodeId),
                Is.EquivalentTo(beforeInterfaces.ToList().Select(child => child.NodeId)));
            Assert.That((await m_client.BrowseObjectsAsync(talker, cancellationToken).ConfigureAwait(false)).ToList()
                .Select(child => child.NodeId),
                Is.EquivalentTo(beforeTalkerChildren.ToList().Select(child => child.NodeId)));

            m_providerBoundary.WatchTransform = null;
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            DateTime recoveryTimestamp = initial.Timestamp.AddSeconds(2);
            m_provider.Publish(current with { Timestamp = recoveryTimestamp });
            await WaitForAsync(async () =>
                (await m_client.ReadInterfaceAsync(physical, cancellationToken).ConfigureAwait(false))
                    .Speed.DataValue.SourceTimestamp == (DateTimeUtc)recoveryTimestamp,
                cancellationToken).ConfigureAwait(false);
            Assert.That((await m_client.ReadInterfaceAsync(physical, cancellationToken).ConfigureAwait(false))
                .Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That((await m_client.ReadStreamAsync(talker, cancellationToken).ConfigureAwait(false))
                .StreamId.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Repeated supplied names and standard-child names cannot alias stream-interface configuration nodes.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        [CancelAfter(60_000)]
        public async Task StreamInterfaceNamesCannotAliasOrReplaceStableConfigurations(
            bool talker, CancellationToken cancellationToken)
        {
            NodeId streamId = (await m_client.DiscoverStreamsAsync(talker, cancellationToken)
                .ConfigureAwait(false))[0];
            NetworkModelSnapshot snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NetworkModelNodeManager manager = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single();
            BaseVariableState? retainedName = null;
            NodeId retainedId = NodeId.Null;
            for (int phase = 0; phase < 2; phase++)
            {
                var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                DateTime timestamp = snapshot.Timestamp.AddSeconds(1);
                m_providerBoundary.AfterDelivery = current =>
                {
                    if (current.Timestamp == timestamp)
                    {
                        delivered.TrySetResult(true);
                    }
                };
                string replacementId = phase == 0 ? "removed" : "replacement";
                NetworkStreamSnapshot[] streams = snapshot.Streams.Span.ToArray();
                for (int index = 0; index < streams.Length; index++)
                {
                    if (streams[index].IsTalker == talker)
                    {
                        streams[index] = streams[index] with
                        {
                            Interfaces =
                            [
                                new NetworkStreamInterfaceSnapshot("retained", "02:00:00:00:00:11")
                                {
                                    InterfaceName = phase == 0 ? "VlanTag" : "renamed"
                                },
                                new NetworkStreamInterfaceSnapshot(replacementId, "02:00:00:00:00:12")
                                {
                                    InterfaceName = "VlanTag"
                                }
                            ]
                        };
                    }
                }
                m_provider.Publish(snapshot with { Timestamp = timestamp, Streams = ArrayOf.Wrapped(streams) });
                await delivered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                ArrayOf<ReferenceDescription> children = await m_client.BrowseObjectsAsync(
                    streamId, cancellationToken).ConfigureAwait(false);
                foreach ((string id, string name, string address) in new[]
                {
                    ("retained", phase == 0 ? "VlanTag" : "renamed", "02:00:00:00:00:11"),
                    (replacementId, "VlanTag", "02:00:00:00:00:12")
                })
                {
                    ReferenceDescription child = children.ToList()
                        .Single(value => value.BrowseName.Name == "Interface-" + id);
                    NodeId configurationId = ExpandedNodeId.ToNodeId(child.NodeId, m_session.NamespaceUris);
                    ArrayOf<NetworkParameterValue> values = await m_client.ReadParametersAsync(
                        configurationId, cancellationToken).ConfigureAwait(false);
                    NetworkParameterValue interfaceName = Parameter(values, BrowseNames.InterfaceName);
                    Assert.That(interfaceName.DataValue.WrappedValue, Is.EqualTo(new Variant(name)));
                    Assert.That(interfaceName.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)timestamp));
                    Assert.That(Parameter(values, BrowseNames.MacAddress).DataValue.WrappedValue,
                        Is.EqualTo(new Variant(address)));
                    if (id == "retained")
                    {
                        BaseVariableState current = manager.FindPredefinedNode<BaseVariableState>(interfaceName.NodeId);
                        if (phase == 0)
                        {
                            retainedId = configurationId;
                            retainedName = current;
                        }
                        else
                        {
                            Assert.That(configurationId, Is.EqualTo(retainedId));
                            Assert.That(current, Is.SameAs(retainedName));
                        }
                    }
                }
                Assert.That(children.ToList().Count(value => value.BrowseName.Name == "VlanTag"),
                    Is.EqualTo(1));
                Assert.That(children.ToList().Any(value => value.BrowseName.Name == "Interface-removed"),
                    Is.EqualTo(phase == 0));
                snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Allows the same stream-interface identity in separate stream scopes.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task CustomProviderStreamInterfaceIdsAreScopedPerStream(CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NodeId talker = (await m_client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false))[0];
            NodeId listener = (await m_client.DiscoverStreamsAsync(false, cancellationToken).ConfigureAwait(false))[0];
            m_providerBoundary.WatchTransform = snapshot =>
            {
                NetworkStreamSnapshot[] streams = snapshot.Streams.Span.ToArray();
                for (int index = 0; index < streams.Length; index++)
                {
                    streams[index] = streams[index] with
                    {
                        Interfaces =
                        [
                            new NetworkStreamInterfaceSnapshot("shared", "02:00:00:00:00:11")
                            {
                                InterfaceName = streams[index].IsTalker ? "talker port" : "listener port"
                            }
                        ]
                    };
                }
                return snapshot with { Streams = ArrayOf.Wrapped(streams) };
            };
            DateTime publicationTimestamp = initial.Timestamp.AddSeconds(1);
            m_provider.Publish(initial with { Timestamp = publicationTimestamp });
            await WaitForAsync(async () =>
            {
                NetworkStreamValues talkerValues = await m_client.ReadStreamAsync(
                    talker, cancellationToken).ConfigureAwait(false);
                NetworkStreamValues listenerValues = await m_client.ReadStreamAsync(
                    listener, cancellationToken).ConfigureAwait(false);
                return talkerValues.StreamId.DataValue.SourceTimestamp == (DateTimeUtc)publicationTimestamp &&
                    listenerValues.StreamId.DataValue.SourceTimestamp == (DateTimeUtc)publicationTimestamp;
            },
                cancellationToken).ConfigureAwait(false);
            foreach ((NodeId id, string name, NodeId interfaceType) in new[]
            {
                (talker, "talker port", ObjectTypeIds.IIeeeTsnInterfaceConfigurationTalkerType),
                (listener, "listener port", ObjectTypeIds.IIeeeTsnInterfaceConfigurationListenerType)
            })
            {
                ArrayOf<NetworkParameterValue> values = await ReadParameterSetAsync(
                    id, interfaceType, cancellationToken).ConfigureAwait(false);
                NetworkParameterValue interfaceName = Parameter(values, BrowseNames.InterfaceName);
                Assert.That(interfaceName.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(interfaceName.DataValue.WrappedValue, Is.EqualTo(new Variant(name)));
                Assert.That(interfaceName.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)publicationTimestamp));
            }
        }

        /// <summary>
        /// Validates the fixed LLDP destination-address size at the custom-provider publication boundary.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(5, false)]
        [TestCase(6, true)]
        [TestCase(7, false)]
        [CancelAfter(60_000)]
        public async Task CustomProviderLldpDestinationMacAddressRequiresSixBytes(
            int addressLength, bool valid, CancellationToken cancellationToken)
        {
            NetworkModelSnapshot seeded = NetworkScenario.Advance(
                await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false), 1);
            m_provider.Publish(seeded);
            NodeId port = (await m_client.DiscoverLldpPortsAsync(cancellationToken).ConfigureAwait(false))[0];
            await WaitForAsync(async () =>
                (await m_client.DiscoverNeighborsAsync(port, cancellationToken).ConfigureAwait(false)).Count == 1,
                cancellationToken).ConfigureAwait(false);
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NetworkParameterValue before = Parameter(await m_client.ReadParametersAsync(
                port, cancellationToken).ConfigureAwait(false), BrowseNames.DestMacAddress);
            Assert.That(before.DataValue.WrappedValue.TryGetValue(out ArrayOf<byte> previousAddress), Is.True);
            Assert.That(previousAddress.Count, Is.EqualTo(6));
            ArrayOf<ReferenceDescription> beforePorts = await m_client.BrowseObjectsAsync(
                ObjectIds.LLDP_Ports, cancellationToken).ConfigureAwait(false);
            ArrayOf<NodeId> beforeNeighbors = await m_client.DiscoverNeighborsAsync(
                port, cancellationToken).ConfigureAwait(false);
            NetworkModelNodeManager manager = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single();
            BaseVariableState retained = manager.FindPredefinedNode<BaseVariableState>(before.NodeId);
            byte[] addressBytes = Enumerable.Range(1, addressLength).Select(value => (byte)value).ToArray();
            ByteString destinationAddress = ByteString.From(addressBytes.AsSpan());
            m_providerBoundary.WatchTransform = snapshot =>
            {
                NetworkLldpSnapshot lldp = snapshot.Lldp
                    ?? throw new InvalidOperationException("The test acquisition requires LLDP.");
                NetworkLldpPortSnapshot[] ports = lldp.Ports.Span.ToArray();
                ports[0] = ports[0] with { DestinationMacAddress = destinationAddress };
                return snapshot with { Lldp = lldp with { Ports = ArrayOf.Wrapped(ports) } };
            };
            DateTime publicationTimestamp = initial.Timestamp.AddSeconds(1);
            m_provider.Publish(initial with { Timestamp = publicationTimestamp });
            await WaitForAsync(async () =>
            {
                DataValue value = Parameter(await m_client.ReadParametersAsync(
                    port, cancellationToken).ConfigureAwait(false), BrowseNames.DestMacAddress).DataValue;
                return valid
                    ? value.SourceTimestamp == (DateTimeUtc)publicationTimestamp
                    : value.StatusCode == StatusCodes.BadOutOfRange;
            }, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue actual = Parameter(await m_client.ReadParametersAsync(
                port, cancellationToken).ConfigureAwait(false), BrowseNames.DestMacAddress);
            if (valid)
            {
                Assert.That(actual.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(actual.DataValue.WrappedValue.TryGetValue(out ArrayOf<byte> acceptedAddress), Is.True);
                Assert.That(acceptedAddress.ToArray(), Is.EqualTo(addressBytes));
            }
            else
            {
                Assert.That(actual.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(actual.DataValue.WrappedValue.IsNull, Is.True);
                Assert.That(actual.DataValue.SourceTimestamp, Is.EqualTo(before.DataValue.SourceTimestamp));
                Assert.That(retained.Value, Is.EqualTo(before.DataValue.WrappedValue));
                Assert.That(retained.Timestamp, Is.EqualTo(initial.Timestamp));
            }
            Assert.That((await m_client.BrowseObjectsAsync(
                ObjectIds.LLDP_Ports, cancellationToken).ConfigureAwait(false)).ToList()
                .Select(child => child.NodeId),
                Is.EquivalentTo(beforePorts.ToList().Select(child => child.NodeId)));
            Assert.That((await m_client.DiscoverNeighborsAsync(port, cancellationToken).ConfigureAwait(false))
                .ToArray(), Is.EqualTo(beforeNeighbors.ToArray()));

            if (!valid)
            {
                m_providerBoundary.WatchTransform = null;
                NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
                DateTime recoveryTimestamp = initial.Timestamp.AddSeconds(2);
                m_provider.Publish(current with { Timestamp = recoveryTimestamp });
                await WaitForAsync(async () =>
                {
                    DataValue value = Parameter(await m_client.ReadParametersAsync(
                        port, cancellationToken).ConfigureAwait(false), BrowseNames.DestMacAddress).DataValue;
                    return value.StatusCode == StatusCodes.Good &&
                        value.SourceTimestamp == (DateTimeUtc)recoveryTimestamp;
                }, cancellationToken).ConfigureAwait(false);
                Assert.That(Parameter(await m_client.ReadParametersAsync(
                    port, cancellationToken).ConfigureAwait(false), BrowseNames.DestMacAddress)
                    .DataValue.WrappedValue, Is.EqualTo(before.DataValue.WrappedValue));
            }
        }

        /// <summary>
        /// Rejects non-UTC acquisition timestamps without interpreting them in the host time zone.
        /// </summary>
        [TestCase(DateTimeKind.Local)]
        [TestCase(DateTimeKind.Unspecified)]
        [CancelAfter(60_000)]
        public async Task CustomProviderNonUtcTimestampsAreRejectedBeforePublication(
            DateTimeKind kind, CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NodeId physical = await FindInterfaceAsync("eth0", cancellationToken).ConfigureAwait(false);
            NetworkInterfaceValues before = await m_client.ReadInterfaceAsync(physical, cancellationToken)
                .ConfigureAwait(false);
            ArrayOf<ReferenceDescription> beforeInterfaces = await m_client.BrowseObjectsAsync(
                ObjectIds.NetworkInterfaces, cancellationToken).ConfigureAwait(false);
            BaseVariableState retained = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single()
                .FindPredefinedNode<BaseVariableState>(before.Speed.NodeId);
            m_providerBoundary.WatchTransform = snapshot => snapshot with
            {
                Timestamp = DateTime.SpecifyKind(snapshot.Timestamp.AddSeconds(1), kind)
            };
            m_provider.Publish(initial with { Timestamp = initial.Timestamp.AddSeconds(1) });
            await WaitForAsync(async () =>
                (await m_client.ReadInterfaceAsync(physical, cancellationToken).ConfigureAwait(false))
                    .Speed.DataValue.StatusCode == StatusCodes.BadInvalidArgument,
                cancellationToken).ConfigureAwait(false);

            NetworkInterfaceValues rejected = await m_client.ReadInterfaceAsync(physical, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(rejected.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(rejected.Speed.DataValue.WrappedValue.IsNull, Is.True);
            Assert.That(rejected.Speed.DataValue.SourceTimestamp,
                Is.EqualTo(before.Speed.DataValue.SourceTimestamp));
            Assert.That(retained.Value, Is.EqualTo(before.Speed.DataValue.WrappedValue));
            Assert.That(retained.Timestamp, Is.EqualTo(initial.Timestamp));
            Assert.That((await m_client.BrowseObjectsAsync(
                ObjectIds.NetworkInterfaces, cancellationToken).ConfigureAwait(false)).ToList()
                .Select(child => child.NodeId),
                Is.EquivalentTo(beforeInterfaces.ToList().Select(child => child.NodeId)));

            m_providerBoundary.WatchTransform = null;
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            DateTime recoveryTimestamp = initial.Timestamp.AddSeconds(2);
            m_provider.Publish(current with { Timestamp = recoveryTimestamp });
            await WaitForAsync(async () =>
                (await m_client.ReadInterfaceAsync(physical, cancellationToken).ConfigureAwait(false))
                    .Speed.DataValue.SourceTimestamp == (DateTimeUtc)recoveryTimestamp,
                cancellationToken).ConfigureAwait(false);
            NetworkInterfaceValues recovered = await m_client.ReadInterfaceAsync(physical, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(recovered.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(recovered.Speed.DataValue.WrappedValue, Is.EqualTo(before.Speed.DataValue.WrappedValue));
        }

        /// <summary>
        /// Rejects an initial non-UTC read before publishing dynamic nodes, then accepts a later UTC watch item.
        /// </summary>
        [TestCase(DateTimeKind.Local)]
        [TestCase(DateTimeKind.Unspecified)]
        [CancelAfter(60_000)]
        public async Task CustomProviderNonUtcInitialReadPublishesNoDynamicNodesAndRecovers(
            DateTimeKind kind, CancellationToken cancellationToken)
        {
            string pkiRoot = Path.Combine(
                Path.GetTempPath(), "OpcUaNetworkTests", Guid.NewGuid().ToString("N")[..8]);
            ReferenceNetworkModelProvider provider = NetworkScenario.Create();
            var watchRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var providerBoundary = new FaultInjectingProvider(provider)
            {
                ReadTransform = snapshot => snapshot with
                {
                    Timestamp = DateTime.SpecifyKind(snapshot.Timestamp.AddSeconds(1), kind)
                },
                WatchRelease = watchRelease
            };
            ServerFixture<StandardServer>? server = null;
            ClientFixture? clientFixture = null;
            Client.ISession? session = null;
            try
            {
                server = new ServerFixture<StandardServer>(telemetry =>
                {
                    var standardServer = new StandardServer(telemetry);
                    standardServer.AddNodeManager(new NetworkModelNodeManagerFactory(
                        providerBoundary, _ => false));
                    return standardServer;
                })
                {
                    SecurityNone = true,
                    AutoAccept = true
                };
                await server.LoadConfigurationAsync(pkiRoot).ConfigureAwait(false);
                await server.StartAsync(pkiRoot).ConfigureAwait(false);
                clientFixture = new ClientFixture(NUnitTelemetryContext.Create());
                await clientFixture.LoadClientConfigurationAsync(pkiRoot).ConfigureAwait(false);
                ConfiguredEndpoint endpoint = await clientFixture.GetEndpointAsync(
                    new Uri("opc.tcp://localhost:" + server.Port.ToString(CultureInfo.InvariantCulture)),
                    SecurityPolicies.None).ConfigureAwait(false);
                session = await clientFixture.ConnectAsync(endpoint).ConfigureAwait(false);
                var client = new NetworkModelClient(session, NUnitTelemetryContext.Create());

                Assert.That((await client.DiscoverInterfacesAsync(cancellationToken).ConfigureAwait(false)).Count,
                    Is.Zero);
                Assert.That((await client.DiscoverMappingTablesAsync(cancellationToken).ConfigureAwait(false)).Count,
                    Is.Zero);
                Assert.That((await client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false)).Count,
                    Is.Zero);
                Assert.That((await client.DiscoverStreamsAsync(false, cancellationToken).ConfigureAwait(false)).Count,
                    Is.Zero);
                Assert.That((await client.DiscoverLldpPortsAsync(cancellationToken).ConfigureAwait(false)).Count,
                    Is.Zero);

                NetworkModelSnapshot initial = await provider.ReadAsync(cancellationToken).ConfigureAwait(false);
                DateTime recoveryTimestamp = initial.Timestamp.AddSeconds(1);
                provider.Publish(initial with { Timestamp = recoveryTimestamp });
                watchRelease.TrySetResult(true);
                await WaitForAsync(async () =>
                {
                    ArrayOf<NodeId> interfaces = await client.DiscoverInterfacesAsync(cancellationToken)
                        .ConfigureAwait(false);
                    ArrayOf<NodeId> talkers = await client.DiscoverStreamsAsync(true, cancellationToken)
                        .ConfigureAwait(false);
                    ArrayOf<NodeId> listeners = await client.DiscoverStreamsAsync(false, cancellationToken)
                        .ConfigureAwait(false);
                    ArrayOf<NodeId> ports = await client.DiscoverLldpPortsAsync(cancellationToken)
                        .ConfigureAwait(false);
                    return interfaces.Count == 2 && talkers.Count == 1 && listeners.Count == 1 && ports.Count == 1;
                }, cancellationToken).ConfigureAwait(false);

                ArrayOf<NodeId> recoveredInterfaces = await client.DiscoverInterfacesAsync(cancellationToken)
                    .ConfigureAwait(false);
                foreach (NodeId interfaceId in recoveredInterfaces.ToList())
                {
                    NetworkInterfaceValues values = await client.ReadInterfaceAsync(
                        interfaceId, cancellationToken).ConfigureAwait(false);
                    Assert.That(values.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(values.Speed.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)recoveryTimestamp));
                }
                foreach (bool talker in new[] { true, false })
                {
                    NodeId streamId = (await client.DiscoverStreamsAsync(talker, cancellationToken)
                        .ConfigureAwait(false))[0];
                    NetworkStreamValues values = await client.ReadStreamAsync(streamId, cancellationToken)
                        .ConfigureAwait(false);
                    Assert.That(values.StreamId.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(values.StreamId.DataValue.SourceTimestamp,
                        Is.EqualTo((DateTimeUtc)recoveryTimestamp));
                }
            }
            finally
            {
                if (session is not null)
                {
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                if (clientFixture is not null)
                {
                    await clientFixture.DisposeAsync().ConfigureAwait(false);
                }
                if (server is not null)
                {
                    await server.StopAsync().ConfigureAwait(false);
                }
                if (Directory.Exists(pkiRoot))
                {
                    Directory.Delete(pkiRoot, recursive: true);
                }
            }
        }

        /// <summary>
        /// Publishes valid VLAN-tag boundaries and rejects malformed custom acquisitions without changing values.
        /// </summary>
        [TestCase((ushort)0, (byte)0, true)]
        [TestCase((ushort)4095, (byte)7, true)]
        [TestCase((ushort)4096, (byte)0, false)]
        [TestCase(ushort.MaxValue, (byte)0, false)]
        [TestCase((ushort)1, (byte)8, false)]
        [TestCase((ushort)1, byte.MaxValue, false)]
        [CancelAfter(60_000)]
        public async Task CustomProviderStreamVlanTagsAreValidatedBeforePublication(
            ushort vlanId, byte priority, bool valid, CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NodeId talker = (await m_client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false))[0];
            ArrayOf<NetworkParameterValue> original = await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeTsnVlanTagType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue before = Parameter(original, BrowseNames.VlanId);
            m_providerBoundary.WatchTransform = snapshot =>
            {
                NetworkStreamSnapshot[] streams = snapshot.Streams.Span.ToArray();
                for (int index = 0; index < streams.Length; index++)
                {
                    if (streams[index].IsTalker)
                    {
                        streams[index] = streams[index] with
                        {
                            VlanTag = new NetworkVlanTagSnapshot(vlanId, priority)
                        };
                    }
                }
                return snapshot with { Streams = ArrayOf.Wrapped(streams) };
            };
            m_provider.Publish(initial with { Timestamp = initial.Timestamp.AddSeconds(1) });
            await WaitForAsync(async () =>
            {
                ArrayOf<NetworkParameterValue> values = await ReadParameterSetAsync(
                    talker, ObjectTypeIds.IIeeeTsnVlanTagType, cancellationToken).ConfigureAwait(false);
                DataValue value = Parameter(values, BrowseNames.VlanId).DataValue;
                return valid
                    ? value.SourceTimestamp == (DateTimeUtc)initial.Timestamp.AddSeconds(1)
                    : value.StatusCode == StatusCodes.BadOutOfRange;
            }, cancellationToken).ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> actual = await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeTsnVlanTagType, cancellationToken).ConfigureAwait(false);
            DataValue actualVlan = Parameter(actual, BrowseNames.VlanId).DataValue;
            DataValue actualPriority = Parameter(actual, BrowseNames.PriorityCodePoint).DataValue;
            if (valid)
            {
                Assert.That(actualVlan.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(actualVlan.WrappedValue, Is.EqualTo(new Variant(vlanId)));
                Assert.That(actualPriority.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(actualPriority.WrappedValue, Is.EqualTo(new Variant(priority)));
            }
            else
            {
                Assert.That(actualVlan.WrappedValue.IsNull, Is.True);
                Assert.That(actualVlan.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(actualPriority.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(actualVlan.SourceTimestamp, Is.EqualTo(before.DataValue.SourceTimestamp));
                NetworkModelNodeManager manager = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                    .OfType<NetworkModelNodeManager>().Single();
                BaseVariableState retained = manager.FindPredefinedNode<BaseVariableState>(before.NodeId);
                Assert.That(retained.Value, Is.EqualTo(before.DataValue.WrappedValue));
            }

            m_providerBoundary.WatchTransform = null;
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            m_provider.Publish(current with { Timestamp = initial.Timestamp.AddSeconds(2) });
            await WaitForAsync(async () =>
            {
                ArrayOf<NetworkParameterValue> values = await ReadParameterSetAsync(
                    talker, ObjectTypeIds.IIeeeTsnVlanTagType, cancellationToken).ConfigureAwait(false);
                return Parameter(values, BrowseNames.VlanId).DataValue.SourceTimestamp ==
                    (DateTimeUtc)initial.Timestamp.AddSeconds(2);
            }, cancellationToken).ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> recovered = await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeTsnVlanTagType, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(recovered, BrowseNames.VlanId).DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(Parameter(recovered, BrowseNames.VlanId).DataValue.WrappedValue,
                Is.EqualTo(before.DataValue.WrappedValue));
        }

        /// <summary>
        /// Validates the fixed StreamId length for custom providers and recovers after rejected acquisitions.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(7, false)]
        [TestCase(8, true)]
        [TestCase(9, false)]
        [CancelAfter(60_000)]
        public async Task CustomProviderStreamIdLengthIsValidatedBeforePublication(
            int length, bool valid, CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NodeId talker = (await m_client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false))[0];
            NetworkStreamValues before = await m_client.ReadStreamAsync(talker, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(before.StreamId.DataValue.WrappedValue.TryGetValue(out ByteString previousId), Is.True);
            BaseVariableState retained = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single()
                .FindPredefinedNode<BaseVariableState>(before.StreamId.NodeId);
            byte[] bytes = Enumerable.Range(1, length).Select(value => (byte)value).ToArray();
            if (length == 8 && bytes.AsSpan().SequenceEqual(previousId.ToArray()))
            {
                bytes[0] = byte.MaxValue;
            }
            ByteString replacementId = ByteString.From(bytes.AsSpan());
            m_providerBoundary.WatchTransform = snapshot =>
            {
                NetworkStreamSnapshot[] streams = snapshot.Streams.Span.ToArray();
                for (int index = 0; index < streams.Length; index++)
                {
                    if (streams[index].IsTalker)
                    {
                        streams[index] = streams[index] with { StreamId = replacementId };
                    }
                }
                return snapshot with { Streams = ArrayOf.Wrapped(streams) };
            };
            m_provider.Publish(initial with { Timestamp = initial.Timestamp.AddSeconds(1) });
            await WaitForAsync(async () =>
            {
                NetworkStreamValues values = await m_client.ReadStreamAsync(talker, cancellationToken)
                    .ConfigureAwait(false);
                return valid
                    ? values.StreamId.DataValue.StatusCode == StatusCodes.Good &&
                        values.StreamId.DataValue.SourceTimestamp == (DateTimeUtc)initial.Timestamp.AddSeconds(1)
                    : values.StreamId.DataValue.StatusCode == StatusCodes.BadOutOfRange;
            }, cancellationToken).ConfigureAwait(false);

            NetworkStreamValues actual = await m_client.ReadStreamAsync(talker, cancellationToken)
                .ConfigureAwait(false);
            if (valid)
            {
                Assert.That(actual.StreamId.DataValue.WrappedValue.TryGetValue(out ByteString publishedId), Is.True);
                Assert.That(publishedId.ToArray(), Is.EqualTo(bytes));
                Assert.That(actual.StreamId.DataValue.SourceTimestamp,
                    Is.EqualTo((DateTimeUtc)initial.Timestamp.AddSeconds(1)));
                Assert.That(actual.StreamId.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            }
            else
            {
                Assert.That(actual.StreamId.DataValue.WrappedValue.IsNull, Is.True);
                Assert.That(actual.StreamId.DataValue.SourceTimestamp,
                    Is.EqualTo(before.StreamId.DataValue.SourceTimestamp));
                Assert.That(actual.StreamId.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(retained.Value, Is.EqualTo(before.StreamId.DataValue.WrappedValue));
                Assert.That(retained.Timestamp, Is.EqualTo(initial.Timestamp));
                Assert.That((await m_client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false))
                    .ToArray(), Is.EqualTo(new[] { talker }));
            }

            m_providerBoundary.WatchTransform = null;
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            m_provider.Publish(current with { Timestamp = initial.Timestamp.AddSeconds(2) });
            await WaitForAsync(async () =>
            {
                NetworkStreamValues values = await m_client.ReadStreamAsync(talker, cancellationToken)
                    .ConfigureAwait(false);
                return values.StreamId.DataValue.StatusCode == StatusCodes.Good &&
                    values.StreamId.DataValue.SourceTimestamp == (DateTimeUtc)initial.Timestamp.AddSeconds(2);
            }, cancellationToken).ConfigureAwait(false);
            NetworkStreamValues recovered = await m_client.ReadStreamAsync(talker, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(recovered.StreamId.DataValue.WrappedValue.TryGetValue(out ByteString recoveredId), Is.True);
            Assert.That(recoveredId.ToArray(), Is.EqualTo(previousId.ToArray()));
            Assert.That(recovered.StreamId.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Validates optional stream SR-class identifiers on custom-provider acquisitions.
        /// </summary>
        [TestCase(false, (byte)0, true)]
        [TestCase(true, (byte)0, true)]
        [TestCase(true, (byte)7, true)]
        [TestCase(true, (byte)8, false)]
        [CancelAfter(60_000)]
        public async Task CustomProviderStreamSrClassIdBoundariesAreValidatedBeforePublication(
            bool includeId, byte id, bool valid, CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NodeId talker = (await m_client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false))[0];
            NetworkStreamValues before = await m_client.ReadStreamAsync(talker, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(before.StreamId.DataValue.WrappedValue.TryGetValue(out ByteString previousId), Is.True);
            ArrayOf<ReferenceDescription> originalTalkers = await m_client.BrowseObjectsAsync(
                ObjectIds.TalkerStreams, cancellationToken).ConfigureAwait(false);
            BaseVariableState retained = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single()
                .FindPredefinedNode<BaseVariableState>(before.SrClassId.NodeId);
            BaseVariableState retainedStreamId = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single()
                .FindPredefinedNode<BaseVariableState>(before.StreamId.NodeId);
            byte? srClassId = includeId ? id : null;
            m_providerBoundary.WatchTransform = snapshot =>
            {
                NetworkStreamSnapshot[] streams = snapshot.Streams.Span.ToArray();
                for (int index = 0; index < streams.Length; index++)
                {
                    if (streams[index].IsTalker)
                    {
                        streams[index] = streams[index] with { SrClassId = srClassId };
                    }
                }
                return snapshot with { Streams = ArrayOf.Wrapped(streams) };
            };
            m_provider.Publish(initial with { Timestamp = initial.Timestamp.AddSeconds(1) });
            await WaitForAsync(async () =>
            {
                NetworkStreamValues values = await m_client.ReadStreamAsync(talker, cancellationToken)
                    .ConfigureAwait(false);
                return valid
                    ? values.StreamId.DataValue.SourceTimestamp == (DateTimeUtc)initial.Timestamp.AddSeconds(1) &&
                        (includeId
                            ? values.SrClassId.DataValue.StatusCode == StatusCodes.Good
                            : values.SrClassId.DataValue.StatusCode == StatusCodes.BadNotSupported)
                    : values.SrClassId.DataValue.StatusCode == StatusCodes.BadOutOfRange;
            }, cancellationToken).ConfigureAwait(false);

            NetworkStreamValues actual = await m_client.ReadStreamAsync(talker, cancellationToken)
                .ConfigureAwait(false);
            if (valid)
            {
                if (includeId)
                {
                    Assert.That(actual.SrClassId.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(actual.SrClassId.DataValue.WrappedValue, Is.EqualTo(new Variant(id)));
                }
                else
                {
                    Assert.That(actual.SrClassId.NodeId.IsNull, Is.True);
                    Assert.That(actual.SrClassId.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
                }
                Assert.That(actual.StreamId.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(actual.StreamId.DataValue.SourceTimestamp,
                    Is.EqualTo((DateTimeUtc)initial.Timestamp.AddSeconds(1)));
            }
            else
            {
                Assert.That(actual.SrClassId.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(actual.SrClassId.DataValue.WrappedValue.IsNull, Is.True);
                Assert.That(actual.SrClassId.DataValue.SourceTimestamp,
                    Is.EqualTo(before.SrClassId.DataValue.SourceTimestamp));
                Assert.That(actual.StreamId.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(actual.StreamId.DataValue.WrappedValue.IsNull, Is.True);
                Assert.That(actual.StreamId.DataValue.SourceTimestamp,
                    Is.EqualTo(before.StreamId.DataValue.SourceTimestamp));
                Assert.That((await m_client.BrowseObjectsAsync(ObjectIds.TalkerStreams, cancellationToken)
                    .ConfigureAwait(false)).ToList().Select(reference => reference.NodeId),
                    Is.EquivalentTo(originalTalkers.ToList().Select(reference => reference.NodeId)));
                Assert.That(retained.Value, Is.EqualTo(before.SrClassId.DataValue.WrappedValue));
                Assert.That(retained.Timestamp, Is.EqualTo(initial.Timestamp));
                Assert.That(retainedStreamId.Value, Is.EqualTo(before.StreamId.DataValue.WrappedValue));
                Assert.That(retainedStreamId.Timestamp, Is.EqualTo(initial.Timestamp));
            }

            m_providerBoundary.WatchTransform = null;
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            m_provider.Publish(current with { Timestamp = initial.Timestamp.AddSeconds(2) });
            await WaitForAsync(async () =>
                (await m_client.ReadStreamAsync(talker, cancellationToken).ConfigureAwait(false))
                    .StreamId.DataValue.SourceTimestamp == (DateTimeUtc)initial.Timestamp.AddSeconds(2),
                cancellationToken).ConfigureAwait(false);
            NetworkStreamValues recovered = await m_client.ReadStreamAsync(talker, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(recovered.SrClassId.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(recovered.SrClassId.DataValue.WrappedValue, Is.EqualTo(new Variant((byte)6)));
            Assert.That(recovered.StreamId.DataValue.WrappedValue.TryGetValue(out ByteString recoveredId), Is.True);
            Assert.That(recoveredId.ToArray(), Is.EqualTo(previousId.ToArray()));
        }

        /// <summary>
        /// Validates each failure-system identifier row before publishing the two-dimensional value.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task CustomProviderFailureSystemIdentifierRowsAreValidatedBeforePublication(
            CancellationToken cancellationToken)
        {
            static ByteString Identifier(byte first)
            {
                return ByteString.From(Enumerable.Range(first, 8).Select(value => (byte)value).ToArray().AsSpan());
            }

            static NetworkModelSnapshot WithTalkerStatus(
                NetworkModelSnapshot snapshot, NetworkStreamStatusSnapshot status)
            {
                NetworkStreamSnapshot[] streams = snapshot.Streams.Span.ToArray();
                for (int index = 0; index < streams.Length; index++)
                {
                    if (streams[index].IsTalker)
                    {
                        streams[index] = streams[index] with { Status = status };
                    }
                }
                return snapshot with { Streams = ArrayOf.Wrapped(streams) };
            }

            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NodeId talker = (await m_client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false))[0];
            NetworkStreamStatusSnapshot baselineStatus = new(TsnFailureCode.NoFailure)
            {
                FailureSystemIdentifiers = [Identifier(1), Identifier(9)]
            };
            m_provider.Publish(WithTalkerStatus(initial, baselineStatus) with
            {
                Timestamp = initial.Timestamp.AddSeconds(1)
            });
            await WaitForAsync(async () =>
            {
                if (!await HasEnumObservationAsync("failure code", cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }
                ArrayOf<NetworkParameterValue> values = await ReadParameterSetAsync(
                    talker, ObjectTypeIds.IIeeeBaseTsnStatusStreamType, cancellationToken).ConfigureAwait(false);
                return Parameter(values, BrowseNames.FailureSystemIdentifier).DataValue.SourceTimestamp ==
                    (DateTimeUtc)initial.Timestamp.AddSeconds(1);
            }, cancellationToken).ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> baseline = await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeBaseTsnStatusStreamType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue failure = Parameter(baseline, BrowseNames.FailureSystemIdentifier);
            Assert.That(failure.DataValue.WrappedValue.TryGetValue(out MatrixOf<byte> previousMatrix), Is.True);
            Assert.That(previousMatrix.Dimensions, Is.EqualTo(s_failureSystemIdentifierDimensions));
            byte[] previousBytes = Identifier(1).ToArray().Concat(Identifier(9).ToArray()).ToArray();
            Assert.That(previousMatrix.Span.ToArray(), Is.EqualTo(previousBytes));
            BaseVariableState retained = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single()
                .FindPredefinedNode<BaseVariableState>(failure.NodeId);

            NetworkStreamStatusSnapshot malformedStatus = new(TsnFailureCode.NoFailure)
            {
                FailureSystemIdentifiers =
                [
                    ByteString.From(new byte[7].AsSpan()),
                    ByteString.From(new byte[9].AsSpan())
                ]
            };
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            m_providerBoundary.WatchTransform = snapshot => WithTalkerStatus(snapshot, malformedStatus);
            m_provider.Publish(current with { Timestamp = current.Timestamp.AddSeconds(1) });
            await WaitForAsync(async () =>
            {
                ArrayOf<NetworkParameterValue> values = await ReadParameterSetAsync(
                    talker, ObjectTypeIds.IIeeeBaseTsnStatusStreamType, cancellationToken).ConfigureAwait(false);
                return Parameter(values, BrowseNames.FailureSystemIdentifier).DataValue.StatusCode ==
                    StatusCodes.BadOutOfRange;
            }, cancellationToken).ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> rejected = await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeBaseTsnStatusStreamType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue rejectedFailure = Parameter(rejected, BrowseNames.FailureSystemIdentifier);
            Assert.That(rejectedFailure.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(rejectedFailure.DataValue.SourceTimestamp,
                Is.EqualTo((DateTimeUtc)initial.Timestamp.AddSeconds(1)));
            Assert.That(rejectedFailure.DataValue.WrappedValue.IsNull, Is.True);
            Assert.That(retained.Value, Is.EqualTo(failure.DataValue.WrappedValue));
            Assert.That(retained.Timestamp, Is.EqualTo(initial.Timestamp.AddSeconds(1)));

            NetworkStreamStatusSnapshot recoveredStatus = new(TsnFailureCode.NoFailure)
            {
                FailureSystemIdentifiers = [Identifier(17), Identifier(25)]
            };
            m_providerBoundary.WatchTransform = null;
            current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            DateTime recoveryTimestamp = current.Timestamp.AddSeconds(1);
            m_provider.Publish(WithTalkerStatus(current, recoveredStatus) with { Timestamp = recoveryTimestamp });
            await WaitForAsync(async () =>
            {
                ArrayOf<NetworkParameterValue> values = await ReadParameterSetAsync(
                    talker, ObjectTypeIds.IIeeeBaseTsnStatusStreamType, cancellationToken).ConfigureAwait(false);
                DataValue dataValue = Parameter(values, BrowseNames.FailureSystemIdentifier).DataValue;
                return dataValue.StatusCode == StatusCodes.Good &&
                    dataValue.SourceTimestamp == (DateTimeUtc)recoveryTimestamp;
            }, cancellationToken).ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> recovered = await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeBaseTsnStatusStreamType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue recoveredFailure = Parameter(recovered, BrowseNames.FailureSystemIdentifier);
            Assert.That(recoveredFailure.DataValue.WrappedValue.TryGetValue(
                out MatrixOf<byte> recoveredMatrix), Is.True);
            Assert.That(recoveredMatrix.Dimensions, Is.EqualTo(s_failureSystemIdentifierDimensions));
            Assert.That(recoveredMatrix.Span.ToArray(),
                Is.EqualTo(Identifier(17).ToArray().Concat(Identifier(25).ToArray()).ToArray()));
            Assert.That(recoveredFailure.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Validates custom-provider stream MAC addresses while preserving absent optional values and recovery.
        /// </summary>
        [TestCase(0, 0, true)]
        [TestCase(6, 0, true)]
        [TestCase(6, 6, true)]
        [TestCase(5, 0, false)]
        [TestCase(0, 6, false)]
        [TestCase(6, 5, false)]
        [CancelAfter(60_000)]
        public async Task CustomProviderStreamMacAddressesAreValidatedBeforePublication(
            int destinationLength, int sourceLength, bool valid, CancellationToken cancellationToken)
        {
            static ByteString Address(int length, byte first)
            {
                return ByteString.From(
                    Enumerable.Range(first, length).Select(value => (byte)value).ToArray().AsSpan());
            }

            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NodeId talker = (await m_client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false))[0];
            NetworkStreamValues before = await m_client.ReadStreamAsync(talker, cancellationToken)
                .ConfigureAwait(false);
            NetworkModelNodeManager manager = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single();
            BaseVariableState retainedState = manager.FindPredefinedNode<BaseVariableState>(before.State.NodeId);
            ArrayOf<NetworkParameterValue> beforeMac = await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeTsnMacAddressType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue beforeDestination = Parameter(beforeMac, BrowseNames.DestinationAddress);
            NetworkParameterValue beforeSource = Parameter(beforeMac, BrowseNames.SourceAddress);
            Assert.That(beforeDestination.DataValue.WrappedValue.TryGetValue(
                out ArrayOf<byte> previousDestination), Is.True);
            BaseVariableState retainedDestination = manager.FindPredefinedNode<BaseVariableState>(
                beforeDestination.NodeId);
            BaseVariableState retainedSource = manager.FindPredefinedNode<BaseVariableState>(beforeSource.NodeId);
            ByteString destination = Address(destinationLength, 1);
            ByteString source = Address(sourceLength, 7);
            m_providerBoundary.WatchTransform = snapshot =>
            {
                NetworkStreamSnapshot[] streams = snapshot.Streams.Span.ToArray();
                for (int index = 0; index < streams.Length; index++)
                {
                    if (streams[index].IsTalker)
                    {
                        streams[index] = streams[index] with
                        {
                            DestinationAddress = destination,
                            SourceAddress = source
                        };
                    }
                }
                return snapshot with { Streams = ArrayOf.Wrapped(streams) };
            };
            m_provider.Publish(initial with { Timestamp = initial.Timestamp.AddSeconds(1) });
            await WaitForAsync(async () =>
            {
                NetworkStreamValues values = await m_client.ReadStreamAsync(talker, cancellationToken)
                    .ConfigureAwait(false);
                return valid
                    ? values.State.DataValue.StatusCode == StatusCodes.Good &&
                        values.State.DataValue.SourceTimestamp == (DateTimeUtc)initial.Timestamp.AddSeconds(1)
                    : values.State.DataValue.StatusCode == StatusCodes.BadOutOfRange;
            }, cancellationToken).ConfigureAwait(false);

            NetworkStreamValues actual = await m_client.ReadStreamAsync(talker, cancellationToken)
                .ConfigureAwait(false);
            bool hasMacInterface = await HasInterfaceAsync(
                talker, ObjectTypeIds.IIeeeTsnMacAddressType, cancellationToken).ConfigureAwait(false);
            if (valid)
            {
                Assert.That(actual.State.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(actual.State.DataValue.SourceTimestamp,
                    Is.EqualTo((DateTimeUtc)initial.Timestamp.AddSeconds(1)));
                Assert.That(hasMacInterface, Is.EqualTo(destinationLength == 6));
                if (destinationLength == 6)
                {
                    ArrayOf<NetworkParameterValue> actualMac = await ReadParameterSetAsync(
                        talker, ObjectTypeIds.IIeeeTsnMacAddressType, cancellationToken).ConfigureAwait(false);
                    Assert.That(Parameter(actualMac, BrowseNames.DestinationAddress).DataValue.WrappedValue
                        .TryGetValue(out ArrayOf<byte> actualDestination), Is.True);
                    Assert.That(actualDestination.ToArray(), Is.EqualTo(destination.ToArray()));
                    bool hasSource = actualMac.ToList()
                        .Any(value => value.BrowseName.Name == BrowseNames.SourceAddress);
                    Assert.That(hasSource, Is.EqualTo(sourceLength == 6));
                    if (sourceLength == 6)
                    {
                        Assert.That(Parameter(actualMac, BrowseNames.SourceAddress).DataValue.WrappedValue
                            .TryGetValue(out ArrayOf<byte> actualSource), Is.True);
                        Assert.That(actualSource.ToArray(), Is.EqualTo(source.ToArray()));
                    }
                }
            }
            else
            {
                Assert.That(actual.State.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(actual.State.DataValue.WrappedValue.IsNull, Is.True);
                Assert.That(actual.State.DataValue.SourceTimestamp,
                    Is.EqualTo(before.State.DataValue.SourceTimestamp));
                Assert.That(retainedState.Value, Is.EqualTo(before.State.DataValue.WrappedValue));
                Assert.That(retainedState.Timestamp, Is.EqualTo(initial.Timestamp));
                Assert.That(hasMacInterface, Is.True);
                ArrayOf<NetworkParameterValue> rejectedMac = await ReadParameterSetAsync(
                    talker, ObjectTypeIds.IIeeeTsnMacAddressType, cancellationToken).ConfigureAwait(false);
                NetworkParameterValue rejectedDestination = Parameter(rejectedMac, BrowseNames.DestinationAddress);
                NetworkParameterValue rejectedSource = Parameter(rejectedMac, BrowseNames.SourceAddress);
                Assert.That(rejectedDestination.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(rejectedSource.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(rejectedDestination.DataValue.WrappedValue.IsNull, Is.True);
                Assert.That(rejectedSource.DataValue.WrappedValue.IsNull, Is.True);
                Assert.That(rejectedDestination.DataValue.SourceTimestamp,
                    Is.EqualTo(beforeDestination.DataValue.SourceTimestamp));
                Assert.That(rejectedSource.DataValue.SourceTimestamp,
                    Is.EqualTo(beforeSource.DataValue.SourceTimestamp));
                Assert.That(retainedDestination.Value, Is.EqualTo(beforeDestination.DataValue.WrappedValue));
                Assert.That(retainedDestination.Timestamp, Is.EqualTo(initial.Timestamp));
                Assert.That(retainedSource.Value, Is.EqualTo(beforeSource.DataValue.WrappedValue));
                Assert.That(retainedSource.Timestamp, Is.EqualTo(initial.Timestamp));
            }

            m_providerBoundary.WatchTransform = null;
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            DateTime recoveryTimestamp = current.Timestamp.AddSeconds(1);
            m_provider.Publish(current with { Timestamp = recoveryTimestamp });
            await WaitForAsync(async () =>
            {
                NetworkStreamValues values = await m_client.ReadStreamAsync(talker, cancellationToken)
                    .ConfigureAwait(false);
                return values.State.DataValue.StatusCode == StatusCodes.Good &&
                    values.State.DataValue.SourceTimestamp == (DateTimeUtc)recoveryTimestamp;
            }, cancellationToken).ConfigureAwait(false);
            Assert.That(await HasInterfaceAsync(
                talker, ObjectTypeIds.IIeeeTsnMacAddressType, cancellationToken).ConfigureAwait(false), Is.True);
            ArrayOf<NetworkParameterValue> recoveredMac = await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeTsnMacAddressType, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(recoveredMac, BrowseNames.DestinationAddress).DataValue.WrappedValue
                .TryGetValue(out ArrayOf<byte> recoveredDestination), Is.True);
            Assert.That(recoveredDestination.ToArray(), Is.EqualTo(previousDestination.ToArray()));
            Assert.That(Parameter(recoveredMac, BrowseNames.DestinationAddress).DataValue.StatusCode,
                Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Validates custom-provider traffic intervals while preserving absent values and recovery.
        /// </summary>
        [TestCase(false, 0u, 0u)]
        [TestCase(true, 0u, 1u)]
        [TestCase(true, uint.MaxValue, uint.MaxValue)]
        [TestCase(true, 1u, 0u)]
        [CancelAfter(60_000)]
        public async Task CustomProviderTrafficIntervalsAreValidatedBeforePublication(
            bool includeTraffic, uint numerator, uint denominator, CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NodeId talker = (await m_client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false))[0];
            ArrayOf<NetworkParameterValue> previousTraffic = await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeBaseTsnTrafficSpecificationType, cancellationToken).ConfigureAwait(false);
            NetworkParameterValue previousInterval = Parameter(previousTraffic, BrowseNames.Interval);
            Assert.That(previousInterval.DataValue.WrappedValue
                .TryGetStructure<UnsignedRationalNumber>(out UnsignedRationalNumber? previousRational), Is.True);
            if (previousRational is null)
            {
                throw new InvalidOperationException("Decoded TSN traffic interval is null.");
            }
            BaseVariableState retainedInterval = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single()
                .FindPredefinedNode<BaseVariableState>(previousInterval.NodeId);

            NetworkTrafficSnapshot? desiredTraffic = includeTraffic
                ? new NetworkTrafficSnapshot(1, 512, numerator, denominator)
                : null;
            bool invalid = includeTraffic && denominator == 0;
            m_providerBoundary.WatchTransform = snapshot =>
            {
                NetworkStreamSnapshot[] streams = snapshot.Streams.Span.ToArray();
                for (int index = 0; index < streams.Length; index++)
                {
                    if (streams[index].IsTalker)
                    {
                        streams[index] = streams[index] with { Traffic = desiredTraffic };
                    }
                }
                return snapshot with { Streams = ArrayOf.Wrapped(streams) };
            };
            m_provider.Publish(initial with { Timestamp = initial.Timestamp.AddSeconds(1) });
            await WaitForAsync(async () =>
            {
                NetworkStreamValues values = await m_client.ReadStreamAsync(talker, cancellationToken)
                    .ConfigureAwait(false);
                return invalid
                    ? values.State.DataValue.StatusCode == StatusCodes.BadOutOfRange
                    : values.State.DataValue.StatusCode == StatusCodes.Good &&
                        values.State.DataValue.SourceTimestamp == (DateTimeUtc)initial.Timestamp.AddSeconds(1);
            }, cancellationToken).ConfigureAwait(false);

            NetworkStreamValues actual = await m_client.ReadStreamAsync(talker, cancellationToken)
                .ConfigureAwait(false);
            bool hasTrafficInterface = await HasInterfaceAsync(
                talker, ObjectTypeIds.IIeeeBaseTsnTrafficSpecificationType, cancellationToken)
                .ConfigureAwait(false);
            if (invalid)
            {
                Assert.That(actual.State.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(actual.State.DataValue.SourceTimestamp, Is.EqualTo(
                    (DateTimeUtc)previousInterval.DataValue.SourceTimestamp));
                Assert.That(hasTrafficInterface, Is.True);
                ArrayOf<NetworkParameterValue> rejectedTraffic = await ReadParameterSetAsync(
                    talker, ObjectTypeIds.IIeeeBaseTsnTrafficSpecificationType, cancellationToken)
                    .ConfigureAwait(false);
                NetworkParameterValue rejectedInterval = Parameter(rejectedTraffic, BrowseNames.Interval);
                Assert.That(rejectedInterval.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
                Assert.That(rejectedInterval.DataValue.WrappedValue.IsNull, Is.True);
                Assert.That(rejectedInterval.DataValue.SourceTimestamp,
                    Is.EqualTo(previousInterval.DataValue.SourceTimestamp));
                Assert.That(retainedInterval.Value, Is.EqualTo(previousInterval.DataValue.WrappedValue));
                Assert.That(retainedInterval.Timestamp, Is.EqualTo(initial.Timestamp));
            }
            else if (!includeTraffic)
            {
                Assert.That(actual.State.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(hasTrafficInterface, Is.False);
            }
            else
            {
                Assert.That(actual.State.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(hasTrafficInterface, Is.True);
                ArrayOf<NetworkParameterValue> publishedTraffic = await ReadParameterSetAsync(
                    talker, ObjectTypeIds.IIeeeBaseTsnTrafficSpecificationType, cancellationToken)
                    .ConfigureAwait(false);
                NetworkParameterValue interval = Parameter(publishedTraffic, BrowseNames.Interval);
                Assert.That(interval.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(interval.DataValue.WrappedValue
                    .TryGetStructure<UnsignedRationalNumber>(out UnsignedRationalNumber? publishedRational), Is.True);
                if (publishedRational is null)
                {
                    throw new InvalidOperationException("Decoded TSN traffic interval is null.");
                }
                Assert.That(publishedRational.Numerator, Is.EqualTo(numerator));
                Assert.That(publishedRational.Denominator, Is.EqualTo(denominator));
            }

            m_providerBoundary.WatchTransform = null;
            NetworkModelSnapshot current = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            DateTime recoveryTimestamp = current.Timestamp.AddSeconds(1);
            m_provider.Publish(current with { Timestamp = recoveryTimestamp });
            await WaitForAsync(async () =>
            {
                NetworkStreamValues values = await m_client.ReadStreamAsync(talker, cancellationToken)
                    .ConfigureAwait(false);
                return values.State.DataValue.StatusCode == StatusCodes.Good &&
                    values.State.DataValue.SourceTimestamp == (DateTimeUtc)recoveryTimestamp;
            }, cancellationToken).ConfigureAwait(false);
            Assert.That(await HasInterfaceAsync(
                talker, ObjectTypeIds.IIeeeBaseTsnTrafficSpecificationType, cancellationToken)
                .ConfigureAwait(false), Is.True);
            ArrayOf<NetworkParameterValue> recoveredTraffic = await ReadParameterSetAsync(
                talker, ObjectTypeIds.IIeeeBaseTsnTrafficSpecificationType, cancellationToken)
                .ConfigureAwait(false);
            NetworkParameterValue recoveredInterval = Parameter(recoveredTraffic, BrowseNames.Interval);
            Assert.That(recoveredInterval.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(recoveredInterval.DataValue.WrappedValue
                .TryGetStructure<UnsignedRationalNumber>(out UnsignedRationalNumber? recoveredRational), Is.True);
            if (recoveredRational is null)
            {
                throw new InvalidOperationException("Decoded TSN traffic interval is null.");
            }
            Assert.That(recoveredRational.Numerator, Is.EqualTo(previousRational.Numerator));
            Assert.That(recoveredRational.Denominator, Is.EqualTo(previousRational.Denominator));
        }

        /// <summary>
        /// Rejects timestamp regression even when the provider advances its revision.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task HigherRevisionWithOlderTimestampIsRejectedBeforeMutation(
            CancellationToken cancellationToken)
        {
            NodeId id = (await m_client.DiscoverInterfacesAsync(cancellationToken).ConfigureAwait(false))[0];
            NetworkInterfaceValues initial = await m_client.ReadInterfaceAsync(id, cancellationToken)
                .ConfigureAwait(false);
            NetworkModelSnapshot snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            DateTime staleTimestamp = snapshot.Timestamp.AddSeconds(-1);
            var delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_providerBoundary.WatchTransform = value => value with { Timestamp = staleTimestamp };
            m_providerBoundary.AfterDelivery = value =>
            {
                if (value.Timestamp == staleTimestamp)
                {
                    delivered.TrySetResult(true);
                }
            };
            NetworkInterfaceSnapshot[] interfaces = snapshot.Interfaces.Span.ToArray();
            for (int index = 0; index < interfaces.Length; index++)
            {
                interfaces[index] = interfaces[index] with { Speed = 100_000_000 };
            }
            m_provider.Publish(snapshot with
            {
                Timestamp = snapshot.Timestamp.AddSeconds(1),
                Interfaces = ArrayOf.Wrapped(interfaces)
            });
            await delivered.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            NetworkInterfaceValues rejected = await m_client.ReadInterfaceAsync(id, cancellationToken)
                .ConfigureAwait(false);

            Assert.That(rejected.Speed.DataValue.WrappedValue, Is.EqualTo(initial.Speed.DataValue.WrappedValue));
            Assert.That(rejected.Speed.DataValue.SourceTimestamp, Is.EqualTo(initial.Speed.DataValue.SourceTimestamp));
            Assert.That(rejected.Speed.DataValue.StatusCode, Is.EqualTo(initial.Speed.DataValue.StatusCode));
            m_providerBoundary.WatchTransform = value => value with { Revision = snapshot.Revision + 1 };
            m_provider.Publish(snapshot with
            {
                Revision = snapshot.Revision + 1,
                Timestamp = snapshot.Timestamp.AddSeconds(2),
                Interfaces = ArrayOf.Wrapped(interfaces)
            });
            await WaitForAsync(async () =>
                (await m_client.ReadInterfaceAsync(id, cancellationToken).ConfigureAwait(false))
                    .Speed.DataValue.WrappedValue == new Variant(100_000_000UL), cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Retains acquired values with bad quality until a newer snapshot recovers observation.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [CancelAfter(60_000)]
        public async Task ProviderWatchFailurePublishesBadQualityUntilANewerAcquisitionRecovers(
            int failureKind, CancellationToken cancellationToken)
        {
            NetworkModelNodeManager manager = m_server.Server.CurrentInstance.NodeManager.AsyncNodeManagers
                .OfType<NetworkModelNodeManager>().Single();
            NodeId id = (await m_client.DiscoverInterfacesAsync(cancellationToken).ConfigureAwait(false))[0];
            NetworkInterfaceValues initial = await m_client.ReadInterfaceAsync(id, cancellationToken)
                .ConfigureAwait(false);
            NetworkModelSnapshot snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            StatusCode expectedStatus = failureKind == 0
                ? StatusCodes.BadNoCommunication : StatusCodes.BadCommunicationError;
            m_providerBoundary.UnexpectedFailure = failureKind == 1;
            m_providerBoundary.CompleteWatch = failureKind == 2;
            m_providerBoundary.FailWatch = true;
            m_provider.Publish(snapshot with { Timestamp = snapshot.Timestamp.AddSeconds(1) });

            await WaitForAsync(async () =>
                (await m_client.ReadInterfaceAsync(id, cancellationToken).ConfigureAwait(false))
                    .Speed.DataValue.StatusCode == expectedStatus, cancellationToken)
                .ConfigureAwait(false);
            NetworkInterfaceValues failed = await m_client.ReadInterfaceAsync(id, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(failed.Speed.DataValue.WrappedValue.IsNull, Is.True);
            Assert.That(failed.Speed.DataValue.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(failed.Speed.DataValue.SourceTimestamp, Is.EqualTo(initial.Speed.DataValue.SourceTimestamp));
            Assert.That(failed.AdminStatus.DataValue.StatusCode, Is.EqualTo(expectedStatus));
            BaseVariableState retained = manager.FindPredefinedNode<BaseVariableState>(initial.Speed.NodeId);
            Assert.That(retained.Value, Is.EqualTo(initial.Speed.DataValue.WrappedValue));
            Assert.That(retained.StatusCode, Is.EqualTo(expectedStatus));
            Assert.That(retained.Timestamp, Is.EqualTo(initial.Speed.DataValue.SourceTimestamp));

            m_provider.Publish(snapshot with
            {
                Revision = snapshot.Revision + 1,
                Timestamp = snapshot.Timestamp.AddSeconds(2)
            });
            m_providerBoundary.FailWatch = false;
            await WaitForAsync(async () =>
                (await m_client.ReadInterfaceAsync(id, cancellationToken).ConfigureAwait(false))
                    .Speed.DataValue.StatusCode == StatusCodes.Good, cancellationToken).ConfigureAwait(false);
            NetworkInterfaceValues recovered = await m_client.ReadInterfaceAsync(id, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(recovered.Speed.DataValue.WrappedValue, Is.EqualTo(initial.Speed.DataValue.WrappedValue));
            Assert.That(recovered.AdminStatus.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(recovered.Speed.DataValue.SourceTimestamp,
                Is.EqualTo((DateTimeUtc)snapshot.Timestamp.AddSeconds(2)));
        }

        /// <summary>
        /// Verifies that generated LLDP child accessors resolve the diagnostics-owned standard nodes.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task LldpUsesUniqueStandardChildrenAndPublishesLocalAndRemoteIdentity(
            CancellationToken cancellationToken)
        {
            ArrayOf<ReferenceDescription> children = await m_client.BrowseObjectsAsync(
                ObjectIds.LLDP, cancellationToken).ConfigureAwait(false);
            Assert.That(children.ToList().Count(value =>
                value.BrowseName == new QualifiedName(BrowseNames.LocalSystemData, 0)), Is.EqualTo(1));
            Assert.That(children.ToList().Count(value =>
                value.BrowseName == new QualifiedName(BrowseNames.Ports, 0)), Is.EqualTo(1));
            LldpLocalSystemTypeClient? localProxy = await m_client.GetLldp(ObjectIds.LLDP)
                .GetLocalSystemDataAsync(NUnitTelemetryContext.Create(), cancellationToken).ConfigureAwait(false);
            Assert.That(localProxy, Is.Not.Null);
            Assert.That(localProxy!.ObjectId, Is.EqualTo(ObjectIds.LLDP_LocalSystemData));
            ArrayOf<NetworkParameterValue> local = await m_client.ReadParametersAsync(
                localProxy.ObjectId, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(local, BrowseNames.ChassisIdSubtype).DataValue.WrappedValue,
                Is.EqualTo(new Variant((int)ChassisIdSubtype.MacAddress)));
            Assert.That(Parameter(local, BrowseNames.ChassisId).DataValue.WrappedValue,
                Is.EqualTo(new Variant("02:00:00:00:00:01")));
            Assert.That(Parameter(local, BrowseNames.SystemName).DataValue.WrappedValue,
                Is.EqualTo(new Variant("Simulated endpoint")));
            Assert.That(Parameter(local, BrowseNames.SystemDescription).DataValue.WrappedValue,
                Is.EqualTo(new Variant("Part 22 reference provider; no hardware control")));

            NodeId port = (await m_client.DiscoverLldpPortsAsync(cancellationToken).ConfigureAwait(false))[0];
            ArrayOf<NetworkParameterValue> portValues = await m_client.ReadParametersAsync(port, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(Parameter(portValues, BrowseNames.DestMacAddress).DataValue.WrappedValue
                .TryGetValue(out ArrayOf<byte> destination), Is.True);
            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 1, 128, 194, 0, 0, 14 }));
            Assert.That(Parameter(portValues, BrowseNames.IetfBaseNetworkInterfaceName).DataValue.WrappedValue,
                Is.EqualTo(new Variant("eth0")));
            NetworkModelSnapshot snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            m_provider.Publish(NetworkScenario.Advance(snapshot, 1));
            await WaitForAsync(async () =>
                (await m_client.DiscoverNeighborsAsync(port, cancellationToken).ConfigureAwait(false)).Count == 1,
                cancellationToken).ConfigureAwait(false);
            NodeId neighbor = (await m_client.DiscoverNeighborsAsync(port, cancellationToken)
                .ConfigureAwait(false))[0];
            ArrayOf<NetworkParameterValue> remote = await m_client.ReadParametersAsync(neighbor, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(Parameter(remote, BrowseNames.TimeMark).DataValue.WrappedValue, Is.EqualTo(new Variant(1u)));
            Assert.That(Parameter(remote, BrowseNames.RemoteIndex).DataValue.WrappedValue,
                Is.EqualTo(new Variant(1u)));
            Assert.That(Parameter(remote, BrowseNames.ChassisIdSubtype).DataValue.WrappedValue,
                Is.EqualTo(new Variant((int)ChassisIdSubtype.MacAddress)));
            Assert.That(Parameter(remote, BrowseNames.ChassisId).DataValue.WrappedValue,
                Is.EqualTo(new Variant("02:00:00:00:00:02")));
            Assert.That(Parameter(remote, BrowseNames.PortIdSubtype).DataValue.WrappedValue,
                Is.EqualTo(new Variant((int)PortIdSubtype.InterfaceName)));
            Assert.That(Parameter(remote, BrowseNames.PortId).DataValue.WrappedValue,
                Is.EqualTo(new Variant("port1")));
            Assert.That(Parameter(remote, BrowseNames.SystemName).DataValue.WrappedValue,
                Is.EqualTo(new Variant("Simulated switch")));
        }

        /// <summary>
        /// Publishes and removes every optional LLDP data family through the provider and typed client path.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task LldpOptionalCapabilitiesArePublishedAndRemovedAsync(
            CancellationToken cancellationToken)
        {
            NetworkModelSnapshot initial = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            NetworkLldpSnapshot lldp = initial.Lldp
                ?? throw new InvalidOperationException("The test acquisition requires LLDP.");
            NetworkLldpPortSnapshot port = lldp.Ports[0] with
            {
                Description = "local port",
                ManagementAddressTxPort =
                [
                    new LldpManagementAddressTxPortType
                    {
                        AddressSubtype = 1,
                        ManAddress = "192.0.2.1",
                        TxEnable = true,
                        AddrLen = 4,
                        IfSubtype = ManAddrIfSubtype.PortRef,
                        IfId = 7
                    }
                ],
                Neighbors =
                [
                    new NetworkLldpNeighborSnapshot(
                        "remote", 1, 2, ChassisIdSubtype.MacAddress, "02:00:00:00:00:02",
                        PortIdSubtype.InterfaceName, "port1")
                    {
                        PortDescription = "remote port",
                        SystemName = "remote system",
                        SystemDescription = "remote description",
                        SystemCapabilitiesSupported = 0x4,
                        SystemCapabilitiesEnabled = 0x4,
                        RemoteChanges = true,
                        RemoteTooManyNeighbors = false,
                        ManagementAddress =
                        [
                            new LldpManagementAddressType
                            {
                                AddressSubtype = 1,
                                Address = "192.0.2.2",
                                IfSubtype = ManAddrIfSubtype.SystemPortNumber,
                                IfId = 8
                            }
                        ],
                        RemoteUnknownTlv =
                        [
                            new LldpTlvType
                            {
                                TlvType = 127,
                                TlvInfo = ByteString.From(new byte[] { 1, 2, 3 })
                            }
                        ]
                    }
                ]
            };
            NetworkLldpSnapshot configured = lldp with
            {
                SystemCapabilitiesSupported = 0x4,
                SystemCapabilitiesEnabled = 0x4,
                RemoteStatistics = new NetworkLldpRemoteStatisticsSnapshot(1234, 5, 6, 7, 8),
                Ports = [port]
            };
            DateTime configuredTimestamp = initial.Timestamp.AddSeconds(1);
            m_provider.Publish(initial with { Timestamp = configuredTimestamp, Lldp = configured });

            NodeId lldpPort = (await m_client.DiscoverLldpPortsAsync(cancellationToken).ConfigureAwait(false))[0];
            await WaitForAsync(async () =>
            {
                ArrayOf<NetworkParameterValue> values = await m_client.ReadParametersAsync(
                    lldpPort, cancellationToken).ConfigureAwait(false);
                ArrayOf<NetworkParameterValue> local = await m_client.ReadParametersAsync(
                    ObjectIds.LLDP_LocalSystemData, cancellationToken).ConfigureAwait(false);
                return Parameter(values, BrowseNames.ManagementAddressTxPort).DataValue.SourceTimestamp ==
                    (DateTimeUtc)configuredTimestamp &&
                    local.ToList().Any(value =>
                        value.BrowseName == new QualifiedName(BrowseNames.SystemCapabilitiesSupported, 0) &&
                        value.DataValue.SourceTimestamp == (DateTimeUtc)configuredTimestamp) &&
                    local.ToList().Any(value =>
                        value.BrowseName == new QualifiedName(BrowseNames.SystemCapabilitiesEnabled, 0) &&
                        value.DataValue.SourceTimestamp == (DateTimeUtc)configuredTimestamp);
            }, cancellationToken).ConfigureAwait(false);

            ArrayOf<NetworkParameterValue> localValues = await m_client.ReadParametersAsync(
                ObjectIds.LLDP_LocalSystemData, cancellationToken).ConfigureAwait(false);
            foreach (string name in new[]
            {
                BrowseNames.SystemCapabilitiesSupported,
                BrowseNames.SystemCapabilitiesEnabled
            })
            {
                NetworkParameterValue value = Parameter(localValues, name);
                Assert.That(value.DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.DataValue.WrappedValue.TryGetValue(out uint capabilities), Is.True);
                Assert.That(capabilities, Is.EqualTo(0x4u));
            }

            ArrayOf<ReferenceDescription> lldpObjects = await m_client.BrowseObjectsAsync(
                ObjectIds.LLDP, cancellationToken).ConfigureAwait(false);
            NodeId statisticsNode = ExpandedNodeId.ToNodeId(
                lldpObjects.ToList().Single(value =>
                    value.BrowseName == new QualifiedName(BrowseNames.RemoteStatistics, 0)).NodeId,
                m_session.NamespaceUris);
            ArrayOf<NetworkParameterValue> statistics = await m_client.ReadParametersAsync(
                statisticsNode, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(statistics, BrowseNames.LastChangeTime).DataValue.WrappedValue,
                Is.EqualTo(new Variant(1234u)));
            Assert.That(Parameter(statistics, BrowseNames.RemoteInserts).DataValue.WrappedValue,
                Is.EqualTo(new Variant(5u)));
            Assert.That(Parameter(statistics, BrowseNames.RemoteDeletes).DataValue.WrappedValue,
                Is.EqualTo(new Variant(6u)));
            Assert.That(Parameter(statistics, BrowseNames.RemoteDrops).DataValue.WrappedValue,
                Is.EqualTo(new Variant(7u)));
            Assert.That(Parameter(statistics, BrowseNames.RemoteAgeouts).DataValue.WrappedValue,
                Is.EqualTo(new Variant(8u)));

            ArrayOf<NetworkParameterValue> portValues = await m_client.ReadParametersAsync(
                lldpPort, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(portValues, BrowseNames.PortDescription).DataValue.WrappedValue,
                Is.EqualTo(new Variant("local port")));
            NetworkParameterValue txAddresses = Parameter(portValues, BrowseNames.ManagementAddressTxPort);
            Assert.That(txAddresses.DataValue.WrappedValue.TryGetStructure(
                out ArrayOf<LldpManagementAddressTxPortType> txAddressValues), Is.True);
            Assert.That(txAddressValues[0].ManAddress, Is.EqualTo("192.0.2.1"));
            Assert.That(txAddressValues[0].TxEnable, Is.True);

            NodeId neighbor = (await m_client.DiscoverNeighborsAsync(lldpPort, cancellationToken)
                .ConfigureAwait(false))[0];
            ArrayOf<NetworkParameterValue> remoteValues = await m_client.ReadParametersAsync(
                neighbor, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(remoteValues, BrowseNames.PortDescription).DataValue.WrappedValue,
                Is.EqualTo(new Variant("remote port")));
            Assert.That(Parameter(remoteValues, BrowseNames.SystemCapabilitiesSupported).DataValue.WrappedValue,
                Is.EqualTo(new Variant(0x4u)));
            Assert.That(Parameter(remoteValues, BrowseNames.SystemCapabilitiesEnabled).DataValue.WrappedValue,
                Is.EqualTo(new Variant(0x4u)));
            Assert.That(Parameter(remoteValues, BrowseNames.RemoteChanges).DataValue.WrappedValue,
                Is.EqualTo(new Variant(true)));
            Assert.That(Parameter(remoteValues, BrowseNames.RemoteTooManyNeighbors).DataValue.WrappedValue,
                Is.EqualTo(new Variant(false)));
            NetworkParameterValue managementAddresses = Parameter(remoteValues, BrowseNames.ManagementAddress);
            Assert.That(managementAddresses.DataValue.WrappedValue.TryGetStructure(
                out ArrayOf<LldpManagementAddressType> managementAddressValues), Is.True);
            Assert.That(managementAddressValues[0].Address, Is.EqualTo("192.0.2.2"));
            NetworkParameterValue unknownTlvs = Parameter(remoteValues, BrowseNames.RemoteUnknownTlv);
            Assert.That(unknownTlvs.DataValue.WrappedValue.TryGetStructure(
                out ArrayOf<LldpTlvType> unknownTlvValues), Is.True);
            Assert.That(unknownTlvValues[0].TlvType, Is.EqualTo(127u));
            Assert.That(unknownTlvValues[0].TlvInfo.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));

            NetworkLldpNeighborSnapshot clearedNeighbor = port.Neighbors[0] with
            {
                PortDescription = null,
                SystemName = null,
                SystemDescription = null,
                SystemCapabilitiesSupported = null,
                SystemCapabilitiesEnabled = null,
                RemoteChanges = null,
                RemoteTooManyNeighbors = null,
                ManagementAddress = [],
                RemoteUnknownTlv = []
            };
            NetworkLldpSnapshot clearedLldp = lldp with
            {
                SystemCapabilitiesSupported = null,
                SystemCapabilitiesEnabled = null,
                RemoteStatistics = null,
                Ports =
                [
                    port with
                    {
                        Description = null,
                        ManagementAddressTxPort = [],
                        Neighbors = [clearedNeighbor]
                    }
                ]
            };
            DateTime clearedTimestamp = configuredTimestamp.AddSeconds(1);
            NetworkModelSnapshot configuredSnapshot =
                await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            m_provider.Publish(configuredSnapshot with { Timestamp = clearedTimestamp, Lldp = clearedLldp });
            await WaitForAsync(async () =>
            {
                ArrayOf<ReferenceDescription> children = await m_client.BrowseObjectsAsync(
                    ObjectIds.LLDP, cancellationToken).ConfigureAwait(false);
                ArrayOf<NetworkParameterValue> local = await m_client.ReadParametersAsync(
                    ObjectIds.LLDP_LocalSystemData, cancellationToken).ConfigureAwait(false);
                return children.ToList().All(value =>
                    value.BrowseName != new QualifiedName(BrowseNames.RemoteStatistics, 0)) &&
                    Parameter(local, BrowseNames.SystemName).DataValue.SourceTimestamp ==
                    (DateTimeUtc)clearedTimestamp &&
                    local.ToList().All(value =>
                        value.BrowseName != new QualifiedName(BrowseNames.SystemCapabilitiesSupported, 0) &&
                        value.BrowseName != new QualifiedName(BrowseNames.SystemCapabilitiesEnabled, 0));
            }, cancellationToken).ConfigureAwait(false);
            ArrayOf<NetworkParameterValue> clearedLocal = await m_client.ReadParametersAsync(
                ObjectIds.LLDP_LocalSystemData, cancellationToken).ConfigureAwait(false);
            Assert.That(clearedLocal.ToList().Any(value =>
                value.BrowseName == new QualifiedName(BrowseNames.SystemCapabilitiesSupported, 0)), Is.False);
            Assert.That(clearedLocal.ToList().Any(value =>
                value.BrowseName == new QualifiedName(BrowseNames.SystemCapabilitiesEnabled, 0)), Is.False);
            ArrayOf<NetworkParameterValue> clearedPortValues = await m_client.ReadParametersAsync(
                lldpPort, cancellationToken).ConfigureAwait(false);
            Assert.That(clearedPortValues.ToList().Any(value =>
                value.BrowseName == new QualifiedName(BrowseNames.ManagementAddressTxPort, 0)), Is.False);
            Assert.That(clearedPortValues.ToList().Any(value =>
                value.BrowseName == new QualifiedName(BrowseNames.PortDescription, 0)), Is.False);
            ArrayOf<NetworkParameterValue> clearedRemote = await m_client.ReadParametersAsync(
                neighbor, cancellationToken).ConfigureAwait(false);
            Assert.That(clearedRemote.ToList().Any(value =>
                value.BrowseName == new QualifiedName(BrowseNames.PortDescription, 0)), Is.False);
            Assert.That(clearedRemote.ToList().Any(value =>
                value.BrowseName == new QualifiedName(BrowseNames.SystemCapabilitiesSupported, 0)), Is.False);
            Assert.That(clearedRemote.ToList().Any(value =>
                value.BrowseName == new QualifiedName(BrowseNames.SystemCapabilitiesEnabled, 0)), Is.False);
            Assert.That(clearedRemote.ToList().Any(value =>
                value.BrowseName == new QualifiedName(BrowseNames.RemoteChanges, 0)), Is.False);
            Assert.That(clearedRemote.ToList().Any(value =>
                value.BrowseName == new QualifiedName(BrowseNames.RemoteTooManyNeighbors, 0)), Is.False);
            Assert.That(clearedRemote.ToList().Any(value =>
                value.BrowseName == new QualifiedName(BrowseNames.ManagementAddress, 0)), Is.False);
            Assert.That(clearedRemote.ToList().Any(value =>
                value.BrowseName == new QualifiedName(BrowseNames.RemoteUnknownTlv, 0)), Is.False);
        }

        /// <summary>
        /// Keeps port and neighbor identity components distinct when they contain path separators.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task LldpNeighborIdentitiesDoNotCollideAcrossPortPathComponents(
            CancellationToken cancellationToken)
        {
            NetworkModelSnapshot snapshot = NetworkScenario.Advance(
                await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false), 1);
            NetworkLldpSnapshot lldp = snapshot.Lldp
                ?? throw new InvalidOperationException("The scenario requires an LLDP acquisition.");
            NetworkLldpPortSnapshot template = lldp.Ports[0];
            NetworkLldpNeighborSnapshot neighbor = template.Neighbors[0];
            m_provider.Publish(snapshot with
            {
                Lldp = lldp with
                {
                    Ports =
                    [
                        template with { Id = "left/right", Neighbors = [neighbor with { Id = "remote" }] },
                        template with { Id = "left", Neighbors = [neighbor with { Id = "right/remote" }] }
                    ]
                }
            });
            await WaitForAsync(async () =>
                (await m_client.DiscoverLldpPortsAsync(cancellationToken).ConfigureAwait(false)).Count == 2,
                cancellationToken).ConfigureAwait(false);
            ArrayOf<ReferenceDescription> ports = await m_client.BrowseObjectsAsync(
                ObjectIds.LLDP_Ports, cancellationToken).ConfigureAwait(false);
            var neighbors = new List<NodeId>();
            foreach (ReferenceDescription port in ports.ToList())
            {
                NodeId portId = ExpandedNodeId.ToNodeId(port.NodeId, m_session.NamespaceUris);
                await WaitForAsync(async () =>
                    (await m_client.DiscoverNeighborsAsync(portId, cancellationToken).ConfigureAwait(false))
                        .Count == 1, cancellationToken).ConfigureAwait(false);
                ArrayOf<NodeId> current = await m_client.DiscoverNeighborsAsync(portId, cancellationToken)
                    .ConfigureAwait(false);
                neighbors.Add(current[0]);
                ArrayOf<NetworkParameterValue> values = await m_client.ReadParametersAsync(
                    current[0], cancellationToken).ConfigureAwait(false);
                Assert.That(Parameter(values, BrowseNames.PortId).DataValue.WrappedValue,
                    Is.EqualTo(new Variant("port1")));
                Assert.That(Parameter(values, BrowseNames.PortId).DataValue.StatusCode,
                    Is.EqualTo(StatusCodes.Good));
            }
            Assert.That(neighbors, Has.Count.EqualTo(2));
            Assert.That(neighbors.Distinct().Count(), Is.EqualTo(2));
        }

        /// <summary>
        /// Preserves a separately registered neighbor when its port gains an optional child.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task ReplacingPortTopologyRetainsItsExistingNeighbor(CancellationToken cancellationToken)
        {
            NetworkModelSnapshot snapshot = NetworkScenario.Advance(
                await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false), 1);
            m_provider.Publish(snapshot);
            NodeId port = (await m_client.DiscoverLldpPortsAsync(cancellationToken).ConfigureAwait(false))[0];
            await WaitForAsync(async () =>
                (await m_client.DiscoverNeighborsAsync(port, cancellationToken).ConfigureAwait(false)).Count == 1,
                cancellationToken).ConfigureAwait(false);
            NodeId neighbor = (await m_client.DiscoverNeighborsAsync(port, cancellationToken)
                .ConfigureAwait(false))[0];
            NetworkLldpSnapshot lldp = snapshot.Lldp
                ?? throw new InvalidOperationException("The scenario requires an LLDP acquisition.");
            m_provider.Publish(snapshot with
            {
                Revision = snapshot.Revision + 1,
                Timestamp = snapshot.Timestamp.AddSeconds(1),
                Lldp = lldp with
                {
                    Ports = [lldp.Ports[0] with { Description = "Updated simulated port" }]
                }
            });
            await WaitForAsync(async () =>
                (await m_client.ReadParametersAsync(port, cancellationToken).ConfigureAwait(false)).ToList()
                    .Any(value => value.BrowseName == new QualifiedName(BrowseNames.PortDescription, 0)),
                cancellationToken).ConfigureAwait(false);
            await WaitForAsync(async () =>
                (await m_client.DiscoverNeighborsAsync(port, cancellationToken).ConfigureAwait(false)).Count == 1,
                cancellationToken).ConfigureAwait(false);
            ArrayOf<NodeId> neighbors = await m_client.DiscoverNeighborsAsync(port, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(neighbors.ToArray(), Is.EqualTo(new[] { neighbor }));
            ArrayOf<NetworkParameterValue> values = await m_client.ReadParametersAsync(neighbor, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(Parameter(values, BrowseNames.ChassisId).DataValue.WrappedValue,
                Is.EqualTo(new Variant("02:00:00:00:00:02")));
            Assert.That(Parameter(values, BrowseNames.ChassisId).DataValue.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Verifies that absent capabilities and edit methods do not become fabricated values.
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task OmittedCapabilitiesRemoveOptionalNodesAndMappingMethods(
            CancellationToken cancellationToken)
        {
            NetworkModelSnapshot snapshot = await m_provider.ReadAsync(cancellationToken).ConfigureAwait(false);
            ArrayOf<ReferenceDescription> interfaces = await m_client.BrowseObjectsAsync(
                ObjectIds.NetworkInterfaces, cancellationToken).ConfigureAwait(false);
            NodeId physical = ExpandedNodeId.ToNodeId(interfaces.ToList()
                .Single(value => value.BrowseName.Name == "eth0").NodeId, m_session.NamespaceUris);
            DateTime timestamp = snapshot.Timestamp.AddSeconds(1);
            m_provider.Publish(snapshot with
            {
                Timestamp = timestamp,
                Interfaces =
                [
                    new NetworkInterfaceSnapshot("physical", "eth0")
                    {
                        AdminStatus = InterfaceAdminStatus.Up,
                        OperStatus = InterfaceOperStatus.Up,
                        Speed = 1_000_000_000
                    }
                ],
                MappingTables = [snapshot.MappingTables[0] with { CanEdit = false }],
                Lldp = null
            });
            await WaitForAsync(async () =>
                (await m_client.ReadInterfaceAsync(physical, cancellationToken).ConfigureAwait(false))
                    .Speed.DataValue.SourceTimestamp == (DateTimeUtc)timestamp, cancellationToken)
                .ConfigureAwait(false);
            NetworkInterfaceValues values = await m_client.ReadInterfaceAsync(physical, cancellationToken)
                .ConfigureAwait(false);
            Assert.That(values.PhysicalAddress.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That((await m_client.BrowseObjectsAsync(physical, cancellationToken).ConfigureAwait(false)).Count,
                Is.Zero);
            Assert.That((await m_client.DiscoverLldpPortsAsync(cancellationToken).ConfigureAwait(false)).Count,
                Is.Zero);
            NodeId table = (await m_client.DiscoverMappingTablesAsync(cancellationToken).ConfigureAwait(false))[0];
            BrowseResponse methods = await m_session.BrowseAsync(null, null, 0,
                [
                    new BrowseDescription
                    {
                        NodeId = table,
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        BrowseDirection = BrowseDirection.Forward,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Method,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], cancellationToken).ConfigureAwait(false);
            Assert.That(methods.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(methods.Results[0].References.Count, Is.Zero);
            ArrayOf<NetworkParameterValue> local = await m_client.ReadParametersAsync(
                ObjectIds.LLDP_LocalSystemData, cancellationToken).ConfigureAwait(false);
            Assert.That(Parameter(local, BrowseNames.ChassisId).DataValue.StatusCode,
                Is.EqualTo(StatusCodes.BadNotSupported));
        }

        /// <summary>
        /// Creates defined stream-status values and an LLDP neighbor for enum publication tests.
        /// </summary>
        private static NetworkModelSnapshot WithEnumObservations(NetworkModelSnapshot snapshot, string valuePath)
        {
            if (valuePath is "failure code" or "talker status" or "listener status")
            {
                NetworkStreamSnapshot[] streams = snapshot.Streams.Span.ToArray();
                for (int index = 0; index < streams.Length; index++)
                {
                    streams[index] = streams[index] with
                    {
                        Status = new NetworkStreamStatusSnapshot(TsnFailureCode.NoFailure)
                        {
                            TalkerStatus = streams[index].IsTalker ? TsnTalkerStatus.Ready : null,
                            ListenerStatus = streams[index].IsTalker ? null : TsnListenerStatus.Ready
                        }
                    };
                }
                return snapshot with { Streams = ArrayOf.Wrapped(streams) };
            }

            if (valuePath is "neighbor chassis subtype" or "neighbor port subtype")
            {
                NetworkLldpSnapshot lldp = snapshot.Lldp
                    ?? throw new InvalidOperationException("The enum test scenario requires LLDP.");
                NetworkLldpPortSnapshot[] ports = lldp.Ports.Span.ToArray();
                ports[0] = ports[0] with
                {
                    Neighbors =
                    [
                        new NetworkLldpNeighborSnapshot(
                            "neighbor", 1, 1, ChassisIdSubtype.MacAddress, "02:00:00:00:00:02",
                            PortIdSubtype.InterfaceName, "port1")
                    ]
                };
                return snapshot with { Lldp = lldp with { Ports = ArrayOf.Wrapped(ports) } };
            }
            return snapshot;
        }

        /// <summary>
        /// Replaces one provider enum with a value outside its generated enumeration.
        /// </summary>
        private static NetworkModelSnapshot WithInvalidEnumValue(NetworkModelSnapshot snapshot, string valuePath)
        {
            if (valuePath is "interface admin" or "interface oper" or "duplex" or "negotiation")
            {
                NetworkInterfaceSnapshot[] interfaces = snapshot.Interfaces.Span.ToArray();
                for (int index = 0; index < interfaces.Length; index++)
                {
                    if (interfaces[index].Id != "physical")
                    {
                        continue;
                    }
                    interfaces[index] = valuePath switch
                    {
                        "interface admin" => interfaces[index] with
                        {
                            AdminStatus = (InterfaceAdminStatus)int.MaxValue
                        },
                        "interface oper" => interfaces[index] with
                        {
                            OperStatus = (InterfaceOperStatus)int.MaxValue
                        },
                        "duplex" => interfaces[index] with
                        {
                            Ethernet = interfaces[index].Ethernet! with { Duplex = (Duplex)int.MaxValue }
                        },
                        _ => interfaces[index] with
                        {
                            Ethernet = interfaces[index].Ethernet! with
                            {
                                NegotiationStatus = (NegotiationStatus)int.MaxValue
                            }
                        }
                    };
                }
                return snapshot with { Interfaces = ArrayOf.Wrapped(interfaces) };
            }

            if (valuePath is "stream state" or "failure code" or "talker status" or "listener status")
            {
                NetworkStreamSnapshot[] streams = snapshot.Streams.Span.ToArray();
                for (int index = 0; index < streams.Length; index++)
                {
                    bool target = valuePath == "listener status"
                        ? !streams[index].IsTalker
                        : streams[index].IsTalker;
                    if (!target)
                    {
                        continue;
                    }
                    if (valuePath == "stream state")
                    {
                        streams[index] = streams[index] with { State = (TsnStreamState)int.MaxValue };
                        continue;
                    }
                    NetworkStreamStatusSnapshot status = streams[index].Status
                        ?? throw new InvalidOperationException("The enum test stream status is absent.");
                    streams[index] = streams[index] with
                    {
                        Status = valuePath switch
                        {
                            "failure code" => status with { FailureCode = (TsnFailureCode)int.MaxValue },
                            "talker status" => status with { TalkerStatus = (TsnTalkerStatus)int.MaxValue },
                            _ => status with { ListenerStatus = (TsnListenerStatus)int.MaxValue }
                        }
                    };
                }
                return snapshot with { Streams = ArrayOf.Wrapped(streams) };
            }

            NetworkLldpSnapshot lldp = snapshot.Lldp
                ?? throw new InvalidOperationException("The enum test scenario requires LLDP.");
            if (valuePath == "local chassis subtype")
            {
                return snapshot with { Lldp = lldp with { ChassisIdSubtype = (ChassisIdSubtype)int.MaxValue } };
            }
            NetworkLldpPortSnapshot[] ports = lldp.Ports.Span.ToArray();
            if (valuePath == "port subtype")
            {
                ports[0] = ports[0] with { PortIdSubtype = (PortIdSubtype)int.MaxValue };
            }
            else
            {
                NetworkLldpNeighborSnapshot[] neighbors = ports[0].Neighbors.Span.ToArray();
                neighbors[0] = valuePath == "neighbor chassis subtype"
                    ? neighbors[0] with { ChassisIdSubtype = (ChassisIdSubtype)int.MaxValue }
                    : neighbors[0] with { PortIdSubtype = (PortIdSubtype)int.MaxValue };
                ports[0] = ports[0] with { Neighbors = ArrayOf.Wrapped(neighbors) };
            }
            return snapshot with { Lldp = lldp with { Ports = ArrayOf.Wrapped(ports) } };
        }

        /// <summary>
        /// Reads one enum-backed server value through its corresponding OPC UA interface.
        /// </summary>
        private async Task<NetworkParameterValue> ReadEnumParameterAsync(
            string valuePath, CancellationToken cancellationToken)
        {
            ArrayOf<ReferenceDescription> interfaces = await m_client.BrowseObjectsAsync(
                ObjectIds.NetworkInterfaces, cancellationToken).ConfigureAwait(false);
            NodeId physical = ExpandedNodeId.ToNodeId(interfaces.ToList()
                .Single(reference => reference.BrowseName.Name == "eth0").NodeId, m_session.NamespaceUris);
            switch (valuePath)
            {
                case "interface admin":
                    return (await m_client.ReadInterfaceAsync(physical, cancellationToken).ConfigureAwait(false))
                        .AdminStatus;
                case "interface oper":
                    return (await m_client.ReadInterfaceAsync(physical, cancellationToken).ConfigureAwait(false))
                        .OperStatus;
                case "duplex":
                    return Parameter(await ReadParameterSetAsync(
                        physical, ObjectTypeIds.IIeeeBaseEthernetPortType, cancellationToken).ConfigureAwait(false),
                        BrowseNames.Duplex);
                case "negotiation":
                    return Parameter(await ReadParameterSetAsync(
                        physical, ObjectTypeIds.IIeeeAutoNegotiationStatusType, cancellationToken)
                        .ConfigureAwait(false), BrowseNames.NegotiationStatus);
                case "stream state":
                    return (await m_client.ReadStreamAsync(
                        (await m_client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false))[0],
                        cancellationToken).ConfigureAwait(false)).State;
                case "failure code":
                case "talker status":
                    return Parameter(await ReadParameterSetAsync(
                        (await m_client.DiscoverStreamsAsync(true, cancellationToken).ConfigureAwait(false))[0],
                        ObjectTypeIds.IIeeeBaseTsnStatusStreamType, cancellationToken).ConfigureAwait(false),
                        valuePath == "failure code" ? BrowseNames.FailureCode : BrowseNames.TalkerStatus);
                case "listener status":
                    return Parameter(await ReadParameterSetAsync(
                        (await m_client.DiscoverStreamsAsync(false, cancellationToken).ConfigureAwait(false))[0],
                        ObjectTypeIds.IIeeeBaseTsnStatusStreamType, cancellationToken).ConfigureAwait(false),
                        BrowseNames.ListenerStatus);
                case "local chassis subtype":
                    return Parameter(await m_client.ReadParametersAsync(
                        ObjectIds.LLDP_LocalSystemData, cancellationToken).ConfigureAwait(false),
                        BrowseNames.ChassisIdSubtype);
                case "port subtype":
                case "neighbor chassis subtype":
                case "neighbor port subtype":
                    NodeId port = (await m_client.DiscoverLldpPortsAsync(cancellationToken).ConfigureAwait(false))[0];
                    if (valuePath == "port subtype")
                    {
                        return Parameter(await m_client.ReadParametersAsync(port, cancellationToken)
                            .ConfigureAwait(false), BrowseNames.PortIdSubtype);
                    }
                    NodeId neighbor = (await m_client.DiscoverNeighborsAsync(port, cancellationToken)
                        .ConfigureAwait(false))[0];
                    ArrayOf<NetworkParameterValue> neighborParameters = await m_client.ReadParametersAsync(
                        neighbor, cancellationToken).ConfigureAwait(false);
                    return Parameter(neighborParameters, valuePath == "neighbor chassis subtype"
                        ? BrowseNames.ChassisIdSubtype
                        : BrowseNames.PortIdSubtype);
                default:
                    throw new ArgumentOutOfRangeException(nameof(valuePath));
            }
        }

        /// <summary>
        /// Checks whether an optional enum observation has reached the address space.
        /// </summary>
        private async Task<bool> HasEnumObservationAsync(string valuePath, CancellationToken cancellationToken)
        {
            if (valuePath is "failure code" or "talker status" or "listener status")
            {
                ArrayOf<NodeId> streams = await m_client.DiscoverStreamsAsync(
                    valuePath != "listener status", cancellationToken).ConfigureAwait(false);
                if (streams.Count == 0)
                {
                    return false;
                }
                return await HasInterfaceAsync(
                    streams[0], ObjectTypeIds.IIeeeBaseTsnStatusStreamType, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (valuePath is "neighbor chassis subtype" or "neighbor port subtype")
            {
                ArrayOf<NodeId> ports = await m_client.DiscoverLldpPortsAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (ports.Count == 0)
                {
                    return false;
                }
                return (await m_client.DiscoverNeighborsAsync(ports[0], cancellationToken)
                    .ConfigureAwait(false)).Count > 0;
            }

            return true;
        }

        /// <summary>
        /// Returns a snapshot with the selected table's existing entry replaced by a supplied entry.
        /// </summary>
        private static NetworkModelSnapshot WithMappingEntry(
            NetworkModelSnapshot snapshot, PriorityMappingEntryType replacement)
        {
            NetworkMappingTableSnapshot[] tables = snapshot.MappingTables.Span.ToArray();
            PriorityMappingEntryType[] entries = tables[0].Entries.Span.ToArray();
            entries[0] = replacement;
            tables[0] = tables[0] with { Entries = ArrayOf.Wrapped(entries) };
            return snapshot with { MappingTables = ArrayOf.Wrapped(tables) };
        }

        /// <summary>
        /// Replaces an entry with one malformed key/value or adds a duplicate composite key.
        /// </summary>
        private static NetworkModelSnapshot WithInvalidMappingEntry(NetworkModelSnapshot snapshot, string invalidField)
        {
            NetworkMappingTableSnapshot[] tables = snapshot.MappingTables.Span.ToArray();
            PriorityMappingEntryType[] entries = tables[0].Entries.Span.ToArray();
            PriorityMappingEntryType source = entries[0];
            var invalid = new PriorityMappingEntryType
            {
                MappingUri = source.MappingUri,
                PriorityLabel = source.PriorityLabel,
                PriorityValue_PCP = source.PriorityValue_PCP,
                PriorityValue_DSCP = source.PriorityValue_DSCP
            };
            switch (invalidField)
            {
                case "empty URI":
                    invalid.MappingUri = " ";
                    entries[0] = invalid;
                    break;
                case "empty label":
                    invalid.PriorityLabel = string.Empty;
                    entries[0] = invalid;
                    break;
                case "PCP range":
                    invalid.PriorityValue_PCP = 8;
                    entries[0] = invalid;
                    break;
                case "DSCP range":
                    invalid.PriorityValue_DSCP = 64;
                    entries[0] = invalid;
                    break;
                case "duplicate key":
                    entries = [.. entries, invalid];
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(invalidField));
            }
            tables[0] = tables[0] with { Entries = ArrayOf.Wrapped(entries) };
            return snapshot with { MappingTables = ArrayOf.Wrapped(tables) };
        }

        /// <summary>
        /// Selects one standard parameter without matching an application-namespace homonym.
        /// </summary>
        private static NetworkParameterValue Parameter(ArrayOf<NetworkParameterValue> parameters, string name)
        {
            return parameters.ToList().Single(value => value.BrowseName == new QualifiedName(name, 0));
        }

        /// <summary>
        /// Finds an interface instance by its published ifName.
        /// </summary>
        private async Task<NodeId> FindInterfaceAsync(string ifName, CancellationToken cancellationToken)
        {
            ArrayOf<ReferenceDescription> interfaces = await m_client.BrowseObjectsAsync(
                ObjectIds.NetworkInterfaces, cancellationToken).ConfigureAwait(false);
            return ExpandedNodeId.ToNodeId(interfaces.ToList()
                .Single(reference => reference.BrowseName.Name == ifName).NodeId, m_session.NamespaceUris);
        }

        /// <summary>
        /// Finds the actual object implementing a parameter-set interface before reading its values.
        /// </summary>
        private async Task<ArrayOf<NetworkParameterValue>> ReadParameterSetAsync(
            NodeId parent, NodeId interfaceType, CancellationToken cancellationToken)
        {
            var pending = new Queue<NodeId>();
            var visited = new HashSet<NodeId>();
            pending.Enqueue(parent);
            while (pending.Count != 0)
            {
                NodeId current = pending.Dequeue();
                if (!visited.Add(current))
                {
                    continue;
                }
                BrowseResponse response = await m_session.BrowseAsync(null, null, 0,
                    [
                        new BrowseDescription
                        {
                            NodeId = current,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = ReferenceTypeIds.HasInterface,
                            IncludeSubtypes = true,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], cancellationToken).ConfigureAwait(false);
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[0].ContinuationPoint.IsEmpty, Is.True);
                if (response.Results[0].References.ToList()
                    .Any(reference => reference.NodeId == new ExpandedNodeId(interfaceType)))
                {
                    return await m_client.ReadParametersAsync(current, cancellationToken).ConfigureAwait(false);
                }
                ArrayOf<ReferenceDescription> children = await m_client.BrowseObjectsAsync(current, cancellationToken)
                    .ConfigureAwait(false);
                foreach (ReferenceDescription child in children.ToList())
                {
                    pending.Enqueue(ExpandedNodeId.ToNodeId(child.NodeId, m_session.NamespaceUris));
                }
            }
            throw new InvalidOperationException("The expected Part 22 interface is not published: " + interfaceType);
        }

        /// <summary>
        /// Checks whether an object directly implements a Part 22 interface.
        /// </summary>
        private async Task<bool> HasInterfaceAsync(
            NodeId node, NodeId interfaceType, CancellationToken cancellationToken)
        {
            BrowseResponse response = await m_session.BrowseAsync(null, null, 0,
                [
                    new BrowseDescription
                    {
                        NodeId = node,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasInterface,
                        IncludeSubtypes = true,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ], cancellationToken).ConfigureAwait(false);
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].ContinuationPoint.IsEmpty, Is.True);
            return response.Results[0].References.ToList()
                .Any(reference => reference.NodeId == new ExpandedNodeId(interfaceType));
        }

        /// <summary>
        /// Checks whether an object subtree contains an instance of a Part 22 interface.
        /// </summary>
        private async Task<bool> HasInterfaceInSubtreeAsync(
            NodeId node, NodeId interfaceType, CancellationToken cancellationToken)
        {
            var pending = new Queue<NodeId>();
            var visited = new HashSet<NodeId>();
            pending.Enqueue(node);
            while (pending.Count != 0)
            {
                NodeId current = pending.Dequeue();
                if (!visited.Add(current))
                {
                    continue;
                }
                BrowseResponse response = await m_session.BrowseAsync(null, null, 0,
                    [
                        new BrowseDescription
                        {
                            NodeId = current,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = ReferenceTypeIds.HasInterface,
                            IncludeSubtypes = true,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ], cancellationToken).ConfigureAwait(false);
                Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(response.Results[0].ContinuationPoint.IsEmpty, Is.True);
                if (response.Results[0].References.ToList()
                    .Any(reference => reference.NodeId == new ExpandedNodeId(interfaceType)))
                {
                    return true;
                }
                ArrayOf<ReferenceDescription> children = await m_client.BrowseObjectsAsync(current, cancellationToken)
                    .ConfigureAwait(false);
                foreach (ReferenceDescription child in children.ToList())
                {
                    pending.Enqueue(ExpandedNodeId.ToNodeId(child.NodeId, m_session.NamespaceUris));
                }
            }
            return false;
        }

        /// <summary>
        /// Waits for the monitored variable's actual data-change notification.
        /// </summary>
        private async Task<DataValue> PublishAsync(uint subscriptionId, CancellationToken cancellationToken)
        {
            while (true)
            {
                PublishResponse response = await m_session.PublishAsync(null!, default, cancellationToken)
                    .ConfigureAwait(false);
                Assert.That(response.SubscriptionId, Is.EqualTo(subscriptionId));
                foreach (ExtensionObject extension in response.NotificationMessage.NotificationData)
                {
                    if (extension.TryGetValue(out DataChangeNotification? notification))
                    {
                        Assert.That(notification!.MonitoredItems.Count, Is.EqualTo(1));
                        return notification.MonitoredItems[0].Value;
                    }
                }
            }
        }

        /// <summary>
        /// Bounds asynchronous provider-to-address-space reconciliation waits.
        /// </summary>
        private static async Task WaitForAsync(Func<Task<bool>> condition, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (!await condition().ConfigureAwait(false))
            {
                await Task.Delay(20, timeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>Expected matrix dimensions for two eight-byte failure-system identifiers.</summary>
        private static readonly int[] s_failureSystemIdentifierDimensions = [2, 8];

        /// <summary>Certificate stores owned by the current test.</summary>
        private string m_pkiRoot = string.Empty;

        /// <summary>Deterministic source of complete acquisitions.</summary>
        private ReferenceNetworkModelProvider m_provider = null!;

        /// <summary>Server that owns the network manager.</summary>
        private ServerFixture<StandardServer> m_server = null!;

        /// <summary>Owner of the connected client's configuration.</summary>
        private ClientFixture m_clientFixture = null!;

        /// <summary>Real session owned by the test.</summary>
        private Client.ISession m_session = null!;

        /// <summary>Borrowing network client bound to the session.</summary>
        private NetworkModelClient m_client = null!;

        /// <summary>Thread-visible authorization switch.</summary>
        private int m_permitMappingEdits;

        /// <summary>Controllable failures at the provider observation boundary.</summary>
        private FaultInjectingProvider m_providerBoundary = null!;

        /// <summary>
        /// Observes address-space preparation independently of provider observation.
        /// </summary>
        private sealed class StartupProbeFactory(
            Action<IDictionary<NodeId, IList<IReference>>> inspect) : IAsyncNodeManagerFactory
        {
            /// <inheritdoc/>
            public ArrayOf<string> NamespacesUris => ["urn:network:startup-probe"];

            /// <inheritdoc/>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IAsyncNodeManager>(new StartupProbeNodeManager(server, configuration, inspect));
            }
        }

        /// <summary>
        /// Inspects the candidate's reference contributions before startup publishes them.
        /// </summary>
        private sealed class StartupProbeNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            Action<IDictionary<NodeId, IList<IReference>>> inspect)
            : AsyncCustomNodeManager(server, configuration, "urn:network:startup-probe")
        {
            /// <inheritdoc/>
            public override async ValueTask CreateAddressSpaceAsync(
                IDictionary<NodeId, IList<IReference>> externalReferences,
                CancellationToken cancellationToken = default)
            {
                await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
                inspect(externalReferences);
            }
        }

        /// <summary>
        /// Injects acquisition faults without changing production provider behavior.
        /// </summary>
        private sealed class FaultInjectingProvider(INetworkModelProvider inner) : INetworkModelProvider
        {
            /// <summary>
            /// Counts observation starts, including attempts made before runtime readiness.
            /// </summary>
            public int WatchCount => Volatile.Read(ref m_watchCount);

            /// <summary>
            /// Ends the sequence normally rather than throwing when observation is interrupted.
            /// </summary>
            public bool CompleteWatch
            {
                get => Volatile.Read(ref m_completeWatch);
                set => Volatile.Write(ref m_completeWatch, value);
            }

            /// <summary>
            /// Selects an unexpected exception instead of a provider-supplied bad service status.
            /// </summary>
            public bool UnexpectedFailure
            {
                get => Volatile.Read(ref m_unexpectedFailure);
                set => Volatile.Write(ref m_unexpectedFailure, value);
            }

            /// <summary>
            /// Makes the next observed acquisition terminate observation with an error.
            /// </summary>
            public bool FailWatch
            {
                get => Volatile.Read(ref m_failWatch);
                set => Volatile.Write(ref m_failWatch, value);
            }

            /// <summary>
            /// Rewrites observed revisions or timestamps to exercise the server's acceptance guards.
            /// </summary>
            public Func<NetworkModelSnapshot, NetworkModelSnapshot>? WatchTransform
            {
                get => Volatile.Read(ref m_watchTransform);
                set => Volatile.Write(ref m_watchTransform, value);
            }

            /// <summary>
            /// Rewrites the initial acquisition returned before watch observation begins.
            /// </summary>
            public Func<NetworkModelSnapshot, NetworkModelSnapshot>? ReadTransform
            {
                get => Volatile.Read(ref m_readTransform);
                set => Volatile.Write(ref m_readTransform, value);
            }

            /// <summary>
            /// Holds watch observation until the test releases the initial address-space inspection.
            /// </summary>
            public TaskCompletionSource<bool>? WatchRelease
            {
                get => Volatile.Read(ref m_watchRelease);
                set => Volatile.Write(ref m_watchRelease, value);
            }

            /// <summary>
            /// Signals that the server consumed or rejected the yielded acquisition.
            /// </summary>
            public Action<NetworkModelSnapshot>? AfterDelivery
            {
                get => Volatile.Read(ref m_afterDelivery);
                set => Volatile.Write(ref m_afterDelivery, value);
            }

            /// <inheritdoc/>
            public async ValueTask<NetworkModelSnapshot> ReadAsync(
                CancellationToken cancellationToken = default)
            {
                NetworkModelSnapshot snapshot = await inner.ReadAsync(cancellationToken).ConfigureAwait(false);
                return ReadTransform?.Invoke(snapshot) ?? snapshot;
            }

            /// <inheritdoc/>
            public async IAsyncEnumerable<NetworkModelSnapshot> WatchAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_watchCount);
                TaskCompletionSource<bool>? watchRelease = WatchRelease;
                if (watchRelease is not null)
                {
                    await watchRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                await foreach (NetworkModelSnapshot snapshot in inner.WatchAsync(cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (FailWatch)
                    {
                        if (CompleteWatch)
                        {
                            yield break;
                        }
                        if (UnexpectedFailure)
                        {
                            throw new InvalidOperationException("Injected provider acquisition failure.");
                        }
                        throw new ServiceResultException(StatusCodes.BadNoCommunication);
                    }
                    NetworkModelSnapshot transformed = WatchTransform?.Invoke(snapshot) ?? snapshot;
                    yield return transformed;
                    AfterDelivery?.Invoke(transformed);
                }
            }

            /// <inheritdoc/>
            public ValueTask<StatusCode> EditPriorityMappingAsync(
                string tableId, PriorityMappingEntryType entry, bool delete,
                CancellationToken cancellationToken = default)
            {
                return inner.EditPriorityMappingAsync(tableId, entry, delete, cancellationToken);
            }

            /// <summary>Acquisition rewrite selected by the test.</summary>
            private Func<NetworkModelSnapshot, NetworkModelSnapshot>? m_watchTransform;

            /// <summary>Initial acquisition rewrite selected by the test.</summary>
            private Func<NetworkModelSnapshot, NetworkModelSnapshot>? m_readTransform;

            /// <summary>Signal that permits provider watch observation.</summary>
            private TaskCompletionSource<bool>? m_watchRelease;

            /// <summary>Notification after the yielded acquisition has been consumed.</summary>
            private Action<NetworkModelSnapshot>? m_afterDelivery;

            /// <summary>Whether the next observation throws.</summary>
            private bool m_failWatch;

            /// <summary>Whether observation throws a non-service exception.</summary>
            private bool m_unexpectedFailure;

            /// <summary>Whether observation ends normally when interrupted.</summary>
            private bool m_completeWatch;

            /// <summary>Number of provider watch enumerations started.</summary>
            private int m_watchCount;
        }
    }
}
