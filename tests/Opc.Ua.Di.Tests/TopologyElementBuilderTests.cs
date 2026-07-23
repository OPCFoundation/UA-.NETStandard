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

using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Pumps;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Pumps;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests the additive topology-element builder surface with generated
    /// Pump companion-model states.
    /// </summary>
    [TestFixture]
    [Category("Pumps")]
    [Category("TopologyElementBuilder")]
    public sealed class TopologyElementBuilderTests
    {
        private ServerFixture<StandardServer> m_fixture = null!;
        private PumpNodeManager m_manager = null!;
        private StandardServer m_server = null!;
        private ushort m_pumpsNamespaceIndex;

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new StandardServer(t))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
            m_manager = new PumpNodeManager(m_server.CurrentInstance, m_fixture.Config);
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await m_manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);
            m_pumpsNamespaceIndex = (ushort)m_server.CurrentInstance.NamespaceUris.GetIndex(
                global::Opc.Ua.Pumps.Namespaces.Pumps);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            m_manager.Dispose();
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task WrapsExistingPumpByInstanceNodeIdAndBrowseNameAsync()
        {
            var browseName = new QualifiedName(
                "Topology Lookup Pump",
                m_pumpsNamespaceIndex);
            PumpState pump = await m_manager.CreatePumpAsync(browseName)
                .ConfigureAwait(false);

            ITopologyElementBuilder<PumpState> byInstance =
                m_manager.TopologyElement(pump);
            ITopologyElementBuilder<PumpState> byNodeId =
                m_manager.TopologyElement<PumpState>(pump.NodeId);
            ITopologyElementBuilder<PumpState> byBrowseName =
                m_manager.TopologyElementByBrowseName<PumpState>(browseName);

            Assert.That(byInstance.Element, Is.SameAs(pump));
            Assert.That(byNodeId.Element, Is.SameAs(pump));
            Assert.That(byBrowseName.Element, Is.SameAs(pump));
        }

        [Test]
        public async Task ConfiguresGroupsReferencesAndRawStateAsync()
        {
            PumpState source = await m_manager.CreatePumpAsync(
                new QualifiedName("Topology Source Pump", m_pumpsNamespaceIndex))
                .ConfigureAwait(false);
            PumpState target = await m_manager.CreatePumpAsync(
                new QualifiedName("Topology Target Pump", m_pumpsNamespaceIndex))
                .ConfigureAwait(false);
            ITopologyElementBuilder<PumpState> builder =
                m_manager.TopologyElement(source);

            FunctionalGroupState? first = null;
            FunctionalGroupState? second = null;
            PumpState? observed = null;
            builder.WithDiagnosticsGroup(group => first = group.Group)
                .WithDiagnosticsGroup(group => second = group.Group)
                .ConnectsTo(target.NodeId)
                .Configure((element, _) => observed = element);

            Assert.That(second, Is.SameAs(first));
            Assert.That(observed, Is.SameAs(source));
            Assert.That(first, Is.Not.Null);
            Assert.That(
                m_manager.FindPredefinedNode(first!.NodeId),
                Is.SameAs(first));

            NodeId connectsTo = NodeId.Create(
                global::Opc.Ua.Di.ReferenceTypes.ConnectsTo,
                DiNodeManager.DiNamespaceUri,
                m_server.CurrentInstance.NamespaceUris);
            Assert.That(
                source.ReferenceExists(connectsTo, false, target.NodeId),
                Is.True);
        }

        [Test]
        public async Task PumpMeasurementPropertiesAreBrowsableAndReadableAsync()
        {
            const string measurementIdentifier =
                "5001_Pump #1_Operational_Measurements_FluidTemperature";
            ushort namespaceIndex = m_manager.DiNamespaceIndex;
            var measurementId = new NodeId(measurementIdentifier, namespaceIndex);
            var engineeringUnitsId = new NodeId(
                $"{measurementIdentifier}_EngineeringUnits",
                namespaceIndex);
            var euRangeId = new NodeId(
                $"{measurementIdentifier}_EURange",
                namespaceIndex);

            object handle = await m_manager.GetManagerHandleAsync(measurementId)
                .ConfigureAwait(false);
            Assert.That(handle, Is.Not.Null);

            using var continuationPoint = new ContinuationPoint
            {
                NodeToBrowse = handle,
                Manager = m_manager,
                View = new ViewDescription(),
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasProperty,
                IncludeSubtypes = true,
                ResultMask = BrowseResultMask.All
            };
            var references = new List<ReferenceDescription>();

            ContinuationPoint? result = await m_manager.BrowseAsync(
                new OperationContext(
                    new RequestHeader(),
                    null,
                    RequestType.Browse,
                    RequestLifetime.None),
                continuationPoint,
                references).ConfigureAwait(false);

            Assert.That(result, Is.Null);
            Assert.That(
                references,
                Has.Some.Matches<ReferenceDescription>(
                    reference =>
                        reference.NodeId == new ExpandedNodeId(engineeringUnitsId) &&
                        reference.BrowseName == new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits)));
            Assert.That(
                references,
                Has.Some.Matches<ReferenceDescription>(
                    reference =>
                        reference.NodeId == new ExpandedNodeId(euRangeId) &&
                        reference.BrowseName == new QualifiedName(Opc.Ua.BrowseNames.EURange)));

            await AssertPropertyReadableAsync(
                engineeringUnitsId,
                Opc.Ua.BrowseNames.EngineeringUnits,
                Opc.Ua.DataTypeIds.EUInformation).ConfigureAwait(false);
            await AssertPropertyReadableAsync(
                euRangeId,
                Opc.Ua.BrowseNames.EURange,
                Opc.Ua.DataTypeIds.Range).ConfigureAwait(false);
        }

        [Test]
        public void ByNodeIdRejectsNonMatchingStateType()
        {
            NodeId deviceSetId = NodeId.Create(
                global::Opc.Ua.Di.Objects.DeviceSet,
                DiNodeManager.DiNamespaceUri,
                m_server.CurrentInstance.NamespaceUris);

            ServiceResultException exception =
                Assert.Throws<ServiceResultException>(
                    () => m_manager.TopologyElement<PumpState>(deviceSetId))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void TopologyElementByBrowseNameRejectsMissingChild()
        {
            ServiceResultException exception =
                Assert.Throws<ServiceResultException>(
                    () => m_manager.TopologyElementByBrowseName<PumpState>(
                        new QualifiedName(
                            "Missing Topology Pump",
                            m_pumpsNamespaceIndex)))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task TopologyElementByBrowseNameRejectsWrongTypeAsync()
        {
            var browseName = new QualifiedName(
                "Topology Wrong Type Device",
                m_manager.DiNamespaceIndex);
            await m_manager.CreateDeviceAsync(browseName).ConfigureAwait(false);

            ServiceResultException exception =
                Assert.Throws<ServiceResultException>(
                    () => m_manager.TopologyElementByBrowseName<PumpState>(
                        browseName))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        [Test]
        public void DeviceByBrowseNameRejectsMissingChild()
        {
            ServiceResultException exception =
                Assert.Throws<ServiceResultException>(
                    () => m_manager.DeviceByBrowseName<DeviceState>(
                        new QualifiedName(
                            "Missing Device",
                            m_manager.DiNamespaceIndex)))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public async Task DeviceByBrowseNameRejectsWrongTypeAsync()
        {
            var browseName = new QualifiedName(
                "Device Wrong Type Pump",
                m_pumpsNamespaceIndex);
            await m_manager.CreatePumpAsync(browseName).ConfigureAwait(false);

            ServiceResultException exception =
                Assert.Throws<ServiceResultException>(
                    () => m_manager.DeviceByBrowseName<DeviceState>(
                        browseName))!;

            Assert.That(
                exception.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
        }

        private async Task AssertPropertyReadableAsync(
            NodeId nodeId,
            string browseName,
            NodeId dataType)
        {
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.BrowseName },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.DataType },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.ValueRank },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }
            ];
            var values = new List<DataValue> { default, default, default, default };
            var errors = new List<ServiceResult> { null!, null!, null!, null! };

            await m_manager.ReadAsync(
                new OperationContext(
                    new RequestHeader(),
                    null,
                    RequestType.Read,
                    RequestLifetime.None),
                0,
                nodesToRead,
                values,
                errors).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                for (int ii = 0; ii < nodesToRead.Count; ii++)
                {
                    Assert.That(nodesToRead[ii].Processed, Is.True);
                }
                Assert.That(errors, Has.All.Matches<ServiceResult>(ServiceResult.IsGood));
                Assert.That(values, Has.All.Matches<DataValue>(
                    value => StatusCode.IsGood(value.StatusCode)));
                Assert.That(
                    values[0].WrappedValue.TryGetValue(out QualifiedName actualBrowseName),
                    Is.True);
                Assert.That(actualBrowseName, Is.EqualTo(new QualifiedName(browseName)));
                Assert.That(
                    values[1].WrappedValue.TryGetValue(out NodeId actualDataType),
                    Is.True);
                Assert.That(actualDataType, Is.EqualTo(dataType));
                Assert.That(
                    values[2].WrappedValue.TryGetValue(out int actualValueRank),
                    Is.True);
                Assert.That(actualValueRank, Is.EqualTo(ValueRanks.Scalar));
                Assert.That(values[3].WrappedValue.IsNull, Is.False);
            });
        }
    }
}
