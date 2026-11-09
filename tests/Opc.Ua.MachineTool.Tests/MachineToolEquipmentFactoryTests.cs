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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.GMS;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies actual GMS subtype adoption, bounded client continuation ownership and independent factory resources.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class MachineToolEquipmentFactoryTests
    {
        /// <summary>
        /// A true GMS MultiSensor/Sensor and Double Qualified declaration survive generic equipment publication.
        /// </summary>
        [Test]
        public async Task ActualGmsSensorAndQualifiedLifeKeepTheirGeneratedIdentityAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture([new MachineToolEquipmentModelProvider()]);
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolTool basis = MachineToolEquipmentTests.Example().Equipment!.Tools[0];
            MachineToolToolLife qualified = basis.Life[0] with
            {
                Name = "Qualified",
                NamespaceUri = GmsModel.NamespaceUri,
                Indication = ToolLifeIndication.Time,
                EngineeringUnits = MachineToolEquipmentTests.Unit(0x485552, "h", "hour"),
                IsCountingUp = false,
                Value = MachineToolEquipmentTests.Sample(Variant.From(2.5), basis.Sample),
                StartValue = MachineToolEquipmentTests.Sample(Variant.From(8.0), basis.Sample),
                WarningValue = MachineToolEquipmentTests.Sample(Variant.From(1.0), basis.Sample),
                LimitValue = MachineToolEquipmentTests.Sample(Variant.From(0.0), basis.Sample)
            };
            MachineToolTool sensorObservation = basis with
            {
                Key = "Probe",
                Location = null,
                Life = [qualified]
            };
            var initial = new MachineToolSnapshot
            {
                Equipment = new()
                {
                    Tools =
                    [
                        new MachineToolTool
                        {
                            Key = "Head", Kind = MachineToolToolKind.MultiTool,
                            Tools = [sensorObservation], Sample = basis.Sample
                        }
                    ]
                }
            };
            var provider = new InMemoryMachineToolProvider(initial);
            SensorState? actualSensor = null;
            MultiSensorState? actualHead = null;
            ToolLifeState? actualQualified = null;
            await using MachineToolRuntime<GMSState> runtime = await fixture.Context.AddMachineTool("GmsEquipment",
                static (context, parent, name) => context.CreateInstanceOfGMSType(parent, name))
                .WithProvider(provider)
                .WithEquipment(new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools | MachineToolFeatures.ToolLife,
                    CreateMultiToolAsync = (context, parent, name, _, _) =>
                    {
                        actualHead = context.CreateInstanceOfMultiSensorType(parent, name);
                        actualHead.AddAxes(context);
                        actualHead.Axes!.Value = ["A", "B"];
                        return new ValueTask<MachineToolEquipmentNode<MultiToolState>>(
                            new MachineToolEquipmentNode<MultiToolState>(actualHead));
                    },
                    CreateToolAsync = (context, parent, name, _, _) =>
                    {
                        actualSensor = context.CreateInstanceOfSensorType(parent, name);
                        actualSensor.AddAlignment(context).AddIsQualifiedStatus(context);
                        actualSensor.Alignment!.Value = ToolAlignmentState.Indexed;
                        actualSensor.IsQualifiedStatus!.Value = ToolIsQualifiedStatus.Qualified;
                        return new ValueTask<MachineToolEquipmentNode<ToolState>>(
                            new MachineToolEquipmentNode<ToolState>(actualSensor));
                    }
                }).WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("GMS equipment tests");
                    identity.SerialNumber = "GMS-Equipment";
                    identity.ProductInstanceUri = "urn:equipment-tests:gms";
                }).BuildAsync().ConfigureAwait(false);
            Assert.That(runtime.Equipment.State, Is.TypeOf<GMSEquipmentState>());
            Assert.That(actualSensor!.Parent, Is.SameAs(actualHead));
            actualQualified = actualSensor.ToolLife!.FindChild(fixture.Context.Context,
                new QualifiedName("Qualified",
                    (ushort)fixture.Context.Context.NamespaceUris.GetIndex(GmsModel.NamespaceUri))) as ToolLifeState;
            Assert.That(actualQualified, Is.InstanceOf<ToolLifeState<double>>());
            NodeId sensorId = actualSensor.NodeId;
            NodeId qualifiedId = actualQualified!.NodeId;
            Assert.That(actualQualified.TypeDefinitionId,
                Is.EqualTo(ExpandedNodeId.ToNodeId(
                    VariableTypeIds.ToolLifeType, fixture.Context.Context.NamespaceUris)));
            Assert.That(actualQualified.BrowseName,
                Is.EqualTo(new QualifiedName("Qualified",
                    (ushort)fixture.Context.Context.NamespaceUris.GetIndex(GmsModel.NamespaceUri))));
            Assert.That(actualQualified.IsCountingUp!.Value, Is.False,
                "The provider's qualification countdown overrides the retained declaration's true default.");
            await using var client = new ClientFixture(NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(fixture.Root, "GmsEquipmentClient").ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using var facade = new MachineToolClient(session, NUnitTelemetryContext.Create());
            MachineToolEquipmentReadSnapshot inventory = await facade.ReadEquipmentAsync(runtime.NodeId)
                .ConfigureAwait(false);
            MachineToolToolReadSnapshot received = inventory.Tools[0].Tools[0];
            Assert.That(received.TypeDefinitionId,
                Is.EqualTo(NodeId.Create(1007u, GmsModel.NamespaceUri, session.NamespaceUris)));
            Assert.That(received.NodeId, Is.EqualTo(sensorId));
            Assert.That(received.Life[0].NodeId, Is.EqualTo(qualifiedId));
            Assert.That(received.Life[0].DataType, Is.EqualTo(Opc.Ua.DataTypeIds.Double));
            Assert.That(received.Life[0].Value.WrappedValue.TryGetValue(out double remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(2.5));
            Assert.That(received.Life[0].Value.StatusCode, Is.EqualTo(basis.Sample.StatusCode));
            await provider.SetSnapshotAsync(initial with
            {
                Equipment = new()
                {
                    Tools =
                    [
                        initial.Equipment!.Tools[0] with
                        {
                            Tools =
                            [
                                sensorObservation with
                                {
                                    Life =
                                    [
                                        qualified with
                                        {
                                            Value = MachineToolEquipmentTests.Sample(
                                                Variant.From(1.25), basis.Sample)
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            }).ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            Assert.That(actualSensor.NodeId, Is.EqualTo(sensorId));
            Assert.That(actualQualified.NodeId, Is.EqualTo(qualifiedId));
            Assert.That(actualSensor.Alignment!.Value, Is.EqualTo(ToolAlignmentState.Indexed));
            Assert.That(actualHead!.Axes!.Value, Is.EqualTo((ArrayOf<string>)["A", "B"]));
            DataValue changed = await session.ReadValueAsync(qualifiedId).ConfigureAwait(false);
            Assert.That(changed.WrappedValue.TryGetValue(out remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(1.25));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// A precreated generated sensor is adopted with its original NodeId, parent and children, not reconstructed.
        /// </summary>
        [Test]
        public async Task PrecreatedGmsEquipmentIsAdoptedWithoutReplacingGeneratedNodesAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture([new MachineToolEquipmentModelProvider()]);
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolSnapshot snapshot = MachineToolEquipmentTests.Example();
            SensorState? sensor = null;
            ToolListState? list = null;
            NodeId original = NodeId.Null;
            await using MachineToolRuntime<GMSState> runtime = await fixture.Context.AddMachineTool("Precreated",
                (context, parent, name) =>
                {
                    GMSState machine = context.CreateInstanceOfGMSType(parent, name);
                    machine.Equipment!.AddTools(context);
                    list = machine.Equipment.Tools!;
                    sensor = context.CreateInstanceOfSensorType(list,
                        new QualifiedName("T1", fixture.Context.InstanceNamespaceIndex));
                    sensor.AddAlignment(context);
                    sensor.Alignment!.Value = ToolAlignmentState.Indexed;
                    sensor.Class!.StatusCode = StatusCodes.BadWaitingForInitialData;
                    original = sensor.NodeId;
                    list.AddChild(sensor);
                    return machine;
                })
                .WithProvider(new InMemoryMachineToolProvider(snapshot))
                .WithEquipment(new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools | MachineToolFeatures.ToolLife
                })
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Precreated GMS");
                    identity.SerialNumber = "Precreated";
                    identity.ProductInstanceUri = "urn:equipment-tests:precreated";
                }).BuildAsync().ConfigureAwait(false);
            Assert.That(runtime.Equipment.State.Tools, Is.SameAs(list));
            Assert.That(fixture.Context.Manager.FindPredefinedNode(original), Is.SameAs(sensor));
            Assert.That(sensor!.NodeId, Is.EqualTo(original));
            Assert.That(sensor.Parent, Is.SameAs(list));
            Assert.That(sensor.Alignment!.Value, Is.EqualTo(ToolAlignmentState.Indexed));
            await using var client = new ClientFixture(NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(fixture.Root, "PrecreatedClient").ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using var facade = new MachineToolClient(session, NUnitTelemetryContext.Create());
            MachineToolEquipmentReadSnapshot read = await facade.ReadEquipmentAsync(runtime.NodeId)
                .ConfigureAwait(false);
            Assert.That(read.Tools.Count, Is.EqualTo(1));
            Assert.That(read.Tools[0].NodeId, Is.EqualTo(original));
            Assert.That(read.Tools[0].TypeDefinitionId,
                Is.EqualTo(NodeId.Create(1007u, GmsModel.NamespaceUri, session.NamespaceUris)));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Bounded early exits release continuation points, and independent machines never share inventories.
        /// </summary>
        [Test]
        public async Task ClientBoundsCancellationAndContinuationCleanupKeepMachinesIsolatedAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var tools = new MachineToolTool[65];
            for (int i = 0; i < tools.Length; i++)
            {
                tools[i] = MachineToolEquipmentLifetimeTests.Tool("Tool" + i, (uint)i);
            }
            var provider = new InMemoryMachineToolProvider(new MachineToolSnapshot
            {
                Equipment = new() { Tools = tools.ToArrayOf() }
            });
            await using MachineToolRuntime<MachineToolState> first =
                await MachineToolEquipmentLifetimeTests.Builder(fixture, provider, "Many")
                    .WithFeatures(MachineToolFeatures.Tools).BuildAsync().ConfigureAwait(false);
            await using MachineToolRuntime<MachineToolState> second =
                await MachineToolEquipmentLifetimeTests.Builder(
                    fixture, new InMemoryMachineToolProvider(new MachineToolSnapshot()), "Mandatory")
                    .BuildAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var client = new ClientFixture(telemetry);
            await client.LoadClientConfigurationAsync(fixture.Root, "BoundedEquipmentClient").ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using var facade = new MachineToolClient(session, telemetry);
            for (int i = 0; i < 12; i++)
            {
                ServiceResultException limit = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await facade.ReadEquipmentAsync(first.NodeId, maximumTools: 1).ConfigureAwait(false)))!;
                Assert.That(limit.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            }
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await facade.ReadEquipmentAsync(first.NodeId, maximumTools: 0).ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await facade.ReadEquipmentAsync(first.NodeId, maximumLifeEntries: 65537).ConfigureAwait(false));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.That(async () => await facade.ReadEquipmentAsync(
                first.NodeId, cancellationToken: cancelled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            MachineToolEquipmentReadSnapshot all = await facade.ReadEquipmentAsync(first.NodeId).ConfigureAwait(false);
            Assert.That(all.Tools.Count, Is.EqualTo(65),
                "The inventory crosses the shared browser's 64-reference page.");
            Assert.That(all.Tools[64].BrowseName.Name, Is.EqualTo("Tool64"));
            MachineToolEquipmentReadSnapshot absent = await facade.ReadEquipmentAsync(second.NodeId)
                .ConfigureAwait(false);
            Assert.That(absent.ToolsNodeId.IsNull, Is.True);
            Assert.That(absent.Tools, Is.Empty);
            Assert.That(absent.NodeVersion.IsNull, Is.True);
            await facade.DisposeAsync().ConfigureAwait(false);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await facade.ReadEquipmentAsync(first.NodeId).ConfigureAwait(false));
            Assert.That(session.Connected, Is.True);
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// A factory cannot reuse an existing live node or take ownership of its resource a second time.
        /// </summary>
        [Test]
        public async Task FactoryCannotStealExistingToolOrItsResourceAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            ToolState? existing = null;
            var resource = new EquipmentResource();
            var initial = new MachineToolSnapshot
            {
                Equipment = new() { Tools = [MachineToolEquipmentLifetimeTests.Tool("First", 1)] }
            };
            var provider = new InMemoryMachineToolProvider(initial);
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolEquipmentLifetimeTests.Builder(fixture, provider, "Ownership")
                    .WithEquipment(new MachineToolEquipmentOptions
                    {
                        Features = MachineToolFeatures.Tools | MachineToolFeatures.DynamicTools,
                        CreateToolAsync = (context, parent, name, _, _) =>
                        {
                            existing ??= context.CreateInstanceOfToolType(parent, name);
                            return new ValueTask<MachineToolEquipmentNode<ToolState>>(
                                new MachineToolEquipmentNode<ToolState>(existing, resource));
                        }
                    }).BuildAsync().ConfigureAwait(false);
            await provider.SetSnapshotAsync(initial with
            {
                Equipment = new()
                {
                    Tools = [initial.Equipment!.Tools[0], MachineToolEquipmentLifetimeTests.Tool("Other", 2)]
                }
            }).ConfigureAwait(false);
            await Assert.ThrowsAsync<ServiceResultException>(async () => await runtime.RefreshAsync().ConfigureAwait(false));
            Assert.That(fixture.Context.Manager.FindPredefinedNode(existing!.NodeId), Is.SameAs(existing));
            Assert.That(resource.Disposals, Is.Zero);
            Assert.That(resource.WorkCompleted, Is.False);
            Assert.That(runtime.Equipment.Observation!.Revision, Is.Zero);
        }

        /// <summary>
        /// Rejecting a foreign live node never disposes a resource belonging to the other machine.
        /// </summary>
        [Test]
        public async Task FactoryCannotDisposeAnotherInstancesLiveToolResourceAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            ToolState? foreign = null;
            var resource = new EquipmentResource();
            MachineToolTool tool = MachineToolEquipmentLifetimeTests.Tool("Tool", 1);
            var snapshot = new MachineToolSnapshot { Equipment = new() { Tools = [tool] } };
            await using MachineToolRuntime<MachineToolState> owner =
                await MachineToolEquipmentLifetimeTests.Builder(
                    fixture, new InMemoryMachineToolProvider(snapshot), "ResourceOwner")
                    .WithEquipment(new MachineToolEquipmentOptions
                    {
                        Features = MachineToolFeatures.Tools,
                        CreateToolAsync = (context, parent, name, _, _) =>
                        {
                            foreign = context.CreateInstanceOfToolType(parent, name);
                            return new ValueTask<MachineToolEquipmentNode<ToolState>>(
                                new MachineToolEquipmentNode<ToolState>(foreign, resource));
                        }
                    }).BuildAsync().ConfigureAwait(false);
            var builder = MachineToolEquipmentLifetimeTests.Builder(
                fixture, new InMemoryMachineToolProvider(snapshot), "WrongOwner")
                .WithEquipment(new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools,
                    CreateToolAsync = (_, _, _, _, _) => new ValueTask<MachineToolEquipmentNode<ToolState>>(
                        new MachineToolEquipmentNode<ToolState>(foreign!, resource))
                });
            await Assert.ThrowsAsync<ServiceResultException>(async () => await builder.BuildAsync().ConfigureAwait(false));
            Assert.That(resource.Disposals, Is.Zero);
            Assert.That(resource.WorkCompleted, Is.False);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(foreign!.NodeId), Is.SameAs(foreign));
            Assert.That(owner.Equipment.State.Tools!.FindChild(fixture.Context.Context,
                new QualifiedName("Tool", fixture.Context.InstanceNamespaceIndex)), Is.SameAs(foreign));
        }

        /// <summary>
        /// A failing resource does not prevent independently owned callbacks from being canceled and joined.
        /// </summary>
        [Test]
        public async Task EveryOwnedCallbackIsDrainedWhenOneDisposalFailsAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var resources = new List<EquipmentResource>();
            MachineToolState machine = fixture.Context.Context.CreateInstanceOfMachineToolType(
                fixture.Context.MachinesFolder, new QualifiedName("Failures", fixture.Context.InstanceNamespaceIndex));
            var controller = new MachineToolEquipmentController(fixture.Context, machine,
                new MachineToolEquipmentOptions
                {
                    Features = MachineToolFeatures.Tools,
                    CreateToolAsync = (context, parent, name, _, _) =>
                    {
                        var resource = new EquipmentResource(failDisposal: name.Name == "Last");
                        resources.Add(resource);
                        return new ValueTask<MachineToolEquipmentNode<ToolState>>(
                            new MachineToolEquipmentNode<ToolState>(
                                context.CreateInstanceOfToolType(parent, name), resource));
                    }
                });
            await controller.InitializeAsync(new MachineToolSnapshot
            {
                Equipment = new()
                {
                    Tools =
                    [
                        MachineToolEquipmentLifetimeTests.Tool("First", 1),
                        MachineToolEquipmentLifetimeTests.Tool("Last", 2)
                    ]
                }
            }).ConfigureAwait(false);
            await Assert.ThrowsAsync<AggregateException>(async () => await controller.DisposeAsync().ConfigureAwait(false));
            Assert.That(resources, Has.Count.EqualTo(2));
            foreach (EquipmentResource resource in resources)
            {
                Assert.That(resource.Disposals, Is.EqualTo(1));
                Assert.That(resource.WorkCompleted, Is.True);
            }
        }

    }
}
