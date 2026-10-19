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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.MonitoredItems;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Vision.Tests
{
    [TestFixture]
    [Category("Vision")]
    public sealed class MachineVisionWorkflowClientTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task CapabilitiesKeepNamespaceSupportIndependentAsync(bool control, bool assets)
        {
            var harness = new VisionSessionHarness(includeDraftNamespace: false);
            if (control)
            {
                AddNamespace(harness, MachineVisionModels.ControlNamespaceUri);
            }
            if (assets)
            {
                AddNamespace(harness, MachineVisionModels.AssetNamespaceUri);
            }
            MachineVisionCapabilities found = await harness.Client.MachineVision().ReadCapabilitiesAsync()
                .ConfigureAwait(false);
            Assert.That(found.SupportsControl, Is.EqualTo(control));
            Assert.That(found.SupportsAssets, Is.EqualTo(assets));
            Assert.That(found.Interfaces.IsEmpty, Is.True);
            harness.Session.Verify(s => s.CallAsync(It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task CapabilitiesInspectActualMethodsAndRetainExecutabilityQualityAsync()
        {
            var harness = new VisionSessionHarness(false);
            ushort ns = AddNamespace(harness, MachineVisionModels.ControlNamespaceUri);
            var system = new NodeId("system", ns);
            var control = new NodeId("control", ns);
            var method = new NodeId("halt", ns);
            harness.AddChild(system, "VisionStateMachine", control);
            harness.AddBrowse(control,
            [
                new ReferenceDescription
                {
                    NodeId = method,
                    BrowseName = new QualifiedName("Halt", ns),
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    NodeClass = NodeClass.Method
                }
            ]);
            MachineVisionCapabilities found = await harness.Client.MachineVision().ReadCapabilitiesAsync(system)
                .ConfigureAwait(false);
            MachineVisionInterfaceCapability top = found.Interfaces.ToArray()!.Single(i =>
                i.Path == "VisionStateMachine");
            Assert.That(top.NodeId, Is.EqualTo(control));
            Assert.That(top.Methods.Count, Is.EqualTo(1));
            Assert.That(top.Methods[0].NodeId, Is.EqualTo(method));
            Assert.That(top.Methods[0].Executable.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(found.Interfaces.ToArray()!.Single(i => i.Path == "RecipeManagement").NodeId.IsNull, Is.True);
        }

        [Test]
        public async Task StatusPreservesMissingOptionalMembersAndBadReadQualityAsync()
        {
            var harness = new VisionSessionHarness(false);
            ushort ns = AddNamespace(harness, MachineVisionModels.ControlNamespaceUri);
            var system = new NodeId("system", ns);
            var level = new NodeId("diagnostic", ns);
            harness.AddValueChild(system, "DiagnosticLevel", level, Variant.From((ushort)42));
            MachineVisionStatusSnapshot found = await harness.Client.MachineVision().ReadStatusAsync(system)
                .ConfigureAwait(false);
            Assert.That(found.Values.Count, Is.EqualTo(5));
            Assert.That(found.Values[0].Value.WrappedValue.TryGetValue(out ushort value), Is.True);
            Assert.That(value, Is.EqualTo(42));
            Assert.That(found.Values[1].NodeId.IsNull, Is.True);
            Assert.That(found.Values[1].Value.StatusCode, Is.EqualTo(StatusCodes.BadNoMatch));
            Assert.That(found.Steps.IsEmpty, Is.True);
        }

        [Test]
        public async Task CapabilityAccessDenialCannotMasqueradeAsAnAbsentOptionalInterface()
        {
            var harness = new VisionSessionHarness(false);
            AddNamespace(harness, MachineVisionModels.ControlNamespaceUri);
            harness.Session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>((_, paths, _) =>
                    new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(new TranslateBrowsePathsToNodeIdsResponse
                    {
                        Results = paths.ToArray()!.Select(_ =>
                            new BrowsePathResult { StatusCode = StatusCodes.BadUserAccessDenied }).ToArrayOf()
                    }));
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await harness.Client.MachineVision().ReadCapabilitiesAsync(new NodeId("system", 2))
                    .ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task RelationshipPageDeduplicatesAndReleasesUnusedContinuationAsync()
        {
            var harness = new VisionSessionHarness(false);
            var node = new NodeId("root", 2);
            ReferenceDescription first = Reference("first", false);
            ReferenceDescription second = Reference("second", true);
            var point = ByteString.From("*"u8.ToArray());
            SetBrowse(harness, new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                References = [first, first, second],
                ContinuationPoint = point
            });
            MachineVisionRelationshipPage result = await harness.Client.MachineVision().ReadRelationshipsAsync(
                node, MachineVisionRelationship.SameEntity, BrowseDirection.Both, maximumResults: 1)
                    .ConfigureAwait(false);
            Assert.That(result.References.Count, Is.EqualTo(1));
            Assert.That(result.References[0].NodeId, Is.EqualTo(first.NodeId));
            Assert.That(result.References[0].IsForward, Is.False);
            Assert.That(result.NextIndex, Is.EqualTo(1));
            Assert.That(result.IsComplete, Is.False);
            harness.Session.Verify(s => s.BrowseNextAsync(It.IsAny<RequestHeader>(), true,
                It.Is<ArrayOf<ByteString>>(points => points.Count == 1 && points[0] == point),
                CancellationToken.None), Times.Once);
        }

        [Test]
        public async Task RelationshipOffsetAtExactEndIsCompleteWithoutSilentTruncationAsync()
        {
            var harness = new VisionSessionHarness(false);
            SetBrowse(harness, new BrowseResult
            {
                StatusCode = StatusCodes.Good,
                References = [Reference("first", true), Reference("second", false)]
            });
            MachineVisionRelationshipPage result = await harness.Client.MachineVision().ReadRelationshipsAsync(
                new NodeId("root", 2), MachineVisionRelationship.Interface, maximumResults: 1, startIndex: 1)
                    .ConfigureAwait(false);
            Assert.That(result.IsComplete, Is.True);
            Assert.That(result.NextIndex, Is.EqualTo(2));
            Assert.That(result.References.Count, Is.EqualTo(1));
            Assert.That(result.References[0].NodeId, Is.EqualTo(Reference("second", false).NodeId));
            harness.Session.Verify(s => s.BrowseNextAsync(It.IsAny<RequestHeader>(), It.IsAny<bool>(),
                It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task RelationshipFailuresRetainPrimaryAndCleanupErrors()
        {
            var harness = new VisionSessionHarness(false);
            SetBrowse(harness, new BrowseResult
            {
                StatusCode = StatusCodes.BadUserAccessDenied,
                ContinuationPoint = ByteString.From(new byte[] { 1 })
            });
            var cleanup = new InvalidOperationException("release failed");
            harness.Session.Setup(s => s.BrowseNextAsync(It.IsAny<RequestHeader>(), true,
                It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
                .Throws(cleanup);
            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Client.MachineVision().ReadRelationshipsAsync(
                    new NodeId("root", 2), MachineVisionRelationship.SameEntity).ConfigureAwait(false));
            Assert.That(failure!.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(((ServiceResultException)failure.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(failure.InnerExceptions[1], Is.SameAs(cleanup));
        }

        [TestCase(0u, 0u)]
        [TestCase(101u, 0u)]
        [TestCase(1u, 10001u)]
        public async Task RelationshipBoundsFailBeforeBrowse(uint size, uint offset)
        {
            var harness = new VisionSessionHarness(false);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await harness.Client.MachineVision().ReadRelationshipsAsync(new NodeId("root", 2),
                    MachineVisionRelationship.SameEntity, maximumResults: size, startIndex: offset)
                        .ConfigureAwait(false));
            harness.Session.Verify(s => s.BrowseAsync(It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task RelationshipRejectsMissingNamespaceAndUnknownDiscriminators()
        {
            MachineVisionClient client = new VisionSessionHarness(false).Client.MachineVision();
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await client.ReadRelationshipsAsync(NodeId.Null, MachineVisionRelationship.SameEntity)
                    .ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await client.ReadRelationshipsAsync(new NodeId("root", 2), (MachineVisionRelationship)100)
                    .ConfigureAwait(false));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await client.ReadRelationshipsAsync(new NodeId("root", 2), MachineVisionRelationship.SameEntity,
                    (BrowseDirection)100).ConfigureAwait(false));
            ServiceResultException? missing = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ReadRelationshipsAsync(new NodeId("root", 2), MachineVisionRelationship.OpticalPath)
                    .ConfigureAwait(false));
            Assert.That(missing!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [TestCase(0)]
        [TestCase(101)]
        public async Task ObservationRejectsInvalidCountBeforeCreatingResources(int maximum)
        {
            var harness = new VisionSessionHarness(false);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await harness.Client.MachineVision().ObserveAsync(new NodeId("notifier", 2),
                    new MachineVisionObservationOptions { MaximumNotifications = maximum }).ConfigureAwait(false));
            harness.Session.Verify(s => s.TryGetSubscriptionManager(out It.Ref<ISubscriptionManager?>.IsAny),
                Times.Never);
        }

        [Test]
        public async Task ObservationLimitRetainsExactEventAndConditionIdentifiersAndDisposesAsync()
        {
            var harness = new ObservationHarness();
            var condition = new NodeId("camera", 2);
            var eventId = ByteString.From(new byte[] { 3, 1, 4 });
            Task<MachineVisionObservation> task = harness.Client.ObserveAsync(new NodeId("notifier", 2),
                new MachineVisionObservationOptions
                {
                    MaximumNotifications = 1,
                    Mode = MachineVisionObservationMode.Conditions
                }).AsTask();
            await harness.EmitAsync(ObjectTypeIds.AcknowledgeableConditionType, eventId, condition)
                .ConfigureAwait(false);
            MachineVisionObservation result = await task.ConfigureAwait(false);
            Assert.That(result.LimitReached, Is.True);
            Assert.That(result.Events.Count, Is.EqualTo(1));
            Assert.That(result.Events[0].EventId, Is.EqualTo(eventId));
            Assert.That(result.Events[0].ConditionId, Is.EqualTo(condition));
            Assert.That(result.Events[0].Decoded, Is.InstanceOf<AcknowledgeableConditionTypeRecord>());
            Assert.That(result.RefreshComplete, Is.False);
            harness.Subscription.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Test]
        public async Task ObservationEmptyWindowIsBoundedAndDoesNotDisposeTheSessionAsync()
        {
            var harness = new ObservationHarness();
            Task<MachineVisionObservation> task = harness.Client.ObserveAsync(new NodeId("notifier", 2)).AsTask();
            harness.Clock.Advance(TimeSpan.FromSeconds(5));
            MachineVisionObservation result = await task.ConfigureAwait(false);
            Assert.That(result.Events.IsEmpty, Is.True);
            Assert.That(result.LimitReached, Is.False);
            Assert.That(result.RefreshComplete, Is.False);
            harness.Subscription.Verify(s => s.DisposeAsync(), Times.Once);
            harness.Session.Session.Verify(s => s.DisposeAsync(), Times.Never);
        }

        [Test]
        public async Task ObservationRefreshRequiresStartAndEndAndReportsOverflowAsync()
        {
            var harness = new ObservationHarness();
            Task<MachineVisionObservation> task = harness.Client.ObserveAsync(new NodeId("notifier", 2),
                new MachineVisionObservationOptions
                {
                    Mode = MachineVisionObservationMode.Conditions,
                    RefreshConditions = true
                }).AsTask();
            harness.Item.Verify(i => i.ConditionRefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
            await harness.EmitAsync(ObjectTypeIds.RefreshStartEventType, default, default).ConfigureAwait(false);
            await harness.EmitAsync(ObjectTypeIds.EventQueueOverflowEventType, default, default).ConfigureAwait(false);
            await harness.EmitAsync(ObjectTypeIds.RefreshEndEventType, default, default).ConfigureAwait(false);
            harness.Clock.Advance(TimeSpan.FromSeconds(5));
            MachineVisionObservation result = await task.ConfigureAwait(false);
            Assert.That(result.RefreshComplete, Is.True);
            Assert.That(result.DroppedNotifications, Is.EqualTo(1));
            Assert.That(result.Events.IsEmpty, Is.True);
            harness.Subscription.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Test]
        public async Task ObservationCancellationPropagatesAndCleansOnlyOwnedSubscriptionAsync()
        {
            var harness = new ObservationHarness();
            using var cancellation = new CancellationTokenSource();
            Task<MachineVisionObservation> task = harness.Client.ObserveAsync(
                new NodeId("notifier", 2), cancellationToken: cancellation.Token).AsTask();
            await cancellation.CancelAsync().ConfigureAwait(false);
            OperationCanceledException? failure = await Assert.ThrowsAsync<OperationCanceledException>(async () => await task
                .ConfigureAwait(false));
            Assert.That(failure!.CancellationToken, Is.EqualTo(cancellation.Token));
            harness.Subscription.Verify(s => s.DisposeAsync(), Times.Once);
            harness.Session.Session.Verify(s => s.DisposeAsync(), Times.Never);
        }

        [Test]
        public async Task ObservationSetupFailureAndCleanupFailureAreBothPreservedAsync()
        {
            var harness = new ObservationHarness();
            harness.Item.SetupGet(i => i.Created).Returns(false);
            harness.Item.SetupGet(i => i.Error).Returns(new ServiceResult(StatusCodes.BadUserAccessDenied));
            var cleanup = new InvalidOperationException("dispose failure");
            harness.Subscription.Setup(s => s.DisposeAsync()).Returns(new ValueTask(Task.FromException(cleanup)));
            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                await harness.Client.ObserveAsync(new NodeId("notifier", 2)).ConfigureAwait(false));
            Assert.That(failure!.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(((ServiceResultException)failure.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(failure.InnerExceptions[1], Is.SameAs(cleanup));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ObservationSetupTimeoutIsNotASuccessfulEmptyWindowAsync()
        {
            var harness = new ObservationHarness();
            harness.Item.SetupGet(i => i.Created).Returns(false);
            harness.Item.SetupGet(i => i.Error).Returns(ServiceResult.Good);
            Task<MachineVisionObservation> task = harness.Client.ObserveAsync(new NodeId("notifier", 2)).AsTask();
            harness.Clock.Advance(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TimeoutException>(async () => await task.ConfigureAwait(false));
            harness.Subscription.Verify(s => s.DisposeAsync(), Times.Once);
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Test]
        public async Task ObservationRejectsAnOversizedEventAndStillDisposesAsync()
        {
            var harness = new ObservationHarness();
            Task<MachineVisionObservation> task = harness.Client.ObserveAsync(new NodeId("notifier", 2)).AsTask();
            await harness.EmitAsync(ObjectTypeIds.BaseEventType, default, default,
                new string('x', 1024 * 1024)).ConfigureAwait(false);
            ServiceResultException? tooLarge = await Assert.ThrowsAsync<ServiceResultException>(async () => await task
                .ConfigureAwait(false));
            Assert.That(tooLarge!.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            harness.Subscription.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Test]
        public async Task ObservationCleanupFailureIsNotHiddenBySuccessfulWindowExpiryAsync()
        {
            var harness = new ObservationHarness();
            var cleanup = new InvalidOperationException("dispose failure");
            harness.Subscription.Setup(s => s.DisposeAsync()).Returns(new ValueTask(Task.FromException(cleanup)));
            Task<MachineVisionObservation> task = harness.Client.ObserveAsync(new NodeId("notifier", 2)).AsTask();
            harness.Clock.Advance(TimeSpan.FromSeconds(5));
            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await task
                .ConfigureAwait(false));
            Assert.That(failure, Is.SameAs(cleanup));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static ushort AddNamespace(VisionSessionHarness harness, string uri)
        {
            harness.MessageContext.NamespaceUris.GetIndexOrAppend(uri);
            return harness.NamespaceUris.GetIndexOrAppend(uri);
        }

        private static ReferenceDescription Reference(string id, bool forward)
        {
            return new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(id, "urn:vendor:vision"),
                BrowseName = new QualifiedName(id, 2),
                IsForward = forward,
                ReferenceTypeId = ReferenceTypeIds.RepresentsSameEntityAs,
                TypeDefinition = new ExpandedNodeId("VendorType", "urn:vendor:vision")
            };
        }

        private static void SetBrowse(VisionSessionHarness harness, BrowseResult result)
        {
            harness.Session.Setup(s => s.BrowseAsync(It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(),
                It.IsAny<uint>(), It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<BrowseResponse>(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [result]
                }));
        }

        private sealed class ObservationHarness
        {
            public ObservationHarness()
            {
                ISubscriptionManager? manager = Manager.Object;
                Session.Session.Setup(s => s.TryGetSubscriptionManager(out manager)).Returns(true);
                Manager.Setup(m => m.Add(It.IsAny<ISubscriptionNotificationHandler>(),
                    It.IsAny<IOptionsMonitor<SubscriptionOptions>>()))
                    .Callback<ISubscriptionNotificationHandler, IOptionsMonitor<SubscriptionOptions>>(
                        (handler, _) => Handler = handler)
                    .Returns(Subscription.Object);
                IMonitoredItem? item = Item.Object;
                Items.Setup(i => i.TryAdd(It.IsAny<string>(),
                    It.IsAny<IOptionsMonitor<MonitoredItemOptions>>(), out item)).Returns(true);
                Subscription.SetupGet(s => s.MonitoredItems).Returns(Items.Object);
                Item.SetupGet(i => i.Created).Returns(true);
                Client = new MachineVisionClient(Session.Session.Object, Session.Telemetry, Clock);
            }

            public VisionSessionHarness Session { get; } = new(false);
            public FakeTimeProvider Clock { get; } = new();
            public Mock<ISubscriptionManager> Manager { get; } = new();
            public Mock<ISubscription> Subscription { get; } = new();
            public Mock<IMonitoredItemCollection> Items { get; } = new();
            public Mock<IMonitoredItem> Item { get; } = new();
            public ISubscriptionNotificationHandler Handler { get; private set; } = null!;
            public MachineVisionClient Client { get; }

            public ValueTask EmitAsync(NodeId eventType, ByteString eventId, NodeId condition, string? message = null)
            {
                QualifiedName[][] paths = EventRecordDecoderRegistry.Default.StandardFields;
                var fields = new List<Variant>();
                foreach (QualifiedName[] path in paths)
                {
                    fields.Add(path.Length == 0 ? Variant.From(condition) : path[^1].Name switch
                    {
                        "EventId" => Variant.From(eventId),
                        "EventType" => Variant.From(eventType),
                        "Message" => Variant.From(new LocalizedText(message)),
                        _ => Variant.Null
                    });
                }
                return Handler.OnEventDataNotificationAsync(Subscription.Object, 1, DateTime.UtcNow,
                    new EventNotification[] { new(Item.Object, fields.ToArrayOf()) },
                    PublishState.None, Array.Empty<string>());
            }
        }
    }
}
