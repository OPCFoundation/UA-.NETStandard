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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies bounded passive event workflows using actual Publish and ConditionRefresh services.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("MachineTool")]
    [Category("Integration")]
    public sealed class MachineToolNotificationClientTests
    {
        /// <summary>
        /// A retained refresh completes at RefreshEnd, preserves standard fields and never implicitly acknowledges.
        /// </summary>
        [Test]
        public async Task RetainedRefreshUsesNativeConditionDecoderAndReleasesSubscriptionAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var sample = new MachineToolSampleInfo(
                new DateTime(2026, 6, 8, 10, 11, 12, DateTimeKind.Utc), StatusCodes.UncertainLastUsableValue);
            var provider = new MachineToolNotificationTests.NotificationProvider(new MachineToolNotificationSnapshot
            {
                Alerts =
                [
                    new MachineToolAlert
                    {
                        Key = "Coolant", ErrorCode = "E91", Message = new LocalizedText("Coolant empty"),
                        Severity = 800, IsActive = true, Sample = sample
                    }
                ]
            });
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolNotificationTests.BuildAsync(fixture, provider).ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            transport.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            await transport.LoadClientConfigurationAsync(fixture.Root, "NotificationObserver").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
            Assert.That(session.TryGetSubscriptionManager(out ISubscriptionManager? subscriptions), Is.True);
            int before = subscriptions!.Count;

            MachineToolEventObservation result = await client.ObserveConditionsAsync(runtime.NodeId,
                new MachineToolObservationOptions { Window = TimeSpan.FromSeconds(10), MaximumNotifications = 1 })
                .ConfigureAwait(false);

            Assert.That(result.RefreshCompleted, Is.True);
            Assert.That(result.Events.Count, Is.EqualTo(1));
            Assert.That(result.Events[0].Event, Is.TypeOf<AlertTypeRecord>());
            var alert = (AlertTypeRecord)result.Events[0].Event;
            Assert.That(alert.ErrorCode, Is.EqualTo("E91"));
            Assert.That(alert.Time, Is.EqualTo(sample.SourceTimestamp));
            Assert.That(runtime.Notifications!.TryGetAlert("Coolant", out AlertState? state), Is.True);
            Assert.That(alert.ConditionId, Is.EqualTo(state!.NodeId));
            Assert.That(state.AckedState!.Id!.Value, Is.False);
            Assert.That(subscriptions.Count, Is.EqualTo(before));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Live messages use concrete types, propagate once, preserve fields and report server overflow.
        /// </summary>
        [Test]
        public async Task LiveEventsSupportGeneratedVendorDecodersAndExplicitOverflowAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var provider = new MachineToolNotificationTests.NotificationProvider(new MachineToolNotificationSnapshot());
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolNotificationTests.BuildAsync(fixture, provider).ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            transport.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            await transport.LoadClientConfigurationAsync(fixture.Root, "LiveEvents").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
            var options = new MachineToolObservationOptions
            {
                Window = TimeSpan.FromSeconds(10),
                MaximumNotifications = 1
            };
            Task<MachineToolEventObservation> first = client.ObserveEventsAsync(runtime.NodeId, options).AsTask();
            await WaitForMonitoringAsync(runtime.State).ConfigureAwait(false);
            MachineToolMessage message = MachineToolNotificationValidationTests.Message(1);
            provider.Snapshot = new MachineToolNotificationSnapshot { Revision = 1, Messages = [message] };
            await runtime.RefreshNotificationsAsync().ConfigureAwait(false);
            MachineToolEventObservation initial = await first.ConfigureAwait(false);
            Assert.That(initial.Events.Count, Is.EqualTo(1));
            Assert.That(initial.Events[0].Event, Is.TypeOf<NotificationEventTypeRecord>());
            var record = (NotificationEventTypeRecord)initial.Events[0].Event;
            Assert.That(record.Identifier, Is.EqualTo("M1"));
            Assert.That(record.Time, Is.EqualTo(message.Time));
            Assert.That(record.EventType.NamespaceIndex, Is.EqualTo(fixture.Context.InstanceNamespaceIndex));
            Assert.That(initial.DroppedNotifications, Is.Zero);

            QualifiedName[][] generated = NotificationEventTypeRecord.Decoder.GetStandardFields(session.NamespaceUris);
            ArrayOf<ArrayOf<QualifiedName>> fields =
                Array.ConvertAll(generated, static path => path.ToArrayOf()).ToArrayOf();
            int decoded = 0;
            client.WithEventDecoder(new ExpandedNodeId(record.EventType), fields, values =>
            {
                Interlocked.Increment(ref decoded);
                return NotificationEventTypeRecord.Decoder.Decode(values.ToArray()!);
            });
            Task<MachineToolEventObservation> next = client.ObserveEventsAsync(runtime.NodeId, options).AsTask();
            await WaitForMonitoringAsync(runtime.State).ConfigureAwait(false);
            Assert.Throws<ServiceResultException>(() => client.WithEventDecoder(
                new ExpandedNodeId(new NodeId(Guid.NewGuid(), fixture.Context.InstanceNamespaceIndex)), fields,
                values => NotificationEventTypeRecord.Decoder.Decode(values.ToArray()!)));
            var overflow = new EventQueueOverflowEventState(null);
            overflow.Initialize(
                fixture.Context.Context, runtime.State, EventSeverity.Low, new LocalizedText("Overflow"));
            await runtime.State.ReportEventAsync(fixture.Context.Context, overflow).ConfigureAwait(false);
            provider.Snapshot = new MachineToolNotificationSnapshot
            {
                Revision = 2,
                Messages = [message with { Sequence = 2, Identifier = "M2" }]
            };
            await runtime.RefreshNotificationsAsync().ConfigureAwait(false);
            MachineToolEventObservation observed = await next.ConfigureAwait(false);
            Assert.That(decoded, Is.EqualTo(1));
            Assert.That(((NotificationEventTypeRecord)observed.Events[0].Event).Identifier, Is.EqualTo("M2"));
            Assert.That(observed.DroppedNotifications, Is.EqualTo(1));
            Assert.That(observed.RefreshCompleted, Is.False);
            Assert.That(observed.LimitReached, Is.True);
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// A retained refresh reports truncation instead of claiming a complete one-condition inventory.
        /// </summary>
        [Test]
        public async Task RetainedConditionCountLimitDoesNotClaimCompletenessAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolAlert alert = MachineToolNotificationValidationTests.Alert();
            var provider = new MachineToolNotificationTests.NotificationProvider(
                new MachineToolNotificationSnapshot { Alerts = [alert, alert with { Key = "Second" }] });
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolNotificationTests.BuildAsync(fixture, provider).ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            transport.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            await transport.LoadClientConfigurationAsync(fixture.Root, "TruncatedConditions").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using MachineToolClient client = session.CreateMachineToolClient(telemetry);

            MachineToolEventObservation result = await client.ObserveConditionsAsync(runtime.NodeId,
                new MachineToolObservationOptions { Window = TimeSpan.FromSeconds(10), MaximumNotifications = 1 })
                .ConfigureAwait(false);

            Assert.That(result.Events.Count, Is.EqualTo(1));
            Assert.That(result.RefreshCompleted, Is.False);
            Assert.That(result.LimitReached, Is.True);
            Assert.That(result.DroppedNotifications, Is.GreaterThanOrEqualTo(1));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Caller cancellation and facade disposal both join subscription deletion and preserve the borrowed session.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task EventObservationCancellationReleasesOnlyItsOwnedSubscriptionAsync(bool disposeFacade)
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var provider = new MachineToolNotificationTests.NotificationProvider(new MachineToolNotificationSnapshot());
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolNotificationTests.BuildAsync(fixture, provider).ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            transport.UseSubscriptionEngineFactory(DefaultSubscriptionEngineFactory.Instance);
            await transport.LoadClientConfigurationAsync(fixture.Root, "CancelledEvents").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
            Assert.That(session.TryGetSubscriptionManager(out ISubscriptionManager? subscriptions), Is.True);
            int before = subscriptions!.Count;
            using var cancellation = new CancellationTokenSource();
            Task<MachineToolEventObservation> pending = client.ObserveEventsAsync(runtime.NodeId,
                new MachineToolObservationOptions { Window = TimeSpan.FromSeconds(30) }, cancellation.Token).AsTask();
            await WaitForMonitoringAsync(runtime.State).ConfigureAwait(false);
            if (disposeFacade)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            await Assert.ThatAsync(() => pending,
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(subscriptions.Count, Is.EqualTo(before));
            Assert.That(session.Connected, Is.True);
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Waits on the real server's public event-monitoring state, not a guessed subscription setup delay.
        /// </summary>
        private static async Task WaitForMonitoringAsync(NodeState node)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!node.AreEventsMonitored)
            {
                await Task.Delay(20, timeout.Token).ConfigureAwait(false);
            }
        }
    }
}
