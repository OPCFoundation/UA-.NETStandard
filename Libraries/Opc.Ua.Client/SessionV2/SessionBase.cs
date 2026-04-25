#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Sessions
{
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Opc.Ua.Client;
    using Opc.Ua.Client.Subscriptions;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Globalization;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Security.Cryptography.X509Certificates;
    using System.Threading;
    using System.Threading.Tasks;
    using Opc.Ua.Client.Services;

    /// <summary>
    /// The session base class combines the session services and adds
    /// subscription management, complex type handling, and node cache
    /// as layers. It provides the connection services that are used by
    /// the Session class that automatically manages the session state.
    /// </summary>
    internal abstract class SessionBase : SessionClient, ISession,
        IServiceSetExtensions, ISubscriptionContext, INodeCacheContext,
        ISubscriptionManagerContext, IMonitoredItemServiceSet,
        ISubscriptionServiceSet, IAsyncDisposable
    {
        /// <inheritdoc/>
        public IUserIdentity Identity { get; private set; }

        /// <inheritdoc/>
        public ISubscriptionManager Subscriptions => _subscriptions;

        /// <inheritdoc/>
        public INodeCache NodeCache => _nodeCache;

        /// <inheritdoc/>
        public new IServiceMessageContext MessageContext { get; }

        /// <inheritdoc/>
        public ISystemContext SystemContext => _systemContext;

        /// <inheritdoc/>
        public IEncodeableFactory Factory => MessageContext.Factory;

        /// <inheritdoc/>
        public NamespaceTable NamespaceUris => MessageContext.NamespaceUris;

        /// <inheritdoc/>
        public ISubscriptionServiceSet SubscriptionServiceSet => this;

        /// <inheritdoc/>
        public IMonitoredItemServiceSet MonitoredItemServiceSet => this;

        /// <inheritdoc/>
        public IMethodServiceSet MethodServiceSet => this;

        /// <inheritdoc/>
        public IViewServiceSet ViewServiceSet => this;

        /// <inheritdoc/>
        public IAttributeServiceSet AttributeServiceSet => this;

        /// <inheritdoc/>
        public INodeManagementServiceSet NodeManagementServiceSet => this;

        /// <inheritdoc/>
        public TimeSpan SessionTimeout { get; private set; }

        /// <summary>
        /// Time the session was created
        /// </summary>
        public DateTimeOffset CreatedAt { get; }

        /// <summary>
        /// Time the session was connected
        /// </summary>
        public DateTimeOffset? ConnectedSince { get; private set; }

        /// <summary>
        /// Gets the endpoint used to connect to the server.
        /// </summary>
        protected ConfiguredEndpoint ConfiguredEndpoint { get; }

        /// <summary>
        /// The operation timeout to use for the session
        /// </summary>
        public new TimeSpan OperationTimeout
        {
            get
            {
                var operationTimeout = base.OperationTimeout;
                if (operationTimeout == 0 &&
                    ConfiguredEndpoint.Configuration != null)
                {
                    operationTimeout =
                        ConfiguredEndpoint.Configuration.OperationTimeout;
                }
                if (operationTimeout == 0)
                {
                    return kDefaultOperationTimeout;
                }
                return TimeSpan.FromMilliseconds(operationTimeout);
            }
            set
            {
                var operationTimeout = (int)value.TotalMilliseconds;
                if (operationTimeout == 0)
                {
                    return;
                }
                if (ConfiguredEndpoint.Configuration != null)
                {
                    ConfiguredEndpoint.Configuration.OperationTimeout =
                        operationTimeout;
                }
                base.OperationTimeout = operationTimeout;
            }
        }

        /// <summary>
        /// Gets the last keep alive timestamp
        /// </summary>
        public long LastKeepAliveTimestamp { get; internal set; }

        /// <summary>
        /// Number of namespace table changes
        /// </summary>
        public int NamespaceTableChanges => _namespaceTableChanges;

        /// <summary>
        /// Server uris
        /// </summary>
        public StringTable ServerUris => MessageContext.ServerUris;

        /// <summary>
        /// Number of namespace table changes
        /// </summary>
        internal int ServerTableChanges => _serverTableChanges;

        /// <summary>
        /// Current session options
        /// </summary>
        protected internal SessionCreateOptions Options { get; protected set; }

        /// <summary>
        /// Constructs a new instance of the <see cref="SessionBase"/> class.
        /// The application configuration is used to look up the certificate
        /// if none is provided.
        /// </summary>
        /// <param name="configuration">The configuration for the client
        /// application.</param>
        /// <param name="endpoint">The endpoint used to initialize the
        /// channel.</param>
        /// <param name="options">Session options</param>
        /// <param name="telemetry">The obs services to use</param>
        /// <param name="reverseConnect">Reverse connect manager</param>
        /// <param name="channelFactory">A factory to create new secure or
        /// http channels</param>
        protected SessionBase(ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint, SessionCreateOptions options,
            ITelemetryContext telemetry, ReverseConnectManager? reverseConnect,
            IChannelFactory? channelFactory = null)
            : base(telemetry, options.Channel)
        {
            Options = options;
            _meter = Observability.CreateMeter();
            _logger = Observability.LoggerFactory.CreateLogger<SessionBase>();
            _keepAliveTimer = TimeProvider.System.CreateTimer(
                _ => TriggerWorker(),
                null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            _configuration = configuration;
            _reverseConnect = reverseConnect;
            _channelFactory = channelFactory
                ?? new ChannelFactory(configuration, telemetry);

            CreatedAt = TimeProvider.System.GetUtcNow();
            ConfiguredEndpoint = endpoint;

            Identity = Options.Identity ?? new UserIdentity();
            SessionTimeout = GetSessionTimeout(Options);
            _clientCertificate = Options.ClientCertificate;

            _nodeCache = new NodeCache(this, Observability);
            _factory = EncodeableFactory.Create();
            var messageContext =
                Options.Channel?.MessageContext as ServiceMessageContext
                    ?? configuration.CreateMessageContext(_factory);

            MessageContext = messageContext;
            _systemContext = new SessionSystemContext((Opc.Ua.ITelemetryContext?)null)
            {
                SystemHandle = this,
                EncodeableFactory = _factory,
                NamespaceUris = NamespaceUris,
                ServerUris = ServerUris,
                TypeTable = _nodeCache.TypeTree,
                PreferredLocales = default,
                SessionId = NodeId.Null,
                UserIdentity = null
            };
            _subscriptions = new SubscriptionManager(this,
                telemetry.LoggerFactory, ReturnDiagnostics);
            _sessionWorker = SessionWorkerAsync(_cts.Token);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return DisposeAsync(true);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{SessionId}({Options.SessionName})";
        }

        /// <inheritdoc/>
        public async ValueTask<ResultSet<Node>> FetchNodesAsync(RequestHeader? header,
            IReadOnlyList<NodeId> nodeIds, CancellationToken ct)
        {
            if (nodeIds.Count == 0)
            {
                return ResultSet.Empty<Node>();
            }

            var nodeCollection = new List<Node>(nodeIds.Count);
            // first read only nodeclasses for nodes from server.
            var itemsToRead = nodeIds.Select(nodeId => new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.NodeClass
                }).ToArrayOf();

            var readResponse = await ReadAsync(header, 0, TimestampsToReturn.Neither,
                itemsToRead, ct).ConfigureAwait(false);

            var nodeClassValues = readResponse.Results;
            var diagnosticInfos = readResponse.DiagnosticInfos;
            Ua.ClientBase.ValidateResponse(nodeClassValues, itemsToRead);
            Ua.ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            // second determine attributes to read per nodeclass
            var attributesPerNodeId = new List<IDictionary<uint, DataValue?>?>(
                nodeIds.Count);

            var serviceResults = new List<ServiceResult>(nodeIds.Count);
            var attributesToReadList = new List<ReadValueId>();
            var responseHeader = readResponse.ResponseHeader;
            for (var index = 0; index < itemsToRead.Count; index++)
            {
                var node = new Node
                {
                    NodeId = itemsToRead[index].NodeId
                };
                if (!DataValue.IsGood(nodeClassValues[index]))
                {
                    nodeCollection.Add(node);
                    serviceResults.Add(new ServiceResult(
                        nodeClassValues[index].StatusCode, index, diagnosticInfos,
                        responseHeader.StringTable));
                    attributesPerNodeId.Add(null);
                    continue;
                }

                // check for valid node class.
                if ((object)nodeClassValues[index].WrappedValue is not int and not NodeClass)
                {
                    nodeCollection.Add(node);
                    serviceResults.Add(ServiceResult.Create(StatusCodes.BadUnexpectedError,
                        "Node does not have a valid value for NodeClass: {0}.",
                        nodeClassValues[index].WrappedValue));
                    attributesPerNodeId.Add(null);
                    continue;
                }

                node.NodeClass = (NodeClass)(object)nodeClassValues[index].WrappedValue;

                var attributes = CreateAttributes(node.NodeClass);
                foreach (var attributeId in attributes.Keys)
                {
                    var itemToRead = new ReadValueId
                    {
                        NodeId = node.NodeId,
                        AttributeId = attributeId
                    };
                    attributesToReadList.Add(itemToRead);
                }

                nodeCollection.Add(node);
                serviceResults.Add(ServiceResult.Good);
                attributesPerNodeId.Add(attributes);
            }

            if (attributesToReadList.Count > 0)
            {
                ArrayOf<ReadValueId> attributesToRead = attributesToReadList.ToArrayOf();
                readResponse = await ReadAsync(header, 0, TimestampsToReturn.Neither,
                    attributesToRead, ct).ConfigureAwait(false);

                var values = readResponse.Results;
                diagnosticInfos = readResponse.DiagnosticInfos;
                Ua.ClientBase.ValidateResponse(values, attributesToRead);
                Ua.ClientBase.ValidateDiagnosticInfos(diagnosticInfos, attributesToRead);
                responseHeader = readResponse.ResponseHeader;
                var readIndex = 0;
                for (var index = 0; index < nodeCollection.Count; index++)
                {
                    var attributes = attributesPerNodeId[index];
                    if (attributes == null)
                    {
                        continue;
                    }

                    var readCount = attributes.Count;
                    var subRangeAttributes = attributesToRead.Slice(readIndex, readCount);
                    var subRangeValues = values.Slice(readIndex, readCount);
                    var subRangeDiagnostics = diagnosticInfos.Count > 0 ?
                        diagnosticInfos.Slice(readIndex, readCount) :
                        diagnosticInfos;
                    try
                    {
                        nodeCollection[index] = ProcessReadResponse(responseHeader, attributes,
                            subRangeAttributes, subRangeValues, subRangeDiagnostics);
                        serviceResults[index] = ServiceResult.Good;
                    }
                    catch (ServiceResultException sre)
                    {
                        serviceResults[index] = sre.Result;
                    }
                    readIndex += readCount;
                }
            }
            return new ResultSet<Node>(nodeCollection, serviceResults);
        }

        /// <inheritdoc/>
        public async ValueTask<Node> FetchNodeAsync(RequestHeader? header,
            NodeId nodeId, CancellationToken ct)
        {
            // build list of attributes.
            var attributes = CreateAttributes();
            var itemsToRead = attributes.Keys.Select(attributeId => new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = attributeId
                }).ToArrayOf();
            // read from server.
            var readResponse = await ReadAsync(header, 0, TimestampsToReturn.Neither,
                itemsToRead, ct).ConfigureAwait(false);
            var values = readResponse.Results;
            var diagnosticInfos = readResponse.DiagnosticInfos;
            Ua.ClientBase.ValidateResponse(values, itemsToRead);
            Ua.ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);
            return ProcessReadResponse(readResponse.ResponseHeader, attributes,
                itemsToRead, values, diagnosticInfos);
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<ReferenceDescription>> FetchReferencesAsync(
            RequestHeader? header, NodeId nodeId, CancellationToken ct)
        {
            var collection = new List<ReferenceDescription>();
            await foreach (var result in BrowseAsync(header, null,
            [
                new BrowseDescription
                {
                    NodeId = nodeId,
                    BrowseDirection = BrowseDirection.Both,
                    ReferenceTypeId = ReferenceTypeIds.References,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                }
            ], ct).ConfigureAwait(false))
            {
                collection.AddRange(result.Result.References.ToList());
            }
            return collection.ToArrayOf();
        }

        /// <inheritdoc/>
        public async ValueTask<ResultSet<ArrayOf<ReferenceDescription>>> FetchReferencesAsync(
            RequestHeader? header, IReadOnlyList<NodeId> nodeIds, CancellationToken ct)
        {
            if (nodeIds.Count == 0)
            {
                return ResultSet.Empty<ArrayOf<ReferenceDescription>>();
            }
            var resultsMap = nodeIds.Select(nodeId => new BrowseDescription
            {
                Handle = ServiceResult.Good,
                NodeId = nodeId,
                BrowseDirection = BrowseDirection.Both,
                ReferenceTypeId = ReferenceTypeIds.References,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            })
            .ToDictionary(k => k, _ => new ArrayOf<ReferenceDescription>());
            await foreach (var result in BrowseAsync(header, null,
                resultsMap.Keys.ToArrayOf(), ct).ConfigureAwait(false))
            {
                resultsMap[result.Description] = resultsMap[result.Description].AddItems(result.Result.References);
                if (ServiceResult.IsNotGood(result.Result.StatusCode))
                {
                    result.Description.Handle = new ServiceResult(result.Result.StatusCode);
                }
            }
            return new ResultSet<ArrayOf<ReferenceDescription>>(
                resultsMap.Select(r => r.Value).ToList(),
                resultsMap.Select(r => (ServiceResult)r.Key.Handle).ToList());
        }

        /// <inheritdoc/>
        public async ValueTask<DataValue> FetchValueAsync(RequestHeader? header,
            NodeId nodeId, CancellationToken ct)
        {
            var itemsToRead = new ReadValueId[]
            {
                new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                }
            }.ToArrayOf();
            // read from server.
            var readResponse = await ReadAsync(header, 0, TimestampsToReturn.Both,
                itemsToRead, ct).ConfigureAwait(false);

            var values = readResponse.Results;
            var diagnosticInfos = readResponse.DiagnosticInfos;
            Ua.ClientBase.ValidateResponse(values, itemsToRead);
            Ua.ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            if (StatusCode.IsBad(values[0].StatusCode))
            {
                var result = Ua.ClientBase.GetResult(values[0].StatusCode, 0,
                    diagnosticInfos, readResponse.ResponseHeader);
                throw new ServiceResultException(result);
            }
            return values[0];
        }

        /// <inheritdoc/>
        public async ValueTask<ResultSet<DataValue>> FetchValuesAsync(RequestHeader? header,
            IReadOnlyList<NodeId> nodeIds, CancellationToken ct)
        {
            if (nodeIds.Count == 0)
            {
                return ResultSet.Empty<DataValue>();
            }

            // read all values from server.
            var itemsToRead = nodeIds.Select(nodeId => new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value
                }).ToArrayOf();

            // read from server.
            var errors = new List<ServiceResult>(itemsToRead.Count);

            var readResponse = await ReadAsync(header, 0, TimestampsToReturn.Both,
                itemsToRead, ct).ConfigureAwait(false);

            var values = readResponse.Results;
            var diagnosticInfos = readResponse.DiagnosticInfos;

            Ua.ClientBase.ValidateResponse(values, itemsToRead);
            Ua.ClientBase.ValidateDiagnosticInfos(diagnosticInfos, itemsToRead);

            foreach (var value in values)
            {
                var result = ServiceResult.Good;
                if (StatusCode.IsBad(value.StatusCode))
                {
                    result = Ua.ClientBase.GetResult(value.StatusCode, 0,
                        diagnosticInfos, readResponse.ResponseHeader);
                }
                errors.Add(result);
            }
            return new ResultSet<DataValue>(values.ToList(), errors);
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<BrowseDescriptionResult> BrowseAsync(
            RequestHeader? requestHeader, ViewDescription? view,
            ArrayOf<BrowseDescription> nodesToBrowse,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var first = await BrowseAsync(requestHeader, view, 0,
                nodesToBrowse, ct).ConfigureAwait(false);
            Ua.ClientBase.ValidateResponse(first.Results, nodesToBrowse);
            Ua.ClientBase.ValidateDiagnosticInfos(first.DiagnosticInfos, nodesToBrowse);

            var browseDescriptions = new List<BrowseDescription>();
            var continuationPoints = new List<ByteString>();
            for (var i = 0; i < first.Results.Count; i++)
            {
                if (StatusCode.IsGood(first.Results[i].StatusCode) &&
                    !first.Results[i].ContinuationPoint.IsNull &&
                    first.Results[i].ContinuationPoint.Length != 0)
                {
                    if (first.Results[i].References.Count > 0)
                    {
                        browseDescriptions.Add(nodesToBrowse[i]);
                        continuationPoints.Add(first.Results[i].ContinuationPoint);
                    }
                    else // Rewrite the error and do not follow continuation points
                    {
                        _logger.LogWarning(
                            "{Session}: Server returned empty references but a " +
                            "continuation. Stopping to prevent denial of service.",
                            this);
                        yield return new BrowseDescriptionResult(nodesToBrowse[i],
                            new BrowseResult { StatusCode = StatusCodes.BadNoData });
                        continue;
                    }
                }
                yield return new BrowseDescriptionResult(nodesToBrowse[i],
                    first.Results[i]);
            }
            try
            {
                while (continuationPoints.Count > 0)
                {
                    var next = await BrowseNextAsync(requestHeader, false,
                        continuationPoints.ToArrayOf(), ct).ConfigureAwait(false);
                    Ua.ClientBase.ValidateResponse(next.Results, continuationPoints.ToArrayOf());
                    Ua.ClientBase.ValidateDiagnosticInfos(next.DiagnosticInfos, continuationPoints.ToArrayOf());
                    continuationPoints = new List<ByteString>();

                    for (var i = 0; i < next.Results.Count; i++)
                    {
                        var browseDescription = browseDescriptions[i];
                        if (StatusCode.IsGood(next.Results[i].StatusCode) &&
                            !next.Results[i].ContinuationPoint.IsNull &&
                            next.Results[i].ContinuationPoint.Length != 0)
                        {
                            if (next.Results[i].References.Count > 0)
                            {
                                continuationPoints.Add(next.Results[i].ContinuationPoint);
                            }
                            else // Rewrite the error and stop
                            {
                                _logger.LogWarning(
                                    "{Session}: Server returned empty references but a " +
                                    "continuation. Stopping to prevent denial of service.",
                                    this);
                                browseDescriptions.RemoveAt(i);
                                yield return new BrowseDescriptionResult(browseDescription,
                                    new BrowseResult { StatusCode = StatusCodes.BadNoData });
                                continue;
                            }
                        }
                        else
                        {
                            browseDescriptions.RemoveAt(i);
                        }
                        yield return new BrowseDescriptionResult(browseDescription,
                            next.Results[i]);
                    }
                }
            }
            finally
            {
                if (continuationPoints.Count > 0)
                {
                    // Try release any dangling continuation points
                    try
                    {
                        await BrowseNextAsync(requestHeader, true, continuationPoints.ToArrayOf(),
                            default).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "{Session}: Failed to release continuation points.", this);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public IManagedSubscription CreateSubscription(ISubscriptionNotificationHandler handler,
            IOptionsMonitor<Subscriptions.SubscriptionOptions> options, IMessageAckQueue queue)
        {
            return CreateSubscription(handler, options, queue, Observability);
        }

        /// <inheritdoc/>
        public virtual async ValueTask OpenAsync(CancellationToken ct = default)
        {
            var securityPolicyUri = ConfiguredEndpoint.Description.SecurityPolicyUri;
            // catch security policies which are not supported by core
            if (SecurityPolicies.GetDisplayName(securityPolicyUri) == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityPolicyRejected,
                    "The chosen security policy is not supported by the " +
                    "client to connect to the server.");
            }

            // get identity token.
            Identity = Options.Identity ?? new UserIdentity();
            // check that the user identity is supported by the endpoint.
            using var identityToken = Identity.TokenHandler.Copy();
            var identityPolicy = GetIdentityPolicyFromToken(identityToken.Token);

            var requireEncryption = securityPolicyUri != SecurityPolicies.None;
            if (!requireEncryption)
            {
                requireEncryption =
                    identityPolicy.SecurityPolicyUri != SecurityPolicies.None &&
                    !string.IsNullOrEmpty(identityPolicy.SecurityPolicyUri);
            }

            ConnectedSince = null;
            await _connecting.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _subscriptions.Pause();
                StopKeepAliveTimer();
                var previousSessionId = SessionId;
                var previousAuthenticationToken = AuthenticationToken;

                _logger.LogInformation("{Session}: {Action} ({Id})...",
                    this, SessionId != null ? "Recreating" : "Opening",
                    SessionId);

                // Ensure channel and optionally a reverse connection exists
                await WaitForReverseConnectIfNeededAsync(ct).ConfigureAwait(false);
                var transportChannel = NullableTransportChannel;
                if (transportChannel == null)
                {
                    _logger.LogInformation("{Session}: Creating new channel.",
                        this);
                    TransportChannel = await CreateChannelAsync(ct).ConfigureAwait(false);
                }

                await CheckCertificatesAreLoadedAsync(ct).ConfigureAwait(false);

                // validate the server certificate /certificate chain.
                X509Certificate2? serverCertificate = null;
                var certificateData = ConfiguredEndpoint.Description.ServerCertificate;

                if (certificateData.Length > 0)
                {
                    var serverCertificateChain = Utils.ParseCertificateChainBlob(certificateData, (Opc.Ua.ITelemetryContext?)null);
                    if (serverCertificateChain.Count > 0)
                    {
                        serverCertificate = serverCertificateChain[0];
                    }
                    if (requireEncryption)
                    {
                        // validation skipped until IOP isses are resolved.
                        // ValidateServerCertificateApplicationUri(serverCertificate);
                        if (Options.CheckDomain)
                        {
                            await _configuration.CertificateValidator.ValidateAsync(
                                serverCertificateChain, ConfiguredEndpoint,
                                ct).ConfigureAwait(false);
                        }
                        else
                        {
                            await _configuration.CertificateValidator.ValidateAsync(
                                serverCertificateChain, ct).ConfigureAwait(false);
                        }
                    }
                }

                // create a nonce.
                var length = (uint)_configuration.SecurityConfiguration.NonceLength;
                var clientNonce = Nonce.CreateRandomNonceData((int)length);

                // send the application instance certificate for the client.
                var clientCertificateData = _clientCertificate?.RawData;
                byte[]? clientCertificateChainData = null;

                if (_clientCertificateChain?.Count > 0 &&
                    _configuration.SecurityConfiguration.SendCertificateChain)
                {
                    var clientCertificateChain = new List<byte>();
                    for (var i = 0; i < _clientCertificateChain.Count; i++)
                    {
                        clientCertificateChain.AddRange(_clientCertificateChain[i].RawData);
                    }
                    clientCertificateChainData = [.. clientCertificateChain];
                }

                var clientDescription = new ApplicationDescription
                {
                    ApplicationUri = _configuration.ApplicationUri,
                    ApplicationName = (LocalizedText)_configuration.ApplicationName,
                    ApplicationType = ApplicationType.Client,
                    ProductUri = _configuration.ProductUri
                };

                var sessionTimeout = GetSessionTimeout(Options);
                var successCreateSession = false;
                CreateSessionResponse? response = null;

                // if security none, first try to connect without certificate
                if (ConfiguredEndpoint.Description.SecurityPolicyUri == SecurityPolicies.None)
                {
                    // first try to connect with client certificate NULL
                    try
                    {
                        response = await CreateSessionAsync(null, clientDescription,
                            ConfiguredEndpoint.Description.Server.ApplicationUri,
                            ConfiguredEndpoint.EndpointUrl.ToString(), Options.SessionName, (ByteString)clientNonce,
                            default, sessionTimeout.TotalMilliseconds,
                            (uint)MessageContext.MaxMessageSize, ct).ConfigureAwait(false);
                        successCreateSession = true;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogInformation(
                            "{Session}: Create session failed with client certificate NULL. {Error}",
                            this, ex.Message);
                        successCreateSession = false;
                    }
                }

                if (!successCreateSession)
                {
                    response = await CreateSessionAsync(null, clientDescription,
                        ConfiguredEndpoint.Description.Server.ApplicationUri,
                        ConfiguredEndpoint.EndpointUrl.ToString(), Options.SessionName, (ByteString)clientNonce,
                        (ByteString)(clientCertificateChainData ?? clientCertificateData ?? []),
                        sessionTimeout.TotalMilliseconds, (uint)MessageContext.MaxMessageSize,
                        ct).ConfigureAwait(false);
                }

                Debug.Assert(response != null);

                var sessionId = response.SessionId;
                var authenticationToken = response.AuthenticationToken;
                var serverNonce = response.ServerNonce.IsNull ? Array.Empty<byte>() : response.ServerNonce.ToArray();
                var serverCertificateData = response.ServerCertificate;
                var serverSignature = response.ServerSignature;
                var serverEndpoints = response.ServerEndpoints;

                SessionTimeout = TimeSpan.FromMilliseconds(response.RevisedSessionTimeout);
                _maxRequestMessageSize = response.MaxRequestMessageSize;

                if (sessionTimeout != SessionTimeout)
                {
                    _logger.LogInformation(
                        "{Session}: Revised session timeout from {Old} to {New}.",
                        this, sessionTimeout, SessionTimeout);
                }
                _logger.LogInformation(
                    "{Session}: Max request/response message sizes: {Request}/{Response}.",
                    this, _maxRequestMessageSize, MessageContext.MaxMessageSize);
                // save session id and cookie in base
                base.SessionCreated(sessionId, authenticationToken);

                // we need to call CloseSession if CreateSession was successful
                // but some other exception is thrown
                try
                {
                    // verify that the server returned the same instance certificate.
                    ValidateServerCertificateData(serverCertificateData.ToArray());
                    ValidateServerEndpoints(serverEndpoints);
                    ValidateServerSignature(serverCertificate, serverSignature,
                        clientCertificateData, clientCertificateChainData, clientNonce);

                    // create the client signature.
                    var dataToSign = Utils.Append(serverCertificate?.RawData, serverNonce);
                    var clientSignature = SecurityPolicies.CreateSignatureData(
                        securityPolicyUri, _clientCertificate, dataToSign);

                    // select the security policy for the user token.
                    securityPolicyUri = identityPolicy.SecurityPolicyUri;
                    if (string.IsNullOrEmpty(securityPolicyUri))
                    {
                        securityPolicyUri = ConfiguredEndpoint.Description.SecurityPolicyUri;
                    }

                    var previousServerNonce = (NullableTransportChannel as ISecureChannel)?.CurrentToken?.ServerNonce?.ToArray()
                        ?? [];

                    // validate server nonce and security parameters for user identity.
                    ValidateServerNonce(Identity, serverNonce, securityPolicyUri!,
                        previousServerNonce, ConfiguredEndpoint.Description.SecurityMode);

                    // sign data with user token.
                    var userTokenSignature = identityToken.Sign(dataToSign, securityPolicyUri);
                    // encrypt token.
                    identityToken.Encrypt(serverCertificate, serverNonce, securityPolicyUri, MessageContext);
                    // send the software certificates assigned to the client.

                    // activate session.
                    var preferredLocales = Options.PreferredLocales ??
                        [CultureInfo.CurrentCulture.Name];
                    var activateResponse = await ActivateSessionAsync(null, clientSignature,
                        [], preferredLocales.ToArrayOf(),
                        new ExtensionObject(identityToken.Token), userTokenSignature,
                        ct).ConfigureAwait(false);

                    serverNonce = activateResponse.ServerNonce.IsNull ? Array.Empty<byte>() : activateResponse.ServerNonce.ToArray();
                    var certificateResults = activateResponse.Results;
                    var certificateDiagnosticInfos = activateResponse.DiagnosticInfos;

                    if (certificateResults.Count > 0)
                    {
                        for (var i = 0; i < certificateResults.Count; i++)
                        {
                            _logger.LogInformation(
                                "{Session}: ActivateSession result[{Index}] = {Result}", i,
                                this, certificateResults[i]);
                        }
                    }

                    // save nonces and update system context.
                    _previousServerNonce = previousServerNonce;
                    _serverNonce = serverNonce;
                    _serverCertificate = serverCertificate;
                    _systemContext.PreferredLocales = preferredLocales.ToArrayOf();
                    _systemContext.SessionId = SessionId;
                    _systemContext.UserIdentity = Identity;

                    NodeCache.Clear();

                    // fetch namespaces.
                    await FetchNamespaceTablesAsync(ct).ConfigureAwait(false);
                    // fetch operation limits
                    await FetchOperationLimitsAsync(ct).ConfigureAwait(false);

                    await _subscriptions.RecreateSubscriptionsAsync(previousSessionId,
                        ct).ConfigureAwait(false);
                    _subscriptions.Resume();

                    // call session created callback, which was already set in base class only.
                    SessionCreated(sessionId, authenticationToken);
                    ConnectedSince = TimeProvider.System.GetUtcNow();
                }
                catch (Exception)
                {
                    _subscriptions.Pause();
                    try
                    {
                        await base.CloseSessionAsync(null, false, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("{Session}: CloseSessionAsync raised exception " +
                            "{Error} during cleanup.", this, ex.Message);
                    }
                    finally
                    {
                        SessionCreated(NodeId.Null, NodeId.Null);
                    }
                    // No throw
                    await CloseChannelAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            }
            finally
            {
                _connecting.Release();
            }
            _subscriptions.Update();
            ResetKeepAliveTimer();
        }

        /// <inheritdoc/>
        public virtual async ValueTask ReconnectAsync(CancellationToken ct = default)
        {
            await _connecting.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _subscriptions.Pause();
                StopKeepAliveTimer();
                _logger.LogInformation("{Session}: RECONNECT starting.", this);
                await CheckCertificatesAreLoadedAsync(ct).ConfigureAwait(false);

                // create the client signature.
                var dataToSign = Utils.Append(_serverCertificate?.RawData, _serverNonce);
                var endpoint = ConfiguredEndpoint.Description;
                var clientSignature = SecurityPolicies.CreateSignatureData(
                    endpoint.SecurityPolicyUri, _clientCertificate, dataToSign);

                using var identityToken = Identity.TokenHandler.Copy();
                var identityPolicy = GetIdentityPolicyFromToken(identityToken.Token);

                // select the security policy for the user token.
                var securityPolicyUri = identityPolicy.SecurityPolicyUri;
                if (string.IsNullOrEmpty(securityPolicyUri))
                {
                    securityPolicyUri = endpoint.SecurityPolicyUri;
                }

                // validate server nonce and security parameters for user identity.
                ValidateServerNonce(Identity, _serverNonce, securityPolicyUri!,
                    _previousServerNonce, ConfiguredEndpoint.Description.SecurityMode);

                // sign data with user token.
                var userTokenSignature = identityToken.Sign(dataToSign, securityPolicyUri);

                // encrypt token.
                identityToken.Encrypt(_serverCertificate, _serverNonce, securityPolicyUri!, MessageContext);

                _logger.LogInformation("{Session}: REPLACING channel.", this);
                var channel = NullableTransportChannel;

                // check if the channel supports reconnect.
                if (channel != null &&
                    (channel.SupportedFeatures & TransportChannelFeatures.Reconnect) != 0)
                {
                    await channel.ReconnectAsync(_connection).ConfigureAwait(false);
                }
                else
                {
                    // re-create channel, disposes the existing channel.
                    TransportChannel = await CreateChannelAsync(ct).ConfigureAwait(false);
                }

                _logger.LogInformation("{Session}: RE-ACTIVATING", this);
                var header = new RequestHeader
                {
                    TimeoutHint = (uint)kReconnectTimeout.TotalMilliseconds
                };
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(kReconnectTimeout / 2);
                try
                {
                    var preferredLocales = Options.PreferredLocales ??
                        [CultureInfo.CurrentCulture.Name];
                    var activation = await base.ActivateSessionAsync(header, clientSignature,
                        [], preferredLocales.ToArrayOf(),
                        new ExtensionObject(identityToken.Token), userTokenSignature,
                        cts.Token).ConfigureAwait(false);

                    var serverNonce = activation.ServerNonce.IsNull ? Array.Empty<byte>() : activation.ServerNonce.ToArray();
                    var certificateResult = activation.Results;
                    var diagnostic = activation.DiagnosticInfos;

                    _previousServerNonce = _serverNonce;
                    _serverNonce = serverNonce;

                    _logger.LogInformation("{Session}: RECONNECT completed successfully.",
                        this);
                    _subscriptions.Resume();
                }
                catch (OperationCanceledException e) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning("{Session}: ACTIVATE SESSION timed out.", this);
                    throw ServiceResultException.Create(StatusCodes.BadTimeout,
                        e, "Timeout during activation");
                }
                catch (ServiceResultException sre)
                {
                    _logger.LogWarning("{Session}: ACTIVATE SESSION failed due to {Error}.",
                        this, sre.StatusCode);
                    throw;
                }
            }
            finally
            {
                _connecting.Release();
            }
            _subscriptions.Update();
            ResetKeepAliveTimer();
        }

        /// <inheritdoc/>
        public virtual async ValueTask<ServiceResult> CloseAsync(bool closeChannel,
            bool deleteSubscriptions, CancellationToken ct = default)
        {
            // check if already closed.
            if (Disposed)
            {
                return ServiceResult.Good;
            }

            var result = ServiceResult.Good;
            await _connecting.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _subscriptions.Pause();
                // stop the keep alive timer.
                StopKeepAliveTimer();

                // check if correctly connected.
                var connected = Connected;
                ConnectedSince = null;

                // close the session with the server.
                if (connected)
                {
                    try
                    {
                        // close the session and delete all subscriptions if
                        // specified.
                        var timeout = closeChannel ? TimeSpan.FromSeconds(2)
                            : Options.KeepAliveInterval ?? kDefaultKeepAliveInterval;
                        var requestHeader = new RequestHeader()
                        {
                            TimeoutHint = timeout > TimeSpan.Zero ?
                                (uint)timeout.TotalMilliseconds :
                                (uint)OperationTimeout.TotalMilliseconds
                        };
                        var response = await base.CloseSessionAsync(requestHeader,
                            deleteSubscriptions, ct).ConfigureAwait(false);
                        // raised notification indicating the session is closed.

                        SessionCreated(NodeId.Null, NodeId.Null);
                    }
                    // don't throw errors on disconnect, but return them
                    // so the caller can log the error.
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "{Session}: Error closing session.", this);
                        result = new ServiceResult(ex);
                    }
                }

                if (closeChannel)
                {
                    await CloseChannelAsync(ct).ConfigureAwait(false);
                }
                return result;
            }
            finally
            {
                _connecting.Release();
            }
        }

        /// <inheritdoc/>
        public sealed override async Task<StatusCode> CloseAsync(CancellationToken ct)
        {
            var result = await CloseAsync(true, true, ct).ConfigureAwait(false);
            return result.StatusCode;
        }

        /// <inheritdoc/>
        public sealed override void DetachChannel()
        {
            // Overriding to remove any existing connection that was used
            // to create the channel
            _connection = null;
            base.DetachChannel();
        }

        /// <summary>
        /// Create a managed subscription inside the session
        /// </summary>
        /// <param name="handler"></param>
        /// <param name="options"></param>
        /// <param name="queue"></param>
        /// <param name="telemetry"></param>
        /// <returns></returns>
        protected abstract IManagedSubscription CreateSubscription(
            ISubscriptionNotificationHandler handler, IOptionsMonitor<Subscriptions.SubscriptionOptions> options,
            IMessageAckQueue queue, ITelemetryContext telemetry);

        /// <summary>
        /// Dispose the session
        /// </summary>
        /// <param name="disposing"></param>
        /// <returns></returns>
        protected virtual async ValueTask DisposeAsync(bool disposing)
        {
            if (_disposeAsyncCalled)
            {
                return;
            }
            _disposeAsyncCalled = true;
            if (disposing && !Disposed)
            {
                try
                {
                    await _cts.CancelAsync().ConfigureAwait(false);
                    StopKeepAliveTimer();
                    await _subscriptions.DisposeAsync().ConfigureAwait(false);

                    // Should not throw
                    TriggerWorker();
                    await _sessionWorker.ConfigureAwait(false);

                    // Will not do anything if already disposed
                    await CloseAsync(true, true, default).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Session}: Exception during dispose", this);
                }

                _nodeCache.Clear();
                _keepAliveTimer.Dispose();
                _cts.Dispose();
                _meter.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
#pragma warning disable CA2215 // Dispose methods should call base class dispose
        protected sealed override void Dispose(bool disposing)
#pragma warning restore CA2215 // Dispose methods should call base class dispose
        {
            if (!_disposeAsyncCalled) // Dispose async which will call base dispose
            {
                DisposeAsync(true).AsTask().GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Called when the state of the session changes
        /// </summary>
        /// <param name="state"></param>
        /// <param name="serviceResult"></param>
        protected virtual void OnStateChange(SessionState state, ServiceResult serviceResult)
        {
            switch (state)
            {
                case SessionState.FailedRetrying:
                    _logger.LogCritical("{Session}: CONNECT FAILED: {Result}", this, serviceResult);
                    break;
                case SessionState.Connecting:
                    _logger.LogInformation("{Session}: CONNECTING...", this);
                    break;
                case SessionState.Connected:
                    _logger.LogInformation("{Session}: CONNECTED.", this);
                    break;
                case SessionState.Disconnected:
                    _logger.LogInformation("{Session}: DISCONNECTED.", this);
                    break;
                case SessionState.Closed:
                    _logger.LogInformation("{Session}: CLOSED.", this);
                    break;
                case SessionState.ConnectError:
                    _logger.LogDebug("{Session}: CONNECT ERROR: {Result}", this, serviceResult);
                    break;
            }
        }

        /// <inheritdoc/>
        protected sealed override void RequestCompleted(IServiceRequest? request,
            IServiceResponse? response, string serviceName)
        {
            var sr = response?.ResponseHeader?.ServiceResult;
            if (sr != null && ServiceResult.IsGood(sr))
            {
                ResetKeepAliveTimer();
            }
            base.RequestCompleted(request, response, serviceName);
        }

        /// <summary>
        /// Worker wait
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        protected async ValueTask WorkerWaitAsync(CancellationToken ct)
        {
            await _trigger.WaitAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Fetch namespace tables and log any changes to the tables.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        internal async Task FetchNamespaceTablesAsync(CancellationToken ct)
        {
            var (values, errors) = await FetchValuesAsync(null,
            [
                VariableIds.Server_NamespaceArray,
                VariableIds.Server_ServerArray
            ], ct).ConfigureAwait(false);

            // validate namespace array.
            if (errors.Count > 0 && ServiceResult.IsBad(errors[0]))
            {
                _logger.LogDebug(
                    "{Session}: Failed to read NamespaceArray: {Status}",
                    this, errors[0]);
                throw new ServiceResultException(errors[0]);
            }
            // validate namespace is a string array.
            if ((object)values[0].WrappedValue is not string[] namespaces)
            {
                throw ServiceResultException.Create(StatusCodes.BadTypeMismatch,
                    $"{this}: Returned namespace array in wrong type!");
            }
            var oldNsTable = NamespaceUris.ToArray();
            NamespaceUris.Update(namespaces);
            LogNamespaceTableChanges(false, oldNsTable, namespaces);

            if (errors.Count > 1 && ServiceResult.IsBad(errors[1]))
            {
                // We tolerate this
                _logger.LogWarning(
                    "{Session}: Failed to read ServerArray node: {Status} ",
                    this, errors[1]);
                return;
            }
            if ((object)values[1].WrappedValue is not string[] serverUris)
            {
                throw ServiceResultException.Create(StatusCodes.BadTypeMismatch,
                    $"{this}: Returned server array with wrong type!");
            }
            var oldSrvTables = ServerUris.ToArray();
            ServerUris.Update(serverUris);
            LogNamespaceTableChanges(true, oldSrvTables, serverUris);

            void LogNamespaceTableChanges(bool serverUris, string[] oldTable, string[] newTable)
            {
                if (oldTable.Length <= 1)
                {
                    return; // First time or root namespace only only
                }
                var tableChanged = false;
                for (var i = 0; i < Math.Max(oldTable.Length, newTable.Length); i++)
                {
                    var tableName = serverUris ? "Server" : "Namespace";
                    if (i < oldTable.Length && i < newTable.Length)
                    {
                        if (oldTable[i] == newTable[i])
                        {
                            continue;
                        }
                        tableChanged = true;
                        _logger.LogWarning(
                            "{Session}: {Table} index #{Index} changed from {Old} to {New}",
                            this, tableName, i, oldTable[i], newTable[i]);
                    }
                    else if (i < oldTable.Length)
                    {
                        tableChanged = true;
                        _logger.LogWarning(
                            "{Session}: {Table} index #{Index} removed {Old}",
                            this, tableName, i, oldTable[i]);
                    }
                    else
                    {
                        tableChanged = true;
                        _logger.LogWarning(
                            "{Session}: {Table} index #{Index} added {New}",
                            this, tableName, i, newTable[i]);
                    }
                }
                if (tableChanged)
                {
                    if (serverUris)
                    {
                        Interlocked.Increment(ref _serverTableChanges);
                    }
                    else
                    {
                        Interlocked.Increment(ref _namespaceTableChanges);
                    }
                }
            }
        }

        /// <summary>
        /// Read operation limits
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        internal async ValueTask FetchOperationLimitsAsync(CancellationToken ct)
        {
            // First we read the node read max to optimize the second read.
            var nodeIds = new[]
            {
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead
            };
            var (values, errors) = await FetchValuesAsync(null, nodeIds, ct).ConfigureAwait(false);
            var index = 0;
            OperationLimits.MaxNodesPerRead = Get<uint>(ref index, values, errors);

            nodeIds =
            [
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadData,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryReadEvents,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerWrite,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateData,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerHistoryUpdateEvents,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerMethodCall,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerBrowse,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRegisterNodes,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerNodeManagement,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxMonitoredItemsPerCall,
        VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerTranslateBrowsePathsToNodeIds,
        VariableIds.Server_ServerCapabilities_MaxBrowseContinuationPoints,
        VariableIds.Server_ServerCapabilities_MaxHistoryContinuationPoints,
        VariableIds.Server_ServerCapabilities_MaxQueryContinuationPoints,
        VariableIds.Server_ServerCapabilities_MaxStringLength,
        VariableIds.Server_ServerCapabilities_MaxArrayLength,
        VariableIds.Server_ServerCapabilities_MaxByteStringLength,
        VariableIds.Server_ServerCapabilities_MinSupportedSampleRate,
        VariableIds.Server_ServerCapabilities_MaxSessions,
        VariableIds.Server_ServerCapabilities_MaxSubscriptions,
        VariableIds.Server_ServerCapabilities_MaxMonitoredItems,
        VariableIds.Server_ServerCapabilities_MaxMonitoredItemsPerSubscription,
        VariableIds.Server_ServerCapabilities_MaxMonitoredItemsQueueSize,
        VariableIds.Server_ServerCapabilities_MaxSubscriptionsPerSession,
        VariableIds.Server_ServerCapabilities_MaxWhereClauseParameters,
        VariableIds.Server_ServerCapabilities_MaxSelectClauseParameters
            ];

            (values, errors) = await FetchValuesAsync(null, nodeIds, ct).ConfigureAwait(false);
            index = 0;
            OperationLimits.MaxNodesPerHistoryReadData = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerHistoryReadEvents = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerWrite = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerRead = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerHistoryUpdateData = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerHistoryUpdateEvents = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerMethodCall = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerBrowse = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerRegisterNodes = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerNodeManagement = Get<uint>(ref index, values, errors);
            OperationLimits.MaxMonitoredItemsPerCall = Get<uint>(ref index, values, errors);
            OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds = Get<uint>(ref index, values, errors);
            OperationLimits.MaxBrowseContinuationPoints = Get<ushort>(ref index, values, errors);
            OperationLimits.MaxHistoryContinuationPoints = Get<ushort>(ref index, values, errors);
            OperationLimits.MaxQueryContinuationPoints = Get<ushort>(ref index, values, errors);
            OperationLimits.MaxStringLength = Get<uint>(ref index, values, errors);
            OperationLimits.MaxArrayLength = Get<uint>(ref index, values, errors);
            OperationLimits.MaxByteStringLength = Get<uint>(ref index, values, errors);
            OperationLimits.MinSupportedSampleRate = Get<double>(ref index, values, errors);
            OperationLimits.MaxSessions = Get<uint>(ref index, values, errors);
            OperationLimits.MaxSubscriptions = Get<uint>(ref index, values, errors);
            OperationLimits.MaxMonitoredItems = Get<uint>(ref index, values, errors);
            OperationLimits.MaxMonitoredItemsPerSubscription = Get<uint>(ref index, values, errors);
            OperationLimits.MaxMonitoredItemsQueueSize = Get<uint>(ref index, values, errors);
            OperationLimits.MaxSubscriptionsPerSession = Get<uint>(ref index, values, errors);
            OperationLimits.MaxWhereClauseParameters = Get<uint>(ref index, values, errors);
            OperationLimits.MaxSelectClauseParameters = Get<uint>(ref index, values, errors);

            // Helper extraction
            static T Get<T>(ref int index, IReadOnlyList<DataValue> values,
                IReadOnlyList<ServiceResult> errors) where T : struct
            {
                var value = values[index];
                var error = errors.Count > 0 ? errors[index] : ServiceResult.Good;
                index++;
                if (ServiceResult.IsNotBad(error) && (object)value.WrappedValue is T retVal)
                {
                    return retVal;
                }
                return default;
            }
        }

        /// <summary>
        /// Manages the session with the server. Sends keep alives as needed
        /// and if the connection is lost, tries to reconnect.
        /// </summary>
        /// <param name="ct"></param>
        /// <exception cref="ServiceResultException"></exception>
        internal virtual async Task SessionWorkerAsync(CancellationToken ct)
        {
            // Initially the session is not connected
            _logger.LogDebug("{Session}: Session management started.", this);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await WorkerWaitAsync(ct).ConfigureAwait(false);

                    if (!await PingServerAsync(ct).ConfigureAwait(false))
                    {
                        OnStateChange(SessionState.Disconnected, ServiceResult.Good);
                    }
                    else
                    {
                        OnStateChange(SessionState.Connected, ServiceResult.Good);
                    }
                }
            }
            catch (OperationCanceledException) { }

            _logger.LogDebug("{Session}: Session manager exits.", this);
        }

        /// <summary>
        /// Ping server and return true if successful or not. Used as keep alive.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask<bool> PingServerAsync(CancellationToken ct)
        {
            var keepAliveInterval = Options.KeepAliveInterval ?? kDefaultKeepAliveInterval;
            try
            {
                var serverState = await FetchValueAsync(new RequestHeader
                {
                    RequestHandle = Utils.IncrementIdentifier(ref _keepAliveCounter),
                    TimeoutHint = (uint)(keepAliveInterval.TotalMilliseconds * 2),
                    ReturnDiagnostics = 0
                }, VariableIds.Server_ServerStatus_State, ct).ConfigureAwait(false);

                if ((object)serverState.WrappedValue is not int and not ServerState)
                {
                    throw ServiceResultException.Create(StatusCodes.BadDataUnavailable,
                        "Keep alive returned invalid server state");
                }
                LastKeepAliveTimestamp = TimeProvider.System.GetTimestamp();
                return true;
            }
            catch (Exception e)
            {
                ct.ThrowIfCancellationRequested();
                var sr = new ServiceResult(e);
                if (sr.StatusCode == StatusCodes.BadNoCommunication)
                {
                    // keep alive read timed out
                    var delta =
                        TimeProvider.System.GetElapsedTime(LastKeepAliveTimestamp);
                    _logger.LogInformation(
                        "{Session}: KEEP ALIVE LATE: {Late} for EndpointUrl={Url}",
                        this, delta, Endpoint?.EndpointUrl);

                    // add a guard band to allow for network lag.
                    if (keepAliveInterval + kKeepAliveGuardBand > delta)
                    {
                        return true;
                    }
                }
                // another error was reported which caused keep alive to stop.
                return false;
            }
        }

        /// <summary>
        /// Trigger session worker
        /// </summary>
        /// <returns></returns>
        internal void TriggerWorker()
        {
            _trigger.Set();
        }

        /// <summary>
        /// Get desired session timeout
        /// </summary>
        /// <param name="options"></param>
        /// <returns></returns>
        private TimeSpan GetSessionTimeout(SessionCreateOptions options)
        {
            var sessionTimeout = options.SessionTimeout;
            if (sessionTimeout.HasValue &&
                sessionTimeout != TimeSpan.Zero)
            {
                return sessionTimeout.Value;
            }
            return TimeSpan.FromMilliseconds(
                _configuration.ClientConfiguration.DefaultSessionTimeout);
        }

        /// <summary>
        /// Starts a timer to check that the connection to the server
        /// is still available.
        /// </summary>
        private void ResetKeepAliveTimer()
        {
            LastKeepAliveTimestamp = TimeProvider.System.GetTimestamp();
            var keepAliveInterval = Options.KeepAliveInterval ?? kDefaultKeepAliveInterval;
            _keepAliveTimer.Change(keepAliveInterval, keepAliveInterval);
        }

        /// <summary>
        /// Stops the keep alive timer.
        /// </summary>
        private void StopKeepAliveTimer()
        {
            _keepAliveTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Creates a Node based on the read response.
        /// </summary>
        /// <param name="responseHeader"></param>
        /// <param name="attributes"></param>
        /// <param name="itemsToRead"></param>
        /// <param name="values"></param>
        /// <param name="diagnosticInfos"></param>
        /// <exception cref="ServiceResultException"></exception>
        private static Node ProcessReadResponse(ResponseHeader responseHeader,
            IDictionary<uint, DataValue?> attributes, ArrayOf<ReadValueId> itemsToRead,
            ArrayOf<DataValue> values, ArrayOf<DiagnosticInfo> diagnosticInfos)
        {
            // process results.
            var nodeClass = 0;
            for (var index = 0; index < itemsToRead.Count; index++)
            {
                var attributeId = itemsToRead[index].AttributeId;

                // the node probably does not exist if the node class is not found.
                if (attributeId == Attributes.NodeClass)
                {
                    if (!DataValue.IsGood(values[index]))
                    {
                        throw ServiceResultException.Create(values[index].StatusCode,
                            index, diagnosticInfos, responseHeader.StringTable);
                    }

                    // check for valid node class.
                    if ((object)values[index].WrappedValue is not int and not NodeClass)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "Node does not have a valid value for NodeClass: {0}.",
                            values[index].WrappedValue);
                    }
                    nodeClass = (int)(object)values[index].WrappedValue;
                }
                else
                {
                    if (!DataValue.IsGood(values[index]))
                    {
                        // check for unsupported attributes.
                        if (values[index].StatusCode == StatusCodes.BadAttributeIdInvalid)
                        {
                            continue;
                        }

                        // ignore errors on optional attributes
                        if (StatusCode.IsBad(values[index].StatusCode) &&
                            attributeId is
                                Attributes.AccessRestrictions or
                                Attributes.Description or
                                Attributes.RolePermissions or
                                Attributes.UserRolePermissions or
                                Attributes.UserWriteMask or
                                Attributes.WriteMask or
                                Attributes.AccessLevelEx or
                                Attributes.ArrayDimensions or
                                Attributes.DataTypeDefinition or
                                Attributes.InverseName or
                                Attributes.MinimumSamplingInterval)
                        {
                            continue;
                        }

                        // all supported attributes must be readable.
                        if (attributeId != Attributes.Value)
                        {
                            throw ServiceResultException.Create(values[index].StatusCode,
                                index, diagnosticInfos, responseHeader.StringTable);
                        }
                    }
                }
                attributes[attributeId] = values[index];
            }

            Node node;
            DataValue? value;
            switch ((NodeClass)nodeClass)
            {
                case NodeClass.Object:
                    var objectNode = new ObjectNode();
                    value = attributes[Attributes.EventNotifier];

                    if (value == null || value.WrappedValue.IsNull)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "Object does not support the EventNotifier attribute.");
                    }

                    objectNode.EventNotifier = (byte)(object)value.WrappedValue;
                    node = objectNode;
                    break;
                case NodeClass.ObjectType:
                    var objectTypeNode = new ObjectTypeNode();
                    value = attributes[Attributes.IsAbstract];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "ObjectType does not support the IsAbstract attribute.");
                    }

                    objectTypeNode.IsAbstract = (bool)(object)value.WrappedValue;
                    node = objectTypeNode;
                    break;
                case NodeClass.Variable:
                    var variableNode = new VariableNode();
                    // DataType Attribute
                    value = attributes[Attributes.DataType];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "Variable does not support the DataType attribute.");
                    }

                    variableNode.DataType = (NodeId)(object)value.WrappedValue;
                    // ValueRank Attribute
                    value = attributes[Attributes.ValueRank];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "Variable does not support the ValueRank attribute.");
                    }

                    variableNode.ValueRank = (int)(object)value.WrappedValue;

                    // ArrayDimensions Attribute
                    value = attributes[Attributes.ArrayDimensions];

                    if (value != null)
                    {
                        if (value.WrappedValue.IsNull)
                        {
                            variableNode.ArrayDimensions = Array.Empty<uint>();
                        }
                        else
                        {
                            variableNode.ArrayDimensions = (uint[])(object)value.WrappedValue;
                        }
                    }

                    // AccessLevel Attribute
                    value = attributes[Attributes.AccessLevel];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "Variable does not support the AccessLevel attribute.");
                    }

                    variableNode.AccessLevel = (byte)(object)value.WrappedValue;

                    // UserAccessLevel Attribute
                    value = attributes[Attributes.UserAccessLevel];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "Variable does not support the UserAccessLevel attribute.");
                    }

                    variableNode.UserAccessLevel = (byte)(object)value.WrappedValue;

                    // Historizing Attribute
                    value = attributes[Attributes.Historizing];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "Variable does not support the Historizing attribute.");
                    }

                    variableNode.Historizing = (bool)(object)value.WrappedValue;

                    // MinimumSamplingInterval Attribute
                    value = attributes[Attributes.MinimumSamplingInterval];

                    if (value != null)
                    {
                        variableNode.MinimumSamplingInterval = Convert.ToDouble(
                            (object?)attributes[Attributes.MinimumSamplingInterval]?.WrappedValue,
                            CultureInfo.InvariantCulture);
                    }

                    // AccessLevelEx Attribute
                    value = attributes[Attributes.AccessLevelEx];

                    if (value != null)
                    {
                        variableNode.AccessLevelEx = (uint)(object)value.WrappedValue;
                    }

                    node = variableNode;
                    break;
                case NodeClass.VariableType:
                    var variableTypeNode = new VariableTypeNode();

                    // IsAbstract Attribute
                    value = attributes[Attributes.IsAbstract];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "VariableType does not support the IsAbstract attribute.");
                    }

                    variableTypeNode.IsAbstract = (bool)(object)value.WrappedValue;

                    // DataType Attribute
                    value = attributes[Attributes.DataType];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "VariableType does not support the DataType attribute.");
                    }

                    variableTypeNode.DataType = (NodeId)(object)value.WrappedValue;

                    // ValueRank Attribute
                    value = attributes[Attributes.ValueRank];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "VariableType does not support the ValueRank attribute.");
                    }

                    variableTypeNode.ValueRank = (int)(object)value.WrappedValue;

                    // ArrayDimensions Attribute
                    value = attributes[Attributes.ArrayDimensions];

                    if (value != null && !value.WrappedValue.IsNull)
                    {
                        variableTypeNode.ArrayDimensions = (uint[])(object)value.WrappedValue;
                    }
                    node = variableTypeNode;
                    break;
                case NodeClass.Method:
                    var methodNode = new MethodNode();
                    // Executable Attribute
                    value = attributes[Attributes.Executable];
                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "Method does not support the Executable attribute.");
                    }

                    methodNode.Executable = (bool)(object)value.WrappedValue;

                    // UserExecutable Attribute
                    value = attributes[Attributes.UserExecutable];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "Method does not support the UserExecutable attribute.");
                    }

                    methodNode.UserExecutable = (bool)(object)value.WrappedValue;
                    node = methodNode;
                    break;
                case NodeClass.DataType:
                    var dataTypeNode = new DataTypeNode();

                    // IsAbstract Attribute
                    value = attributes[Attributes.IsAbstract];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "DataType does not support the IsAbstract attribute.");
                    }

                    dataTypeNode.IsAbstract = (bool)(object)value.WrappedValue;

                    // DataTypeDefinition Attribute
                    value = attributes[Attributes.DataTypeDefinition];

                    if (value != null && !value.WrappedValue.IsNull)
                    {
                        dataTypeNode.DataTypeDefinition = (ExtensionObject)(object)value.WrappedValue;
                    }

                    node = dataTypeNode;
                    break;
                case NodeClass.ReferenceType:
                    var referenceTypeNode = new ReferenceTypeNode();
                    // IsAbstract Attribute
                    value = attributes[Attributes.IsAbstract];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "ReferenceType does not support the IsAbstract attribute.");
                    }

                    referenceTypeNode.IsAbstract = (bool)(object)value.WrappedValue;

                    // Symmetric Attribute
                    value = attributes[Attributes.Symmetric];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "ReferenceType does not support the Symmetric attribute.");
                    }

                    referenceTypeNode.Symmetric = (bool)(object)value.WrappedValue;

                    // InverseName Attribute
                    value = attributes[Attributes.InverseName];

                    if (value != null && !value.WrappedValue.IsNull)
                    {
                        referenceTypeNode.InverseName =
                            (LocalizedText)(object)value.WrappedValue;
                    }

                    node = referenceTypeNode;
                    break;
                case NodeClass.View:
                    var viewNode = new ViewNode();
                    // EventNotifier Attribute
                    value = attributes[Attributes.EventNotifier];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "View does not support the EventNotifier attribute.");
                    }

                    viewNode.EventNotifier = (byte)(object)value.WrappedValue;

                    // ContainsNoLoops Attribute
                    value = attributes[Attributes.ContainsNoLoops];

                    if (value == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                            "View does not support the ContainsNoLoops attribute.");
                    }

                    viewNode.ContainsNoLoops = (bool)(object)value.WrappedValue;
                    node = viewNode;
                    break;
                default:
                    throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                        "Node does not have a valid value for NodeClass: {0}.", nodeClass);
            }

            // NodeId Attribute
            value = attributes[Attributes.NodeId];
            if (value == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                    "Node does not support the NodeId attribute.");
            }

            node.NodeId = (NodeId)(object)value.WrappedValue;
            node.NodeClass = (NodeClass)nodeClass;

            // BrowseName Attribute
            value = attributes[Attributes.BrowseName];
            if (value == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                    "Node does not support the BrowseName attribute.");
            }

            node.BrowseName = (QualifiedName)(object)value.WrappedValue;

            // DisplayName Attribute
            value = attributes[Attributes.DisplayName];
            if (value == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                    "Node does not support the DisplayName attribute.");
            }
            node.DisplayName = (LocalizedText)(object)value.WrappedValue;

            // all optional attributes follow
            // Description Attribute
            if (attributes.TryGetValue(Attributes.Description, out value) &&
                value != null && !value.WrappedValue.IsNull)
            {
                node.Description = (LocalizedText)(object)value.WrappedValue;
            }

            // WriteMask Attribute
            if (attributes.TryGetValue(Attributes.WriteMask, out value) &&
                value != null)
            {
                node.WriteMask = (uint)(object)value.WrappedValue;
            }

            // UserWriteMask Attribute
            if (attributes.TryGetValue(Attributes.UserWriteMask, out value) &&
                value != null)
            {
                node.UserWriteMask = (uint)(object)value.WrappedValue;
            }

            // RolePermissions Attribute
            if (attributes.TryGetValue(Attributes.RolePermissions, out value) &&
                value != null)
            {
                if ((object)value.WrappedValue is ExtensionObject[] rolePermissions)
                {
                    var rpList = new List<RolePermissionType>();
                    foreach (var rolePermission in rolePermissions)
                    {
                        if (rolePermission.TryGetEncodeable(out IEncodeable encodeable) &&
                            encodeable is RolePermissionType rpt)
                        {
                            rpList.Add(rpt);
                        }
                    }
                    node.RolePermissions = rpList.ToArrayOf();
                }
            }

            // UserRolePermissions Attribute
            if (attributes.TryGetValue(Attributes.UserRolePermissions, out value) &&
                value != null)
            {
                if ((object)value.WrappedValue is ExtensionObject[] userRolePermissions)
                {
                    var urpList = new List<RolePermissionType>();
                    foreach (var rolePermission in userRolePermissions)
                    {
                        if (rolePermission.TryGetEncodeable(out IEncodeable encodeable) &&
                            encodeable is RolePermissionType rpt)
                        {
                            urpList.Add(rpt);
                        }
                    }
                    node.UserRolePermissions = urpList.ToArrayOf();
                }
            }

            // AccessRestrictions Attribute
            if (attributes.TryGetValue(Attributes.AccessRestrictions, out value) &&
                value != null)
            {
                node.AccessRestrictions = (ushort)(object)value.WrappedValue;
            }
            return node;
        }

        /// <summary>
        /// Create a dictionary of attributes to read for a nodeclass.
        /// </summary>
        /// <param name="nodeclass"></param>
        /// <param name="optionalAttributes"></param>
        private static SortedDictionary<uint, DataValue?> CreateAttributes(
            NodeClass nodeclass = NodeClass.Unspecified, bool optionalAttributes = true)
        {
            // Attributes to read for all types of nodes
            var attributes = new SortedDictionary<uint, DataValue?>()
            {
                { Attributes.NodeId, null },
                { Attributes.NodeClass, null },
                { Attributes.BrowseName, null },
                { Attributes.DisplayName, null }
            };
            switch (nodeclass)
            {
                case NodeClass.Object:
                    attributes.Add(Attributes.EventNotifier, null);
                    break;
                case NodeClass.Variable:
                    attributes.Add(Attributes.DataType, null);
                    attributes.Add(Attributes.ValueRank, null);
                    attributes.Add(Attributes.ArrayDimensions, null);
                    attributes.Add(Attributes.AccessLevel, null);
                    attributes.Add(Attributes.UserAccessLevel, null);
                    attributes.Add(Attributes.Historizing, null);
                    attributes.Add(Attributes.MinimumSamplingInterval, null);
                    attributes.Add(Attributes.AccessLevelEx, null);
                    break;
                case NodeClass.Method:
                    attributes.Add(Attributes.Executable, null);
                    attributes.Add(Attributes.UserExecutable, null);
                    break;
                case NodeClass.ObjectType:
                    attributes.Add(Attributes.IsAbstract, null);
                    break;
                case NodeClass.VariableType:
                    attributes.Add(Attributes.IsAbstract, null);
                    attributes.Add(Attributes.DataType, null);
                    attributes.Add(Attributes.ValueRank, null);
                    attributes.Add(Attributes.ArrayDimensions, null);
                    break;
                case NodeClass.ReferenceType:
                    attributes.Add(Attributes.IsAbstract, null);
                    attributes.Add(Attributes.Symmetric, null);
                    attributes.Add(Attributes.InverseName, null);
                    break;
                case NodeClass.DataType:
                    attributes.Add(Attributes.IsAbstract, null);
                    attributes.Add(Attributes.DataTypeDefinition, null);
                    break;
                case NodeClass.View:
                    attributes.Add(Attributes.EventNotifier, null);
                    attributes.Add(Attributes.ContainsNoLoops, null);
                    break;
                default:
                    attributes.Add(Attributes.DataType, null);
                    attributes.Add(Attributes.ValueRank, null);
                    attributes.Add(Attributes.ArrayDimensions, null);
                    attributes.Add(Attributes.AccessLevel, null);
                    attributes.Add(Attributes.UserAccessLevel, null);
                    attributes.Add(Attributes.MinimumSamplingInterval, null);
                    attributes.Add(Attributes.Historizing, null);
                    attributes.Add(Attributes.EventNotifier, null);
                    attributes.Add(Attributes.Executable, null);
                    attributes.Add(Attributes.UserExecutable, null);
                    attributes.Add(Attributes.IsAbstract, null);
                    attributes.Add(Attributes.InverseName, null);
                    attributes.Add(Attributes.Symmetric, null);
                    attributes.Add(Attributes.ContainsNoLoops, null);
                    attributes.Add(Attributes.DataTypeDefinition, null);
                    attributes.Add(Attributes.AccessLevelEx, null);
                    break;
            }
            if (optionalAttributes)
            {
                attributes.Add(Attributes.Description, null);
                attributes.Add(Attributes.WriteMask, null);
                attributes.Add(Attributes.UserWriteMask, null);
                attributes.Add(Attributes.RolePermissions, null);
                attributes.Add(Attributes.UserRolePermissions, null);
                attributes.Add(Attributes.AccessRestrictions, null);
            }
            return attributes;
        }

        /// <summary>
        /// Creates a secure channel to the specified endpoint.
        /// </summary>
        /// <param name="ct">The cancellation token.</param>
        /// <returns></returns>
        private async ValueTask<ITransportChannel> CreateChannelAsync(CancellationToken ct)
        {
            var endpoint = ConfiguredEndpoint;

            // update endpoint description using the discovery endpoint.
            if (_connection == null && (_updateFromServer || endpoint.UpdateBeforeConnect))
            {
                await endpoint.UpdateFromServerAsync((Opc.Ua.ITelemetryContext?)null, ct).ConfigureAwait(false);
                _updateFromServer = false;
            }

            // checks the domains in the certificate.
            if (Options.CheckDomain &&
                endpoint.Description.ServerCertificate.Length > 0)
            {
                using var cert = X509CertificateLoader.LoadCertificate(
                    endpoint.Description.ServerCertificate.ToArray());
                _configuration.CertificateValidator?.ValidateDomains(cert, endpoint);
            }

            if (endpoint.Description.SecurityPolicyUri != SecurityPolicies.None)
            {
                await CheckCertificatesAreLoadedAsync(ct).ConfigureAwait(false);
            }

            var clientCertificate = _clientCertificate;
            var clientCertificateChain =
                _configuration.SecurityConfiguration.SendCertificateChain ?
                _clientCertificateChain : null;

            return _channelFactory.CreateChannel(endpoint, MessageContext,
                clientCertificate, clientCertificateChain);
        }

        /// <summary>
        /// Create a connection using reverse connect manager if configured.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async ValueTask WaitForReverseConnectIfNeededAsync(CancellationToken ct)
        {
            if (_reverseConnect == null
                // || ConfiguredEndpoint.ReverseConnect?.Enabled != true
                )
            {
                return;
            }
            var endpoint = ConfiguredEndpoint;
            var updateFromEndpoint = endpoint.UpdateBeforeConnect || _updateFromServer;

            _connection ??= Options.Connection;
            while (!IsConnected(_connection))
            {
                ct.ThrowIfCancellationRequested();
                _connection = await _reverseConnect.WaitForConnectionAsync(
                    endpoint.EndpointUrl, endpoint.ReverseConnect?.ServerUri,
                    ct).ConfigureAwait(false);
                if (updateFromEndpoint)
                {
                    await endpoint.UpdateFromServerAsync(endpoint.EndpointUrl,
                        _connection, endpoint.Description.SecurityMode,
                        endpoint.Description.SecurityPolicyUri,
                        (Opc.Ua.ITelemetryContext?)null, ct).ConfigureAwait(false);
                    _updateFromServer = updateFromEndpoint = false;
                }
            }

            static bool IsConnected(ITransportWaitingConnection? connection)
            {
                var socket = connection?.Handle as Bindings.IMessageSocket;
                return socket?.RemoteEndpoint != null;
            }
        }

        /// <summary>
        /// Validates the server certificate returned.
        /// </summary>
        /// <param name="serverCertificateData"></param>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateServerCertificateData(byte[] serverCertificateData)
        {
            if (serverCertificateData != null &&
                !ConfiguredEndpoint.Description.ServerCertificate.IsNull &&
                !Utils.IsEqual(serverCertificateData, ConfiguredEndpoint.Description.ServerCertificate.ToArray()))
            {
                try
                {
                    // verify for certificate chain in endpoint.
                    var serverCertificateChain = Utils.ParseCertificateChainBlob(
                        ConfiguredEndpoint.Description.ServerCertificate, (Opc.Ua.ITelemetryContext?)null);

                    if (serverCertificateChain.Count > 0 && !Utils.IsEqual(
                        serverCertificateData, serverCertificateChain[0].RawData))
                    {
                        throw ServiceResultException.Create(StatusCodes.BadCertificateInvalid,
                            "Server did not return the certificate used to create the secure channel.");
                    }
                }
                catch (Exception)
                {
                    throw ServiceResultException.Create(StatusCodes.BadCertificateInvalid,
                        "Server did not return the certificate used to create the secure channel.");
                }
            }
        }

        /// <summary>
        /// Validates the server signature created with the client nonce.
        /// </summary>
        /// <param name="serverCertificate"></param>
        /// <param name="serverSignature"></param>
        /// <param name="clientCertificateData"></param>
        /// <param name="clientCertificateChainData"></param>
        /// <param name="clientNonce"></param>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateServerSignature(X509Certificate2? serverCertificate,
            SignatureData? serverSignature, byte[]? clientCertificateData,
            byte[]? clientCertificateChainData, byte[] clientNonce)
        {
            if (serverSignature == null || serverSignature.Signature.IsNull)
            {
                _logger.LogInformation("{Session}: Server signature is null or empty.",
                    this);

                //throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed,
                //    "Server signature is null or empty.");
            }

            // validate the server's signature.
            var dataToSign = Utils.Append(clientCertificateData, clientNonce);

            if (SecurityPolicies.VerifySignatureData(serverSignature,
                ConfiguredEndpoint.Description.SecurityPolicyUri, serverCertificate,
                dataToSign))
            {
                return;
            }

            // validate the signature with complete chain if the check with
            // leaf certificate failed.
            if (clientCertificateChainData == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadApplicationSignatureInvalid,
                   "Server did not provide a correct signature for the nonce data " +
                   "provided by the client.");
            }

            dataToSign = Utils.Append(clientCertificateChainData, clientNonce);

            if (!SecurityPolicies.VerifySignatureData(serverSignature,
                ConfiguredEndpoint.Description.SecurityPolicyUri, serverCertificate,
                dataToSign))
            {
                throw ServiceResultException.Create(StatusCodes.BadApplicationSignatureInvalid,
                    "Server did not provide a correct signature for the nonce " +
                    "data provided by the client.");
            }
        }

        /// <summary>
        /// Get user token policy from token
        /// </summary>
        /// <param name="identityToken"></param>
        /// <returns></returns>
        /// <exception cref="ServiceResultException"></exception>
        private UserTokenPolicy GetIdentityPolicyFromToken(UserIdentityToken identityToken)
        {
            var identityPolicy = ConfiguredEndpoint.Description.FindUserTokenPolicy(
                identityToken.PolicyId, ConfiguredEndpoint.Description.SecurityPolicyUri);
            if (identityPolicy != null)
            {
                return identityPolicy;
            }
            // try looking up by TokenType if the policy id was not found.
            identityPolicy = ConfiguredEndpoint.Description.FindUserTokenPolicy(
                Identity.TokenType, Identity.IssuedTokenType,
                ConfiguredEndpoint.Description.SecurityPolicyUri);
            if (identityPolicy != null)
            {
                identityToken.PolicyId = identityPolicy.PolicyId;
                return identityPolicy;
            }
            throw ServiceResultException.Create(StatusCodes.BadUserAccessDenied,
                "Endpoint does not support the user identity type provided.");
        }

        /// <summary>
        /// Validates the server endpoints returned.
        /// </summary>
        /// <param name="serverEndpoints"></param>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateServerEndpoints(ArrayOf<EndpointDescription> serverEndpoints)
        {
            var options = Options;
            var discoveryServerEndpoints = options.AvailableEndpoints;
            var discoveryProfileUris = options.DiscoveryProfileUris;
            if (discoveryServerEndpoints?.Count > 0)
            {
                // Compare EndpointDescriptions returned at GetEndpoints with values
                // returned at CreateSession
                ArrayOf<EndpointDescription> expectedServerEndpoints;
                if (discoveryProfileUris?.Count > 0)
                {
                    // Select EndpointDescriptions with a transportProfileUri that matches the
                    // profileUris specified in the original GetEndpoints() request.
                    var expectedList = new List<EndpointDescription>();

                    foreach (var serverEndpoint in serverEndpoints)
                    {
                        var profileMatch = false;
                        for (var j = 0; j < discoveryProfileUris.Value.Count; j++)
                        {
                            if (discoveryProfileUris.Value[j] == serverEndpoint.TransportProfileUri)
                            {
                                profileMatch = true;
                                break;
                            }
                        }
                        if (profileMatch)
                        {
                            expectedList.Add(serverEndpoint);
                        }
                    }
                    expectedServerEndpoints = expectedList.ToArrayOf();
                }
                else
                {
                    expectedServerEndpoints = serverEndpoints;
                }

                if (discoveryServerEndpoints.Value.Count != expectedServerEndpoints.Count)
                {
                    throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed,
                        "Server did not return a number of ServerEndpoints that matches the " +
                        "one from GetEndpoints.");
                }

                for (var index = 0; index < expectedServerEndpoints.Count; index++)
                {
                    var serverEndpoint = expectedServerEndpoints[index];
                    var expectedServerEndpoint = discoveryServerEndpoints.Value[index];

                    if (serverEndpoint.SecurityMode != expectedServerEndpoint.SecurityMode ||
                        serverEndpoint.SecurityPolicyUri != expectedServerEndpoint.SecurityPolicyUri ||
                        serverEndpoint.TransportProfileUri != expectedServerEndpoint.TransportProfileUri ||
                        serverEndpoint.SecurityLevel != expectedServerEndpoint.SecurityLevel)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed,
                            "The list of ServerEndpoints returned at CreateSession does not match " +
                            "the list from GetEndpoints.");
                    }

                    if (serverEndpoint.UserIdentityTokens.Count !=
                        expectedServerEndpoint.UserIdentityTokens.Count)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed,
                            "The list of ServerEndpoints returned at CreateSession does not match " +
                            "the one from GetEndpoints.");
                    }

                    for (var i = 0; i < serverEndpoint.UserIdentityTokens.Count; i++)
                    {
                        if (!serverEndpoint.UserIdentityTokens[i].IsEqual(
                            expectedServerEndpoint.UserIdentityTokens[i]))
                        {
                            throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed,
                            "The list of ServerEndpoints returned at CreateSession does not match" +
                            " the one from GetEndpoints.");
                        }
                    }
                }
            }

            // find the matching description (TBD - check domains against certificate).
            var found = false;

            var foundDescription = Find(serverEndpoints,
                ConfiguredEndpoint.Description, true);
            if (foundDescription != null)
            {
                found = true;
                // ensure endpoint has up to date information.
                Update(ConfiguredEndpoint.Description, foundDescription);
            }
            else
            {
                foundDescription = Find(serverEndpoints,
                    ConfiguredEndpoint.Description, false);
                if (foundDescription != null)
                {
                    found = true;
                    // ensure endpoint has up to date information.
                    Update(ConfiguredEndpoint.Description, foundDescription);
                }
            }

            // could be a security risk.
            if (!found)
            {
                throw ServiceResultException.Create(StatusCodes.BadSecurityChecksFailed,
                    "Server did not return an EndpointDescription that matched the one " +
                    "used to create the secure channel.");
            }

            EndpointDescription? Find(ArrayOf<EndpointDescription> endpointDescriptions,
                EndpointDescription match, bool matchPort)
            {
                var expectedUrl = Utils.ParseUri(match.EndpointUrl);
                for (var index = 0; index < endpointDescriptions.Count; index++)
                {
                    var serverEndpoint = endpointDescriptions[index];
                    var actualUrl = Utils.ParseUri(serverEndpoint.EndpointUrl);

                    if (actualUrl != null &&
                        actualUrl.Scheme == expectedUrl.Scheme &&
                        (!matchPort || actualUrl.Port == expectedUrl.Port) &&
                        serverEndpoint.SecurityPolicyUri ==
                            ConfiguredEndpoint.Description.SecurityPolicyUri &&
                        serverEndpoint.SecurityMode == ConfiguredEndpoint.Description.SecurityMode)
                    {
                        return serverEndpoint;
                    }
                }
                return null;
            }

            static void Update(EndpointDescription target, EndpointDescription source)
            {
                target.Server.ApplicationName = source.Server.ApplicationName;
                target.Server.ApplicationUri = source.Server.ApplicationUri;
                target.Server.ApplicationType = source.Server.ApplicationType;
                target.Server.ProductUri = source.Server.ProductUri;
                target.TransportProfileUri = source.TransportProfileUri;
                target.UserIdentityTokens = source.UserIdentityTokens;
            }
        }

        /// <summary>
        /// Validates the server nonce and security parameters of user identity.
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="serverNonce"></param>
        /// <param name="securityPolicyUri"></param>
        /// <param name="previousServerNonce"></param>
        /// <param name="channelSecurityMode"></param>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateServerNonce(IUserIdentity identity, byte[] serverNonce,
            string securityPolicyUri, byte[] previousServerNonce,
            MessageSecurityMode channelSecurityMode = MessageSecurityMode.None)
        {
            // skip validation if server nonce is not used for encryption.
            if (string.IsNullOrEmpty(securityPolicyUri) ||
                securityPolicyUri == SecurityPolicies.None)
            {
                return;
            }

            if (identity == null || identity.TokenType == UserTokenType.Anonymous)
            {
                return;
            }

            // the server nonce should be validated if the token includes a secret.
            if (!Nonce.ValidateNonce(serverNonce,
                MessageSecurityMode.SignAndEncrypt,
                (int)_configuration.SecurityConfiguration.NonceLength))
            {
                if (channelSecurityMode == MessageSecurityMode.SignAndEncrypt ||
                    _configuration.SecurityConfiguration.SuppressNonceValidationErrors)
                {
                    _logger.LogWarning(
                        "{Session}: The server nonce has not the correct length or " +
                        "is not random enough. The error is suppressed by user setting " +
                        "or because the channel is encrypted.", this);
                }
                else
                {
                    throw ServiceResultException.Create(StatusCodes.BadNonceInvalid,
                        "The server nonce has not the correct length or is not " +
                        "random enough.");
                }
            }

            // check that new nonce is different from the previously returned
            // server nonce.
            if (previousServerNonce != null &&
                Nonce.CompareNonce(serverNonce, previousServerNonce))
            {
                if (channelSecurityMode == MessageSecurityMode.SignAndEncrypt ||
                    _configuration.SecurityConfiguration.SuppressNonceValidationErrors)
                {
                    _logger.LogWarning(
                        "{Session}: The Server nonce is equal with previously returned " +
                        "nonce. The error is suppressed by user setting or because the " +
                        "channel is encrypted.", this);
                }
                else
                {
                    throw ServiceResultException.Create(StatusCodes.BadNonceInvalid,
                        "Server nonce is equal with previously returned nonce.");
                }
            }
        }

        /// <summary>
        /// Ensure the certificate and certificate chain are loaded if needed.
        /// </summary>
        /// <param name="ct"></param>
        /// <exception cref="ServiceResultException"></exception>
        private async Task CheckCertificatesAreLoadedAsync(CancellationToken ct)
        {
            if (ConfiguredEndpoint.Description.SecurityPolicyUri != SecurityPolicies.None)
            {
                // update client certificate.
                if (_clientCertificate?.HasPrivateKey != true ||
                    _clientCertificate.NotAfter < DateTime.UtcNow)
                {
                    // load the application instance certificate.
                    var cert = _configuration.SecurityConfiguration.ApplicationCertificate ?? throw new ServiceResultException(StatusCodes.BadConfigurationError,
                            "The client configuration does not specify an application " +
                            "instance certificate.");
                    ct.ThrowIfCancellationRequested();
                    _clientCertificate = await cert.FindAsync(true).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();

                    // check for valid certificate.
                    if (_clientCertificate == null)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                            "Cannot find the application instance certificate. " +
                            "Store={0}, SubjectName={1}, Thumbprint={2}.",
                            cert.StorePath, cert.SubjectName, cert.Thumbprint);
                    }
                    // check for private key.
                    if (!_clientCertificate.HasPrivateKey)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                            "No private key for the application instance certificate. " +
                            "Subject={0}, Thumbprint={1}.",
                            _clientCertificate.Subject, _clientCertificate.Thumbprint);
                    }
                    if (_clientCertificate.NotAfter < DateTime.UtcNow)
                    {
                        throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                             "Application instance certificate has expired. " +
                             "Store={0}, SubjectName={1}, Thumbprint={2}.",
                             cert.StorePath, cert.SubjectName, cert.Thumbprint);
                    }
                    _clientCertificateChain = null;
                }

                if (_clientCertificateChain == null)
                {
                    // load certificate chain.
                    _clientCertificateChain = new X509Certificate2Collection(_clientCertificate);
                    var issuers = new List<CertificateIdentifier>();
                    await _configuration.CertificateValidator.GetIssuersAsync(_clientCertificate,
                        issuers).ConfigureAwait(false);

                    for (var i = 0; i < issuers.Count; i++)
                    {
                        _clientCertificateChain.Add(issuers[i].Certificate);
                    }
                }
            }
        }

        private X509Certificate2? _clientCertificate;
        private X509Certificate2Collection? _clientCertificateChain;
        private X509Certificate2? _serverCertificate;
        private uint _maxRequestMessageSize;
        private uint _keepAliveCounter;
        private ITransportWaitingConnection? _connection;
        private int _namespaceTableChanges;
        private int _serverTableChanges;
        private bool _disposeAsyncCalled;
        private readonly Task _sessionWorker;
        private readonly Nito.AsyncEx.AsyncAutoResetEvent _trigger = new();
        private readonly ReverseConnectManager? _reverseConnect;
        private readonly IChannelFactory _channelFactory;
        private readonly ApplicationConfiguration _configuration;
        private readonly ITimer _keepAliveTimer;
        private readonly CancellationTokenSource _cts = new();
        private readonly SessionSystemContext _systemContext;
        private readonly SubscriptionManager _subscriptions;
        private readonly SemaphoreSlim _connecting = new(1, 1);
        private readonly NodeCache _nodeCache;
        private byte[] _previousServerNonce = [];
        internal byte[] _serverNonce = [];
#pragma warning disable IDE1006 // Naming Styles
        internal readonly ILogger _logger;
        internal readonly Meter _meter;
        internal bool _updateFromServer;
#pragma warning restore IDE1006 // Naming Styles

        private static readonly TimeSpan kDefaultOperationTimeout = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan kDefaultKeepAliveInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan kKeepAliveGuardBand = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan kReconnectTimeout = TimeSpan.FromSeconds(15);
        private readonly IEncodeableFactory _factory;

        #region INodeCacheContext explicit implementations

        /// <inheritdoc/>
        async ValueTask<global::Opc.Ua.ResultSet<Node>> INodeCacheContext.FetchNodesAsync(
            RequestHeader? requestHeader, ArrayOf<NodeId> nodeIds,
            bool skipOptionalAttributes, CancellationToken ct)
        {
            var result = await FetchNodesAsync(requestHeader, nodeIds.ToList(), ct)
                .ConfigureAwait(false);
            return new global::Opc.Ua.ResultSet<Node>(
                result.Results.ToArray(),
                result.Errors.ToArray());
        }

        /// <inheritdoc/>
        async ValueTask<global::Opc.Ua.ResultSet<Node>> INodeCacheContext.FetchNodesAsync(
            RequestHeader? requestHeader, ArrayOf<NodeId> nodeIds,
            NodeClass nodeClass, bool skipOptionalAttributes, CancellationToken ct)
        {
            var result = await FetchNodesAsync(requestHeader, nodeIds.ToList(), ct)
                .ConfigureAwait(false);
            return new global::Opc.Ua.ResultSet<Node>(
                result.Results.ToArray(),
                result.Errors.ToArray());
        }

        /// <inheritdoc/>
        ValueTask<Node> INodeCacheContext.FetchNodeAsync(
            RequestHeader? requestHeader, NodeId nodeId,
            NodeClass nodeClass, bool skipOptionalAttributes, CancellationToken ct)
        {
            return FetchNodeAsync(requestHeader, nodeId, ct);
        }

        /// <inheritdoc/>
        async ValueTask<global::Opc.Ua.ResultSet<ArrayOf<ReferenceDescription>>>
            INodeCacheContext.FetchReferencesAsync(
            RequestHeader? requestHeader, ArrayOf<NodeId> nodeIds, CancellationToken ct)
        {
            var result = await FetchReferencesAsync(requestHeader, nodeIds.ToList(), ct)
                .ConfigureAwait(false);
            return new global::Opc.Ua.ResultSet<ArrayOf<ReferenceDescription>>(
                result.Results.ToArray(),
                result.Errors.ToArray());
        }

        /// <inheritdoc/>
        async ValueTask<global::Opc.Ua.ResultSet<DataValue>> INodeCacheContext.FetchValuesAsync(
            RequestHeader? requestHeader, ArrayOf<NodeId> nodeIds, CancellationToken ct)
        {
            var result = await FetchValuesAsync(requestHeader, nodeIds.ToList(), ct)
                .ConfigureAwait(false);
            return new global::Opc.Ua.ResultSet<DataValue>(
                result.Results.ToArray(),
                result.Errors.ToArray());
        }

        #endregion
    }
}
#endif
