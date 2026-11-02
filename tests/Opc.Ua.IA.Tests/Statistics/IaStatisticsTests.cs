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
        /// A synchronous statistics observer can re-enter refresh after publication releases its gate.
        /// </summary>
        [Test]
        public async Task StatisticsObserverCanReenterRefresh()
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
            Assert.That(reentrantRefreshCompleted, Is.True);
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
        /// Creates a fixed collection snapshot.
        /// </summary>
        private static IaStatisticsSnapshot Snapshot(uint value)
        {
            return new IaStatisticsSnapshot { StartTime = IaTestContext.Timestamp, Values = [Reading(value)] };
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
    }
}
