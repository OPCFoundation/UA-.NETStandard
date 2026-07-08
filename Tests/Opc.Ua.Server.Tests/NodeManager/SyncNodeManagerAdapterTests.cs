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
    /// Unit tests for <see cref="SyncNodeManagerAdapter"/>.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    [Parallelizable(ParallelScope.All)]
    public class SyncNodeManagerAdapterTests
    {
        [Test]
        public void ConstructorRejectsNullNodeManager()
        {
            Assert.That(() => new SyncNodeManagerAdapter(null!), Throws.ArgumentNullException);
        }

        [Test]
        public void FactoryReturnsSameInstanceWhenAsyncManagerIsAlsoSyncManager()
        {
            var manager = new Mock<IAsyncAndSyncNodeManager>();

            INodeManager result = manager.Object.ToSyncNodeManager();

            Assert.That(result, Is.SameAs(manager.Object));
        }

        [Test]
        public void FactoryWrapsAsyncOnlyManager()
        {
            var manager = new Mock<IAsyncNodeManager>();

            INodeManager result = manager.Object.ToSyncNodeManager();

            Assert.That(result, Is.TypeOf<SyncNodeManagerAdapter>());
        }

        [Test]
        public void AllMethodsForwardToAsyncNodeManager()
        {
            var manager = new Mock<IAsyncNodeManager>(MockBehavior.Strict);
            var adapter = new SyncNodeManagerAdapter(manager.Object);
            var context = NewOpContext(RequestType.Read);
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            var references = new Dictionary<NodeId, IList<IReference>>();
            var nodeId = new NodeId(123, 2);
            var handle = new object();
            var sourceHandle = new object();
            var referenceTypeId = new NodeId(234, 2);
            var targetId = new ExpandedNodeId(new NodeId(345, 2));
            var metadata = new NodeMetadata(handle, nodeId);
            var continuationPoint = new ContinuationPoint();
            var returnedContinuationPoint = new ContinuationPoint();
            var referenceDescriptions = new List<ReferenceDescription>();
            var relativePath = new RelativePathElement();
            var targetIds = new List<ExpandedNodeId>();
            var unresolvedTargetIds = new List<NodeId>();
            var readValues = new ArrayOf<ReadValueId>();
            var values = new List<DataValue>();
            var readErrors = new List<ServiceResult>();
            var historyReadDetails = new ReadRawModifiedDetails();
            var historyReadValues = new ArrayOf<HistoryReadValueId>();
            var historyReadResults = new List<HistoryReadResult>();
            var historyReadErrors = new List<ServiceResult>();
            var writeValues = new ArrayOf<WriteValue>();
            var writeErrors = new List<ServiceResult>();
            var historyUpdates = new ArrayOf<HistoryUpdateDetails>();
            var historyUpdateResults = new List<HistoryUpdateResult>();
            var historyUpdateErrors = new List<ServiceResult>();
            var methodsToCall = new ArrayOf<CallMethodRequest>();
            var callResults = new List<CallMethodResult>();
            var callErrors = new List<ServiceResult>();
            var monitoredItem = new Mock<IEventMonitoredItem>().Object;
            var eventItems = new List<IEventMonitoredItem>();
            var itemsToCreate = new ArrayOf<MonitoredItemCreateRequest>();
            var createErrors = new List<ServiceResult>();
            var filterErrors = new List<MonitoringFilterResult>();
            var monitoredItems = new List<IMonitoredItem>();
            var idFactory = new MonitoredItemIdFactory();
            var itemsToRestore = new List<IStoredMonitoredItem>();
            var ownerIdentity = new Mock<IUserIdentity>().Object;
            var itemsToModify = new ArrayOf<MonitoredItemModifyRequest>();
            var modifyErrors = new List<ServiceResult>();
            var deleteProcessed = new List<bool>();
            var deleteErrors = new List<ServiceResult>();
            var transferProcessed = new List<bool>();
            var transferErrors = new List<ServiceResult>();
            var monitoringProcessed = new List<bool>();
            var monitoringErrors = new List<ServiceResult>();
            var sessionId = new NodeId(456, 2);
            var permissionCache = new Dictionary<NodeId, Variant[]>();
            var methodToCall = new CallMethodRequest();
            var methodState = new MethodState(null);
            var filterTarget = new Mock<IFilterTarget>().Object;
            var namespaces = new[] { "urn:test" };
            var deleteReferenceResult = new ServiceResult(StatusCodes.BadNodeIdUnknown);
            var eventResult = new ServiceResult(StatusCodes.BadEventFilterInvalid);
            var allEventsResult = new ServiceResult(StatusCodes.BadSubscriptionIdInvalid);
            var permissionResult = new ServiceResult(StatusCodes.BadUserAccessDenied);
            var roleResult = new ServiceResult(StatusCodes.BadSecurityChecksFailed);

            SetupAsyncMethods(
                manager,
                namespaces,
                externalReferences,
                references,
                nodeId,
                handle,
                sourceHandle,
                referenceTypeId,
                targetId,
                deleteReferenceResult,
                context,
                metadata,
                continuationPoint,
                returnedContinuationPoint,
                referenceDescriptions,
                relativePath,
                targetIds,
                unresolvedTargetIds,
                readValues,
                values,
                readErrors,
                historyReadDetails,
                historyReadValues,
                historyReadResults,
                historyReadErrors,
                writeValues,
                writeErrors,
                historyUpdates,
                historyUpdateResults,
                historyUpdateErrors,
                methodsToCall,
                callResults,
                callErrors,
                monitoredItem,
                eventItems,
                eventResult,
                allEventsResult,
                itemsToCreate,
                createErrors,
                filterErrors,
                monitoredItems,
                idFactory,
                itemsToRestore,
                ownerIdentity,
                itemsToModify,
                modifyErrors,
                deleteProcessed,
                deleteErrors,
                transferProcessed,
                transferErrors,
                monitoringProcessed,
                monitoringErrors,
                sessionId,
                permissionCache,
                methodToCall,
                methodState,
                filterTarget,
                permissionResult,
                roleResult);

            Assert.That(adapter.NamespaceUris, Is.SameAs(namespaces));
            adapter.CreateAddressSpace(externalReferences);
            adapter.DeleteAddressSpace();
            Assert.That(adapter.GetManagerHandle(nodeId), Is.SameAs(handle));
            adapter.AddReferences(references);
            Assert.That(adapter.DeleteReference(sourceHandle, referenceTypeId, true, targetId, true), Is.SameAs(deleteReferenceResult));
            Assert.That(adapter.GetNodeMetadata(context, handle, BrowseResultMask.All), Is.SameAs(metadata));
            adapter.Browse(context, ref continuationPoint, referenceDescriptions);
            Assert.That(continuationPoint, Is.SameAs(returnedContinuationPoint));
            adapter.TranslateBrowsePath(context, sourceHandle, relativePath, targetIds, unresolvedTargetIds);
            adapter.Read(context, 1.0, readValues, values, readErrors);
            adapter.HistoryRead(
                context,
                historyReadDetails,
                TimestampsToReturn.Both,
                true,
                historyReadValues,
                historyReadResults,
                historyReadErrors);
            adapter.Write(context, writeValues, writeErrors);
            adapter.HistoryUpdate(context, typeof(UpdateDataDetails), historyUpdates, historyUpdateResults, historyUpdateErrors);
            adapter.Call(context, methodsToCall, callResults, callErrors);
            Assert.That(adapter.SubscribeToEvents(context, sourceHandle, 1, monitoredItem, false), Is.SameAs(eventResult));
            Assert.That(adapter.SubscribeToAllEvents(context, 2, monitoredItem, true), Is.SameAs(allEventsResult));
            Assert.That(ServiceResult.IsGood(adapter.ConditionRefresh(context, eventItems)), Is.True);
            adapter.CreateMonitoredItems(
                context,
                3,
                1000.0,
                TimestampsToReturn.Server,
                itemsToCreate,
                createErrors,
                filterErrors,
                monitoredItems,
                true,
                idFactory);
            adapter.RestoreMonitoredItems(itemsToRestore, monitoredItems, ownerIdentity);
            adapter.ModifyMonitoredItems(
                context,
                TimestampsToReturn.Source,
                monitoredItems,
                itemsToModify,
                modifyErrors,
                filterErrors);
            adapter.DeleteMonitoredItems(context, monitoredItems, deleteProcessed, deleteErrors);
            adapter.TransferMonitoredItems(context, true, monitoredItems, transferProcessed, transferErrors);
            adapter.SetMonitoringMode(context, MonitoringMode.Reporting, monitoredItems, monitoringProcessed, monitoringErrors);
            adapter.SessionClosing(context, sessionId, true);
            adapter.SessionActivated(context, sessionId);
            Assert.That(adapter.IsNodeInView(context, nodeId, handle), Is.True);
            Assert.That(
                adapter.GetPermissionMetadata(context, handle, BrowseResultMask.All, permissionCache, true),
                Is.SameAs(metadata));
            Assert.That(adapter.FindMethodState(context, methodToCall), Is.SameAs(methodState));
            Assert.That(adapter.ValidateEventRolePermissions(monitoredItem, filterTarget), Is.SameAs(permissionResult));
            Assert.That(
                adapter.ValidateRolePermissions(context, nodeId, PermissionType.Read),
                Is.SameAs(roleResult));

            manager.VerifyAll();
        }

        /// <summary>
        /// Composite interface for factory branch coverage.
        /// </summary>
        public interface IAsyncAndSyncNodeManager : IAsyncNodeManager, INodeManager;

        private static void SetupAsyncMethods(
            Mock<IAsyncNodeManager> manager,
            IEnumerable<string> namespaces,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            IDictionary<NodeId, IList<IReference>> references,
            NodeId nodeId,
            object handle,
            object sourceHandle,
            NodeId referenceTypeId,
            ExpandedNodeId targetId,
            ServiceResult deleteReferenceResult,
            OperationContext context,
            NodeMetadata metadata,
            ContinuationPoint continuationPoint,
            ContinuationPoint returnedContinuationPoint,
            IList<ReferenceDescription> referenceDescriptions,
            RelativePathElement relativePath,
            IList<ExpandedNodeId> targetIds,
            IList<NodeId> unresolvedTargetIds,
            ArrayOf<ReadValueId> readValues,
            IList<DataValue> values,
            IList<ServiceResult> readErrors,
            HistoryReadDetails historyReadDetails,
            ArrayOf<HistoryReadValueId> historyReadValues,
            IList<HistoryReadResult> historyReadResults,
            IList<ServiceResult> historyReadErrors,
            ArrayOf<WriteValue> writeValues,
            IList<ServiceResult> writeErrors,
            ArrayOf<HistoryUpdateDetails> historyUpdates,
            IList<HistoryUpdateResult> historyUpdateResults,
            IList<ServiceResult> historyUpdateErrors,
            ArrayOf<CallMethodRequest> methodsToCall,
            IList<CallMethodResult> callResults,
            IList<ServiceResult> callErrors,
            IEventMonitoredItem monitoredItem,
            IList<IEventMonitoredItem> eventItems,
            ServiceResult eventResult,
            ServiceResult allEventsResult,
            ArrayOf<MonitoredItemCreateRequest> itemsToCreate,
            IList<ServiceResult> createErrors,
            IList<MonitoringFilterResult> filterErrors,
            IList<IMonitoredItem> monitoredItems,
            MonitoredItemIdFactory idFactory,
            IList<IStoredMonitoredItem> itemsToRestore,
            IUserIdentity ownerIdentity,
            ArrayOf<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult> modifyErrors,
            IList<bool> deleteProcessed,
            IList<ServiceResult> deleteErrors,
            IList<bool> transferProcessed,
            IList<ServiceResult> transferErrors,
            IList<bool> monitoringProcessed,
            IList<ServiceResult> monitoringErrors,
            NodeId sessionId,
            Dictionary<NodeId, Variant[]> permissionCache,
            CallMethodRequest methodToCall,
            MethodState methodState,
            IFilterTarget filterTarget,
            ServiceResult permissionResult,
            ServiceResult roleResult)
        {
            manager.Setup(m => m.NamespaceUris).Returns(namespaces);
            manager.Setup(m => m.CreateAddressSpaceAsync(externalReferences, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.GetManagerHandleAsync(nodeId, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<object>(handle));
            manager.Setup(m => m.AddReferencesAsync(references, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.DeleteReferenceAsync(
                    sourceHandle,
                    referenceTypeId,
                    true,
                    targetId,
                    true,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(deleteReferenceResult));
            manager.Setup(m => m.GetNodeMetadataAsync(context, handle, BrowseResultMask.All, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeMetadata>(metadata));
            manager.Setup(m => m.BrowseAsync(
                    context,
                    continuationPoint,
                    referenceDescriptions,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ContinuationPoint?>(returnedContinuationPoint));
            manager.Setup(m => m.TranslateBrowsePathAsync(
                    context,
                    sourceHandle,
                    relativePath,
                    targetIds,
                    unresolvedTargetIds,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.ReadAsync(context, 1.0, readValues, values, readErrors, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.HistoryReadAsync(
                    context,
                    historyReadDetails,
                    TimestampsToReturn.Both,
                    true,
                    historyReadValues,
                    historyReadResults,
                    historyReadErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.WriteAsync(context, writeValues, writeErrors, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.HistoryUpdateAsync(
                    context,
                    typeof(UpdateDataDetails),
                    historyUpdates,
                    historyUpdateResults,
                    historyUpdateErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.CallAsync(context, methodsToCall, callResults, callErrors, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.SubscribeToEventsAsync(
                    context,
                    sourceHandle,
                    1,
                    monitoredItem,
                    false,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(eventResult));
            manager.Setup(m => m.SubscribeToAllEventsAsync(
                    context,
                    2,
                    monitoredItem,
                    true,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(allEventsResult));
            manager.Setup(m => m.ConditionRefreshAsync(context, eventItems, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(ServiceResult.Good));
            manager.Setup(m => m.CreateMonitoredItemsAsync(
                    context,
                    3,
                    1000.0,
                    TimestampsToReturn.Server,
                    itemsToCreate,
                    createErrors,
                    filterErrors,
                    monitoredItems,
                    true,
                    idFactory,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.RestoreMonitoredItemsAsync(
                    itemsToRestore,
                    monitoredItems,
                    ownerIdentity,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.ModifyMonitoredItemsAsync(
                    context,
                    TimestampsToReturn.Source,
                    monitoredItems,
                    itemsToModify,
                    modifyErrors,
                    filterErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.DeleteMonitoredItemsAsync(
                    context,
                    monitoredItems,
                    deleteProcessed,
                    deleteErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.TransferMonitoredItemsAsync(
                    context,
                    true,
                    monitoredItems,
                    transferProcessed,
                    transferErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.SetMonitoringModeAsync(
                    context,
                    MonitoringMode.Reporting,
                    monitoredItems,
                    monitoringProcessed,
                    monitoringErrors,
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.SessionClosingAsync(context, sessionId, true, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.SessionActivatedAsync(context, sessionId, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            manager.Setup(m => m.IsNodeInViewAsync(context, nodeId, handle, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(true));
            manager.Setup(m => m.GetPermissionMetadataAsync(
                    context,
                    handle,
                    BrowseResultMask.All,
                    permissionCache,
                    true,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<NodeMetadata?>(metadata));
            manager.Setup(m => m.FindMethodStateAsync(context, methodToCall, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<MethodState>(methodState));
            manager.Setup(m => m.ValidateEventRolePermissionsAsync(
                    monitoredItem,
                    filterTarget,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(permissionResult));
            manager.Setup(m => m.ValidateRolePermissionsAsync(
                    context,
                    nodeId,
                    PermissionType.Read,
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ServiceResult>(roleResult));
        }

        private static OperationContext NewOpContext(RequestType type)
        {
            var session = new Mock<ISession>();
            session.Setup(s => s.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            session.Setup(s => s.PreferredLocales).Returns([]);
            return new OperationContext(
                new RequestHeader(), null!, type, RequestLifetime.None, session.Object);
        }
    }
}
