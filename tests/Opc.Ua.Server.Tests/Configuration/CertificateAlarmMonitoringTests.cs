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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Deterministic tests for the OPC 10000-12 §7.8.3 certificate-group
    /// alarms (<c>CertificateExpired</c> and <c>TrustListOutOfDate</c>) driven
    /// by <see cref="CertificateGroupAlarmMonitor"/>. Every test uses a
    /// dedicated, isolated certificate group and a <see cref="FakeTimeProvider"/>
    /// so time is fully controlled and there is no cross-test state.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [Category("Alarms")]
    [NonParallelizable]
    [Parallelizable(ParallelScope.None)]
    public class CertificateAlarmMonitoringTests
    {
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();
        private static readonly DateTime s_now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        private ServerFixture<StandardServer> m_fixture = null!;
        private StandardServer m_server = null!;
        private ConfigurationNodeManager m_configManager = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
            m_configManager = (ConfigurationNodeManager)m_server.CurrentInstance.ConfigurationNodeManager;
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public void CertificateExpiredActivatesWithMediumSeverityWhenApproachingExpiry()
        {
            AlarmHarness harness = CreateHarness();
            CertificateExpirationAlarmState alarm = harness.Monitor.CertificateExpired!;

            // Expires in 7 days, inside the default 14-day limit -> approaching.
            SetExpiration(alarm, s_now.AddDays(7));
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);

            Assert.Multiple(() =>
            {
                Assert.That(alarm.ActiveState!.Id!.Value, Is.True, "alarm should be active");
                Assert.That(alarm.Severity!.Value, Is.EqualTo((ushort)EventSeverity.Medium));
                Assert.That(alarm.Retain!.Value, Is.True, "an active alarm must be retained");
                Assert.That(alarm.AckedState!.Id!.Value, Is.False, "activation resets acknowledgement");
                Assert.That(harness.CertificateExpiredEvents, Is.EqualTo(1));
                Assert.That(alarm.Message!.Value.Text, Does.Contain("will expire"));
            });
        }

        [Test]
        public void CertificateExpiredActivatesWithHighSeverityWhenAlreadyExpired()
        {
            AlarmHarness harness = CreateHarness();
            CertificateExpirationAlarmState alarm = harness.Monitor.CertificateExpired!;

            SetExpiration(alarm, s_now.AddDays(-1));
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);

            Assert.Multiple(() =>
            {
                Assert.That(alarm.ActiveState!.Id!.Value, Is.True);
                Assert.That(alarm.Severity!.Value, Is.EqualTo((ushort)EventSeverity.High));
                Assert.That(alarm.Message!.Value.Text, Does.Contain("has expired"));
                Assert.That(harness.CertificateExpiredEvents, Is.EqualTo(1));
            });
        }

        [Test]
        public void CertificateExpiredStaysInactiveWhenBeyondLimit()
        {
            AlarmHarness harness = CreateHarness();
            CertificateExpirationAlarmState alarm = harness.Monitor.CertificateExpired!;

            // Expires in 100 days, well beyond the 14-day limit.
            SetExpiration(alarm, s_now.AddDays(100));
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);

            Assert.Multiple(() =>
            {
                Assert.That(alarm.ActiveState!.Id!.Value, Is.False);
                Assert.That(alarm.Retain!.Value, Is.False);
                Assert.That(harness.CertificateExpiredEvents, Is.Zero,
                    "an alarm that never activates must not emit an event");
            });
        }

        [Test]
        public void CertificateExpiredHonoursConfiguredExpirationLimit()
        {
            AlarmHarness harness = CreateHarness();
            CertificateExpirationAlarmState alarm = harness.Monitor.CertificateExpired!;

            // Shrink the limit to one day; a certificate that expires in 7 days
            // is now outside the (smaller) alarm window.
            alarm.ExpirationLimit!.Value = TimeSpan.FromDays(1).TotalMilliseconds;
            SetExpiration(alarm, s_now.AddDays(7));
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);

            Assert.That(alarm.ActiveState!.Id!.Value, Is.False);
        }

        [Test]
        public void CertificateExpiredDeactivatesAfterCertificateReplacement()
        {
            AlarmHarness harness = CreateHarness();
            CertificateExpirationAlarmState alarm = harness.Monitor.CertificateExpired!;

            SetExpiration(alarm, s_now.AddDays(-1));
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);
            Assert.That(alarm.ActiveState!.Id!.Value, Is.True);

            // Replace the certificate with one that is valid for a long time.
            SetExpiration(alarm, s_now.AddDays(365));
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);

            Assert.Multiple(() =>
            {
                Assert.That(alarm.ActiveState.Id!.Value, Is.False, "renewal deactivates the alarm");
                Assert.That(harness.CertificateExpiredEvents, Is.EqualTo(2), "activate + deactivate");
                Assert.That(alarm.Retain!.Value, Is.True,
                    "still retained because it was never acknowledged");
            });
        }

        [Test]
        public void RepeatedEvaluationsDoNotEmitDuplicateTransitionEvents()
        {
            AlarmHarness harness = CreateHarness();
            CertificateExpirationAlarmState alarm = harness.Monitor.CertificateExpired!;

            SetExpiration(alarm, s_now.AddDays(-1));
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);

            // Three further ticks observe the same active state.
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);

            Assert.That(harness.CertificateExpiredEvents, Is.EqualTo(1),
                "a stable state must not re-emit transition events");
        }

        [Test]
        public void AcknowledgedAlarmClearsRetainAfterDeactivation()
        {
            AlarmHarness harness = CreateHarness();
            CertificateExpirationAlarmState alarm = harness.Monitor.CertificateExpired!;

            SetExpiration(alarm, s_now.AddDays(-1));
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);
            Assert.That(alarm.Retain!.Value, Is.True);
            Assert.That(alarm.AckedState!.Id!.Value, Is.False);

            // Operator acknowledges the active alarm; it stays retained while active.
            alarm.SetAcknowledgedState(harness.Context, acknowledged: true);
            Assert.That(alarm.Retain.Value, Is.True, "acknowledged but still active -> retained");

            // Certificate renewed -> alarm deactivates and, being acknowledged,
            // is no longer retained.
            SetExpiration(alarm, s_now.AddDays(365));
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);

            Assert.Multiple(() =>
            {
                Assert.That(alarm.ActiveState!.Id!.Value, Is.False);
                Assert.That(alarm.AckedState.Id!.Value, Is.True);
                Assert.That(alarm.Retain.Value, Is.False, "inactive + acknowledged -> not retained");
            });
        }

        [Test]
        public void AcknowledgementStateMachineFollowsGeneratedAlarmModel()
        {
            AlarmHarness harness = CreateHarness();
            CertificateExpirationAlarmState alarm = harness.Monitor.CertificateExpired!;

            // The generated alarm model exposes the Acknowledge method and the
            // AckedState two-state variable required by OPC 10000-9.
            Assert.Multiple(() =>
            {
                Assert.That(alarm.Acknowledge, Is.Not.Null, "generated model provides Acknowledge");
                Assert.That(alarm.AckedState, Is.Not.Null);
            });

            SetExpiration(alarm, s_now.AddDays(-1));
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);
            Assert.That(alarm.AckedState!.Id!.Value, Is.False, "activation clears acknowledgement");

            // Acknowledging the active alarm flips AckedState as the wired
            // Acknowledge method does through SetAcknowledgedState.
            alarm.SetAcknowledgedState(harness.Context, acknowledged: true);
            Assert.That(alarm.AckedState.Id!.Value, Is.True);
        }

        [Test]
        public void ClientAcknowledgeMethodMarksAlarmAcknowledged()
        {
            AlarmHarness harness = CreateHarness();
            CertificateExpirationAlarmState alarm = harness.Monitor.CertificateExpired!;

            SetExpiration(alarm, s_now.AddDays(-1));
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);
            Assert.That(alarm.AckedState!.Id!.Value, Is.False);

            // Invoke the generated, wired Acknowledge method handler exactly as
            // the server routes a client Acknowledge call.
            ByteString eventId = alarm.EventId!.Value;
            ServiceResult result = alarm.Acknowledge!.OnCall!(
                harness.Context,
                alarm.Acknowledge,
                alarm.NodeId,
                eventId,
                LocalizedText.From("acknowledged by test"));

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(alarm.AckedState.Id!.Value, Is.True);
            });
        }

        [Test]
        public void TransitionTimestampsUseInjectedTimeProvider()
        {
            AlarmHarness harness = CreateHarness();
            CertificateExpirationAlarmState alarm = harness.Monitor.CertificateExpired!;

            SetExpiration(alarm, s_now.AddDays(-1));
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);

            Assert.Multiple(() =>
            {
                Assert.That((DateTime)alarm.Time!.Value, Is.EqualTo(s_now));
                Assert.That((DateTime)alarm.ReceiveTime!.Value, Is.EqualTo(s_now));
                Assert.That(alarm.SourceNode!.Value, Is.EqualTo(harness.Group.NodeId));
                Assert.That(alarm.EventType!.Value,
                    Is.EqualTo((NodeId)ObjectTypeIds.CertificateExpirationAlarmType));
                Assert.That(alarm.ConditionName!.Value, Is.EqualTo(BrowseNames.CertificateExpired));
            });
        }

        [Test]
        public void QuietEvaluationDoesNotEmitEventsBeforeInfrastructureReady()
        {
            AlarmHarness harness = CreateHarness();
            CertificateExpirationAlarmState alarm = harness.Monitor.CertificateExpired!;

            // Certificate is already expired but monitoring has not started, so
            // the evaluation must set the state without reporting an event.
            SetExpiration(alarm, s_now.AddDays(-1));
            harness.Monitor.Evaluate(harness.Context, emitEvents: false);

            Assert.Multiple(() =>
            {
                Assert.That(alarm.ActiveState!.Id!.Value, Is.True, "state is still updated");
                Assert.That(harness.CertificateExpiredEvents, Is.Zero,
                    "no event before the subscription infrastructure is ready");
            });
        }

        [Test]
        public void TrustListOutOfDateActivatesWhenStale()
        {
            AlarmHarness harness = CreateHarness();
            TrustListOutOfDateAlarmState alarm = harness.Monitor.TrustListOutOfDate!;

            // Last updated two hours ago with an expected one-hour update cadence.
            harness.Monitor.SetTrustListStatus(
                harness.Context,
                new NodeId(1),
                s_now.AddHours(-2),
                TimeSpan.FromHours(1).TotalMilliseconds);
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);

            Assert.Multiple(() =>
            {
                Assert.That(alarm.ActiveState!.Id!.Value, Is.True);
                Assert.That(alarm.Retain!.Value, Is.True);
                Assert.That(harness.TrustListEvents, Is.EqualTo(1));
                Assert.That(alarm.Message!.Value.Text, Does.Contain("not been updated"));
            });
        }

        [Test]
        public void TrustListOutOfDateStaysInactiveWhenFresh()
        {
            AlarmHarness harness = CreateHarness();
            TrustListOutOfDateAlarmState alarm = harness.Monitor.TrustListOutOfDate!;

            harness.Monitor.SetTrustListStatus(
                harness.Context,
                new NodeId(1),
                s_now.AddMinutes(-5),
                TimeSpan.FromHours(1).TotalMilliseconds);
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);

            Assert.Multiple(() =>
            {
                Assert.That(alarm.ActiveState!.Id!.Value, Is.False);
                Assert.That(harness.TrustListEvents, Is.Zero);
            });
        }

        [Test]
        public void TrustListOutOfDateDisabledWhenUpdateFrequencyIsZero()
        {
            AlarmHarness harness = CreateHarness();
            TrustListOutOfDateAlarmState alarm = harness.Monitor.TrustListOutOfDate!;

            // A non-positive update frequency disables the staleness check even
            // for a very old last-update time.
            harness.Monitor.SetTrustListStatus(
                harness.Context,
                new NodeId(1),
                s_now.AddYears(-1),
                updateFrequencyMs: 0);
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);

            Assert.Multiple(() =>
            {
                Assert.That(alarm.ActiveState!.Id!.Value, Is.False);
                Assert.That(harness.TrustListEvents, Is.Zero);
            });
        }

        [Test]
        public void ServerStartsAlarmMonitoringOnStartup()
        {
            // StandardServer.OnServerStarted wires StartAlarmMonitoring once the
            // subscription/event infrastructure is ready.
            Assert.Multiple(() =>
            {
                Assert.That(m_configManager.AlarmMonitoringActive, Is.True);
                Assert.That(m_configManager.AlarmMonitors, Is.Not.Empty);
            });
        }

        [Test]
        public void StartAlarmMonitoringUsesInjectedTimeProviderAndStops()
        {
            var time = new FakeTimeProvider(new DateTimeOffset(s_now, TimeSpan.Zero));
            using var manager = new ConfigurationNodeManager(
                m_server.CurrentInstance,
                m_fixture.Config,
                s_telemetry.CreateLogger<ConfigurationNodeManager>(),
                time);

            Assert.That(manager.AlarmMonitoringActive, Is.False);

            manager.StartAlarmMonitoring(TimeSpan.FromSeconds(30));
            Assert.That(manager.AlarmMonitoringActive, Is.True);

            // Firing the injected timer must not throw (no monitors on this
            // stand-alone manager, but the callback path is exercised).
            Assert.DoesNotThrow(() => time.Advance(TimeSpan.FromSeconds(30)));

            // Starting again while running is a no-op.
            Assert.DoesNotThrow(() => manager.StartAlarmMonitoring(TimeSpan.FromSeconds(30)));

            manager.StopAlarmMonitoring();
            Assert.That(manager.AlarmMonitoringActive, Is.False);

            // Stopping again is safe.
            Assert.DoesNotThrow(() => manager.StopAlarmMonitoring());
        }

        [Test]
        public void CertificateExpiredEscalatesFromMediumToHighAtExpirationBoundary()
        {
            AlarmHarness harness = CreateHarness();
            CertificateExpirationAlarmState alarm = harness.Monitor.CertificateExpired!;

            // Expires in 7 days -> inside the default 14-day limit -> approaching
            // expiry, so the alarm activates at Medium severity.
            DateTime expiration = s_now.AddDays(7);
            SetExpiration(alarm, expiration);
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);

            Assert.Multiple(() =>
            {
                Assert.That(alarm.ActiveState!.Id!.Value, Is.True);
                Assert.That(alarm.Severity!.Value, Is.EqualTo((ushort)EventSeverity.Medium));
                Assert.That(alarm.Message!.Value.Text, Does.Contain("will expire"));
                Assert.That(harness.CertificateExpiredEvents, Is.EqualTo(1));
            });

            // Operator acknowledges the active alarm before it escalates.
            alarm.SetAcknowledgedState(harness.Context, acknowledged: true);
            Assert.That(alarm.AckedState!.Id!.Value, Is.True);

            // Advance deterministic time just past the expiration boundary. The
            // alarm stays active (ActiveState remains true) but must escalate
            // Medium -> High, refresh its Message, and emit exactly one further
            // reportable state-change event.
            harness.Time.Advance(TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1));
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);

            Assert.Multiple(() =>
            {
                Assert.That(alarm.ActiveState!.Id!.Value, Is.True, "still active across the boundary");
                Assert.That(alarm.Severity!.Value, Is.EqualTo((ushort)EventSeverity.High));
                Assert.That(alarm.Message!.Value.Text, Does.Contain("has expired"));
                Assert.That(harness.CertificateExpiredEvents, Is.EqualTo(2),
                    "one additional escalation event on the Medium -> High crossing");
                Assert.That(alarm.AckedState!.Id!.Value, Is.True,
                    "a severity escalation on an already-active alarm keeps the acknowledgement");
            });

            // Further ticks in the same expired (High) state must deduplicate.
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);
            harness.Monitor.Evaluate(harness.Context, emitEvents: true);
            Assert.That(harness.CertificateExpiredEvents, Is.EqualTo(2),
                "a stable High state must not re-emit transition events");
        }

        [Test]
        public void ConcurrentEvaluationStartAndStopIsRaceFree()
        {
            var time = new FakeTimeProvider(new DateTimeOffset(s_now, TimeSpan.Zero));
            using var manager = new ConfigurationNodeManager(
                m_server.CurrentInstance,
                m_fixture.Config,
                s_telemetry.CreateLogger<ConfigurationNodeManager>(),
                time);

            manager.StartAlarmMonitoring(TimeSpan.FromSeconds(30));

            ISystemContext context = manager.SystemContext;
            Exception? failure = null;
            using var release = new ManualResetEventSlim(false);

            // Several worker threads hammer the serialized refresh + evaluation
            // path while the main thread interleaves start/stop transitions. The
            // evaluation lock must serialize every mutation so nothing throws and
            // the timer is never disposed from under an in-flight evaluation.
            var workers = new Task[6];
            for (int i = 0; i < workers.Length; i++)
            {
                workers[i] = Task.Run(() =>
                {
                    try
                    {
                        for (int n = 0; n < 500; n++)
                        {
                            manager.UpdateAndEvaluateAlarms(context, emitEvents: true);
                        }

                        release.Wait(TimeSpan.FromSeconds(10));
                        manager.UpdateAndEvaluateAlarms(context, emitEvents: true);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref failure, ex, null);
                    }
                });
            }

            for (int r = 0; r < 30; r++)
            {
                manager.StopAlarmMonitoring();
                manager.StartAlarmMonitoring(TimeSpan.FromSeconds(30));
            }

            manager.StopAlarmMonitoring();
            release.Set();

            Assert.DoesNotThrow(() => Task.WaitAll(workers));
            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Null, "concurrent evaluate/start/stop must not throw");
                Assert.That(manager.AlarmMonitoringActive, Is.False,
                    "the final Stop wins and leaves monitoring inactive");
            });
        }

        [Test]
        public async Task ServerShutdownStopsAlarmMonitoringAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
            var manager = (ConfigurationNodeManager)server.CurrentInstance.ConfigurationNodeManager;

            Assert.That(manager.AlarmMonitoringActive, Is.True,
                "monitoring is active while the server is running");

            // The async shutdown drives DeleteAddressSpaceAsync -> StopAlarmMonitoring
            // before the address space is torn down, guaranteeing no in-flight
            // evaluation mutates a disposed node afterwards.
            await fixture.StopAsync().ConfigureAwait(false);

            Assert.That(manager.AlarmMonitoringActive, Is.False,
                "server shutdown must stop alarm monitoring");
        }

        private AlarmHarness CreateHarness()
        {
            var time = new FakeTimeProvider(new DateTimeOffset(s_now, TimeSpan.Zero));
            ISystemContext context = m_configManager.SystemContext;

            var group = new CertificateGroupState(null);
            group.Create(
                context,
                new NodeId(Guid.NewGuid(), 3),
                new QualifiedName("TestGroup", 3),
                new LocalizedText("TestGroup"),
                true);

            group.AddCertificateExpired(context);
            CertificateExpirationAlarmState certificateExpired = group.CertificateExpired!;
            WireConditionMethods(context, certificateExpired);
            certificateExpired.AddExpirationLimit(context);

            group.AddTrustListOutOfDate(context);
            WireConditionMethods(context, group.TrustListOutOfDate!);

            var monitor = new CertificateGroupAlarmMonitor(
                group,
                "TestGroup",
                time,
                s_telemetry.CreateLogger<CertificateAlarmMonitoringTests>());
            monitor.InitializeQuiet(context);

            var harness = new AlarmHarness(group, monitor, time, context);
            monitor.CertificateExpired!.OnReportEvent = (_, _, _) => harness.CertificateExpiredEvents++;
            monitor.TrustListOutOfDate!.OnReportEvent = (_, _, _) => harness.TrustListEvents++;
            return harness;
        }

        // Mirrors ConfigurationNodeManager.WireConditionMethodHandlers: the
        // generated Add<Alarm> factory builds the full structure but does not
        // run OnAfterCreate, so re-run Create to wire the condition method
        // handlers (Acknowledge/Confirm/Enable/Disable/AddComment).
        private static void WireConditionMethods(ISystemContext context, NodeState alarm)
        {
            alarm.Create(
                context,
                alarm.NodeId,
                alarm.BrowseName,
                alarm.DisplayName,
                assignNodeIds: false);
        }

        private static void SetExpiration(CertificateExpirationAlarmState alarm, DateTime expiration)
        {
            alarm.ExpirationDate!.Value = (DateTimeUtc)DateTime.SpecifyKind(expiration, DateTimeKind.Utc);
        }

        private sealed class AlarmHarness
        {
            public AlarmHarness(
                CertificateGroupState group,
                CertificateGroupAlarmMonitor monitor,
                FakeTimeProvider time,
                ISystemContext context)
            {
                Group = group;
                Monitor = monitor;
                Time = time;
                Context = context;
            }

            public CertificateGroupState Group { get; }

            public CertificateGroupAlarmMonitor Monitor { get; }

            public FakeTimeProvider Time { get; }

            public ISystemContext Context { get; }

            public int CertificateExpiredEvents { get; set; }

            public int TrustListEvents { get; set; }
        }
    }
}
