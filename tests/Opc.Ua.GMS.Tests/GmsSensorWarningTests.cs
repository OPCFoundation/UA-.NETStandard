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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Verifies the real native SensorWarningAlarmType through standard core condition methods and refresh.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class GmsSensorWarningTests
    {
        /// <summary>
        /// Environmental warnings use the additional sensor's measurement, independently of primary probe inventory.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task AdditionalSensorWarningUsesMeasuredInputOverTcpAsync(bool includeProbe)
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            GmsSensor probe = GmsEquipmentTests.Sensor("Probe");
            GmsSystemSnapshot initial = Example() with
            {
                Sensors = includeProbe ?
                [
                    probe with
                    {
                        Qualified = probe.Qualified! with
                        {
                            RemainingHours = GmsEquipmentTests.Sample(Variant.From(-0.1))
                        },
                        IsQualifiedStatus = GmsEquipmentTests.Sample(Variant.From(ToolIsQualifiedStatus.NotQualified))
                    }
                ] : default
            };
            var provider = new InMemoryGmsProvider(initial);
            GmsBuilder builder = GmsSystemTests.Builder(context, "EnvironmentalWarnings", provider)
                .WithOptions(new GmsSystemOptions
                {
                    Features = GmsFeatures.AdditionalSensors | GmsFeatures.SensorWarnings |
                        (includeProbe ? GmsFeatures.Sensors : GmsFeatures.None)
                })
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()));
            if (includeProbe)
            {
                builder.WithNotifications(new MachineToolNotificationOptions
                {
                    Provider = new QualificationNotificationProvider()
                });
            }
            await using GmsHandle handle = await builder.BuildAsync().ConfigureAwait(false);
            Assert.That(handle.TryGetSensorWarning("TemperatureWarning", out SensorWarningAlarmState? warning),
                Is.True);
            var temperature = handle.State.Equipment!.AdditionalSensor!.FindChild(context.Context,
                new QualifiedName("Temperature", context.InstanceNamespaceIndex)) as AdditionalSensorState;
            Assert.That(temperature, Is.Not.Null);
            ISession session = await fixture.ConnectAsync(managed: true).ConfigureAwait(false);
            await using var client = new GmsClient(session, NUnitTelemetryContext.Create());
            MachineToolEventObservation retained = await client.ObserveConditionsAsync(handle.NodeId,
                new MachineToolObservationOptions { Window = TimeSpan.FromSeconds(10) }).ConfigureAwait(false);
            Assert.That(retained.RefreshCompleted, Is.True);
            SensorWarningAlarmTypeRecord[] events = retained.Events.ToArray()!
                .Select(item => item.Event).OfType<SensorWarningAlarmTypeRecord>().ToArray();
            Assert.That(events, Has.Length.EqualTo(1));
            NodeId eventType = NodeId.Create(1010u, GmsModel.NamespaceUri, session.NamespaceUris);
            Assert.Multiple(() =>
            {
                Assert.That(events[0].EventType, Is.EqualTo(eventType));
                Assert.That(events[0].SourceNode, Is.EqualTo(temperature!.NodeId));
                Assert.That(events[0].InputNode, Is.EqualTo(temperature.NodeId));
                Assert.That(events[0].ErrorCode, Is.EqualTo("T42"));
                Assert.That(events[0].Retain, Is.True);
                Assert.That(handle.ConformanceUnits.ToArray()!.Any(unit => unit.Name == "GMS SensorWarningEventType"),
                    Is.True);
                Assert.That(handle.ConformanceUnits.ToArray()!.Any(unit => unit.Name == "GMS SensorType"),
                    Is.EqualTo(includeProbe));
            });
            ReadResponse read = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
            [
                new ReadValueId { NodeId = temperature!.NodeId, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = warning!.ActiveState!.Id!.NodeId, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = warning.AckedState!.Id!.NodeId, AttributeId = Attributes.Value }
            ], default).ConfigureAwait(false);
            Assert.That(read.Results.ToArray()!.All(value => StatusCode.IsGood(value.StatusCode)), Is.True);
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out double measured), Is.True);
            Assert.That(measured, Is.EqualTo(31.5));
            Assert.That(read.Results[0].SourceTimestamp, Is.EqualTo(GmsEquipmentTests.AcquisitionTime));
            Assert.That(read.Results[1].WrappedValue.TryGetValue(out bool active), Is.True);
            Assert.That(active, Is.True);
            Assert.That(read.Results[2].WrappedValue.TryGetValue(out bool acknowledged), Is.True);
            Assert.That(acknowledged, Is.False);
            if (includeProbe)
            {
                AlertTypeRecord[] alerts = retained.Events.ToArray()!
                    .Select(item => item.Event).OfType<AlertTypeRecord>().ToArray();
                Assert.That(alerts, Has.Length.EqualTo(1));
                Assert.That(alerts[0].ErrorCode, Is.EqualTo("Q42"));
                Assert.That(alerts[0].EventType, Is.EqualTo(ExpandedNodeId.ToNodeId(
                    Opc.Ua.MachineTool.ObjectTypeIds.AlertType, session.NamespaceUris)));
                Assert.That(alerts[0].ConditionId, Is.Not.EqualTo(events[0].ConditionId));
                Assert.That(handle.TryGetSensor("Probe", out BaseToolState? sensor), Is.True);
                Assert.That(sensor, Is.TypeOf<SensorState>());
                var qualified = (ToolLifeState)((SensorState)sensor!).ToolLife!.FindChild(context.Context,
                    new QualifiedName(BrowseNames.Qualified,
                        (ushort)session.NamespaceUris.GetIndex(GmsModel.NamespaceUri)))!;
                DataValue countdown = await session.ReadValueAsync(qualified.NodeId).ConfigureAwait(false);
                Assert.That(countdown.WrappedValue.TryGetValue(out double hours), Is.True);
                Assert.That(hours, Is.EqualTo(-0.1));
                Assert.That(qualified.IsCountingUp!.Value, Is.False);
                Assert.That(events[0].SourceNode, Is.Not.EqualTo(sensor.NodeId));
                var condition = new SensorWarningAlarmTypeClient(
                    session, warning.NodeId, NUnitTelemetryContext.Create());
                await condition.AcknowledgeAsync(warning.EventId!.Value, new LocalizedText("Environment inspected"))
                    .ConfigureAwait(false);
                await provider.SetSnapshotAsync(initial with
                {
                    AdditionalSensors =
                    [
                        initial.AdditionalSensors[0] with { Value = GmsEquipmentTests.Sample(Variant.From(21.5)) }
                    ],
                    SensorWarnings = [initial.SensorWarnings[0] with { IsActive = false }]
                }).ConfigureAwait(false);
                await handle.RefreshAsync().ConfigureAwait(false);
                MachineToolEventObservation remaining = await client.ObserveConditionsAsync(handle.NodeId,
                    new MachineToolObservationOptions { Window = TimeSpan.FromSeconds(10) }).ConfigureAwait(false);
                Assert.That(remaining.RefreshCompleted, Is.True);
                Assert.That(remaining.Events.Count, Is.EqualTo(1));
                Assert.That(remaining.Events[0].Event, Is.TypeOf<AlertTypeRecord>());
                Assert.That(((AlertTypeRecord)remaining.Events[0].Event).ConditionId,
                    Is.EqualTo(alerts[0].ConditionId));
                Assert.That(warning.ActiveState.Id.Value, Is.False);
                Assert.That(warning.Retain!.Value, Is.False);
                countdown = await session.ReadValueAsync(qualified.NodeId).ConfigureAwait(false);
                Assert.That(countdown.WrappedValue, Is.EqualTo(Variant.From(-0.1)));
            }
        }

        /// <summary>
        /// A primary measurement probe or an unknown slot cannot become a standard environmental warning source.
        /// </summary>
        [TestCase("Probe")]
        [TestCase("Missing")]
        public async Task NonAdditionalWarningSourceIsRejectedBeforePublicationAsync(string sensorKey)
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            GmsSystemSnapshot initial = Example();
            var provider = new InMemoryGmsProvider(initial with
            {
                Sensors = [GmsEquipmentTests.Sensor("Probe")],
                SensorWarnings = [initial.SensorWarnings[0] with { SensorKey = sensorKey }]
            });
            GmsBuilder builder = GmsSystemTests.Builder(context, "RejectedSource", provider)
                .WithOptions(new GmsSystemOptions
                {
                    Features = GmsFeatures.Sensors | GmsFeatures.AdditionalSensors | GmsFeatures.SensorWarnings
                })
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()));
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await builder.BuildAsync().ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(error.Message, Does.Contain("actual additional sensor"));
            Assert.That(context.MachinesFolder.FindChild(context.Context,
                new QualifiedName("RejectedSource", context.InstanceNamespaceIndex)), Is.Null);
        }

        /// <summary>
        /// Explicit input references may name the measured additional-sensor variable, not metadata or foreign nodes.
        /// </summary>
        [TestCase("Measurement")]
        [TestCase("EngineeringUnits")]
        [TestCase("Foreign")]
        public async Task ExplicitWarningInputMustBeItsAdditionalSensorMeasurementAsync(string inputKind)
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            var measuredId = new NodeId("WarningTemperature", context.InstanceNamespaceIndex);
            var unitsId = new NodeId("WarningTemperatureUnits", context.InstanceNamespaceIndex);
            NodeId input = inputKind switch
            {
                "Measurement" => measuredId,
                "EngineeringUnits" => unitsId,
                _ => Opc.Ua.VariableIds.Server_ServerStatus_CurrentTime
            };
            GmsSystemSnapshot initial = Example();
            var provider = new InMemoryGmsProvider(initial with
            {
                SensorWarnings = [initial.SensorWarnings[0] with { InputNodeId = input }]
            });
            GmsBuilder builder = GmsSystemTests.Builder(context, "ExplicitInput", provider)
                .WithOptions(new GmsSystemOptions
                {
                    Features = GmsFeatures.AdditionalSensors | GmsFeatures.SensorWarnings
                })
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .WithMachinery(machine =>
                {
                    var temperature = (AdditionalSensorState)machine.State.Equipment!.AdditionalSensor!
                        .FindChild(context.Context, new QualifiedName("Temperature", context.InstanceNamespaceIndex))!;
                    temperature.NodeId = measuredId;
                    temperature.EngineeringUnits!.NodeId = unitsId;
                });
            if (inputKind == "Measurement")
            {
                await using GmsHandle handle = await builder.BuildAsync().ConfigureAwait(false);
                Assert.That(handle.TryGetSensorWarning("TemperatureWarning", out SensorWarningAlarmState? warning),
                    Is.True);
                ISession session = await fixture.ConnectAsync().ConfigureAwait(false);
                DataValue read = await session.ReadValueAsync(warning!.InputNode!.NodeId).ConfigureAwait(false);
                Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.WrappedValue, Is.EqualTo(Variant.From(measuredId)));
                Assert.That(warning.SourceNode!.Value, Is.EqualTo(measuredId));
                return;
            }
            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await builder.BuildAsync().ConfigureAwait(false)))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(error.Message, Does.Contain("additional sensor's measured value"));
            Assert.That(fixture.Manager.FindPredefinedNode(measuredId), Is.Null);
            Assert.That(context.MachinesFolder.FindChild(context.Context,
                new QualifiedName("ExplicitInput", context.InstanceNamespaceIndex)), Is.Null);
        }

        /// <summary>
        /// Native warnings retain their error code, sensor source and acknowledgement until the reason clears.
        /// </summary>
        [Test]
        public async Task NativeSensorConditionRetainsAcknowledgementAndDecodesErrorCodeOverTcpAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            GmsSystemSnapshot initial = Example();
            var provider = new InMemoryGmsProvider(initial);
            await using GmsHandle handle = await GmsSystemTests.Builder(context, "WarningsGms", provider)
                .WithOptions(new GmsSystemOptions
                {
                    Features = GmsFeatures.AdditionalSensors | GmsFeatures.SensorWarnings
                })
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            Assert.That(handle.TryGetSensorWarning("TemperatureWarning", out SensorWarningAlarmState? warning),
                Is.True);
            var sensor = (AdditionalSensorState)handle.State.Equipment!.AdditionalSensor!.FindChild(context.Context,
                new QualifiedName("Temperature", context.InstanceNamespaceIndex))!;
            NodeId warningId = warning!.NodeId;
            Assert.Multiple(() =>
            {
                Assert.That(warning!.TypeDefinitionId,
                    Is.EqualTo(NodeId.Create(1010u, GmsModel.NamespaceUri, context.Context.NamespaceUris)));
                Assert.That(warning.SourceNode!.Value, Is.EqualTo(sensor!.NodeId));
                Assert.That(warning.InputNode!.Value, Is.EqualTo(sensor.NodeId));
                Assert.That(warning.ActiveState!.Id!.Value, Is.True);
                Assert.That(warning.AckedState!.Id!.Value, Is.False);
                Assert.That(warning.Retain!.Value, Is.True);
                Assert.That(warning.ErrorCode!.Value, Is.EqualTo("T42"));
            });
            ISession session = await fixture.ConnectAsync(managed: true).ConfigureAwait(false);
            await using var client = new GmsClient(session, NUnitTelemetryContext.Create());
            var options = new MachineToolObservationOptions { Window = TimeSpan.FromSeconds(10) };
            MachineToolEventObservation retained = await client.ObserveConditionsAsync(handle.NodeId, options)
                .ConfigureAwait(false);
            Assert.That(retained.RefreshCompleted, Is.True);
            Assert.That(retained.Events.ToArray()!.Any(item => item.Event is SensorWarningAlarmTypeRecord native &&
                native.ErrorCode == "T42"), Is.True);
            var condition = new SensorWarningAlarmTypeClient(session, warning!.NodeId, NUnitTelemetryContext.Create());
            await condition.AcknowledgeAsync(warning.EventId!.Value, new LocalizedText("Inspected"))
                .ConfigureAwait(false);
            Assert.That(warning.AckedState!.Id!.Value, Is.True);
            Assert.That(warning.ActiveState!.Id!.Value, Is.True,
                "Acknowledgement must not clear the physical warning.");
            Assert.That(warning.Retain!.Value, Is.True);
            await provider.SetSnapshotAsync(initial with
            {
                AdditionalSensors =
                [
                    initial.AdditionalSensors[0] with { Value = GmsEquipmentTests.Sample(Variant.From(21.5)) }
                ],
                SensorWarnings = [initial.SensorWarnings[0] with { IsActive = false }]
            }).ConfigureAwait(false);
            await handle.RefreshAsync().ConfigureAwait(false);
            Assert.That(warning.ActiveState.Id.Value, Is.False);
            Assert.That(warning.AckedState.Id.Value, Is.True);
            Assert.That(warning.Retain.Value, Is.False);
            Assert.That(handle.TryGetSensorWarning("TemperatureWarning", out SensorWarningAlarmState? inactive),
                Is.True);
            Assert.That(inactive, Is.SameAs(warning));
            Assert.That(inactive!.NodeId, Is.EqualTo(warningId));
            await condition.DisableAsync().ConfigureAwait(false);
            Assert.That(warning.EnabledState!.Id!.Value, Is.False);
            await condition.EnableAsync().ConfigureAwait(false);
            Assert.That(warning.EnabledState.Id.Value, Is.True);
            await provider.SetSnapshotAsync(initial with
            {
                SensorWarnings =
                [
                    initial.SensorWarnings[0] with { AllowedOperations = MachineToolConditionOperations.None }
                ]
            }).ConfigureAwait(false);
            await handle.RefreshAsync().ConfigureAwait(false);
            ServiceResultException denied = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await condition.DisableAsync().ConfigureAwait(false)))!;
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(warning.EnabledState.Id.Value, Is.True);
            Assert.That(warning.ActiveState.Id.Value, Is.True);
            Assert.That(warning.AckedState.Id.Value, Is.False);
            Assert.That(warning.Retain.Value, Is.True);
            await handle.DisposeAsync().ConfigureAwait(false);
            Assert.That(handle.TryGetSensorWarning("TemperatureWarning", out _), Is.False);
            Assert.That(fixture.Manager.FindPredefinedNode(warningId), Is.Null);
            Assert.That(fixture.Manager.FindPredefinedNode(sensor.NodeId), Is.Null);
            Assert.That(handle.ConformanceUnits, Is.Empty);
        }

        /// <summary>
        /// A failing event sink retains the same accepted native event and never reacquires a later provider sample.
        /// </summary>
        [Test]
        public async Task FailedSensorPublicationRetriesExactAcceptedEventWithoutReacquisitionAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            GmsSystemSnapshot initial = Example();
            var provider = new InMemoryGmsProvider(initial);
            await using GmsHandle handle = await GmsSystemTests.Builder(context, "RetryWarningGms", provider)
                .WithOptions(new GmsSystemOptions
                {
                    Features = GmsFeatures.AdditionalSensors | GmsFeatures.SensorWarnings
                })
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            Assert.That(handle.TryGetSensorWarning("TemperatureWarning", out SensorWarningAlarmState? warning),
                Is.True);
            IFilterTarget? captured = null;
            int calls = 0;
            warning!.OnReportEventAsync = (_, _, value, _) =>
            {
                calls++;
                if (calls == 1)
                {
                    captured = value;
                    throw new InvalidOperationException("Expected native publication failure.");
                }
                Assert.That(value, Is.SameAs(captured));
                return default;
            };
            await provider.SetSnapshotAsync(initial with
            {
                SensorWarnings = [initial.SensorWarnings[0] with { ErrorCode = "T43" }]
            }).ConfigureAwait(false);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await handle.RefreshAsync().ConfigureAwait(false));
            ByteString eventId = warning.EventId!.Value;
            await provider.SetSnapshotAsync(initial with
            {
                SensorWarnings = [initial.SensorWarnings[0] with { ErrorCode = "NotYetAcquired" }]
            }).ConfigureAwait(false);
            await handle.RetryPublicationAsync().ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(warning.EventId.Value, Is.EqualTo(eventId));
            Assert.That(warning.ErrorCode!.Value, Is.EqualTo("T43"));
            Assert.That(handle.Observation!.SensorWarnings[0].ErrorCode, Is.EqualTo("T43"));
            Assert.That(handle.TryGetSensorWarning("Unknown", out _), Is.False);
            Assert.Throws<ArgumentNullException>(() => handle.TryGetSensorWarning(null!, out _));
            Assert.Throws<ArgumentNullException>(() => handle.TryGetSensor(null!, out _));
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await handle.RefreshNotificationsAsync().ConfigureAwait(false));
            warning.OnReportEventAsync = null;
        }

        /// <summary>
        /// Supplies an additional environmental sensor outside its configured temperature range.
        /// </summary>
        internal static GmsSystemSnapshot Example()
        {
            return new GmsSystemSnapshot
            {
                AdditionalSensors =
                [
                    new GmsAdditionalSensor
                    {
                        Key = "Temperature",
                        Identifier = "Ambient-T1",
                        Name = "Ambient temperature",
                        Class = GmsEquipmentTests.Sample(Variant.From((byte)1)),
                        Value = GmsEquipmentTests.Sample(Variant.From(31.5)),
                        EngineeringUnits = new EUInformation
                        {
                            NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                            UnitId = 4408652,
                            DisplayName = new LocalizedText("°C"),
                            Description = new LocalizedText("degree Celsius")
                        }
                    }
                ],
                SensorWarnings =
                [
                    new GmsSensorWarning
                    {
                        Key = "TemperatureWarning", SensorKey = "Temperature", ErrorCode = "T42",
                        Message = new LocalizedText("Ambient temperature above allowed range"), IsActive = true,
                        HighLimit = GmsEquipmentTests.Sample(Variant.From(30.0)),
                        Sample = new MachineToolSampleInfo(GmsEquipmentTests.AcquisitionTime, StatusCodes.Good)
                    }
                ]
            };
        }

        /// <summary>
        /// Keeps primary-probe qualification notifications on the existing distinct inherited AlertType.
        /// </summary>
        private sealed class QualificationNotificationProvider : IMachineToolNotificationProvider
        {
            /// <inheritdoc/>
            public ValueTask<MachineToolNotificationSnapshot> ReadAsync(
                ulong afterSequence, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<MachineToolNotificationSnapshot>(new MachineToolNotificationSnapshot
                {
                    Alerts =
                    [
                        new MachineToolAlert
                        {
                            Key = "ProbeQualification", ErrorCode = "Q42", Message = new LocalizedText("Probe expired"),
                            IsActive = true, Severity = 500,
                            Sample = new MachineToolSampleInfo(GmsEquipmentTests.AcquisitionTime, StatusCodes.Good)
                        }
                    ]
                });
            }
        }
    }
}
