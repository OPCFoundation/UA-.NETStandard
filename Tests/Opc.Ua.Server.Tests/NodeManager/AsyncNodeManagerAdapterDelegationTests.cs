/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Unit tests for delegation in <see cref="AsyncNodeManagerAdapter"/>.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public class AsyncNodeManagerAdapterDelegationTests
    {
        [Test]
        public void ConstructorRejectsNullNodeManager()
        {
            Assert.That(() => new AsyncNodeManagerAdapter(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void PropertiesExposeWrappedSyncManager()
        {
            var manager = new Mock<INodeManager>();
            string[] namespaces = ["urn:test"];
            manager.Setup(m => m.NamespaceUris).Returns(namespaces);
            using var adapter = new AsyncNodeManagerAdapter(manager.Object);

            Assert.That(adapter.SyncNodeManager, Is.SameAs(manager.Object));
            Assert.That(adapter.NamespaceUris, Is.SameAs(namespaces));
        }

        [Test]
        public void DisposeForwardsToDisposableNodeManager()
        {
            var manager = new Mock<INodeManagerWithDisposable>();
            using var adapter = new AsyncNodeManagerAdapter(manager.Object);

            adapter.Dispose();

            manager.Verify(m => m.Dispose(), Times.Once);
        }

        [Test]
        public void DisposeAllowsNonDisposableNodeManager()
        {
            var manager = new Mock<INodeManager>();
            using var adapter = new AsyncNodeManagerAdapter(manager.Object);

            Assert.That(adapter.Dispose, Throws.Nothing);
        }

        [Test]
        public void FactoryReturnsSameInstanceWhenSyncManagerImplementsAsyncFacet()
        {
            var manager = new Mock<INodeManagerWithAsync>();

            IAsyncNodeManager result = manager.Object.ToAsyncNodeManager();

            Assert.That(result, Is.SameAs(manager.Object));
        }

        [Test]
        public void FactoryWrapsSyncOnlyManager()
        {
            var manager = new Mock<INodeManager>();

            IAsyncNodeManager result = manager.Object.ToAsyncNodeManager();

            Assert.That(result, Is.TypeOf<AsyncNodeManagerAdapter>());
        }

        [Test]
        public async Task AllAsyncMethodsForwardToAsyncFacetAsync()
        {
            var manager = new Mock<INodeManagerWithAsync>(MockBehavior.Strict);
            using var adapter = new AsyncNodeManagerAdapter(manager.Object);
            TestValues values = CreateTestValues();

            SetupAsyncFacet(manager, values);

            await adapter.CreateAddressSpaceAsync(values.ExternalReferences).ConfigureAwait(false);
            await adapter.DeleteAddressSpaceAsync().ConfigureAwait(false);
            Assert.That(await adapter.GetManagerHandleAsync(values.NodeId).ConfigureAwait(false), Is.SameAs(values.Handle));
            await adapter.AddReferencesAsync(values.References).ConfigureAwait(false);
            Assert.That(
                await adapter.DeleteReferenceAsync(
                    values.SourceHandle,
                    values.ReferenceTypeId,
                    true,
                    values.TargetId,
                    true).ConfigureAwait(false),
                Is.SameAs(values.DeleteReferenceResult));
            Assert.That(
                await adapter.GetNodeMetadataAsync(values.Context, values.Handle, BrowseResultMask.All).ConfigureAwait(false),
                Is.SameAs(values.Metadata));
            Assert.That(
                await adapter.BrowseAsync(
                    values.Context,
                    values.ContinuationPoint,
                    values.ReferenceDescriptions).ConfigureAwait(false),
                Is.SameAs(values.ReturnedContinuationPoint));
            await adapter.TranslateBrowsePathAsync(
                values.Context,
                values.SourceHandle,
                values.RelativePath,
                values.TargetIds,
                values.UnresolvedTargetIds).ConfigureAwait(false);
            await adapter.ReadAsync(
                values.Context,
                1.0,
                values.ReadValues,
                values.DataValues,
                values.ReadErrors).ConfigureAwait(false);
            await adapter.HistoryReadAsync(
                values.Context,
                values.HistoryReadDetails,
                TimestampsToReturn.Both,
                true,
                values.HistoryReadValues,
                values.HistoryReadResults,
                values.HistoryReadErrors).ConfigureAwait(false);
            await adapter.WriteAsync(values.Context, values.WriteValues, values.WriteErrors).ConfigureAwait(false);
            await adapter.HistoryUpdateAsync(
                values.Context,
                typeof(UpdateDataDetails),
                values.HistoryUpdates,
                values.HistoryUpdateResults,
                values.HistoryUpdateErrors).ConfigureAwait(false);
            await adapter.CallAsync(values.Context, values.MethodsToCall, values.CallResults, values.CallErrors)
                .ConfigureAwait(false);
            Assert.That(
                await adapter.SubscribeToEventsAsync(
                    values.Context,
                    values.SourceHandle,
                    1,
                    values.EventMonitoredItem,
                    false).ConfigureAwait(false),
                Is.SameAs(values.EventResult));
            Assert.That(
                await adapter.SubscribeToAllEventsAsync(
                    values.Context,
                    2,
                    values.EventMonitoredItem,
                    true).ConfigureAwait(false),
                Is.SameAs(values.AllEventsResult));
            Assert.That(
                await adapter.ConditionRefreshAsync(values.Context, values.EventItems).ConfigureAwait(false),
                Is.SameAs(values.ConditionResult));
            await adapter.CreateMonitoredItemsAsync(
                values.Context,
                3,
                1000.0,
                TimestampsToReturn.Server,
                values.ItemsToCreate,
                values.CreateErrors,
                values.FilterErrors,
                values.MonitoredItems,
                true,
                values.IdFactory).ConfigureAwait(false);
            await adapter.RestoreMonitoredItemsAsync(
                values.ItemsToRestore,
                values.MonitoredItems,
                values.OwnerIdentity).ConfigureAwait(false);
            await adapter.ModifyMonitoredItemsAsync(
                values.Context,
                TimestampsToReturn.Source,
                values.MonitoredItems,
                values.ItemsToModify,
                values.ModifyErrors,
                values.FilterErrors).ConfigureAwait(false);
            await adapter.DeleteMonitoredItemsAsync(
                values.Context,
                values.MonitoredItems,
                values.DeleteProcessed,
                values.DeleteErrors).ConfigureAwait(false);
            await adapter.TransferMonitoredItemsAsync(
                values.Context,
                true,
                values.MonitoredItems,
                values.TransferProcessed,
                values.TransferErrors).ConfigureAwait(false);
            await adapter.SetMonitoringModeAsync(
                values.Context,
                MonitoringMode.Reporting,
                values.MonitoredItems,
                values.MonitoringProcessed,
                values.MonitoringErrors).ConfigureAwait(false);
            await adapter.SessionClosingAsync(values.Context, values.SessionId, true).ConfigureAwait(false);
            await adapter.SessionActivatedAsync(values.Context, values.SessionId).ConfigureAwait(false);
            Assert.That(
                await adapter.IsNodeInViewAsync(values.Context, values.NodeId, values.Handle).ConfigureAwait(false),
                Is.True);
            Assert.That(
                await adapter.GetPermissionMetadataAsync(
                    values.Context,
                    values.Handle,
                    BrowseResultMask.All,
                    values.PermissionCache,
                    true).ConfigureAwait(false),
                Is.SameAs(values.Metadata));
            Assert.That(
                await adapter.FindMethodStateAsync(values.Context, values.MethodToCall).ConfigureAwait(false),
                Is.SameAs(values.MethodState));
            Assert.That(
                await adapter.ValidateEventRolePermissionsAsync(
                    values.EventMonitoredItem,
                    values.FilterTarget).ConfigureAwait(false),
                Is.SameAs(values.PermissionResult));
            Assert.That(
                await adapter.ValidateRolePermissionsAsync(
                    values.Context,
                    values.NodeId,
                    PermissionType.Read).ConfigureAwait(false),
                Is.SameAs(values.RoleResult));
            Assert.That(adapter.IsMultipleEventConsumerNode(values.NodeId), Is.False);

            manager.VerifyAll();
            manager.As<INodeManager>().Verify(
                m => m.CreateAddressSpace(It.IsAny<IDictionary<NodeId, IList<IReference>>>()),
                Times.Never);
            manager.As<INodeManager>().Verify(
                m => m.Read(
                    It.IsAny<OperationContext>(),
                    It.IsAny<double>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<IList<DataValue>>(),
                    It.IsAny<IList<ServiceResult>>()),
                Times.Never);
        }

        [Test]
        public async Task AllAsyncMethodsUseSyncFallbackAsync()
        {
            var manager = new Mock<INodeManager3>(MockBehavior.Strict);
            using var adapter = new AsyncNodeManagerAdapter(manager.Object);
            TestValues values = CreateTestValues();

            SetupSyncFallback(manager, values);

            await adapter.CreateAddressSpaceAsync(values.ExternalReferences).ConfigureAwait(false);
            await adapter.DeleteAddressSpaceAsync().ConfigureAwait(false);
            Assert.That(await adapter.GetManagerHandleAsync(values.NodeId).ConfigureAwait(false), Is.SameAs(values.Handle));
            await adapter.AddReferencesAsync(values.References).ConfigureAwait(false);
            Assert.That(
                await adapter.DeleteReferenceAsync(
                    values.SourceHandle,
                    values.ReferenceTypeId,
                    true,
                    values.TargetId,
                    true).ConfigureAwait(false),
                Is.SameAs(values.DeleteReferenceResult));
            Assert.That(
                await adapter.GetNodeMetadataAsync(values.Context, values.Handle, BrowseResultMask.All).ConfigureAwait(false),
                Is.SameAs(values.Metadata));
            ContinuationPoint? browseContinuationPoint = await adapter.BrowseAsync(
                values.Context,
                values.ContinuationPoint,
                values.ReferenceDescriptions).ConfigureAwait(false);
            Assert.That(browseContinuationPoint, Is.SameAs(values.ReturnedContinuationPoint));
            await adapter.TranslateBrowsePathAsync(
                values.Context,
                values.SourceHandle,
                values.RelativePath,
                values.TargetIds,
                values.UnresolvedTargetIds).ConfigureAwait(false);
            await adapter.ReadAsync(
                values.Context,
                1.0,
                values.ReadValues,
                values.DataValues,
                values.ReadErrors).ConfigureAwait(false);
            await adapter.HistoryReadAsync(
                values.Context,
                values.HistoryReadDetails,
                TimestampsToReturn.Both,
                true,
                values.HistoryReadValues,
                values.HistoryReadResults,
                values.HistoryReadErrors).ConfigureAwait(false);
            await adapter.WriteAsync(values.Context, values.WriteValues, values.WriteErrors).ConfigureAwait(false);
            await adapter.HistoryUpdateAsync(
                values.Context,
                typeof(UpdateDataDetails),
                values.HistoryUpdates,
                values.HistoryUpdateResults,
                values.HistoryUpdateErrors).ConfigureAwait(false);
            await adapter.CallAsync(values.Context, values.MethodsToCall, values.CallResults, values.CallErrors)
                .ConfigureAwait(false);
            Assert.That(
                await adapter.SubscribeToEventsAsync(
                    values.Context,
                    values.SourceHandle,
                    1,
                    values.EventMonitoredItem,
                    false).ConfigureAwait(false),
                Is.SameAs(values.EventResult));
            Assert.That(
                await adapter.SubscribeToAllEventsAsync(
                    values.Context,
                    2,
                    values.EventMonitoredItem,
                    true).ConfigureAwait(false),
                Is.SameAs(values.AllEventsResult));
            Assert.That(await adapter.ConditionRefreshAsync(values.Context, values.EventItems).ConfigureAwait(false), Is.Null);
            await adapter.CreateMonitoredItemsAsync(
                values.Context,
                3,
                1000.0,
                TimestampsToReturn.Server,
                values.ItemsToCreate,
                values.CreateErrors,
                values.FilterErrors,
                values.MonitoredItems,
                true,
                values.IdFactory).ConfigureAwait(false);
            await adapter.RestoreMonitoredItemsAsync(
                values.ItemsToRestore,
                values.MonitoredItems,
                values.OwnerIdentity).ConfigureAwait(false);
            await adapter.ModifyMonitoredItemsAsync(
                values.Context,
                TimestampsToReturn.Source,
                values.MonitoredItems,
                values.ItemsToModify,
                values.ModifyErrors,
                values.FilterErrors).ConfigureAwait(false);
            await adapter.DeleteMonitoredItemsAsync(
                values.Context,
                values.MonitoredItems,
                values.DeleteProcessed,
                values.DeleteErrors).ConfigureAwait(false);
            await adapter.TransferMonitoredItemsAsync(
                values.Context,
                true,
                values.MonitoredItems,
                values.TransferProcessed,
                values.TransferErrors).ConfigureAwait(false);
            await adapter.SetMonitoringModeAsync(
                values.Context,
                MonitoringMode.Reporting,
                values.MonitoredItems,
                values.MonitoringProcessed,
                values.MonitoringErrors).ConfigureAwait(false);
            await adapter.SessionClosingAsync(values.Context, values.SessionId, true).ConfigureAwait(false);
            await adapter.SessionActivatedAsync(values.Context, values.SessionId).ConfigureAwait(false);
            Assert.That(
                await adapter.IsNodeInViewAsync(values.Context, values.NodeId, values.Handle).ConfigureAwait(false),
                Is.True);
            Assert.That(
                await adapter.GetPermissionMetadataAsync(
                    values.Context,
                    values.Handle,
                    BrowseResultMask.All,
                    values.PermissionCache,
                    true).ConfigureAwait(false),
                Is.SameAs(values.Metadata));
            Assert.That(
                await adapter.FindMethodStateAsync(values.Context, values.MethodToCall).ConfigureAwait(false),
                Is.SameAs(values.MethodState));
            Assert.That(
                await adapter.ValidateEventRolePermissionsAsync(
                    values.EventMonitoredItem,
                    values.FilterTarget).ConfigureAwait(false),
                Is.SameAs(values.PermissionResult));
            Assert.That(
                await adapter.ValidateRolePermissionsAsync(
                    values.Context,
                    values.NodeId,
                    PermissionType.Read).ConfigureAwait(false),
                Is.SameAs(values.RoleResult));

            manager.VerifyAll();
        }

        [Test]
        public async Task ExtendedSyncFallbacksHandleLegacyNodeManagerAsync()
        {
            var manager = new Mock<INodeManager>();
            using var adapter = new AsyncNodeManagerAdapter(manager.Object);
            TestValues values = CreateTestValues();

            Assert.That(
                await adapter.GetPermissionMetadataAsync(
                    values.Context,
                    values.Handle,
                    BrowseResultMask.All,
                    values.PermissionCache,
                    true).ConfigureAwait(false),
                Is.Null);
            Assert.That(
                await adapter.FindMethodStateAsync(values.Context, values.MethodToCall).ConfigureAwait(false),
                Is.Null);
            Assert.That(
                await adapter.ValidateEventRolePermissionsAsync(
                    values.EventMonitoredItem,
                    values.FilterTarget).ConfigureAwait(false),
                Is.SameAs(ServiceResult.Good));
            Assert.That(
                await adapter.ValidateRolePermissionsAsync(
                    values.Context,
                    values.NodeId,
                    PermissionType.Read).ConfigureAwait(false),
                Is.SameAs(ServiceResult.Good));
            await adapter.SessionClosingAsync(values.Context, values.SessionId, true).ConfigureAwait(false);
            await adapter.SessionActivatedAsync(values.Context, values.SessionId).ConfigureAwait(false);

            Assert.That(
                async () => await adapter.IsNodeInViewAsync(
                    values.Context,
                    values.NodeId,
                    values.Handle).ConfigureAwait(false),
                Throws.TypeOf<NotImplementedException>());
        }

        /// <summary>
        /// Composite test interface for async branch coverage.
        /// </summary>
        public interface INodeManagerWithAsync : INodeManager3, IAsyncNodeManager;

        /// <summary>
        /// Composite test interface for dispose coverage.
        /// </summary>
        public interface INodeManagerWithDisposable : INodeManager, IDisposable;

        private delegate void BrowseCallback(
            OperationContext context,
            ref ContinuationPoint continuationPoint,
            IList<ReferenceDescription> references);

        private static TestValues CreateTestValues()
        {
            object handle = new object();
            return new TestValues
            {
                ExternalReferences = new Dictionary<NodeId, IList<IReference>>(),
                References = new Dictionary<NodeId, IList<IReference>>(),
                NodeId = new NodeId(123, 2),
                Handle = handle,
                SourceHandle = new object(),
                ReferenceTypeId = new NodeId(234, 2),
                TargetId = new ExpandedNodeId(new NodeId(345, 2)),
                DeleteReferenceResult = new ServiceResult(StatusCodes.BadNodeIdUnknown),
                Context = NewOpContext(RequestType.Read),
                Metadata = new NodeMetadata(handle, new NodeId(123, 2)),
                ContinuationPoint = new ContinuationPoint(),
                ReturnedContinuationPoint = new ContinuationPoint(),
                ReferenceDescriptions = [],
                RelativePath = new RelativePathElement(),
                TargetIds = [],
                UnresolvedTargetIds = [],
                ReadValues = new ArrayOf<ReadValueId>(),
                DataValues = [],
                ReadErrors = [],
                HistoryReadDetails = new ReadRawModifiedDetails(),
                HistoryReadValues = new ArrayOf<HistoryReadValueId>(),
                HistoryReadResults = [],
                HistoryReadErrors = [],
                WriteValues = new ArrayOf<WriteValue>(),
                WriteErrors = [],
                HistoryUpdates = new ArrayOf<HistoryUpdateDetails>(),
                HistoryUpdateResults = [],
                HistoryUpdateErrors = [],
                MethodsToCall = new ArrayOf<CallMethodRequest>(),
                CallResults = [],
                CallErrors = [],
                EventMonitoredItem = new Mock<IEventMonitoredItem>().Object,
                EventItems = [],
                EventResult = new ServiceResult(StatusCodes.BadEventFilterInvalid),
                AllEventsResult = new ServiceResult(StatusCodes.BadSubscriptionIdInvalid),
                ConditionResult = new ServiceResult(StatusCodes.BadConditionDisabled),
                ItemsToCreate = new ArrayOf<MonitoredItemCreateRequest>(),
                CreateErrors = [],
                FilterErrors = [],
                MonitoredItems = [],
                IdFactory = new MonitoredItemIdFactory(),
                ItemsToRestore = [],
                OwnerIdentity = new Mock<IUserIdentity>().Object,
                ItemsToModify = new ArrayOf<MonitoredItemModifyRequest>(),
                ModifyErrors = [],
                DeleteProcessed = [],
                DeleteErrors = [],
                TransferProcessed = [],
                TransferErrors = [],
                MonitoringProcessed = [],
                MonitoringErrors = [],
                SessionId = new NodeId(456, 2),
                PermissionCache = [],
                MethodToCall = new CallMethodRequest(),
                MethodState = new MethodState(null),
                FilterTarget = new Mock<IFilterTarget>().Object,
                PermissionResult = new ServiceResult(StatusCodes.BadUserAccessDenied),
                RoleResult = new ServiceResult(StatusCodes.BadSecurityChecksFailed)
            };
        }

        private static void SetupAsyncFacet(Mock<INodeManagerWithAsync> manager, TestValues values)
        {
            manager.Setup(m => m.CreateAddressSpaceAsync(values.ExternalReferences, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.GetManagerHandleAsync(values.NodeId, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<object>(values.Handle));
            manager.Setup(m => m.AddReferencesAsync(values.References, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.DeleteReferenceAsync(
                    values.SourceHandle,
                    values.ReferenceTypeId,
                    true,
                    values.TargetId,
                    true,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(values.DeleteReferenceResult));
            manager.Setup(m => m.GetNodeMetadataAsync(
                    values.Context,
                    values.Handle,
                    BrowseResultMask.All,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeMetadata>(values.Metadata));
            manager.Setup(m => m.BrowseAsync(
                    values.Context,
                    values.ContinuationPoint,
                    values.ReferenceDescriptions,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ContinuationPoint?>(values.ReturnedContinuationPoint));
            manager.Setup(m => m.TranslateBrowsePathAsync(
                    values.Context,
                    values.SourceHandle,
                    values.RelativePath,
                    values.TargetIds,
                    values.UnresolvedTargetIds,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.ReadAsync(
                    values.Context,
                    1.0,
                    values.ReadValues,
                    values.DataValues,
                    values.ReadErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.HistoryReadAsync(
                    values.Context,
                    values.HistoryReadDetails,
                    TimestampsToReturn.Both,
                    true,
                    values.HistoryReadValues,
                    values.HistoryReadResults,
                    values.HistoryReadErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.WriteAsync(
                    values.Context,
                    values.WriteValues,
                    values.WriteErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.HistoryUpdateAsync(
                    values.Context,
                    typeof(UpdateDataDetails),
                    values.HistoryUpdates,
                    values.HistoryUpdateResults,
                    values.HistoryUpdateErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.CallAsync(
                    values.Context,
                    values.MethodsToCall,
                    values.CallResults,
                    values.CallErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.SubscribeToEventsAsync(
                    values.Context,
                    values.SourceHandle,
                    1,
                    values.EventMonitoredItem,
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(values.EventResult));
            manager.Setup(m => m.SubscribeToAllEventsAsync(
                    values.Context,
                    2,
                    values.EventMonitoredItem,
                    true,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(values.AllEventsResult));
            manager.Setup(m => m.ConditionRefreshAsync(
                    values.Context,
                    values.EventItems,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(values.ConditionResult));
            manager.Setup(m => m.CreateMonitoredItemsAsync(
                    values.Context,
                    3,
                    1000.0,
                    TimestampsToReturn.Server,
                    values.ItemsToCreate,
                    values.CreateErrors,
                    values.FilterErrors,
                    values.MonitoredItems,
                    true,
                    values.IdFactory,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.RestoreMonitoredItemsAsync(
                    values.ItemsToRestore,
                    values.MonitoredItems,
                    values.OwnerIdentity,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.ModifyMonitoredItemsAsync(
                    values.Context,
                    TimestampsToReturn.Source,
                    values.MonitoredItems,
                    values.ItemsToModify,
                    values.ModifyErrors,
                    values.FilterErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.DeleteMonitoredItemsAsync(
                    values.Context,
                    values.MonitoredItems,
                    values.DeleteProcessed,
                    values.DeleteErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.TransferMonitoredItemsAsync(
                    values.Context,
                    true,
                    values.MonitoredItems,
                    values.TransferProcessed,
                    values.TransferErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.SetMonitoringModeAsync(
                    values.Context,
                    MonitoringMode.Reporting,
                    values.MonitoredItems,
                    values.MonitoringProcessed,
                    values.MonitoringErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.SessionClosingAsync(
                    values.Context,
                    values.SessionId,
                    true,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.SessionActivatedAsync(
                    values.Context,
                    values.SessionId,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.IsNodeInViewAsync(
                    values.Context,
                    values.NodeId,
                    values.Handle,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));
            manager.Setup(m => m.GetPermissionMetadataAsync(
                    values.Context,
                    values.Handle,
                    BrowseResultMask.All,
                    values.PermissionCache,
                    true,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeMetadata?>(values.Metadata));
            manager.Setup(m => m.FindMethodState(values.Context, values.MethodToCall)).Returns(values.MethodState);
            manager.Setup(m => m.ValidateEventRolePermissionsAsync(
                    values.EventMonitoredItem,
                    values.FilterTarget,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(values.PermissionResult));
            manager.Setup(m => m.ValidateRolePermissionsAsync(
                    values.Context,
                    values.NodeId,
                    PermissionType.Read,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(values.RoleResult));
        }

        private static void SetupSyncFallback(Mock<INodeManager3> manager, TestValues values)
        {
            manager.Setup(m => m.CreateAddressSpace(values.ExternalReferences));
            manager.Setup(m => m.DeleteAddressSpace());
            manager.Setup(m => m.GetManagerHandle(values.NodeId)).Returns(values.Handle);
            manager.Setup(m => m.AddReferences(values.References));
            manager.Setup(m => m.DeleteReference(
                    values.SourceHandle,
                    values.ReferenceTypeId,
                    true,
                    values.TargetId,
                    true))
                .Returns(values.DeleteReferenceResult);
            manager.Setup(m => m.GetNodeMetadata(values.Context, values.Handle, BrowseResultMask.All))
                .Returns(values.Metadata);
            manager.Setup(m => m.Browse(
                    values.Context,
                    ref It.Ref<ContinuationPoint>.IsAny,
                    values.ReferenceDescriptions))
                .Callback(new BrowseCallback(
                    (_, ref cp, _) =>
                        cp = values.ReturnedContinuationPoint));
            manager.Setup(m => m.TranslateBrowsePath(
                values.Context,
                values.SourceHandle,
                values.RelativePath,
                values.TargetIds,
                values.UnresolvedTargetIds));
            manager.Setup(m => m.Read(
                values.Context,
                1.0,
                values.ReadValues,
                values.DataValues,
                values.ReadErrors));
            manager.Setup(m => m.HistoryRead(
                values.Context,
                values.HistoryReadDetails,
                TimestampsToReturn.Both,
                true,
                values.HistoryReadValues,
                values.HistoryReadResults,
                values.HistoryReadErrors));
            manager.Setup(m => m.Write(values.Context, values.WriteValues, values.WriteErrors));
            manager.Setup(m => m.HistoryUpdate(
                values.Context,
                typeof(UpdateDataDetails),
                values.HistoryUpdates,
                values.HistoryUpdateResults,
                values.HistoryUpdateErrors));
            manager.Setup(m => m.Call(values.Context, values.MethodsToCall, values.CallResults, values.CallErrors));
            manager.Setup(m => m.SubscribeToEvents(
                    values.Context,
                    values.SourceHandle,
                    1,
                    values.EventMonitoredItem,
                    false))
                .Returns(values.EventResult);
            manager.Setup(m => m.SubscribeToAllEvents(
                    values.Context,
                    2,
                    values.EventMonitoredItem,
                    true))
                .Returns(values.AllEventsResult);
            manager.Setup(m => m.ConditionRefresh(values.Context, values.EventItems))
                .Returns(values.ConditionResult);
            manager.Setup(m => m.CreateMonitoredItems(
                values.Context,
                3,
                1000.0,
                TimestampsToReturn.Server,
                values.ItemsToCreate,
                values.CreateErrors,
                values.FilterErrors,
                values.MonitoredItems,
                true,
                values.IdFactory));
            manager.Setup(m => m.RestoreMonitoredItems(
                values.ItemsToRestore,
                values.MonitoredItems,
                values.OwnerIdentity));
            manager.Setup(m => m.ModifyMonitoredItems(
                values.Context,
                TimestampsToReturn.Source,
                values.MonitoredItems,
                values.ItemsToModify,
                values.ModifyErrors,
                values.FilterErrors));
            manager.Setup(m => m.DeleteMonitoredItems(
                values.Context,
                values.MonitoredItems,
                values.DeleteProcessed,
                values.DeleteErrors));
            manager.Setup(m => m.TransferMonitoredItems(
                values.Context,
                true,
                values.MonitoredItems,
                values.TransferProcessed,
                values.TransferErrors));
            manager.Setup(m => m.SetMonitoringMode(
                values.Context,
                MonitoringMode.Reporting,
                values.MonitoredItems,
                values.MonitoringProcessed,
                values.MonitoringErrors));
            manager.Setup(m => m.SessionClosing(values.Context, values.SessionId, true));
            manager.Setup(m => m.SessionActivated(values.Context, values.SessionId));
            manager.Setup(m => m.IsNodeInView(values.Context, values.NodeId, values.Handle)).Returns(true);
            manager.Setup(m => m.GetPermissionMetadata(
                    values.Context,
                    values.Handle,
                    BrowseResultMask.All,
                    values.PermissionCache,
                    true))
                .Returns(values.Metadata);
            manager.Setup(m => m.FindMethodState(values.Context, values.MethodToCall)).Returns(values.MethodState);
            manager.Setup(m => m.ValidateEventRolePermissions(values.EventMonitoredItem, values.FilterTarget))
                .Returns(values.PermissionResult);
            manager.Setup(m => m.ValidateRolePermissions(values.Context, values.NodeId, PermissionType.Read))
                .Returns(values.RoleResult);
        }

        private static OperationContext NewOpContext(RequestType type)
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            session.Setup(s => s.PreferredLocales).Returns([]);
            return new OperationContext(
                new RequestHeader(), null!, type, RequestLifetime.None, session.Object);
        }

        private sealed class TestValues
        {
            public IDictionary<NodeId, IList<IReference>> ExternalReferences { get; set; } = null!;

            public IDictionary<NodeId, IList<IReference>> References { get; set; } = null!;

            public NodeId NodeId { get; set; }

            public object Handle { get; set; } = null!;

            public object SourceHandle { get; set; } = null!;

            public NodeId ReferenceTypeId { get; set; }

            public ExpandedNodeId TargetId { get; set; }

            public ServiceResult DeleteReferenceResult { get; set; } = null!;

            public OperationContext Context { get; set; } = null!;

            public NodeMetadata Metadata { get; set; } = null!;

            public ContinuationPoint ContinuationPoint { get; set; } = null!;

            public ContinuationPoint ReturnedContinuationPoint { get; set; } = null!;

            public IList<ReferenceDescription> ReferenceDescriptions { get; set; } = null!;

            public RelativePathElement RelativePath { get; set; } = null!;

            public IList<ExpandedNodeId> TargetIds { get; set; } = null!;

            public IList<NodeId> UnresolvedTargetIds { get; set; } = null!;

            public ArrayOf<ReadValueId> ReadValues { get; set; }

            public IList<DataValue> DataValues { get; set; } = null!;

            public IList<ServiceResult> ReadErrors { get; set; } = null!;

            public HistoryReadDetails HistoryReadDetails { get; set; } = null!;

            public ArrayOf<HistoryReadValueId> HistoryReadValues { get; set; }

            public IList<HistoryReadResult> HistoryReadResults { get; set; } = null!;

            public IList<ServiceResult> HistoryReadErrors { get; set; } = null!;

            public ArrayOf<WriteValue> WriteValues { get; set; }

            public IList<ServiceResult> WriteErrors { get; set; } = null!;

            public ArrayOf<HistoryUpdateDetails> HistoryUpdates { get; set; }

            public IList<HistoryUpdateResult> HistoryUpdateResults { get; set; } = null!;

            public IList<ServiceResult> HistoryUpdateErrors { get; set; } = null!;

            public ArrayOf<CallMethodRequest> MethodsToCall { get; set; }

            public IList<CallMethodResult> CallResults { get; set; } = null!;

            public IList<ServiceResult> CallErrors { get; set; } = null!;

            public IEventMonitoredItem EventMonitoredItem { get; set; } = null!;

            public IList<IEventMonitoredItem> EventItems { get; set; } = null!;

            public ServiceResult EventResult { get; set; } = null!;

            public ServiceResult AllEventsResult { get; set; } = null!;

            public ServiceResult ConditionResult { get; set; } = null!;

            public ArrayOf<MonitoredItemCreateRequest> ItemsToCreate { get; set; }

            public IList<ServiceResult> CreateErrors { get; set; } = null!;

            public IList<MonitoringFilterResult> FilterErrors { get; set; } = null!;

            public IList<IMonitoredItem> MonitoredItems { get; set; } = null!;

            public MonitoredItemIdFactory IdFactory { get; set; } = null!;

            public IList<IStoredMonitoredItem> ItemsToRestore { get; set; } = null!;

            public IUserIdentity OwnerIdentity { get; set; } = null!;

            public ArrayOf<MonitoredItemModifyRequest> ItemsToModify { get; set; }

            public IList<ServiceResult> ModifyErrors { get; set; } = null!;

            public IList<bool> DeleteProcessed { get; set; } = null!;

            public IList<ServiceResult> DeleteErrors { get; set; } = null!;

            public IList<bool> TransferProcessed { get; set; } = null!;

            public IList<ServiceResult> TransferErrors { get; set; } = null!;

            public IList<bool> MonitoringProcessed { get; set; } = null!;

            public IList<ServiceResult> MonitoringErrors { get; set; } = null!;

            public NodeId SessionId { get; set; }

            public Dictionary<NodeId, Variant[]> PermissionCache { get; set; } = null!;

            public CallMethodRequest MethodToCall { get; set; } = null!;

            public MethodState MethodState { get; set; } = null!;

            public IFilterTarget FilterTarget { get; set; } = null!;

            public ServiceResult PermissionResult { get; set; } = null!;

            public ServiceResult RoleResult { get; set; } = null!;
        }
    }
}
