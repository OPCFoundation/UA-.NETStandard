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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
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
                .WithOptions(new GmsSystemOptions { Features = GmsFeatures.Sensors | GmsFeatures.SensorWarnings })
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            Assert.That(handle.TryGetSensorWarning("QualificationWarning", out SensorWarningAlarmState? warning),
                Is.True);
            Assert.That(handle.TryGetSensor("Probe", out BaseToolState? sensor), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(warning!.TypeDefinitionId,
                    Is.EqualTo(NodeId.Create(1010u, GmsModel.NamespaceUri, context.Context.NamespaceUris)));
                Assert.That(warning.SourceNode!.Value, Is.EqualTo(sensor!.NodeId));
                Assert.That(warning.ActiveState!.Id!.Value, Is.True);
                Assert.That(warning.AckedState!.Id!.Value, Is.False);
                Assert.That(warning.Retain!.Value, Is.True);
                Assert.That(warning.ErrorCode!.Value, Is.EqualTo("Q42"));
            });
            ISession session = await fixture.ConnectAsync(managed: true).ConfigureAwait(false);
            await using var client = new GmsClient(session, NUnitTelemetryContext.Create());
            var options = new MachineToolObservationOptions { Window = TimeSpan.FromSeconds(10) };
            MachineToolEventObservation retained = await client.ObserveConditionsAsync(handle.NodeId, options)
                .ConfigureAwait(false);
            Assert.That(retained.RefreshCompleted, Is.True);
            Assert.That(retained.Events.ToArray()!.Any(item => item.Event is SensorWarningAlarmTypeRecord native &&
                native.ErrorCode == "Q42"), Is.True);
            var condition = new SensorWarningAlarmTypeClient(session, warning!.NodeId, NUnitTelemetryContext.Create());
            await condition.AcknowledgeAsync(warning.EventId!.Value, new LocalizedText("Inspected"))
                .ConfigureAwait(false);
            Assert.That(warning.AckedState!.Id!.Value, Is.True);
            Assert.That(warning.ActiveState!.Id!.Value, Is.True,
                "Acknowledgement must not clear the physical warning.");
            Assert.That(warning.Retain!.Value, Is.True);
            await provider.SetSnapshotAsync(initial with
            {
                Sensors = [GmsEquipmentTests.Sensor("Probe")],
                SensorWarnings = [initial.SensorWarnings[0] with { IsActive = false }]
            }).ConfigureAwait(false);
            await handle.RefreshAsync().ConfigureAwait(false);
            Assert.That(warning.ActiveState.Id.Value, Is.False);
            Assert.That(warning.AckedState.Id.Value, Is.True);
            Assert.That(warning.Retain.Value, Is.False);
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
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await condition.DisableAsync().ConfigureAwait(false))!;
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(warning.EnabledState.Id.Value, Is.True);
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
                .WithOptions(new GmsSystemOptions { Features = GmsFeatures.Sensors | GmsFeatures.SensorWarnings })
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            Assert.That(handle.TryGetSensorWarning("QualificationWarning", out SensorWarningAlarmState? warning),
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
                SensorWarnings = [initial.SensorWarnings[0] with { ErrorCode = "Q43" }]
            }).ConfigureAwait(false);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await handle.RefreshAsync().ConfigureAwait(false));
            ByteString eventId = warning.EventId!.Value;
            await provider.SetSnapshotAsync(initial with
            {
                SensorWarnings = [initial.SensorWarnings[0] with { ErrorCode = "NotYetAcquired" }]
            }).ConfigureAwait(false);
            await handle.RetryPublicationAsync().ConfigureAwait(false);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(warning.EventId.Value, Is.EqualTo(eventId));
            Assert.That(warning.ErrorCode!.Value, Is.EqualTo("Q43"));
            Assert.That(handle.Observation!.SensorWarnings[0].ErrorCode, Is.EqualTo("Q43"));
            Assert.That(handle.TryGetSensorWarning("Unknown", out _), Is.False);
            Assert.Throws<ArgumentNullException>(() => handle.TryGetSensorWarning(null!, out _));
            Assert.Throws<ArgumentNullException>(() => handle.TryGetSensor(null!, out _));
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await handle.RefreshNotificationsAsync().ConfigureAwait(false));
            warning.OnReportEventAsync = null;
        }

        /// <summary>
        /// Supplies an actual unqualified sensor and its persistent provider-reported warning.
        /// </summary>
        internal static GmsSystemSnapshot Example()
        {
            GmsSensor sensor = GmsEquipmentTests.Sensor("Probe");
            return new GmsSystemSnapshot
            {
                Sensors =
                [
                    sensor with
                    {
                        Qualified = sensor.Qualified! with
                        {
                            RemainingHours = GmsEquipmentTests.Sample(Variant.From(-0.1))
                        },
                        IsQualifiedStatus = GmsEquipmentTests.Sample(Variant.From(ToolIsQualifiedStatus.NotQualified))
                    }
                ],
                SensorWarnings =
                [
                    new GmsSensorWarning
                    {
                        Key = "QualificationWarning", SensorKey = "Probe", ErrorCode = "Q42",
                        Message = new LocalizedText("Qualification expired"), IsActive = true,
                        LowLimit = GmsEquipmentTests.Sample(Variant.From(0.0)),
                        Sample = new MachineToolSampleInfo(GmsEquipmentTests.AcquisitionTime, StatusCodes.Good)
                    }
                ]
            };
        }
    }
}
