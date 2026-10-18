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
using Opc.Ua.MachineTool.Server;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies provider cancellation, competing callers, peripheral isolation and asynchronous ownership drainage.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class WmtpLifetimeTests
    {
        /// <summary>
        /// Local cancellation preserves both settings, frees admission and never invokes Abort authorization.
        /// </summary>
        [Test]
        public async Task CancelledConfigurationPreservesSettingsAndDoesNotAbortOtherWorkAsync()
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            equipment.Samples["Temperature"] = equipment.Sample(Variant.From(5d));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            equipment.Measurement.Setup(provider => provider.ConfigureAsync(
                    It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
                .Returns(async (string _, uint _, Variant _, CancellationToken token) =>
                {
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                    return StatusCodes.Good;
                });
            await using WmtpPeripheral peripheral = await equipment.Builder(fixture.Context)
                .AddMeasurement(WmtpTestEquipment.Definition()).BuildAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            Task configure = peripheral.Measurements[0].SetTriggerAsync(
                25, new WmtpRequest { SessionId = new NodeId("first", 1) }, cancellation.Token).AsTask();
            await entered.Task.ConfigureAwait(false);
            ServiceResultException busy = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peripheral.Measurements[0].SetDeltaAsync(
                    Variant.From(2d), new WmtpRequest { SessionId = new NodeId("second", 1) }).ConfigureAwait(false))!;
            Assert.That(busy.StatusCode, Is.EqualTo(StatusCodes.BadServerTooBusy));
            cancellation.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await configure.ConfigureAwait(false));
            Assert.That(peripheral.Measurements[0].TriggerMilliseconds, Is.EqualTo(1000));
            Assert.That(peripheral.Measurements[0].State.DeltaCondition!.Value, Is.Zero);
            equipment.Authorization.Verify(provider => provider.AuthorizeAsync(
                It.IsAny<WmtpRequest>(), WmtpOperation.Abort, It.IsAny<CancellationToken>()), Times.Never);
            await peripheral.RefreshAsync().ConfigureAwait(false);
            Assert.That(peripheral.Measurements[0].Sample.WrappedValue.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(5d));
        }

        /// <summary>
        /// Both dataset Abort methods target the same peripheral operation.
        /// Authorization policy and other peripherals isolate callers.
        /// </summary>
        [Test]
        public async Task AbortIsPeripheralScopedAuthorizedAndNeverAffectsAnotherPeripheralAsync()
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            NodeId firstCaller = new("caller-a", 1);
            NodeId otherCaller = new("caller-b", 1);
            equipment.Authorization.Setup(provider => provider.AuthorizeAsync(
                    It.IsAny<WmtpRequest>(), WmtpOperation.Abort, It.IsAny<CancellationToken>()))
                .Returns((WmtpRequest request, WmtpOperation _, CancellationToken _) =>
                    new ValueTask<StatusCode>(request.SessionId == request.ActiveSessionId ?
                        StatusCodes.Good : StatusCodes.BadUserAccessDenied));
            var waiting = new Mock<IWmtpRecordStore>(MockBehavior.Strict);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting.Setup(store => store.ReadAsync(It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()))
                .Returns(async (WmtpRecordQuery _, CancellationToken token) =>
                {
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                    return ArrayOf<WMTPOutputDataType>.Empty;
                });
            await using var independent = new InMemoryWmtpRecordStore(NUnitTelemetryContext.Create());
            await independent.AppendAsync(WmtpRecordStoreTests.Record(3)).ConfigureAwait(false);
            await using WmtpPeripheral first = await equipment.Builder(fixture.Context, "First")
                .WithWorkRecords(waiting.Object).BuildAsync().ConfigureAwait(false);
            await using WmtpPeripheral second = await equipment.Builder(fixture.Context, "Second")
                .WithWorkRecords(independent).BuildAsync().ConfigureAwait(false);
            Task<ArrayOf<WMTPOutputDataType>> pending = first.WorkRecords!.ReadAsync(
                new WmtpRecordQuery(), new WmtpRequest { SessionId = firstCaller }).AsTask();
            await entered.Task.ConfigureAwait(false);
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await first.AbortAsync(new WmtpRequest { SessionId = otherCaller }).ConfigureAwait(false))!;
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(pending.IsCompleted, Is.False);
            Assert.That(await second.WorkRecords!.CountAsync(
                new WmtpRecordQuery(), new WmtpRequest { SessionId = otherCaller }).ConfigureAwait(false),
                Is.EqualTo(1));
            await first.AbortAsync(new WmtpRequest { SessionId = firstCaller }).ConfigureAwait(false);
            ServiceResultException aborted = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await pending.ConfigureAwait(false))!;
            Assert.That(aborted.StatusCode, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(await second.WorkRecords.CountAsync(new WmtpRecordQuery(), new WmtpRequest())
                .ConfigureAwait(false), Is.EqualTo(1));
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await second.WorkRecords.CountAsync(new WmtpRecordQuery(),
                    new WmtpRequest { PeripheralId = first.NodeId }).ConfigureAwait(false));
        }

        /// <summary>
        /// Every disposal caller joins provider cancellation and the asynchronous owned-resource cleanup.
        /// </summary>
        [Test]
        public async Task ConcurrentDisposalDrainsProviderAndOwnedResourcesExactlyOnceAsync()
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancellationSeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var providerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resourceEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resourceRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new Mock<IWmtpRecordStore>(MockBehavior.Strict);
            store.Setup(provider => provider.CountAsync(It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()))
                .Returns(async (WmtpRecordQuery _, CancellationToken token) =>
                {
                    using CancellationTokenRegistration registration = token.Register(() =>
                        cancellationSeen.TrySetResult(true));
                    entered.TrySetResult(true);
                    await providerRelease.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return 0u;
                });
            var resource = new Mock<IAsyncDisposable>(MockBehavior.Strict);
            resource.Setup(owned => owned.DisposeAsync()).Returns(async () =>
            {
                resourceEntered.TrySetResult(true);
                await resourceRelease.Task.ConfigureAwait(false);
            });
            WmtpPeripheral peripheral = await equipment.Builder(fixture.Context).WithWorkRecords(store.Object)
                .WithOwnedResource(resource.Object).BuildAsync().ConfigureAwait(false);
            Task<uint> pending = peripheral.WorkRecords!.CountAsync(new WmtpRecordQuery(), new WmtpRequest()).AsTask();
            await entered.Task.ConfigureAwait(false);
            Task dispose = peripheral.DisposeAsync().AsTask();
            await cancellationSeen.Task.ConfigureAwait(false);
            bool providerPending = !dispose.IsCompleted;
            providerRelease.TrySetResult(true);
            Assert.CatchAsync<OperationCanceledException>(async () => await pending.ConfigureAwait(false));
            await resourceEntered.Task.ConfigureAwait(false);
            Task repeated = peripheral.DisposeAsync().AsTask();
            bool resourcePending = !repeated.IsCompleted;
            resourceRelease.TrySetResult(true);
            await Task.WhenAll(dispose, repeated).ConfigureAwait(false);
            Assert.That(providerPending, Is.True);
            Assert.That(resourcePending, Is.True);
            resource.Verify(owned => owned.DisposeAsync(), Times.Once);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(peripheral.NodeId), Is.Null);
            Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await peripheral.RefreshAsync().ConfigureAwait(false));
        }

        /// <summary>
        /// Reentrant provider cleanup fails explicitly rather than deadlocking on its own operation.
        /// </summary>
        [Test]
        public async Task ProviderCanObserveReentrantDisposalRefusalWithoutHoldingTheAdmissionGateAsync()
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            equipment.Samples["Temperature"] = equipment.Sample(Variant.From(1d));
            await using WmtpPeripheral peripheral = await equipment.Builder(fixture.Context)
                .AddMeasurement(WmtpTestEquipment.Definition()).BuildAsync().ConfigureAwait(false);
            equipment.Measurement.Setup(provider => provider.ConfigureAsync(
                    It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<Variant>(), It.IsAny<CancellationToken>()))
                .Returns(async (string _, uint _, Variant _, CancellationToken _) =>
                {
                    ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await peripheral.DisposeAsync().ConfigureAwait(false))!;
                    Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                    await Task.Yield();
                    return StatusCodes.BadNotSupported;
                });
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await peripheral.Measurements[0].SetTriggerAsync(3, new WmtpRequest()).ConfigureAwait(false));
            Assert.That(peripheral.Measurements[0].TriggerMilliseconds, Is.EqualTo(1000));
        }

        /// <summary>
        /// A failing abort-cancellation callback cannot prevent primary cancellation or asynchronous resource cleanup.
        /// </summary>
        [Test]
        public async Task CancellationCallbackFailureStillCancelsPrimaryAndDrainsOwnedResourcesAsync()
        {
            await using var fixture = new WmtpTestFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var equipment = new WmtpTestEquipment();
            var primaryEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var primaryCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var primaryRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var abortEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var abortRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var store = new Mock<IWmtpRecordStore>(MockBehavior.Strict);
            store.Setup(provider => provider.CountAsync(It.IsAny<WmtpRecordQuery>(), It.IsAny<CancellationToken>()))
                .Returns(async (WmtpRecordQuery _, CancellationToken token) =>
                {
                    using CancellationTokenRegistration registration =
                        token.Register(() => primaryCancelled.TrySetResult(true));
                    primaryEntered.TrySetResult(true);
                    await primaryRelease.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return 0u;
                });
            equipment.Authorization.Setup(provider => provider.AuthorizeAsync(
                    It.IsAny<WmtpRequest>(), WmtpOperation.Abort, It.IsAny<CancellationToken>()))
                .Returns(async (WmtpRequest _, WmtpOperation _, CancellationToken token) =>
                {
                    using CancellationTokenRegistration registration =
                        token.Register(() => throw new InvalidOperationException("Cancellation callback failure"));
                    abortEntered.TrySetResult(true);
                    await abortRelease.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return StatusCodes.Good;
                });
            var resource = new Mock<IAsyncDisposable>(MockBehavior.Strict);
            resource.Setup(owned => owned.DisposeAsync()).Returns(default(ValueTask));
            WmtpPeripheral peripheral = await equipment.Builder(fixture.Context).WithWorkRecords(store.Object)
                .WithOwnedResource(resource.Object).BuildAsync().ConfigureAwait(false);
            Task<uint> primary = peripheral.WorkRecords!.CountAsync(new WmtpRecordQuery(), new WmtpRequest()).AsTask();
            await primaryEntered.Task.ConfigureAwait(false);
            Task abort = peripheral.AbortAsync(new WmtpRequest()).AsTask();
            await abortEntered.Task.ConfigureAwait(false);
            Task disposal = peripheral.DisposeAsync().AsTask();
            bool cancellationWasDelivered = primaryCancelled.Task.IsCompleted;
            primaryRelease.TrySetResult(true);
            abortRelease.TrySetResult(true);
            Assert.CatchAsync<OperationCanceledException>(async () => await primary.ConfigureAwait(false));
            Assert.CatchAsync<OperationCanceledException>(async () => await abort.ConfigureAwait(false));
            Assert.ThrowsAsync<AggregateException>(async () => await disposal.ConfigureAwait(false));
            Assert.That(cancellationWasDelivered, Is.True);
            resource.Verify(owned => owned.DisposeAsync(), Times.Once);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(peripheral.NodeId), Is.Null);
        }
    }
}
