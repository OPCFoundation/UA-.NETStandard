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
            m_pkiRoot = Path.Combine(Path.GetTempPath(), "OpcUaNetworkTests", Guid.NewGuid().ToString("N")[..8]);
            m_provider = NetworkScenario.Create();
            m_providerBoundary = new FaultInjectingProvider(m_provider);
            m_permitMappingEdits = 0;
            m_server = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
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

            m_provider.Publish(snapshot with { Timestamp = snapshot.Timestamp.AddSeconds(2) });
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
                        Assert.That(identifiers.Dimensions, Is.EqualTo(new[] { 1, 8 }));
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

            m_provider.Publish(snapshot with { Timestamp = timestamp.AddSeconds(1) });
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
                Timestamp = nextTimestamp.AddSeconds(1),
                Interfaces = ArrayOf.Wrapped(interfaces)
            });
            await WaitForAsync(async () =>
                (await m_client.ReadInterfaceAsync(id, cancellationToken).ConfigureAwait(false))
                    .Speed.DataValue.WrappedValue == new Variant(100_000_000UL), cancellationToken)
                .ConfigureAwait(false);
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

            m_provider.Publish(snapshot with { Timestamp = snapshot.Timestamp.AddSeconds(2) });
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
        /// Selects one standard parameter without matching an application-namespace homonym.
        /// </summary>
        private static NetworkParameterValue Parameter(ArrayOf<NetworkParameterValue> parameters, string name)
        {
            return parameters.ToList().Single(value => value.BrowseName == new QualifiedName(name, 0));
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
        /// Injects acquisition faults without changing production provider behavior.
        /// </summary>
        private sealed class FaultInjectingProvider(INetworkModelProvider inner) : INetworkModelProvider
        {
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
            /// Signals that the server consumed or rejected the yielded acquisition.
            /// </summary>
            public Action<NetworkModelSnapshot>? AfterDelivery
            {
                get => Volatile.Read(ref m_afterDelivery);
                set => Volatile.Write(ref m_afterDelivery, value);
            }

            /// <inheritdoc/>
            public ValueTask<NetworkModelSnapshot> ReadAsync(CancellationToken cancellationToken = default)
            {
                return inner.ReadAsync(cancellationToken);
            }

            /// <inheritdoc/>
            public async IAsyncEnumerable<NetworkModelSnapshot> WatchAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
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

            /// <summary>Notification after the yielded acquisition has been consumed.</summary>
            private Action<NetworkModelSnapshot>? m_afterDelivery;

            /// <summary>Whether the next observation throws.</summary>
            private bool m_failWatch;

            /// <summary>Whether observation throws a non-service exception.</summary>
            private bool m_unexpectedFailure;

            /// <summary>Whether observation ends normally when interrupted.</summary>
            private bool m_completeWatch;
        }
    }
}
