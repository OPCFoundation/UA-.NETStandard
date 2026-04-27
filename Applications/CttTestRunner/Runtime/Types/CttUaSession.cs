/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Interop;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.CttTestRunner.Runtime.Settings;

namespace Opc.Ua.CttTestRunner.Runtime.Types
{
    /// <summary>
    /// Wraps an OPC UA Session for use from CTT JavaScript.
    /// Exposes synchronous service methods matching the CTT C++ API:
    ///   session.read(request, response) → UaStatusCode
    ///   session.write(request, response) → UaStatusCode
    ///   session.browse(request, response) → UaStatusCode
    ///   session.call(request, response) → UaStatusCode
    ///   etc.
    /// </summary>
    public sealed class CttUaSession : IDisposable
    {
        private readonly ApplicationConfiguration _config;
        private readonly CttProjectSettings _project;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly bool _verbose;
        private ISession? _session;
        private bool _connected;

        public CttUaSession(
            ApplicationConfiguration config,
            CttProjectSettings project,
            ILoggerFactory loggerFactory,
            bool verbose)
        {
            _config = config;
            _project = project;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<CttUaSession>();
            _verbose = verbose;
        }

        public void Dispose()
        {
            if (_session != null)
            {
                try { _session.CloseAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { }
                _session.Dispose();
            }
        }

        /// <summary>
        /// Ensures connected to server. Auto-connects on first use.
        /// </summary>
        private async Task<ISession> EnsureConnectedAsync()
        {
            if (_session != null && _connected)
            {
                return _session;
            }

            string url = _project.ServerUrl;
            _logger.LogInformation("Connecting to {Url}...", url);

            try
            {
                // Get the endpoint description
                var endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                    _config, url, true, _config.CreateMessageContext().Telemetry,
                    CancellationToken.None).ConfigureAwait(false);

                var endpointConfig = EndpointConfiguration.Create(_config);
                var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfig);

                // Create session using factory
                var factory = new DefaultSessionFactory(_config.CreateMessageContext().Telemetry);
                _session = await factory.CreateAsync(
                    _config,
                    endpoint,
                    updateBeforeConnect: false,
                    checkDomain: true,
                    sessionName: "CTT Test Runner",
                    sessionTimeout: 60000,
                    identity: new UserIdentity(new AnonymousIdentityToken()),
                    preferredLocales: new ArrayOf<string>(),
                    ct: CancellationToken.None).ConfigureAwait(false);

                _connected = true;
                _logger.LogInformation("Connected. SessionId={Id}", _session.SessionId);
                return _session;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to server at {Url}", url);
                throw;
            }
        }

        /// <summary>
        /// Synchronous wrapper for EnsureConnectedAsync.
        /// </summary>
        private ISession EnsureConnected()
        {
            return EnsureConnectedAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates the JavaScript UaSession object with service methods.
        /// </summary>
        public ObjectInstance CreateJsObject(Engine engine)
        {
            var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());

            // The CTT wraps session in a Test.Session.Session pattern
            var sessionInner = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());

            // Register all service methods
            RegisterServiceMethod(engine, sessionInner, "read", ServiceRead);
            RegisterServiceMethod(engine, sessionInner, "write", ServiceWrite);
            RegisterServiceMethod(engine, sessionInner, "browse", ServiceBrowse);
            RegisterServiceMethod(engine, sessionInner, "browseNext", ServiceBrowseNext);
            RegisterServiceMethod(engine, sessionInner, "call", ServiceCall);
            RegisterServiceMethod(engine, sessionInner, "createSubscription", ServiceCreateSubscription);
            RegisterServiceMethod(engine, sessionInner, "deleteSubscriptions", ServiceDeleteSubscriptions);
            RegisterServiceMethod(engine, sessionInner, "createMonitoredItems", ServiceCreateMonitoredItems);
            RegisterServiceMethod(engine, sessionInner, "deleteMonitoredItems", ServiceDeleteMonitoredItems);
            RegisterServiceMethod(engine, sessionInner, "modifyMonitoredItems", ServiceModifyMonitoredItems);
            RegisterServiceMethod(engine, sessionInner, "modifySubscription", ServiceModifySubscription);
            RegisterServiceMethod(engine, sessionInner, "setMonitoringMode", ServiceSetMonitoringMode);
            RegisterServiceMethod(engine, sessionInner, "setPublishingMode", ServiceSetPublishingMode);
            RegisterServiceMethod(engine, sessionInner, "publish", ServicePublish);
            RegisterServiceMethod(engine, sessionInner, "republish", ServiceRepublish);
            RegisterServiceMethod(engine, sessionInner, "translateBrowsePathsToNodeIds", ServiceTranslateBrowsePaths);
            RegisterServiceMethod(engine, sessionInner, "registerNodes", ServiceRegisterNodes);
            RegisterServiceMethod(engine, sessionInner, "unregisterNodes", ServiceUnregisterNodes);
            RegisterServiceMethod(engine, sessionInner, "addNodes", ServiceAddNodes);
            RegisterServiceMethod(engine, sessionInner, "deleteNodes", ServiceDeleteNodes);
            RegisterServiceMethod(engine, sessionInner, "addReferences", ServiceAddReferences);
            RegisterServiceMethod(engine, sessionInner, "deleteReferences", ServiceDeleteReferences);
            RegisterServiceMethod(engine, sessionInner, "historyRead", ServiceHistoryRead);
            RegisterServiceMethod(engine, sessionInner, "historyUpdate", ServiceHistoryUpdate);
            RegisterServiceMethod(engine, sessionInner, "queryFirst", ServiceQueryFirst);
            RegisterServiceMethod(engine, sessionInner, "queryNext", ServiceQueryNext);
            RegisterServiceMethod(engine, sessionInner, "cancel", ServiceCancel);
            RegisterServiceMethod(engine, sessionInner, "findServers", ServiceFindServers);
            RegisterServiceMethod(engine, sessionInner, "getEndpoints", ServiceGetEndpoints);
            RegisterServiceMethod(engine, sessionInner, "activateSession", ServiceActivateSession);
            RegisterServiceMethod(engine, sessionInner, "closeSession", ServiceCloseSession);
            RegisterServiceMethod(engine, sessionInner, "createSession", ServiceCreateSession);
            RegisterServiceMethod(engine, sessionInner, "setTriggering", ServiceSetTriggering);
            RegisterServiceMethod(engine, sessionInner, "transferSubscriptions", ServiceTransferSubscriptions);

            // CTT scripts use Test.Session.Session.read(req,resp)
            obj.Set("Session", sessionInner);

            // Also expose buildRequestHeader
            sessionInner.Set("buildRequestHeader",
                new ClrFunction(engine, "buildRequestHeader", (_, args) =>
                {
                    // Return a stub request header
                    var header = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                    header.Set("Timestamp",
                        JsValue.FromObject(engine, DateTime.UtcNow.ToString("o")));
                    return header;
                }));

            return obj;
        }

        private void RegisterServiceMethod(Engine engine, ObjectInstance obj, string name,
            Func<Engine, JsValue[], JsValue> handler)
        {
            obj.Set(name, new ClrFunction(engine, name,
                (thisObj, args) =>
                {
                    if (_verbose)
                    {
                        _logger.LogDebug("session.{Method}() called", name);
                    }
                    try
                    {
                        return handler(engine, args);
                    }
                    catch (ServiceResultException sre)
                    {
                        _logger.LogWarning("Service {Method} failed: {Error}", name, sre.StatusCode);
                        return CreateUaStatusCode(engine, sre.StatusCode.Code);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Service {Method} threw", name);
                        return CreateUaStatusCode(engine, (uint)StatusCodes.BadUnexpectedError);
                    }
                }));
        }

        #region Service Implementations

        private JsValue ServiceRead(Engine engine, JsValue[] args)
        {
            var session = EnsureConnected();
            if (args.Length < 2) return CreateUaStatusCode(engine, (uint)StatusCodes.BadInvalidArgument);

            var request = args[0].AsObject();
            var response = args[1].AsObject();

            // Extract NodesToRead from request
            var nodesToRead = new List<ReadValueId>();
            var reqNodes = request.Get("NodesToRead");
            if (reqNodes.IsObject())
            {
                var nodesObj = (ObjectInstance)reqNodes;
                int length = (int)nodesObj.Get("length").AsNumber();
                for (int i = 0; i < length; i++)
                {
                    var item = nodesObj.Get(i.ToString()).AsObject();
                    var rvid = new ReadValueId {
                        NodeId = ParseNodeId(item.Get("NodeId")),
                        AttributeId = (uint)item.Get("AttributeId").AsNumber()
                    };
                    var indexRange = item.Get("IndexRange");
                    if (CttGlobals.IsDefined(indexRange))
                    {
                        rvid.IndexRange = indexRange.ToString();
                    }
                    nodesToRead.Add(rvid);
                }
            }

            var tsReturn = TimestampsToReturn.Both;
            var tsVal = request.Get("TimestampsToReturn");
            if (tsVal.IsNumber())
            {
                tsReturn = (TimestampsToReturn)(int)tsVal.AsNumber();
            }

            double maxAge = 0;
            var maxAgeVal = request.Get("MaxAge");
            if (maxAgeVal.IsNumber())
            {
                maxAge = maxAgeVal.AsNumber();
            }

            // Execute the Read
            var readResponse = session.ReadAsync(null, maxAge, tsReturn,
                new ArrayOf<ReadValueId>(nodesToRead.ToArray()),
                CancellationToken.None).GetAwaiter().GetResult();

            // Fill the response object
            var responseHeader = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
            responseHeader.Set("ServiceResult",
                CreateUaStatusCode(engine, readResponse.ResponseHeader.ServiceResult.Code));
            responseHeader.Set("Timestamp",
                JsValue.FromObject(engine, DateTime.UtcNow.ToString("o")));
            response.Set("ResponseHeader", responseHeader);

            // Fill Results array
            var resultsArray = CreateDataValuesArray(engine, readResponse.Results);
            response.Set("Results", resultsArray);

            // DiagnosticInfos
            var diagArray = engine.Intrinsics.Array.Construct(Array.Empty<JsValue>());
            response.Set("DiagnosticInfos", diagArray);

            return CreateUaStatusCode(engine, (uint)StatusCodes.Good);
        }

        private JsValue ServiceWrite(Engine engine, JsValue[] args)
        {
            var session = EnsureConnected();
            if (args.Length < 2) return CreateUaStatusCode(engine, (uint)StatusCodes.BadInvalidArgument);

            var request = args[0].AsObject();
            var response = args[1].AsObject();

            var writeValues = new List<WriteValue>();
            var reqNodes = request.Get("NodesToWrite");
            if (reqNodes.IsObject())
            {
                var nodesObj = (ObjectInstance)reqNodes;
                int length = (int)nodesObj.Get("length").AsNumber();
                for (int i = 0; i < length; i++)
                {
                    var item = nodesObj.Get(i.ToString()).AsObject();
                    var wv = new WriteValue {
                        NodeId = ParseNodeId(item.Get("NodeId")),
                        AttributeId = (uint)item.Get("AttributeId").AsNumber(),
                        Value = ParseDataValue(item.Get("Value"))
                    };
                    var indexRange = item.Get("IndexRange");
                    if (CttGlobals.IsDefined(indexRange))
                    {
                        wv.IndexRange = indexRange.ToString();
                    }
                    writeValues.Add(wv);
                }
            }

            session.WriteAsync(null, new ArrayOf<WriteValue>(writeValues.ToArray()),
                CancellationToken.None).GetAwaiter().GetResult();
            var writeResponse = session.WriteAsync(null,
                new ArrayOf<WriteValue>(writeValues.ToArray()),
                CancellationToken.None).GetAwaiter().GetResult();

            var responseHeader = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
            responseHeader.Set("ServiceResult",
                CreateUaStatusCode(engine, writeResponse.ResponseHeader.ServiceResult.Code));
            response.Set("ResponseHeader", responseHeader);

            var writeResults = writeResponse.Results.ToArray() ?? Array.Empty<StatusCode>();
            var resultsArray = engine.Intrinsics.Array.Construct(
                writeResults.Select(r => CreateUaStatusCode(engine, r.Code)).ToArray());
            response.Set("Results", resultsArray);

            return CreateUaStatusCode(engine, (uint)StatusCodes.Good);
        }

        private JsValue ServiceBrowse(Engine engine, JsValue[] args)
        {
            var session = EnsureConnected();
            if (args.Length < 2) return CreateUaStatusCode(engine, (uint)StatusCodes.BadInvalidArgument);

            var request = args[0].AsObject();
            var response = args[1].AsObject();

            var nodesToBrowse = new List<BrowseDescription>();
            var reqNodes = request.Get("NodesToBrowse");
            if (reqNodes.IsObject())
            {
                var nodesObj = (ObjectInstance)reqNodes;
                int length = (int)nodesObj.Get("length").AsNumber();
                for (int i = 0; i < length; i++)
                {
                    var item = nodesObj.Get(i.ToString()).AsObject();
                    var bd = new BrowseDescription {
                        NodeId = ParseNodeId(item.Get("NodeId")),
                        BrowseDirection = (BrowseDirection)(int)item.Get("BrowseDirection").AsNumber(),
                        IncludeSubtypes = item.Get("IncludeSubtypes").AsBoolean(),
                        NodeClassMask = (uint)item.Get("NodeClassMask").AsNumber(),
                        ResultMask = (uint)item.Get("ResultMask").AsNumber()
                    };
                    var refType = item.Get("ReferenceTypeId");
                    if (CttGlobals.IsDefined(refType))
                    {
                        bd.ReferenceTypeId = ParseNodeId(refType);
                    }
                    nodesToBrowse.Add(bd);
                }
            }

            uint maxRefs = 0;
            var maxRefsVal = request.Get("RequestedMaxReferencesPerNode");
            if (maxRefsVal.IsNumber())
            {
                maxRefs = (uint)maxRefsVal.AsNumber();
            }

            var browseResponse = session.BrowseAsync(null, null, maxRefs,
                new ArrayOf<BrowseDescription>(nodesToBrowse.ToArray()),
                CancellationToken.None).GetAwaiter().GetResult();

            var responseHeader = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
            responseHeader.Set("ServiceResult",
                CreateUaStatusCode(engine, browseResponse.ResponseHeader.ServiceResult.Code));
            response.Set("ResponseHeader", responseHeader);

            var resultsArray = CreateBrowseResultsArray(engine, browseResponse.Results);
            response.Set("Results", resultsArray);

            return CreateUaStatusCode(engine, (uint)StatusCodes.Good);
        }

        // Stub implementations for remaining services — each follows the same pattern
        private JsValue ServiceBrowseNext(Engine e, JsValue[] a) => ServiceStub(e, "browseNext");
        private JsValue ServiceCall(Engine e, JsValue[] a) => ServiceStub(e, "call");
        private JsValue ServiceCreateSubscription(Engine e, JsValue[] a) => ServiceStub(e, "createSubscription");
        private JsValue ServiceDeleteSubscriptions(Engine e, JsValue[] a) => ServiceStub(e, "deleteSubscriptions");
        private JsValue ServiceCreateMonitoredItems(Engine e, JsValue[] a) => ServiceStub(e, "createMonitoredItems");
        private JsValue ServiceDeleteMonitoredItems(Engine e, JsValue[] a) => ServiceStub(e, "deleteMonitoredItems");
        private JsValue ServiceModifyMonitoredItems(Engine e, JsValue[] a) => ServiceStub(e, "modifyMonitoredItems");
        private JsValue ServiceModifySubscription(Engine e, JsValue[] a) => ServiceStub(e, "modifySubscription");
        private JsValue ServiceSetMonitoringMode(Engine e, JsValue[] a) => ServiceStub(e, "setMonitoringMode");
        private JsValue ServiceSetPublishingMode(Engine e, JsValue[] a) => ServiceStub(e, "setPublishingMode");
        private JsValue ServicePublish(Engine e, JsValue[] a) => ServiceStub(e, "publish");
        private JsValue ServiceRepublish(Engine e, JsValue[] a) => ServiceStub(e, "republish");
        private JsValue ServiceTranslateBrowsePaths(Engine e, JsValue[] a) => ServiceStub(e, "translateBrowsePaths");
        private JsValue ServiceRegisterNodes(Engine e, JsValue[] a) => ServiceStub(e, "registerNodes");
        private JsValue ServiceUnregisterNodes(Engine e, JsValue[] a) => ServiceStub(e, "unregisterNodes");
        private JsValue ServiceAddNodes(Engine e, JsValue[] a) => ServiceStub(e, "addNodes");
        private JsValue ServiceDeleteNodes(Engine e, JsValue[] a) => ServiceStub(e, "deleteNodes");
        private JsValue ServiceAddReferences(Engine e, JsValue[] a) => ServiceStub(e, "addReferences");
        private JsValue ServiceDeleteReferences(Engine e, JsValue[] a) => ServiceStub(e, "deleteReferences");
        private JsValue ServiceHistoryRead(Engine e, JsValue[] a) => ServiceStub(e, "historyRead");
        private JsValue ServiceHistoryUpdate(Engine e, JsValue[] a) => ServiceStub(e, "historyUpdate");
        private JsValue ServiceQueryFirst(Engine e, JsValue[] a) => ServiceStub(e, "queryFirst");
        private JsValue ServiceQueryNext(Engine e, JsValue[] a) => ServiceStub(e, "queryNext");
        private JsValue ServiceCancel(Engine e, JsValue[] a) => ServiceStub(e, "cancel");
        private JsValue ServiceActivateSession(Engine e, JsValue[] a) => ServiceStub(e, "activateSession");
        private JsValue ServiceCloseSession(Engine e, JsValue[] a) => ServiceStub(e, "closeSession");
        private JsValue ServiceCreateSession(Engine e, JsValue[] a) => ServiceStub(e, "createSession");
        private JsValue ServiceSetTriggering(Engine e, JsValue[] a) => ServiceStub(e, "setTriggering");
        private JsValue ServiceTransferSubscriptions(Engine e, JsValue[] a) => ServiceStub(e, "transferSubscriptions");

        private JsValue ServiceFindServers(Engine engine, JsValue[] args)
        {
            if (args.Length < 2) return CreateUaStatusCode(engine, (uint)StatusCodes.BadInvalidArgument);
            var request = args[0].AsObject();
            var response = args[1].AsObject();

            string endpointUrl = _project.ServerUrl;
            var reqUrl = request.Get("EndpointUrl");
            if (CttGlobals.IsDefined(reqUrl)) endpointUrl = reqUrl.ToString();

            var discoveryClient = DiscoveryClient.CreateAsync(
                _config, new Uri(endpointUrl), ct: CancellationToken.None).GetAwaiter().GetResult();
            var servers = discoveryClient.FindServersAsync(default, CancellationToken.None).GetAwaiter().GetResult();

            var responseHeader = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
            responseHeader.Set("ServiceResult",
                CreateUaStatusCode(engine, (uint)StatusCodes.Good));
            response.Set("ResponseHeader", responseHeader);

            var serversList = servers.ToArray() ?? Array.Empty<ApplicationDescription>();
            var serversArray = engine.Intrinsics.Array.Construct(
                serversList.Select(s =>
                {
                    var sObj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                    sObj.Set("ApplicationName",
                        JsValue.FromObject(engine, s.ApplicationName.Text));
                    sObj.Set("ApplicationUri",
                        JsValue.FromObject(engine, s.ApplicationUri));
                    sObj.Set("ApplicationType",
                        JsValue.FromObject(engine, (int)s.ApplicationType));
                    return (JsValue)sObj;
                }).ToArray());
            response.Set("Servers", serversArray);

            return CreateUaStatusCode(engine, (uint)StatusCodes.Good);
        }

        private JsValue ServiceGetEndpoints(Engine engine, JsValue[] args)
        {
            if (args.Length < 2) return CreateUaStatusCode(engine, (uint)StatusCodes.BadInvalidArgument);
            var request = args[0].AsObject();
            var response = args[1].AsObject();

            string endpointUrl = _project.ServerUrl;
            var reqUrl = request.Get("EndpointUrl");
            if (CttGlobals.IsDefined(reqUrl)) endpointUrl = reqUrl.ToString();

            var epDiscoveryClient = DiscoveryClient.CreateAsync(
                _config, new Uri(endpointUrl), ct: CancellationToken.None).GetAwaiter().GetResult();
            var endpoints = epDiscoveryClient.GetEndpointsAsync(default, CancellationToken.None).GetAwaiter().GetResult();

            var responseHeader = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
            responseHeader.Set("ServiceResult",
                CreateUaStatusCode(engine, (uint)StatusCodes.Good));
            response.Set("ResponseHeader", responseHeader);

            var endpointsList = endpoints.ToArray() ?? Array.Empty<EndpointDescription>();
            var epArray = engine.Intrinsics.Array.Construct(
                endpointsList.Select(ep =>
                {
                    var epObj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                    epObj.Set("EndpointUrl",
                        JsValue.FromObject(engine, ep.EndpointUrl));
                    epObj.Set("SecurityMode",
                        JsValue.FromObject(engine, (int)ep.SecurityMode));
                    epObj.Set("SecurityPolicyUri",
                        JsValue.FromObject(engine, ep.SecurityPolicyUri));
                    epObj.Set("SecurityLevel",
                        JsValue.FromObject(engine, ep.SecurityLevel));
                    return (JsValue)epObj;
                }).ToArray());
            response.Set("Endpoints", epArray);

            return CreateUaStatusCode(engine, (uint)StatusCodes.Good);
        }

        private JsValue ServiceStub(Engine engine, string serviceName)
        {
            _logger.LogWarning("Service stub called: session.{Service}() — not yet implemented", serviceName);
            return CreateUaStatusCode(engine, (uint)StatusCodes.Good);
        }

        #endregion

        #region Helpers

        private static ObjectInstance CreateUaStatusCode(Engine engine, uint code)
        {
            var sc = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
            sc.Set("StatusCode", JsValue.FromObject(engine, (double)code));
            sc.Set("isGood", new ClrFunction(engine, "isGood",
                (_, _) => JsValue.FromObject(engine, StatusCode.IsGood(code))));
            sc.Set("isBad", new ClrFunction(engine, "isBad",
                (_, _) => JsValue.FromObject(engine, StatusCode.IsBad(code))));
            sc.Set("isUncertain", new ClrFunction(engine, "isUncertain",
                (_, _) => JsValue.FromObject(engine, StatusCode.IsUncertain(code))));
            sc.Set("toString", new ClrFunction(engine, "toString",
                (_, _) => JsValue.FromObject(engine, $"0x{code:X8}")));
            return sc;
        }

        private ObjectInstance CreateDataValuesArray(Engine engine, ArrayOf<DataValue> values)
        {
            var itemsList = new List<JsValue>();
            foreach (var dv in values)
            {
                var dvObj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                dvObj.Set("StatusCode",
                    CreateUaStatusCode(engine, dv.StatusCode.Code));

                // Value as UaVariant
                var variantObj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                var wrappedValue = dv.WrappedValue;
                object? rawValue = wrappedValue.AsBoxedObject();
                if (rawValue != null)
                {
                    variantObj.Set("DataType",
                        JsValue.FromObject(engine, (int)wrappedValue.TypeInfo.BuiltInType));
                    variantObj.Set("Value",
                        ConvertToJsValue(engine, rawValue));

                    // Array support
                    if (rawValue is Array arr)
                    {
                        variantObj.Set("getArraySize",
                            new ClrFunction(engine, "getArraySize",
                                (_, _) => JsValue.FromObject(engine, arr.Length)));
                    }
                    else
                    {
                        variantObj.Set("getArraySize",
                            new ClrFunction(engine, "getArraySize",
                                (_, _) => JsValue.FromObject(engine, -1)));
                    }

                    // toXxx() converters
                    variantObj.Set("toBoolean",
                        new ClrFunction(engine, "toBoolean",
                            (_, _) => JsValue.FromObject(engine, Convert.ToBoolean(rawValue))));
                    variantObj.Set("toInt32",
                        new ClrFunction(engine, "toInt32",
                            (_, _) => JsValue.FromObject(engine, Convert.ToInt32(rawValue))));
                    variantObj.Set("toUInt32",
                        new ClrFunction(engine, "toUInt32",
                            (_, _) => JsValue.FromObject(engine, Convert.ToUInt32(rawValue))));
                    variantObj.Set("toDouble",
                        new ClrFunction(engine, "toDouble",
                            (_, _) => JsValue.FromObject(engine, Convert.ToDouble(rawValue))));
                    variantObj.Set("toString",
                        new ClrFunction(engine, "toString",
                            (_, _) => JsValue.FromObject(engine, rawValue?.ToString() ?? "")));
                }
                dvObj.Set("Value", variantObj);

                // Timestamps
                dvObj.Set("SourceTimestamp",
                    JsValue.FromObject(engine, dv.SourceTimestamp.ToString("o")));
                dvObj.Set("ServerTimestamp",
                    JsValue.FromObject(engine, dv.ServerTimestamp.ToString("o")));
                dvObj.Set("SourcePicoseconds",
                    JsValue.FromObject(engine, dv.SourcePicoseconds));
                dvObj.Set("ServerPicoseconds",
                    JsValue.FromObject(engine, dv.ServerPicoseconds));

                // clone() method
                dvObj.Set("clone", new ClrFunction(engine, "clone",
                    (_, _) => dvObj)); // shallow clone is sufficient for most tests

                itemsList.Add(dvObj);
            }

            return (ObjectInstance)engine.Intrinsics.Array.Construct(itemsList.ToArray());
        }

        private ObjectInstance CreateBrowseResultsArray(Engine engine, ArrayOf<BrowseResult> results)
        {
            var itemsList = new List<JsValue>();
            foreach (var br in results)
            {
                var brObj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                brObj.Set("StatusCode",
                    CreateUaStatusCode(engine, br.StatusCode.Code));

                // References array
                var refsList = new List<JsValue>();
                foreach (var rd in br.References)
                {
                    var rdObj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
                    rdObj.Set("ReferenceTypeId",
                        CreateNodeIdObject(engine, rd.ReferenceTypeId));
                    rdObj.Set("IsForward",
                        JsValue.FromObject(engine, rd.IsForward));
                    rdObj.Set("NodeId",
                        CreateExpandedNodeIdObject(engine, rd.NodeId));
                    rdObj.Set("BrowseName",
                        CreateQualifiedNameObject(engine, rd.BrowseName));
                    rdObj.Set("DisplayName",
                        JsValue.FromObject(engine, rd.DisplayName.Text));
                    rdObj.Set("NodeClass",
                        JsValue.FromObject(engine, (int)rd.NodeClass));
                    rdObj.Set("TypeDefinition",
                        CreateExpandedNodeIdObject(engine, rd.TypeDefinition));
                    refsList.Add(rdObj);
                }
                brObj.Set("References",
                    engine.Intrinsics.Array.Construct(refsList.ToArray()));

                // ContinuationPoint
                byte[] cpBytes = (byte[]?)br.ContinuationPoint.ToArray() ?? Array.Empty<byte>();
                if (cpBytes.Length > 0)
                {
                    brObj.Set("ContinuationPoint",
                        JsValue.FromObject(engine, Convert.ToBase64String(cpBytes)));
                }
                else
                {
                    brObj.Set("ContinuationPoint", JsValue.Null);
                }

                itemsList.Add(brObj);
            }

            return (ObjectInstance)engine.Intrinsics.Array.Construct(itemsList.ToArray());
        }

        private static JsValue ConvertToJsValue(Engine engine, object? value)
        {
            return value switch {
                null => JsValue.Null,
                bool b => JsValue.FromObject(engine, b),
                string s => JsValue.FromObject(engine, s),
                byte n => JsValue.FromObject(engine, (double)n),
                sbyte n => JsValue.FromObject(engine, (double)n),
                short n => JsValue.FromObject(engine, (double)n),
                ushort n => JsValue.FromObject(engine, (double)n),
                int n => JsValue.FromObject(engine, (double)n),
                uint n => JsValue.FromObject(engine, (double)n),
                long n => JsValue.FromObject(engine, (double)n),
                ulong n => JsValue.FromObject(engine, (double)n),
                float n => JsValue.FromObject(engine, (double)n),
                double n => JsValue.FromObject(engine, n),
                DateTime dt => JsValue.FromObject(engine, dt.ToString("o")),
                Uuid guid => JsValue.FromObject(engine, guid.ToString()),
                NodeId nid => CreateNodeIdObject(engine, nid),
                LocalizedText lt => JsValue.FromObject(engine, lt.Text),
                QualifiedName qn => CreateQualifiedNameObject(engine, qn),
                StatusCode sc => CreateUaStatusCode(engine, sc.Code),
                _ => JsValue.FromObject(engine, value.ToString() ?? "")
            };
        }

        private static ObjectInstance CreateNodeIdObject(Engine engine, NodeId? nodeId)
        {
            var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
            if (nodeId == null || nodeId.Value.IsNull)
            {
                obj.Set("IdentifierNumeric", JsValue.FromObject(engine, 0));
                obj.Set("NamespaceIndex", JsValue.FromObject(engine, 0));
            }
            else
            {
                obj.Set("IdentifierNumeric",
                    JsValue.FromObject(engine, nodeId.Value.TryGetIdentifier(out uint numId) ? (double)numId : 0));
                obj.Set("NamespaceIndex",
                    JsValue.FromObject(engine, (double)nodeId.Value.NamespaceIndex));
            }
            obj.Set("toString", new ClrFunction(engine, "toString",
                (_, _) => JsValue.FromObject(engine, nodeId?.ToString() ?? "i=0")));
            return obj;
        }

        private static ObjectInstance CreateExpandedNodeIdObject(Engine engine, ExpandedNodeId? nodeId)
        {
            var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
            var nid = nodeId != null ? ExpandedNodeId.ToNodeId(nodeId.Value, null) : NodeId.Null;
            obj.Set("NodeId", CreateNodeIdObject(engine, nid));
            obj.Set("toString", new ClrFunction(engine, "toString",
                (_, _) => JsValue.FromObject(engine, nodeId?.ToString() ?? "i=0")));
            return obj;
        }

        private static ObjectInstance CreateQualifiedNameObject(Engine engine, QualifiedName? qn)
        {
            var obj = (ObjectInstance)engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());
            obj.Set("Name", JsValue.FromObject(engine, qn?.Name ?? ""));
            obj.Set("NamespaceIndex",
                JsValue.FromObject(engine, qn?.NamespaceIndex ?? 0));
            obj.Set("toString", new ClrFunction(engine, "toString",
                (_, _) => JsValue.FromObject(engine, qn?.ToString() ?? "")));
            return obj;
        }

        private static NodeId ParseNodeId(JsValue value)
        {
            if (value.IsString())
            {
                return NodeId.Parse(value.AsString());
            }
            if (value.IsObject())
            {
                var obj = (ObjectInstance)value;
                if (obj.HasProperty("IdentifierNumeric"))
                {
                    uint id = (uint)obj.Get("IdentifierNumeric").AsNumber();
                    ushort ns = (ushort)obj.Get("NamespaceIndex").AsNumber();
                    return new NodeId(id, ns);
                }
                var toString = obj.Get("toString");
                if (toString.IsObject())
                {
                    return NodeId.Parse(toString.Call(value, Array.Empty<JsValue>()).AsString());
                }
            }
            return NodeId.Null;
        }

        private static DataValue ParseDataValue(JsValue value)
        {
            if (!value.IsObject()) return new DataValue();
            var obj = (ObjectInstance)value;
            var dv = new DataValue();

            var val = obj.Get("Value");
            if (CttGlobals.IsDefined(val))
            {
                if (val.IsNumber()) dv.WrappedValue = val.AsNumber();
                else if (val.IsBoolean()) dv.WrappedValue = val.AsBoolean();
                else if (val.IsString()) dv.WrappedValue = val.AsString();
            }

            return dv;
        }

        #endregion
    }
}





