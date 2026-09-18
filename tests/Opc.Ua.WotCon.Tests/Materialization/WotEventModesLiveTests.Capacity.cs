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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Tests;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Tests.Materialization
{
    public sealed partial class WotEventModesLiveTests
    {
        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task NativeRetainedCapacityPreservesProvenanceAndRejectsAlteredIdentityAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            destination.Session.Factory.Builder.AddOpcUaWotCon().Commit();
            await source.InstallAsync(ConditionSourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            var publisher = new CapacityObservedPublisher();
            var factory = new WotProjectionBindingRuntimeFactory(
                new NativeChannels(source.Session), null, publisher, new WotProjectionConditionFactory(),
                new WotProjectionBindingRuntimeOptions { MaxEventRoutes = 1 });
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle, factory);
            WotProjectionHandle handle = await host.AddAsync(
                ConditionProjection(mode, source.EndpointUrl, PrivatePublicSelection("Core")), ct)
                .ConfigureAwait(false);
            try
            {
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                await using NativeConditionEvents events = await NativeConditionEvents.OpenAsync(
                    destination.Session, ct).ConfigureAwait(false);
                NodeId descriptor = await CapacityDescriptorAsync(destination, "condition", ct).ConfigureAwait(false);
                var provenance = new WoTEventBindingTypeClient(
                    destination.Session, descriptor, NUnitTelemetryContext.Create());
                ByteString mainId = ByteString.From(new byte[] { 0xD3, 0xC2, 0x10, 0x01 });
                await ReportConditionAsync(source, "ConditionA", "PumpA", false, true, mainId, ct)
                    .ConfigureAwait(false);
                ProjectionObservation first = await publisher.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(first.Status, Is.EqualTo(StatusCodes.Good));
                ArrayOf<Variant> main = await events.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(main[0].TryGetValue(out ByteString localId), Is.True);
                Assert.That(main[8].TryGetValue(out NodeId conditionId), Is.True);
                Assert.That(first.EventId, Is.EqualTo(localId));
                WoTEventOriginDataType before = await provenance.GetEventProvenanceAsync(localId, ct)
                    .ConfigureAwait(false);

                await ReportConditionAsync(source, "ConditionA", "PumpA", true, true,
                    ByteString.From(new byte[] { 0xD3, 0xC2, 0x10, 0x02 }), ct).ConfigureAwait(false);
                ProjectionObservation second = await publisher.ReadAsync(ct).ConfigureAwait(false);
                int published = 1;
                if (StatusCode.IsGood(second.Status))
                {
                    _ = await events.ReadAsync(ct).ConfigureAwait(false);
                    published++;
                }
                WoTEventOriginDataType? after = null;
                StatusCode provenanceStatus = StatusCodes.Good;
                try
                {
                    after = await provenance.GetEventProvenanceAsync(localId, ct).ConfigureAwait(false);
                }
                catch (ServiceResultException exception)
                {
                    provenanceStatus = exception.StatusCode;
                }
                await ReportConditionAsync(source, "ConditionA", "PumpA", false, true, mainId, ct,
                    condition => condition.Severity!.Value = 999).ConfigureAwait(false);
                if (StatusCode.IsGood(second.Status))
                {
                    ProjectionObservation changed = await publisher.ReadAsync(ct).ConfigureAwait(false);
                    if (StatusCode.IsGood(changed.Status))
                    {
                        _ = await events.ReadAsync(ct).ConfigureAwait(false);
                        published++;
                    }
                }
                StatusCode availability = await ReadCapacityAvailabilityAsync(destination, descriptor, ct)
                    .ConfigureAwait(false);
                NodeId severityId = await FindNativeChildAsync(
                    destination.Session, conditionId, QualifiedName.From(Ua.BrowseNames.Severity), ct)
                    .ConfigureAwait(false);
                DataValue severity = await destination.Session.ReadValueAsync(severityId, ct).ConfigureAwait(false);
                Assert.That(severity.WrappedValue.TryGetValue(out ushort retainedSeverity), Is.True);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(second.Status, Is.EqualTo(StatusCodes.BadTooManyOperations));
                    Assert.That(availability, Is.EqualTo(StatusCodes.BadTooManyOperations));
                    Assert.That(published, Is.EqualTo(1),
                        "Neither the excess branch nor altered E1 may be republished.");
                    Assert.That(provenanceStatus, Is.EqualTo(StatusCodes.Good));
                    Assert.That(after, Is.Not.Null, "Retained main-state provenance must survive route capacity.");
                    Assert.That(retainedSeverity, Is.EqualTo((ushort)412));
                    if (after is not null)
                    {
                        Assert.That(after.SourceEventId, Is.EqualTo(mainId));
                        Assert.That(after.SourceConditionId, Is.EqualTo(before.SourceConditionId));
                        Assert.That(after.SourceBranchId.IsNull, Is.True);
                        Assert.That(after.ReceiveTime, Is.EqualTo(before.ReceiveTime));
                        Assert.That(after.Generation, Is.EqualTo(checked((uint)handle.Generation)));
                        Assert.That(after.EncodingMask, Is.EqualTo(before.EncodingMask));
                    }
                }
            }
            finally
            {
                await host.RemoveAsync(handle, CancellationToken.None).ConfigureAwait(false);
            }
        }

        [TestCase("local-re-emission")]
        [TestCase("transparent-forwarding")]
        public async Task NativeOrdinaryCapacityRecoversWithoutDiscardingDrainingProvenanceAsync(string mode)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            CancellationToken ct = timeout.Token;
            await using var source = new NativeEndpoint();
            await using var destination = new NativeEndpoint();
            await source.StartAsync(false, ct).ConfigureAwait(false);
            await destination.StartAsync(false, ct).ConfigureAwait(false);
            destination.Session.Factory.Builder.AddOpcUaWotCon().Commit();
            await source.InstallAsync(SourceNodes(), ct).ConfigureAwait(false);
            await source.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
            var publisher = new CapacityObservedPublisher();
            var factory = new WotProjectionBindingRuntimeFactory(
                new NativeChannels(source.Session), null, publisher, new WotProjectionConditionFactory(),
                new WotProjectionBindingRuntimeOptions { MaxEventRoutes = 1 });
            var host = new LifecycleWotProjectionHost(destination.Server.NodeManagerLifecycle, factory);
            ExpandedNodeId type = new("EventType", mode == "transparent-forwarding" ? SourceNamespace : LocalNamespace);
            WotProjectionDocument document = Projection(
                type, EventForm(source.EndpointUrl), EventDeclaration(mode, type));
            WotProjectionHandle active = await host.AddAsync(document, ct).ConfigureAwait(false);
            NativeEvents? retained = null;
            try
            {
                uint producingGeneration = checked((uint)active.Generation);
                await destination.RefreshNamespaceTableAsync(ct).ConfigureAwait(false);
                NodeId owner = ExpandedNodeId.Parse(
                    "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
                retained = await NativeEvents.OpenAsync(destination.Session, ct, owner).ConfigureAwait(false);
                NodeId descriptor = await CapacityDescriptorAsync(destination, "event", ct).ConfigureAwait(false);
                var provenance = new WoTEventBindingTypeClient(
                    destination.Session, descriptor, NUnitTelemetryContext.Create());
                ByteString original = ByteString.From(new byte[] { 0xD3, 0xC2, 0x20, 0x01 });
                await source.ReportAsync(original, ct).ConfigureAwait(false);
                Assert.That((await publisher.ReadAsync(ct).ConfigureAwait(false)).Status, Is.EqualTo(StatusCodes.Good));
                ArrayOf<Variant> first = await retained.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(first[0].TryGetValue(out ByteString localId), Is.True);
                WoTEventOriginDataType before = await provenance.GetEventProvenanceAsync(localId, ct)
                    .ConfigureAwait(false);
                await source.ReportAsync(ByteString.From(new byte[] { 0xD3, 0xC2, 0x20, 0x02 }), ct)
                    .ConfigureAwait(false);
                ProjectionObservation exhausted = await publisher.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(exhausted.Status, Is.EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(await ReadCapacityAvailabilityAsync(destination, descriptor, ct).ConfigureAwait(false),
                    Is.EqualTo(StatusCodes.BadTooManyOperations));

                active = await host.ShadowReloadAsync(active, document, ct).ConfigureAwait(false);
                Assert.That(active.Generation, Is.GreaterThan(producingGeneration));
                await using NativeEvents replacement = await NativeEvents.OpenAsync(destination.Session, ct, owner)
                    .ConfigureAwait(false);
                ByteString nextSourceId = ByteString.From(new byte[] { 0xD3, 0xC2, 0x20, 0x03 });
                await source.ReportAsync(nextSourceId, ct).ConfigureAwait(false);
                ProjectionObservation next = await publisher.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(next.Status, Is.EqualTo(StatusCodes.Good),
                    "The finite generation budget must have a working explicit recovery boundary.");
                ArrayOf<Variant> nextFields = await replacement.ReadAsync(ct).ConfigureAwait(false);
                Assert.That(nextFields[0].TryGetValue(out ByteString nextId), Is.True);
                NodeId currentDescriptor = await CapacityDescriptorAsync(destination, "event", ct)
                    .ConfigureAwait(false);
                var currentProvenance = new WoTEventBindingTypeClient(
                    destination.Session, currentDescriptor, NUnitTelemetryContext.Create());
                WoTEventOriginDataType current = await currentProvenance.GetEventProvenanceAsync(nextId, ct)
                    .ConfigureAwait(false);
                WoTEventOriginDataType draining = await provenance.GetEventProvenanceAsync(localId, ct)
                    .ConfigureAwait(false);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(draining.Generation, Is.EqualTo(producingGeneration));
                    Assert.That(draining.SourceEventId, Is.EqualTo(original));
                    Assert.That(draining.ReceiveTime, Is.EqualTo(before.ReceiveTime));
                    Assert.That(current.Generation, Is.EqualTo(checked((uint)active.Generation)));
                    Assert.That(current.SourceEventId, Is.EqualTo(nextSourceId));
                    Assert.That(nextId, Is.Not.EqualTo(localId));
                }
            }
            finally
            {
                if (retained is not null)
                {
                    await retained.DisposeAsync().ConfigureAwait(false);
                }
                await host.RemoveAsync(active, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static async Task<NodeId> CapacityDescriptorAsync(
            NativeEndpoint destination, string name, CancellationToken ct)
        {
            NodeId owner = ExpandedNodeId.Parse(
                "nsu=" + LocalNamespace + ";s=Owner", destination.Session.NamespaceUris);
            ushort metadata = checked((ushort)destination.Session.NamespaceUris.GetIndex(Namespaces.WotCon));
            NodeId folder = await FindNativeChildAsync(destination.Session, owner,
                new QualifiedName("EventBindings", metadata), ct).ConfigureAwait(false);
            return await FindNativeChildAsync(destination.Session, folder, new QualifiedName(name, metadata), ct)
                .ConfigureAwait(false);
        }

        private static async Task<StatusCode> ReadCapacityAvailabilityAsync(
            NativeEndpoint destination, NodeId descriptor, CancellationToken ct)
        {
            ushort metadata = checked((ushort)destination.Session.NamespaceUris.GetIndex(Namespaces.WotCon));
            NodeId availability = await FindNativeChildAsync(destination.Session, descriptor,
                new QualifiedName("Availability", metadata), ct).ConfigureAwait(false);
            DataValue value = await destination.Session.ReadValueAsync(availability, ct).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out StatusCode status), Is.True);
            return status;
        }

        private readonly record struct ProjectionObservation(StatusCode Status, ByteString EventId);

        private sealed class CapacityObservedPublisher : IWotNativeProjectionEventPublisher
        {
            public void Register(
                INodeManagerBuilder builder,
                BaseObjectState notifier,
                Func<CancellationToken, IAsyncEnumerable<BaseEventState>> source,
                bool registerAsRootNotifier)
            {
                m_native.Register(builder, notifier,
                    token => new ObservedStream(source(token), m_observations.Writer), registerAsRootNotifier);
            }

            public ValueTask<ProjectionObservation> ReadAsync(CancellationToken ct)
            {
                return m_observations.Reader.ReadAsync(ct);
            }

            private sealed class ObservedStream(
                IAsyncEnumerable<BaseEventState> source,
                ChannelWriter<ProjectionObservation> observations)
                : IAsyncEnumerable<BaseEventState>, IEventSourceReadiness
            {
                public IAsyncEnumerator<BaseEventState> GetAsyncEnumerator(
                    CancellationToken cancellationToken = default)
                {
                    return ObserveAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
                }

                public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
                {
                    return source is IEventSourceReadiness readiness
                        ? readiness.WaitUntilReadyAsync(cancellationToken)
                        : throw new InvalidOperationException("The production stream must retain readiness.");
                }

                private async IAsyncEnumerable<BaseEventState> ObserveAsync(
                    [EnumeratorCancellation] CancellationToken cancellationToken)
                {
                    await using IAsyncEnumerator<BaseEventState> iterator =
                        source.GetAsyncEnumerator(cancellationToken);
                    while (true)
                    {
                        bool next;
                        try
                        {
                            next = await iterator.MoveNextAsync().ConfigureAwait(false);
                        }
                        catch (ServiceResultException exception)
                        {
                            if (!observations.TryWrite(new ProjectionObservation(exception.StatusCode, default)))
                            {
                                throw new InvalidOperationException("The observation queue is full.", exception);
                            }
                            throw;
                        }
                        if (!next)
                        {
                            yield break;
                        }
                        BaseEventState current = iterator.Current;
                        yield return current;
                        if (!observations.TryWrite(new ProjectionObservation(StatusCodes.Good, current.EventId!.Value)))
                        {
                            throw new InvalidOperationException("The observation queue is full.");
                        }
                    }
                }
            }

            private readonly WotProjectionEventPublisher m_native = new();
            private readonly Channel<ProjectionObservation> m_observations =
                Channel.CreateBounded<ProjectionObservation>(16);
        }
    }
}
#endif
