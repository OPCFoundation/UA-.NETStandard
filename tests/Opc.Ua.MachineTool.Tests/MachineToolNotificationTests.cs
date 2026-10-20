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
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises the OPC 40501-1 sections 8.6 and 9 notification binding through its public builder.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [NonParallelizable]
    public sealed class MachineToolNotificationTests
    {
        /// <summary>
        /// Explicit notification selection must not publish an unusable Messages notifier or allocate a machine.
        /// </summary>
        [Test]
        public async Task MissingNotificationProviderFailsBeforeTheMachineFactoryAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            bool factoryCalled = false;
            MachineToolBuilder<MachineToolState> builder = fixture.Context.AddMachineTool(
                "Notifications", (system, parent, name) =>
                {
                    factoryCalled = true;
                    return system.CreateInstanceOfMachineToolType(parent, name);
                })
                .WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Notification tests");
                    identity.SerialNumber = "Notification-1";
                    identity.ProductInstanceUri = "urn:notifications:1";
                })
                .WithNotifications(new MachineToolNotificationOptions());

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await builder.BuildAsync().ConfigureAwait(false)))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(factoryCalled, Is.False);
        }

        /// <summary>
        /// Sections 8.6.2 and 9.1 retain cleared warnings until an explicit standard acknowledgement.
        /// </summary>
        [Test]
        public async Task AlertRetainsQualityAndTimeUntilExplicitWireAcknowledgementAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var acquired = new MachineToolSampleInfo(
                new DateTime(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc), StatusCodes.UncertainLastUsableValue);
            var alert = new MachineToolAlert
            {
                Key = "Lubricant",
                ErrorCode = "W17",
                Message = new LocalizedText("Low lubricant"),
                Severity = 450,
                IsActive = true,
                Sample = acquired
            };
            var provider = new NotificationProvider(new MachineToolNotificationSnapshot { Alerts = [alert] });
            await using MachineToolRuntime<MachineToolState> runtime =
                await BuildAsync(fixture, provider).ConfigureAwait(false);
            Assert.That(runtime.Notifications, Is.Not.Null);
            Assert.That(runtime.Notifications!.TryGetAlert("Lubricant", out AlertState? condition), Is.True);
            Assert.That(runtime.State.Notification!.Messages, Is.SameAs(runtime.Notifications.State));
            Assert.That(condition!.SourceNode!.Value, Is.EqualTo(runtime.Notifications.State.NodeId));
            Assert.That(condition.ErrorCode!.Value, Is.EqualTo("W17"));
            Assert.That(condition.Quality!.Value, Is.EqualTo(acquired.StatusCode));
            Assert.That(condition.Quality.SourceTimestamp!.Value, Is.EqualTo(acquired.SourceTimestamp));
            Assert.That(condition.Time!.Value, Is.EqualTo(acquired.SourceTimestamp));
            Assert.That(condition.ActiveState!.Id!.Value, Is.True);
            Assert.That(condition.AckedState!.Id!.Value, Is.False);
            Assert.That(condition.Retain!.Value, Is.True);

            provider.Snapshot = new MachineToolNotificationSnapshot
            {
                Revision = 1,
                Alerts = [alert with { IsActive = false }]
            };
            await runtime.RefreshNotificationsAsync().ConfigureAwait(false);
            Assert.That(condition.ActiveState.Id.Value, Is.False);
            Assert.That(condition.Retain.Value, Is.True, "A passive observation must not acknowledge an alarm.");

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var client = new ClientFixture(telemetry);
            await client.LoadClientConfigurationAsync(fixture.Root, "NotificationClient").ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            var alarms = new AlarmClient(session, telemetry);
            await alarms.AcknowledgeAsync(condition.NodeId, condition.EventId!.Value, new LocalizedText("Checked"))
                .ConfigureAwait(false);
            Assert.That(condition.AckedState.Id.Value, Is.True);
            Assert.That(condition.Retain.Value, Is.False);
            Assert.That(condition.Comment!.Value.Text, Is.EqualTo("Checked"));

            provider.Snapshot = new MachineToolNotificationSnapshot { Revision = 2 };
            NodeId removedId = condition.NodeId;
            await runtime.RefreshNotificationsAsync().ConfigureAwait(false);
            Assert.That(runtime.Notifications.TryGetAlert("Lubricant", out _), Is.False);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(removedId), Is.Null);
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Acknowledgement while active must retain the alert only until its provider reports the cause cleared.
        /// </summary>
        [Test]
        public async Task AcknowledgedActiveAlertStopsRetainingWhenProviderClearsItAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolAlert alert = MachineToolNotificationValidationTests.Alert();
            var provider = new NotificationProvider(new MachineToolNotificationSnapshot { Alerts = [alert] });
            await using MachineToolRuntime<MachineToolState> runtime =
                await BuildAsync(fixture, provider).ConfigureAwait(false);
            Assert.That(runtime.Notifications!.TryGetAlert(alert.Key, out AlertState? state), Is.True);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(fixture.Root, "AcknowledgeBeforeClear").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            var client = new AlarmClient(session, telemetry);
            await client.AcknowledgeAsync(state!.NodeId, state.EventId!.Value, new LocalizedText("Acknowledged"))
                .ConfigureAwait(false);
            Assert.That(state.ActiveState!.Id!.Value, Is.True);
            Assert.That(state.AckedState!.Id!.Value, Is.True);
            Assert.That(state.Retain!.Value, Is.True);
            provider.Snapshot = new MachineToolNotificationSnapshot
            {
                Revision = 1,
                Alerts = [alert with { IsActive = false }]
            };

            await runtime.RefreshNotificationsAsync().ConfigureAwait(false);

            Assert.That(state.ActiveState.Id.Value, Is.False);
            Assert.That(state.AckedState.Id.Value, Is.True);
            Assert.That(state.Retain.Value, Is.False,
                "Core SetActiveState must recompute retention; the provider must not copy the retain formula.");
            provider.Snapshot = new MachineToolNotificationSnapshot { Revision = 2 };
            await runtime.RefreshNotificationsAsync().ConfigureAwait(false);
            Assert.That(runtime.Notifications.TryGetAlert(alert.Key, out _), Is.False);
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// A companion prepare failure rejects generic writes too; accepted assignments run after generic commits.
        /// </summary>
        [Test]
        public async Task CompanionProjectionRejectsBeforeCommitAndSharesPublicationAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var initial = new MachineToolSnapshot
            {
                ActiveProgram = new MachineToolProgram { Name = "Initial" }
            };
            var provider = new InMemoryMachineToolProvider(initial);
            bool reject = false;
            ulong projectedRevision = ulong.MaxValue;
            string? programAtProjection = null;
            await using MachineToolRuntime<MachineToolState> runtime = await fixture.Context
                .AddMachineTool("Projection")
                .WithProvider(provider).WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Projection test");
                    identity.SerialNumber = "Projection";
                    identity.ProductInstanceUri = "urn:projection:1";
                })
                .WithObservationProjection((_, state, snapshot) =>
                {
                    if (reject)
                    {
                        throw new ArgumentException("Rejected companion observation.");
                    }
                    return () =>
                    {
                        projectedRevision = snapshot.Revision;
                        programAtProjection = state.Production!.ActiveProgram!.Name!.Value;
                    };
                }).BuildAsync().ConfigureAwait(false);
            Assert.That(projectedRevision, Is.Zero);
            Assert.That(programAtProjection, Is.EqualTo("Initial"));
            reject = true;
            await provider.SetSnapshotAsync(initial with
            {
                Revision = 1,
                ActiveProgram = new MachineToolProgram { Name = "Rejected" },
                Operation = new MachineToolOperation { Mode = MachineOperationMode.Automatic }
            }).ConfigureAwait(false);
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await runtime.RefreshAsync().ConfigureAwait(false));
            Assert.That(runtime.State.Production!.ActiveProgram!.Name!.Value, Is.EqualTo("Initial"));
            Assert.That(runtime.State.Monitoring!.MachineTool!.OperationMode!.Value,
                Is.EqualTo(MachineOperationMode.Manual));
            Assert.That(projectedRevision, Is.Zero);
            reject = false;
            await provider.SetSnapshotAsync(initial with
            {
                Revision = 2,
                ActiveProgram = new MachineToolProgram { Name = "Accepted" }
            }).ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            Assert.That(projectedRevision, Is.EqualTo(2));
            Assert.That(programAtProjection, Is.EqualTo("Accepted"));
        }

        /// <summary>
        /// Builds one notification-selected machine through the real shared owner.
        /// </summary>
        internal static ValueTask<MachineToolRuntime<MachineToolState>> BuildAsync(
            MachineToolRuntimeFixture fixture, IMachineToolNotificationProvider provider, string name = "Messages")
        {
            return fixture.Context.AddMachineTool(name)
                .WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Notification tests");
                    identity.SerialNumber = name;
                    identity.ProductInstanceUri = "urn:notifications:" + name;
                })
                .WithNotifications(new MachineToolNotificationOptions { Provider = provider })
                .BuildAsync();
        }

        /// <summary>
        /// Supplies test observations without introducing a parallel condition implementation.
        /// </summary>
        internal sealed class NotificationProvider(MachineToolNotificationSnapshot snapshot)
            : IMachineToolNotificationProvider
        {
            /// <summary>
            /// Gets or sets the provider's complete alert inventory and pending messages.
            /// </summary>
            public MachineToolNotificationSnapshot Snapshot { get; set; } = snapshot;

            /// <inheritdoc/>
            public ValueTask<MachineToolNotificationSnapshot> ReadAsync(
                ulong afterSequence, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<MachineToolNotificationSnapshot>(Snapshot);
            }
        }
    }
}
