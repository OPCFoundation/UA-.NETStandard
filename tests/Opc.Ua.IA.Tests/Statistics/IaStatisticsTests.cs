/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.IA.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.IA.Tests.Statistics
{
    /// <summary>
    /// Verifies application-owned statistics and grouped reset independently of wire transport.
    /// </summary>
    [TestFixture]
    public sealed class IaStatisticsTests
    {
        /// <summary>
        /// Publishes exactly one interface and mutually consistent calculation metadata.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void StatisticsMetadataMatchesSelectedInterface(int window)
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            var builder = new IaStatisticsBuilder().AddComponent(quantity);
            switch (window)
            {
                case 0:
                    builder.WithAggregate("Application restart");
                    break;
                case 1:
                    builder.WithDurationWindow(1000);
                    break;
                case 2:
                    builder.WithCountWindow(4);
                    break;
            }
            IaStatisticsOptions options = window == 3
                ? new IaStatisticsOptions { Rolling = true }
                : new IaStatisticsOptions();
            using IaStatistics binding = window == 3
                ? new IaStatistics(fixture.Context, owner, [quantity], Mock.Of<IIaStatisticsProvider>(), options)
                : builder.Build(fixture.Context, owner, Mock.Of<IIaStatisticsProvider>());
            NodeId expected = ExpandedNodeId.ToNodeId(
                window == 0 ? ObjectTypeIds.IAggregateStatisticsType : ObjectTypeIds.IRollingStatisticsType,
                fixture.Context.NamespaceUris);
            Assert.That(owner.ReferenceExists(Opc.Ua.ReferenceTypeIds.HasInterface, false, expected), Is.True);
            NodeId reference = ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasStatisticComponent,
                fixture.Context.NamespaceUris);
            Assert.That(owner.ReferenceExists(reference, false, quantity.NodeId), Is.True);
            Assert.That(quantity.ReferenceExists(reference, true, owner.NodeId), Is.True);
            string[] names = fixture.Children(owner).Select(child => child.BrowseName.Name!).ToArray();
            Assert.That(names.Contains(BrowseNames.WindowDuration), Is.EqualTo(window == 1));
            Assert.That(names.Contains(BrowseNames.WindowNumberOfValues), Is.EqualTo(window == 2));
            Assert.That(names.Contains(BrowseNames.ResetCondition), Is.EqualTo(window == 0));
            if (window is 0 or 1 or 2)
            {
                string property = window switch
                {
                    0 => BrowseNames.ResetCondition,
                    1 => BrowseNames.WindowDuration,
                    _ => BrowseNames.WindowNumberOfValues
                };
                Variant expectedValue = window switch
                {
                    0 => Variant.From("Application restart"),
                    1 => Variant.From(1000.0),
                    _ => Variant.From(4u)
                };
                Assert.That(fixture.Children(owner).OfType<BaseVariableState>()
                    .Single(child => child.BrowseName.Name == property).WrappedValue, Is.EqualTo(expectedValue));
            }
            Assert.That(quantity.AccessLevel, Is.EqualTo(AccessLevels.CurrentRead));
            Assert.That(binding.StartTime.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadWaitingForInitialData));
        }

        /// <summary>
        /// Rejects inconsistent rolling windows before an owner is modified.
        /// </summary>
        [TestCase(-1.0)]
        [TestCase(0.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidDurationWindowIsRejected(double duration)
        {
            Assert.That(() => new IaStatisticsBuilder().WithDurationWindow(duration),
                Throws.ArgumentException);
        }

        /// <summary>
        /// Rejects mutually exclusive windows and metadata belonging to another interface.
        /// </summary>
        [Test]
        public void InconsistentStatisticsMetadataIsRejected()
        {
            Assert.That(() => new IaStatisticsOptions
            { Rolling = true, WindowDuration = 1, WindowNumberOfValues = 1 }.Validate(), Throws.ArgumentException);
            Assert.That(() => new IaStatisticsOptions { WindowDuration = 1 }.Validate(), Throws.ArgumentException);
            Assert.That(() => new IaStatisticsOptions { Rolling = true, ResetCondition = "restart" }.Validate(),
                Throws.ArgumentException);
            Assert.That(() => new IaStatisticsBuilder().WithCountWindow(0), Throws.ArgumentException);
        }

        /// <summary>
        /// A grouped quantity shares its owner's StartTime and reset without an extra direct statistics link.
        /// </summary>
        [Test]
        public async Task IndirectGroupsShareCollectionReset()
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            var group = new BaseObjectState(owner);
            group.Create(fixture.Context, NodeId.Null, fixture.Name("Group"), new LocalizedText("Group"), true);
            owner.AddChild(group);
            BaseVariableState quantity = fixture.Quantity(group);
            var provider = new Mock<IIaStatisticsProvider>();
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(17));
            provider.Setup(source => source.AuthorizeResetAsync(fixture.Context, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);
            provider.Setup(source => source.ResetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(0));
            using IaStatistics binding = new IaStatisticsBuilder().AddGroup(group).AddComponent(quantity)
                .Build(fixture.Context, owner, provider.Object);
            NodeId reference = ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasStatisticComponent,
                fixture.Context.NamespaceUris);
            Assert.That(owner.ReferenceExists(reference, false, group.NodeId), Is.True);
            Assert.That(owner.ReferenceExists(reference, false, quantity.NodeId), Is.False);
            await binding.RefreshAsync().ConfigureAwait(false);
            AssertValue(quantity, 17);
            await binding.ResetAsync(fixture.Context).ConfigureAwait(false);
            AssertValue(quantity, 0);
        }

        /// <summary>
        /// Group topology cannot hide another collection or a quantity the owner would not reset.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void AmbiguousOrIncompleteStatisticGroupsAreRejected(bool nestedCollection)
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseObjectState group = fixture.Owner("Group");
            BaseVariableState quantity = fixture.Quantity(group);
            var builder = new IaStatisticsBuilder().AddGroup(group);
            if (nestedCollection)
            {
                builder.AddComponent(quantity);
                group.AddReference(Opc.Ua.ReferenceTypeIds.HasInterface, false,
                    ExpandedNodeId.ToNodeId(ObjectTypeIds.IStatisticsType, fixture.Context.NamespaceUris));
            }
            else
            {
                builder.AddComponent(fixture.Quantity(owner, "Other"));
            }
            Assert.That(() => builder.Build(fixture.Context, owner, Mock.Of<IIaStatisticsProvider>()),
                Throws.ArgumentException);
        }

        /// <summary>
        /// Vendor grouping types require matching metadata and cannot inherit another statistics collection.
        /// </summary>
        [TestCase(0, true, false)]
        [TestCase(0, true, true)]
        [TestCase(1, false, false)]
        [TestCase(2, false, false)]
        [TestCase(3, false, false)]
        [TestCase(4, false, false)]
        [TestCase(5, false, false)]
        [TestCase(6, false, false)]
        [TestCase(7, false, false)]
        public void GroupTypeValidationPrecedesMutation(int scenario, bool accepted, bool attachment)
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseObjectState group = fixture.Owner("Group");
            BaseVariableState quantity = fixture.Quantity(group);
            ushort vendorNamespace = fixture.Context.NamespaceUris.GetIndexOrAppend("urn:tests:ia:vendor-types");
            NodeId typeId = new("GroupType", vendorNamespace);
            NodeId parentId = new("ParentType", fixture.InstanceNamespace);
            var definition = new BaseObjectTypeState { NodeId = typeId, SuperTypeId = parentId };
            var parent = new BaseObjectTypeState
            {
                NodeId = parentId,
                SuperTypeId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            var types = new Dictionary<NodeId, BaseObjectTypeState> { [typeId] = definition, [parentId] = parent };
            var table = (TypeTable)fixture.Context.TypeTable;
            table.AddSubtype(Opc.Ua.ObjectTypeIds.BaseObjectType, NodeId.Null);
            table.AddSubtype(parentId, Opc.Ua.ObjectTypeIds.BaseObjectType);
            table.AddSubtype(typeId, parentId);
            group.TypeDefinitionId = typeId;
            if (scenario == 1)
            {
                parent.AddReference(Opc.Ua.ReferenceTypeIds.HasInterface, false,
                    ExpandedNodeId.ToNodeId(ObjectTypeIds.IStatisticsType, fixture.Context.NamespaceUris));
            }
            if (scenario == 3)
            {
                definition.NodeId = new NodeId("Wrong", vendorNamespace);
            }
            if (scenario == 4)
            {
                parent.SuperTypeId = typeId;
                table.AddSubtype(parentId, typeId);
            }
            if (scenario == 5)
            {
                parent.SuperTypeId = Opc.Ua.ObjectTypeIds.FolderType;
            }
            if (scenario == 6)
            {
                types.Remove(parentId);
            }
            if (scenario == 7)
            {
                NodeId statisticsType = ExpandedNodeId.ToNodeId(ObjectTypeIds.IStatisticsType,
                    fixture.Context.NamespaceUris);
                NodeId vendorInterface = new("StatisticsInterface", vendorNamespace);
                table.AddSubtype(statisticsType, NodeId.Null);
                table.AddSubtype(vendorInterface, statisticsType);
                parent.AddReference(Opc.Ua.ReferenceTypeIds.HasInterface, false, vendorInterface);
            }
            var builder = new IaStatisticsBuilder().AddGroup(group).AddComponent(quantity);
            if (scenario != 2 && !attachment)
            {
                builder.WithGroupTypeResolver(node => types.TryGetValue(node, out BaseObjectTypeState? value)
                    ? value : null);
            }
            if (accepted)
            {
                var attach = new Mock<INodeAttachContext>();
                attach.SetupGet(value => value.SystemContext).Returns(fixture.Context);
                attach.Setup(value => value.Find(It.IsAny<NodeId>()))
                    .Returns<NodeId>(node => types.TryGetValue(node, out BaseObjectTypeState? value) ? value : null);
                using IaStatistics binding = attachment
                    ? builder.Build(attach.Object, owner, Mock.Of<IIaStatisticsProvider>())
                    : builder.Build(fixture.Context, owner, Mock.Of<IIaStatisticsProvider>());
                Assert.That(binding.StartTime.NodeId.IsNull, Is.False);
                attach.Verify(value => value.Find(typeId), Times.Exactly(attachment ? 1 : 0));
                attach.Verify(value => value.Find(parentId), Times.Exactly(attachment ? 1 : 0));
            }
            else
            {
                Assert.That(() => builder.Build(fixture.Context, owner, Mock.Of<IIaStatisticsProvider>()),
                    Throws.ArgumentException);
                Assert.That(fixture.Children(owner), Is.Empty);
                NodeId reference = ExpandedNodeId.ToNodeId(ReferenceTypeIds.HasStatisticComponent,
                    fixture.Context.NamespaceUris);
                Assert.That(owner.ReferenceExists(reference, false, group.NodeId), Is.False);
            }
        }

        /// <summary>
        /// Cancellation during ObjectType resolution leaves the detached owner unchanged.
        /// </summary>
        [Test]
        public void GroupTypeResolutionHonorsCancellationBeforeMutation()
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseObjectState group = fixture.Owner("Group");
            BaseVariableState quantity = fixture.Quantity(group);
            group.TypeDefinitionId = new NodeId("VendorType", fixture.InstanceNamespace);
            var type = new BaseObjectTypeState
            {
                NodeId = group.TypeDefinitionId,
                SuperTypeId = Opc.Ua.ObjectTypeIds.BaseObjectType
            };
            var table = (TypeTable)fixture.Context.TypeTable;
            table.AddSubtype(Opc.Ua.ObjectTypeIds.BaseObjectType, NodeId.Null);
            table.AddSubtype(type.NodeId, type.SuperTypeId);
            using var cancellation = new CancellationTokenSource();
            var builder = new IaStatisticsBuilder().AddGroup(group).AddComponent(quantity)
                .WithGroupTypeResolver(_ =>
                {
                    cancellation.Cancel();
                    return type;
                });
            Assert.That(() => builder.Build(fixture.Context, owner, Mock.Of<IIaStatisticsProvider>(),
                cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(fixture.Children(owner), Is.Empty);
        }

        /// <summary>
        /// Direct construction accepts the last permitted ancestry depth and rejects the next without publication.
        /// </summary>
        [TestCase(128, true)]
        [TestCase(129, false)]
        public void GroupTypeAncestryHasAnExactDepthBound(int depth, bool accepted)
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseObjectState group = fixture.Owner("Group");
            BaseVariableState quantity = fixture.Quantity(group);
            var types = new Dictionary<NodeId, BaseObjectTypeState>();
            var table = (TypeTable)fixture.Context.TypeTable;
            table.AddSubtype(Opc.Ua.ObjectTypeIds.BaseObjectType, NodeId.Null);
            NodeId parent = Opc.Ua.ObjectTypeIds.BaseObjectType;
            for (int i = 0; i < depth; i++)
            {
                var type = new BaseObjectTypeState
                {
                    NodeId = new NodeId("GroupType" + i, fixture.InstanceNamespace),
                    SuperTypeId = parent
                };
                table.AddSubtype(type.NodeId, parent);
                types.Add(type.NodeId, type);
                parent = type.NodeId;
            }
            group.TypeDefinitionId = parent;
            var options = new IaStatisticsOptions
            {
                ResolveGroupType = node => types.TryGetValue(node, out BaseObjectTypeState? value) ? value : null
            };
            if (accepted)
            {
                using var binding = new IaStatistics(fixture.Context, owner, [quantity],
                    Mock.Of<IIaStatisticsProvider>(), options, [group]);
                Assert.That(binding.StartTime.NodeId.IsNull, Is.False);
            }
            else
            {
                Assert.That(() => new IaStatistics(fixture.Context, owner, [quantity],
                    Mock.Of<IIaStatisticsProvider>(), options, [group]), Throws.ArgumentException);
                Assert.That(fixture.Children(owner), Is.Empty);
            }
        }

        /// <summary>
        /// Denial and provider failure preserve published values; a committed reset changes every field.
        /// </summary>
        [Test]
        public async Task ResetPublishesOnlyAuthorizedCommittedSnapshot()
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            var provider = new Mock<IIaStatisticsProvider>(MockBehavior.Strict);
            IaStatisticsSnapshot initial = Snapshot(7);
            IaStatisticsSnapshot reset = Snapshot(0) with
            { StartTime = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) };
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(initial);
            provider.Setup(source => source.AuthorizeResetAsync(fixture.Context, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ServiceResult(StatusCodes.BadUserAccessDenied));
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            ServiceResult denied = await binding.ResetAsync(fixture.Context).ConfigureAwait(false);
            Assert.That(denied.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            provider.Verify(source => source.ResetAsync(It.IsAny<CancellationToken>()), Times.Never);
            AssertValue(quantity, 7);
            Assert.That(binding.StartTime.WrappedValue, Is.EqualTo(Variant.From(initial.StartTime)));

            provider.Setup(source => source.AuthorizeResetAsync(fixture.Context, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);
            provider.Setup(source => source.ResetAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Store unavailable"));
            await Assert.ThatAsync(async () => await binding.ResetAsync(fixture.Context).ConfigureAwait(false),
                Throws.InvalidOperationException);
            AssertValue(quantity, 7);
            Assert.That(binding.StartTime.WrappedValue, Is.EqualTo(Variant.From(initial.StartTime)));

            provider.Setup(source => source.ResetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(reset);
            ServiceResult result = await binding.ResetAsync(fixture.Context).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            AssertValue(quantity, 0);
            Assert.That(binding.StartTime.WrappedValue, Is.EqualTo(Variant.From(reset.StartTime)));
            await binding.ResetAsync(fixture.Context).ConfigureAwait(false);
            AssertValue(quantity, 0);
            provider.Verify(source => source.ResetAsync(It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        /// <summary>
        /// A reset authorization provider can re-enter refresh without waiting on the publication gate.
        /// </summary>
        [Test]
        public async Task ResetAuthorizationCanReenterRefresh()
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            var provider = new Mock<IIaStatisticsProvider>(MockBehavior.Strict);
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IaStatisticsSnapshot>(Snapshot(3)));
            IaStatisticsSnapshot reset = Snapshot(0) with
            { StartTime = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) };
            provider.Setup(source => source.ResetAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IaStatisticsSnapshot>(reset));
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            bool reentrantRefreshCompleted = false;
            Task reentrantRefresh = Task.CompletedTask;
            provider.Setup(source => source.AuthorizeResetAsync(fixture.Context, It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    ValueTask refresh = binding.RefreshAsync();
                    reentrantRefreshCompleted = refresh.IsCompletedSuccessfully;
                    reentrantRefresh = refresh.AsTask();
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });

            ServiceResult result = await binding.ResetAsync(fixture.Context).ConfigureAwait(false);
            await reentrantRefresh.ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(reentrantRefreshCompleted, Is.True);
            provider.Verify(source => source.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
            provider.Verify(source => source.ResetAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Committed refresh and reset notifications await sinks without blocking the invocation thread.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task StatisticsNotificationsAwaitAsyncSinks(bool reset, bool failSink)
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            var provider = new Mock<IIaStatisticsProvider>(MockBehavior.Strict);
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(9));
            provider.Setup(source => source.AuthorizeResetAsync(fixture.Context, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);
            provider.Setup(source => source.ResetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(0));
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var eventEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var eventRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var returned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var order = new List<string>();
            var failure = new InvalidOperationException("Notification failed");
            quantity.OnStateChanged = (_, _, _) => order.Add("on-sync");
            quantity.StateChanged += (_, _, _) => order.Add("event-sync");
            quantity.OnStateChangedAsync = async (_, _, _, token) =>
            {
                order.Add("on-async");
                cancellation.Cancel();
                Assert.That(token.CanBeCanceled, Is.False);
                entered.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                if (failSink)
                {
                    throw failure;
                }
                order.Add("on-complete");
            };
            quantity.StateChangedAsync += async (_, _, _, token) =>
            {
                Assert.That(token.CanBeCanceled, Is.False);
                order.Add("event-async");
                eventEntered.TrySetResult(true);
                await eventRelease.Task.ConfigureAwait(false);
                order.Add("event-complete");
            };
            quantity.StateChangedAsync += (_, _, _, _) =>
            {
                order.Add("event-second");
                return default;
            };
            owner.UpdateChangeMasks(NodeStateChangeMasks.Children);
            owner.OnStateChangedAsync = (_, _, _, _) =>
            {
                order.Add("owner");
                return default;
            };
            Task operation = Task.Run(async () =>
            {
                Task notification;
                if (reset)
                {
                    notification = binding.ResetAsync(fixture.Context, cancellation.Token).AsTask();
                }
                else
                {
                    notification = binding.RefreshAsync(cancellation.Token).AsTask();
                }
                returned.TrySetResult(true);
                await notification.ConfigureAwait(false);
            });
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await returned.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(operation.IsCompleted, Is.False);
                AssertValue(quantity, reset ? 0u : 9u);
                Assert.That(order, Is.EqualTo(new[] { "on-sync", "event-sync", "on-async" }));
                release.TrySetResult(true);
                if (failSink)
                {
                    await Assert.ThatAsync(async () =>
                        await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                        Throws.TypeOf<InvalidOperationException>().And.SameAs(failure));
                    Assert.That(quantity.ChangeMasks, Is.Not.EqualTo(NodeStateChangeMasks.None));
                    Assert.That(order, Is.EqualTo(new[] { "on-sync", "event-sync", "on-async" }));
                }
                else
                {
                    await eventEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(operation.IsCompleted, Is.False);
                    Assert.That(order, Is.EqualTo(
                        new[] { "on-sync", "event-sync", "on-async", "on-complete", "event-async" }));
                    eventRelease.TrySetResult(true);
                    await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(order, Is.EqualTo(
                        new[] { "on-sync", "event-sync", "on-async", "on-complete", "event-async",
                            "event-complete", "event-second", "owner" }));
                }
                AssertValue(quantity, reset ? 0u : 9u);
                Assert.That(quantity.StatusCode, Is.EqualTo((StatusCode)StatusCodes.UncertainLastUsableValue));
                Assert.That(quantity.Timestamp, Is.EqualTo(IaTestContext.Timestamp));
                Assert.That(binding.StartTime.Value, Is.EqualTo(Variant.From(IaTestContext.Timestamp)));
            }
            finally
            {
                release.TrySetResult(true);
                eventRelease.TrySetResult(true);
                if (failSink)
                {
                    await Assert.ThatAsync(async () =>
                        await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false),
                        Throws.TypeOf<InvalidOperationException>().And.SameAs(failure));
                }
                else
                {
                    await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Async observers can queue, but must not await, a reentrant operation requiring the held owner gate.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task StatisticsAsyncObserverCanQueueReentrantOperation(bool reset)
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            var provider = new Mock<IIaStatisticsProvider>(MockBehavior.Strict);
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(9));
            provider.Setup(source => source.AuthorizeResetAsync(fixture.Context, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);
            provider.Setup(source => source.ResetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(0));
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            using var cleanup = new CancellationTokenSource();
            int calls = 0;
            Task nested = Task.CompletedTask;
            quantity.OnStateChangedAsync = (_, _, _, _) =>
            {
                if (Interlocked.Increment(ref calls) != 1)
                {
                    return default;
                }
                nested = reset
                    ? binding.ResetAsync(fixture.Context, cleanup.Token).AsTask()
                    : binding.RefreshAsync(cleanup.Token).AsTask();
                return default;
            };
            Task operation = Task.Run(async () =>
            {
                if (reset)
                {
                    await binding.ResetAsync(fixture.Context).ConfigureAwait(false);
                }
                else
                {
                    await binding.RefreshAsync().ConfigureAwait(false);
                }
            });
            try
            {
                await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await nested.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            finally
            {
                cleanup.Cancel();
                try
                {
                    await nested.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
                {
                    // Drain a nested operation cancelled during cleanup after a failed bounded wait.
                }
                await operation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            provider.Verify(source => source.ReadAsync(It.IsAny<CancellationToken>()),
                reset ? Times.Never() : Times.Exactly(2));
            provider.Verify(source => source.ResetAsync(It.IsAny<CancellationToken>()),
                reset ? Times.Exactly(2) : Times.Never());
        }

        /// <summary>
        /// A synchronous statistics observer can queue refresh without blocking on the held publication gate.
        /// </summary>
        [Test]
        public async Task StatisticsObserverCanQueueReentrantRefresh()
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            var provider = new Mock<IIaStatisticsProvider>(MockBehavior.Strict);
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IaStatisticsSnapshot>(Snapshot(9)));
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            int observerCalls = 0;
            bool reentrantRefreshCompleted = false;
            Task reentrantRefresh = Task.CompletedTask;
            quantity.OnStateChanged = (_, _, _) =>
            {
                if (Interlocked.Exchange(ref observerCalls, 1) != 0)
                {
                    return;
                }

                ValueTask refresh = binding.RefreshAsync();
                reentrantRefreshCompleted = refresh.IsCompletedSuccessfully;
                reentrantRefresh = refresh.AsTask();
            };

            await binding.RefreshAsync().ConfigureAwait(false);
            await reentrantRefresh.ConfigureAwait(false);

            Assert.That(observerCalls, Is.EqualTo(1));
            Assert.That(reentrantRefreshCompleted, Is.False);
            AssertValue(quantity, 9);
            provider.Verify(source => source.ReadAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// Rejects an incomplete or ill-typed last quantity without changing an earlier quantity.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task InvalidSnapshotDoesNotPartiallyPublish(int invalid)
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState first = fixture.Quantity(owner, "First");
            BaseVariableState second = fixture.Quantity(owner, "Second");
            var provider = new Mock<IIaStatisticsProvider>();
            IaStatisticsSnapshot snapshot = Snapshot(9) with { Values = [Reading(9), Reading(10)] };
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IaStatisticsSnapshot>(snapshot));
            using var binding = new IaStatistics(fixture.Context, owner, [first, second], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            snapshot = snapshot with
            {
                Values = invalid switch
                {
                    0 => [Reading(99)],
                    1 => [Reading(99), new DataValue(Variant.From("wrong"), StatusCodes.Good, IaTestContext.Timestamp)],
                    _ => [Reading(99), new DataValue(Variant.From(100u))]
                }
            };
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
            AssertValue(first, 9);
            AssertValue(second, 10);
            Assert.That(first.StatusCode, Is.EqualTo((StatusCode)StatusCodes.UncertainLastUsableValue));
            Assert.That(first.Timestamp, Is.EqualTo(IaTestContext.Timestamp));
        }

        /// <summary>
        /// A later refresh or reset cannot publish while the earlier snapshot is notifying its components.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task StatisticsOperationsRemainSerializedThroughNotifications(
            bool firstIsReset,
            bool notificationFails)
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState first = fixture.Quantity(owner, "First");
            BaseVariableState second = fixture.Quantity(owner, "Second");
            using var callerCancellation = new CancellationTokenSource();
            var provider = new Mock<IIaStatisticsProvider>(MockBehavior.Strict);
            int readCalls = 0;
            int resetCalls = 0;
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).Returns(() =>
            {
                Interlocked.Increment(ref readCalls);
                return new ValueTask<IaStatisticsSnapshot>(
                    SnapshotPair(firstIsReset ? 30u : 10u));
            });
            provider.Setup(source => source.AuthorizeResetAsync(fixture.Context, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);
            provider.Setup(source => source.ResetAsync(It.IsAny<CancellationToken>())).Returns(() =>
            {
                Interlocked.Increment(ref resetCalls);
                return new ValueTask<IaStatisticsSnapshot>(SnapshotPair(20));
            });
            using var binding = new IaStatistics(fixture.Context, owner, [first, second], provider.Object);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var observed = new List<string>();
            var notificationFailure = new InvalidOperationException("Notification failed");
            int firstNotifications = 0;
            first.OnStateChangedAsync = async (_, _, _, token) =>
            {
                Assert.That(token.CanBeCanceled, Is.False);
                observed.Add($"first:{ReadQuantity(first)}");
                if (Interlocked.Increment(ref firstNotifications) == 1)
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    if (notificationFails)
                    {
                        throw notificationFailure;
                    }
                }
            };
            second.OnStateChangedAsync = (_, _, _, _) =>
            {
                observed.Add($"second:{ReadQuantity(first)}:{ReadQuantity(second)}");
                return default;
            };
            Task<Exception?> firstOperation = Task.Run(async () =>
            {
                try
                {
                    if (firstIsReset)
                    {
                        await binding.ResetAsync(fixture.Context, callerCancellation.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        await binding.RefreshAsync(callerCancellation.Token).ConfigureAwait(false);
                    }
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            });
            Task? secondOperation = null;

            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                secondOperation = Task.Run(async () =>
                {
                    if (firstIsReset)
                    {
                        ValueTask operation = binding.RefreshAsync();
                        secondStarted.TrySetResult(true);
                        await operation.ConfigureAwait(false);
                    }
                    else
                    {
                        ValueTask<ServiceResult> operation = binding.ResetAsync(fixture.Context);
                        secondStarted.TrySetResult(true);
                        await operation.ConfigureAwait(false);
                    }
                });
                await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                uint firstValue = firstIsReset ? 20u : 10u;
                Assert.That(secondOperation.IsCompleted, Is.False);
                AssertValue(first, firstValue);
                AssertValue(second, firstValue + 1);
                Assert.That(readCalls, Is.EqualTo(firstIsReset ? 0 : 1));
                Assert.That(resetCalls, Is.EqualTo(firstIsReset ? 1 : 0));

                callerCancellation.Cancel();
                Assert.That(firstOperation.IsCompleted, Is.False);
                AssertValue(first, firstValue);
                AssertValue(second, firstValue + 1);
                release.TrySetResult(true);
                Exception? firstError = await firstOperation.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                await secondOperation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                if (notificationFails)
                {
                    Assert.That(firstError, Is.SameAs(notificationFailure));
                }
                else
                {
                    Assert.That(firstError, Is.Null);
                }
                uint secondValue = firstIsReset ? 30u : 20u;
                AssertValue(first, secondValue);
                AssertValue(second, secondValue + 1);
                string[] expected = notificationFails
                    ? [$"first:{firstValue}", $"first:{secondValue}",
                        $"second:{secondValue}:{secondValue + 1}"]
                    : [$"first:{firstValue}", $"second:{firstValue}:{firstValue + 1}",
                        $"first:{secondValue}", $"second:{secondValue}:{secondValue + 1}"];
                Assert.That(observed, Is.EqualTo(expected));
            }
            finally
            {
                release.TrySetResult(true);
                await firstOperation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (secondOperation != null)
                {
                    await secondOperation.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// A cancelled reset waiting behind refresh notifications does not commit or overtake that refresh.
        /// </summary>
        [Test]
        public async Task CancelledResetWaitsBehindRefreshNotificationsWithoutCommitting()
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            var authorizationEntered =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var notificationEntered =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseNotification =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IIaStatisticsProvider>(MockBehavior.Strict);
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(9));
            provider.Setup(source => source.AuthorizeResetAsync(fixture.Context, It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    authorizationEntered.TrySetResult(true);
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            provider.Setup(source => source.ResetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(0));
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            quantity.OnStateChangedAsync = async (_, _, _, token) =>
            {
                Assert.That(token.CanBeCanceled, Is.False);
                notificationEntered.TrySetResult(true);
                await releaseNotification.Task.ConfigureAwait(false);
            };
            Task refresh = binding.RefreshAsync().AsTask();
            using var cancellation = new CancellationTokenSource();
            Task<ServiceResult> reset = binding.ResetAsync(fixture.Context, cancellation.Token).AsTask();
            try
            {
                await notificationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await authorizationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                cancellation.Cancel();
                await Assert.ThatAsync(async () => await reset.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());
                provider.Verify(source => source.ResetAsync(It.IsAny<CancellationToken>()), Times.Never);
                AssertValue(quantity, 9);
                Assert.That(refresh.IsCompleted, Is.False);
            }
            finally
            {
                releaseNotification.TrySetResult(true);
                await refresh.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A queued cancelled refresh cannot overtake the current acquisition or enter the provider.
        /// </summary>
        [Test]
        public async Task ResetAndRefreshAreSerialized()
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            var pending = new TaskCompletionSource<IaStatisticsSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IIaStatisticsProvider>(MockBehavior.Strict);
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).Returns(() =>
            {
                entered.SetResult(true);
                return new ValueTask<IaStatisticsSnapshot>(pending.Task);
            });
            provider.Setup(source => source.AuthorizeResetAsync(fixture.Context, It.IsAny<CancellationToken>()))
                .ReturnsAsync(ServiceResult.Good);
            provider.Setup(source => source.ResetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(0));
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            Task refresh = binding.RefreshAsync().AsTask();
            await entered.Task.ConfigureAwait(false);
            Task<ServiceResult> reset = binding.ResetAsync(fixture.Context).AsTask();
            provider.Verify(source => source.ResetAsync(It.IsAny<CancellationToken>()), Times.Never);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThatAsync(async () => await binding.RefreshAsync(cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            pending.SetResult(Snapshot(10));
            await refresh.ConfigureAwait(false);
            await reset.ConfigureAwait(false);
            AssertValue(quantity, 0);
            provider.Verify(source => source.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Rebinding restored application storage does not replace persisted collection time with startup time.
        /// </summary>
        [Test]
        public async Task RestoredProviderRetainsStartTime()
        {
            var fixture = new IaTestContext();
            IaStatisticsSnapshot persisted = Snapshot(400);
            var provider = new Mock<IIaStatisticsProvider>();
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(persisted);
            for (int i = 0; i < 2; i++)
            {
                BaseObjectState owner = fixture.Owner("Statistics" + i);
                BaseVariableState quantity = fixture.Quantity(owner);
                using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
                await binding.RefreshAsync().ConfigureAwait(false);
                AssertValue(quantity, 400);
                Assert.That(binding.StartTime.WrappedValue, Is.EqualTo(Variant.From(persisted.StartTime)));
            }
        }

        /// <summary>
        /// Cancellation before reset invocation prevents a commit; cancellation after commit still publishes it.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ResetCancellationRespectsTheProviderCommitBoundary(bool committed)
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            using var cancellation = new CancellationTokenSource();
            var provider = new Mock<IIaStatisticsProvider>(MockBehavior.Strict);
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(8));
            provider.Setup(source => source.AuthorizeResetAsync(fixture.Context, It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    if (!committed)
                    {
                        cancellation.Cancel();
                    }
                    return new ValueTask<ServiceResult>(ServiceResult.Good);
                });
            IaStatisticsSnapshot reset = Snapshot(0) with
            { StartTime = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) };
            provider.Setup(source => source.ResetAsync(It.IsAny<CancellationToken>())).Returns(() =>
            {
                cancellation.Cancel();
                return new ValueTask<IaStatisticsSnapshot>(reset);
            });
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            if (!committed)
            {
                await Assert.ThatAsync(async () => await binding.ResetAsync(fixture.Context, cancellation.Token)
                    .ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());
                provider.Verify(source => source.ResetAsync(It.IsAny<CancellationToken>()), Times.Never);
                AssertValue(quantity, 8);
                Assert.That(binding.StartTime.WrappedValue, Is.EqualTo(Variant.From(IaTestContext.Timestamp)));
                return;
            }
            ServiceResult result = await binding.ResetAsync(fixture.Context, cancellation.Token).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            AssertValue(quantity, 0);
            Assert.That(binding.StartTime.WrappedValue, Is.EqualTo(Variant.From(reset.StartTime)));
            provider.Verify(source => source.ResetAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// An acquisition that returns after cancellation does not overwrite the previous collection.
        /// </summary>
        [Test]
        public async Task RefreshCancellationPreservesPublishedCollection()
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            var provider = new Mock<IIaStatisticsProvider>();
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(10));
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).Returns(() =>
            {
                cancellation.Cancel();
                return new ValueTask<IaStatisticsSnapshot>(Snapshot(20));
            });
            await Assert.ThatAsync(async () => await binding.RefreshAsync(cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            AssertValue(quantity, 10);
            Assert.That(binding.StartTime.WrappedValue, Is.EqualTo(Variant.From(IaTestContext.Timestamp)));
        }

        /// <summary>
        /// An unavailable statistic has explicit bad quality; a successful null quantity is a type error.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingQuantitiesRequireBadQuality(bool unavailable)
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            IaStatisticsSnapshot snapshot = Snapshot(10);
            var provider = new Mock<IIaStatisticsProvider>();
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IaStatisticsSnapshot>(snapshot));
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            snapshot = snapshot with
            {
                Values = [new DataValue(Variant.Null,
                    unavailable ? StatusCodes.BadWaitingForInitialData : StatusCodes.Good, IaTestContext.Timestamp)]
            };
            if (!unavailable)
            {
                await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>());
                AssertValue(quantity, 10);
                return;
            }
            await binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(quantity.WrappedValue.IsNull, Is.True);
            Assert.That(quantity.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadWaitingForInitialData));
            Assert.That(quantity.Timestamp, Is.EqualTo(IaTestContext.Timestamp));
        }

        /// <summary>
        /// Publication owns an independent quantity payload after the provider reuses its array.
        /// </summary>
        [Test]
        public async Task PublishedStatisticsDoNotAliasProviderArrays()
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            quantity.ValueRank = ValueRanks.OneDimension;
            uint[] values = [10, 20];
            var provider = new Mock<IIaStatisticsProvider>();
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                Snapshot(0) with
                {
                    Values = [new DataValue(Variant.From(new ArrayOf<uint>(values.AsMemory())),
                        StatusCodes.Good, IaTestContext.Timestamp)]
                });
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            values[0] = 99;
            Assert.That(quantity.WrappedValue.TryGetValue(out ArrayOf<uint> published), Is.True);
            Assert.That(published.Span.ToArray(), Is.EqualTo(new uint[] { 10, 20 }));
        }

        /// <summary>
        /// A published matrix keeps its values and dimensions after the provider reuses its buffer.
        /// </summary>
        [Test]
        public async Task PublishedStatisticsDoNotAliasProviderMatrices()
        {
            double[] buffer = [1, 2, 3, 4, 5, 6];
            Variant published = await PublishThenMutateAsync(Opc.Ua.DataTypeIds.Double, ValueRanks.TwoDimensions,
                Variant.From(new ArrayOf<double>(buffer.AsMemory()).ToMatrix(2, 3)),
                () => buffer[0] = 99).ConfigureAwait(false);
            Assert.That(published.TryGetValue(out MatrixOf<double> matrix), Is.True);
            Assert.That(matrix.Span.ToArray(), Is.EqualTo(new double[] { 1, 2, 3, 4, 5, 6 }));
            Assert.That(matrix.Dimensions, Is.EqualTo(s_twoByThree));
        }

        /// <summary>
        /// Published byte strings own their bytes as scalars and as array elements.
        /// </summary>
        [TestCase(ValueRanks.Scalar)]
        [TestCase(ValueRanks.OneDimension)]
        public async Task PublishedStatisticsDoNotAliasProviderByteStrings(int rank)
        {
            byte[] bytes = [1, 2, 3];
            var value = new ByteString(bytes.AsMemory());
            ArrayOf<ByteString> values = [value];
            Variant published = await PublishThenMutateAsync(Opc.Ua.DataTypeIds.ByteString, rank,
                rank == ValueRanks.Scalar ? Variant.From(value) : Variant.From(values),
                () => bytes[0] = 99).ConfigureAwait(false);
            ByteString actual;
            if (rank == ValueRanks.Scalar)
            {
                Assert.That(published.TryGetValue(out actual), Is.True);
            }
            else
            {
                Assert.That(published.TryGetValue(out ArrayOf<ByteString> array), Is.True);
                Assert.That(array, Has.Count.EqualTo(1));
                actual = array[0];
            }
            Assert.That(actual.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        /// <summary>
        /// A published opaque node id owns its identifier bytes.
        /// </summary>
        [Test]
        public async Task PublishedStatisticsDoNotAliasOpaqueNodeIds()
        {
            byte[] bytes = [4, 5, 6];
            Variant published = await PublishThenMutateAsync(Opc.Ua.DataTypeIds.NodeId, ValueRanks.Scalar,
                new Variant(new NodeId(new ByteString(bytes.AsMemory()), 1)),
                () => bytes[0] = 99).ConfigureAwait(false);
            Assert.That(published.TryGetValue(out NodeId nodeId), Is.True);
            Assert.That(nodeId.TryGetValue(out ByteString identifier), Is.True);
            Assert.That(identifier.Span.ToArray(), Is.EqualTo(new byte[] { 4, 5, 6 }));
            Assert.That(nodeId.NamespaceIndex, Is.EqualTo((ushort)1));
        }

        /// <summary>
        /// A published Variant array owns the arrays nested in its elements.
        /// </summary>
        [Test]
        public async Task PublishedStatisticsDoNotAliasNestedVariantArrays()
        {
            uint[] values = [7, 8];
            ArrayOf<Variant> outer = [Variant.From(new ArrayOf<uint>(values.AsMemory()))];
            Variant published = await PublishThenMutateAsync(Opc.Ua.DataTypeIds.BaseDataType, ValueRanks.OneDimension,
                Variant.From(outer), () => values[0] = 99).ConfigureAwait(false);
            Assert.That(published.TryGetValue(out ArrayOf<Variant> elements), Is.True);
            Assert.That(elements, Has.Count.EqualTo(1));
            Assert.That(elements[0].TryGetValue(out ArrayOf<uint> nested), Is.True);
            Assert.That(nested.Span.ToArray(), Is.EqualTo(new uint[] { 7, 8 }));
        }

        /// <summary>
        /// Recursive provider storage is bounded before any published node changes.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        public async Task CyclicContainersRejectBeforePublication(bool matrix, bool dataValues, bool scalarWrapper)
        {
            Variant cyclic;
            if (dataValues)
            {
                var items = new DataValue[1];
                var values = new ArrayOf<DataValue>(items.AsMemory());
                cyclic = matrix ? new Variant(values.ToMatrix(1, 1)) : new Variant(values);
                items[0] = new DataValue(cyclic);
            }
            else
            {
                var items = new Variant[1];
                var values = new ArrayOf<Variant>(items.AsMemory());
                cyclic = matrix ? new Variant(values.ToMatrix(1, 1)) : new Variant(values);
                items[0] = scalarWrapper ? new Variant(new DataValue(cyclic)) : cyclic;
            }
            await AssertRejectedSnapshotAsync(cyclic, StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
        }

        /// <summary>
        /// Shared acyclic branches remain supported and detach each nested mutable leaf.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SharedAcyclicContainersRemainDetached(bool matrix)
        {
            byte[] buffer = [1, 2, 3];
            Variant[] shared = [new Variant(ByteString.From(buffer))];
            var branch = new Variant(new ArrayOf<Variant>(shared.AsMemory()));
            var elements = new ArrayOf<DataValue>([new DataValue(branch), new DataValue(branch)]);
            Variant source = matrix ? new Variant(elements.ToMatrix(1, 2)) : new Variant(elements);
            Variant published = await PublishThenMutateAsync(Opc.Ua.DataTypeIds.BaseDataType,
                matrix ? ValueRanks.TwoDimensions : ValueRanks.OneDimension, source, () =>
                {
                    buffer[0] = 99;
                    shared[0] = Variant.Null;
                }).ConfigureAwait(false);
            ArrayOf<DataValue> copies;
            if (matrix)
            {
                Assert.That(published.TryGetValue(out MatrixOf<DataValue> values), Is.True);
                Assert.That(values.Dimensions, Is.EqualTo(new[] { 1, 2 }));
                copies = ArrayOf.Create(values.Span);
            }
            else
            {
                Assert.That(published.TryGetValue(out copies), Is.True);
            }
            Assert.That(copies.Count, Is.EqualTo(2));
            for (int i = 0; i < copies.Count; i++)
            {
                Assert.That(copies[i].WrappedValue.TryGetValue(out ArrayOf<Variant> nested), Is.True);
                Assert.That(nested.Count, Is.EqualTo(1));
                Assert.That(nested[0].TryGetValue(out ByteString bytes), Is.True);
                Assert.That(bytes.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            }
        }

        /// <summary>
        /// The SDK default bounds copies when the system context exposes no server quotas.
        /// </summary>
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public async Task DefaultNestingLimitPreservesBoundary(int offset)
        {
            int depth = DefaultEncodingLimits.MaxEncodingNestingLevels + offset;
            Variant value = Variant.From(7u);
            for (int i = 0; i < depth; i++)
            {
                value = new Variant(new ArrayOf<Variant>([value]));
            }
            if (offset > 0)
            {
                await AssertRejectedSnapshotAsync(value, StatusCodes.BadEncodingLimitsExceeded).ConfigureAwait(false);
                return;
            }
            Variant published = await PublishThenMutateAsync(Opc.Ua.DataTypeIds.BaseDataType,
                ValueRanks.OneDimension, value, static () => { }).ConfigureAwait(false);
            for (int i = 0; i < depth; i++)
            {
                Assert.That(published.TryGetValue(out ArrayOf<Variant> nested), Is.True);
                Assert.That(nested.Count, Is.EqualTo(1));
                published = nested[0];
            }
            Assert.That(published.TryGetValue(out uint number), Is.True);
            Assert.That(number, Is.EqualTo(7u));
        }

        /// <summary>
        /// A server's configured nesting limit is used rather than a fresh default message context.
        /// </summary>
        [TestCase(0)]
        [TestCase(3)]
        [TestCase(201)]
        public async Task ServerNestingLimitPreservesBoundary(int limit)
        {
            var fixture = new IaTestContext();
            ServiceMessageContext messages = ServiceMessageContext.Create(fixture.Context.Telemetry);
            messages.MaxEncodingNestingLevels = limit;
            var server = new Mock<IServerInternal>(MockBehavior.Strict);
            server.SetupGet(item => item.Telemetry).Returns(fixture.Context.Telemetry);
            server.SetupGet(item => item.NamespaceUris).Returns(fixture.Context.NamespaceUris);
            server.SetupGet(item => item.ServerUris).Returns(new StringTable());
            server.SetupGet(item => item.TypeTree).Returns(new TypeTable(fixture.Context.NamespaceUris));
            server.SetupGet(item => item.Factory).Returns(fixture.Context.EncodeableFactory);
            server.SetupGet(item => item.MessageContext).Returns(messages);
            var context = new ServerSystemContext(server.Object) { NodeIdFactory = fixture };
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            quantity.DataType = Opc.Ua.DataTypeIds.BaseDataType;
            quantity.ValueRank = ValueRanks.Any;
            Variant accepted = Variant.From(7u);
            for (int i = 0; i < limit; i++)
            {
                accepted = new Variant(new ArrayOf<Variant>([accepted]));
            }
            Variant rejected = new(new ArrayOf<Variant>([accepted]));
            var provider = new Mock<IIaStatisticsProvider>(MockBehavior.Strict);
            provider.SetupSequence(item => item.ReadAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Snapshot(7) with
                {
                    Values = [new DataValue(accepted, StatusCodes.Good, IaTestContext.Timestamp)]
                })
                .ReturnsAsync(Snapshot(0) with
                {
                    Values = [new DataValue(rejected, StatusCodes.Good, IaTestContext.Timestamp)]
                });
            using var binding = new IaStatistics(context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
            Variant current = quantity.WrappedValue;
            for (int i = 0; i < limit; i++)
            {
                Assert.That(current.TryGetValue(out ArrayOf<Variant> nested), Is.True);
                Assert.That(nested.Count, Is.EqualTo(1));
                current = nested[0];
            }
            Assert.That(current.TryGetValue(out uint number), Is.True);
            Assert.That(number, Is.EqualTo(7u));
            Assert.That(quantity.WrappedValue.TypeInfo, Is.EqualTo(accepted.TypeInfo));
        }

        /// <summary>
        /// Malformed mutable scalars are rejected before replacing the previous publication.
        /// </summary>
        [TestCase(BuiltInType.ByteString)]
        [TestCase(BuiltInType.NodeId)]
        [TestCase(BuiltInType.ExpandedNodeId)]
        [TestCase(BuiltInType.ExtensionObject)]
        [TestCase(BuiltInType.DataValue)]
        [TestCase(BuiltInType.LocalizedText)]
        public async Task MalformedMutableScalarRejectsBeforePublication(BuiltInType builtInType)
        {
            byte[] buffer = [1, 2, 3];
            var payload = new ArrayOf<byte>(buffer.AsMemory());
            // Compatibility coverage for explicit legacy type tags. TODO: Remove when the constructor is removed.
#pragma warning disable CS0618
            var malformed = new Variant(payload, TypeInfo.Create(builtInType, ValueRanks.Scalar));
#pragma warning restore CS0618
            Assert.That(malformed.TypeInfo.BuiltInType, Is.EqualTo(builtInType));
            Assert.That(malformed.TypeInfo.IsScalar, Is.True);
            Assert.That(malformed.TryGetValue(out ByteString _), Is.False);
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            quantity.DataType = builtInType == BuiltInType.ByteString
                ? Opc.Ua.DataTypeIds.ByteString
                : Opc.Ua.DataTypeIds.BaseDataType;
            Variant initial = builtInType == BuiltInType.ByteString
                ? new Variant(ByteString.From(new byte[] { 7, 8 }.AsSpan()))
                : Variant.From(7u);
            var provider = new Mock<IIaStatisticsProvider>(MockBehavior.Strict);
            provider.SetupSequence(item => item.ReadAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Snapshot(7) with
                {
                    Values = [new DataValue(initial, StatusCodes.UncertainLastUsableValue, IaTestContext.Timestamp)]
                })
                .ReturnsAsync(Snapshot(0) with
                {
                    StartTime = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                    Values = [new DataValue(malformed, StatusCodes.Good, IaTestContext.Timestamp)]
                });
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            Variant previous = quantity.WrappedValue;
            StatusCode previousStatus = quantity.StatusCode;
            DateTime previousTimestamp = quantity.Timestamp;
            Variant previousStartTime = binding.StartTime.Value;
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNotSupported));
            buffer[0] = 99;
            Assert.That(quantity.WrappedValue, Is.EqualTo(previous));
            Assert.That(quantity.StatusCode, Is.EqualTo(previousStatus));
            Assert.That(quantity.Timestamp, Is.EqualTo(previousTimestamp));
            Assert.That(binding.StartTime.Value, Is.EqualTo(previousStartTime));
            if (builtInType == BuiltInType.ByteString)
            {
                Assert.That(quantity.WrappedValue.TryGetValue(out ByteString bytes), Is.True);
                Assert.That(bytes.Span.ToArray(), Is.EqualTo(new byte[] { 7, 8 }));
            }
        }

        /// <summary>
        /// Typed null mutable scalars retain their declared type and remain publishable.
        /// </summary>
        [TestCase(BuiltInType.ByteString)]
        [TestCase(BuiltInType.NodeId)]
        [TestCase(BuiltInType.ExpandedNodeId)]
        [TestCase(BuiltInType.ExtensionObject)]
        [TestCase(BuiltInType.DataValue)]
        [TestCase(BuiltInType.LocalizedText)]
        public async Task TypedNullMutableScalarRemainsPublishable(BuiltInType builtInType)
        {
            Variant value = builtInType switch
            {
                BuiltInType.ByteString => new Variant(ByteString.Null),
                BuiltInType.NodeId => new Variant(NodeId.Null),
                BuiltInType.ExpandedNodeId => new Variant(ExpandedNodeId.Null),
                BuiltInType.ExtensionObject => new Variant(ExtensionObject.Null),
                BuiltInType.DataValue => new Variant(DataValue.Null),
                BuiltInType.LocalizedText => new Variant(LocalizedText.Null),
                _ => throw new ArgumentOutOfRangeException(nameof(builtInType))
            };
            Variant published = await PublishThenMutateAsync(Opc.Ua.DataTypeIds.BaseDataType, ValueRanks.Scalar,
                value, static () => { }).ConfigureAwait(false);
            Assert.That(published.TypeInfo, Is.EqualTo(value.TypeInfo));
            Assert.That(published, Is.EqualTo(value));
        }

        /// <summary>
        /// Abstract numeric container tags are rejected without losing elements or invoking nested custom code.
        /// </summary>
        [TestCase(BuiltInType.Number, false, false)]
        [TestCase(BuiltInType.Integer, false, false)]
        [TestCase(BuiltInType.UInteger, false, false)]
        [TestCase(BuiltInType.Number, true, false)]
        [TestCase(BuiltInType.Integer, true, false)]
        [TestCase(BuiltInType.UInteger, true, false)]
        [TestCase(BuiltInType.Number, false, true)]
        [TestCase(BuiltInType.Integer, false, true)]
        [TestCase(BuiltInType.UInteger, false, true)]
        [TestCase(BuiltInType.Number, true, true)]
        [TestCase(BuiltInType.Integer, true, true)]
        [TestCase(BuiltInType.UInteger, true, true)]
        public async Task AbstractNumericContainersRejectBeforePublicationOrCustomCode(
            BuiltInType builtInType,
            bool matrix,
            bool customElement)
        {
            var source = new LossyStructure { Value = 1 };
            ArrayOf<Variant> elements = customElement
                ? [new Variant(new ExtensionObject(source)), Variant.From(8u)]
                : [Variant.From(7u), Variant.From(8u)];
            int rank = matrix ? ValueRanks.TwoDimensions : ValueRanks.OneDimension;
            // Compatibility coverage for explicit legacy type tags. TODO: Remove when the constructor is removed.
#pragma warning disable CS0618
            Variant value = matrix
                ? new Variant(elements.ToMatrix(1, 2), TypeInfo.Create(builtInType, rank))
                : new Variant(elements, TypeInfo.Create(builtInType, rank));
#pragma warning restore CS0618
            Assert.That(value.TypeInfo.BuiltInType, Is.EqualTo(builtInType));
            Assert.That(value.TypeInfo.ValueRank, Is.EqualTo(rank));
            if (matrix)
            {
                Assert.That(value.TryGetMatrix(out MatrixOf<Variant> stored, builtInType), Is.True);
                Assert.That(stored.Dimensions, Is.EqualTo(new int[] { 1, 2 }));
                Assert.That(stored.Span.Length, Is.EqualTo(2));
            }
            else
            {
                Assert.That(value.TryGetArray(out ArrayOf<Variant> stored, builtInType), Is.True);
                Assert.That(stored.Count, Is.EqualTo(2));
            }
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            quantity.DataType = Opc.Ua.DataTypeIds.BaseDataType;
            quantity.ValueRank = ValueRanks.Any;
            var provider = new Mock<IIaStatisticsProvider>(MockBehavior.Strict);
            provider.SetupSequence(item => item.ReadAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Snapshot(7))
                .ReturnsAsync(Snapshot(0) with
                {
                    StartTime = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                    Values = [new DataValue(value, StatusCodes.Good, IaTestContext.Timestamp)]
                });
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            Variant previous = quantity.WrappedValue;
            StatusCode previousStatus = quantity.StatusCode;
            DateTime previousTimestamp = quantity.Timestamp;
            Variant previousStartTime = binding.StartTime.Value;
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(source.CloneCalls, Is.Zero);
            Assert.That(source.EncodeCalls, Is.Zero);
            Assert.That(source.DecodeCalls, Is.Zero);
            Assert.That(source.EqualityCalls, Is.Zero);
            Assert.That(quantity.WrappedValue, Is.EqualTo(previous));
            Assert.That(quantity.StatusCode, Is.EqualTo(previousStatus));
            Assert.That(quantity.Timestamp, Is.EqualTo(previousTimestamp));
            Assert.That(binding.StartTime.Value, Is.EqualTo(previousStartTime));
        }

        /// <summary>
        /// Published text remains stable after translation dictionaries or formatting arguments change.
        /// </summary>
        [TestCase(ValueRanks.Scalar, false)]
        [TestCase(ValueRanks.OneDimension, false)]
        [TestCase(ValueRanks.TwoDimensions, false)]
        [TestCase(ValueRanks.Scalar, true)]
        [TestCase(ValueRanks.OneDimension, true)]
        [TestCase(ValueRanks.TwoDimensions, true)]
        public async Task PublishedLocalizedTextDoesNotAliasTranslationDictionary(int rank, bool nested)
        {
            var translations = new Dictionary<string, string>
            {
                ["en-US"] = "Original",
                ["fr-FR"] = "Initial"
            };
            var text = new LocalizedText(translations);
            await AssertLocalizedTextPublicationStableAsync(text, rank, nested,
                () => translations["fr-FR"] = "Changed").ConfigureAwait(false);
        }

        /// <summary>
        /// Replacing a provider formatting argument cannot change previously published text.
        /// </summary>
        [TestCase(ValueRanks.Scalar, false)]
        [TestCase(ValueRanks.OneDimension, false)]
        [TestCase(ValueRanks.TwoDimensions, false)]
        [TestCase(ValueRanks.Scalar, true)]
        [TestCase(ValueRanks.OneDimension, true)]
        [TestCase(ValueRanks.TwoDimensions, true)]
        public async Task PublishedLocalizedTextDoesNotAliasArgumentArray(int rank, bool nested)
        {
            object[] args = ["Original"];
            var text = new LocalizedText("Statistic", "en-US", "Value: {0}", args);
            await AssertLocalizedTextPublicationStableAsync(text, rank, nested,
                () => args[0] = "Changed").ConfigureAwait(false);
        }

        /// <summary>
        /// Unsupported mutable formatting arguments reject the entire snapshot before publication.
        /// </summary>
        [TestCase(ValueRanks.Scalar, false)]
        [TestCase(ValueRanks.OneDimension, false)]
        [TestCase(ValueRanks.TwoDimensions, false)]
        [TestCase(ValueRanks.Scalar, true)]
        [TestCase(ValueRanks.OneDimension, true)]
        [TestCase(ValueRanks.TwoDimensions, true)]
        public async Task MutableLocalizedTextArgumentRejectsSnapshot(int rank, bool nested)
        {
            var text = new LocalizedText("Statistic", "en-US", "{0}", new System.Text.StringBuilder("Original"));
            ArrayOf<LocalizedText> elements = [text, text];
            Variant value = rank switch
            {
                ValueRanks.Scalar => new Variant(text),
                ValueRanks.OneDimension => new Variant(elements),
                _ => new Variant(elements.ToMatrix(1, 2))
            };
            if (nested)
            {
                value = new Variant(new DataValue(value, StatusCodes.Good, IaTestContext.Timestamp));
            }
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            quantity.DataType = Opc.Ua.DataTypeIds.BaseDataType;
            quantity.ValueRank = ValueRanks.Any;
            var provider = new Mock<IIaStatisticsProvider>();
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(7));
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            Variant previous = quantity.WrappedValue;
            Variant start = binding.StartTime.WrappedValue;
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                Snapshot(0) with
                {
                    StartTime = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                    Values = [new DataValue(value, StatusCodes.Good, IaTestContext.Timestamp)]
                });
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(quantity.WrappedValue, Is.EqualTo(previous));
            Assert.That(binding.StartTime.WrappedValue, Is.EqualTo(start));
            Assert.That(quantity.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(quantity.Timestamp, Is.EqualTo(IaTestContext.Timestamp));
        }

        /// <summary>
        /// Captures publication before mutation without requiring a particular translation-copy policy.
        /// </summary>
        private static async Task AssertLocalizedTextPublicationStableAsync(
            LocalizedText text, int rank, bool nested, Action mutate)
        {
            LocalizedText[] elements = [text, text];
            Variant value = rank switch
            {
                ValueRanks.Scalar => new Variant(text),
                ValueRanks.OneDimension => Variant.From(new ArrayOf<LocalizedText>(elements.AsMemory())),
                _ => Variant.From(new ArrayOf<LocalizedText>(elements.AsMemory()).ToMatrix(1, 2))
            };
            if (nested)
            {
                value = new Variant(new DataValue(value, StatusCodes.Good, IaTestContext.Timestamp));
            }
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            quantity.DataType = Opc.Ua.DataTypeIds.BaseDataType;
            quantity.ValueRank = nested ? ValueRanks.Scalar : rank;
            var provider = new Mock<IIaStatisticsProvider>();
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                Snapshot(0) with
                {
                    Values = [new DataValue(value, StatusCodes.Good, IaTestContext.Timestamp)]
                });
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            LocalizedText published = ReadPublishedText(quantity.WrappedValue, rank, nested);
            string? original = published.Text;
            string? originalFrench = published.FilterByPreferredLocales(["fr-FR"]).Text;
            string? providerOriginal = text.FilterByPreferredLocales(["fr-FR"]).Text;
            mutate();
            Assert.That(text.FilterByPreferredLocales(["fr-FR"]).Text, Is.Not.EqualTo(providerOriginal));
            elements[0] = new LocalizedText("Replacement");
            LocalizedText after = ReadPublishedText(quantity.WrappedValue, rank, nested);
            Assert.That(after.Text, Is.EqualTo(original));
            Assert.That(after.FilterByPreferredLocales(["fr-FR"]).Text, Is.EqualTo(originalFrench));
            Assert.That(quantity.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            Assert.That(quantity.Timestamp, Is.EqualTo(IaTestContext.Timestamp));
        }

        /// <summary>
        /// Reads scalar, array or matrix text through an optional nested DataValue.
        /// </summary>
        private static LocalizedText ReadPublishedText(Variant value, int rank, bool nested)
        {
            if (nested)
            {
                Assert.That(value.TryGetValue(out DataValue data), Is.True);
                Assert.That(data.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(data.SourceTimestamp, Is.EqualTo(IaTestContext.Timestamp));
                value = data.WrappedValue;
            }
            if (rank == ValueRanks.Scalar)
            {
                Assert.That(value.TryGetValue(out LocalizedText scalar), Is.True);
                return scalar;
            }
            if (rank == ValueRanks.OneDimension)
            {
                Assert.That(value.TryGetValue(out ArrayOf<LocalizedText> array), Is.True);
                Assert.That(array.Count, Is.EqualTo(2));
                return array[0];
            }
            Assert.That(value.TryGetValue(out MatrixOf<LocalizedText> matrix), Is.True);
            Assert.That(matrix.Dimensions, Is.EqualTo(new int[] { 1, 2 }));
            return matrix.Span[0];
        }

        /// <summary>
        /// A registered custom structure is rejected before its codec, equality or clone is invoked.
        /// </summary>
        [Test]
        public async Task RegisteredCustomStructuresAreRejectedBeforeCodecOrClone()
        {
            var fixture = new IaTestContext();
            var source = new LossyStructure { Value = 1 };
            int activations = 0;
            fixture.Context.EncodeableFactory.Builder.AddEncodeableType(
                ActivatorFor(() =>
                {
                    activations++;
                    return new LossyStructure();
                })).Commit();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            quantity.DataType = Opc.Ua.DataTypeIds.BaseDataType;
            var provider = new Mock<IIaStatisticsProvider>();
            provider.Setup(item => item.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                Snapshot(0) with
                {
                    Values = [new DataValue(new Variant(new ExtensionObject(source)),
                        StatusCodes.Good, IaTestContext.Timestamp)]
                });
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            int registeredActivations = activations;
            Variant previous = quantity.WrappedValue;
            StatusCode previousStatus = quantity.StatusCode;
            DateTime previousTimestamp = quantity.Timestamp;
            Variant previousStartTime = binding.StartTime.Value;
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNotSupported));
            Assert.That(source.EncodeCalls, Is.Zero);
            Assert.That(source.DecodeCalls, Is.Zero);
            Assert.That(source.CloneCalls, Is.Zero);
            Assert.That(source.EqualityCalls, Is.Zero);
            Assert.That(activations, Is.EqualTo(registeredActivations));
            Assert.That(quantity.WrappedValue, Is.EqualTo(previous));
            Assert.That(quantity.StatusCode, Is.EqualTo(previousStatus));
            Assert.That(quantity.Timestamp, Is.EqualTo(previousTimestamp));
            Assert.That(binding.StartTime.Value, Is.EqualTo(previousStartTime));
        }

        /// <summary>
        /// A supported Argument owns its dimensions and deferred localized text.
        /// </summary>
        [Test]
        public async Task PublishedStatisticsDetachSupportedArgumentStorage()
        {
            uint[] dimensions = [2, 3];
            var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["en-US"] = "Input {0}",
                ["de-DE"] = "Eingabe {0}"
            };
            object[] args = ["Original"];
            var argument = new Argument
            {
                Name = "Input",
                ArrayDimensions = new ArrayOf<uint>(dimensions.AsMemory()),
                Description = new LocalizedText(translations,
                    new TranslationInfo("argument", "en-US", "Fallback {0}", args))
                    .FilterByPreferredLocales(["en-US"])
            };
            Variant published = await PublishThenMutateAsync(Opc.Ua.DataTypeIds.BaseDataType, ValueRanks.Scalar,
                new Variant(new ExtensionObject(argument)), () =>
                {
                    dimensions[0] = 99;
                    translations["de-DE"] = "Changed {0}";
                    args[0] = "Changed";
                }).ConfigureAwait(false);
            Assert.That(published.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out Argument? copy), Is.True);
            Assert.That(copy, Is.Not.SameAs(argument));
            Assert.That(copy!.Name, Is.EqualTo("Input"));
            Assert.That(copy.ArrayDimensions.Span.ToArray(), Is.EqualTo(new uint[] { 2, 3 }));
            Assert.That(copy.Description.Text, Is.EqualTo("Input Original"));
            Assert.That(copy.Description.Translations, Is.EquivalentTo(new Dictionary<string, string>
            {
                ["en-US"] = "Input {0}",
                ["de-DE"] = "Eingabe {0}"
            }));
            Assert.That(copy.Description.Translations, Is.Not.SameAs(translations));
            Assert.That(copy.Description.TranslationInfo.Args, Is.Not.SameAs(args));
            Assert.That(copy.Description.FilterByPreferredLocales(["de-DE"]).Text, Is.EqualTo("Eingabe Original"));
        }

        /// <summary>
        /// A supported Argument copy preserves null and explicitly empty dimensions.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task PublishedArgumentPreservesNullAndEmptyDimensions(bool nullDimensions)
        {
            var argument = new Argument
            {
                Name = "Input",
                ArrayDimensions = nullDimensions ? ArrayOf<uint>.Null : ArrayOf<uint>.Empty
            };
            Variant published = await PublishThenMutateAsync(Opc.Ua.DataTypeIds.BaseDataType, ValueRanks.Scalar,
                new Variant(new ExtensionObject(argument)), static () => { }).ConfigureAwait(false);
            Assert.That(published.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out Argument? copy), Is.True);
            Assert.That(copy!.ArrayDimensions.IsNull, Is.EqualTo(nullDimensions));
            Assert.That(copy.ArrayDimensions.Count, Is.Zero);
        }

        /// <summary>
        /// A non-null runtime-only Argument value is rejected before the existing publication changes.
        /// </summary>
        [Test]
        public async Task ArgumentWithRuntimeOnlyValueIsRejectedBeforePublication()
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            quantity.DataType = Opc.Ua.DataTypeIds.BaseDataType;
            var runtimeValue = new Mock<ICloneable>();
            runtimeValue.Setup(value => value.Clone()).Throws(new InvalidOperationException("Clone invoked"));
            runtimeValue.Setup(value => value.ToString()).Throws(new InvalidOperationException("Formatting invoked"));
            var provider = new Mock<IIaStatisticsProvider>();
            provider.SetupSequence(item => item.ReadAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Snapshot(7))
                .ReturnsAsync(Snapshot(0) with
                {
                    Values = [new DataValue(new Variant(new ExtensionObject(new Argument
                    {
                        Name = "Input",
                        Value = runtimeValue.Object
                    })), StatusCodes.Good, IaTestContext.Timestamp)]
                });
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            Variant previous = quantity.WrappedValue;
            StatusCode previousStatus = quantity.StatusCode;
            DateTime previousTimestamp = quantity.Timestamp;
            Variant previousStartTime = binding.StartTime.Value;
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNotSupported));
            runtimeValue.Verify(value => value.Clone(), Times.Never);
            runtimeValue.Verify(value => value.ToString(), Times.Never);
            Assert.That(quantity.WrappedValue, Is.EqualTo(previous));
            Assert.That(quantity.StatusCode, Is.EqualTo(previousStatus));
            Assert.That(quantity.Timestamp, Is.EqualTo(previousTimestamp));
            Assert.That(binding.StartTime.Value, Is.EqualTo(previousStartTime));
        }

        /// <summary>
        /// An extension object with an XML, JSON or null body owns its opaque type identifier.
        /// </summary>
        [TestCase("xml")]
        [TestCase("json")]
        [TestCase("null")]
        public async Task PublishedStatisticsDoNotAliasExtensionObjectTypeIds(string body)
        {
            byte[] typeBytes = [7, 8];
            var typeId = new ExpandedNodeId(new ByteString(typeBytes.AsMemory()), 1);
            ExtensionObject extension = body switch
            {
                "xml" => new ExtensionObject(typeId, XmlElement.From("<Value>1</Value>")),
                "json" => new ExtensionObject(typeId, "{\"Value\":1}"),
                _ => new ExtensionObject(typeId)
            };
            Variant published = await PublishThenMutateAsync(Opc.Ua.DataTypeIds.BaseDataType, ValueRanks.Scalar,
                new Variant(extension), () => typeBytes[0] = 99).ConfigureAwait(false);
            Assert.That(published.TryGetValue(out ExtensionObject copy), Is.True);
            AssertOpaqueTypeId(copy, [7, 8]);
            switch (body)
            {
                case "xml":
                    Assert.That(copy.TryGetAsXml(out XmlElement xml), Is.True);
                    Assert.That(xml.OuterXml, Is.EqualTo("<Value>1</Value>"));
                    break;
                case "json":
                    Assert.That(copy.TryGetAsJson(out string? json), Is.True);
                    Assert.That(json, Is.EqualTo("{\"Value\":1}"));
                    break;
                default:
                    Assert.That(copy.TryGetValue(out IEncodeable? _), Is.False);
                    Assert.That(copy.TryGetAsBinary(out ByteString _), Is.False);
                    Assert.That(copy.TryGetAsXml(out XmlElement _), Is.False);
                    Assert.That(copy.TryGetAsJson(out string? _), Is.False);
                    break;
            }
        }

        /// <summary>
        /// Unregistered receivers are rejected without invoking a potentially unsafe clone or decoder.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task UnregisteredStructuresAreRejectedBeforeCloneOrDecode(bool cloneReturnsSource)
        {
            double[] buffer = [1, 2];
            var source = new Mock<IEncodeable>();
            source.SetupGet(body => body.TypeId).Returns(new ExpandedNodeId(9021u, 1));
            var shallow = new Mock<IEncodeable>();
            shallow.Setup(body => body.Decode(It.IsAny<IDecoder>())).Callback(() => buffer[0] = 99);
            source.Setup(body => body.Clone()).Returns(() => cloneReturnsSource ? source.Object : shallow.Object);
            source.Setup(body => body.Decode(It.IsAny<IDecoder>())).Callback(() => buffer[0] = 99);
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            quantity.DataType = Opc.Ua.DataTypeIds.BaseDataType;
            quantity.ValueRank = ValueRanks.Scalar;
            Variant previous = quantity.WrappedValue;
            var provider = new Mock<IIaStatisticsProvider>();
            provider.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                Snapshot(0) with
                {
                    Values = [new DataValue(new Variant(new ExtensionObject(source.Object)),
                        StatusCodes.Good, IaTestContext.Timestamp)]
                });
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNotSupported));
            source.Verify(body => body.Clone(), Times.Never);
            source.Verify(body => body.Decode(It.IsAny<IDecoder>()), Times.Never);
            source.Verify(body => body.Encode(It.IsAny<IEncoder>()), Times.Never);
            shallow.Verify(body => body.Decode(It.IsAny<IDecoder>()), Times.Never);
            Assert.That(buffer[0], Is.EqualTo(1));
            Assert.That(quantity.WrappedValue, Is.EqualTo(previous));
        }

        /// <summary>
        /// Wire equality does not establish preservation of an enumeration's deferred translations.
        /// </summary>
        [Test]
        public void EnumValueWireEqualityDoesNotProveDeferredPreservation()
        {
            var fixture = new IaTestContext();
            var source = new EnumValueType
            {
                Value = 42,
                DisplayName = new LocalizedText(new Dictionary<string, string>
                {
                    ["en-US"] = "Value {0}",
                    ["de-DE"] = "Wert {0}"
                }, new TranslationInfo("enum-key", "en-US", "Fallback {0}", "Original"))
                    .FilterByPreferredLocales(["en-US"])
            };
            byte[] encoded;
            using (var encoder = new BinaryEncoder(fixture.Context.AsMessageContext()))
            {
                source.Encode(encoder);
                encoded = encoder.CloseAndReturnBuffer() ?? [];
            }
            var decoded = new EnumValueType();
            using (var decoder = new BinaryDecoder(encoded, fixture.Context.AsMessageContext()))
            {
                decoded.Decode(decoder);
            }
            Assert.That(decoded.IsEqual(source), Is.True);
            Assert.That(decoded.DisplayName.Text, Is.EqualTo("Value Original"));
            Assert.That(source.DisplayName.Translations!.Count, Is.EqualTo(2));
            Assert.That(decoded.DisplayName.Translations, Is.Null);
            Assert.That(decoded.DisplayName.TranslationInfo.IsNull, Is.True);
        }

        /// <summary>
        /// A statistics snapshot retains deferred translations instead of flattening them through wire encoding.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task EnumValueStatisticsPreserveDeferredTranslations(bool nested)
        {
            var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["en-US"] = "Value {0}",
                ["de-DE"] = "Wert {0}"
            };
            object[] args = ["Original"];
            var info = new TranslationInfo("enum-key", "en-US", "Fallback {0}", args);
            var source = new EnumValueType
            {
                Value = 42,
                DisplayName = new LocalizedText(translations, info).FilterByPreferredLocales(["en-US"]),
                Description = new LocalizedText(translations, info).FilterByPreferredLocales(["de-DE"])
            };
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            quantity.DataType = Opc.Ua.DataTypeIds.BaseDataType;
            var provider = new Mock<IIaStatisticsProvider>();
            Variant value = new Variant(new ExtensionObject(source));
            if (nested)
            {
                value = new Variant(new DataValue(value, StatusCodes.Good, IaTestContext.Timestamp));
            }
            provider.Setup(item => item.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                Snapshot(0) with { Values = [new DataValue(value, StatusCodes.Good, IaTestContext.Timestamp)] });
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            Variant published = quantity.WrappedValue;
            if (nested)
            {
                Assert.That(published.TryGetValue(out DataValue inner), Is.True);
                published = inner.WrappedValue;
            }
            Assert.That(published.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out IEncodeable? body), Is.True);
            Assert.That(body, Is.TypeOf<EnumValueType>());
            if (body is not EnumValueType copy)
            {
                throw new InvalidOperationException("The published enumeration was not decoded.");
            }
            Assert.That(copy, Is.Not.SameAs(source));
            Assert.That(copy.Value, Is.EqualTo(42));
            Assert.That(copy.DisplayName.Translations, Is.EquivalentTo(translations));
            Assert.That(copy.Description.Translations, Is.EquivalentTo(translations));
            Assert.That(copy.DisplayName.Translations, Is.Not.SameAs(translations));
            Assert.That(copy.Description.Translations, Is.Not.SameAs(translations));
            Assert.That(copy.DisplayName.TranslationInfo.Args, Is.Not.SameAs(args));
            Assert.That(copy.Description.TranslationInfo.Args, Is.Not.SameAs(args));
            Assert.That(copy.DisplayName.TranslationInfo.Key, Is.EqualTo("enum-key"));
            Assert.That(copy.DisplayName.TranslationInfo.Text, Is.EqualTo("Fallback {0}"));
            Assert.That(copy.DisplayName.FilterByPreferredLocales(["EN-us"]).Locale, Is.EqualTo("EN-us"));
            translations["de-DE"] = "Changed {0}";
            args[0] = "Changed";
            source.Value = 99;
            Assert.That(source.DisplayName.Text, Is.EqualTo("Value Changed"));
            Assert.That(source.Description.FilterByPreferredLocales(["de-DE"]).Text, Is.EqualTo("Changed Changed"));
            Assert.That(copy.Value, Is.EqualTo(42));
            Assert.That(copy.DisplayName.Text, Is.EqualTo("Value Original"));
            Assert.That(copy.DisplayName.FilterByPreferredLocales(["de-DE"]).Text, Is.EqualTo("Wert Original"));
            Assert.That(copy.Description.Text, Is.EqualTo("Wert Original"));
            copy.DisplayName.TranslationInfo.Args![0] = "Detached";
            Assert.That(copy.DisplayName.Text, Is.EqualTo("Value Detached"));
            Assert.That(args[0], Is.EqualTo("Changed"));
        }

        /// <summary>
        /// Enumeration text rejects unknown arguments before wire formatting and keeps the previous publication.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task EnumValueStatisticsRejectUnsafeArgumentsBeforeFormatting(bool description, bool builtIn)
        {
            var argument = new Mock<ICloneable>();
            argument.Setup(item => item.Clone()).Throws(new InvalidOperationException("Clone invoked"));
            argument.Setup(item => item.ToString()).Throws(new InvalidOperationException("Formatting invoked"));
            object formattingArgument = builtIn ? new System.Text.StringBuilder("Original") : argument.Object;
            var unsafeText = new LocalizedText("enum-key", "en-US", "Value {0}", formattingArgument);
            var source = new EnumValueType
            {
                Value = 42,
                DisplayName = description ? new LocalizedText("Display") : unsafeText,
                Description = description ? unsafeText : new LocalizedText("Description")
            };
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            quantity.DataType = Opc.Ua.DataTypeIds.BaseDataType;
            var provider = new Mock<IIaStatisticsProvider>();
            provider.Setup(item => item.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot(7));
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            Variant previous = quantity.WrappedValue;
            Variant startTime = binding.StartTime.Value;
            provider.Setup(item => item.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                Snapshot(0) with
                {
                    StartTime = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                    Values = [new DataValue(new Variant(new ExtensionObject(source)),
                        StatusCodes.Good, IaTestContext.Timestamp)]
                });
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo((StatusCode)StatusCodes.BadNotSupported));
            argument.Verify(item => item.Clone(), Times.Never);
            argument.Verify(item => item.ToString(), Times.Never);
            Assert.That(quantity.WrappedValue, Is.EqualTo(previous));
            Assert.That(quantity.StatusCode, Is.EqualTo((StatusCode)StatusCodes.UncertainLastUsableValue));
            Assert.That(quantity.Timestamp, Is.EqualTo(IaTestContext.Timestamp));
            Assert.That(binding.StartTime.Value, Is.EqualTo(startTime));
        }

        /// <summary>
        /// Asserts that an extension object keeps its opaque type identifier bytes.
        /// </summary>
        private static void AssertOpaqueTypeId(ExtensionObject extension, byte[] expected)
        {
            Assert.That(extension.TypeId.TryGetValue(out ByteString identifier), Is.True);
            Assert.That(identifier.Span.ToArray(), Is.EqualTo(expected));
            Assert.That(extension.TypeId.NamespaceIndex, Is.EqualTo((ushort)1));
        }

        /// <summary>
        /// Verifies that rejecting a provider value retains the previous collection and metadata.
        /// </summary>
        private static async Task AssertRejectedSnapshotAsync(Variant value, StatusCode expectedStatus)
        {
            var fixture = new IaTestContext();
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            quantity.DataType = Opc.Ua.DataTypeIds.BaseDataType;
            quantity.ValueRank = ValueRanks.Any;
            var provider = new Mock<IIaStatisticsProvider>(MockBehavior.Strict);
            provider.SetupSequence(item => item.ReadAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Snapshot(7))
                .ReturnsAsync(Snapshot(0) with
                {
                    StartTime = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                    Values = [new DataValue(value, StatusCodes.Good, IaTestContext.Timestamp)]
                });
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            StatusCode previousStatus = quantity.StatusCode;
            DateTime previousTimestamp = quantity.Timestamp;
            Variant previousStartTime = binding.StartTime.Value;
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(expectedStatus));
            AssertValue(quantity, 7);
            Assert.That(quantity.StatusCode, Is.EqualTo(previousStatus));
            Assert.That(quantity.Timestamp, Is.EqualTo(previousTimestamp));
            Assert.That(binding.StartTime.Value, Is.EqualTo(previousStartTime));
        }

        /// <summary>
        /// Publishes one statistic, lets the provider mutate its buffer and returns the published value.
        /// </summary>
        private static async Task<Variant> PublishThenMutateAsync(
            NodeId dataType, int valueRank, Variant value, Action mutate, IEncodeableType? activator = null)
        {
            var fixture = new IaTestContext();
            if (activator != null)
            {
                fixture.Context.EncodeableFactory.Builder.AddEncodeableType(activator).Commit();
            }
            BaseObjectState owner = fixture.Owner();
            BaseVariableState quantity = fixture.Quantity(owner);
            quantity.DataType = dataType;
            quantity.ValueRank = valueRank;
            var provider = new Mock<IIaStatisticsProvider>();
            provider.Setup(source => source.ReadAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                Snapshot(0) with { Values = [new DataValue(value, StatusCodes.Good, IaTestContext.Timestamp)] });
            using var binding = new IaStatistics(fixture.Context, owner, [quantity], provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            mutate();
            return quantity.WrappedValue;
        }

        /// <summary>
        /// Creates an explicitly registered activator without runtime reflection.
        /// </summary>
        private static IEncodeableType ActivatorFor(Func<IEncodeable> create)
        {
            var activator = new Mock<IEncodeableType>();
            activator.Setup(type => type.CreateInstance()).Returns(create);
            activator.SetupGet(type => type.Type).Returns(create().GetType());
            activator.SetupGet(type => type.XmlName).Returns(new System.Xml.XmlQualifiedName("Custom", "urn:ia:test"));
            return activator.Object;
        }

        /// <summary>
        /// Creates a fixed collection snapshot.
        /// </summary>
        private static IaStatisticsSnapshot Snapshot(uint value)
        {
            return new IaStatisticsSnapshot { StartTime = IaTestContext.Timestamp, Values = [Reading(value)] };
        }

        /// <summary>
        /// Creates a two-component collection snapshot.
        /// </summary>
        private static IaStatisticsSnapshot SnapshotPair(uint first)
        {
            return new IaStatisticsSnapshot
            {
                StartTime = IaTestContext.Timestamp,
                Values = [Reading(first), Reading(first + 1)]
            };
        }

        /// <summary>
        /// Creates a source-quality-preserving quantity reading.
        /// </summary>
        private static DataValue Reading(uint value)
        {
            return new DataValue(Variant.From(value), StatusCodes.UncertainLastUsableValue, IaTestContext.Timestamp);
        }

        /// <summary>
        /// Asserts the exact scalar type and value instead of a boxed representation.
        /// </summary>
        private static void AssertValue(BaseVariableState variable, uint expected)
        {
            Assert.That(variable.WrappedValue.TryGetValue(out uint actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Reads an exact scalar quantity from a variable state.
        /// </summary>
        private static uint ReadQuantity(BaseVariableState variable)
        {
            Assert.That(variable.WrappedValue.TryGetValue(out uint value), Is.True);
            return value;
        }

        /// <summary>The dimensions of the two-by-three statistics matrix.</summary>
        private static readonly int[] s_twoByThree = [2, 3];

        /// <summary>
        /// A structure whose encoding does not reproduce its value.
        /// </summary>
        private sealed class LossyStructure : IEncodeable
        {
            /// <summary>
            /// The number of codec or equality calls.
            /// </summary>
            public int EncodeCalls { get; private set; }

            /// <summary>
            /// The number of decode calls.
            /// </summary>
            public int DecodeCalls { get; private set; }

            /// <summary>
            /// The number of clone calls.
            /// </summary>
            public int CloneCalls { get; private set; }

            /// <summary>
            /// The number of equality calls.
            /// </summary>
            public int EqualityCalls { get; private set; }

            /// <summary>
            /// The value that is changed by the encoding.
            /// </summary>
            public int Value { get; set; }

            /// <inheritdoc/>
            public ExpandedNodeId TypeId => new(9011u, 1);

            /// <inheritdoc/>
            public ExpandedNodeId BinaryEncodingId => new(9012u, 1);

            /// <inheritdoc/>
            public ExpandedNodeId XmlEncodingId => new(9013u, 1);

            /// <inheritdoc/>
            public void Encode(IEncoder encoder)
            {
                EncodeCalls++;
                encoder.WriteInt32(nameof(Value), Value + 1);
            }

            /// <inheritdoc/>
            public void Decode(IDecoder decoder)
            {
                DecodeCalls++;
                Value = decoder.ReadInt32(nameof(Value));
            }

            /// <inheritdoc/>
            public bool IsEqual(IEncodeable? encodeable)
            {
                EqualityCalls++;
                return encodeable is LossyStructure other && other.Value == Value;
            }

            /// <inheritdoc/>
            public object Clone()
            {
                CloneCalls++;
                return MemberwiseClone();
            }
        }
    }
}
