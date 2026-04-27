/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Reflection;
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
            RegisterRequestResponse(engine, "UaActivateSessionRequest", "ClientSignature", "ClientSoftwareCertificates", "LocaleIds", "UserIdentityToken", "UserTokenSignature", "RequestHeader");
            RegisterRequestResponse(engine, "UaActivateSessionResponse", "ServerNonce", "Results", "DiagnosticInfos", "ResponseHeader");
            RegisterRequestResponse(engine, "UaCloseSessionRequest", "DeleteSubscriptions", "RequestHeader");
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
            RegisterSimpleType(engine, "UaVariant", "DataType", "Value");
            RegisterSimpleType(engine, "UaDataValue", "Value", "StatusCode", "SourceTimestamp", "ServerTimestamp");
            RegisterSimpleType(engine, "UaStatusCode", "StatusCode");
            RegisterSimpleType(engine, "UaLocalizedText", "Text", "Locale");
            RegisterSimpleType(engine, "UaQualifiedName", "Name", "NamespaceIndex");
            RegisterSimpleType(engine, "UaExpandedNodeId", "NodeId", "NamespaceUri", "ServerIndex");
            RegisterSimpleType(engine, "UaExtensionObject", "TypeId", "Body");
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
            RegisterRequestHeaderHelper(engine);
            RegisterResponseHeaderHelper(engine);
        }

        private static void RegisterRequestResponse(Engine engine, string name, params string[] properties)
        {
            engine.SetValue(name, new ClrFunction(engine, name, (thisObj, args) =>
            {
                var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                foreach (var prop in properties)
                {
                    if (prop == "RequestHeader")
                    {
                        var header = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                        header.Set("Timestamp", JsValue.FromObject(engine, DateTime.UtcNow.ToString("o")));
                        header.Set("ReturnDiagnostics", JsValue.FromObject(engine, 0));
                        obj.Set(prop, header);
                    }
                    else if (prop.EndsWith("s", StringComparison.Ordinal) && prop != "Status")
                    {
                        obj.Set(prop, engine.Intrinsics.Array.Construct(Array.Empty<JsValue>()));
                    }
                    else
                    {
                        obj.Set(prop, JsValue.FromObject(engine, 0));
                    }
                }
                return obj;
            }));
        }

        private static void RegisterTypedArray(Engine engine, string name)
        {
            engine.SetValue(name, new ClrFunction(engine, name, (thisObj, args) =>
            {
                if (args.Length > 0 && args[0].IsNumber())
                {
                    int size = (int)args[0].AsNumber();
                    var items = new JsValue[size];
                    for (int i = 0; i < size; i++) items[i] = JsValue.Undefined;
                    return engine.Intrinsics.Array.Construct(items);
                }
                return engine.Intrinsics.Array.Construct(Array.Empty<JsValue>());
            }));
        }

        private static void RegisterNodeIdConstructor(Engine engine)
        {
            engine.SetValue("UaNodeId", new ClrFunction(engine, "UaNodeId", (thisObj, args) =>
            {
                var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                if (args.Length >= 1)
                {
                    if (args[0].IsNumber())
                    {
                        uint id = (uint)args[0].AsNumber();
                        ushort ns = args.Length >= 2 ? (ushort)args[1].AsNumber() : (ushort)0;
                        obj.Set("IdentifierNumeric", JsValue.FromObject(engine, (double)id));
                        obj.Set("NamespaceIndex", JsValue.FromObject(engine, (double)ns));
                        obj.Set("toString", new ClrFunction(engine, "toString",
                            (_, _) => JsValue.FromObject(engine, ns == 0 ? $"i={id}" : $"ns={ns};i={id}")));
                    }
                    else if (args[0].IsString())
                    {
                        string s = args[0].AsString();
                        obj.Set("IdentifierString", JsValue.FromObject(engine, s));
                        ushort ns = args.Length >= 2 ? (ushort)args[1].AsNumber() : (ushort)0;
                        obj.Set("NamespaceIndex", JsValue.FromObject(engine, (double)ns));
                        obj.Set("toString", new ClrFunction(engine, "toString",
                            (_, _) => JsValue.FromObject(engine, ns == 0 ? $"s={s}" : $"ns={ns};s={s}")));
                    }
                }
                else
                {
                    obj.Set("IdentifierNumeric", JsValue.FromObject(engine, 0));
                    obj.Set("NamespaceIndex", JsValue.FromObject(engine, 0));
                    obj.Set("toString", new ClrFunction(engine, "toString",
                        (_, _) => JsValue.FromObject(engine, "i=0")));
                }
                return obj;
            }));
        }

        private static void RegisterSimpleType(Engine engine, string name, params string[] properties)
        {
            engine.SetValue(name, new ClrFunction(engine, name, (thisObj, args) =>
            {
                var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                for (int i = 0; i < properties.Length; i++)
                {
                    JsValue val = i < args.Length ? args[i] : JsValue.Undefined;
                    obj.Set(properties[i], val);
                }
                obj.Set("clone", new ClrFunction(engine, "clone",
                    (_, _) => obj));
                return obj;
            }));
        }

        private static void RegisterChannelConstructor(Engine engine)
        {
            engine.SetValue("UaChannel", new ClrFunction(engine, "UaChannel", (_, _) =>
            {
                var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                obj.Set("Connected", JsValue.False);
                return obj;
            }));
        }

        private static void RegisterSessionConstructor(Engine engine, CttHostEnvironment host)
        {
            engine.SetValue("UaSession", new ClrFunction(engine, "UaSession", (_, args) =>
            {
                // UaSession takes a channel argument
                var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                if (args.Length > 0)
                {
                    obj.Set("Channel", args[0]);
                }
                return obj;
            }));
        }

        private static void RegisterDiscoveryConstructor(Engine engine)
        {
            engine.SetValue("UaDiscovery", new ClrFunction(engine, "UaDiscovery", (_, _) =>
            {
                var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                return obj;
            }));
        }

        private static void RegisterDateTimeConstructor(Engine engine)
        {
            engine.SetValue("UaDateTime", new ClrFunction(engine, "UaDateTime", (_, _) =>
            {
                var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                obj.Set("toString", new ClrFunction(engine, "toString",
                    (_, _) => JsValue.FromObject(engine, DateTime.UtcNow.ToString("o"))));
                return obj;
            }));

            // UaDateTime.utcNow() static method
            engine.Execute(@"
                UaDateTime.utcNow = function() {
                    var dt = new UaDateTime();
                    return dt;
                };
            ");
        }

        private static void RegisterCryptoProviderConstructor(Engine engine)
        {
            engine.SetValue("UaCryptoProvider", new ClrFunction(engine, "UaCryptoProvider", (_, _) =>
                (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>())));
        }

        private static void RegisterRequestHeaderHelper(Engine engine)
        {
            engine.Execute(@"
                var UaRequestHeader = {
                    New: function(args) {
                        var h = {};
                        h.Timestamp = UaDateTime.utcNow();
                        h.ReturnDiagnostics = 0;
                        if (args && args.ReturnDiagnostics !== undefined) {
                            h.ReturnDiagnostics = args.ReturnDiagnostics;
                        }
                        return h;
                    }
                };
            ");
        }

        private static void RegisterResponseHeaderHelper(Engine engine)
        {
            engine.Execute(@"
                var UaResponseHeader = {
                    IsValid: function(args) {
                        if (!args || !args.Service || !args.Service.Response) return false;
                        var rh = args.Service.Response.ResponseHeader;
                        if (!rh) return false;
                        var sc = rh.ServiceResult;
                        if (!sc) return true;
                        if (typeof sc.isGood === 'function') return sc.isGood();
                        return true;
                    }
                };
            ");
        }
    }
}

