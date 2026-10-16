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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Alarms;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises provider cancellation, callback reentrancy, per-instance disposal and standard method permissions.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("MachineTool")]
    public sealed class MachineToolNotificationLifetimeTests
    {
        /// <summary>
        /// Runtime removal cancels and drains only its own admitted notification acquisition.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationAndRemovalDrainAcquisitionWithoutChangingOtherMachinesAsync(bool remove)
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            int reads = 0;
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IMachineToolNotificationProvider>(MockBehavior.Strict);
            provider.Setup(source => source.ReadAsync(It.IsAny<ulong>(), It.IsAny<CancellationToken>()))
                .Returns((ulong _, CancellationToken ct) => ReadAsync(ct));
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolNotificationTests.BuildAsync(fixture, provider.Object, "Blocking")
                    .ConfigureAwait(false);
            var otherProvider = new MachineToolNotificationTests.NotificationProvider(
                new MachineToolNotificationSnapshot());
            await using MachineToolRuntime<MachineToolState> other =
                await MachineToolNotificationTests.BuildAsync(fixture, otherProvider, "Independent")
                    .ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            Task pending = runtime.RefreshNotificationsAsync(cancellation.Token).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (remove)
            {
                await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            else
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
            }
            await Assert.ThatAsync(async () => await pending.ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            await other.RefreshNotificationsAsync().ConfigureAwait(false);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(other.NodeId), Is.SameAs(other.State));
            if (remove)
            {
                Assert.That(fixture.Context.Manager.FindPredefinedNode(runtime.NodeId), Is.Null);
                Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await runtime.RefreshNotificationsAsync().ConfigureAwait(false));
            }
            else
            {
                Assert.That(fixture.Context.Manager.FindPredefinedNode(runtime.NodeId), Is.SameAs(runtime.State));
                Assert.That(runtime.Notifications!.Observation!.Revision, Is.Zero);
            }

            async ValueTask<MachineToolNotificationSnapshot> ReadAsync(CancellationToken ct)
            {
                if (Interlocked.Increment(ref reads) != 1)
                {
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                }
                return new MachineToolNotificationSnapshot();
            }
        }

        /// <summary>
        /// Provider callbacks cannot recursively update or await disposal of their own runtime.
        /// </summary>
        [Test]
        public async Task ReentrantProviderOperationsFailPromptlyWithoutDeadlockAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolRuntime<MachineToolState>? runtime = null;
            var provider = new Mock<IMachineToolNotificationProvider>(MockBehavior.Strict);
            provider.Setup(source => source.ReadAsync(It.IsAny<ulong>(), It.IsAny<CancellationToken>()))
                .Returns((ulong _, CancellationToken _) => ReadAsync());
            runtime = await MachineToolNotificationTests.BuildAsync(fixture, provider.Object).ConfigureAwait(false);
            await using (runtime.ConfigureAwait(false))
            {
                await runtime.RefreshNotificationsAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
            }

            async ValueTask<MachineToolNotificationSnapshot> ReadAsync()
            {
                if (runtime != null)
                {
                    await Assert.ThatAsync(async () => await runtime.DisposeAsync().ConfigureAwait(false),
                        Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                            .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);
                    await Assert.ThatAsync(async () => await runtime.RefreshAsync().ConfigureAwait(false),
                        Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                            .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);
                    await Assert.ThatAsync(
                        async () => await runtime.Notifications!.DisposeAsync().ConfigureAwait(false),
                        Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                            .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);
                }
                return new MachineToolNotificationSnapshot();
            }
        }

        /// <summary>
        /// Enable, disable, comment and acknowledge use existing Part 9 methods and the latest provider permissions.
        /// </summary>
        [Test]
        public async Task StandardConditionMethodsRespectPermissionsAndNeverResetEquipmentAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            MachineToolAlert alert = MachineToolNotificationValidationTests.Alert();
            var provider = new MachineToolNotificationTests.NotificationProvider(
                new MachineToolNotificationSnapshot { Alerts = [alert] });
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolNotificationTests.BuildAsync(fixture, provider).ConfigureAwait(false);
            Assert.That(runtime.Notifications!.TryGetAlert(alert.Key, out AlertState? state), Is.True);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(fixture.Root, "ConditionPermissions").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            var client = new AlarmClient(session, telemetry);
            await client.AddCommentAsync(state!.NodeId, state.EventId!.Value, new LocalizedText("Inspected"))
                .ConfigureAwait(false);
            Assert.That(state.Comment!.Value.Text, Is.EqualTo("Inspected"));
            await client.DisableAsync(state.NodeId).ConfigureAwait(false);
            Assert.That(state.EnabledState!.Id!.Value, Is.False);
            Assert.That(state.Retain!.Value, Is.False);
            await client.EnableAsync(state.NodeId).ConfigureAwait(false);
            Assert.That(state.Retain.Value, Is.True);
            provider.Snapshot = new MachineToolNotificationSnapshot
            {
                Revision = 1,
                Alerts = [alert with { AllowedOperations = MachineToolConditionOperations.None }]
            };
            await runtime.RefreshNotificationsAsync().ConfigureAwait(false);
            await Assert.ThatAsync(async () =>
                await client.AcknowledgeAsync(state.NodeId, state.EventId.Value, new LocalizedText("Denied"))
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadUserAccessDenied)).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await client.DisableAsync(state.NodeId).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadUserAccessDenied)).ConfigureAwait(false);
            await Assert.ThatAsync(async () =>
                await client.AddCommentAsync(state.NodeId, state.EventId.Value, new LocalizedText("Denied"))
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadUserAccessDenied)).ConfigureAwait(false);
            Assert.That(state.ActiveState!.Id!.Value, Is.True);
            Assert.That(state.AckedState!.Id!.Value, Is.False);
            Assert.That(state.Comment.Value.Text, Is.EqualTo("Inspected"));
            Assert.That(state.Confirm, Is.Null);
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
