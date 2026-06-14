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

#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Rest
{
    /// <summary>
    /// Concrete session-bound wrapper for <see cref="IRestApiClient"/>.
    /// </summary>
    public sealed class RestApiSession : IRestApiSession
    {
        private readonly IRestApiClient m_client;
        private readonly RestApiSessionOptions m_options;
        private readonly bool m_ownsClient;
        private readonly TimeProvider m_timeProvider;
        private readonly SemaphoreSlim m_openLock = new(1, 1);
        private readonly System.Threading.Lock m_stateLock = new();
        private readonly System.Threading.Lock m_ackLock = new();
        private readonly Channel<NotificationMessage> m_notifications = Channel.CreateUnbounded<NotificationMessage>(
            new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false
            });
        private readonly List<SubscriptionAcknowledgement> m_pendingAcknowledgements = [];
        private readonly List<Task> m_publishWorkers = [];
        private CancellationTokenSource? m_sessionCts;
        private CancellationTokenSource? m_publishCts;
        private ITimer? m_keepAliveTimer;
        private NodeId m_sessionId = NodeId.Null;
        private NodeId m_authenticationToken = NodeId.Null;
        private double m_revisedSessionTimeout;
        private int m_requestHandle;
        private int m_keepAliveRunning;
        private bool m_isConnected;
        private bool m_disposed;

        /// <summary>
        /// Initializes a new unopened REST API session wrapper.
        /// </summary>
        /// <param name="client">The REST API client.</param>
        /// <param name="options">The session options.</param>
        public RestApiSession(IRestApiClient client, RestApiSessionOptions options)
            : this(client, options, ownsClient: false)
        {
        }

        internal RestApiSession(IRestApiClient client, RestApiSessionOptions options, bool ownsClient)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            m_client = client;
            m_options = options;
            m_ownsClient = ownsClient;
            m_timeProvider = options.TimeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc/>
        public NodeId SessionId
        {
            get
            {
                lock (m_stateLock)
                {
                    return m_sessionId;
                }
            }
        }

        /// <inheritdoc/>
        public double RevisedSessionTimeout
        {
            get
            {
                lock (m_stateLock)
                {
                    return m_revisedSessionTimeout;
                }
            }
        }

        /// <inheritdoc/>
        public bool IsConnected
        {
            get
            {
                lock (m_stateLock)
                {
                    return m_isConnected;
                }
            }
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<NotificationMessage> Notifications => m_notifications.Reader.ReadAllAsync();

        /// <inheritdoc/>
        public event EventHandler<RestApiSessionStateEventArgs>? SessionStateChanged;

        /// <inheritdoc/>
        public async Task OpenAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (IsConnected)
            {
                return;
            }

            await m_openLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (IsConnected)
                {
                    return;
                }

                m_sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                CreateSessionResponse createResponse = await m_client
                    .CreateSessionAsync(CreateCreateSessionRequest(), ct)
                    .ConfigureAwait(false);
                ValidateResponseHeader(createResponse.ResponseHeader);

                NodeId sessionId = createResponse.SessionId;
                NodeId authenticationToken = createResponse.AuthenticationToken;
                if (sessionId.IsNull || authenticationToken.IsNull)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadUnexpectedError,
                        "CreateSession did not return a session id and authentication token.");
                }

                lock (m_stateLock)
                {
                    m_sessionId = sessionId;
                    m_authenticationToken = authenticationToken;
                    m_revisedSessionTimeout = createResponse.RevisedSessionTimeout;
                    if (m_revisedSessionTimeout <= 0)
                    {
                        m_revisedSessionTimeout = m_options.RequestedSessionTimeout;
                    }
                }

                ActivateSessionRequest activateRequest = await CreateActivateSessionRequestAsync(
                    createResponse,
                    ct).ConfigureAwait(false);
                ActivateSessionResponse activateResponse = await m_client
                    .ActivateSessionAsync(activateRequest, ct)
                    .ConfigureAwait(false);
                ValidateResponseHeader(activateResponse.ResponseHeader);

                ChangeState(isConnected: true, ServiceResult.Good);
                StartKeepAliveTimer();
                if (m_options.AutoPublish)
                {
                    StartPublishLoop();
                }
            }
            catch
            {
                ChangeState(isConnected: false, new ServiceResult(StatusCodes.BadUnexpectedError));
                throw;
            }
            finally
            {
                m_openLock.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            m_keepAliveTimer?.Dispose();
            m_sessionCts?.Cancel();
            m_publishCts?.Cancel();

            if (m_publishWorkers.Count > 0)
            {
                try
                {
                    await Task.WhenAll(m_publishWorkers).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (ServiceResultException)
                {
                }
            }

            if (IsConnected)
            {
                try
                {
                    await m_client.CloseSessionAsync(
                        new CloseSessionRequest
                        {
                            RequestHeader = CreateAuthenticatedRequestHeader(),
                            DeleteSubscriptions = true
                        },
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                }
            }

            ChangeState(isConnected: false, ServiceResult.Good);
            m_notifications.Writer.TryComplete();
            m_publishCts?.Dispose();
            m_sessionCts?.Dispose();
            m_openLock.Dispose();

            if (m_ownsClient && m_client is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        /// <inheritdoc/>
        public Task<ReadResponse> ReadAsync(ReadRequest request, CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.ReadAsync, ct);

        /// <inheritdoc/>
        public Task<WriteResponse> WriteAsync(WriteRequest request, CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.WriteAsync, ct);

        /// <inheritdoc/>
        public Task<HistoryReadResponse> HistoryReadAsync(HistoryReadRequest request, CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.HistoryReadAsync, ct);

        /// <inheritdoc/>
        public Task<HistoryUpdateResponse> HistoryUpdateAsync(
            HistoryUpdateRequest request,
            CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.HistoryUpdateAsync, ct);

        /// <inheritdoc/>
        public Task<CallResponse> CallAsync(CallRequest request, CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.CallAsync, ct);

        /// <inheritdoc/>
        public Task<BrowseResponse> BrowseAsync(BrowseRequest request, CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.BrowseAsync, ct);

        /// <inheritdoc/>
        public Task<BrowseNextResponse> BrowseNextAsync(BrowseNextRequest request, CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.BrowseNextAsync, ct);

        /// <inheritdoc/>
        public Task<TranslateBrowsePathsToNodeIdsResponse> TranslateBrowsePathsToNodeIdsAsync(
            TranslateBrowsePathsToNodeIdsRequest request,
            CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.TranslateBrowsePathsToNodeIdsAsync, ct);

        /// <inheritdoc/>
        public Task<RegisterNodesResponse> RegisterNodesAsync(
            RegisterNodesRequest request,
            CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.RegisterNodesAsync, ct);

        /// <inheritdoc/>
        public Task<UnregisterNodesResponse> UnregisterNodesAsync(
            UnregisterNodesRequest request,
            CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.UnregisterNodesAsync, ct);

        /// <inheritdoc/>
        public Task<CancelResponse> CancelAsync(CancelRequest request, CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.CancelAsync, ct);

        /// <inheritdoc/>
        public Task<CreateMonitoredItemsResponse> CreateMonitoredItemsAsync(
            CreateMonitoredItemsRequest request,
            CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.CreateMonitoredItemsAsync, ct);

        /// <inheritdoc/>
        public Task<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
            ModifyMonitoredItemsRequest request,
            CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.ModifyMonitoredItemsAsync, ct);

        /// <inheritdoc/>
        public Task<SetMonitoringModeResponse> SetMonitoringModeAsync(
            SetMonitoringModeRequest request,
            CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.SetMonitoringModeAsync, ct);

        /// <inheritdoc/>
        public Task<SetTriggeringResponse> SetTriggeringAsync(
            SetTriggeringRequest request,
            CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.SetTriggeringAsync, ct);

        /// <inheritdoc/>
        public Task<DeleteMonitoredItemsResponse> DeleteMonitoredItemsAsync(
            DeleteMonitoredItemsRequest request,
            CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.DeleteMonitoredItemsAsync, ct);

        /// <inheritdoc/>
        public Task<CreateSubscriptionResponse> CreateSubscriptionAsync(
            CreateSubscriptionRequest request,
            CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.CreateSubscriptionAsync, ct);

        /// <inheritdoc/>
        public Task<ModifySubscriptionResponse> ModifySubscriptionAsync(
            ModifySubscriptionRequest request,
            CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.ModifySubscriptionAsync, ct);

        /// <inheritdoc/>
        public Task<SetPublishingModeResponse> SetPublishingModeAsync(
            SetPublishingModeRequest request,
            CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.SetPublishingModeAsync, ct);

        /// <inheritdoc/>
        public Task<PublishResponse> PublishAsync(PublishRequest request, CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.PublishAsync, ct);

        /// <inheritdoc/>
        public Task<RepublishResponse> RepublishAsync(RepublishRequest request, CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.RepublishAsync, ct);

        /// <inheritdoc/>
        public Task<TransferSubscriptionsResponse> TransferSubscriptionsAsync(
            TransferSubscriptionsRequest request,
            CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.TransferSubscriptionsAsync, ct);

        /// <inheritdoc/>
        public Task<DeleteSubscriptionsResponse> DeleteSubscriptionsAsync(
            DeleteSubscriptionsRequest request,
            CancellationToken ct = default)
            => InvokeSessionServiceAsync(request, m_client.DeleteSubscriptionsAsync, ct);

        private async Task<TResponse> InvokeSessionServiceAsync<TRequest, TResponse>(
            TRequest request,
            Func<TRequest, CancellationToken, Task<TResponse>> invoke,
            CancellationToken ct)
            where TRequest : IServiceRequest
            where TResponse : IServiceResponse
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (invoke == null)
            {
                throw new ArgumentNullException(nameof(invoke));
            }

            await OpenAsync(ct).ConfigureAwait(false);
            request.RequestHeader = CreateAuthenticatedRequestHeader(request.RequestHeader);
            return await invoke(request, ct).ConfigureAwait(false);
        }

        private CreateSessionRequest CreateCreateSessionRequest()
        {
            ApplicationConfiguration? configuration = m_options.ClientConfiguration;
            string sessionName = string.IsNullOrEmpty(m_options.SessionName)
                ? "UrnRestApiSession"
                : m_options.SessionName;
            string baseAddress = m_client.BaseAddress.ToString();
            string serverUri = string.IsNullOrEmpty(m_options.ServerUri)
                ? baseAddress
                : m_options.ServerUri;
            string endpointUrl = string.IsNullOrEmpty(m_options.EndpointUrl)
                ? baseAddress
                : m_options.EndpointUrl;

            var clientDescription = new ApplicationDescription
            {
                ApplicationUri = configuration?.ApplicationUri ?? $"urn:{Environment.MachineName}:OpcUaRestClient",
                ApplicationName = LocalizedText.From(configuration?.ApplicationName ?? sessionName),
                ApplicationType = ApplicationType.Client,
                ProductUri = configuration?.ProductUri ?? "urn:opcfoundation.org:UA:RESTClient"
            };

            return new CreateSessionRequest
            {
                RequestHeader = CreateRequestHeader(),
                ClientDescription = clientDescription,
                ServerUri = serverUri,
                EndpointUrl = endpointUrl,
                SessionName = sessionName,
                ClientNonce = ByteString.From(CreateClientNonce()),
                ClientCertificate = default,
                RequestedSessionTimeout = m_options.RequestedSessionTimeout,
                MaxResponseMessageSize = 0
            };
        }

        private async Task<ActivateSessionRequest> CreateActivateSessionRequestAsync(
            CreateSessionResponse createResponse,
            CancellationToken ct)
        {
            IUserIdentity identity = m_options.Identity ?? new UserIdentity();
            IUserIdentityTokenHandler identityToken = identity.TokenHandler.Copy();
            var userTokenSignature = new SignatureData();

            // Make sure the identity token carries a PolicyId matching one
            // of the server's UserTokenPolicies, otherwise the server
            // rejects ActivateSession with BadIdentityTokenInvalid.
            string? policyId = m_options.IdentityPolicyId
                ?? FindMatchingPolicyId(createResponse, identityToken);
            if (!string.IsNullOrEmpty(policyId))
            {
                identityToken.Token.PolicyId = policyId;
            }

            if (identity.TokenType != UserTokenType.Anonymous && identity.SupportsSignatures)
            {
                userTokenSignature = await identityToken
                    .SignAsync(createResponse.ServerNonce.ToArray(), SecurityPolicies.None, ct)
                    .ConfigureAwait(false);
            }

            return new ActivateSessionRequest
            {
                RequestHeader = CreateAuthenticatedRequestHeader(),
                ClientSignature = new SignatureData(),
                ClientSoftwareCertificates = [],
                LocaleIds = [],
                UserIdentityToken = new ExtensionObject(identityToken.Token),
                UserTokenSignature = userTokenSignature
            };
        }

        private static string? FindMatchingPolicyId(
            CreateSessionResponse createResponse,
            IUserIdentityTokenHandler identityToken)
        {
            ArrayOf<EndpointDescription> serverEndpoints = createResponse.ServerEndpoints;
            if (serverEndpoints.IsNull)
            {
                return null;
            }
            UserTokenType desiredTokenType = identityToken.Token switch
            {
                AnonymousIdentityToken => UserTokenType.Anonymous,
                UserNameIdentityToken => UserTokenType.UserName,
                X509IdentityToken => UserTokenType.Certificate,
                IssuedIdentityToken => UserTokenType.IssuedToken,
                _ => UserTokenType.Anonymous
            };
            foreach (EndpointDescription endpoint in serverEndpoints)
            {
                ArrayOf<UserTokenPolicy> policies = endpoint.UserIdentityTokens;
                if (policies.IsNull)
                {
                    continue;
                }
                foreach (UserTokenPolicy policy in policies)
                {
                    if (policy.TokenType == desiredTokenType)
                    {
                        return policy.PolicyId;
                    }
                }
            }
            return null;
        }

        private RequestHeader CreateAuthenticatedRequestHeader(RequestHeader? requestHeader = null)
        {
            RequestHeader header = requestHeader ?? new RequestHeader();
            header.AuthenticationToken = GetAuthenticationToken();
            header.Timestamp = DateTime.UtcNow;
            header.RequestHandle = (uint)Interlocked.Increment(ref m_requestHandle);
            return header;
        }

        private RequestHeader CreateRequestHeader()
        {
            return new RequestHeader
            {
                Timestamp = DateTime.UtcNow,
                RequestHandle = (uint)Interlocked.Increment(ref m_requestHandle)
            };
        }

        private NodeId GetAuthenticationToken()
        {
            lock (m_stateLock)
            {
                return m_authenticationToken;
            }
        }

        private void StartKeepAliveTimer()
        {
            double divisor = m_options.KeepAliveDivisor > 0 ? m_options.KeepAliveDivisor : 4.0;
            double keepAliveMilliseconds = Math.Max(1, RevisedSessionTimeout / divisor);
            var interval = TimeSpan.FromMilliseconds(keepAliveMilliseconds);
            m_keepAliveTimer = m_timeProvider.CreateTimer(
                _ => _ = SendKeepAliveAsync(),
                null,
                interval,
                interval);
        }

        private async Task SendKeepAliveAsync()
        {
            if (Interlocked.Exchange(ref m_keepAliveRunning, 1) != 0)
            {
                return;
            }

            try
            {
                CancellationToken token = m_sessionCts?.Token ?? CancellationToken.None;
                if (!token.IsCancellationRequested && IsConnected)
                {
                    await m_client.ReadAsync(
                        new ReadRequest
                        {
                            RequestHeader = CreateAuthenticatedRequestHeader(),
                            MaxAge = 0,
                            TimestampsToReturn = TimestampsToReturn.Neither,
                            NodesToRead = []
                        },
                        token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ChangeState(isConnected: false, new ServiceResult(ex));
            }
            finally
            {
                Interlocked.Exchange(ref m_keepAliveRunning, 0);
            }
        }

        private void StartPublishLoop()
        {
            int workerCount = Math.Max(1, m_options.MaxConcurrentPublish);
            m_publishCts = CancellationTokenSource.CreateLinkedTokenSource(
                m_sessionCts?.Token ?? CancellationToken.None);
            for (int ii = 0; ii < workerCount; ii++)
            {
                CancellationToken token = m_publishCts.Token;
                m_publishWorkers.Add(Task.Run(() => PublishWorkerLoopAsync(token), token));
            }
        }

        private async Task PublishWorkerLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && IsConnected)
            {
                try
                {
                    var request = new PublishRequest
                    {
                        RequestHeader = CreateAuthenticatedRequestHeader(),
                        SubscriptionAcknowledgements = DrainPendingAcknowledgements()
                    };

                    PublishResponse response = await m_client
                        .PublishAsync(request, ct)
                        .ConfigureAwait(false);

                    ValidatePublishResponse(response);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNoSubscription)
                {
                    StopPublishLoopAsDisconnected(new ServiceResult(ex));
                    break;
                }
            }
        }

        private void ValidatePublishResponse(PublishResponse response)
        {
            ResponseHeader responseHeader = response.ResponseHeader;
            if (responseHeader != null && StatusCode.IsBad(responseHeader.ServiceResult))
            {
                if (responseHeader.ServiceResult == StatusCodes.BadNoSubscription)
                {
                    StopPublishLoopAsDisconnected(new ServiceResult(responseHeader.ServiceResult));
                    return;
                }

                throw new ServiceResultException(responseHeader.ServiceResult);
            }

            NotificationMessage notificationMessage = response.NotificationMessage;
            if (notificationMessage != null)
            {
                m_notifications.Writer.TryWrite(notificationMessage);
                AddPendingAcknowledgement(response.SubscriptionId, notificationMessage.SequenceNumber);
            }
        }

        private void StopPublishLoopAsDisconnected(ServiceResult result)
        {
            ChangeState(isConnected: false, result);
            m_publishCts?.Cancel();
        }

        private ArrayOf<SubscriptionAcknowledgement> DrainPendingAcknowledgements()
        {
            var acknowledgements = new List<SubscriptionAcknowledgement>();
            lock (m_ackLock)
            {
                acknowledgements.AddRange(m_pendingAcknowledgements);
                m_pendingAcknowledgements.Clear();
            }

            return acknowledgements;
        }

        private void AddPendingAcknowledgement(uint subscriptionId, uint sequenceNumber)
        {
            lock (m_ackLock)
            {
                m_pendingAcknowledgements.Add(new SubscriptionAcknowledgement
                {
                    SubscriptionId = subscriptionId,
                    SequenceNumber = sequenceNumber
                });
            }
        }

        private void ChangeState(bool isConnected, ServiceResult? result)
        {
            RestApiSessionStateEventArgs? args = null;
            lock (m_stateLock)
            {
                if (m_isConnected == isConnected)
                {
                    if (!isConnected)
                    {
                        m_sessionId = NodeId.Null;
                        m_authenticationToken = NodeId.Null;
                    }

                    return;
                }

                m_isConnected = isConnected;
                if (!isConnected)
                {
                    m_sessionId = NodeId.Null;
                    m_authenticationToken = NodeId.Null;
                }

                args = new RestApiSessionStateEventArgs(m_sessionId, isConnected, result);
            }

            SessionStateChanged?.Invoke(this, args);
        }

        private static void ValidateResponseHeader(ResponseHeader responseHeader)
        {
            if (responseHeader != null && StatusCode.IsBad(responseHeader.ServiceResult))
            {
                throw new ServiceResultException(responseHeader.ServiceResult);
            }
        }

        private static byte[] CreateClientNonce()
        {
            byte[] nonce = new byte[32];
            using RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create();
            randomNumberGenerator.GetBytes(nonce);
            return nonce;
        }

        private void ThrowIfDisposed()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(RestApiSession));
            }
        }
    }

    /// <summary>
    /// Event data for REST API session connection state changes.
    /// </summary>
    public sealed class RestApiSessionStateEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RestApiSessionStateEventArgs"/> class.
        /// </summary>
        /// <param name="sessionId">The current session id.</param>
        /// <param name="isConnected">Whether the session is connected.</param>
        /// <param name="serviceResult">The service result associated with the state change.</param>
        public RestApiSessionStateEventArgs(
            NodeId sessionId,
            bool isConnected,
            ServiceResult? serviceResult)
        {
            SessionId = sessionId;
            IsConnected = isConnected;
            ServiceResult = serviceResult;
        }

        /// <summary>
        /// The current session id.
        /// </summary>
        public NodeId SessionId { get; }

        /// <summary>
        /// Whether the session is connected.
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        /// The service result associated with the state change.
        /// </summary>
        public ServiceResult? ServiceResult { get; }
    }
}
