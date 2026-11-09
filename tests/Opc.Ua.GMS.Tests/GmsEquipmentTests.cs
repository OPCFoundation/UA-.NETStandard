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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.Server.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Exercises native sensor observations through the public system facade and real service reads.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class GmsEquipmentTests
    {
        /// <summary>
        /// Both released rack types expose their own native identity and acquired availability over a real session.
        /// </summary>
        [TestCase(GmsAccessoryKind.SensorExchangeRack, false)]
        [TestCase(GmsAccessoryKind.SensorExchangeRack, true)]
        [TestCase(GmsAccessoryKind.TipExchangeRack, false)]
        [TestCase(GmsAccessoryKind.TipExchangeRack, true)]
        public async Task ExchangeRackIdentityAndAvailabilityAreNativeOverTcpAsync(
            GmsAccessoryKind kind, bool available)
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            var accessory = new GmsAccessory
            {
                Key = "Rack", Kind = kind,
                Identification = new GmsItemIdentification(new LocalizedText("Rack manufacturer"), "Rack-1"),
                IsAvailable = Sample(Variant.From(available), StatusCodes.UncertainSensorNotAccurate)
            };
            var provider = new InMemoryGmsProvider(new GmsSystemSnapshot { Accessories = [accessory] });
            await using GmsHandle handle = await GmsSystemTests.Builder(context, "RackGms", provider)
                .WithOptions(new GmsSystemOptions { Features = GmsFeatures.Accessories })
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            BaseInstanceState? rack = handle.State.Equipment!.Accessories!.FindChild(
                context.Context, new QualifiedName("Rack", context.InstanceNamespaceIndex));
            PropertyState<bool> availability;
            ExpandedNodeId expected;
            if (kind == GmsAccessoryKind.SensorExchangeRack)
            {
                Assert.That(rack, Is.TypeOf<SensorExchangeRackState>());
                availability = ((SensorExchangeRackState)rack!).IsAvailable!;
                expected = ObjectTypeIds.SensorExchangeRackType;
            }
            else
            {
                Assert.That(rack, Is.TypeOf<TipExchangeRackState>());
                availability = ((TipExchangeRackState)rack!).IsAvailable!;
                expected = ObjectTypeIds.TipExchangeRackType;
            }
            Assert.That(availability, Is.Not.Null);
            ISession session = await fixture.ConnectAsync().ConfigureAwait(false);
            INode wireNode = await session.NodeCache.GetNodeAsync(rack!.NodeId).ConfigureAwait(false);
            Assert.That(wireNode.NodeClass, Is.EqualTo(NodeClass.Object));
            Assert.That(ExpandedNodeId.ToNodeId(wireNode.TypeDefinitionId, session.NamespaceUris),
                Is.EqualTo(ExpandedNodeId.ToNodeId(expected, session.NamespaceUris)));
            DataValue initial = await session.ReadValueAsync(availability.NodeId).ConfigureAwait(false);
            Assert.That(initial.WrappedValue.TryGetValue(out bool actual), Is.True);
            Assert.That(actual, Is.EqualTo(available));
            Assert.That(initial.StatusCode, Is.EqualTo(StatusCodes.UncertainSensorNotAccurate));
            Assert.That(initial.SourceTimestamp, Is.EqualTo(AcquisitionTime));

            DateTimeUtc later = AcquisitionTime.AddMilliseconds(500);
            await provider.SetSnapshotAsync(new GmsSystemSnapshot
            {
                Accessories = [accessory with
                {
                    IsAvailable = new DataValue(Variant.From(!available), StatusCodes.Good, later)
                }]
            }).ConfigureAwait(false);
            await handle.RefreshAsync().ConfigureAwait(false);
            DataValue changed = await session.ReadValueAsync(availability.NodeId).ConfigureAwait(false);
            Assert.That(changed.WrappedValue.TryGetValue(out actual), Is.True);
            Assert.That(actual, Is.EqualTo(!available));
            Assert.That(changed.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(changed.SourceTimestamp, Is.EqualTo(later));
            await using var facade = new GmsClient(session, NUnitTelemetryContext.Create());
            GmsEquipmentReadSnapshot inventory = await facade.ReadEquipmentAsync(handle.NodeId).ConfigureAwait(false);
            Assert.That(inventory.Accessories.Count, Is.EqualTo(1));
            Assert.That(inventory.Accessories[0].NodeId, Is.EqualTo(rack.NodeId));
        }

        /// <summary>
        /// Native qualification is a meaningful Double countdown, not the contradictory XML true default.
        /// </summary>
        [Test]
        public async Task NativeSensorPublishesQualifiedCountdownAndPreservesQualityAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            GmsSensor sensor = Sensor("Probe");
            var provider = new InMemoryGmsProvider(new GmsSystemSnapshot { Sensors = [sensor] });
            await using GmsHandle handle = await GmsSystemTests.Builder(context, "SensorGms", provider)
                .WithOptions(new GmsSystemOptions { Features = GmsFeatures.Sensors })
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            Assert.That(handle.State.Equipment!.Tools, Is.Not.Null);
            BaseInstanceState? child = handle.State.Equipment.Tools!.FindChild(context.Context,
                new QualifiedName("Probe", context.InstanceNamespaceIndex));
            Assert.That(child, Is.TypeOf<SensorState>());
            var native = (SensorState)child!;
            Assert.That(native.AddQualified(context.Context), Is.SameAs(native));
            var qualified = (ToolLifeState)native.ToolLife!.FindChild(context.Context,
                new QualifiedName("Qualified", (ushort)context.Context.NamespaceUris.GetIndex(GmsModel.NamespaceUri)))!;
            Assert.Multiple(() =>
            {
                Assert.That(qualified, Is.InstanceOf<ToolLifeState<double>>());
                Assert.That(qualified.IsCountingUp!.Value, Is.False);
                Assert.That(qualified.Indication!.Value, Is.EqualTo(ToolLifeIndication.Time));
                Assert.That(qualified.EngineeringUnits!.Value!.UnitId, Is.EqualTo(0x485552));
            });
            ISession session = await fixture.ConnectAsync().ConfigureAwait(false);
            DataValue read = await session.ReadValueAsync(qualified.NodeId).ConfigureAwait(false);
            Assert.That(read.WrappedValue.TryGetValue(out double hours), Is.True);
            Assert.That(hours, Is.EqualTo(2.5));
            Assert.That(read.SourceTimestamp, Is.EqualTo(AcquisitionTime));
            await provider.SetSnapshotAsync(new GmsSystemSnapshot
            {
                Sensors =
                [
                    sensor with
                    {
                        Qualified = sensor.Qualified! with
                        {
                            RemainingHours = Sample(Variant.From(1.25), StatusCodes.UncertainSensorNotAccurate)
                        }
                    }
                ]
            }).ConfigureAwait(false);
            await handle.RefreshAsync().ConfigureAwait(false);
            DataValue changed = await session.ReadValueAsync(qualified.NodeId).ConfigureAwait(false);
            Assert.That(changed.WrappedValue.TryGetValue(out hours), Is.True);
            Assert.That(hours, Is.EqualTo(1.25));
            Assert.That(changed.StatusCode, Is.EqualTo(StatusCodes.UncertainSensorNotAccurate));
            Assert.That(changed.SourceTimestamp, Is.EqualTo(AcquisitionTime));
        }

        /// <summary>
        /// Native accessories keep their actual item nameplates and additional sensors retain numeric representation.
        /// </summary>
        [Test]
        public async Task AccessoriesAndAdditionalSensorsRetainNativeTypesAndItemNameplatesAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            var initial = new GmsSystemSnapshot
            {
                Accessories =
                [
                    new GmsAccessory
                    {
                        Key = "Turntable",
                        Kind = GmsAccessoryKind.RotaryTable,
                        Identification = new GmsItemIdentification(new LocalizedText("Table manufacturer"), "RT-1"),
                        IsIntegrated = Sample(Variant.From(true)),
                        NumberOfAxes = Sample(Variant.From((byte)2))
                    },
                    new GmsAccessory
                    {
                        Key = "Probes",
                        Kind = GmsAccessoryKind.SensorExchangeRack,
                        Identification = new GmsItemIdentification(new LocalizedText("Rack manufacturer"), "SR-1"),
                        IsAvailable = Sample(Variant.From(true))
                    },
                    new GmsAccessory
                    {
                        Key = "Tips",
                        Kind = GmsAccessoryKind.TipExchangeRack,
                        Identification = new GmsItemIdentification(new LocalizedText("Rack manufacturer"), "TR-1"),
                        IsAvailable = Sample(Variant.From(false))
                    }
                ],
                AdditionalSensors =
                [
                    new GmsAdditionalSensor
                    {
                        Key = "Temperature",
                        Identifier = "T-1",
                        Name = "Ambient",
                        Class = Sample(Variant.From((byte)1)),
                        Value = Sample(Variant.From(21.5f)),
                        EngineeringUnits = new EUInformation
                        {
                            NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                            UnitId = 4408652, DisplayName = new LocalizedText("°C"),
                            Description = new LocalizedText("degree Celsius")
                        }
                    }
                ]
            };
            var provider = new InMemoryGmsProvider(initial);
            await using GmsHandle handle = await GmsSystemTests.Builder(context, "EquipmentGms", provider)
                .WithOptions(new GmsSystemOptions
                {
                    Features = GmsFeatures.Accessories | GmsFeatures.AdditionalSensors
                }).WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            FolderState? accessories = handle.State.Equipment!.Accessories;
            Assert.That(accessories, Is.Not.Null);
            var rotary = accessories!.FindChild(context.Context,
                new QualifiedName("Turntable", context.InstanceNamespaceIndex)) as RotaryTableState;
            Assert.That(rotary, Is.Not.Null);
            Assert.That(rotary!.Identification!.TypeDefinitionId, Is.EqualTo(ExpandedNodeId.ToNodeId(
                Opc.Ua.Machinery.ObjectTypeIds.MachineryItemIdentificationType, context.Context.NamespaceUris)));
            Assert.That(rotary.Identification.Manufacturer!.Value.Text, Is.EqualTo("Table manufacturer"));
            Assert.That(rotary.Identification.SerialNumber!.Value, Is.EqualTo("RT-1"));
            Assert.That(rotary.NumberOfAxes!.Value, Is.EqualTo((byte)2));
            var temperature = handle.State.Equipment.AdditionalSensor!.FindChild(context.Context,
                new QualifiedName("Temperature", context.InstanceNamespaceIndex)) as AdditionalSensorState;
            Assert.That(temperature, Is.Not.Null);
            ISession session = await fixture.ConnectAsync().ConfigureAwait(false);
            DataValue read = await session.ReadValueAsync(temperature!.NodeId).ConfigureAwait(false);
            Assert.That(read.WrappedValue.TryGetValue(out float value), Is.True);
            Assert.That(value, Is.EqualTo(21.5f));
            Assert.That(temperature.EngineeringUnits!.Value!.UnitId, Is.EqualTo(4408652));
            await provider.SetSnapshotAsync(initial with
            {
                AdditionalSensors =
                [
                    initial.AdditionalSensors[0] with { Value = Sample(Variant.From(22.0f)) }
                ]
            }).ConfigureAwait(false);
            await handle.RefreshAsync().ConfigureAwait(false);
            read = await session.ReadValueAsync(temperature.NodeId).ConfigureAwait(false);
            Assert.That(read.WrappedValue.TryGetValue(out value), Is.True);
            Assert.That(value, Is.EqualTo(22.0f));
            await using var facade = new GmsClient(session, NUnitTelemetryContext.Create());
            GmsEquipmentReadSnapshot inventory = await facade.ReadEquipmentAsync(handle.NodeId).ConfigureAwait(false);
            Assert.That(inventory.Accessories.Count, Is.EqualTo(3));
            Assert.That(inventory.AdditionalSensors.Count, Is.EqualTo(1));
            Assert.That(inventory.Accessories[0].Identification.Manufacturer.Value.WrappedValue.TryGetValue(
                out LocalizedText manufacturer), Is.True);
            Assert.That(manufacturer.Text, Is.EqualTo("Table manufacturer"));
        }

        /// <summary>
        /// A rejected prospective active-tool link rolls back the actual native factory and can be retried safely.
        /// </summary>
        [Test]
        public async Task DynamicSensorsRollbackInvalidReferencesAndReleaseRemovedNodesAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            GmsSensor first = Sensor("First");
            GmsSensor second = Sensor("Second") with
            {
                Tool = Sensor("Second").Tool with
                {
                    Control = new MachineToolToolControl(8, ToolManagement.NumberBased, false,
                        ToolLocked.CapabilityUnavailable)
                }
            };
            var metadata = new MachineToolSampleInfo(AcquisitionTime, StatusCodes.Good);
            var initial = new GmsSystemSnapshot
            {
                Sensors = [first],
                ToolMonitoring = new GmsToolMonitoringObservation { ActiveSensorKeys = ["First"], Sample = metadata }
            };
            var provider = new InMemoryGmsProvider(initial);
            await using GmsHandle handle = await GmsSystemTests.Builder(context, "DynamicNative", provider)
                .WithOptions(new GmsSystemOptions
                {
                    Features = GmsFeatures.Sensors | GmsFeatures.DynamicSensors | GmsFeatures.ToolMonitoring
                }).WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            Assert.That(handle.TryGetSensor("First", out BaseToolState? original), Is.True);
            NodeId originalId = original!.NodeId;
            string initialVersion = handle.State.Equipment!.Tools!.GetNodeVersionProperty()!.Value;
            await provider.SetSnapshotAsync(initial with
            {
                Sensors = [first, second],
                ToolMonitoring = initial.ToolMonitoring with { ActiveSensorKeys = ["Missing"] }
            }).ConfigureAwait(false);
            await Assert.ThrowsAsync<ServiceResultException>(async () => await handle.RefreshAsync().ConfigureAwait(false));
            Assert.That(handle.Observation, Is.SameAs(initial));
            Assert.That(handle.TryGetSensor("Second", out _), Is.False);
            Assert.That(handle.State.Equipment.Tools!.GetNodeVersionProperty()!.Value, Is.EqualTo(initialVersion));
            await provider.SetSnapshotAsync(initial with
            {
                Sensors = [first, second],
                ToolMonitoring = initial.ToolMonitoring with { ActiveSensorKeys = ["Second"] }
            }).ConfigureAwait(false);
            await handle.RefreshAsync().ConfigureAwait(false);
            Assert.That(handle.TryGetSensor("Second", out BaseToolState? added), Is.True);
            Assert.That(added, Is.TypeOf<SensorState>());
            Assert.That(handle.State.Monitoring!.ToolMonitoring!.ActiveTool!.Value,
                Is.EqualTo((ArrayOf<NodeId>)[added!.NodeId]));
            await provider.SetSnapshotAsync(initial with
            {
                Sensors = [second],
                ToolMonitoring = initial.ToolMonitoring with { ActiveSensorKeys = ["Second"] }
            }).ConfigureAwait(false);
            await handle.RefreshAsync().ConfigureAwait(false);
            Assert.That(handle.TryGetSensor("First", out _), Is.False);
            Assert.That(fixture.Manager.FindPredefinedNode(originalId), Is.Null);
            ISession session = await fixture.ConnectAsync().ConfigureAwait(false);
            await using var client = new GmsClient(session, NUnitTelemetryContext.Create());
            GmsEquipmentReadSnapshot read = await client.ReadEquipmentAsync(handle.NodeId).ConfigureAwait(false);
            Assert.That(read.Sensors.Count, Is.EqualTo(1));
            Assert.That(read.Sensors[0].Inherited.NodeId, Is.EqualTo(added.NodeId));
        }

        /// <summary>
        /// Supplies an explicitly qualified physical sensor with genuine controller observations.
        /// </summary>
        internal static GmsSensor Sensor(string key)
        {
            return new GmsSensor
            {
                Tool = new MachineToolTool
                {
                    Key = key,
                    Control = new MachineToolToolControl(7, ToolManagement.NumberBased, false,
                        ToolLocked.CapabilityUnavailable),
                    Sample = new MachineToolSampleInfo(AcquisitionTime, StatusCodes.Good)
                },
                Class = Sample(Variant.From((ushort)3)),
                Alignment = Sample(Variant.From(ToolAlignmentState.Fixed)),
                IsQualifiedStatus = Sample(Variant.From(ToolIsQualifiedStatus.Qualified)),
                Qualified = new GmsQualifiedLife { RemainingHours = Sample(Variant.From(2.5)) }
            };
        }

        /// <summary>
        /// Supplies a deterministic raw acquisition.
        /// </summary>
        internal static DataValue Sample(Variant value, StatusCode statusCode = default)
        {
            return new DataValue(value, statusCode, AcquisitionTime);
        }

        /// <summary>The deterministic equipment acquisition timestamp.</summary>
        internal static readonly DateTimeUtc AcquisitionTime = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    }
}
