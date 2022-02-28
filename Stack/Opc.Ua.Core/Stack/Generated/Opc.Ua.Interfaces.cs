/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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

#if (!NET_STANDARD)
using System.Collections.Generic;
using System.Xml;
using System.ServiceModel;
using System.Runtime.Serialization;
#endif

#if (NET_STANDARD_ASYNC)
using System.Threading;
using System.Threading.Tasks;
#endif

namespace Opc.Ua
{
    #region ISessionEndpoint Interface
#if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
    /// <summary>
    /// The service contract which must be implemented by all UA servers.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [ServiceContract(Namespace = Namespaces.OpcUaWsdl)]
    public interface ISessionEndpoint : IEndpointBase
    {
#if (!OPCUA_EXCLUDE_CreateSession)
        /// <summary>
        /// The operation contract for the CreateSession service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/CreateSession", ReplyAction = Namespaces.OpcUaWsdl + "/CreateSessionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CreateSessionFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        CreateSessionResponseMessage CreateSession(CreateSessionMessage request);
#endif

#if (!OPCUA_EXCLUDE_ActivateSession)
        /// <summary>
        /// The operation contract for the ActivateSession service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/ActivateSession", ReplyAction = Namespaces.OpcUaWsdl + "/ActivateSessionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/ActivateSessionFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        ActivateSessionResponseMessage ActivateSession(ActivateSessionMessage request);
#endif

#if (!OPCUA_EXCLUDE_CloseSession)
        /// <summary>
        /// The operation contract for the CloseSession service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/CloseSession", ReplyAction = Namespaces.OpcUaWsdl + "/CloseSessionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CloseSessionFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        CloseSessionResponseMessage CloseSession(CloseSessionMessage request);
#endif

#if (!OPCUA_EXCLUDE_Cancel)
        /// <summary>
        /// The operation contract for the Cancel service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/Cancel", ReplyAction = Namespaces.OpcUaWsdl + "/CancelResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CancelFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        CancelResponseMessage Cancel(CancelMessage request);
#endif

#if (!OPCUA_EXCLUDE_AddNodes)
        /// <summary>
        /// The operation contract for the AddNodes service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/AddNodes", ReplyAction = Namespaces.OpcUaWsdl + "/AddNodesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/AddNodesFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        AddNodesResponseMessage AddNodes(AddNodesMessage request);
#endif

#if (!OPCUA_EXCLUDE_AddReferences)
        /// <summary>
        /// The operation contract for the AddReferences service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/AddReferences", ReplyAction = Namespaces.OpcUaWsdl + "/AddReferencesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/AddReferencesFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        AddReferencesResponseMessage AddReferences(AddReferencesMessage request);
#endif

#if (!OPCUA_EXCLUDE_DeleteNodes)
        /// <summary>
        /// The operation contract for the DeleteNodes service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/DeleteNodes", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteNodesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/DeleteNodesFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        DeleteNodesResponseMessage DeleteNodes(DeleteNodesMessage request);
#endif

#if (!OPCUA_EXCLUDE_DeleteReferences)
        /// <summary>
        /// The operation contract for the DeleteReferences service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/DeleteReferences", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteReferencesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/DeleteReferencesFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        DeleteReferencesResponseMessage DeleteReferences(DeleteReferencesMessage request);
#endif

#if (!OPCUA_EXCLUDE_Browse)
        /// <summary>
        /// The operation contract for the Browse service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/Browse", ReplyAction = Namespaces.OpcUaWsdl + "/BrowseResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/BrowseFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        BrowseResponseMessage Browse(BrowseMessage request);
#endif

#if (!OPCUA_EXCLUDE_BrowseNext)
        /// <summary>
        /// The operation contract for the BrowseNext service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/BrowseNext", ReplyAction = Namespaces.OpcUaWsdl + "/BrowseNextResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/BrowseNextFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        BrowseNextResponseMessage BrowseNext(BrowseNextMessage request);
#endif

#if (!OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds)
        /// <summary>
        /// The operation contract for the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/TranslateBrowsePathsToNodeIds", ReplyAction = Namespaces.OpcUaWsdl + "/TranslateBrowsePathsToNodeIdsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/TranslateBrowsePathsToNodeIdsFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        TranslateBrowsePathsToNodeIdsResponseMessage TranslateBrowsePathsToNodeIds(TranslateBrowsePathsToNodeIdsMessage request);
#endif

#if (!OPCUA_EXCLUDE_RegisterNodes)
        /// <summary>
        /// The operation contract for the RegisterNodes service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/RegisterNodes", ReplyAction = Namespaces.OpcUaWsdl + "/RegisterNodesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/RegisterNodesFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        RegisterNodesResponseMessage RegisterNodes(RegisterNodesMessage request);
#endif

#if (!OPCUA_EXCLUDE_UnregisterNodes)
        /// <summary>
        /// The operation contract for the UnregisterNodes service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/UnregisterNodes", ReplyAction = Namespaces.OpcUaWsdl + "/UnregisterNodesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/UnregisterNodesFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        UnregisterNodesResponseMessage UnregisterNodes(UnregisterNodesMessage request);
#endif

#if (!OPCUA_EXCLUDE_QueryFirst)
        /// <summary>
        /// The operation contract for the QueryFirst service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/QueryFirst", ReplyAction = Namespaces.OpcUaWsdl + "/QueryFirstResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/QueryFirstFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        QueryFirstResponseMessage QueryFirst(QueryFirstMessage request);
#endif

#if (!OPCUA_EXCLUDE_QueryNext)
        /// <summary>
        /// The operation contract for the QueryNext service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/QueryNext", ReplyAction = Namespaces.OpcUaWsdl + "/QueryNextResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/QueryNextFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        QueryNextResponseMessage QueryNext(QueryNextMessage request);
#endif

#if (!OPCUA_EXCLUDE_Read)
        /// <summary>
        /// The operation contract for the Read service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/Read", ReplyAction = Namespaces.OpcUaWsdl + "/ReadResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/ReadFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        ReadResponseMessage Read(ReadMessage request);
#endif

#if (!OPCUA_EXCLUDE_HistoryRead)
        /// <summary>
        /// The operation contract for the HistoryRead service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/HistoryRead", ReplyAction = Namespaces.OpcUaWsdl + "/HistoryReadResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/HistoryReadFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        HistoryReadResponseMessage HistoryRead(HistoryReadMessage request);
#endif

#if (!OPCUA_EXCLUDE_Write)
        /// <summary>
        /// The operation contract for the Write service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/Write", ReplyAction = Namespaces.OpcUaWsdl + "/WriteResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/WriteFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        WriteResponseMessage Write(WriteMessage request);
#endif

#if (!OPCUA_EXCLUDE_HistoryUpdate)
        /// <summary>
        /// The operation contract for the HistoryUpdate service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/HistoryUpdate", ReplyAction = Namespaces.OpcUaWsdl + "/HistoryUpdateResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/HistoryUpdateFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        HistoryUpdateResponseMessage HistoryUpdate(HistoryUpdateMessage request);
#endif

#if (!OPCUA_EXCLUDE_Call)
        /// <summary>
        /// The operation contract for the Call service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/Call", ReplyAction = Namespaces.OpcUaWsdl + "/CallResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CallFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        CallResponseMessage Call(CallMessage request);
#endif

#if (!OPCUA_EXCLUDE_CreateMonitoredItems)
        /// <summary>
        /// The operation contract for the CreateMonitoredItems service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/CreateMonitoredItems", ReplyAction = Namespaces.OpcUaWsdl + "/CreateMonitoredItemsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CreateMonitoredItemsFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        CreateMonitoredItemsResponseMessage CreateMonitoredItems(CreateMonitoredItemsMessage request);
#endif

#if (!OPCUA_EXCLUDE_ModifyMonitoredItems)
        /// <summary>
        /// The operation contract for the ModifyMonitoredItems service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/ModifyMonitoredItems", ReplyAction = Namespaces.OpcUaWsdl + "/ModifyMonitoredItemsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/ModifyMonitoredItemsFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        ModifyMonitoredItemsResponseMessage ModifyMonitoredItems(ModifyMonitoredItemsMessage request);
#endif

#if (!OPCUA_EXCLUDE_SetMonitoringMode)
        /// <summary>
        /// The operation contract for the SetMonitoringMode service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/SetMonitoringMode", ReplyAction = Namespaces.OpcUaWsdl + "/SetMonitoringModeResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/SetMonitoringModeFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        SetMonitoringModeResponseMessage SetMonitoringMode(SetMonitoringModeMessage request);
#endif

#if (!OPCUA_EXCLUDE_SetTriggering)
        /// <summary>
        /// The operation contract for the SetTriggering service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/SetTriggering", ReplyAction = Namespaces.OpcUaWsdl + "/SetTriggeringResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/SetTriggeringFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        SetTriggeringResponseMessage SetTriggering(SetTriggeringMessage request);
#endif

#if (!OPCUA_EXCLUDE_DeleteMonitoredItems)
        /// <summary>
        /// The operation contract for the DeleteMonitoredItems service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/DeleteMonitoredItems", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteMonitoredItemsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/DeleteMonitoredItemsFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        DeleteMonitoredItemsResponseMessage DeleteMonitoredItems(DeleteMonitoredItemsMessage request);
#endif

#if (!OPCUA_EXCLUDE_CreateSubscription)
        /// <summary>
        /// The operation contract for the CreateSubscription service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/CreateSubscription", ReplyAction = Namespaces.OpcUaWsdl + "/CreateSubscriptionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CreateSubscriptionFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        CreateSubscriptionResponseMessage CreateSubscription(CreateSubscriptionMessage request);
#endif

#if (!OPCUA_EXCLUDE_ModifySubscription)
        /// <summary>
        /// The operation contract for the ModifySubscription service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/ModifySubscription", ReplyAction = Namespaces.OpcUaWsdl + "/ModifySubscriptionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/ModifySubscriptionFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        ModifySubscriptionResponseMessage ModifySubscription(ModifySubscriptionMessage request);
#endif

#if (!OPCUA_EXCLUDE_SetPublishingMode)
        /// <summary>
        /// The operation contract for the SetPublishingMode service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/SetPublishingMode", ReplyAction = Namespaces.OpcUaWsdl + "/SetPublishingModeResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/SetPublishingModeFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        SetPublishingModeResponseMessage SetPublishingMode(SetPublishingModeMessage request);
#endif

#if (!OPCUA_EXCLUDE_Publish)
        /// <summary>
        /// The operation contract for the Publish service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/Publish", ReplyAction = Namespaces.OpcUaWsdl + "/PublishResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/PublishFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        PublishResponseMessage Publish(PublishMessage request);
#endif

#if (!OPCUA_EXCLUDE_Republish)
        /// <summary>
        /// The operation contract for the Republish service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/Republish", ReplyAction = Namespaces.OpcUaWsdl + "/RepublishResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/RepublishFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        RepublishResponseMessage Republish(RepublishMessage request);
#endif

#if (!OPCUA_EXCLUDE_TransferSubscriptions)
        /// <summary>
        /// The operation contract for the TransferSubscriptions service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/TransferSubscriptions", ReplyAction = Namespaces.OpcUaWsdl + "/TransferSubscriptionsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/TransferSubscriptionsFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        TransferSubscriptionsResponseMessage TransferSubscriptions(TransferSubscriptionsMessage request);
#endif

#if (!OPCUA_EXCLUDE_DeleteSubscriptions)
        /// <summary>
        /// The operation contract for the DeleteSubscriptions service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/DeleteSubscriptions", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteSubscriptionsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/DeleteSubscriptionsFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        DeleteSubscriptionsResponseMessage DeleteSubscriptions(DeleteSubscriptionsMessage request);
#endif
    }
#else
    /// <summary>
    /// The asynchronous service contract which must be implemented by UA servers.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
#if (!NET_STANDARD)
    [ServiceContract(Namespace = Namespaces.OpcUaWsdl)]
#endif
    public interface ISessionEndpoint : IEndpointBase
    {
#if (!OPCUA_EXCLUDE_CreateSession)
        /// <summary>
        /// The operation contract for the CreateSession service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/CreateSession", ReplyAction = Namespaces.OpcUaWsdl + "/CreateSessionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CreateSessionFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginCreateSession(CreateSessionMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a CreateSession service request.
        /// </summary>
        CreateSessionResponseMessage EndCreateSession(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_ActivateSession)
        /// <summary>
        /// The operation contract for the ActivateSession service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/ActivateSession", ReplyAction = Namespaces.OpcUaWsdl + "/ActivateSessionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/ActivateSessionFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginActivateSession(ActivateSessionMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a ActivateSession service request.
        /// </summary>
        ActivateSessionResponseMessage EndActivateSession(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_CloseSession)
        /// <summary>
        /// The operation contract for the CloseSession service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/CloseSession", ReplyAction = Namespaces.OpcUaWsdl + "/CloseSessionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CloseSessionFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginCloseSession(CloseSessionMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a CloseSession service request.
        /// </summary>
        CloseSessionResponseMessage EndCloseSession(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_Cancel)
        /// <summary>
        /// The operation contract for the Cancel service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/Cancel", ReplyAction = Namespaces.OpcUaWsdl + "/CancelResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CancelFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginCancel(CancelMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a Cancel service request.
        /// </summary>
        CancelResponseMessage EndCancel(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_AddNodes)
        /// <summary>
        /// The operation contract for the AddNodes service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/AddNodes", ReplyAction = Namespaces.OpcUaWsdl + "/AddNodesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/AddNodesFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginAddNodes(AddNodesMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a AddNodes service request.
        /// </summary>
        AddNodesResponseMessage EndAddNodes(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_AddReferences)
        /// <summary>
        /// The operation contract for the AddReferences service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/AddReferences", ReplyAction = Namespaces.OpcUaWsdl + "/AddReferencesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/AddReferencesFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginAddReferences(AddReferencesMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a AddReferences service request.
        /// </summary>
        AddReferencesResponseMessage EndAddReferences(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_DeleteNodes)
        /// <summary>
        /// The operation contract for the DeleteNodes service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/DeleteNodes", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteNodesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/DeleteNodesFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginDeleteNodes(DeleteNodesMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a DeleteNodes service request.
        /// </summary>
        DeleteNodesResponseMessage EndDeleteNodes(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_DeleteReferences)
        /// <summary>
        /// The operation contract for the DeleteReferences service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/DeleteReferences", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteReferencesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/DeleteReferencesFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginDeleteReferences(DeleteReferencesMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a DeleteReferences service request.
        /// </summary>
        DeleteReferencesResponseMessage EndDeleteReferences(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_Browse)
        /// <summary>
        /// The operation contract for the Browse service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/Browse", ReplyAction = Namespaces.OpcUaWsdl + "/BrowseResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/BrowseFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginBrowse(BrowseMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a Browse service request.
        /// </summary>
        BrowseResponseMessage EndBrowse(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_BrowseNext)
        /// <summary>
        /// The operation contract for the BrowseNext service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/BrowseNext", ReplyAction = Namespaces.OpcUaWsdl + "/BrowseNextResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/BrowseNextFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginBrowseNext(BrowseNextMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a BrowseNext service request.
        /// </summary>
        BrowseNextResponseMessage EndBrowseNext(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds)
        /// <summary>
        /// The operation contract for the TranslateBrowsePathsToNodeIds service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/TranslateBrowsePathsToNodeIds", ReplyAction = Namespaces.OpcUaWsdl + "/TranslateBrowsePathsToNodeIdsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/TranslateBrowsePathsToNodeIdsFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginTranslateBrowsePathsToNodeIds(TranslateBrowsePathsToNodeIdsMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a TranslateBrowsePathsToNodeIds service request.
        /// </summary>
        TranslateBrowsePathsToNodeIdsResponseMessage EndTranslateBrowsePathsToNodeIds(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_RegisterNodes)
        /// <summary>
        /// The operation contract for the RegisterNodes service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/RegisterNodes", ReplyAction = Namespaces.OpcUaWsdl + "/RegisterNodesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/RegisterNodesFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginRegisterNodes(RegisterNodesMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a RegisterNodes service request.
        /// </summary>
        RegisterNodesResponseMessage EndRegisterNodes(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_UnregisterNodes)
        /// <summary>
        /// The operation contract for the UnregisterNodes service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/UnregisterNodes", ReplyAction = Namespaces.OpcUaWsdl + "/UnregisterNodesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/UnregisterNodesFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginUnregisterNodes(UnregisterNodesMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a UnregisterNodes service request.
        /// </summary>
        UnregisterNodesResponseMessage EndUnregisterNodes(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_QueryFirst)
        /// <summary>
        /// The operation contract for the QueryFirst service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/QueryFirst", ReplyAction = Namespaces.OpcUaWsdl + "/QueryFirstResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/QueryFirstFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginQueryFirst(QueryFirstMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a QueryFirst service request.
        /// </summary>
        QueryFirstResponseMessage EndQueryFirst(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_QueryNext)
        /// <summary>
        /// The operation contract for the QueryNext service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/QueryNext", ReplyAction = Namespaces.OpcUaWsdl + "/QueryNextResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/QueryNextFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginQueryNext(QueryNextMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a QueryNext service request.
        /// </summary>
        QueryNextResponseMessage EndQueryNext(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_Read)
        /// <summary>
        /// The operation contract for the Read service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/Read", ReplyAction = Namespaces.OpcUaWsdl + "/ReadResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/ReadFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginRead(ReadMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a Read service request.
        /// </summary>
        ReadResponseMessage EndRead(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_HistoryRead)
        /// <summary>
        /// The operation contract for the HistoryRead service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/HistoryRead", ReplyAction = Namespaces.OpcUaWsdl + "/HistoryReadResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/HistoryReadFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginHistoryRead(HistoryReadMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a HistoryRead service request.
        /// </summary>
        HistoryReadResponseMessage EndHistoryRead(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_Write)
        /// <summary>
        /// The operation contract for the Write service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/Write", ReplyAction = Namespaces.OpcUaWsdl + "/WriteResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/WriteFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginWrite(WriteMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a Write service request.
        /// </summary>
        WriteResponseMessage EndWrite(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_HistoryUpdate)
        /// <summary>
        /// The operation contract for the HistoryUpdate service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/HistoryUpdate", ReplyAction = Namespaces.OpcUaWsdl + "/HistoryUpdateResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/HistoryUpdateFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginHistoryUpdate(HistoryUpdateMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a HistoryUpdate service request.
        /// </summary>
        HistoryUpdateResponseMessage EndHistoryUpdate(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_Call)
        /// <summary>
        /// The operation contract for the Call service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/Call", ReplyAction = Namespaces.OpcUaWsdl + "/CallResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CallFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginCall(CallMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a Call service request.
        /// </summary>
        CallResponseMessage EndCall(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_CreateMonitoredItems)
        /// <summary>
        /// The operation contract for the CreateMonitoredItems service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/CreateMonitoredItems", ReplyAction = Namespaces.OpcUaWsdl + "/CreateMonitoredItemsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CreateMonitoredItemsFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginCreateMonitoredItems(CreateMonitoredItemsMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a CreateMonitoredItems service request.
        /// </summary>
        CreateMonitoredItemsResponseMessage EndCreateMonitoredItems(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_ModifyMonitoredItems)
        /// <summary>
        /// The operation contract for the ModifyMonitoredItems service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/ModifyMonitoredItems", ReplyAction = Namespaces.OpcUaWsdl + "/ModifyMonitoredItemsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/ModifyMonitoredItemsFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginModifyMonitoredItems(ModifyMonitoredItemsMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a ModifyMonitoredItems service request.
        /// </summary>
        ModifyMonitoredItemsResponseMessage EndModifyMonitoredItems(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_SetMonitoringMode)
        /// <summary>
        /// The operation contract for the SetMonitoringMode service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/SetMonitoringMode", ReplyAction = Namespaces.OpcUaWsdl + "/SetMonitoringModeResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/SetMonitoringModeFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginSetMonitoringMode(SetMonitoringModeMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a SetMonitoringMode service request.
        /// </summary>
        SetMonitoringModeResponseMessage EndSetMonitoringMode(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_SetTriggering)
        /// <summary>
        /// The operation contract for the SetTriggering service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/SetTriggering", ReplyAction = Namespaces.OpcUaWsdl + "/SetTriggeringResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/SetTriggeringFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginSetTriggering(SetTriggeringMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a SetTriggering service request.
        /// </summary>
        SetTriggeringResponseMessage EndSetTriggering(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_DeleteMonitoredItems)
        /// <summary>
        /// The operation contract for the DeleteMonitoredItems service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/DeleteMonitoredItems", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteMonitoredItemsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/DeleteMonitoredItemsFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginDeleteMonitoredItems(DeleteMonitoredItemsMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a DeleteMonitoredItems service request.
        /// </summary>
        DeleteMonitoredItemsResponseMessage EndDeleteMonitoredItems(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_CreateSubscription)
        /// <summary>
        /// The operation contract for the CreateSubscription service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/CreateSubscription", ReplyAction = Namespaces.OpcUaWsdl + "/CreateSubscriptionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CreateSubscriptionFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginCreateSubscription(CreateSubscriptionMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a CreateSubscription service request.
        /// </summary>
        CreateSubscriptionResponseMessage EndCreateSubscription(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_ModifySubscription)
        /// <summary>
        /// The operation contract for the ModifySubscription service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/ModifySubscription", ReplyAction = Namespaces.OpcUaWsdl + "/ModifySubscriptionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/ModifySubscriptionFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginModifySubscription(ModifySubscriptionMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a ModifySubscription service request.
        /// </summary>
        ModifySubscriptionResponseMessage EndModifySubscription(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_SetPublishingMode)
        /// <summary>
        /// The operation contract for the SetPublishingMode service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/SetPublishingMode", ReplyAction = Namespaces.OpcUaWsdl + "/SetPublishingModeResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/SetPublishingModeFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginSetPublishingMode(SetPublishingModeMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a SetPublishingMode service request.
        /// </summary>
        SetPublishingModeResponseMessage EndSetPublishingMode(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_Publish)
        /// <summary>
        /// The operation contract for the Publish service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/Publish", ReplyAction = Namespaces.OpcUaWsdl + "/PublishResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/PublishFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginPublish(PublishMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a Publish service request.
        /// </summary>
        PublishResponseMessage EndPublish(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_Republish)
        /// <summary>
        /// The operation contract for the Republish service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/Republish", ReplyAction = Namespaces.OpcUaWsdl + "/RepublishResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/RepublishFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginRepublish(RepublishMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a Republish service request.
        /// </summary>
        RepublishResponseMessage EndRepublish(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_TransferSubscriptions)
        /// <summary>
        /// The operation contract for the TransferSubscriptions service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/TransferSubscriptions", ReplyAction = Namespaces.OpcUaWsdl + "/TransferSubscriptionsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/TransferSubscriptionsFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginTransferSubscriptions(TransferSubscriptionsMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a TransferSubscriptions service request.
        /// </summary>
        TransferSubscriptionsResponseMessage EndTransferSubscriptions(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_DeleteSubscriptions)
        /// <summary>
        /// The operation contract for the DeleteSubscriptions service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/DeleteSubscriptions", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteSubscriptionsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/DeleteSubscriptionsFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginDeleteSubscriptions(DeleteSubscriptionsMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a DeleteSubscriptions service request.
        /// </summary>
        DeleteSubscriptionsResponseMessage EndDeleteSubscriptions(IAsyncResult result);

#endif
    }
#endif
    #endregion

    #region ISessionClient Interface
    /// <summary>
    /// The client side interface for a UA server.
    /// </summary>
    /// <remarks>
    /// TODO: ISessionClient should be generated in ModelCompiler.
    /// </remarks>
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    public interface ISessionClient
    {
        #region CreateSession Methods
#if (!OPCUA_EXCLUDE_CreateSession)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the CreateSession service.
        /// </summary>
        ResponseHeader CreateSession(
            RequestHeader requestHeader,
            ApplicationDescription clientDescription,
            string serverUri,
            string endpointUrl,
            string sessionName,
            byte[] clientNonce,
            byte[] clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            out NodeId sessionId,
            out NodeId authenticationToken,
            out double revisedSessionTimeout,
            out byte[] serverNonce,
            out byte[] serverCertificate,
            out EndpointDescriptionCollection serverEndpoints,
            out SignedSoftwareCertificateCollection serverSoftwareCertificates,
            out SignatureData serverSignature,
            out uint maxRequestMessageSize);

        /// <summary>
        /// Begins an asynchronous invocation of the CreateSession service.
        /// </summary>
        IAsyncResult BeginCreateSession(
            RequestHeader requestHeader,
            ApplicationDescription clientDescription,
            string serverUri,
            string endpointUrl,
            string sessionName,
            byte[] clientNonce,
            byte[] clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CreateSession service.
        /// </summary>
        ResponseHeader EndCreateSession(
            IAsyncResult result,
            out NodeId sessionId,
            out NodeId authenticationToken,
            out double revisedSessionTimeout,
            out byte[] serverNonce,
            out byte[] serverCertificate,
            out EndpointDescriptionCollection serverEndpoints,
            out SignedSoftwareCertificateCollection serverSoftwareCertificates,
            out SignatureData serverSignature,
            out uint maxRequestMessageSize);
#else  // NET_STANDARD
        /// <summary>
        /// Invokes the CreateSession service.
        /// </summary>
        ResponseHeader CreateSession(
        RequestHeader requestHeader,
        ApplicationDescription clientDescription,
        string serverUri,
        string endpointUrl,
        string sessionName,
        byte[] clientNonce,
        byte[] clientCertificate,
        double requestedSessionTimeout,
        uint maxResponseMessageSize,
        out NodeId sessionId,
        out NodeId authenticationToken,
        out double revisedSessionTimeout,
        out byte[] serverNonce,
        out byte[] serverCertificate,
        out EndpointDescriptionCollection serverEndpoints,
        out SignedSoftwareCertificateCollection serverSoftwareCertificates,
        out SignatureData serverSignature,
        out uint maxRequestMessageSize);

        /// <summary>
        /// Begins an asynchronous invocation of the CreateSession service.
        /// </summary>
        IAsyncResult BeginCreateSession(
            RequestHeader requestHeader,
            ApplicationDescription clientDescription,
            string serverUri,
            string endpointUrl,
            string sessionName,
            byte[] clientNonce,
            byte[] clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CreateSession service.
        /// </summary>
        ResponseHeader EndCreateSession(
            IAsyncResult result,
            out NodeId sessionId,
            out NodeId authenticationToken,
            out double revisedSessionTimeout,
            out byte[] serverNonce,
            out byte[] serverCertificate,
            out EndpointDescriptionCollection serverEndpoints,
            out SignedSoftwareCertificateCollection serverSoftwareCertificates,
            out SignatureData serverSignature,
            out uint maxRequestMessageSize);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the CreateSession service using Task based request.
        /// </summary>
        Task<CreateSessionResponse> CreateSessionAsync(
            RequestHeader requestHeader,
            ApplicationDescription clientDescription,
            string serverUri,
            string endpointUrl,
            string sessionName,
            byte[] clientNonce,
            byte[] clientCertificate,
            double requestedSessionTimeout,
            uint maxResponseMessageSize,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region ActivateSession Methods
#if (!OPCUA_EXCLUDE_ActivateSession)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the ActivateSession service.
        /// </summary>
        ResponseHeader ActivateSession(
            RequestHeader requestHeader,
            SignatureData clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection localeIds,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            out byte[] serverNonce,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the ActivateSession service.
        /// </summary>
        IAsyncResult BeginActivateSession(
            RequestHeader requestHeader,
            SignatureData clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection localeIds,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the ActivateSession service.
        /// </summary>
        ResponseHeader EndActivateSession(
            IAsyncResult result,
            out byte[] serverNonce,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the ActivateSession service.
        /// </summary>
        ResponseHeader ActivateSession(
            RequestHeader requestHeader,
            SignatureData clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection localeIds,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            out byte[] serverNonce,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the ActivateSession service.
        /// </summary>
        IAsyncResult BeginActivateSession(
            RequestHeader requestHeader,
            SignatureData clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection localeIds,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the ActivateSession service.
        /// </summary>
        ResponseHeader EndActivateSession(
            IAsyncResult result,
            out byte[] serverNonce,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the ActivateSession service using Task based request.
        /// </summary>
        Task<ActivateSessionResponse> ActivateSessionAsync(
            RequestHeader requestHeader,
            SignatureData clientSignature,
            SignedSoftwareCertificateCollection clientSoftwareCertificates,
            StringCollection localeIds,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region CloseSession Methods
#if (!OPCUA_EXCLUDE_CloseSession)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the CloseSession service.
        /// </summary>
        ResponseHeader CloseSession(
            RequestHeader requestHeader,
            bool deleteSubscriptions);

        /// <summary>
        /// Begins an asynchronous invocation of the CloseSession service.
        /// </summary>
        IAsyncResult BeginCloseSession(
            RequestHeader requestHeader,
            bool deleteSubscriptions,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CloseSession service.
        /// </summary>
        ResponseHeader EndCloseSession(IAsyncResult result);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the CloseSession service.
        /// </summary>
        ResponseHeader CloseSession(
            RequestHeader requestHeader,
            bool deleteSubscriptions);

        /// <summary>
        /// Begins an asynchronous invocation of the CloseSession service.
        /// </summary>
        IAsyncResult BeginCloseSession(
            RequestHeader requestHeader,
            bool deleteSubscriptions,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CloseSession service.
        /// </summary>
        ResponseHeader EndCloseSession(
            IAsyncResult result);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the CloseSession service using Task based request.
        /// </summary>
        Task<CloseSessionResponse> CloseSessionAsync(
            RequestHeader requestHeader,
            bool deleteSubscriptions,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region Cancel Methods
#if (!OPCUA_EXCLUDE_Cancel)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the Cancel service.
        /// </summary>
        ResponseHeader Cancel(
            RequestHeader requestHeader,
            uint requestHandle,
            out uint cancelCount);

        /// <summary>
        /// Begins an asynchronous invocation of the Cancel service.
        /// </summary>
        IAsyncResult BeginCancel(
            RequestHeader requestHeader,
            uint requestHandle,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Cancel service.
        /// </summary>
        ResponseHeader EndCancel(
            IAsyncResult result,
            out uint cancelCount);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the Cancel service.
        /// </summary>
        ResponseHeader Cancel(
            RequestHeader requestHeader,
            uint requestHandle,
            out uint cancelCount);

        /// <summary>
        /// Begins an asynchronous invocation of the Cancel service.
        /// </summary>
        IAsyncResult BeginCancel(
            RequestHeader requestHeader,
            uint requestHandle,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Cancel service.
        /// </summary>
        ResponseHeader EndCancel(
            IAsyncResult result,
            out uint cancelCount);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the Cancel service using Task based request.
        /// </summary>
        Task<CancelResponse> CancelAsync(
            RequestHeader requestHeader,
            uint requestHandle,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region AddNodes Methods
#if (!OPCUA_EXCLUDE_AddNodes)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the AddNodes service.
        /// </summary>
        ResponseHeader AddNodes(
            RequestHeader requestHeader,
            AddNodesItemCollection nodesToAdd,
            out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the AddNodes service.
        /// </summary>
        IAsyncResult BeginAddNodes(
            RequestHeader requestHeader,
            AddNodesItemCollection nodesToAdd,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the AddNodes service.
        /// </summary>
        ResponseHeader EndAddNodes(
            IAsyncResult result,
            out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the AddNodes service.
        /// </summary>
        ResponseHeader AddNodes(
            RequestHeader requestHeader,
            AddNodesItemCollection nodesToAdd,
            out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the AddNodes service.
        /// </summary>
        IAsyncResult BeginAddNodes(
            RequestHeader requestHeader,
            AddNodesItemCollection nodesToAdd,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the AddNodes service.
        /// </summary>
        ResponseHeader EndAddNodes(
            IAsyncResult result,
            out AddNodesResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the AddNodes service using Task based request.
        /// </summary>
        Task<AddNodesResponse> AddNodesAsync(
            RequestHeader requestHeader,
            AddNodesItemCollection nodesToAdd,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region AddReferences Methods
#if (!OPCUA_EXCLUDE_AddReferences)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the AddReferences service.
        /// </summary>
        ResponseHeader AddReferences(
            RequestHeader requestHeader,
            AddReferencesItemCollection referencesToAdd,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the AddReferences service.
        /// </summary>
        IAsyncResult BeginAddReferences(
            RequestHeader requestHeader,
            AddReferencesItemCollection referencesToAdd,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the AddReferences service.
        /// </summary>
        ResponseHeader EndAddReferences(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the AddReferences service.
        /// </summary>
        ResponseHeader AddReferences(
            RequestHeader requestHeader,
            AddReferencesItemCollection referencesToAdd,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the AddReferences service.
        /// </summary>
        IAsyncResult BeginAddReferences(
            RequestHeader requestHeader,
            AddReferencesItemCollection referencesToAdd,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the AddReferences service.
        /// </summary>
        ResponseHeader EndAddReferences(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the AddReferences service using Task based request.
        /// </summary>
        Task<AddReferencesResponse> AddReferencesAsync(
            RequestHeader requestHeader,
            AddReferencesItemCollection referencesToAdd,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region DeleteNodes Methods
#if (!OPCUA_EXCLUDE_DeleteNodes)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the DeleteNodes service.
        /// </summary>
        ResponseHeader DeleteNodes(
            RequestHeader requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteNodes service.
        /// </summary>
        IAsyncResult BeginDeleteNodes(
            RequestHeader requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteNodes service.
        /// </summary>
        ResponseHeader EndDeleteNodes(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the DeleteNodes service.
        /// </summary>
        ResponseHeader DeleteNodes(
            RequestHeader requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteNodes service.
        /// </summary>
        IAsyncResult BeginDeleteNodes(
            RequestHeader requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteNodes service.
        /// </summary>
        ResponseHeader EndDeleteNodes(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the DeleteNodes service using Task based request.
        /// </summary>
        Task<DeleteNodesResponse> DeleteNodesAsync(
            RequestHeader requestHeader,
            DeleteNodesItemCollection nodesToDelete,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region DeleteReferences Methods
#if (!OPCUA_EXCLUDE_DeleteReferences)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the DeleteReferences service.
        /// </summary>
        ResponseHeader DeleteReferences(
            RequestHeader requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteReferences service.
        /// </summary>
        IAsyncResult BeginDeleteReferences(
            RequestHeader requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteReferences service.
        /// </summary>
        ResponseHeader EndDeleteReferences(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the DeleteReferences service.
        /// </summary>
        ResponseHeader DeleteReferences(
            RequestHeader requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteReferences service.
        /// </summary>
        IAsyncResult BeginDeleteReferences(
            RequestHeader requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteReferences service.
        /// </summary>
        ResponseHeader EndDeleteReferences(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the DeleteReferences service using Task based request.
        /// </summary>
        Task<DeleteReferencesResponse> DeleteReferencesAsync(
            RequestHeader requestHeader,
            DeleteReferencesItemCollection referencesToDelete,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region Browse Methods
#if (!OPCUA_EXCLUDE_Browse)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        ResponseHeader Browse(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Browse service.
        /// </summary>
        IAsyncResult BeginBrowse(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Browse service.
        /// </summary>
        ResponseHeader EndBrowse(
            IAsyncResult result,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        ResponseHeader Browse(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Browse service.
        /// </summary>
        IAsyncResult BeginBrowse(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Browse service.
        /// </summary>
        ResponseHeader EndBrowse(
            IAsyncResult result,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the Browse service using Task based request.
        /// </summary>
        Task<BrowseResponse> BrowseAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            uint requestedMaxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region BrowseNext Methods
#if (!OPCUA_EXCLUDE_BrowseNext)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        ResponseHeader BrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the BrowseNext service.
        /// </summary>
        IAsyncResult BeginBrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the BrowseNext service.
        /// </summary>
        ResponseHeader EndBrowseNext(
            IAsyncResult result,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        ResponseHeader BrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the BrowseNext service.
        /// </summary>
        IAsyncResult BeginBrowseNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the BrowseNext service.
        /// </summary>
        ResponseHeader EndBrowseNext(
            IAsyncResult result,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the BrowseNext service using Task based request.
        /// </summary>
        Task<BrowseNextResponse> BrowseNextAsync(
            RequestHeader requestHeader,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region TranslateBrowsePathsToNodeIds Methods
#if (!OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        ResponseHeader TranslateBrowsePathsToNodeIds(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        IAsyncResult BeginTranslateBrowsePathsToNodeIds(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        ResponseHeader EndTranslateBrowsePathsToNodeIds(
            IAsyncResult result,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        ResponseHeader TranslateBrowsePathsToNodeIds(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        IAsyncResult BeginTranslateBrowsePathsToNodeIds(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        ResponseHeader EndTranslateBrowsePathsToNodeIds(
            IAsyncResult result,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the TranslateBrowsePathsToNodeIds service using Task based request.
        /// </summary>
        Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            RequestHeader requestHeader,
            BrowsePathCollection browsePaths,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region RegisterNodes Methods
#if (!OPCUA_EXCLUDE_RegisterNodes)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the RegisterNodes service.
        /// </summary>
        ResponseHeader RegisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToRegister,
            out NodeIdCollection registeredNodeIds);

        /// <summary>
        /// Begins an asynchronous invocation of the RegisterNodes service.
        /// </summary>
        IAsyncResult BeginRegisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToRegister,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the RegisterNodes service.
        /// </summary>
        ResponseHeader EndRegisterNodes(
            IAsyncResult result,
            out NodeIdCollection registeredNodeIds);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the RegisterNodes service.
        /// </summary>
        ResponseHeader RegisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToRegister,
            out NodeIdCollection registeredNodeIds);

        /// <summary>
        /// Begins an asynchronous invocation of the RegisterNodes service.
        /// </summary>
        IAsyncResult BeginRegisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToRegister,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the RegisterNodes service.
        /// </summary>
        ResponseHeader EndRegisterNodes(
            IAsyncResult result,
            out NodeIdCollection registeredNodeIds);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the RegisterNodes service using Task based request.
        /// </summary>
        Task<RegisterNodesResponse> RegisterNodesAsync(
            RequestHeader requestHeader,
            NodeIdCollection nodesToRegister,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region UnregisterNodes Methods
#if (!OPCUA_EXCLUDE_UnregisterNodes)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the UnregisterNodes service.
        /// </summary>
        ResponseHeader UnregisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToUnregister);

        /// <summary>
        /// Begins an asynchronous invocation of the UnregisterNodes service.
        /// </summary>
        IAsyncResult BeginUnregisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToUnregister,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the UnregisterNodes service.
        /// </summary>
        ResponseHeader EndUnregisterNodes(
            IAsyncResult result);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the UnregisterNodes service.
        /// </summary>
        ResponseHeader UnregisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToUnregister);

        /// <summary>
        /// Begins an asynchronous invocation of the UnregisterNodes service.
        /// </summary>
        IAsyncResult BeginUnregisterNodes(
            RequestHeader requestHeader,
            NodeIdCollection nodesToUnregister,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the UnregisterNodes service.
        /// </summary>
        ResponseHeader EndUnregisterNodes(
            IAsyncResult result);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the UnregisterNodes service using Task based request.
        /// </summary>
        Task<UnregisterNodesResponse> UnregisterNodesAsync(
            RequestHeader requestHeader,
            NodeIdCollection nodesToUnregister,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region QueryFirst Methods
#if (!OPCUA_EXCLUDE_QueryFirst)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the QueryFirst service.
        /// </summary>
        ResponseHeader QueryFirst(
            RequestHeader requestHeader,
            ViewDescription view,
            NodeTypeDescriptionCollection nodeTypes,
            ContentFilter filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            out QueryDataSetCollection queryDataSets,
            out byte[] continuationPoint,
            out ParsingResultCollection parsingResults,
            out DiagnosticInfoCollection diagnosticInfos,
            out ContentFilterResult filterResult);

        /// <summary>
        /// Begins an asynchronous invocation of the QueryFirst service.
        /// </summary>
        IAsyncResult BeginQueryFirst(
            RequestHeader requestHeader,
            ViewDescription view,
            NodeTypeDescriptionCollection nodeTypes,
            ContentFilter filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the QueryFirst service.
        /// </summary>
        ResponseHeader EndQueryFirst(
            IAsyncResult result,
            out QueryDataSetCollection queryDataSets,
            out byte[] continuationPoint,
            out ParsingResultCollection parsingResults,
            out DiagnosticInfoCollection diagnosticInfos,
            out ContentFilterResult filterResult);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the QueryFirst service.
        /// </summary>
        ResponseHeader QueryFirst(
            RequestHeader requestHeader,
            ViewDescription view,
            NodeTypeDescriptionCollection nodeTypes,
            ContentFilter filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            out QueryDataSetCollection queryDataSets,
            out byte[] continuationPoint,
            out ParsingResultCollection parsingResults,
            out DiagnosticInfoCollection diagnosticInfos,
            out ContentFilterResult filterResult);

        /// <summary>
        /// Begins an asynchronous invocation of the QueryFirst service.
        /// </summary>
        IAsyncResult BeginQueryFirst(
            RequestHeader requestHeader,
            ViewDescription view,
            NodeTypeDescriptionCollection nodeTypes,
            ContentFilter filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the QueryFirst service.
        /// </summary>
        ResponseHeader EndQueryFirst(
            IAsyncResult result,
            out QueryDataSetCollection queryDataSets,
            out byte[] continuationPoint,
            out ParsingResultCollection parsingResults,
            out DiagnosticInfoCollection diagnosticInfos,
            out ContentFilterResult filterResult);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the QueryFirst service using Task based request.
        /// </summary>
        Task<QueryFirstResponse> QueryFirstAsync(
            RequestHeader requestHeader,
            ViewDescription view,
            NodeTypeDescriptionCollection nodeTypes,
            ContentFilter filter,
            uint maxDataSetsToReturn,
            uint maxReferencesToReturn,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region QueryNext Methods
#if (!OPCUA_EXCLUDE_QueryNext)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the QueryNext service.
        /// </summary>
        ResponseHeader QueryNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            out QueryDataSetCollection queryDataSets,
            out byte[] revisedContinuationPoint);

        /// <summary>
        /// Begins an asynchronous invocation of the QueryNext service.
        /// </summary>
        IAsyncResult BeginQueryNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the QueryNext service.
        /// </summary>
        ResponseHeader EndQueryNext(
            IAsyncResult result,
            out QueryDataSetCollection queryDataSets,
            out byte[] revisedContinuationPoint);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the QueryNext service.
        /// </summary>
        ResponseHeader QueryNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            out QueryDataSetCollection queryDataSets,
            out byte[] revisedContinuationPoint);

        /// <summary>
        /// Begins an asynchronous invocation of the QueryNext service.
        /// </summary>
        IAsyncResult BeginQueryNext(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the QueryNext service.
        /// </summary>
        ResponseHeader EndQueryNext(
            IAsyncResult result,
            out QueryDataSetCollection queryDataSets,
            out byte[] revisedContinuationPoint);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the QueryNext service using Task based request.
        /// </summary>
        Task<QueryNextResponse> QueryNextAsync(
            RequestHeader requestHeader,
            bool releaseContinuationPoint,
            byte[] continuationPoint,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region Read Methods
#if (!OPCUA_EXCLUDE_Read)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the Read service.
        /// </summary>
        ResponseHeader Read(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            out DataValueCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Read service.
        /// </summary>
        IAsyncResult BeginRead(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Read service.
        /// </summary>
        ResponseHeader EndRead(
            IAsyncResult result,
            out DataValueCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the Read service.
        /// </summary>
        ResponseHeader Read(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            out DataValueCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Read service.
        /// </summary>
        IAsyncResult BeginRead(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Read service.
        /// </summary>
        ResponseHeader EndRead(
            IAsyncResult result,
            out DataValueCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the Read service using Task based request.
        /// </summary>
        Task<ReadResponse> ReadAsync(
            RequestHeader requestHeader,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region HistoryRead Methods
#if (!OPCUA_EXCLUDE_HistoryRead)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the HistoryRead service.
        /// </summary>
        ResponseHeader HistoryRead(
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the HistoryRead service.
        /// </summary>
        IAsyncResult BeginHistoryRead(
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the HistoryRead service.
        /// </summary>
        ResponseHeader EndHistoryRead(
            IAsyncResult result,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the HistoryRead service.
        /// </summary>
        ResponseHeader HistoryRead(
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the HistoryRead service.
        /// </summary>
        IAsyncResult BeginHistoryRead(
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the HistoryRead service.
        /// </summary>
        ResponseHeader EndHistoryRead(
            IAsyncResult result,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the HistoryRead service using Task based request.
        /// </summary>
        Task<HistoryReadResponse> HistoryReadAsync(
            RequestHeader requestHeader,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region Write Methods
#if (!OPCUA_EXCLUDE_Write)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the Write service.
        /// </summary>
        ResponseHeader Write(
            RequestHeader requestHeader,
            WriteValueCollection nodesToWrite,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Write service.
        /// </summary>
        IAsyncResult BeginWrite(
            RequestHeader requestHeader,
            WriteValueCollection nodesToWrite,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Write service.
        /// </summary>
        ResponseHeader EndWrite(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the Write service.
        /// </summary>
        ResponseHeader Write(
            RequestHeader requestHeader,
            WriteValueCollection nodesToWrite,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Write service.
        /// </summary>
        IAsyncResult BeginWrite(
            RequestHeader requestHeader,
            WriteValueCollection nodesToWrite,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Write service.
        /// </summary>
        ResponseHeader EndWrite(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the Write service using Task based request.
        /// </summary>
        Task<WriteResponse> WriteAsync(
            RequestHeader requestHeader,
            WriteValueCollection nodesToWrite,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region HistoryUpdate Methods
#if (!OPCUA_EXCLUDE_HistoryUpdate)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the HistoryUpdate service.
        /// </summary>
        ResponseHeader HistoryUpdate(
            RequestHeader requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the HistoryUpdate service.
        /// </summary>
        IAsyncResult BeginHistoryUpdate(
            RequestHeader requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the HistoryUpdate service.
        /// </summary>
        ResponseHeader EndHistoryUpdate(
            IAsyncResult result,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the HistoryUpdate service.
        /// </summary>
        ResponseHeader HistoryUpdate(
            RequestHeader requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the HistoryUpdate service.
        /// </summary>
        IAsyncResult BeginHistoryUpdate(
            RequestHeader requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the HistoryUpdate service.
        /// </summary>
        ResponseHeader EndHistoryUpdate(
            IAsyncResult result,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the HistoryUpdate service using Task based request.
        /// </summary>
        Task<HistoryUpdateResponse> HistoryUpdateAsync(
            RequestHeader requestHeader,
            ExtensionObjectCollection historyUpdateDetails,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region Call Methods
#if (!OPCUA_EXCLUDE_Call)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the Call service.
        /// </summary>
        ResponseHeader Call(
            RequestHeader requestHeader,
            CallMethodRequestCollection methodsToCall,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Call service.
        /// </summary>
        IAsyncResult BeginCall(
            RequestHeader requestHeader,
            CallMethodRequestCollection methodsToCall,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Call service.
        /// </summary>
        ResponseHeader EndCall(
            IAsyncResult result,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the Call service.
        /// </summary>
        ResponseHeader Call(
            RequestHeader requestHeader,
            CallMethodRequestCollection methodsToCall,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Call service.
        /// </summary>
        IAsyncResult BeginCall(
            RequestHeader requestHeader,
            CallMethodRequestCollection methodsToCall,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Call service.
        /// </summary>
        ResponseHeader EndCall(
            IAsyncResult result,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the Call service using Task based request.
        /// </summary>
        Task<CallResponse> CallAsync(
            RequestHeader requestHeader,
            CallMethodRequestCollection methodsToCall,
            CancellationToken ct);

#endif
#endif
        #endregion

        #region CreateMonitoredItems Methods
#if (!OPCUA_EXCLUDE_CreateMonitoredItems)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the CreateMonitoredItems service.
        /// </summary>
        ResponseHeader CreateMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the CreateMonitoredItems service.
        /// </summary>
        IAsyncResult BeginCreateMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CreateMonitoredItems service.
        /// </summary>
        ResponseHeader EndCreateMonitoredItems(
            IAsyncResult result,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the CreateMonitoredItems service.
        /// </summary>
        ResponseHeader CreateMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the CreateMonitoredItems service.
        /// </summary>
        IAsyncResult BeginCreateMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CreateMonitoredItems service.
        /// </summary>
        ResponseHeader EndCreateMonitoredItems(
            IAsyncResult result,
            out MonitoredItemCreateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the CreateMonitoredItems service using Task based request.
        /// </summary>
        Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemCreateRequestCollection itemsToCreate,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region ModifyMonitoredItems Methods
#if (!OPCUA_EXCLUDE_ModifyMonitoredItems)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the ModifyMonitoredItems service.
        /// </summary>
        ResponseHeader ModifyMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the ModifyMonitoredItems service.
        /// </summary>
        IAsyncResult BeginModifyMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the ModifyMonitoredItems service.
        /// </summary>
        ResponseHeader EndModifyMonitoredItems(
            IAsyncResult result,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the ModifyMonitoredItems service.
        /// </summary>
        ResponseHeader ModifyMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the ModifyMonitoredItems service.
        /// </summary>
        IAsyncResult BeginModifyMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the ModifyMonitoredItems service.
        /// </summary>
        ResponseHeader EndModifyMonitoredItems(
            IAsyncResult result,
            out MonitoredItemModifyResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the ModifyMonitoredItems service using Task based request.
        /// </summary>
        Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            TimestampsToReturn timestampsToReturn,
            MonitoredItemModifyRequestCollection itemsToModify,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region SetMonitoringMode Methods
#if (!OPCUA_EXCLUDE_SetMonitoringMode)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the SetMonitoringMode service.
        /// </summary>
        ResponseHeader SetMonitoringMode(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the SetMonitoringMode service.
        /// </summary>
        IAsyncResult BeginSetMonitoringMode(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the SetMonitoringMode service.
        /// </summary>
        ResponseHeader EndSetMonitoringMode(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the SetMonitoringMode service.
        /// </summary>
        ResponseHeader SetMonitoringMode(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the SetMonitoringMode service.
        /// </summary>
        IAsyncResult BeginSetMonitoringMode(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the SetMonitoringMode service.
        /// </summary>
        ResponseHeader EndSetMonitoringMode(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the SetMonitoringMode service using Task based request.
        /// </summary>
        Task<SetMonitoringModeResponse> SetMonitoringModeAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            MonitoringMode monitoringMode,
            UInt32Collection monitoredItemIds,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region SetTriggering Methods
#if (!OPCUA_EXCLUDE_SetTriggering)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the SetTriggering service.
        /// </summary>
        ResponseHeader SetTriggering(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the SetTriggering service.
        /// </summary>
        IAsyncResult BeginSetTriggering(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the SetTriggering service.
        /// </summary>
        ResponseHeader EndSetTriggering(
            IAsyncResult result,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the SetTriggering service.
        /// </summary>
        ResponseHeader SetTriggering(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the SetTriggering service.
        /// </summary>
        IAsyncResult BeginSetTriggering(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the SetTriggering service.
        /// </summary>
        ResponseHeader EndSetTriggering(
            IAsyncResult result,
            out StatusCodeCollection addResults,
            out DiagnosticInfoCollection addDiagnosticInfos,
            out StatusCodeCollection removeResults,
            out DiagnosticInfoCollection removeDiagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the SetTriggering service using Task based request.
        /// </summary>
        Task<SetTriggeringResponse> SetTriggeringAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint triggeringItemId,
            UInt32Collection linksToAdd,
            UInt32Collection linksToRemove,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region DeleteMonitoredItems Methods
#if (!OPCUA_EXCLUDE_DeleteMonitoredItems)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the DeleteMonitoredItems service.
        /// </summary>
        ResponseHeader DeleteMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteMonitoredItems service.
        /// </summary>
        IAsyncResult BeginDeleteMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteMonitoredItems service.
        /// </summary>
        ResponseHeader EndDeleteMonitoredItems(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the DeleteMonitoredItems service.
        /// </summary>
        ResponseHeader DeleteMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteMonitoredItems service.
        /// </summary>
        IAsyncResult BeginDeleteMonitoredItems(
            RequestHeader requestHeader,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteMonitoredItems service.
        /// </summary>
        ResponseHeader EndDeleteMonitoredItems(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the DeleteMonitoredItems service using Task based request.
        /// </summary>
        Task<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            UInt32Collection monitoredItemIds,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region CreateSubscription Methods
#if (!OPCUA_EXCLUDE_CreateSubscription)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the CreateSubscription service.
        /// </summary>
        ResponseHeader CreateSubscription(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            out uint subscriptionId,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount);

        /// <summary>
        /// Begins an asynchronous invocation of the CreateSubscription service.
        /// </summary>
        IAsyncResult BeginCreateSubscription(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CreateSubscription service.
        /// </summary>
        ResponseHeader EndCreateSubscription(
            IAsyncResult result,
            out uint subscriptionId,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the CreateSubscription service.
        /// </summary>
        ResponseHeader CreateSubscription(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            out uint subscriptionId,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount);

        /// <summary>
        /// Begins an asynchronous invocation of the CreateSubscription service.
        /// </summary>
        IAsyncResult BeginCreateSubscription(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the CreateSubscription service.
        /// </summary>
        ResponseHeader EndCreateSubscription(
            IAsyncResult result,
            out uint subscriptionId,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the CreateSubscription service using Task based request.
        /// </summary>
        Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
            RequestHeader requestHeader,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            bool publishingEnabled,
            byte priority,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region ModifySubscription Methods
#if (!OPCUA_EXCLUDE_ModifySubscription)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the ModifySubscription service.
        /// </summary>
        ResponseHeader ModifySubscription(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount);

        /// <summary>
        /// Begins an asynchronous invocation of the ModifySubscription service.
        /// </summary>
        IAsyncResult BeginModifySubscription(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the ModifySubscription service.
        /// </summary>
        ResponseHeader EndModifySubscription(
            IAsyncResult result,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the ModifySubscription service.
        /// </summary>
        ResponseHeader ModifySubscription(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount);

        /// <summary>
        /// Begins an asynchronous invocation of the ModifySubscription service.
        /// </summary>
        IAsyncResult BeginModifySubscription(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the ModifySubscription service.
        /// </summary>
        ResponseHeader EndModifySubscription(
            IAsyncResult result,
            out double revisedPublishingInterval,
            out uint revisedLifetimeCount,
            out uint revisedMaxKeepAliveCount);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the ModifySubscription service using Task based request.
        /// </summary>
        Task<ModifySubscriptionResponse> ModifySubscriptionAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            double requestedPublishingInterval,
            uint requestedLifetimeCount,
            uint requestedMaxKeepAliveCount,
            uint maxNotificationsPerPublish,
            byte priority,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region SetPublishingMode Methods
#if (!OPCUA_EXCLUDE_SetPublishingMode)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the SetPublishingMode service.
        /// </summary>
        ResponseHeader SetPublishingMode(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the SetPublishingMode service.
        /// </summary>
        IAsyncResult BeginSetPublishingMode(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the SetPublishingMode service.
        /// </summary>
        ResponseHeader EndSetPublishingMode(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the SetPublishingMode service.
        /// </summary>
        ResponseHeader SetPublishingMode(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the SetPublishingMode service.
        /// </summary>
        IAsyncResult BeginSetPublishingMode(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the SetPublishingMode service.
        /// </summary>
        ResponseHeader EndSetPublishingMode(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the SetPublishingMode service using Task based request.
        /// </summary>
        Task<SetPublishingModeResponse> SetPublishingModeAsync(
            RequestHeader requestHeader,
            bool publishingEnabled,
            UInt32Collection subscriptionIds,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region Publish Methods
#if (!OPCUA_EXCLUDE_Publish)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the Publish service.
        /// </summary>
        ResponseHeader Publish(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            out uint subscriptionId,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications,
            out NotificationMessage notificationMessage,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Publish service.
        /// </summary>
        IAsyncResult BeginPublish(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Publish service.
        /// </summary>
        ResponseHeader EndPublish(
            IAsyncResult result,
            out uint subscriptionId,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications,
            out NotificationMessage notificationMessage,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the Publish service.
        /// </summary>
        ResponseHeader Publish(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            out uint subscriptionId,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications,
            out NotificationMessage notificationMessage,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the Publish service.
        /// </summary>
        IAsyncResult BeginPublish(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Publish service.
        /// </summary>
        ResponseHeader EndPublish(
            IAsyncResult result,
            out uint subscriptionId,
            out UInt32Collection availableSequenceNumbers,
            out bool moreNotifications,
            out NotificationMessage notificationMessage,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the Publish service using Task based request.
        /// </summary>
        Task<PublishResponse> PublishAsync(
            RequestHeader requestHeader,
            SubscriptionAcknowledgementCollection subscriptionAcknowledgements,
            CancellationToken ct);

#endif
#endif
        #endregion

        #region Republish Methods
#if (!OPCUA_EXCLUDE_Republish)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the Republish service.
        /// </summary>
        ResponseHeader Republish(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            out NotificationMessage notificationMessage);

        /// <summary>
        /// Begins an asynchronous invocation of the Republish service.
        /// </summary>
        IAsyncResult BeginRepublish(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Republish service.
        /// </summary>
        ResponseHeader EndRepublish(
            IAsyncResult result,
            out NotificationMessage notificationMessage);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the Republish service.
        /// </summary>
        ResponseHeader Republish(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            out NotificationMessage notificationMessage);

        /// <summary>
        /// Begins an asynchronous invocation of the Republish service.
        /// </summary>
        IAsyncResult BeginRepublish(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the Republish service.
        /// </summary>
        ResponseHeader EndRepublish(
            IAsyncResult result,
            out NotificationMessage notificationMessage);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the Republish service using Task based request.
        /// </summary>
        Task<RepublishResponse> RepublishAsync(
            RequestHeader requestHeader,
            uint subscriptionId,
            uint retransmitSequenceNumber,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region TransferSubscriptions Methods
#if (!OPCUA_EXCLUDE_TransferSubscriptions)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the TransferSubscriptions service.
        /// </summary>
        ResponseHeader TransferSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the TransferSubscriptions service.
        /// </summary>
        IAsyncResult BeginTransferSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the TransferSubscriptions service.
        /// </summary>
        ResponseHeader EndTransferSubscriptions(
            IAsyncResult result,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

#else  // NET_STANDARD
        /// <summary>
        /// Invokes the TransferSubscriptions service.
        /// </summary>
        ResponseHeader TransferSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the TransferSubscriptions service.
        /// </summary>
        IAsyncResult BeginTransferSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the TransferSubscriptions service.
        /// </summary>
        ResponseHeader EndTransferSubscriptions(
            IAsyncResult result,
            out TransferResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the TransferSubscriptions service using Task based request.
        /// </summary>
        Task<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            bool sendInitialValues,
            CancellationToken ct);
#endif
#endif
        #endregion

        #region DeleteSubscriptions Methods
#if (!OPCUA_EXCLUDE_DeleteSubscriptions)
#if (!NET_STANDARD)
        /// <summary>
        /// Invokes the DeleteSubscriptions service.
        /// </summary>
        ResponseHeader DeleteSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteSubscriptions service.
        /// </summary>
        IAsyncResult BeginDeleteSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteSubscriptions service.
        /// </summary>
        ResponseHeader EndDeleteSubscriptions(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#else  // NET_STANDARD
        /// <summary>
        /// Invokes the DeleteSubscriptions service.
        /// </summary>
        ResponseHeader DeleteSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);

        /// <summary>
        /// Begins an asynchronous invocation of the DeleteSubscriptions service.
        /// </summary>
        IAsyncResult BeginDeleteSubscriptions(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            AsyncCallback callback,
            object asyncState);

        /// <summary>
        /// Finishes an asynchronous invocation of the DeleteSubscriptions service.
        /// </summary>
        ResponseHeader EndDeleteSubscriptions(
            IAsyncResult result,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos);
#endif

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// Invokes the DeleteSubscriptions service using Task based request.
        /// </summary>
        Task<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            RequestHeader requestHeader,
            UInt32Collection subscriptionIds,
            CancellationToken ct);
#endif
#endif
    }
    #endregion

    #region ISessionChannel Interface
    /// <summary>
    /// An interface used by by clients to access a UA server.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
#if (!NET_STANDARD)
    [ServiceContract(Namespace = Namespaces.OpcUaWsdl)]
#endif
    public interface ISessionChannel : IChannelBase
    {
#if (!OPCUA_EXCLUDE_CreateSession)
        /// <summary>
        /// The operation contract for the CreateSession service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/CreateSession", ReplyAction = Namespaces.OpcUaWsdl + "/CreateSessionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CreateSessionFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        CreateSessionResponseMessage CreateSession(CreateSessionMessage request);

        /// <summary>
        /// The operation contract for the CreateSession service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/CreateSession", ReplyAction = Namespaces.OpcUaWsdl + "/CreateSessionResponse")]
#endif
        IAsyncResult BeginCreateSession(CreateSessionMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a CreateSession service request.
        /// </summary>
        CreateSessionResponseMessage EndCreateSession(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the CreateSession service.
        /// </summary>
        Task<CreateSessionResponseMessage> CreateSessionAsync(CreateSessionMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_ActivateSession)
        /// <summary>
        /// The operation contract for the ActivateSession service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/ActivateSession", ReplyAction = Namespaces.OpcUaWsdl + "/ActivateSessionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/ActivateSessionFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        ActivateSessionResponseMessage ActivateSession(ActivateSessionMessage request);

        /// <summary>
        /// The operation contract for the ActivateSession service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/ActivateSession", ReplyAction = Namespaces.OpcUaWsdl + "/ActivateSessionResponse")]
#endif
        IAsyncResult BeginActivateSession(ActivateSessionMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a ActivateSession service request.
        /// </summary>
        ActivateSessionResponseMessage EndActivateSession(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the ActivateSession service.
        /// </summary>
        Task<ActivateSessionResponseMessage> ActivateSessionAsync(ActivateSessionMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_CloseSession)
        /// <summary>
        /// The operation contract for the CloseSession service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/CloseSession", ReplyAction = Namespaces.OpcUaWsdl + "/CloseSessionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CloseSessionFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        CloseSessionResponseMessage CloseSession(CloseSessionMessage request);

        /// <summary>
        /// The operation contract for the CloseSession service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/CloseSession", ReplyAction = Namespaces.OpcUaWsdl + "/CloseSessionResponse")]
#endif
        IAsyncResult BeginCloseSession(CloseSessionMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a CloseSession service request.
        /// </summary>
        CloseSessionResponseMessage EndCloseSession(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the CloseSession service.
        /// </summary>
        Task<CloseSessionResponseMessage> CloseSessionAsync(CloseSessionMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_Cancel)
        /// <summary>
        /// The operation contract for the Cancel service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/Cancel", ReplyAction = Namespaces.OpcUaWsdl + "/CancelResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CancelFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        CancelResponseMessage Cancel(CancelMessage request);

        /// <summary>
        /// The operation contract for the Cancel service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/Cancel", ReplyAction = Namespaces.OpcUaWsdl + "/CancelResponse")]
#endif
        IAsyncResult BeginCancel(CancelMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a Cancel service request.
        /// </summary>
        CancelResponseMessage EndCancel(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the Cancel service.
        /// </summary>
        Task<CancelResponseMessage> CancelAsync(CancelMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_AddNodes)
        /// <summary>
        /// The operation contract for the AddNodes service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/AddNodes", ReplyAction = Namespaces.OpcUaWsdl + "/AddNodesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/AddNodesFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        AddNodesResponseMessage AddNodes(AddNodesMessage request);

        /// <summary>
        /// The operation contract for the AddNodes service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/AddNodes", ReplyAction = Namespaces.OpcUaWsdl + "/AddNodesResponse")]
#endif
        IAsyncResult BeginAddNodes(AddNodesMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a AddNodes service request.
        /// </summary>
        AddNodesResponseMessage EndAddNodes(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the AddNodes service.
        /// </summary>
        Task<AddNodesResponseMessage> AddNodesAsync(AddNodesMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_AddReferences)
        /// <summary>
        /// The operation contract for the AddReferences service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/AddReferences", ReplyAction = Namespaces.OpcUaWsdl + "/AddReferencesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/AddReferencesFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        AddReferencesResponseMessage AddReferences(AddReferencesMessage request);

        /// <summary>
        /// The operation contract for the AddReferences service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/AddReferences", ReplyAction = Namespaces.OpcUaWsdl + "/AddReferencesResponse")]
#endif
        IAsyncResult BeginAddReferences(AddReferencesMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a AddReferences service request.
        /// </summary>
        AddReferencesResponseMessage EndAddReferences(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the AddReferences service.
        /// </summary>
        Task<AddReferencesResponseMessage> AddReferencesAsync(AddReferencesMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_DeleteNodes)
        /// <summary>
        /// The operation contract for the DeleteNodes service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/DeleteNodes", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteNodesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/DeleteNodesFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        DeleteNodesResponseMessage DeleteNodes(DeleteNodesMessage request);

        /// <summary>
        /// The operation contract for the DeleteNodes service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/DeleteNodes", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteNodesResponse")]
#endif
        IAsyncResult BeginDeleteNodes(DeleteNodesMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a DeleteNodes service request.
        /// </summary>
        DeleteNodesResponseMessage EndDeleteNodes(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the DeleteNodes service.
        /// </summary>
        Task<DeleteNodesResponseMessage> DeleteNodesAsync(DeleteNodesMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_DeleteReferences)
        /// <summary>
        /// The operation contract for the DeleteReferences service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/DeleteReferences", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteReferencesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/DeleteReferencesFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        DeleteReferencesResponseMessage DeleteReferences(DeleteReferencesMessage request);

        /// <summary>
        /// The operation contract for the DeleteReferences service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/DeleteReferences", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteReferencesResponse")]
#endif
        IAsyncResult BeginDeleteReferences(DeleteReferencesMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a DeleteReferences service request.
        /// </summary>
        DeleteReferencesResponseMessage EndDeleteReferences(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the DeleteReferences service.
        /// </summary>
        Task<DeleteReferencesResponseMessage> DeleteReferencesAsync(DeleteReferencesMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_Browse)
        /// <summary>
        /// The operation contract for the Browse service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/Browse", ReplyAction = Namespaces.OpcUaWsdl + "/BrowseResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/BrowseFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        BrowseResponseMessage Browse(BrowseMessage request);

        /// <summary>
        /// The operation contract for the Browse service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/Browse", ReplyAction = Namespaces.OpcUaWsdl + "/BrowseResponse")]
#endif
        IAsyncResult BeginBrowse(BrowseMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a Browse service request.
        /// </summary>
        BrowseResponseMessage EndBrowse(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the Browse service.
        /// </summary>
        Task<BrowseResponseMessage> BrowseAsync(BrowseMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_BrowseNext)
        /// <summary>
        /// The operation contract for the BrowseNext service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/BrowseNext", ReplyAction = Namespaces.OpcUaWsdl + "/BrowseNextResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/BrowseNextFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        BrowseNextResponseMessage BrowseNext(BrowseNextMessage request);

        /// <summary>
        /// The operation contract for the BrowseNext service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/BrowseNext", ReplyAction = Namespaces.OpcUaWsdl + "/BrowseNextResponse")]
#endif
        IAsyncResult BeginBrowseNext(BrowseNextMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a BrowseNext service request.
        /// </summary>
        BrowseNextResponseMessage EndBrowseNext(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the BrowseNext service.
        /// </summary>
        Task<BrowseNextResponseMessage> BrowseNextAsync(BrowseNextMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_TranslateBrowsePathsToNodeIds)
        /// <summary>
        /// The operation contract for the TranslateBrowsePathsToNodeIds service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/TranslateBrowsePathsToNodeIds", ReplyAction = Namespaces.OpcUaWsdl + "/TranslateBrowsePathsToNodeIdsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/TranslateBrowsePathsToNodeIdsFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        TranslateBrowsePathsToNodeIdsResponseMessage TranslateBrowsePathsToNodeIds(TranslateBrowsePathsToNodeIdsMessage request);

        /// <summary>
        /// The operation contract for the TranslateBrowsePathsToNodeIds service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/TranslateBrowsePathsToNodeIds", ReplyAction = Namespaces.OpcUaWsdl + "/TranslateBrowsePathsToNodeIdsResponse")]
#endif
        IAsyncResult BeginTranslateBrowsePathsToNodeIds(TranslateBrowsePathsToNodeIdsMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a TranslateBrowsePathsToNodeIds service request.
        /// </summary>
        TranslateBrowsePathsToNodeIdsResponseMessage EndTranslateBrowsePathsToNodeIds(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the TranslateBrowsePathsToNodeIds service.
        /// </summary>
        Task<TranslateBrowsePathsToNodeIdsResponseMessage> TranslateBrowsePathsToNodeIdsAsync(TranslateBrowsePathsToNodeIdsMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_RegisterNodes)
        /// <summary>
        /// The operation contract for the RegisterNodes service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/RegisterNodes", ReplyAction = Namespaces.OpcUaWsdl + "/RegisterNodesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/RegisterNodesFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        RegisterNodesResponseMessage RegisterNodes(RegisterNodesMessage request);

        /// <summary>
        /// The operation contract for the RegisterNodes service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/RegisterNodes", ReplyAction = Namespaces.OpcUaWsdl + "/RegisterNodesResponse")]
#endif
        IAsyncResult BeginRegisterNodes(RegisterNodesMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a RegisterNodes service request.
        /// </summary>
        RegisterNodesResponseMessage EndRegisterNodes(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the RegisterNodes service.
        /// </summary>
        Task<RegisterNodesResponseMessage> RegisterNodesAsync(RegisterNodesMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_UnregisterNodes)
        /// <summary>
        /// The operation contract for the UnregisterNodes service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/UnregisterNodes", ReplyAction = Namespaces.OpcUaWsdl + "/UnregisterNodesResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/UnregisterNodesFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        UnregisterNodesResponseMessage UnregisterNodes(UnregisterNodesMessage request);

        /// <summary>
        /// The operation contract for the UnregisterNodes service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/UnregisterNodes", ReplyAction = Namespaces.OpcUaWsdl + "/UnregisterNodesResponse")]
#endif
        IAsyncResult BeginUnregisterNodes(UnregisterNodesMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a UnregisterNodes service request.
        /// </summary>
        UnregisterNodesResponseMessage EndUnregisterNodes(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the UnregisterNodes service.
        /// </summary>
        Task<UnregisterNodesResponseMessage> UnregisterNodesAsync(UnregisterNodesMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_QueryFirst)
        /// <summary>
        /// The operation contract for the QueryFirst service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/QueryFirst", ReplyAction = Namespaces.OpcUaWsdl + "/QueryFirstResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/QueryFirstFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        QueryFirstResponseMessage QueryFirst(QueryFirstMessage request);

        /// <summary>
        /// The operation contract for the QueryFirst service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/QueryFirst", ReplyAction = Namespaces.OpcUaWsdl + "/QueryFirstResponse")]
#endif
        IAsyncResult BeginQueryFirst(QueryFirstMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a QueryFirst service request.
        /// </summary>
        QueryFirstResponseMessage EndQueryFirst(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the QueryFirst service.
        /// </summary>
        Task<QueryFirstResponseMessage> QueryFirstAsync(QueryFirstMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_QueryNext)
        /// <summary>
        /// The operation contract for the QueryNext service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/QueryNext", ReplyAction = Namespaces.OpcUaWsdl + "/QueryNextResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/QueryNextFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        QueryNextResponseMessage QueryNext(QueryNextMessage request);

        /// <summary>
        /// The operation contract for the QueryNext service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/QueryNext", ReplyAction = Namespaces.OpcUaWsdl + "/QueryNextResponse")]
#endif
        IAsyncResult BeginQueryNext(QueryNextMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a QueryNext service request.
        /// </summary>
        QueryNextResponseMessage EndQueryNext(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the QueryNext service.
        /// </summary>
        Task<QueryNextResponseMessage> QueryNextAsync(QueryNextMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_Read)
        /// <summary>
        /// The operation contract for the Read service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/Read", ReplyAction = Namespaces.OpcUaWsdl + "/ReadResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/ReadFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        ReadResponseMessage Read(ReadMessage request);

        /// <summary>
        /// The operation contract for the Read service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/Read", ReplyAction = Namespaces.OpcUaWsdl + "/ReadResponse")]
#endif
        IAsyncResult BeginRead(ReadMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a Read service request.
        /// </summary>
        ReadResponseMessage EndRead(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the Read service.
        /// </summary>
        Task<ReadResponseMessage> ReadAsync(ReadMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_HistoryRead)
        /// <summary>
        /// The operation contract for the HistoryRead service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/HistoryRead", ReplyAction = Namespaces.OpcUaWsdl + "/HistoryReadResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/HistoryReadFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        HistoryReadResponseMessage HistoryRead(HistoryReadMessage request);

        /// <summary>
        /// The operation contract for the HistoryRead service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/HistoryRead", ReplyAction = Namespaces.OpcUaWsdl + "/HistoryReadResponse")]
#endif
        IAsyncResult BeginHistoryRead(HistoryReadMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a HistoryRead service request.
        /// </summary>
        HistoryReadResponseMessage EndHistoryRead(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the HistoryRead service.
        /// </summary>
        Task<HistoryReadResponseMessage> HistoryReadAsync(HistoryReadMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_Write)
        /// <summary>
        /// The operation contract for the Write service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/Write", ReplyAction = Namespaces.OpcUaWsdl + "/WriteResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/WriteFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        WriteResponseMessage Write(WriteMessage request);

        /// <summary>
        /// The operation contract for the Write service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/Write", ReplyAction = Namespaces.OpcUaWsdl + "/WriteResponse")]
#endif
        IAsyncResult BeginWrite(WriteMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a Write service request.
        /// </summary>
        WriteResponseMessage EndWrite(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the Write service.
        /// </summary>
        Task<WriteResponseMessage> WriteAsync(WriteMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_HistoryUpdate)
        /// <summary>
        /// The operation contract for the HistoryUpdate service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/HistoryUpdate", ReplyAction = Namespaces.OpcUaWsdl + "/HistoryUpdateResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/HistoryUpdateFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        HistoryUpdateResponseMessage HistoryUpdate(HistoryUpdateMessage request);

        /// <summary>
        /// The operation contract for the HistoryUpdate service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/HistoryUpdate", ReplyAction = Namespaces.OpcUaWsdl + "/HistoryUpdateResponse")]
#endif
        IAsyncResult BeginHistoryUpdate(HistoryUpdateMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a HistoryUpdate service request.
        /// </summary>
        HistoryUpdateResponseMessage EndHistoryUpdate(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the HistoryUpdate service.
        /// </summary>
        Task<HistoryUpdateResponseMessage> HistoryUpdateAsync(HistoryUpdateMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_Call)
        /// <summary>
        /// The operation contract for the Call service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/Call", ReplyAction = Namespaces.OpcUaWsdl + "/CallResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CallFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        CallResponseMessage Call(CallMessage request);

        /// <summary>
        /// The operation contract for the Call service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/Call", ReplyAction = Namespaces.OpcUaWsdl + "/CallResponse")]
#endif
        IAsyncResult BeginCall(CallMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a Call service request.
        /// </summary>
        CallResponseMessage EndCall(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the Call service.
        /// </summary>
        Task<CallResponseMessage> CallAsync(CallMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_CreateMonitoredItems)
        /// <summary>
        /// The operation contract for the CreateMonitoredItems service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/CreateMonitoredItems", ReplyAction = Namespaces.OpcUaWsdl + "/CreateMonitoredItemsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CreateMonitoredItemsFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        CreateMonitoredItemsResponseMessage CreateMonitoredItems(CreateMonitoredItemsMessage request);

        /// <summary>
        /// The operation contract for the CreateMonitoredItems service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/CreateMonitoredItems", ReplyAction = Namespaces.OpcUaWsdl + "/CreateMonitoredItemsResponse")]
#endif
        IAsyncResult BeginCreateMonitoredItems(CreateMonitoredItemsMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a CreateMonitoredItems service request.
        /// </summary>
        CreateMonitoredItemsResponseMessage EndCreateMonitoredItems(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the CreateMonitoredItems service.
        /// </summary>
        Task<CreateMonitoredItemsResponseMessage> CreateMonitoredItemsAsync(CreateMonitoredItemsMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_ModifyMonitoredItems)
        /// <summary>
        /// The operation contract for the ModifyMonitoredItems service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/ModifyMonitoredItems", ReplyAction = Namespaces.OpcUaWsdl + "/ModifyMonitoredItemsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/ModifyMonitoredItemsFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        ModifyMonitoredItemsResponseMessage ModifyMonitoredItems(ModifyMonitoredItemsMessage request);

        /// <summary>
        /// The operation contract for the ModifyMonitoredItems service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/ModifyMonitoredItems", ReplyAction = Namespaces.OpcUaWsdl + "/ModifyMonitoredItemsResponse")]
#endif
        IAsyncResult BeginModifyMonitoredItems(ModifyMonitoredItemsMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a ModifyMonitoredItems service request.
        /// </summary>
        ModifyMonitoredItemsResponseMessage EndModifyMonitoredItems(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the ModifyMonitoredItems service.
        /// </summary>
        Task<ModifyMonitoredItemsResponseMessage> ModifyMonitoredItemsAsync(ModifyMonitoredItemsMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_SetMonitoringMode)
        /// <summary>
        /// The operation contract for the SetMonitoringMode service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/SetMonitoringMode", ReplyAction = Namespaces.OpcUaWsdl + "/SetMonitoringModeResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/SetMonitoringModeFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        SetMonitoringModeResponseMessage SetMonitoringMode(SetMonitoringModeMessage request);

        /// <summary>
        /// The operation contract for the SetMonitoringMode service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/SetMonitoringMode", ReplyAction = Namespaces.OpcUaWsdl + "/SetMonitoringModeResponse")]
#endif
        IAsyncResult BeginSetMonitoringMode(SetMonitoringModeMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a SetMonitoringMode service request.
        /// </summary>
        SetMonitoringModeResponseMessage EndSetMonitoringMode(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the SetMonitoringMode service.
        /// </summary>
        Task<SetMonitoringModeResponseMessage> SetMonitoringModeAsync(SetMonitoringModeMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_SetTriggering)
        /// <summary>
        /// The operation contract for the SetTriggering service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/SetTriggering", ReplyAction = Namespaces.OpcUaWsdl + "/SetTriggeringResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/SetTriggeringFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        SetTriggeringResponseMessage SetTriggering(SetTriggeringMessage request);

        /// <summary>
        /// The operation contract for the SetTriggering service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/SetTriggering", ReplyAction = Namespaces.OpcUaWsdl + "/SetTriggeringResponse")]
#endif
        IAsyncResult BeginSetTriggering(SetTriggeringMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a SetTriggering service request.
        /// </summary>
        SetTriggeringResponseMessage EndSetTriggering(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the SetTriggering service.
        /// </summary>
        Task<SetTriggeringResponseMessage> SetTriggeringAsync(SetTriggeringMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_DeleteMonitoredItems)
        /// <summary>
        /// The operation contract for the DeleteMonitoredItems service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/DeleteMonitoredItems", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteMonitoredItemsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/DeleteMonitoredItemsFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        DeleteMonitoredItemsResponseMessage DeleteMonitoredItems(DeleteMonitoredItemsMessage request);

        /// <summary>
        /// The operation contract for the DeleteMonitoredItems service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/DeleteMonitoredItems", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteMonitoredItemsResponse")]
#endif
        IAsyncResult BeginDeleteMonitoredItems(DeleteMonitoredItemsMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a DeleteMonitoredItems service request.
        /// </summary>
        DeleteMonitoredItemsResponseMessage EndDeleteMonitoredItems(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the DeleteMonitoredItems service.
        /// </summary>
        Task<DeleteMonitoredItemsResponseMessage> DeleteMonitoredItemsAsync(DeleteMonitoredItemsMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_CreateSubscription)
        /// <summary>
        /// The operation contract for the CreateSubscription service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/CreateSubscription", ReplyAction = Namespaces.OpcUaWsdl + "/CreateSubscriptionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/CreateSubscriptionFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        CreateSubscriptionResponseMessage CreateSubscription(CreateSubscriptionMessage request);

        /// <summary>
        /// The operation contract for the CreateSubscription service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/CreateSubscription", ReplyAction = Namespaces.OpcUaWsdl + "/CreateSubscriptionResponse")]
#endif
        IAsyncResult BeginCreateSubscription(CreateSubscriptionMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a CreateSubscription service request.
        /// </summary>
        CreateSubscriptionResponseMessage EndCreateSubscription(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the CreateSubscription service.
        /// </summary>
        Task<CreateSubscriptionResponseMessage> CreateSubscriptionAsync(CreateSubscriptionMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_ModifySubscription)
        /// <summary>
        /// The operation contract for the ModifySubscription service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/ModifySubscription", ReplyAction = Namespaces.OpcUaWsdl + "/ModifySubscriptionResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/ModifySubscriptionFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        ModifySubscriptionResponseMessage ModifySubscription(ModifySubscriptionMessage request);

        /// <summary>
        /// The operation contract for the ModifySubscription service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/ModifySubscription", ReplyAction = Namespaces.OpcUaWsdl + "/ModifySubscriptionResponse")]
#endif
        IAsyncResult BeginModifySubscription(ModifySubscriptionMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a ModifySubscription service request.
        /// </summary>
        ModifySubscriptionResponseMessage EndModifySubscription(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the ModifySubscription service.
        /// </summary>
        Task<ModifySubscriptionResponseMessage> ModifySubscriptionAsync(ModifySubscriptionMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_SetPublishingMode)
        /// <summary>
        /// The operation contract for the SetPublishingMode service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/SetPublishingMode", ReplyAction = Namespaces.OpcUaWsdl + "/SetPublishingModeResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/SetPublishingModeFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        SetPublishingModeResponseMessage SetPublishingMode(SetPublishingModeMessage request);

        /// <summary>
        /// The operation contract for the SetPublishingMode service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/SetPublishingMode", ReplyAction = Namespaces.OpcUaWsdl + "/SetPublishingModeResponse")]
#endif
        IAsyncResult BeginSetPublishingMode(SetPublishingModeMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a SetPublishingMode service request.
        /// </summary>
        SetPublishingModeResponseMessage EndSetPublishingMode(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the SetPublishingMode service.
        /// </summary>
        Task<SetPublishingModeResponseMessage> SetPublishingModeAsync(SetPublishingModeMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_Publish)
        /// <summary>
        /// The operation contract for the Publish service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/Publish", ReplyAction = Namespaces.OpcUaWsdl + "/PublishResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/PublishFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        PublishResponseMessage Publish(PublishMessage request);

        /// <summary>
        /// The operation contract for the Publish service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/Publish", ReplyAction = Namespaces.OpcUaWsdl + "/PublishResponse")]
#endif
        IAsyncResult BeginPublish(PublishMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a Publish service request.
        /// </summary>
        PublishResponseMessage EndPublish(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the Publish service.
        /// </summary>
        Task<PublishResponseMessage> PublishAsync(PublishMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_Republish)
        /// <summary>
        /// The operation contract for the Republish service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/Republish", ReplyAction = Namespaces.OpcUaWsdl + "/RepublishResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/RepublishFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        RepublishResponseMessage Republish(RepublishMessage request);

        /// <summary>
        /// The operation contract for the Republish service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/Republish", ReplyAction = Namespaces.OpcUaWsdl + "/RepublishResponse")]
#endif
        IAsyncResult BeginRepublish(RepublishMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a Republish service request.
        /// </summary>
        RepublishResponseMessage EndRepublish(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the Republish service.
        /// </summary>
        Task<RepublishResponseMessage> RepublishAsync(RepublishMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_TransferSubscriptions)
        /// <summary>
        /// The operation contract for the TransferSubscriptions service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/TransferSubscriptions", ReplyAction = Namespaces.OpcUaWsdl + "/TransferSubscriptionsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/TransferSubscriptionsFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        TransferSubscriptionsResponseMessage TransferSubscriptions(TransferSubscriptionsMessage request);

        /// <summary>
        /// The operation contract for the TransferSubscriptions service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/TransferSubscriptions", ReplyAction = Namespaces.OpcUaWsdl + "/TransferSubscriptionsResponse")]
#endif
        IAsyncResult BeginTransferSubscriptions(TransferSubscriptionsMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a TransferSubscriptions service request.
        /// </summary>
        TransferSubscriptionsResponseMessage EndTransferSubscriptions(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the TransferSubscriptions service.
        /// </summary>
        Task<TransferSubscriptionsResponseMessage> TransferSubscriptionsAsync(TransferSubscriptionsMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_DeleteSubscriptions)
        /// <summary>
        /// The operation contract for the DeleteSubscriptions service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/DeleteSubscriptions", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteSubscriptionsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/DeleteSubscriptionsFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        DeleteSubscriptionsResponseMessage DeleteSubscriptions(DeleteSubscriptionsMessage request);

        /// <summary>
        /// The operation contract for the DeleteSubscriptions service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/DeleteSubscriptions", ReplyAction = Namespaces.OpcUaWsdl + "/DeleteSubscriptionsResponse")]
#endif
        IAsyncResult BeginDeleteSubscriptions(DeleteSubscriptionsMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a DeleteSubscriptions service request.
        /// </summary>
        DeleteSubscriptionsResponseMessage EndDeleteSubscriptions(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the DeleteSubscriptions service.
        /// </summary>
        Task<DeleteSubscriptionsResponseMessage> DeleteSubscriptionsAsync(DeleteSubscriptionsMessage request);
#endif
#endif
        #endregion
    }
    #endregion

    #region IDiscoveryEndpoint Interface
#if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
    /// <summary>
    /// The service contract which must be implemented by all UA servers.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [ServiceContract(Namespace = Namespaces.OpcUaWsdl)]
    public interface IDiscoveryEndpoint : IEndpointBase
    {
#if (!OPCUA_EXCLUDE_FindServers)
        /// <summary>
        /// The operation contract for the FindServers service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/FindServers", ReplyAction = Namespaces.OpcUaWsdl + "/FindServersResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/FindServersFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        FindServersResponseMessage FindServers(FindServersMessage request);
#endif

#if (!OPCUA_EXCLUDE_FindServersOnNetwork)
        /// <summary>
        /// The operation contract for the FindServersOnNetwork service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/FindServersOnNetwork", ReplyAction = Namespaces.OpcUaWsdl + "/FindServersOnNetworkResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/FindServersOnNetworkFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        FindServersOnNetworkResponseMessage FindServersOnNetwork(FindServersOnNetworkMessage request);
#endif

#if (!OPCUA_EXCLUDE_GetEndpoints)
        /// <summary>
        /// The operation contract for the GetEndpoints service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/GetEndpoints", ReplyAction = Namespaces.OpcUaWsdl + "/GetEndpointsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/GetEndpointsFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        GetEndpointsResponseMessage GetEndpoints(GetEndpointsMessage request);
#endif
    }
#else
    /// <summary>
    /// The asynchronous service contract which must be implemented by UA servers.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
#if (!NET_STANDARD)
    [ServiceContract(Namespace = Namespaces.OpcUaWsdl)]
#endif
    public interface IDiscoveryEndpoint : IEndpointBase
    {
#if (!OPCUA_EXCLUDE_FindServers)
        /// <summary>
        /// The operation contract for the FindServers service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/FindServers", ReplyAction = Namespaces.OpcUaWsdl + "/FindServersResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/FindServersFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginFindServers(FindServersMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a FindServers service request.
        /// </summary>
        FindServersResponseMessage EndFindServers(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_FindServersOnNetwork)
        /// <summary>
        /// The operation contract for the FindServersOnNetwork service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/FindServersOnNetwork", ReplyAction = Namespaces.OpcUaWsdl + "/FindServersOnNetworkResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/FindServersOnNetworkFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginFindServersOnNetwork(FindServersOnNetworkMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a FindServersOnNetwork service request.
        /// </summary>
        FindServersOnNetworkResponseMessage EndFindServersOnNetwork(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_GetEndpoints)
        /// <summary>
        /// The operation contract for the GetEndpoints service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/GetEndpoints", ReplyAction = Namespaces.OpcUaWsdl + "/GetEndpointsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/GetEndpointsFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginGetEndpoints(GetEndpointsMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a GetEndpoints service request.
        /// </summary>
        GetEndpointsResponseMessage EndGetEndpoints(IAsyncResult result);

#endif
    }
#endif
    #endregion

    #region IDiscoveryChannel Interface
    /// <summary>
    /// An interface used by by clients to access a UA server.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
#if (!NET_STANDARD)
    [ServiceContract(Namespace = Namespaces.OpcUaWsdl)]
#endif
    public interface IDiscoveryChannel : IChannelBase
    {
#if (!OPCUA_EXCLUDE_FindServers)
        /// <summary>
        /// The operation contract for the FindServers service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/FindServers", ReplyAction = Namespaces.OpcUaWsdl + "/FindServersResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/FindServersFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        FindServersResponseMessage FindServers(FindServersMessage request);

        /// <summary>
        /// The operation contract for the FindServers service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/FindServers", ReplyAction = Namespaces.OpcUaWsdl + "/FindServersResponse")]
#endif
        IAsyncResult BeginFindServers(FindServersMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a FindServers service request.
        /// </summary>
        FindServersResponseMessage EndFindServers(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the FindServers service.
        /// </summary>
        Task<FindServersResponseMessage> FindServersAsync(FindServersMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_FindServersOnNetwork)
        /// <summary>
        /// The operation contract for the FindServersOnNetwork service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/FindServersOnNetwork", ReplyAction = Namespaces.OpcUaWsdl + "/FindServersOnNetworkResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/FindServersOnNetworkFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        FindServersOnNetworkResponseMessage FindServersOnNetwork(FindServersOnNetworkMessage request);

        /// <summary>
        /// The operation contract for the FindServersOnNetwork service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/FindServersOnNetwork", ReplyAction = Namespaces.OpcUaWsdl + "/FindServersOnNetworkResponse")]
#endif
        IAsyncResult BeginFindServersOnNetwork(FindServersOnNetworkMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a FindServersOnNetwork service request.
        /// </summary>
        FindServersOnNetworkResponseMessage EndFindServersOnNetwork(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the FindServersOnNetwork service.
        /// </summary>
        Task<FindServersOnNetworkResponseMessage> FindServersOnNetworkAsync(FindServersOnNetworkMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_GetEndpoints)
        /// <summary>
        /// The operation contract for the GetEndpoints service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/GetEndpoints", ReplyAction = Namespaces.OpcUaWsdl + "/GetEndpointsResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/GetEndpointsFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        GetEndpointsResponseMessage GetEndpoints(GetEndpointsMessage request);

        /// <summary>
        /// The operation contract for the GetEndpoints service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/GetEndpoints", ReplyAction = Namespaces.OpcUaWsdl + "/GetEndpointsResponse")]
#endif
        IAsyncResult BeginGetEndpoints(GetEndpointsMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a GetEndpoints service request.
        /// </summary>
        GetEndpointsResponseMessage EndGetEndpoints(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the GetEndpoints service.
        /// </summary>
        Task<GetEndpointsResponseMessage> GetEndpointsAsync(GetEndpointsMessage request);
#endif
#endif
    }
    #endregion

    #region IRegistrationEndpoint Interface
#if OPCUA_USE_SYNCHRONOUS_ENDPOINTS
    /// <summary>
    /// The service contract which must be implemented by all UA servers.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
    [ServiceContract(Namespace = Namespaces.OpcUaWsdl)]
    public interface IRegistrationEndpoint : IEndpointBase
    {
#if (!OPCUA_EXCLUDE_RegisterServer)
        /// <summary>
        /// The operation contract for the RegisterServer service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/RegisterServer", ReplyAction = Namespaces.OpcUaWsdl + "/RegisterServerResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/RegisterServerFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        RegisterServerResponseMessage RegisterServer(RegisterServerMessage request);
#endif

#if (!OPCUA_EXCLUDE_RegisterServer2)
        /// <summary>
        /// The operation contract for the RegisterServer2 service.
        /// </summary>
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/RegisterServer2", ReplyAction = Namespaces.OpcUaWsdl + "/RegisterServer2Response")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/RegisterServer2Fault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
        RegisterServer2ResponseMessage RegisterServer2(RegisterServer2Message request);
#endif
    }
#else
    /// <summary>
    /// The asynchronous service contract which must be implemented by UA servers.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
#if (!NET_STANDARD)
    [ServiceContract(Namespace = Namespaces.OpcUaWsdl)]
#endif
    public interface IRegistrationEndpoint : IEndpointBase
    {
#if (!OPCUA_EXCLUDE_RegisterServer)
        /// <summary>
        /// The operation contract for the RegisterServer service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/RegisterServer", ReplyAction = Namespaces.OpcUaWsdl + "/RegisterServerResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/RegisterServerFault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginRegisterServer(RegisterServerMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a RegisterServer service request.
        /// </summary>
        RegisterServerResponseMessage EndRegisterServer(IAsyncResult result);

#endif

#if (!OPCUA_EXCLUDE_RegisterServer2)
        /// <summary>
        /// The operation contract for the RegisterServer2 service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/RegisterServer2", ReplyAction = Namespaces.OpcUaWsdl + "/RegisterServer2Response")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/RegisterServer2Fault", Name = "ServiceFault", Namespace = Namespaces.OpcUaXsd)]
#endif
        IAsyncResult BeginRegisterServer2(RegisterServer2Message request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a RegisterServer2 service request.
        /// </summary>
        RegisterServer2ResponseMessage EndRegisterServer2(IAsyncResult result);

#endif
    }
#endif
    #endregion

    #region IRegistrationChannel Interface
    /// <summary>
    /// An interface used by by clients to access a UA server.
    /// </summary>
    /// <exclude />
    [System.CodeDom.Compiler.GeneratedCodeAttribute("Opc.Ua.ModelCompiler", "1.0.0.0")]
#if (!NET_STANDARD)
    [ServiceContract(Namespace = Namespaces.OpcUaWsdl)]
#endif
    public interface IRegistrationChannel : IChannelBase
    {
#if (!OPCUA_EXCLUDE_RegisterServer)
        /// <summary>
        /// The operation contract for the RegisterServer service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/RegisterServer", ReplyAction = Namespaces.OpcUaWsdl + "/RegisterServerResponse")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/RegisterServerFault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        RegisterServerResponseMessage RegisterServer(RegisterServerMessage request);

        /// <summary>
        /// The operation contract for the RegisterServer service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/RegisterServer", ReplyAction = Namespaces.OpcUaWsdl + "/RegisterServerResponse")]
#endif
        IAsyncResult BeginRegisterServer(RegisterServerMessage request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a RegisterServer service request.
        /// </summary>
        RegisterServerResponseMessage EndRegisterServer(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the RegisterServer service.
        /// </summary>
        Task<RegisterServerResponseMessage> RegisterServerAsync(RegisterServerMessage request);
#endif
#endif

#if (!OPCUA_EXCLUDE_RegisterServer2)
        /// <summary>
        /// The operation contract for the RegisterServer2 service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContract(Action = Namespaces.OpcUaWsdl + "/RegisterServer2", ReplyAction = Namespaces.OpcUaWsdl + "/RegisterServer2Response")]
        [FaultContract(typeof(ServiceFault), Action = Namespaces.OpcUaWsdl + "/RegisterServer2Fault", Name="ServiceFault", Namespace=Namespaces.OpcUaXsd)]
#endif
        RegisterServer2ResponseMessage RegisterServer2(RegisterServer2Message request);

        /// <summary>
        /// The operation contract for the RegisterServer2 service.
        /// </summary>
#if (!NET_STANDARD)
        [OperationContractAttribute(AsyncPattern=true, Action=Namespaces.OpcUaWsdl + "/RegisterServer2", ReplyAction = Namespaces.OpcUaWsdl + "/RegisterServer2Response")]
#endif
        IAsyncResult BeginRegisterServer2(RegisterServer2Message request, AsyncCallback callback, object asyncState);

        /// <summary>
        /// The method used to retrieve the results of a RegisterServer2 service request.
        /// </summary>
        RegisterServer2ResponseMessage EndRegisterServer2(IAsyncResult result);

#if (NET_STANDARD_ASYNC)
        /// <summary>
        /// The async operation contract for the RegisterServer2 service.
        /// </summary>
        Task<RegisterServer2ResponseMessage> RegisterServer2Async(RegisterServer2Message request);
#endif
#endif
    }
    #endregion
}
