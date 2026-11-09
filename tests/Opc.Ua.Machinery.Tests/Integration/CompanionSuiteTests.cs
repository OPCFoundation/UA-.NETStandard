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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.CranesHoists;
using Opc.Ua.CranesHoists.Client;
using Opc.Ua.Di.Server.Hosting;
using Opc.Ua.GMS;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;
using Opc.Ua.Vision.MachineVision;
using Opc.Ua.Vision.Server;
using ClientSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Machinery.Tests.Integration
{
    /// <summary>
    /// Verifies actual mixed-runtime composition, not isolated family models or registration names.
    /// </summary>
    [TestFixture]
    [Category("Integration")]
    [Category("CompanionSuite")]
    [NonParallelizable]
    public sealed partial class CompanionSuiteTests
    {
        /// <summary>
        /// Every family uses the same real owner while repeated local identities, data and results remain isolated.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task AllFamiliesShareOneOwnerAndKeepIndependentTypedStateAsync(bool reordered, bool standalone)
        {
            await using var fixture = new CompanionSuiteFixture();
            await fixture.StartAsync(reordered, standalone: standalone).ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            ClientSession session = await fixture.ConnectAsync("CompositionReader").ConfigureAwait(false);
            Assert.That(session.ConfiguredEndpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(fixture.Services.Count(value => value.ServiceType == typeof(DiAddressSpaceOwnership)),
                Is.EqualTo(1));
            Assert.That(fixture.Provider.GetServices<OpcUaServerNodeManagerRegistration>().Count(), Is.EqualTo(1));
            Assert.That(fixture.Contexts, Has.Count.EqualTo(6));
            foreach (IMachineryBuildContext context in fixture.Contexts)
            {
                Assert.That(context.Manager, Is.SameAs(fixture.Context.Manager));
                Assert.That(context.DeviceSet, Is.SameAs(fixture.Context.DeviceSet));
                Assert.That(context.MachinesFolder, Is.SameAs(fixture.Context.MachinesFolder));
            }
            Assert.That(fixture.Contexts.Distinct().Count(), Is.GreaterThan(1));
            NodeId deviceSet = ExpandedNodeId.ToNodeId(Opc.Ua.Di.ObjectIds.DeviceSet, session.NamespaceUris);
            NodeId machinesFolder = ExpandedNodeId.ToNodeId(Opc.Ua.Machinery.ObjectIds.Machines, session.NamespaceUris);
            Assert.That(fixture.Context.DeviceSet.NodeId, Is.EqualTo(deviceSet));
            Assert.That(fixture.Context.MachinesFolder.NodeId, Is.EqualTo(machinesFolder));
            var server = ((ServerSystemContext)fixture.Context.Context).Server;
            foreach (NodeId root in new[] { deviceSet, machinesFolder }
                .Concat(Roots(fixture).Select(value => value.NodeId))
                .Concat(Roots(fixture).Select(value => value.TypeDefinitionId)).Distinct())
            {
                (_, IAsyncNodeManager? owner) = await server.NodeManager.GetManagerHandleAsync(root, ct)
                    .ConfigureAwait(false);
                Assert.That(owner, Is.SameAs(fixture.Context.Manager), root.ToString());
            }
            if (reordered)
            {
                Assert.That(session.NamespaceUris.GetIndex(GmsModel.NamespaceUri),
                    Is.LessThan(session.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi)));
            }

            List<ReferenceDescription> objects = await BrowseAsync(session, Opc.Ua.ObjectIds.ObjectsFolder, ct)
                .ConfigureAwait(false);
            Assert.That(objects.Count(value => ExpandedNodeId.ToNodeId(value.NodeId, session.NamespaceUris) ==
                deviceSet), Is.EqualTo(1));
            Assert.That(objects.Count(value => ExpandedNodeId.ToNodeId(value.NodeId, session.NamespaceUris) ==
                machinesFolder), Is.EqualTo(1));
            List<ReferenceDescription> machines = await BrowseAsync(session, machinesFolder, ct).ConfigureAwait(false);
            foreach (BaseInstanceState root in Roots(fixture).Where(root =>
                fixture.Machines.Any(machine => machine.State == root) ||
                fixture.Systems.Any(system => system.State == root) ||
                fixture.Cranes.Any(crane => crane.State == root)))
            {
                Assert.That(machines.Any(value => ExpandedNodeId.ToNodeId(value.NodeId, session.NamespaceUris) ==
                    root.NodeId), Is.True, root.BrowseName.ToString());
            }
            var ids = new Dictionary<NodeId, NodeState>();
            foreach (BaseInstanceState root in Roots(fixture))
            {
                IndexTree(fixture.Context.Context, root, ids);
            }
            Assert.That(ids, Has.Count.GreaterThan(100));
            foreach ((NodeId id, NodeState node) in ids)
            {
                Assert.That(fixture.Context.Manager.FindPredefinedNode(id), Is.SameAs(node), id.ToString());
            }

            await using var tools = new MachineToolClient(session, fixture.Telemetry);
            await using var gms = new GmsClient(session, fixture.Telemetry);
            var cranes = new CranesHoistsClient(session, fixture.Telemetry);
            var vision = new MachineVisionClient(session, fixture.Telemetry);
            ArrayOf<MachineToolDeviceEntry> discovered = await tools.DiscoverAsync(cancellationToken: ct)
                .ConfigureAwait(false);
            Assert.That(discovered.ToArray()!.Select(value => value.NodeId), Is.EquivalentTo(fixture.Machines
                .Select(value => value.NodeId).Concat(fixture.Systems.Select(value => value.NodeId))
                .Concat(fixture.Peripherals.Select(value => value.NodeId))));
            Assert.That((await gms.DiscoverAsync(ct).ConfigureAwait(false)).ToArray()!.Select(value => value.NodeId),
                Is.EquivalentTo(fixture.Systems.Select(value => value.NodeId)));
            Assert.That((await cranes.DiscoverAsync(ct).ConfigureAwait(false)).ToArray()!.Select(value => value.NodeId),
                Is.EquivalentTo(fixture.Cranes.Select(value => value.NodeId)));
            var inspectionIds = new List<NodeId>();
            await foreach (VisionNodeEntry entry in vision.EnumerateSystemsAsync(cancellationToken: ct)
                .ConfigureAwait(false))
            {
                inspectionIds.Add(entry.NodeId);
            }
            Assert.That(inspectionIds, Is.EquivalentTo(fixture.Inspections.Select(value => value.State.NodeId)));
            for (int i = 0; i < 2; i++)
            {
                int number = i + 1;
                MachineToolReadSnapshot machine = await tools.ReadSnapshotAsync(fixture.Machines[i].NodeId, ct)
                    .ConfigureAwait(false);
                AssertValue(machine.ActiveProgramName, "CELL" + number + ".NC");
                AssertValue(machine.FeedOverride, 80.0 + number);
                MachineToolEquipmentReadSnapshot equipment = await tools
                    .ReadEquipmentAsync(fixture.Machines[i].NodeId, cancellationToken: ct).ConfigureAwait(false);
                Assert.That(equipment.Tools.Count, Is.EqualTo(1));
                AssertValue(equipment.Tools[0].Identifier, "Cutter" + number);
                Assert.That(await tools.GetMachineProxy(fixture.Machines[i].NodeId)
                    .GetProductionAsync(fixture.Telemetry, ct).ConfigureAwait(false), Is.Not.Null);

                GmsSystemReadSnapshot system = await gms.ReadSnapshotAsync(fixture.Systems[i].NodeId, ct)
                    .ConfigureAwait(false);
                Assert.That(fixture.Systems[i].State, Is.InstanceOf<MachineToolState>());
                Assert.That(fixture.Systems[i].State.Identification, Is.TypeOf<GMSIdentificationState>());
                Assert.That(fixture.Systems[i].State.ResultManagement, Is.TypeOf<GMSResultManagementState>());
                Assert.That(system.Identification.SerialNumber.Value.WrappedValue.TryGetValue(out string serial),
                    Is.True);
                Assert.That(serial, Is.EqualTo("Gms" + number));
                Assert.That(system.Identification.SerialNumber.Value.StatusCode, Is.EqualTo(StatusCodes.Good));
                GmsEquipmentReadSnapshot sensors = await gms.ReadEquipmentAsync(system.NodeId, ct)
                    .ConfigureAwait(false);
                Assert.That(sensors.Sensors.Count, Is.EqualTo(1));
                Assert.That(sensors.Sensors[0].Inherited.TypeDefinitionId,
                    Is.EqualTo(ExpandedNodeId.ToNodeId(Opc.Ua.GMS.ObjectTypeIds.SensorType, session.NamespaceUris)));
                Assert.That(sensors.Sensors[0].Inherited.Life.Count, Is.EqualTo(1));
                AssertValue(sensors.Sensors[0].Inherited.Life[0].Value, 10.0 + number);
                Assert.That(system.ToolMonitoring!.ActiveTool.Value.WrappedValue.TryGetValue(
                    out ArrayOf<NodeId> active), Is.True);
                Assert.That(active.ToArray(), Is.EqualTo(new[] { sensors.Sensors[0].Inherited.NodeId }));
                Assert.That(active.ToArray(), Does.Not.Contain(fixture.Machines[i].NodeId));
                GmsProductionReadSnapshot production = await gms.ReadProductionAsync(system.NodeId, ct)
                    .ConfigureAwait(false);
                Assert.That(production.Jobs.Count, Is.EqualTo(1));
                Assert.That(production.Jobs[0].Inherited.TypeDefinitionId,
                    Is.EqualTo(ExpandedNodeId.ToNodeId(Opc.Ua.GMS.ObjectTypeIds.GMSJobType, session.NamespaceUris)));
                AssertValue(production.Jobs[0].BatchIdentifier.Value, "InspectionBatch" + number);
                var result = await gms.GetResults(system.NodeId).GetLatestAsync(ct).ConfigureAwait(false);
                Assert.That(result.ResultMetaData!.ResultId, Is.EqualTo("Result" + number));
                Assert.That(result.ResultContent[0].TryGetValue(out double measurement), Is.True);
                Assert.That(measurement, Is.EqualTo(100.0 + number));
                Assert.That((await gms.GetResults(system.NodeId).DownloadAsync("Result" + number, ct)
                    .ConfigureAwait(false)).ToArray(), Is.EqualTo(new byte[] { (byte)number, 7, 9 }));

                WmtpPeripheralClient peripheral = tools.GetWirelessPeripheral(fixture.Peripherals[i].NodeId);
                WmtpPeripheralSnapshot peripheralState = await peripheral.ReadSnapshotAsync(ct).ConfigureAwait(false);
                AssertValue(peripheralState.Battery, 70.0 + number);
                ArrayOf<WmtpMeasurementReading> readings = await peripheral.ReadMeasurementsAsync(
                    cancellationToken: ct).ConfigureAwait(false);
                Assert.That(readings.Count, Is.EqualTo(1));
                AssertValue(readings[0].Sample, 20.0 + number);
                WmtpRecordClient records = await peripheral.GetWorkRecordsAsync(ct).ConfigureAwait(false);
                ArrayOf<WMTPOutputDataType> first = await records.ReadAsync(new WmtpRecordQuery(),
                    cancellationToken: ct).ConfigureAwait(false);
                ArrayOf<WMTPOutputDataType> second = await records.ReadAsync(new WmtpRecordQuery(),
                    cancellationToken: ct).ConfigureAwait(false);
                Assert.That(first.Count, Is.EqualTo(1));
                Assert.That(second[0].ActualValue, Is.EqualTo(200.0 + number));
                Assert.That(second[0].Index, Is.EqualTo(first[0].Index));
                Assert.That(await fixture.Records[i].CountAsync(new WmtpRecordQuery(), ct).ConfigureAwait(false),
                    Is.EqualTo(1));

                CraneSystemSnapshot crane = await cranes.ReadSystemAsync(fixture.Cranes[i].NodeId, ct)
                    .ConfigureAwait(false);
                Assert.That(fixture.Cranes[i].State, Is.TypeOf<CraneMotionDeviceSystemState>());
                CraneElementSnapshot axis = crane.Elements.ToArray()!.Single(value =>
                    value.Entry.Kind == CraneElementKind.Axis);
                AssertValue(axis.Signals.ToArray()!.Single(value => value.Signal == CraneSignal.LoadMass).Value,
                    100.0 + number);
                AssertValue(axis.Signals.ToArray()!.Single(value => value.Signal == CraneSignal.ActualPosition).Value,
                    1.0 + number);
                Assert.That(await cranes.Axis(axis.Entry.NodeId).GetParameterSetAsync(fixture.Telemetry, ct)
                    .ConfigureAwait(false), Is.Not.Null);
                CraneElementSnapshot protection = crane.Elements.ToArray()!.Single(value =>
                    value.Entry.Kind == CraneElementKind.ProtectiveFunction);
                AssertValue(protection.Signals.ToArray()!.Single(value => value.Signal == CraneSignal.Active).Value,
                    number == 2);
                Assert.That(protection.Relationships.ToArray()!.Any(value => value.Target == axis.Entry.NodeId),
                    Is.True);
                Assert.That(fixture.CraneProviders[i].AcceptedRequests, Is.Empty);

                ArrayOf<NodeId> sameEntity = await vision.GetSameEntityNodesAsync(fixture.Assets[i].State.NodeId, ct)
                    .ConfigureAwait(false);
                Assert.That(sameEntity.ToArray(), Is.EqualTo(new[] { fixture.Inspections[i].State.NodeId }));
                MachineVisionSystemClient inspection = vision.System(fixture.Inspections[i].State.NodeId);
                ResultManagementTypeClient resultProxy = await inspection.ResultsAsync(ct).ConfigureAwait(false);
                (uint handle, Opc.Ua.Vision.MachineVision.ResultDataType result, int error) inspected =
                    await resultProxy.GetResultByIdAsync(new ResultIdDataType { Id = "Inspection" + number }, 0, ct)
                        .ConfigureAwait(false);
                Assert.That(inspected.error, Is.Zero);
                Assert.That(inspected.result.ResultState, Is.EqualTo(number));
                Assert.That(inspected.result.ResultContent[0].TryGetValue(out int inspectionValue), Is.True);
                Assert.That(inspectionValue, Is.EqualTo(number));
                Assert.That(await resultProxy.ReleaseResultHandleAsync(inspected.handle, ct).ConfigureAwait(false),
                    Is.Zero);
                Assert.That(fixture.Inspections[i].ControlState.CurrentState!.Value.Text, Is.EqualTo("Preoperational"));
                fixture.MachineProviders[i].Verify(value => value.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
                fixture.GmsProviders[i].Verify(value => value.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
                fixture.VisionProviders[i].VerifyNoOtherCalls();
            }
            await AssertConformanceUnionAsync(fixture, session, ct).ConfigureAwait(false);
            foreach (Mock<IMachineToolProvider> provider in fixture.MachineProviders)
            {
                provider.Verify(value => value.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            foreach (Mock<IGmsProvider> provider in fixture.GmsProviders)
            {
                provider.Verify(value => value.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        /// <summary>
        /// Both public hosting paths support only actual wireless instances, with no fictional MachineTool owner.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task WirelessOnlyHostingDoesNotPublishMachineToolAsync(bool standalone)
        {
            await using var fixture = new CompanionSuiteFixture();
            await fixture.StartAsync(wirelessOnly: true, standalone: standalone).ConfigureAwait(false);
            ClientSession session = await fixture.ConnectAsync("WirelessOnly").ConfigureAwait(false);
            await using var client = new MachineToolClient(session, fixture.Telemetry);
            Assert.That(client.AvailableParts, Is.EqualTo(MachineToolParts.Wmtp));
            Assert.That(session.NamespaceUris.GetIndex(Opc.Ua.MachineTool.Namespaces.MachineTool), Is.EqualTo(-1));
            Assert.That(session.NamespaceUris.GetIndex(GmsModel.NamespaceUri), Is.EqualTo(-1));
            Assert.That((await client.DiscoverAsync().ConfigureAwait(false)).ToArray()!.Select(value => value.Kind),
                Is.EqualTo(new[]
                {
                    MachineToolDeviceKind.WirelessPeripheral, MachineToolDeviceKind.WirelessPeripheral
                }));
            WmtpPeripheralClient peripheral = client.GetWirelessPeripheral(fixture.Peripherals[0].NodeId);
            AssertValue((await peripheral.ReadSnapshotAsync().ConfigureAwait(false)).Battery, 71.0);
            Assert.That(fixture.Provider.GetServices<OpcUaServerNodeManagerRegistration>().Count(), Is.EqualTo(1));
        }

        /// <summary>
        /// A standalone convenience call cannot silently introduce another owner after a shared family is selected.
        /// </summary>
        [TestCase("MachineTool")]
        [TestCase("GMS")]
        [TestCase("CranesHoists")]
        [TestCase("Vision")]
        public void DuplicateStandaloneOwnersAreRejected(string family)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { })
                .AddMachinery(options => options.Parts = CompanionSuiteFixture.RequiredParts)
                .AddMachineToolToMachinery(options => options.Parts = MachineToolParts.All)
                .AddGmsToMachinery().AddCranesHoistsToMachinery().AddVisionToMachinery();
            Assert.Throws<InvalidOperationException>(() =>
            {
                switch (family)
                {
                    case "MachineTool":
                        builder.AddMachineTool();
                        break;
                    case "GMS":
                        builder.AddGms();
                        break;
                    case "CranesHoists":
                        builder.AddCranesHoists();
                        break;
                    default:
                        builder.AddVision(options => options.MachineVisionParts = MachineVisionParts.All);
                        break;
                }
            });
            Assert.That(services.Count(value => value.ServiceType == typeof(DiAddressSpaceOwnership)), Is.EqualTo(1));
            Assert.That(services.Count(value => value.ServiceType == typeof(OpcUaServerNodeManagerRegistration)),
                Is.EqualTo(1));
        }

        /// <summary>
        /// The independent control/perception namespaces cannot be owned by both standalone and shared managers.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void DuplicateVisionNamespaceOwnersAreRejectedBeforeChangingRegistrations(bool standaloneFirst)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });
            MachineVisionParts expected;
            if (standaloneFirst)
            {
                expected = MachineVisionParts.Control;
                builder.AddVision(options => options.MachineVisionParts = expected)
                    .AddMachinery(options => options.Parts = CompanionSuiteFixture.RequiredParts);
            }
            else
            {
                expected = MachineVisionParts.All;
                builder.AddMachinery(options => options.Parts = CompanionSuiteFixture.RequiredParts)
                    .AddVisionToMachinery();
            }
            int registrations = services.Count(value =>
                value.ServiceType == typeof(OpcUaServerNodeManagerRegistration));
            Assert.Throws<InvalidOperationException>(() =>
            {
                if (standaloneFirst)
                {
                    builder.AddVisionToMachinery();
                }
                else
                {
                    builder.AddVision(options => options.MachineVisionParts = MachineVisionParts.Control);
                }
            });
            Assert.That(services.Count(value => value.ServiceType == typeof(OpcUaServerNodeManagerRegistration)),
                Is.EqualTo(registrations));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IOptions<VisionServerOptions>>().Value.MachineVisionParts,
                Is.EqualTo(expected));
        }

        /// <summary>
        /// Requires an exact typed value and original acquisition metadata, not only presence.
        /// </summary>
        private static void AssertValue(in DataValue value, double expected)
        {
            Assert.That(value.WrappedValue.TryGetValue(out double actual), Is.True, value.WrappedValue.ToString());
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.SourceTimestamp, Is.EqualTo(CompanionSuiteData.Acquired));
        }

        /// <summary>
        /// Preserves string acquisitions without boxing a Variant or discarding their quality.
        /// </summary>
        private static void AssertValue(in DataValue value, string expected)
        {
            Assert.That(value.WrappedValue.TryGetValue(out string actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.SourceTimestamp, Is.EqualTo(CompanionSuiteData.Acquired));
        }

        /// <summary>
        /// Requires an actual Boolean acquisition with its original quality and source time.
        /// </summary>
        private static void AssertValue(in DataValue value, bool expected)
        {
            Assert.That(value.WrappedValue.TryGetValue(out bool actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.SourceTimestamp, Is.EqualTo(CompanionSuiteData.Acquired));
        }

        /// <summary>
        /// Collects actual application roots; children must not reuse an identifier or another root's state.
        /// </summary>
        private static IEnumerable<BaseInstanceState> Roots(CompanionSuiteFixture fixture)
        {
            return fixture.Machines.Select(value => (BaseInstanceState)value.State)
                .Concat(fixture.Systems.Select(value => value.State))
                .Concat(fixture.Cranes.Select(value => value.State))
                .Concat(fixture.Peripherals.Select(value => value.State))
                .Concat(fixture.Inspections.Select(value => value.State))
                .Concat(fixture.Assets.Select(value => value.State));
        }

        /// <summary>
        /// Rejects colliding native descendants instead of concealing them behind dictionary replacement.
        /// </summary>
        private static void IndexTree(ISystemContext context, NodeState node, Dictionary<NodeId, NodeState> nodes)
        {
            Assert.That(node.NodeId.IsNull, Is.False, node.BrowseName.ToString());
            Assert.That(nodes.TryAdd(node.NodeId, node), Is.True, node.NodeId.ToString());
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                IndexTree(context, child, nodes);
            }
        }

        /// <summary>
        /// Forces continuation-point handling while retaining all actual hierarchical references.
        /// </summary>
        private static async Task<List<ReferenceDescription>> BrowseAsync(
            ClientSession session, NodeId root, CancellationToken ct)
        {
            BrowseResponse response = await session.BrowseAsync(null, null, 2,
                [new BrowseDescription
                {
                    NodeId = root,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    ResultMask = (uint)BrowseResultMask.All
                }], ct).ConfigureAwait(false);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            BrowseResult page = response.Results[0];
            var references = new List<ReferenceDescription>();
            for (int pages = 0; ; pages++)
            {
                Assert.That(page.StatusCode, Is.EqualTo(StatusCodes.Good));
                references.AddRange(page.References.ToArray()!);
                if (page.ContinuationPoint.IsNull || page.ContinuationPoint.IsEmpty)
                {
                    return references;
                }
                Assert.That(pages, Is.LessThan(128), "The mixed workcell browse must be bounded.");
                BrowseNextResponse next = await session.BrowseNextAsync(null, false, [page.ContinuationPoint], ct)
                    .ConfigureAwait(false);
                Assert.That(next.Results.Count, Is.EqualTo(1));
                page = next.Results[0];
            }
        }

        /// <summary>
        /// Compares the shared catalog with the exact union of committed, independently validated family runtimes.
        /// </summary>
        private static async Task AssertConformanceUnionAsync(
            CompanionSuiteFixture fixture, ClientSession session, CancellationToken ct)
        {
            IConformanceContributor[] contributors = fixture.Machines.Cast<IConformanceContributor>()
                .Concat(fixture.Systems).Concat(fixture.Cranes).Concat(fixture.Peripherals)
                .Concat(fixture.Inspections).Concat(fixture.Assets).ToArray();
            var namespaces = new HashSet<ushort>(new[]
            {
                Opc.Ua.MachineTool.Namespaces.MachineTool, Opc.Ua.MachineTool.WMTP.Namespaces.WMTP,
                GmsModel.NamespaceUri, CranesHoistsModel.NamespaceUri,
                MachineVisionModels.ControlNamespaceUri, MachineVisionModels.AssetNamespaceUri
            }.Select(uri => (ushort)session.NamespaceUris.GetIndex(uri)));
            var expected = new HashSet<QualifiedName>(contributors
                .SelectMany(value => value.ConformanceUnits.ToArray()!)
                .Where(value => namespaces.Contains(value.NamespaceIndex)));
            var expectedProfiles = new HashSet<string>(contributors
                .SelectMany(value => value.ServerProfiles.ToArray()!), StringComparer.Ordinal);
            await using var inherited = new MachineToolClient(session, fixture.Telemetry);
            foreach (var system in fixture.Systems)
            {
                MachineToolCapabilityReadSnapshot declaration = await inherited.GetCapabilitiesAsync(system.NodeId, ct)
                    .ConfigureAwait(false);
                Assert.That(declaration.DeclarationAvailable, Is.True);
                Assert.That(declaration.Capabilities, Is.Not.Null);
                expected.UnionWith(declaration.Capabilities!.ConformanceUnits.ToArray()!);
                expectedProfiles.UnionWith(declaration.Capabilities.ServerProfiles.ToArray()!);
            }
            DataValue read = await session.ReadValueAsync(
                Opc.Ua.VariableIds.Server_ServerCapabilities_ConformanceUnits, ct)
                .ConfigureAwait(false);
            Assert.That(read.WrappedValue.TryGetValue(out ArrayOf<QualifiedName> units), Is.True);
            Assert.That(units.ToArray()!.Where(value => namespaces.Contains(value.NamespaceIndex)),
                Is.EquivalentTo(expected));
            Assert.That(units.ToArray()!.Distinct().Count(), Is.EqualTo(units.Count));
            foreach ((string uri, string name) in new[]
            {
                (Opc.Ua.MachineTool.Namespaces.MachineTool, "MachineTool MachineToolType Mandatory Nodes"),
                (Opc.Ua.MachineTool.WMTP.Namespaces.WMTP, "WMTP Basic"),
                (GmsModel.NamespaceUri, "GMS QualifiedToolLife"),
                (GmsModel.NamespaceUri, "GMS GMSJobType"),
                (CranesHoistsModel.NamespaceUri, "Cranes Base Info CraneAxisType"),
                (MachineVisionModels.ControlNamespaceUri, "Automatic Mode"),
                (MachineVisionModels.AssetNamespaceUri, "VSA Basic")
            })
            {
                Assert.That(units.ToArray(), Does.Contain(new QualifiedName(
                    name, (ushort)session.NamespaceUris.GetIndex(uri))));
            }
            Assert.That(units.ToArray()!.Any(value => value.Name == "GMS GMS_ProductionStatisticsType"), Is.False);
            DataValue profileRead = await session.ReadValueAsync(
                Opc.Ua.VariableIds.Server_ServerCapabilities_ServerProfileArray, ct).ConfigureAwait(false);
            Assert.That(profileRead.WrappedValue.TryGetValue(out ArrayOf<string> profiles), Is.True);
            Assert.That(profiles.ToArray(),
                Does.Contain("http://opcfoundation.org/UA-Profile/CranesHoists/Server/Basic"));
            Assert.That(profiles.ToArray(),
                Does.Contain("http://opcfoundation.org/UA-Profile/WMTP/Server/WMTPBasicServerFacet"));
            foreach (string profile in expectedProfiles)
            {
                Assert.That(profiles.ToArray(), Does.Contain(profile));
            }
            Assert.That(profiles.ToArray()!.Where(IsFamilyProfile),
                Is.EquivalentTo(expectedProfiles.Where(IsFamilyProfile)));
            Assert.That(profiles.ToArray()!.Any(value => value.Contains("/GMS/", StringComparison.Ordinal) &&
                value.EndsWith("/Production", StringComparison.Ordinal)), Is.False);
        }

        /// <summary>
        /// Separates family profile declarations from the standard server and shared-foundation profile set.
        /// </summary>
        private static bool IsFamilyProfile(string profile)
        {
            return profile.StartsWith("http://opcfoundation.org/UA-Profile/MachineTool/", StringComparison.Ordinal) ||
                profile.StartsWith("http://opcfoundation.org/UA-Profile/WMTP/", StringComparison.Ordinal) ||
                profile.StartsWith("http://opcfoundation.org/UA-Profile/GMS/", StringComparison.Ordinal) ||
                profile.StartsWith("http://opcfoundation.org/UA-Profile/CranesHoists/", StringComparison.Ordinal) ||
                profile.StartsWith("http://opcfoundation.org/UA-Profile/MachineVision/", StringComparison.Ordinal);
        }
    }
}
