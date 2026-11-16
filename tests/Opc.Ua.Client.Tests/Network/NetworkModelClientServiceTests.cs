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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Network;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.Network
{
    /// <summary>
    /// Deterministic service-level tests for Part 22 discovery, topology, reads and calls.
    /// </summary>
    [TestFixture]
    [Category("NetworkModel")]
    [NonParallelizable]
    public sealed class NetworkModelClientServiceTests
    {
        /// <summary>
        /// Creates a strict service boundary with independent browse, read and call responses.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext context = ServiceMessageContext.Create(m_telemetry);
            context.NamespaceUris.GetIndexOrAppend("urn:unrelated");
            context.NamespaceUris.GetIndexOrAppend("urn:network:vendor");
            m_session = new Mock<ISessionClient>(MockBehavior.Strict);
            m_session.SetupGet(session => session.MessageContext).Returns(context);
            m_browse.Clear();
            m_removed.Clear();
            m_values.Clear();
            m_calls.Clear();
            m_callResult = new CallMethodResult { StatusCode = StatusCodes.Good };
            m_session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ViewDescription, uint, ArrayOf<BrowseDescription>, CancellationToken>(
                    (_, _, _, requests, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        BrowseDescription request = requests[0];
                        if (m_removed.Contains(request.NodeId))
                        {
                            return new ValueTask<BrowseResponse>(new BrowseResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = [new BrowseResult { StatusCode = StatusCodes.BadNodeIdUnknown }]
                            });
                        }
                        m_browse.TryGetValue(
                            (request.NodeId, request.ReferenceTypeId, request.BrowseDirection), out var references);
                        return new ValueTask<BrowseResponse>(new BrowseResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [new BrowseResult { References = references }]
                        });
                    });
            m_session.Setup(session => session.ReadAsync(
                    It.IsAny<RequestHeader>(), 0, TimestampsToReturn.Both,
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, requests, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        return new ValueTask<ReadResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = ArrayOf.Wrapped(requests.ToList().Select(value => m_values[value.NodeId])
                                .ToArray())
                        });
                    });
            m_session.Setup(session => session.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    m_calls.AddRange(requests.ToList());
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [m_callResult]
                    });
                });
            m_client = new NetworkModelClient(m_session.Object, m_telemetry);
        }

        /// <summary>
        /// Discovers vendor subtypes and interface implementations without fixed namespace indices.
        /// </summary>
        [Test]
        public async Task DiscoveryMatchesVendorSubtypeAndImplementedInterfaceWithoutFixedNamespaceAsync()
        {
            NodeId vendorType = new("VendorType", 2);
            NodeId implemented = new("Implemented", 2);
            SetBrowse(ObjectIds.NetworkInterfaces, ReferenceTypeIds.HasTypeDefinition,
                Reference(ObjectTypeIds.FolderType));
            SetBrowse(ObjectIds.NetworkInterfaces, ReferenceTypeIds.HierarchicalReferences,
                Reference(s_interface), Reference(implemented));
            SetBrowse(s_interface, ReferenceTypeIds.HasTypeDefinition, Reference(vendorType));
            SetBrowse(vendorType, ReferenceTypeIds.HasSubtype,
                BrowseDirection.Inverse, Reference(ObjectTypeIds.IetfBaseNetworkInterfaceType));
            SetBrowse(implemented, ReferenceTypeIds.HasInterface,
                Reference(ObjectTypeIds.IIetfBaseNetworkInterfaceType));

            ArrayOf<NodeId> actual = await m_client.DiscoverInterfacesAsync().ConfigureAwait(false);

            Assert.That(actual.ToArray(), Is.EquivalentTo(new[] { s_interface, implemented }));
        }

        /// <summary>
        /// Skips objects the server removes between browsing their parent and browsing them.
        /// </summary>
        [Test]
        public async Task DiscoverySkipsChildRemovedDuringTraversalAsync()
        {
            NodeId removed = new("Removed", 2);
            SetBrowse(ObjectIds.NetworkInterfaces, ReferenceTypeIds.HasTypeDefinition,
                Reference(ObjectTypeIds.FolderType));
            SetBrowse(ObjectIds.NetworkInterfaces, ReferenceTypeIds.HierarchicalReferences,
                Reference(removed), Reference(s_interface));
            SetBrowse(s_interface, ReferenceTypeIds.HasTypeDefinition,
                Reference(ObjectTypeIds.IetfBaseNetworkInterfaceType));
            m_removed.Add(removed);

            ArrayOf<NodeId> actual = await m_client.DiscoverInterfacesAsync().ConfigureAwait(false);

            Assert.That(actual.ToArray(), Is.EqualTo(new[] { s_interface }));
        }

        /// <summary>
        /// Still reports a missing discovery entry point instead of returning an empty result.
        /// </summary>
        [Test]
        public async Task DiscoveryReportsMissingEntryPointAsync()
        {
            m_removed.Add(ObjectIds.NetworkInterfaces);

            ServiceResultException? ex = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_client.DiscoverInterfacesAsync().ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(ex!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        /// <summary>
        /// Recognizes vendor interface ancestry declared on an instance or its concrete type's ancestor.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DiscoveryMatchesVendorInterfaceSubtypeOnInstanceOrTypeAncestorAsync(bool onTypeAncestor)
        {
            NodeId concreteType = new("VendorConcreteType", 2);
            NodeId parentType = new("VendorParentType", 2);
            NodeId vendorInterface = new("VendorInterface", 2);
            NodeId parentInterface = new("VendorParentInterface", 2);
            NodeId unrelated = new("UnrelatedInstance", 2);
            NodeId unrelatedInterface = new("UnrelatedInterface", 2);
            NodeId unrelatedParent = new("UnrelatedParentInterface", 2);
            SetBrowse(ObjectIds.NetworkInterfaces, ReferenceTypeIds.HierarchicalReferences,
                Reference(s_interface), Reference(unrelated));
            if (onTypeAncestor)
            {
                SetBrowse(s_interface, ReferenceTypeIds.HasTypeDefinition, Reference(concreteType));
                SetBrowse(concreteType, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse, Reference(parentType));
            }
            SetBrowse(onTypeAncestor ? parentType : s_interface, ReferenceTypeIds.HasInterface,
                Reference(vendorInterface));
            SetBrowse(vendorInterface, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse,
                Reference(parentInterface));
            SetBrowse(parentInterface, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse,
                Reference(ObjectTypeIds.IIetfBaseNetworkInterfaceType));
            SetBrowse(unrelated, ReferenceTypeIds.HasInterface, Reference(unrelatedInterface));
            SetBrowse(unrelatedInterface, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse,
                Reference(unrelatedParent));
            SetBrowse(unrelatedParent, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse,
                Reference(unrelatedInterface));

            ArrayOf<NodeId> actual = await m_client.DiscoverInterfacesAsync().ConfigureAwait(false);

            Assert.That(actual.ToArray(), Is.EqualTo(new[] { s_interface }));
        }

        /// <summary>
        /// Discovers talkers and listeners implementing a vendor subtype of the TSN stream interface.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StreamDiscoveryMatchesVendorInterfaceSubtypeAsync(bool talkers)
        {
            NodeId vendorInterface = new("VendorStreamInterface", 2);
            SetBrowse(talkers ? ObjectIds.TalkerStreams : ObjectIds.ListenerStreams,
                ReferenceTypeIds.HierarchicalReferences, Reference(s_interface));
            SetBrowse(s_interface, ReferenceTypeIds.HasInterface, Reference(vendorInterface));
            SetBrowse(vendorInterface, ReferenceTypeIds.HasSubtype, BrowseDirection.Inverse,
                Reference(ObjectTypeIds.IIeeeBaseTsnStreamType));

            ArrayOf<NodeId> actual = await m_client.DiscoverStreamsAsync(talkers).ConfigureAwait(false);

            Assert.That(actual.ToArray(), Is.EqualTo(new[] { s_interface }));
        }

        /// <summary>
        /// Returns empty discovery results without issuing an empty Read request.
        /// </summary>
        [Test]
        public async Task EmptyDiscoveryAndParameterSetRemainEmptyAsync()
        {
            Assert.That((await m_client.DiscoverInterfacesAsync().ConfigureAwait(false)).Count, Is.Zero);
            Assert.That((await m_client.ReadParametersAsync(s_interface).ConfigureAwait(false)).Count, Is.Zero);
            m_session.Verify(session => session.ReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Stops traversal at an explicitly assigned mapping table.
        /// </summary>
        [Test]
        public async Task DirectMappingStopsLowerLayerFallbackAsync()
        {
            SetBrowse(s_interface, ReferenceTypeIds.UsesPriorityMappingTable, Reference(s_table));
            SetBrowse(s_table, ReferenceTypeIds.HasTypeDefinition,
                Reference(ObjectTypeIds.PriorityMappingTableType));
            SetBrowse(s_interface, ReferenceTypeIds.HasLowerLayerInterface, Reference(s_interface));

            ArrayOf<NodeId> result = await m_client.GetEffectiveMappingTablesAsync(s_interface).ConfigureAwait(false);

            Assert.That(result.ToArray(), Is.EqualTo(new[] { s_table }));
            m_session.Verify(session => session.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.Is<ArrayOf<BrowseDescription>>(requests =>
                    requests[0].ReferenceTypeId == ReferenceTypeIds.HasLowerLayerInterface),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Deduplicates a mapping inherited through multiple lower-layer branches.
        /// </summary>
        [Test]
        public async Task LayeredBranchesDeduplicateInheritedMappingAsync()
        {
            NodeId lower = new("lower", 2);
            NodeId second = new("second", 2);
            SetBrowse(s_interface, ReferenceTypeIds.HasLowerLayerInterface, Reference(lower), Reference(second));
            SetBrowse(lower, ReferenceTypeIds.UsesPriorityMappingTable, Reference(s_table));
            SetBrowse(second, ReferenceTypeIds.UsesPriorityMappingTable, Reference(s_table));
            SetBrowse(s_table, ReferenceTypeIds.HasTypeDefinition,
                Reference(ObjectTypeIds.PriorityMappingTableType));

            Assert.That((await m_client.GetEffectiveMappingTablesAsync(s_interface).ConfigureAwait(false))
                .ToArray(), Is.EqualTo(new[] { s_table }));
        }

        /// <summary>
        /// Resolves shared lower-layer subgraphs once, including empty resolutions and direct-table barriers.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SharedLowerLayerSubgraphsAreResolvedOnceAsync(bool hasTables)
        {
            const int layerCount = 13;
            var layers = new NodeId[layerCount][];
            for (int layer = 0; layer < layerCount; layer++)
            {
                layers[layer] = [new NodeId($"left{layer}", 2), new NodeId($"right{layer}", 2)];
            }
            SetBrowse(s_interface, ReferenceTypeIds.HasLowerLayerInterface,
                Reference(layers[0][0]), Reference(layers[0][1]));
            for (int layer = 0; layer < layerCount - 1; layer++)
            {
                foreach (NodeId id in layers[layer])
                {
                    SetBrowse(id, ReferenceTypeIds.HasLowerLayerInterface,
                        Reference(layers[layer + 1][0]), Reference(layers[layer + 1][1]));
                }
            }
            NodeId otherTable = new("otherMapping", 2);
            if (hasTables)
            {
                SetBrowse(layers[^1][0], ReferenceTypeIds.UsesPriorityMappingTable, Reference(s_table));
                SetBrowse(layers[^1][1], ReferenceTypeIds.UsesPriorityMappingTable, Reference(otherTable));
                SetBrowse(s_table, ReferenceTypeIds.HasTypeDefinition,
                    Reference(ObjectTypeIds.PriorityMappingTableType));
                SetBrowse(otherTable, ReferenceTypeIds.HasTypeDefinition,
                    Reference(ObjectTypeIds.PriorityMappingTableType));
                foreach (NodeId id in layers[^1])
                {
                    SetBrowse(id, ReferenceTypeIds.HasLowerLayerInterface, Reference(s_interface));
                }
            }

            ArrayOf<NodeId> actual = await m_client.GetEffectiveMappingTablesAsync(s_interface).ConfigureAwait(false);

            Assert.That(actual.ToArray(), Is.EquivalentTo(hasTables ? new[] { s_table, otherTable } : []));
            for (int layer = 0; layer < layerCount; layer++)
            {
                foreach (NodeId id in layers[layer])
                {
                    m_session.Verify(session => session.BrowseAsync(
                        It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                        It.Is<ArrayOf<BrowseDescription>>(requests =>
                            requests[0].NodeId == id &&
                            requests[0].ReferenceTypeId == ReferenceTypeIds.UsesPriorityMappingTable),
                        It.IsAny<CancellationToken>()), Times.Once);
                    m_session.Verify(session => session.BrowseAsync(
                        It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                        It.Is<ArrayOf<BrowseDescription>>(requests =>
                            requests[0].NodeId == id &&
                            requests[0].ReferenceTypeId == ReferenceTypeIds.HasLowerLayerInterface),
                        It.IsAny<CancellationToken>()),
                        hasTables && layer == layerCount - 1 ? Times.Never() : Times.Once());
                }
            }
        }

        /// <summary>
        /// Does not mistake a completed shared branch for permission to revisit an active ancestor.
        /// </summary>
        [Test]
        public async Task SharedLowerLayerDoesNotHideActivePathCycleAsync()
        {
            NodeId first = new("first", 2);
            NodeId second = new("second", 2);
            NodeId shared = new("shared", 2);
            SetBrowse(s_interface, ReferenceTypeIds.HasLowerLayerInterface, Reference(first), Reference(second));
            SetBrowse(first, ReferenceTypeIds.HasLowerLayerInterface, Reference(shared));
            SetBrowse(second, ReferenceTypeIds.HasLowerLayerInterface, Reference(shared), Reference(s_interface));

            await Assert.ThatAsync(
                async () => await m_client.GetEffectiveMappingTablesAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);
            m_session.Verify(session => session.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.Is<ArrayOf<BrowseDescription>>(requests =>
                    requests[0].NodeId == shared &&
                    requests[0].ReferenceTypeId == ReferenceTypeIds.UsesPriorityMappingTable),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Accepts a mapping table whose concrete type derives from the standard table type.
        /// </summary>
        [Test]
        public async Task MappingTargetAcceptsVendorSubtypeOfPriorityMappingTableTypeAsync()
        {
            NodeId vendorType = new("VendorPriorityTableType", 2);
            SetBrowse(s_interface, ReferenceTypeIds.UsesPriorityMappingTable, Reference(s_table));
            SetBrowse(s_table, ReferenceTypeIds.HasTypeDefinition, Reference(vendorType));
            SetBrowse(vendorType, ReferenceTypeIds.HasSubtype,
                BrowseDirection.Inverse, Reference(ObjectTypeIds.PriorityMappingTableType));

            ArrayOf<NodeId> result = await m_client.GetEffectiveMappingTablesAsync(s_interface).ConfigureAwait(false);

            Assert.That(result.ToArray(), Is.EqualTo(new[] { s_table }));
        }

        /// <summary>
        /// Rejects a mapping target without a HasTypeDefinition reference.
        /// </summary>
        [Test]
        public async Task MappingTargetWithoutTypeDefinitionIsRejected()
        {
            SetBrowse(s_interface, ReferenceTypeIds.UsesPriorityMappingTable, Reference(s_table));

            await Assert.ThatAsync(
                async () => await m_client.GetEffectiveMappingTablesAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTypeDefinitionInvalid)).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects an object whose type is unrelated to the standard mapping-table type.
        /// </summary>
        [Test]
        public async Task MappingTargetWithIncompatibleTypeDefinitionIsRejected()
        {
            SetBrowse(s_interface, ReferenceTypeIds.UsesPriorityMappingTable, Reference(s_table));
            SetBrowse(s_table, ReferenceTypeIds.HasTypeDefinition, Reference(ObjectTypeIds.FolderType));

            await Assert.ThatAsync(
                async () => await m_client.GetEffectiveMappingTablesAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTypeDefinitionInvalid)).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects cycles rather than returning an arbitrary mapping.
        /// </summary>
        [Test]
        public async Task CyclicLowerLayerGraphIsRejected()
        {
            NodeId lower = new("lower", 2);
            SetBrowse(s_interface, ReferenceTypeIds.HasLowerLayerInterface, Reference(lower));
            SetBrowse(lower, ReferenceTypeIds.HasLowerLayerInterface, Reference(s_interface));

            await Assert.ThatAsync(
                async () => await m_client.GetEffectiveMappingTablesAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);
        }

        /// <summary>
        /// Reports ambiguous direct assignments instead of selecting the first table.
        /// </summary>
        [Test]
        public async Task MultipleDirectTablesAreRejectedInsteadOfChoosingOne()
        {
            SetBrowse(s_interface, ReferenceTypeIds.UsesPriorityMappingTable,
                Reference(s_table), Reference(new NodeId("other", 2)));

            await Assert.ThatAsync(
                async () => await m_client.GetEffectiveMappingTablesAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects a remote-server reference instead of resolving it as a local NodeId.
        /// </summary>
        [Test]
        public async Task RemoteLowerLayerCannotBeMistakenForLocalNode()
        {
            SetBrowse(s_interface, ReferenceTypeIds.HasLowerLayerInterface, new ReferenceDescription
            {
                NodeId = new ExpandedNodeId("remote", 2, null, 1)
            });

            await Assert.ThatAsync(
                async () => await m_client.GetEffectiveMappingTablesAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNotSupported)).ConfigureAwait(false);
        }

        /// <summary>
        /// Distinguishes standard parameters from vendor homonyms and preserves acquisition quality.
        /// </summary>
        [Test]
        public async Task NamespaceQualifiedParametersRetainBadQualityAndAcquisitionTimeAsync()
        {
            NodeId standard = new("speed", 2);
            NodeId homonym = new("vendorSpeed", 2);
            DateTime timestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            SetBrowse(s_interface, ReferenceTypeIds.HasChild,
                new ReferenceDescription { NodeId = standard, BrowseName = new QualifiedName(BrowseNames.Speed, 0) },
                new ReferenceDescription { NodeId = homonym, BrowseName = new QualifiedName(BrowseNames.Speed, 2) });
            m_values[standard] = new DataValue(
                new Variant(1_000_000_000UL), StatusCodes.BadCommunicationError, timestamp);
            m_values[homonym] = new DataValue(new Variant(7UL));

            NetworkInterfaceValues actual = await m_client.ReadInterfaceAsync(s_interface).ConfigureAwait(false);

            Assert.That(actual.Speed.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadCommunicationError));
            Assert.That(actual.Speed.DataValue.SourceTimestamp, Is.EqualTo((DateTimeUtc)timestamp));
            Assert.That(actual.Speed.DataValue.WrappedValue, Is.EqualTo(new Variant(1_000_000_000UL)));
            Assert.That(actual.AdminStatus.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
            Assert.That(actual.PhysicalAddress.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        /// <summary>
        /// Returns bad mapping quality without decoding the unavailable payload.
        /// </summary>
        [Test]
        public async Task BadMappingQualityDoesNotDecodeAnInvalidPayloadAsync()
        {
            SetMappingValue(new DataValue(new Variant("not an entry array"), StatusCodes.BadNotConnected));

            NetworkMappingValues actual = await m_client.ReadMappingTableAsync(s_table).ConfigureAwait(false);

            Assert.That(actual.Acquisition.DataValue.StatusCode, Is.EqualTo(StatusCodes.BadNotConnected));
            Assert.That(actual.Entries.Count, Is.Zero);
        }

        /// <summary>
        /// Rejects a good-quality mapping value whose payload has the wrong type.
        /// </summary>
        [Test]
        public async Task GoodMappingQualityWithWrongTypeIsRejected()
        {
            SetMappingValue(new DataValue(new Variant("not an entry array")));

            await Assert.ThatAsync(async () => await m_client.ReadMappingTableAsync(s_table).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTypeMismatch)).ConfigureAwait(false);
        }

        /// <summary>
        /// Preserves method arguments, sentinel values and per-argument diagnostics.
        /// </summary>
        [Test]
        public async Task MappingCallsPreserveWireArgumentsSentinelsAndOperationDiagnosticsAsync()
        {
            m_callResult = new CallMethodResult
            {
                StatusCode = StatusCodes.BadOutOfRange,
                InputArgumentResults =
                [
                    StatusCodes.Good, StatusCodes.Good, StatusCodes.BadOutOfRange, StatusCodes.Good
                ]
            };

            CallMethodResult result = await m_client.AddPriorityMappingEntryAsync(
                s_table, "urn:test", "Control").ConfigureAwait(false);

            Assert.That(result, Is.SameAs(m_callResult));
            Assert.That(m_calls, Has.Count.EqualTo(1));
            Assert.That(m_calls[0].ObjectId, Is.EqualTo(s_table));
            Assert.That(m_calls[0].MethodId,
                Is.EqualTo(MethodIds.PriorityMappingTableType_AddPriorityMappingEntry));
            Assert.That(m_calls[0].InputArguments.ToArray(), Is.EqualTo(new[]
            {
                new Variant("urn:test"), new Variant("Control"),
                new Variant(byte.MaxValue), new Variant(uint.MaxValue)
            }));
            Assert.That(result.InputArgumentResults[2], Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        /// <summary>
        /// Returns a rejected deletion without replaying the operation.
        /// </summary>
        [Test]
        public async Task DeleteMappingCallIsNotRetriedAfterProviderRejectionAsync()
        {
            m_callResult = new CallMethodResult { StatusCode = StatusCodes.BadBrowseNameInvalid };

            CallMethodResult result = await m_client.DeletePriorityMappingEntryAsync(
                s_table, "urn:test", "Control").ConfigureAwait(false);

            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
            Assert.That(m_calls, Has.Count.EqualTo(1));
            Assert.That(m_calls[0].MethodId,
                Is.EqualTo(MethodIds.PriorityMappingTableType_DeletePriorityMappingEntry));
            Assert.That(m_calls[0].InputArguments.ToArray(),
                Is.EqualTo(new[] { new Variant("urn:test"), new Variant("Control") }));
        }

        /// <summary>
        /// Releases the active continuation after a BrowseNext failure.
        /// </summary>
        [Test]
        public async Task BrowseContinuationIsReleasedWhenNextPageFails()
        {
            ByteString point = ByteString.From(new byte[] { 1, 2, 3 }.AsSpan());
            m_session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { ContinuationPoint = point }]
                });
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), false, It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadCommunicationError));
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), true, It.IsAny<ArrayOf<ByteString>>(), CancellationToken.None))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult()]
                });

            await Assert.ThatAsync(async () => await m_client.BrowseObjectsAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadCommunicationError)).ConfigureAwait(false);

            m_session.Verify(session => session.BrowseNextAsync(It.IsAny<RequestHeader>(), true,
                It.Is<ArrayOf<ByteString>>(points => points.Count == 1 && points[0] == point),
                CancellationToken.None), Times.Once);
        }

        /// <summary>
        /// Preserves the browse failure and reports a bad per-point release status.
        /// </summary>
        [Test]
        public async Task BrowseReleaseFailureDoesNotReplaceTheOriginalBrowseFailure()
        {
            using var logs = new RecordingLoggerProvider();
            m_telemetry.LoggerFactory.AddProvider(logs);
            ByteString point = ByteString.From(new byte[] { 1, 2, 3 }.AsSpan());
            m_session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { ContinuationPoint = point }]
                });
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), false, It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ServiceResultException(StatusCodes.BadCommunicationError));
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), true, It.IsAny<ArrayOf<ByteString>>(), CancellationToken.None))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { StatusCode = StatusCodes.BadContinuationPointInvalid }]
                });

            await Assert.ThatAsync(async () => await m_client.BrowseObjectsAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadCommunicationError)).ConfigureAwait(false);

            RecordedLogRecord warning = logs.Records.Single(record =>
                record.EventId.Id == ClientEventIds.NetworkModelClient);
            Assert.That(warning.LogLevel, Is.EqualTo(LogLevel.Warning));
            Assert.That(warning.Exception,
                Is.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadContinuationPointInvalid));
            m_session.Verify(session => session.BrowseNextAsync(It.IsAny<RequestHeader>(), true,
                It.Is<ArrayOf<ByteString>>(points => points.Count == 1 && points[0] == point),
                CancellationToken.None), Times.Once);
        }

        /// <summary>
        /// Releases the continuation returned before initial-page diagnostics fail validation.
        /// </summary>
        [Test]
        public async Task BrowseContinuationIsReleasedWhenInitialDiagnosticsAreMalformed()
        {
            ByteString point = ByteString.From(new byte[] { 1, 2, 3 }.AsSpan());
            m_session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { ContinuationPoint = point }],
                    DiagnosticInfos = [new DiagnosticInfo(), new DiagnosticInfo()]
                });
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), true, It.IsAny<ArrayOf<ByteString>>(), CancellationToken.None))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult()]
                });

            await Assert.ThatAsync(async () => await m_client.BrowseObjectsAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUnexpectedError)).ConfigureAwait(false);

            m_session.Verify(session => session.BrowseNextAsync(It.IsAny<RequestHeader>(), true,
                It.Is<ArrayOf<ByteString>>(points => points.Count == 1 && points[0] == point),
                CancellationToken.None), Times.Once);
        }

        /// <summary>
        /// Releases the continuation returned by BrowseNext when its diagnostics fail validation.
        /// </summary>
        [Test]
        public async Task BrowseContinuationIsReleasedWhenNextPageDiagnosticsAreMalformed()
        {
            ByteString firstPoint = ByteString.From(new byte[] { 1, 2, 3 }.AsSpan());
            ByteString nextPoint = ByteString.From(new byte[] { 4, 5, 6 }.AsSpan());
            m_session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { ContinuationPoint = firstPoint }]
                });
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), false, It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { ContinuationPoint = nextPoint }],
                    DiagnosticInfos = [new DiagnosticInfo(), new DiagnosticInfo()]
                });
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), true, It.IsAny<ArrayOf<ByteString>>(), CancellationToken.None))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult()]
                });

            await Assert.ThatAsync(async () => await m_client.BrowseObjectsAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUnexpectedError)).ConfigureAwait(false);

            m_session.Verify(session => session.BrowseNextAsync(It.IsAny<RequestHeader>(), true,
                It.Is<ArrayOf<ByteString>>(points => points.Count == 1 && points[0] == nextPoint),
                CancellationToken.None), Times.Once);
        }

        /// <summary>
        /// Releases all continuation points from a BrowseNext response with extra results.
        /// </summary>
        [Test]
        public async Task BrowseReleasesContinuationsFromMalformedNextPageExtraResults()
        {
            ByteString firstPoint = ByteString.From(new byte[] { 1, 2, 3 }.AsSpan());
            ByteString nextPoint = ByteString.From(new byte[] { 4, 5, 6 }.AsSpan());
            ByteString extraPoint = ByteString.From(new byte[] { 7, 8, 9 }.AsSpan());
            m_session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { ContinuationPoint = firstPoint }]
                });
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), false, It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowseResult { ContinuationPoint = nextPoint },
                        new BrowseResult { ContinuationPoint = extraPoint }
                    ]
                });
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), true, It.IsAny<ArrayOf<ByteString>>(), CancellationToken.None))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult()]
                });

            await Assert.ThatAsync(async () => await m_client.BrowseObjectsAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUnexpectedError)).ConfigureAwait(false);

            m_session.Verify(session => session.BrowseNextAsync(It.IsAny<RequestHeader>(), true,
                It.Is<ArrayOf<ByteString>>(points =>
                    points.Count == 2 && points[0] == nextPoint && points[1] == extraPoint),
                CancellationToken.None), Times.Once);
        }

        /// <summary>
        /// Retains the last known continuation when BrowseNext fails before returning results.
        /// </summary>
        [Test]
        public async Task BrowseContinuationIsReleasedWhenNextPageServiceFailsWithoutResults()
        {
            ByteString point = ByteString.From(new byte[] { 1, 2, 3 }.AsSpan());
            m_session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { ContinuationPoint = point }]
                });
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), false, It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadServerTooBusy },
                    Results = []
                });
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), true, It.IsAny<ArrayOf<ByteString>>(), CancellationToken.None))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult()]
                });

            await Assert.ThatAsync(async () => await m_client.BrowseObjectsAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadServerTooBusy)).ConfigureAwait(false);

            m_session.Verify(session => session.BrowseNextAsync(It.IsAny<RequestHeader>(), true,
                It.Is<ArrayOf<ByteString>>(points => points.Count == 1 && points[0] == point),
                CancellationToken.None), Times.Once);
        }

        /// <summary>
        /// Releases every continuation point in an initial response with extra results.
        /// </summary>
        [Test]
        public async Task BrowseReleasesContinuationsFromMalformedExtraResults()
        {
            ByteString firstPoint = ByteString.From(new byte[] { 1, 2, 3 }.AsSpan());
            ByteString extraPoint = ByteString.From(new byte[] { 4, 5, 6 }.AsSpan());
            m_session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowseResult { ContinuationPoint = firstPoint },
                        new BrowseResult { ContinuationPoint = extraPoint }
                    ]
                });
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), true, It.IsAny<ArrayOf<ByteString>>(), CancellationToken.None))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult()]
                });

            await Assert.ThatAsync(async () => await m_client.BrowseObjectsAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadUnexpectedError)).ConfigureAwait(false);

            m_session.Verify(session => session.BrowseNextAsync(It.IsAny<RequestHeader>(), true,
                It.Is<ArrayOf<ByteString>>(points =>
                    points.Count == 2 && points[0] == firstPoint && points[1] == extraPoint),
                CancellationToken.None), Times.Once);
        }

        /// <summary>
        /// Decodes generated entry arrays without dropping their identities or sentinels.
        /// </summary>
        [Test]
        public async Task GoodMappingArrayRetainsEveryGeneratedEntryAsync()
        {
            ArrayOf<PriorityMappingEntryType> entries =
            [
                new PriorityMappingEntryType
                {
                    MappingUri = "urn:first", PriorityLabel = "Control",
                    PriorityValue_PCP = 7, PriorityValue_DSCP = 63
                },
                new PriorityMappingEntryType
                {
                    MappingUri = "urn:second", PriorityLabel = "Control",
                    PriorityValue_PCP = byte.MaxValue, PriorityValue_DSCP = uint.MaxValue
                }
            ];
            SetMappingValue(new DataValue(Variant.FromStructure(entries)));

            NetworkMappingValues actual = await m_client.ReadMappingTableAsync(s_table).ConfigureAwait(false);

            Assert.That(actual.Entries.Count, Is.EqualTo(2));
            Assert.That(actual.Entries[0].MappingUri, Is.EqualTo("urn:first"));
            Assert.That(actual.Entries[1].PriorityValue_PCP, Is.EqualTo(byte.MaxValue));
            Assert.That(actual.Entries[1].PriorityValue_DSCP, Is.EqualTo(uint.MaxValue));
        }

        /// <summary>
        /// Propagates an unknown lower-layer target's service status.
        /// </summary>
        [Test]
        public async Task MissingLowerLayerReturnsItsServiceStatus()
        {
            NodeId missing = new("missing", 2);
            SetBrowse(s_interface, ReferenceTypeIds.HasLowerLayerInterface, Reference(missing));
            m_session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.Is<ArrayOf<BrowseDescription>>(requests => requests[0].NodeId == missing),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { StatusCode = StatusCodes.BadNodeIdUnknown }]
                });

            await Assert.ThatAsync(
                async () => await m_client.GetEffectiveMappingTablesAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNodeIdUnknown)).ConfigureAwait(false);
        }

        /// <summary>
        /// Validates a direct mapping target before returning its NodeId.
        /// </summary>
        [Test]
        public async Task MissingDirectMappingTargetReturnsItsServiceStatus()
        {
            SetBrowse(s_interface, ReferenceTypeIds.UsesPriorityMappingTable, Reference(s_table));
            m_session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.Is<ArrayOf<BrowseDescription>>(requests => requests[0].NodeId == s_table),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { StatusCode = StatusCodes.BadNodeIdUnknown }]
                });

            await Assert.ThatAsync(
                async () => await m_client.GetEffectiveMappingTablesAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNodeIdUnknown)).ConfigureAwait(false);
        }

        /// <summary>
        /// Bounds repeated empty pages and releases the final continuation.
        /// </summary>
        [Test]
        public async Task RepeatedEmptyBrowsePagesAreBoundedAndTheContinuationIsReleased()
        {
            ByteString point = ByteString.From(new byte[] { 1, 2, 3 }.AsSpan());
            m_session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { ContinuationPoint = point }]
                });
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), false, It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { ContinuationPoint = point }]
                });
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), true, It.IsAny<ArrayOf<ByteString>>(), CancellationToken.None))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult()]
                });

            await Assert.ThatAsync(async () => await m_client.BrowseObjectsAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadTooManyOperations)).ConfigureAwait(false);
            m_session.Verify(session => session.BrowseNextAsync(
                It.IsAny<RequestHeader>(), false, It.IsAny<ArrayOf<ByteString>>(),
                It.IsAny<CancellationToken>()), Times.Exactly(4096));
            m_session.Verify(session => session.BrowseNextAsync(
                It.IsAny<RequestHeader>(), true,
                It.Is<ArrayOf<ByteString>>(values => values.Count == 1 && values[0] == point),
                CancellationToken.None), Times.Once);
        }

        /// <summary>
        /// Propagates a failed Read service rather than fabricating missing parameters.
        /// </summary>
        [Test]
        public async Task ReadServiceFailureIsNotConvertedToMissingParameters()
        {
            SetBrowse(s_interface, ReferenceTypeIds.HasChild,
                new ReferenceDescription
                {
                    NodeId = new NodeId("speed", 2),
                    BrowseName = new QualifiedName(BrowseNames.Speed, 0)
                });
            m_session.Setup(session => session.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadNotConnected }
                });

            await Assert.ThatAsync(async () => await m_client.ReadInterfaceAsync(s_interface).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNotConnected)).ConfigureAwait(false);
        }

        /// <summary>
        /// Observes pre-cancellation before issuing a Browse request.
        /// </summary>
        [Test]
        public async Task DiscoveryHonorsCancellationBeforeIssuingRequests()
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();

            await Assert.ThatAsync(
                async () => await m_client.DiscoverInterfacesAsync(cancelled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            m_session.Verify(session => session.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Combines successful pages without releasing an already consumed continuation.
        /// </summary>
        [Test]
        public async Task BrowseContinuationCombinesPagesWithoutReleasingConsumedPointAsync()
        {
            ByteString point = ByteString.From(new byte[] { 1, 2, 3 }.AsSpan());
            m_session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { ContinuationPoint = point, References = [Reference(s_interface)] }]
                });
            m_session.Setup(session => session.BrowseNextAsync(
                    It.IsAny<RequestHeader>(), false, It.IsAny<ArrayOf<ByteString>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { References = [Reference(s_table)] }]
                });

            ArrayOf<ReferenceDescription> references = await m_client.BrowseObjectsAsync(s_interface)
                .ConfigureAwait(false);

            Assert.That(references.ToList().Select(reference => reference.NodeId),
                Is.EqualTo(new[] { new ExpandedNodeId(s_interface), new ExpandedNodeId(s_table) }));
            m_session.Verify(session => session.BrowseNextAsync(
                It.IsAny<RequestHeader>(), true, It.IsAny<ArrayOf<ByteString>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Supplies a standard mapping-entry variable and its acquisition.
        /// </summary>
        private void SetMappingValue(DataValue value)
        {
            NodeId variable = new("entries", 2);
            SetBrowse(s_table, ReferenceTypeIds.HasChild, new ReferenceDescription
            {
                NodeId = variable,
                BrowseName = new QualifiedName(BrowseNames.PriorityMapppingEntries, 0)
            });
            m_values[variable] = value;
        }

        /// <summary>
        /// Supplies forward references for one browse source and reference type.
        /// </summary>
        private void SetBrowse(NodeId id, NodeId reference, params ReferenceDescription[] values)
        {
            SetBrowse(id, reference, BrowseDirection.Forward, values);
        }

        /// <summary>
        /// Supplies references for one browse source, reference type and direction.
        /// </summary>
        private void SetBrowse(
            NodeId id, NodeId reference, BrowseDirection direction, params ReferenceDescription[] values)
        {
            m_browse[(id, reference, direction)] = ArrayOf.Wrapped(values);
        }

        /// <summary>
        /// Creates a local reference target.
        /// </summary>
        private static ReferenceDescription Reference(NodeId id)
        {
            return new ReferenceDescription { NodeId = id };
        }

        /// <summary>Strict service boundary for the current test.</summary>
        private Mock<ISessionClient> m_session = null!;

        /// <summary>Telemetry context used by the client and captured logs.</summary>
        private ITelemetryContext m_telemetry = null!;

        /// <summary>Client bound to the current service boundary.</summary>
        private NetworkModelClient m_client = null!;

        /// <summary>Next provider-confirmed method result.</summary>
        private CallMethodResult m_callResult = null!;

        /// <summary>References returned by source, reference type and direction.</summary>
        private readonly Dictionary<(NodeId, NodeId, BrowseDirection), ArrayOf<ReferenceDescription>> m_browse = [];

        /// <summary>Browse sources the server reports as removed (BadNodeIdUnknown).</summary>
        private readonly HashSet<NodeId> m_removed = [];

        /// <summary>Read acquisitions keyed by their variable NodeIds.</summary>
        private readonly Dictionary<NodeId, DataValue> m_values = [];

        /// <summary>Recorded wire method requests.</summary>
        private readonly List<CallMethodRequest> m_calls = [];

        /// <summary>Application-owned interface identity.</summary>
        private static readonly NodeId s_interface = new("interface", 2);

        /// <summary>Application-owned mapping-table identity.</summary>
        private static readonly NodeId s_table = new("mapping", 2);
    }
}
