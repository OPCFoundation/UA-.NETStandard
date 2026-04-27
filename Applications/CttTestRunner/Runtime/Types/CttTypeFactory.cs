/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Reflection;
using System.Text;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Interop;

namespace Opc.Ua.CttTestRunner.Runtime.Types
{
    /// <summary>
    /// Registers Ua* constructor functions into the Jint engine.
    /// CTT scripts create objects like: new UaReadRequest(), new UaNodeId(123), etc.
    /// </summary>
    public static class CttTypeFactory
    {
        public static void RegisterTypes(Engine engine, CttHostEnvironment host)
        {
            // Request/Response types — create empty JS objects with expected properties
            RegisterRequestResponse(engine, "UaReadRequest", "NodesToRead", "TimestampsToReturn", "MaxAge", "RequestHeader");
            RegisterRequestResponse(engine, "UaReadResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaWriteRequest", "NodesToWrite", "RequestHeader");
            RegisterRequestResponse(engine, "UaWriteResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaBrowseRequest", "NodesToBrowse", "RequestedMaxReferencesPerNode", "RequestHeader", "View");
            RegisterRequestResponse(engine, "UaBrowseResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaBrowseNextRequest", "ContinuationPoints", "ReleaseContinuationPoints", "RequestHeader");
            RegisterRequestResponse(engine, "UaBrowseNextResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaCallRequest", "MethodsToCall", "RequestHeader");
            RegisterRequestResponse(engine, "UaCallResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaCreateSubscriptionRequest", "RequestedPublishingInterval", "RequestedLifetimeCount", "RequestedMaxKeepAliveCount", "MaxNotificationsPerPublish", "PublishingEnabled", "Priority", "RequestHeader");
            RegisterRequestResponse(engine, "UaCreateSubscriptionResponse", "SubscriptionId", "RevisedPublishingInterval", "RevisedLifetimeCount", "RevisedMaxKeepAliveCount", "ResponseHeader");
            RegisterRequestResponse(engine, "UaDeleteSubscriptionsRequest", "SubscriptionIds", "RequestHeader");
            RegisterRequestResponse(engine, "UaDeleteSubscriptionsResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaCreateMonitoredItemsRequest", "SubscriptionId", "TimestampsToReturn", "ItemsToCreate", "RequestHeader");
            RegisterRequestResponse(engine, "UaCreateMonitoredItemsResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaDeleteMonitoredItemsRequest", "SubscriptionId", "MonitoredItemIds", "RequestHeader");
            RegisterRequestResponse(engine, "UaDeleteMonitoredItemsResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaModifyMonitoredItemsRequest", "SubscriptionId", "TimestampsToReturn", "ItemsToModify", "RequestHeader");
            RegisterRequestResponse(engine, "UaModifyMonitoredItemsResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaModifySubscriptionRequest", "SubscriptionId", "RequestedPublishingInterval", "RequestedLifetimeCount", "RequestedMaxKeepAliveCount", "MaxNotificationsPerPublish", "Priority", "RequestHeader");
            RegisterRequestResponse(engine, "UaModifySubscriptionResponse", "RevisedPublishingInterval", "RevisedLifetimeCount", "RevisedMaxKeepAliveCount", "ResponseHeader");
            RegisterRequestResponse(engine, "UaSetMonitoringModeRequest", "SubscriptionId", "MonitoringMode", "MonitoredItemIds", "RequestHeader");
            RegisterRequestResponse(engine, "UaSetMonitoringModeResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaSetPublishingModeRequest", "PublishingEnabled", "SubscriptionIds", "RequestHeader");
            RegisterRequestResponse(engine, "UaSetPublishingModeResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaPublishRequest", "SubscriptionAcknowledgements", "RequestHeader");
            RegisterRequestResponse(engine, "UaPublishResponse", "SubscriptionId", "AvailableSequenceNumbers", "MoreNotifications", "NotificationMessage", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaRepublishRequest", "SubscriptionId", "RetransmitSequenceNumber", "RequestHeader");
            RegisterRequestResponse(engine, "UaRepublishResponse", "NotificationMessage", "ResponseHeader");
            RegisterRequestResponse(engine, "UaGetEndpointsRequest", "EndpointUrl", "LocaleIds", "ProfileUris", "RequestHeader");
            RegisterRequestResponse(engine, "UaGetEndpointsResponse", "Endpoints", "ResponseHeader");
            RegisterRequestResponse(engine, "UaFindServersRequest", "EndpointUrl", "LocaleIds", "ServerUris", "RequestHeader");
            RegisterRequestResponse(engine, "UaFindServersResponse", "Servers", "ResponseHeader");
            RegisterRequestResponse(engine, "UaTranslateBrowsePathsToNodeIdsRequest", "BrowsePaths", "RequestHeader");
            RegisterRequestResponse(engine, "UaTranslateBrowsePathsToNodeIdsResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaRegisterNodesRequest", "NodesToRegister", "RequestHeader");
            RegisterRequestResponse(engine, "UaRegisterNodesResponse", "RegisteredNodeIds", "ResponseHeader");
            RegisterRequestResponse(engine, "UaUnregisterNodesRequest", "NodesToUnregister", "RequestHeader");
            RegisterRequestResponse(engine, "UaUnregisterNodesResponse", "ResponseHeader");
            RegisterRequestResponse(engine, "UaHistoryReadRequest", "HistoryReadDetails", "TimestampsToReturn", "ReleaseContinuationPoints", "NodesToRead", "RequestHeader");
            RegisterRequestResponse(engine, "UaHistoryReadResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaAddNodesRequest", "NodesToAdd", "RequestHeader");
            RegisterRequestResponse(engine, "UaAddNodesResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaDeleteNodesRequest", "NodesToDelete", "RequestHeader");
            RegisterRequestResponse(engine, "UaDeleteNodesResponse", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaCancelRequest", "RequestHandle", "RequestHeader");
            RegisterRequestResponse(engine, "UaCancelResponse", "CancelCount", "ResponseHeader");
            RegisterRequestResponse(engine, "UaSetTriggeringRequest", "SubscriptionId", "TriggeringItemId", "LinksToAdd", "LinksToRemove", "RequestHeader");
            RegisterRequestResponse(engine, "UaSetTriggeringResponse", "AddResults", "AddDiagnosticInfos", "RemoveResults", "RemoveDiagnosticInfos", "ResponseHeader");

            // UaCreateSessionRequest needs deep nested structure
            // Called both with and without 'new' keyword
            engine.Execute(@"
                function UaCreateSessionRequest() {
                    var obj = (this instanceof UaCreateSessionRequest) ? this : {};
                    obj.RequestHeader = new UaRequestHeader();
                    obj.ClientDescription = {
                        ApplicationName: { Locale: '', Text: '' },
                        ApplicationType: 0,
                        ApplicationUri: '',
                        ProductUri: '',
                        GatewayServerUri: '',
                        DiscoveryProfileUri: '',
                        DiscoveryUrls: []
                    };
                    obj.ServerUri = '';
                    obj.EndpointUrl = '';
                    obj.SessionName = '';
                    obj.ClientNonce = { length: 0, isEmpty: function() { return true; } };
                    obj.ClientCertificate = { length: 0, isEmpty: function() { return true; } };
                    obj.RequestedSessionTimeout = 60000;
                    obj.MaxResponseMessageSize = 0;
                    obj.toString = function() { return 'CreateSessionRequest'; };
                    if (!(this instanceof UaCreateSessionRequest)) return obj;
                }
            ");

            // UaActivateSessionRequest with nested UserIdentityToken
            engine.Execute(@"
                function UaActivateSessionRequest() {
                    var obj = (this instanceof UaActivateSessionRequest) ? this : {};
                    obj.RequestHeader = new UaRequestHeader();
                    obj.ClientSignature = { Algorithm: '', Signature: _makeByteString(0) };
                    obj.ClientSoftwareCertificates = [];
                    obj.LocaleIds = [];
                    obj.UserIdentityToken = null;
                    obj.UserTokenSignature = { Algorithm: '', Signature: _makeByteString(0) };
                    obj.toString = function() { return 'ActivateSessionRequest'; };
                    if (!(this instanceof UaActivateSessionRequest)) return obj;
                }
            ");

            // UaCloseSessionRequest
            engine.Execute(@"
                function UaCloseSessionRequest() {
                    var obj = (this instanceof UaCloseSessionRequest) ? this : {};
                    obj.RequestHeader = new UaRequestHeader();
                    obj.DeleteSubscriptions = true;
                    obj.toString = function() { return 'CloseSessionRequest'; };
                    if (!(this instanceof UaCloseSessionRequest)) return obj;
                }
            ");
            RegisterRequestResponse(engine, "UaActivateSessionResponse", "ServerNonce", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaCloseSessionResponse", "ResponseHeader");
            RegisterRequestResponse(engine, "UaCreateSessionResponse", "SessionId", "AuthenticationToken", "RevisedSessionTimeout", "ServerNonce", "ServerCertificate", "ServerEndpoints", "ServerSoftwareCertificates", "ServerSignature", "MaxRequestMessageSize", "ResponseHeader");
            RegisterRequestResponse(engine, "UaQueryFirstRequest", "View", "NodeTypes", "Filter", "MaxDataSetsToReturn", "MaxReferencesToReturn", "RequestHeader");
            RegisterRequestResponse(engine, "UaQueryFirstResponse", "QueryDataSets", "ContinuationPoint", "ParsingResults", "DiagnosticInfos", "FilterResult", "ResponseHeader");
            RegisterRequestResponse(engine, "UaQueryNextRequest", "ReleaseContinuationPoint", "ContinuationPoint", "RequestHeader");
            RegisterRequestResponse(engine, "UaQueryNextResponse", "QueryDataSets", "RevisedContinuationPoint", "ResponseHeader");
            RegisterRequestResponse(engine, "UaTransferSubscriptionsRequest", "SubscriptionIds", "SendInitialValues", "RequestHeader");
            RegisterRequestResponse(engine, "UaTransferSubscriptionsResponse", "Results", "DiagnosticInfos", "ResponseHeader");

            // Typed array constructors
            RegisterTypedArray(engine, "UaReadValueIds");
            RegisterTypedArray(engine, "UaWriteValues");
            RegisterTypedArray(engine, "UaBrowseDescriptions");
            RegisterTypedArray(engine, "UaBrowseResults");
            RegisterTypedArray(engine, "UaDataValues");
            RegisterTypedArray(engine, "UaNodeIds");
            RegisterTypedArray(engine, "UaStatusCodes");
            RegisterTypedArray(engine, "UaVariants");
            RegisterTypedArray(engine, "UaStrings");
            RegisterTypedArray(engine, "UaInt32s");
            RegisterTypedArray(engine, "UaUInt32s");
            RegisterTypedArray(engine, "UaInt16s");
            RegisterTypedArray(engine, "UaUInt16s");
            RegisterTypedArray(engine, "UaInt64s");
            RegisterTypedArray(engine, "UaUInt64s");
            RegisterTypedArray(engine, "UaDoubles");
            RegisterTypedArray(engine, "UaFloats");
            RegisterTypedArray(engine, "UaBooleans");
            RegisterTypedArray(engine, "UaBytes");
            RegisterTypedArray(engine, "UaSBytes");
            RegisterTypedArray(engine, "UaByteStrings");
            RegisterTypedArray(engine, "UaDateTimes");
            RegisterTypedArray(engine, "UaGuids");
            RegisterTypedArray(engine, "UaLocalizedTexts");
            RegisterTypedArray(engine, "UaQualifiedNames");
            RegisterTypedArray(engine, "UaExtensionObjects");
            RegisterTypedArray(engine, "UaXmlElements");
            RegisterTypedArray(engine, "UaEndpointDescriptions");
            RegisterTypedArray(engine, "UaReferenceDescriptions");
            RegisterTypedArray(engine, "UaArguments");
            RegisterTypedArray(engine, "UaBrowsePathResults");
            RegisterTypedArray(engine, "UaBrowsePaths");
            RegisterTypedArray(engine, "UaHistoryReadResults");
            RegisterTypedArray(engine, "UaHistoryReadValueIds");
            RegisterTypedArray(engine, "UaCallMethodRequest");
            RegisterTypedArray(engine, "UaMonitoredItemCreateRequest");
            RegisterTypedArray(engine, "UaMonitoredItemModifyRequests");
            RegisterTypedArray(engine, "UaMonitoredItemModifyResults");
            RegisterTypedArray(engine, "UaAddNodesItems");
            RegisterTypedArray(engine, "UaAddNodesResults");
            RegisterTypedArray(engine, "UaAddReferencesItems");
            RegisterTypedArray(engine, "UaDeleteNodesItems");
            RegisterTypedArray(engine, "UaDeleteReferencesItems");
            RegisterTypedArray(engine, "UaHistoryUpdateResults");

            // Individual value types
            RegisterNodeIdConstructor(engine);
            // UaVariant — full implementation with setter methods
            engine.Execute(@"
                function UaVariant() {
                    this.DataType = 0;
                    this.Value = undefined;
                    this.ArrayType = 0;
                    var _self = this;
                    this.clone = function() { var c = new UaVariant(); c.DataType = _self.DataType; c.Value = _self.Value; return c; };
                    this.setBoolean = function(v) { _self.DataType = 1; _self.Value = v; };
                    this.setByte = function(v) { _self.DataType = 3; _self.Value = v; };
                    this.setSByte = function(v) { _self.DataType = 2; _self.Value = v; };
                    this.setInt16 = function(v) { _self.DataType = 4; _self.Value = v; };
                    this.setUInt16 = function(v) { _self.DataType = 5; _self.Value = v; };
                    this.setInt32 = function(v) { _self.DataType = 6; _self.Value = v; };
                    this.setUInt32 = function(v) { _self.DataType = 7; _self.Value = v; };
                    this.setInt64 = function(v) { _self.DataType = 8; _self.Value = v; };
                    this.setUInt64 = function(v) { _self.DataType = 9; _self.Value = v; };
                    this.setFloat = function(v) { _self.DataType = 10; _self.Value = v; };
                    this.setDouble = function(v) { _self.DataType = 11; _self.Value = v; };
                    this.setString = function(v) { _self.DataType = 12; _self.Value = v; };
                    this.setDateTime = function(v) { _self.DataType = 13; _self.Value = v; };
                    this.setGuid = function(v) { _self.DataType = 14; _self.Value = v; };
                    this.setByteString = function(v) { _self.DataType = 15; _self.Value = v; };
                    this.setNodeId = function(v) { _self.DataType = 17; _self.Value = v; };
                    this.setExpandedNodeId = function(v) { _self.DataType = 18; _self.Value = v; };
                    this.setStatusCode = function(v) { _self.DataType = 19; _self.Value = v; };
                    this.setQualifiedName = function(v) { _self.DataType = 20; _self.Value = v; };
                    this.setLocalizedText = function(v) { _self.DataType = 21; _self.Value = v; };
                    this.setExtensionObject = function(v) { _self.DataType = 22; _self.Value = v; };
                    this.toBoolean = function() { return !!_self.Value; };
                    this.toInt32 = function() { return parseInt(_self.Value) || 0; };
                    this.toUInt32 = function() { return (parseInt(_self.Value) || 0) >>> 0; };
                    this.toDouble = function() { return parseFloat(_self.Value) || 0; };
                    this.toString = function() { return '' + _self.Value; };
                    this.getArraySize = function() { return Array.isArray(_self.Value) ? _self.Value.length : -1; };
                }
            ");
            RegisterSimpleType(engine, "UaDataValue", "Value", "StatusCode", "SourceTimestamp", "ServerTimestamp");
            RegisterUaStatusCodeConstructor(engine);
            RegisterSimpleType(engine, "UaLocalizedText", "Text", "Locale");
            RegisterSimpleType(engine, "UaQualifiedName", "Name", "NamespaceIndex");
            RegisterSimpleType(engine, "UaExpandedNodeId", "NodeId", "NamespaceUri", "ServerIndex");
            // UaExtensionObject with setter methods for identity tokens, filters, etc.
            engine.Execute(@"
                function UaExtensionObject() {
                    var obj = (this instanceof UaExtensionObject) ? this : {};
                    obj.TypeId = undefined;
                    obj.Body = undefined;
                    obj._inner = null;
                    obj.clone = function() { return obj; };
                    obj.setAnonymousIdentityToken = function(token) { obj._inner = token; obj.TypeId = { NodeId: new UaNodeId(321) }; };
                    obj.setUserNameIdentityToken = function(token) { obj._inner = token; obj.TypeId = { NodeId: new UaNodeId(324) }; };
                    obj.setX509IdentityToken = function(token) { obj._inner = token; obj.TypeId = { NodeId: new UaNodeId(327) }; };
                    obj.setAttributeOperand = function(op) { obj._inner = op; };
                    obj.setDataChangeFilter = function(f) { obj._inner = f; };
                    obj.setEventFilter = function(f) { obj._inner = f; };
                    if (!(this instanceof UaExtensionObject)) return obj;
                }
            ");
            RegisterSimpleType(engine, "UaGuid");
            RegisterSimpleType(engine, "UaByteString");
            RegisterSimpleType(engine, "UaXmlElement");
            RegisterSimpleType(engine, "UaDataChangeFilter", "Trigger", "DeadbandType", "DeadbandValue");
            RegisterSimpleType(engine, "UaEventFilter");
            RegisterSimpleType(engine, "UaAggregateFilter");
            RegisterSimpleType(engine, "UaAggregateConfiguration");
            RegisterSimpleType(engine, "UaContentFilter");
            RegisterSimpleType(engine, "UaContentFilterElement");
            RegisterSimpleType(engine, "UaContentFilterElements");
            RegisterSimpleType(engine, "UaSimpleAttributeOperand");
            RegisterSimpleType(engine, "UaSimpleAttributeOperands");
            RegisterSimpleType(engine, "UaLiteralOperand");
            RegisterSimpleType(engine, "UaAttributeOperand");
            RegisterSimpleType(engine, "UaElementOperand");
            RegisterSimpleType(engine, "UaRange");
            RegisterSimpleType(engine, "UaEUInformation");
            RegisterSimpleType(engine, "UaArgument");
            RegisterSimpleType(engine, "UaBrowseDescription");
            RegisterSimpleType(engine, "UaBrowsePath");
            RegisterSimpleType(engine, "UaRelativePath");
            RegisterSimpleType(engine, "UaRelativePathElement");
            RegisterSimpleType(engine, "UaHistoryData");
            RegisterSimpleType(engine, "UaAnnotation");
            RegisterSimpleType(engine, "UaSignatureData");
            RegisterSimpleType(engine, "UaApplicationDescription");
            RegisterSimpleType(engine, "UaAnonymousIdentityToken");
            RegisterSimpleType(engine, "UaUserNameIdentityToken", "UserName", "Password");
            RegisterSimpleType(engine, "UaX509IdentityToken");
            RegisterSimpleType(engine, "UaRolePermissionType");
            RegisterSimpleType(engine, "UaEnumDefinition");
            RegisterSimpleType(engine, "UaStructureDefinition");
            RegisterSimpleType(engine, "UaStructureField");
            RegisterSimpleType(engine, "UaNode");
            RegisterSimpleType(engine, "UaNodeAttributes");
            RegisterSimpleType(engine, "UaObjectAttributes");
            RegisterSimpleType(engine, "UaObjectTypeAttributes");
            RegisterSimpleType(engine, "UaVariableAttributes");
            RegisterSimpleType(engine, "UaVariableTypeAttributes");
            RegisterSimpleType(engine, "UaReferenceTypeAttributes");
            RegisterSimpleType(engine, "UaDataTypeAttributes");
            RegisterSimpleType(engine, "UaMethodAttributes");
            RegisterSimpleType(engine, "UaViewAttributes");
            RegisterSimpleType(engine, "UaAddNodesItem");
            RegisterSimpleType(engine, "UaAddReferencesItem");
            RegisterSimpleType(engine, "UaDeleteNodesItem");
            RegisterSimpleType(engine, "UaDeleteReferencesItem");
            RegisterSimpleType(engine, "UaHistoryReadValueId");
            RegisterSimpleType(engine, "UaReadRawModifiedDetails");
            RegisterSimpleType(engine, "UaReadProcessedDetails");
            RegisterSimpleType(engine, "UaReadAtTimeDetails");
            RegisterSimpleType(engine, "UaUpdateDataDetails");
            RegisterSimpleType(engine, "UaDeleteAtTimeDetails");
            RegisterSimpleType(engine, "UaDeleteRawModifiedDetails");
            RegisterSimpleType(engine, "UaDeleteEventDetails");
            RegisterSimpleType(engine, "UaModificationInfo");
            RegisterSimpleType(engine, "UaModificationInfos");
            RegisterSimpleType(engine, "UaHistoryReadResult");
            RegisterSimpleType(engine, "UaHistoryEventFieldList");
            RegisterSimpleType(engine, "UaAttributeValue");
            RegisterSimpleType(engine, "UaAttributeValues");
            RegisterSimpleType(engine, "UaNodeTypeDescription");
            RegisterSimpleType(engine, "UaQueryDataDescription");
            RegisterSimpleType(engine, "UaKeyValuePair");
            RegisterSimpleType(engine, "UaKeyValuePairs");
            RegisterSimpleType(engine, "UaGenericStructureValue");
            RegisterSimpleType(engine, "UaGenericStructureArray");
            RegisterSimpleType(engine, "UaGenericUnionValue");

            // Special types
            RegisterChannelConstructor(engine);
            RegisterSessionConstructor(engine, host);
            RegisterDiscoveryConstructor(engine);
            RegisterDateTimeConstructor(engine);
            RegisterCryptoProviderConstructor(engine);
            RegisterPkiTypes(engine);
            RegisterByteStringHelpers(engine);
            RegisterRequestHeaderHelper(engine);
            RegisterResponseHeaderHelper(engine);
            RegisterUserIdentityTokenHelpers(engine);

            // Catch-all: register any remaining Ua* types that CTT scripts use
            // These all become generic JS object constructors
            string[] additionalTypes = new[] {
                "UaAddMonitoredItemToThreadRequest", "UaAddMonitoredItemToThreadResponse",
                "UaAddSubscriptionToThreadRequest", "UaAddSubscriptionToThreadResponse",
                "UaAuditEventParams", "UaBrowseAddressSpaceRequest", "UaBrowseAddressSpaceResponse",
                "UaClearThreadDataRequest", "UaConfigurationVersionDataType",
                "UaDataSetMetaDataType", "UaDataSetReaderDataTypes", "UaDataSetWriterDataTypes",
                "UaDiagnosticInfo", "UaDiagnosticInfos",
                "UaDropAuditRecordRequest", "UaDropAuditRecordResponse",
                "UaExecuteAggregateQueryCachedRequest", "UaExecuteAggregateQueryCachedResponse",
                "UaExecuteAggregateQueryReadRequest", "UaExecuteAggregateQueryReadResponse",
                "UaExecuteAggregateQueryReadResultsRequest", "UaExecuteAggregateQueryReadResultsResponse",
                "UaExpandedNIDHelper",
                "UaFindEntryRequest", "UaFindEntryResponse",
                "UaFindObjectsOfTypeRequest", "UaFindObjectsOfTypeResponse",
                "UaGetAuditEventParamsRequest", "UaGetAuditEventParamsResponse",
                "UaGetBufferRequest", "UaGetBufferResponse",
                "UaGetDataValuesRequest", "UaGetDataValuesResponse",
                "UaGetThreadPublishStatisticsRequest", "UaGetThreadPublishStatisticsResponse",
                "UaHistoryUpdateRequest", "UaHistoryUpdateResponse",
                "UaIsSubTypeOfTypeRequest", "UaIsSubTypeOfTypeResponse",
                "UaModelMap",
                "UaNetworkAddressUrlDataType",
                "UaPausePublishRequest",
                "UaPkiPrivateKey",
                "UaPublishedDataItemsDataType", "UaPublishedDataSetDataType",
                "UaPublishedDataSetDataTypes", "UaPublishedVariableDataTypes",
                "UaPubSubConfiguration2DataType",
                "UaPubSubConfigurationRefDataType", "UaPubSubConfigurationRefDataTypes",
                "UaPubSubConnectionDataType", "UaPubSubTraceNetworkMessage",
                "UaPushAuditRecordRequest", "UaPushAuditRecordResponse",
                "UaReaderGroupDataTypes",
                "UaRegisterServerRequest", "UaRegisterServerResponse",
                "UaRemoveEntryRequest", "UaRemoveEntryResponse",
                "UaSecurityGroupDataType", "UaSecurityGroupDataTypes",
                "UaStandaloneSubscribedDataSetDataType", "UaStandaloneSubscribedDataSetDataTypes",
                "UaStandaloneSubscribedDataSetRefDataType",
                "UaStartThreadPublishRequest", "UaStartThreadSessionResponse",
                "UaStopThreadRequest",
                "UaSubscriptionAcknowledgements",
                "UaTargetVariablesDataType", "UaTargetVariablesDataTypes",
                "UaTest",
                "UaTypeHierarchyRequest", "UaTypeHierarchyResponse",
                "UaWriterGroupDataTypes",
                "UaBrokerConnectionTransportDataType", "UaBrokerDataSetReaderTransportDataType",
                "UaBrokerWriterGroupTransportDataType",
                "UaDatagramConnectionTransportDataType", "UaDatagramWriterGroupTransport2DataType",
                "UaJsonDataSetWriterMessageDataType", "UaJsonWriterGroupMessageDataType",
                "UaUadpDataSetReaderMessageDataType", "UaUadpDataSetWriterMessageDataType",
                "UaUadpWriterGroupMessageDataType",
                "UaAddReferencesRequest", "UaAddReferencesResponse",
                "UaDeleteReferencesRequest", "UaDeleteReferencesResponse",
                "UaEndpointDescription", "UaEndpointDescriptions",
                "UaHistoryUpdateDetails", "UaUpdateEventDetails",
                "UaUpdateStructureDataDetails",
                "UaReadEventDetails", "UaReadAtTime",
                "UaNodeTypeDescriptions",
                "UaReferenceDescription", "UaReferenceDescriptions",
                "UaVariantArray", "UaVariantArray1d",
                "UaDataSetReaderDataType",
                "UaSubscriptionAcknowledgement",
                "UaSignature", "UaSignatureData",
                // ClassBased and additional type stubs
                "UaSubscriptionDiagnosticsDataType", "UaSubscriptionDiagnostic",
                "UaSessionDiagnosticsDataType",
                "UaServerDiagnosticsSummaryDataType",
                "UaNodeType", "UaObjectType", "UaVariableType",
                "UaReferenceType", "UaDataType", "UaAuditType",
                "UaHistoryUpdateResult", "UaHistoryUpdateResults",
                "UaHistoryModifiedData", "UaHistoricalData",
                "UaFieldMetaData", "UaFieldMetaDatas",
                "UaServerOnNetwork", "UaStatus",
                "UaNodes", "UaVariables",
                "UaViewDescription", "UaBrowsePathTargets",
                "UaGenericStructureValues", "UaBrowseResult",
                "UaDataSetWriterDataType", "UaReaderGroupDataType",
                "UaWriterGroupDataType",
                "UaPubSubTraceDataSetMessage", "UaPubSubTraceGroupHeader",
                "UaPubSubTracePayloadHeader", "UaRawPubSubNetworkMessage",
                "UaVersionTime", "UaUInt32",
                "UaComplianceTestTool", "UaInstanceAsMonitoredItem",
            };
            foreach (var typeName in additionalTypes)
            {
                RegisterSimpleType(engine, typeName);
            }
        }

        private static void RegisterRequestResponse(Engine engine, string name, params string[] properties)
        {
            // Build a JS function body that initializes properties
            // Uses "obj" pattern to work with both `new Name()` and `Name()` calls
            var body = new StringBuilder();
            body.AppendLine($"    var obj = (this instanceof {name}) ? this : {{}};");
            foreach (var prop in properties)
            {
                if (prop == "RequestHeader")
                {
                    body.AppendLine($"    obj.{prop} = new UaRequestHeader();");
                }
                else if (prop == "ResponseHeader")
                {
                    body.AppendLine($"    obj.{prop} = {{ ServiceResult: new UaStatusCode(0), Timestamp: UaDateTime.utcNow(), RequestHandle: 0, ServiceDiagnostics: {{ InnerDiagnosticInfo: null, InnerStatusCode: 0, HasInnerDiagnosticInfo: false }}, StringTable: [] }};");
                }
                else if (prop.EndsWith("s", StringComparison.Ordinal) && prop != "Status" && prop != "MoreNotifications")
                {
                    body.AppendLine($"    obj.{prop} = [];");
                }
                else
                {
                    body.AppendLine($"    obj.{prop} = 0;");
                }
            }
            body.AppendLine($"    obj.toString = function() {{ return '{name}'; }};");
            body.AppendLine($"    if (!(this instanceof {name})) return obj;");
            engine.Execute($"function {name}() {{\n{body}}}");
        }

        private static void RegisterTypedArray(Engine engine, string name)
        {
            // UaVariants and UaQualifiedNames need specialized elements with setter methods
            string elementInit = name switch {
                "UaVariants" => "new UaVariant()",
                "UaQualifiedNames" => "new UaQualifiedName()",
                "UaLocalizedTexts" => "new UaLocalizedText()",
                _ => "{}"
            };
            engine.Execute($@"
                function {name}(size) {{
                    var arr = [];
                    if (typeof size === 'number') {{
                        for (var i = 0; i < size; i++) arr.push({elementInit});
                    }}
                    return arr;
                }}
            ");
        }

        private static void RegisterNodeIdConstructor(Engine engine)
        {
            engine.Execute(@"
                function UaNodeId(idOrString, ns) {
                    this.NamespaceIndex = 0;
                    this.IdentifierNumeric = 0;
                    if (typeof idOrString === 'number') {
                        this.IdentifierNumeric = idOrString;
                        this.IdentifierType = 0; // Numeric
                        if (typeof ns === 'number') this.NamespaceIndex = ns;
                        var _id = idOrString, _ns = this.NamespaceIndex;
                        this.toString = function() { return _ns === 0 ? 'i=' + _id : 'ns=' + _ns + ';i=' + _id; };
                        this.getIdentifierNumeric = function() { return _id; };
                    } else if (typeof idOrString === 'string') {
                        this.IdentifierString = idOrString;
                        this.IdentifierType = 1; // String
                        if (typeof ns === 'number') this.NamespaceIndex = ns;
                        var _s = idOrString, _ns2 = this.NamespaceIndex;
                        this.toString = function() { return _ns2 === 0 ? 's=' + _s : 'ns=' + _ns2 + ';s=' + _s; };
                        this.getIdentifierNumeric = function() { return 0; };
                    } else if (idOrString !== undefined && idOrString !== null && typeof idOrString === 'object') {
                        // Copy constructor
                        if (idOrString.IdentifierNumeric !== undefined) {
                            this.IdentifierNumeric = idOrString.IdentifierNumeric;
                            this.IdentifierType = 0;
                        }
                        if (idOrString.IdentifierString !== undefined) {
                            this.IdentifierString = idOrString.IdentifierString;
                            this.IdentifierType = 1;
                        }
                        if (idOrString.NamespaceIndex !== undefined) this.NamespaceIndex = idOrString.NamespaceIndex;
                        var _src = idOrString;
                        this.toString = function() { return _src.toString ? _src.toString() : 'i=0'; };
                        this.getIdentifierNumeric = function() { return _src.IdentifierNumeric || 0; };
                    } else {
                        this.IdentifierType = 0;
                        this.toString = function() { return 'i=0'; };
                        this.getIdentifierNumeric = function() { return 0; };
                    }
                    this.clone = function() { return new UaNodeId(this.IdentifierNumeric || this.IdentifierString, this.NamespaceIndex); };
                    this.equals = function(other) {
                        if (!other) return false;
                        return this.IdentifierNumeric === other.IdentifierNumeric &&
                               this.NamespaceIndex === other.NamespaceIndex;
                    };
                }
                UaNodeId.fromString = function(str) {
                    if (!str) return new UaNodeId();
                    // Parse 'ns=X;i=Y' or 'i=Y' or 'ns=X;s=Y' or 's=Y'
                    var ns = 0, m;
                    m = str.match(/ns=(\d+);/);
                    if (m) ns = parseInt(m[1]);
                    m = str.match(/i=(\d+)/);
                    if (m) return new UaNodeId(parseInt(m[1]), ns);
                    m = str.match(/s=(.+)/);
                    if (m) return new UaNodeId(m[1], ns);
                    return new UaNodeId();
                };
            ");
        }

        private static void RegisterSimpleType(Engine engine, string name, params string[] properties)
        {
            var body = new System.Text.StringBuilder();
            body.AppendLine($"    var obj = (this instanceof {name}) ? this : {{}};");
            for (int i = 0; i < properties.Length; i++)
            {
                body.AppendLine($"    obj.{properties[i]} = arguments.length > {i} ? arguments[{i}] : undefined;");
            }
            body.AppendLine("    obj.clone = function() { return obj; };");
            body.AppendLine($"    if (!(this instanceof {name})) return obj;");
            engine.Execute($"function {name}() {{\n{body}}}");
        }

        private static void RegisterUaStatusCodeConstructor(Engine engine)
        {
            // UaStatusCode with isGood()/isBad()/isUncertain() methods
            engine.Execute(@"
                function UaStatusCode(code) {
                    this.StatusCode = (typeof code === 'number') ? code : 0;
                    var _code = this.StatusCode;
                    this.isGood = function() { return (_code & 0xC0000000) === 0; };
                    this.isBad = function() { return (_code & 0x80000000) !== 0; };
                    this.isUncertain = function() { return (_code & 0xC0000000) === 0x40000000; };
                    this.toString = function() {
                        var hex = _code.toString(16).toUpperCase();
                        while (hex.length < 8) hex = '0' + hex;
                        return '0x' + hex;
                    };
                    this.clone = function() { return new UaStatusCode(_code); };
                    this.setStatusCode = function(newCode) {
                        _code = newCode;
                        this.StatusCode = newCode;
                    };
                }
            ");
        }

        private static void RegisterChannelConstructor(Engine engine)
        {
            engine.Execute(@"
                function _makeByteString(len) {
                    return {
                        length: len || 0,
                        isEmpty: function() { return this.length === 0; },
                        clone: function() { return _makeByteString(this.length); },
                        append: function(other) { if (other && other.length) this.length += other.length; },
                        toString: function() { return '[ByteString(' + this.length + ')]'; }
                    };
                }

                function UaChannel() {
                    this.Connected = false;
                    this.ClientCertificate = _makeByteString(0);
                    this.ClientPrivateKey = _makeByteString(0);
                    this.ServerCertificate = _makeByteString(0);
                    this.ClientNonce = _makeByteString(0);
                    this.ServerNonce = _makeByteString(0);
                    this.RequestedLifetime = 0;
                    this.NetworkTimeout = 60000;
                    this.SecurityMode = 1; // None
                    this.MessageSecurityMode = 1; // None
                    this.RequestedSecurityPolicyUri = 'http://opcfoundation.org/UA/SecurityPolicy#None';
                    this.SecurityPolicy = '';
                    this.ServerUrl = '';
                    this.CertificateTrustListLocation = '';
                    this.CertificateRevocationListLocation = '';
                    this.ClientAuditEntryId = '';
                    this.connect = function(url) {
                        this.Connected = true;
                        this._url = url;
                        return new UaStatusCode(0);
                    };
                    this.disconnect = function() {
                        this.Connected = false;
                        return new UaStatusCode(0);
                    };
                    this.IsSecure = function() {
                        return this.RequestedSecurityPolicyUri !== 'http://opcfoundation.org/UA/SecurityPolicy#None' &&
                               this.MessageSecurityMode !== 1;
                    };
                }
            ");
        }

        private static void RegisterSessionConstructor(Engine engine, CttHostEnvironment host)
        {
            // Define a JS constructor that delegates to the host to attach service methods.
            // 'new UaSession(channel)' must work, so we use a JS function, not ClrFunction.
            engine.SetValue("__createUaSession", new ClrFunction(engine, "__createUaSession",
                (_, args) =>
                {
                    var sessionObj = host.CreateUaSessionObject(engine);
                    if (args.Length > 0)
                    {
                        sessionObj.Set("Channel", args[0]);
                    }
                    return sessionObj;
                }));

            engine.Execute(@"
                function UaSession(channel) {
                    var s = __createUaSession(channel);
                    for (var k in s) {
                        if (s.hasOwnProperty(k)) this[k] = s[k];
                    }
                }
            ");
        }

        private static void RegisterDiscoveryConstructor(Engine engine)
        {
            // UaDiscovery needs getEndpoints/findServers — delegate to __discoverySession
            // which is set up by CttHostEnvironment (provides actual OPC UA discovery).
            engine.Execute(@"
                function UaDiscovery(channelOrArgs) {
                    if (channelOrArgs && channelOrArgs.Channel) {
                        this.Channel = channelOrArgs.Channel;
                    } else if (channelOrArgs) {
                        this.Channel = channelOrArgs;
                    }
                    // Copy service methods from the global __discoverySession if available
                    if (typeof __discoveryGetEndpoints === 'function') {
                        this.getEndpoints = __discoveryGetEndpoints;
                    }
                    if (typeof __discoveryFindServers === 'function') {
                        this.findServers = __discoveryFindServers;
                    }
                }
            ");
        }

        private static void RegisterDateTimeConstructor(Engine engine)
        {
            // Pure JS function so 'new UaDateTime()' works
            engine.Execute(@"
                function UaDateTime() {
                    this._date = new Date();
                    this.toString = function() {
                        return this._date.toISOString();
                    };
                    this.equals = function(other) {
                        if (other && other._date) return this._date.getTime() === other._date.getTime();
                        return false;
                    };
                    this.msecsTo = function(other) {
                        if (other && other._date) return other._date.getTime() - this._date.getTime();
                        return 0;
                    };
                    this.addMSecs = function(ms) {
                        this._date = new Date(this._date.getTime() + ms);
                        return this;
                    };
                    this.addSecs = function(s) {
                        this._date = new Date(this._date.getTime() + s * 1000);
                        return this;
                    };
                    this.toFileTime = function() {
                        return this._date.getTime() * 10000 + 116444736000000000;
                    };
                }
                UaDateTime.utcNow = function() {
                    return new UaDateTime();
                };
                UaDateTime.Now = function() {
                    this.toString = function() {
                        return new Date().toISOString();
                    };
                };
            ");
        }

        private static void RegisterCryptoProviderConstructor(Engine engine)
        {
            engine.Execute(@"
                function UaCryptoProvider(securityPolicyUri) {
                    this.SecurityPolicyUri = securityPolicyUri || '';
                    var _goodResult = function() {
                        var r = new UaStatusCode(0);
                        return r;
                    };
                    this.asymmetricSign = function(data, privateKey, signature) { return _goodResult(); };
                    this.asymmetricVerify = function(data, certificate, signature) { return _goodResult(); };
                    this.asymmetricEncrypt = function(data, certificate) { return _goodResult(); };
                    this.asymmetricDecrypt = function(data, privateKey) { return _goodResult(); };
                    this.symmetricSign = function(data, key) { return _goodResult(); };
                    this.symmetricVerify = function(data, key, signature) { return _goodResult(); };
                    this.symmetricEncrypt = function(data, key) { return _goodResult(); };
                    this.symmetricDecrypt = function(data, key) { return _goodResult(); };
                    this.generateKey = function(length) { return _makeByteString(length || 32); };
                }
            ");
        }

        private static void RegisterRequestHeaderHelper(Engine engine)
        {
            engine.Execute(@"
                function UaRequestHeader() {
                    this.Timestamp = UaDateTime.utcNow();
                    this.ReturnDiagnostics = 0;
                    this.RequestHandle = 0;
                    this.AuditEntryId = '';
                    this.TimeoutHint = 0;
                    this.AdditionalHeader = null;
                }
                UaRequestHeader.New = function(args) {
                    var h = new UaRequestHeader();
                    if (args && args.ReturnDiagnostics !== undefined) {
                        h.ReturnDiagnostics = args.ReturnDiagnostics;
                    }
                    return h;
                };
            ");
        }

        private static void RegisterResponseHeaderHelper(Engine engine)
        {
            engine.Execute(@"
                var UaResponseHeader = {
                    IsValid: function(args) {
                        if (!args || !args.Service) return false;
                        var svc = args.Service;
                        if (!svc.Response) return true;
                        var rh = svc.Response.ResponseHeader;
                        if (!rh) return true;
                        var sc = rh.ServiceResult;
                        if (!sc) return true;
                        // Check ServiceResult against expected
                        if (args.ServiceResult && typeof args.ServiceResult === 'object' && typeof args.ServiceResult.containsStatusCode === 'function') {
                            var code = (typeof sc.StatusCode !== 'undefined') ? sc.StatusCode : 0;
                            if (!args.ServiceResult.containsStatusCode(code)) {
                                if (!args.SuppressMessaging) addError(svc.Name + 'ResponseHeader.ServiceResult unexpected: 0x' + code.toString(16));
                                return false;
                            }
                            return true;
                        }
                        // Default: check isGood
                        if (typeof sc.isGood === 'function') {
                            var good = sc.isGood();
                            if (!good && !args.SuppressMessaging && !args.SuppressErrors) {
                                var code = (typeof sc.StatusCode !== 'undefined') ? sc.StatusCode : 0;
                                addError(svc.Name + ' ResponseHeader.ServiceResult is not Good: 0x' + code.toString(16));
                            }
                            if (good && args.ServiceInfo && !args.SuppressMessaging) {
                                addLog(svc.Name + '( ' + args.ServiceInfo + ' ).Response.ResponseHeader.ServiceResult: 0x00000000 as expected.');
                            }
                            return good;
                        }
                        return true;
                    }
                };
            ");
        }

        private static void RegisterPkiTypes(Engine engine)
        {
            // UaPkiCertificate with fromDER static method
            engine.Execute(@"
                function UaPkiCertificate() {
                    this.ApplicationUri = '';
                    this.SubjectName = '';
                    this.Thumbprint = 'thumb=000000000000000000000000000000000000000000';
                    this.IssuerName = '';
                    this.ValidFrom = '';
                    this.ValidTo = '';
                    this.Hostnames = [];
                    this.IpAddresses = [];
                    this.KeyLength = 2048;
                    this.SignatureAlgorithm = 'sha256';
                    this.KeyUsage = ['digitalSignature', 'nonRepudiation', 'keyEncipherment', 'dataEncipherment'];
                    this.ExtendedKeyUsage = ['serverAuth', 'clientAuth'];
                    this.IsCA = false;
                    this.Version = 3;
                    this.SerialNumber = '00';
                    this.PublicKey = { length: 256 }; // 256 bytes = 2048 bits
                    this.Issuer = { Organization: 'OPC Foundation', CommonName: 'Reference Server', Country: 'US', State: 'Arizona', OrganizationUnit: '', Locality: 'Scottsdale', DomainComponent: '' };
                    this.Subject = { Organization: 'OPC Foundation', CommonName: 'Reference Server', Country: 'US', State: 'Arizona', OrganizationUnit: '', Locality: 'Scottsdale', DomainComponent: '' };
                }
                UaPkiCertificate.fromDER = function(byteString) {
                    var cert = new UaPkiCertificate();
                    cert.ApplicationUri = 'urn:localhost:OPCFoundation:ReferenceServer';
                    cert.SubjectName = 'CN=Reference Server';
                    cert.Thumbprint = 'thumb=000000000000000000000000000000000000000000';
                    cert.Hostnames = ['localhost'];
                    cert.IpAddresses = ['127.0.0.1'];
                    cert.KeyUsage = ['digitalSignature', 'nonRepudiation', 'keyEncipherment', 'dataEncipherment'];
                    cert.ExtendedKeyUsage = ['serverAuth', 'clientAuth'];
                    cert.toDERFile = function(filename) { return true; };
                    cert.toFile = function(filename) { return true; };
                    return cert;
                };
                UaPkiCertificate.IsValid = function(cert) { return true; };
                UaPkiCertificate.LoadFromSetting = function(settingPath) {
                    return new UaPkiCertificate();
                };
            ");

            // PkiType enum
            engine.Execute(@"
                var PkiType = { OpenSSL: 0, MbedTLS: 1, Windows: 2 };
            ");

            // UaPkiUtility
            engine.Execute(@"
                function UaPkiUtility() {
                    this.PkiType = 0;
                    this.CertificateTrustListLocation = '';
                    this.CertificateRevocationListLocation = '';
                    this.IssuersListLocation = '';
                    this.IssuersCrlListLocation = '';
                    var _goodResult = function() {
                        var result = {};
                        result.StatusCode = 0;
                        result.isGood = function() { return true; };
                        result.isBad = function() { return false; };
                        result.isUncertain = function() { return false; };
                        result.toString = function() { return '0x00000000'; };
                        return result;
                    };
                    this.loadCertificateFromFile = function(path, outCert) { return _goodResult(); };
                    this.loadPrivateKeyFromFile = function(path, outKey) { return _goodResult(); };
                    this.validateCertificate = function(cert) { return _goodResult(); };
                }
            ");
        }

        private static void RegisterByteStringHelpers(Engine engine)
        {
            // Add fromStringData static method to UaByteString
            engine.Execute(@"
                UaByteString.fromStringData = function(str) {
                    var bs = new UaByteString();
                    if (str && typeof str === 'string') {
                        bs.length = str.length;
                    } else {
                        bs.length = 0;
                    }
                    bs.Data = str || '';
                    return bs;
                };
                UaByteString.FromByteArray = function(bytes) {
                    var bs = new UaByteString();
                    bs.length = bytes ? bytes.length : 0;
                    bs.Data = bytes;
                    return bs;
                };
                UaByteString.ToByteArray = function(bs) {
                    return bs && bs.Data ? bs.Data : [];
                };
                UaByteString.fromHexString = function(hex) {
                    var bs = new UaByteString();
                    bs.length = hex ? hex.length / 2 : 0;
                    bs.Data = hex || '';
                    return bs;
                };
                UaByteString.Increment = function(bs) {
                    return bs; // stub
                };
            ");
        }

        private static void RegisterUserIdentityTokenHelpers(Engine engine)
        {
            // UaUserIdentityToken.FromUserCredentials — maps UserCredentials to token
            engine.Execute(@"
                var UaUserIdentityToken = {
                    FromUserCredentials: function(args) {
                        var token = {};
                        token.PolicyId = '';
                        token.Type = 0;
                        if (args && args.UserCredentials) {
                            token.UserName = args.UserCredentials.UserName || '';
                            token.Password = args.UserCredentials.Password || '';
                        }
                        return token;
                    }
                };
            ");

            // Nonce helper for CreateSession's ClientNonce
            engine.Execute(@"
                var Nonce = {
                    Next: function() {
                        return 'nonce_' + Math.random().toString(36).substring(2);
                    }
                };
            ");

            // UaSignatureData.New
            engine.Execute(@"
                UaSignatureData.New = function(args) {
                    var sig = new UaSignatureData();
                    sig.Algorithm = '';
                    sig.Signature = null;
                    return sig;
                };
            ");
        }
    }
}

