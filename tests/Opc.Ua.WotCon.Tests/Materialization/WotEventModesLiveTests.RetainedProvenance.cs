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

#if NET8_0_OR_GREATER
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    /// <summary>
    /// Contains tests for WoT event modes live.
    /// </summary>
    public sealed partial class WotEventModesLiveTests
    {
        /// <summary>
        /// Verifies original provenance while an admitted action holds a retiring generation,
        /// and rejects its evidence after physical reclamation.
        /// </summary>
        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task NativeRetainedProvenanceKeepsItsPublishedGenerationAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            destination.Session.Factory.Builder.AddOpcUaWotCon().Commit();
            var calls = Channel.CreateBounded<NativeConditionCall>(8);
            var actionEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseAction = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            NodeId methodId = await InstallActionSourceAsync(source, calls.Writer, ct,
                beforeReturn: async token =>
                {
                    actionEntered.TrySetResult(true);
                    await releaseAction.Task.WaitAsync(token).ConfigureAwait(false);
                }).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            string method = NodeId.ToExpandedNodeId(methodId, source.Session.NamespaceUris).ToString();
            WotProjectionDocument document = ActionProjection(mode, source.EndpointUrl, method);
            Task retirement = Task.CompletedTask;
            INodeManagerLifecycle lifecycle = ObserveRetirement(destination.Server.NodeManagerLifecycle,
                result => retirement = result.Retirement);
            var host = new LifecycleWotProjectionHost(lifecycle,
                new WotProjectionBindingRuntimeFactory(new NativeChannels(source.Session)));
            WotProjectionHandle active = await host.AddAsync(document, ct).ConfigureAwait(false);
            NativeConditionEvents? retained = null;
            Task<CallMethodResult>? inFlightAction = null;
            try
            {
                uint producingGeneration = checked((uint)active.Generation);
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                NodeId owner = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
                retained = await NativeConditionEvents.OpenAsync(destination.Session, ct, owner).ConfigureAwait(false);
                ByteString originalId = ByteString.From(new byte[] { 0xD3, 0xF1, 0x01, 0x01 });
                await ReportConditionAsync(source, "ConditionA", "PumpA", true, true, originalId, ct)
                    .ConfigureAwait(false);
                ArrayOf<Variant> fields = await retained.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(fields[0].TryGetValue(out ByteString eventId), Is.True);
                ushort metadata = checked((ushort)destination.Session.NamespaceUris.GetIndex(Namespaces.WotCon));
                NodeId folder = await FindNativeChildAsync(destination.Session, owner,
                    new QualifiedName("EventBindings", metadata), ct).ConfigureAwait(false);
                NodeId descriptor = await FindNativeChildAsync(destination.Session, folder,
                    new QualifiedName("condition", metadata), ct).ConfigureAwait(false);
                var client = new WoTEventBindingTypeClient(
                    destination.Session, descriptor, NUnitTelemetryContext.Create());
                WoTEventOriginDataType before = await client.GetEventProvenanceAsync(eventId, ct)
                    .ConfigureAwait(false);
                NodeId action = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Ack", destination.Session.NamespaceUris);
                inFlightAction = InvokeNativeActionAsync(destination.Session, owner, action, eventId,
                    new LocalizedText("Hold original generation provenance"), ct);
                await actionEntered.Task.WaitAsync(ct).ConfigureAwait(false);
                NativeConditionCall call = await calls.Reader.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(call.EventId, Is.EqualTo(originalId));
                Assert.That(call.SessionId, Is.EqualTo(source.Session.SessionId));
                active = await host.ShadowReloadAsync(active, document, ct).ConfigureAwait(false);
                Assert.That(active.Generation, Is.GreaterThan(producingGeneration));
                Assert.That(inFlightAction.IsCompleted, Is.False);
                Assert.That(retirement.IsCompleted, Is.False);
                WoTEventOriginDataType after = await client.GetEventProvenanceAsync(eventId, ct)
                    .ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(before.Generation, Is.EqualTo(producingGeneration));
                    Assert.That(after.Generation, Is.EqualTo(producingGeneration));
                    Assert.That(after.SourceEventId, Is.EqualTo(originalId));
                    Assert.That(after.SourceConditionId, Is.EqualTo(new ExpandedNodeId("ConditionA", SourceNamespace)));
                    Assert.That(after.SourceBranchId, Is.EqualTo(new ExpandedNodeId("Retained", SourceNamespace)));
                    Assert.That(after.ReceiveTime, Is.EqualTo(before.ReceiveTime));
                    Assert.That(after.EncodingMask, Is.EqualTo(0xFFU));
                }
                releaseAction.TrySetResult(true);
                CallMethodResult result = await inFlightAction.ConfigureAwait(false);
                Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
                await retirement.WaitAsync(ct).ConfigureAwait(false);
                ServiceResultException? reclaimed = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    _ = await client.GetEventProvenanceAsync(eventId, ct).ConfigureAwait(false);
                });
                Assert.That(reclaimed!.StatusCode, Is.EqualTo(StatusCodes.BadNoData));

                await retained.DisposeAsync().ConfigureAwait(false);
                retained = null;
                await using NativeConditionEvents current = await NativeConditionEvents.OpenAsync(
                    destination.Session, ct, owner).ConfigureAwait(false);
                folder = await FindNativeChildAsync(destination.Session, owner,
                    new QualifiedName("EventBindings", metadata), ct).ConfigureAwait(false);
                NodeId currentDescriptor = await FindNativeChildAsync(destination.Session, folder,
                    new QualifiedName("condition", metadata), ct).ConfigureAwait(false);
                Assert.That(currentDescriptor, Is.Not.EqualTo(descriptor));
                var currentClient = new WoTEventBindingTypeClient(
                    destination.Session, currentDescriptor, NUnitTelemetryContext.Create());
                ByteString currentSourceId = ByteString.From(new byte[] { 0xD3, 0xF1, 0x01, 0x02 });
                await ReportConditionAsync(source, "ConditionA", "PumpA", false, true, currentSourceId, ct)
                    .ConfigureAwait(false);
                ArrayOf<Variant> currentFields = await current.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(currentFields[0].TryGetValue(out ByteString currentEventId), Is.True);
                WoTEventOriginDataType currentOrigin = await currentClient.GetEventProvenanceAsync(currentEventId, ct)
                    .ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(currentOrigin.Generation, Is.EqualTo(checked((uint)active.Generation)));
                    Assert.That(currentOrigin.SourceEventId, Is.EqualTo(currentSourceId));
                }
                ServiceResultException? inherited = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    _ = await client.GetEventProvenanceAsync(currentEventId, ct).ConfigureAwait(false);
                });
                Assert.That(inherited!.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            }
            finally
            {
                releaseAction.TrySetResult(true);
                try
                {
                    if (inFlightAction is not null)
                    {
                        await inFlightAction.ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (retained is not null)
                        {
                            await retained.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        await host.RemoveAsync(active, cleanup.Token).ConfigureAwait(false);
                        calls.Writer.TryComplete();
                    }
                }
            }
        }

        /// <summary>
        /// Observes real committed retirement tasks without replacing preparation, publication, or disposal.
        /// </summary>
        private static INodeManagerLifecycle ObserveRetirement(
            INodeManagerLifecycle lifecycle,
            Action<NodeManagerBatchResult> committed)
        {
            var observed = new Mock<INodeManagerLifecycle>(MockBehavior.Strict);
            observed.SetupGet(owner => owner.Revision).Returns(() => lifecycle.Revision);
            observed.SetupGet(owner => owner.Slots).Returns(() => lifecycle.Slots);
            observed.SetupGet(owner => owner.Registrations).Returns(() => lifecycle.Registrations);
            observed.SetupGet(owner => owner.IsShuttingDown).Returns(() => lifecycle.IsShuttingDown);
            observed.SetupGet(owner => owner.SupportsPreparedPublication)
                .Returns(() => lifecycle.SupportsPreparedPublication);
            observed.SetupGet(owner => owner.SupportsPublicationIsolation)
                .Returns(() => lifecycle.SupportsPublicationIsolation);
            observed.Setup(owner => owner.CapturePublication()).Returns(lifecycle.CapturePublication);
            observed.Setup(owner => owner.PrepareAsync(
                It.IsAny<ArrayOf<NodeManagerBatchChange>>(), It.IsAny<CancellationToken>()))
                .Returns(async (ArrayOf<NodeManagerBatchChange> changes, CancellationToken token) =>
                {
                    IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(changes, token)
                        .ConfigureAwait(false);
                    var batch = new Mock<IPreparedNodeManagerBatch>(MockBehavior.Strict);
                    batch.SetupGet(owner => owner.Registrations).Returns(() => prepared.Registrations);
                    batch.SetupGet(owner => owner.IsCommitted).Returns(() => prepared.IsCommitted);
                    batch.Setup(owner => owner.BindReadImages(It.IsAny<ArrayOf<INodeManagerReadImage>>()))
                        .Callback<ArrayOf<INodeManagerReadImage>>(prepared.BindReadImages);
                    batch.Setup(owner => owner.CommitAsync(
                        It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<CancellationToken>()))
                        .Returns((Func<CancellationToken, ValueTask> decide, CancellationToken cancellation) =>
                            ObserveCommitAsync(prepared.CommitAsync(decide, cancellation), committed));
                    batch.Setup(owner => owner.CommitAsync(
                        It.IsAny<Func<CancellationToken, ValueTask>>(), It.IsAny<Action>(),
                        It.IsAny<CancellationToken>()))
                        .Returns((Func<CancellationToken, ValueTask> decide, Action publish,
                            CancellationToken cancellation) =>
                            ObserveCommitAsync(prepared.CommitAsync(decide, publish, cancellation), committed));
                    batch.Setup(owner => owner.DisposeAsync()).Returns(() => prepared.DisposeAsync());
                    return batch.Object;
                });
            return observed.Object;
        }

        /// <summary>
        /// Returns the actual commit result after recording its physical retirement task.
        /// </summary>
        private static async ValueTask<NodeManagerBatchResult> ObserveCommitAsync(
            ValueTask<NodeManagerBatchResult> commit,
            Action<NodeManagerBatchResult> committed)
        {
            NodeManagerBatchResult result = await commit.ConfigureAwait(false);
            committed(result);
            return result;
        }
    }
}
#endif
