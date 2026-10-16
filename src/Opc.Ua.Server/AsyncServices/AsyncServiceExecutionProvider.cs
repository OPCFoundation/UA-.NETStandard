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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Implements bounded, same-Session recoverable Read, Write and Call execution without submission replay.
    /// </summary>
    public sealed class AsyncServiceExecutionProvider : IAsyncServiceExecutionProvider
    {
        /// <summary>
        /// Creates a Server-owned provider using existing message limits, admission pools and an elapsed-time clock.
        /// </summary>
        public AsyncServiceExecutionProvider(
            StandardServer server,
            ApplicationConfiguration configuration,
            AsyncServiceExecutionOptions options,
            TimeProvider? timeProvider = null,
            IAsyncServiceAuthorizationPolicy? authorizationPolicy = null)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            m_options = (options ?? throw new ArgumentNullException(nameof(options))).Snapshot();
            m_clock = timeProvider ?? TimeProvider.System;
            m_authorizationPolicy = authorizationPolicy;
            m_maxMessageSize = configuration.TransportQuotas?.MaxMessageSize ??
                throw new ArgumentException("Transport quotas are required.", nameof(configuration));
            m_isolation = server.ResourceIsolationProvider;
            m_auditServer = server.CurrentInstance;
            m_logger = server.MessageContext.Telemetry.CreateLogger<AsyncServiceExecutionProvider>();
            server.CurrentInstance.SessionManager.SessionClosing += OnSessionClosing;
        }

        /// <inheritdoc/>
        public ArrayOf<NodeId> SupportedServices =>
            [DataTypeIds.ReadRequest, DataTypeIds.WriteRequest, DataTypeIds.CallRequest];

        /// <inheritdoc/>
        public AsyncServiceExecutionStatistics GetStatistics()
        {
            lock (m_gate)
            {
                return new AsyncServiceExecutionStatistics(
                    (uint)m_byToken.Values.Count(static operation => !operation.WorkFinished),
                    (uint)m_byToken.Values.Count(
                        static operation => operation.State == ServiceOperationState.OutcomeAvailable),
                    m_retainedBytes,
                    (uint)m_byToken.Values.Count(static operation => IsReleased(operation.State)),
                    m_admitted, m_rejected, m_acknowledged, m_abandoned, m_expired, m_deadlines);
            }
        }

        /// <inheritdoc/>
        public bool CanPark(IServiceRequest request)
        {
            return request is SubmitServiceRequest or GetServiceResultRequest;
        }

        /// <inheritdoc/>
        public async ValueTask<SubmitServiceResponse> SubmitAsync(
            OperationContext context, SubmitServiceRequest request, CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            bool admitted = false;
            NodeId submittedService = default;
            try
            {
                TimeSpan execution = ValidateDuration(
                    request.ExecutionAllowance, m_options.MinExecutionAllowance, m_options.MaxExecutionAllowance);
                TimeSpan retention = ValidateDuration(
                    request.ResultRetentionTime, m_options.MinResultRetentionTime, m_options.MaxResultRetentionTime);
                TimeSpan wait = ValidateWait(request.WaitTime);
                if (request.ClientOperationId == Uuid.Empty ||
                    !request.Request.TryGetValue(out IServiceRequest? original, m_server.MessageContext) ||
                    original.RequestHeader == null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                RequestType requestType = original switch
                {
                    ReadRequest => RequestType.Read,
                    WriteRequest => RequestType.Write,
                    CallRequest => RequestType.Call,
                    _ => throw new ServiceResultException(StatusCodes.BadServiceUnsupported)
                };
                submittedService = ExpandedNodeId.ToNodeId(original.TypeId, m_server.MessageContext.NamespaceUris);
                NodeId expectedService = requestType switch
                {
                    RequestType.Read => DataTypeIds.ReadRequest,
                    RequestType.Write => DataTypeIds.WriteRequest,
                    RequestType.Call => DataTypeIds.CallRequest,
                    _ => NodeId.Null
                };
                if (submittedService != expectedService)
                {
                    throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
                }
                if (m_server.CurrentInstance.NodeManager is not MasterNodeManager nodeManager ||
                    m_server.CurrentInstance.SessionManager is not SessionManager)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotSupported,
                        "The installed node manager cannot revalidate retained result access.");
                }
                if (!original.RequestHeader.AuthenticationToken.IsNull &&
                    original.RequestHeader.AuthenticationToken != request.RequestHeader.AuthenticationToken)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                if (context.Session.IdentityToken.TokenType == UserTokenType.Anonymous &&
                    context.ChannelContext!.EndpointDescription?.SecurityMode == MessageSecurityMode.Sign)
                {
                    throw new ServiceResultException(StatusCodes.BadSecurityModeRejected);
                }
                int limit = GetMessageLimit(context.Session);
                ValidateCurrentIdentity(context);
                ByteString creator = GetCreator(context.Session);
                await AuthorizeAsync(context, request.ClientOperationId,
                    ExpandedNodeId.ToNodeId(original.TypeId, m_server.MessageContext.NamespaceUris),
                    AsyncServiceAccess.Submit, ct).ConfigureAwait(false);
                await nodeManager.ValidateRecoverableServiceEligibilityAsync(context, original, ct)
                    .ConfigureAwait(false);
                (ISession Session, Uuid ClientOperationId) key = (context.Session, request.ClientOperationId);
                lock (m_gate)
                {
                    ThrowIfStopping();
                    if (m_byClient.ContainsKey(key))
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState);
                    }
                }

                Reservations reservations = Reserve(context, request.RequestHeader.AuthenticationToken, limit);
                Operation? operation = null;
                try
                {
                    ServiceMessageContext encoding = TransportChannelBinding.CopyContext(m_server.MessageContext);
                    encoding.MaxMessageSize = limit;
                    byte[] input = EncodeBinary(original, encoding);
                    IServiceRequest ownedRequest = BinaryDecoder.DecodeMessage<IServiceRequest>(input, encoding);
                    ownedRequest.RequestHeader.AuthenticationToken = request.RequestHeader.AuthenticationToken;
                    ownedRequest.RequestHeader.TimeoutHint = 0;
                    ownedRequest.RequestHeader.ReturnDiagnostics &=
                        ~(uint)DiagnosticsMasks.UserPermissionAdditionalInfo;
                    context.Session.ValidateDiagnosticInfo(ownedRequest.RequestHeader);
                    input = EncodeBinary(ownedRequest, encoding);
                    operation = new Operation(
                        this, context, ownedRequest, input, requestType, request.ClientOperationId, NewToken(),
                        creator, execution, retention, encoding, limit, reservations);
                    ValidateResultSize(operation, new ServiceFault
                    {
                        ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadResponseTooLarge }
                    });
                    lock (m_gate)
                    {
                        ThrowIfStopping();
                        ct.ThrowIfCancellationRequested();
                        ValidateCurrentIdentity(context);
                        if (creator != GetCreator(context.Session))
                        {
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                        }
                        if (SessionTermination.IsClosingOrClaimed(context.Session))
                        {
                            throw new ServiceResultException(StatusCodes.BadSessionClosed);
                        }
                        if (m_byClient.ContainsKey(key))
                        {
                            throw new ServiceResultException(StatusCodes.BadInvalidState);
                        }
                        if (m_byToken.ContainsKey(operation.Token))
                        {
                            throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                        }
                        operation.AdmittedAt = m_clock.GetTimestamp();
                        m_byClient.Add(key, operation);
                        m_byToken.Add(operation.Token, operation);
                        admitted = true;
                        Increment(ref m_admitted);
                        RecordAudit(operation, ServiceOperationAuditAction.Admitted);
                        ArmTimer(operation);
                        using (ExecutionContext.SuppressFlow())
                        {
                            operation.Work = Task.Run(() => ExecuteAsync(operation), CancellationToken.None);
                        }
                    }
                    await WaitAsync(operation, wait, context.RequestLifetime, ct).ConfigureAwait(false);
                    using OperationContext current = await RevalidateAsync(
                        context, request.RequestHeader, operation, AsyncServiceAccess.Collect)
                        .ConfigureAwait(false);
                    return await CreateSubmissionResponseAsync(current, request.RequestHeader, operation)
                        .ConfigureAwait(false);
                }
                finally
                {
                    if (!admitted)
                    {
                        operation?.DisposeUnadmitted();
                        reservations.Dispose();
                    }
                }
            }
            catch
            {
                if (!admitted)
                {
                    lock (m_gate)
                    {
                        Increment(ref m_rejected);
                        RecordAudit(request.ClientOperationId, submittedService,
                            ServiceOperationAuditAction.Rejected, ServiceOperationState.Unknown,
                            ServiceOperationOutcome.None, StatusCodes.Good, succeeded: false, context.SessionId);
                    }
                }
                throw;
            }
            finally
            {
                FlushAudits();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<GetServiceResultResponse> GetAsync(
            OperationContext context, GetServiceResultRequest request, CancellationToken ct)
        {
            try
            {
                return await GetCoreAsync(context, request, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadUserAccessDenied)
            {
                RecordDenied(context, request.ClientOperationId, request.ServerToken);
                throw;
            }
            finally
            {
                FlushAudits();
            }
        }

        /// <summary>
        /// Collects one shared outcome while keeping exchange waits separate from invocation cancellation.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<GetServiceResultResponse> GetCoreAsync(
            OperationContext context, GetServiceResultRequest request, CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            TimeSpan wait = ValidateWait(request.WaitTime);
            bool clientId = request.ClientOperationId != Uuid.Empty;
            bool token = !request.ServerToken.IsNull;
            if (clientId == token || (token && !IsToken(request.ServerToken)))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            ct.ThrowIfCancellationRequested();
            Operation? operation;
            lock (m_gate)
            {
                if (clientId)
                {
                    m_byClient.TryGetValue((context.Session, request.ClientOperationId), out operation);
                }
                else
                {
                    m_byToken.TryGetValue(request.ServerToken, out operation);
                }
                if (operation != null && !ReferenceEquals(operation.Session, context.Session))
                {
                    operation = null;
                }
                if (operation != null)
                {
                    ValidateOwner(context, operation);
                    Advance(operation);
                }
            }
            await AuthorizeAsync(context, operation != null ? operation.ClientId : request.ClientOperationId,
                operation != null ? operation.ServiceId : NodeId.Null, AsyncServiceAccess.Collect, ct)
                .ConfigureAwait(false);
            if (operation != null)
            {
                Cleanup(operation);
                await WaitAsync(operation, wait, context.RequestLifetime, ct).ConfigureAwait(false);
                using OperationContext current = await RevalidateAsync(
                    context, request.RequestHeader, operation, AsyncServiceAccess.Collect)
                    .ConfigureAwait(false);
                return await CreateCollectionResponseAsync(
                    current, request.RequestHeader, operation, request.ClientOperationId).ConfigureAwait(false);
            }
            return new GetServiceResultResponse
            {
                ResponseHeader = m_server.CreateResponse(request.RequestHeader, context),
                Operation = Unknown(request.ClientOperationId)
            };
        }

        /// <inheritdoc/>
        public async ValueTask<ControlServiceExecutionResponse> ControlAsync(
            OperationContext context, ControlServiceExecutionRequest request, CancellationToken ct)
        {
            try
            {
                return await ControlCoreAsync(context, request, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadUserAccessDenied)
            {
                RecordDenied(context, default, request.ServerToken);
                throw;
            }
            finally
            {
                FlushAudits();
            }
        }

        /// <summary>
        /// Authorizes a specific control action before applying its state change.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<ControlServiceExecutionResponse> ControlCoreAsync(
            OperationContext context, ControlServiceExecutionRequest request, CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (!IsToken(request.ServerToken) ||
                request.Action is < ServiceOperationControlAction.Cancel or > ServiceOperationControlAction.Abandon)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            ct.ThrowIfCancellationRequested();
            Operation? operation;
            lock (m_gate)
            {
                m_byToken.TryGetValue(request.ServerToken, out operation);
                if (operation != null && !ReferenceEquals(operation.Session, context.Session))
                {
                    operation = null;
                }
                if (operation != null)
                {
                    ValidateOwner(context, operation);
                }
            }
            AsyncServiceAccess access = request.Action switch
            {
                ServiceOperationControlAction.Cancel => AsyncServiceAccess.Cancel,
                ServiceOperationControlAction.Acknowledge => AsyncServiceAccess.Acknowledge,
                ServiceOperationControlAction.Abandon => AsyncServiceAccess.Abandon,
                _ => throw new ServiceResultException(StatusCodes.BadInvalidArgument)
            };
            await AuthorizeAsync(context, operation != null ? operation.ClientId : Uuid.Empty,
                operation != null ? operation.ServiceId : NodeId.Null, access, ct).ConfigureAwait(false);
            if (operation == null)
            {
                return new ControlServiceExecutionResponse
                {
                    ResponseHeader = m_server.CreateResponse(request.RequestHeader, context),
                    Result = ServiceOperationControlResult.UnknownOperation,
                    Operation = Unknown(default)
                };
            }
            using OperationContext current = await RevalidateAsync(context, request.RequestHeader, operation, access)
                .ConfigureAwait(false);
            ServiceOperationControlResult result;
            ServiceOperationInfoDataType info;
            lock (m_gate)
            {
                ct.ThrowIfCancellationRequested();
                ValidateOwner(current, operation);
                Advance(operation);
                result = operation.Forgotten
                    ? ServiceOperationControlResult.UnknownOperation
                    : ApplyControl(operation, request.Action);
                info = operation.Forgotten ? Unknown(default) : Snapshot(operation);
                ArmTimer(operation);
            }
            Cleanup(operation);
            return new ControlServiceExecutionResponse
            {
                ResponseHeader = m_server.CreateResponse(request.RequestHeader, current),
                Result = result,
                Operation = info
            };
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Operation[] operations;
            lock (m_gate)
            {
                if (m_stopping)
                {
                    return;
                }
                m_stopping = true;
                operations = [.. m_byToken.Values];
                foreach (Operation operation in operations)
                {
                    Release(operation, ServiceOperationState.Abandoned, countAction: false);
                    operation.Forgotten = true;
                    RecordAudit(operation, ServiceOperationAuditAction.HistoryForgotten);
                    RequestCancellation(operation);
                }
            }
            m_server.CurrentInstance.SessionManager.SessionClosing -= OnSessionClosing;
            await Task.WhenAll(operations.Select(static operation => operation.Work)).ConfigureAwait(false);
            foreach (Operation operation in operations)
            {
                Cleanup(operation);
            }
        }

        /// <summary>
        /// Validates exact tick-representable positive lifetimes rather than rounding or revising them.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static TimeSpan ValidateDuration(double value, double minimum, double maximum)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            double ticks = value * TimeSpan.TicksPerMillisecond;
            if (value < minimum || value > maximum || ticks != Math.Truncate(ticks))
            {
                throw new ServiceResultException(StatusCodes.BadOutOfRange);
            }
            return TimeSpan.FromTicks((long)ticks);
        }

        /// <summary>
        /// Validates bounded waiting independently of work cancellation and execution allowance.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static TimeSpan ValidateWait(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            return value == 0 ? TimeSpan.Zero : ValidateDuration(value, 0, int.MaxValue);
        }

        /// <summary>
        /// Checks the protocol's nonempty bounded opaque token representation.
        /// </summary>
        private static bool IsToken(ByteString token)
        {
            return token.Length is > 0 and <= 64;
        }

        /// <summary>
        /// Creates a cryptographically random prototype token without exposing it to logging.
        /// </summary>
        private static ByteString NewToken()
        {
            var bytes = new byte[32];
            using var random = RandomNumberGenerator.Create();
            random.GetBytes(bytes);
            return new ByteString(bytes);
        }

        /// <summary>
        /// Keeps a bounded digest of the validated creator continuity key, not credentials or token data.
        /// </summary>
        private static ByteString GetCreator(ISession session)
        {
            string key = ClientUserIdResolver.ResolveContinuityKey(session.IdentityToken, session.Identity)
                ?? "anonymous";
            return new ByteString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        }

        /// <summary>
        /// Produces the canonical lookup-only unknown metadata without claiming non-execution.
        /// </summary>
        private static ServiceOperationInfoDataType Unknown(Uuid clientOperationId)
        {
            return new ServiceOperationInfoDataType
            {
                ClientOperationId = clientOperationId,
                ServerToken = default
            };
        }

        /// <summary>
        /// Enforces the same Session instance and validated creator independently of opaque identifiers.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateOwner(OperationContext context, Operation operation)
        {
            ValidateCurrentIdentity(context);
            if (!ReferenceEquals(context.Session, operation.Session) ||
                GetCreator(context.Session) != operation.Creator ||
                !HasRoles(operation.Roles, context.UserIdentity.GrantedRoleIds))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }

        /// <summary>
        /// Fails closed when activation or role refresh changed the validated identity during an awaited decision.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateCurrentIdentity(OperationContext context)
        {
            if (context.Session.IsIdentityStale ||
                !ReferenceEquals(context.UserIdentity, context.Session.EffectiveIdentity))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }

        /// <summary>
        /// Does not transfer an operation's permissions when the creator loses an admitted role.
        /// </summary>
        private static bool HasRoles(ArrayOf<NodeId> required, ArrayOf<NodeId> current)
        {
            foreach (NodeId role in required)
            {
                if (current.Span.IndexOf(role) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Resolves the finite Server and Session message bounds without changing zero/unlimited semantics.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private int GetMessageLimit(ISession session)
        {
            if (m_maxMessageSize <= 0 || m_isolation == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadResourceUnavailable,
                    "Recoverable admission requires finite existing resource limits.");
            }
            uint sessionLimit = session.ReadDiagnostics(static diagnostics => diagnostics.MaxResponseMessageSize);
            return sessionLimit == 0 ? m_maxMessageSize : (int)Math.Min(m_maxMessageSize, sessionLimit);
        }

        /// <summary>
        /// Reserves work, input, outcome and history capacity before identity registration and dispatch.
        /// </summary>
        private Reservations Reserve(OperationContext context, NodeId authenticationToken, int limit)
        {
            ResourceIsolationOwner owner = m_isolation!.Classify(context.ChannelContext!, authenticationToken);
            var reservations = new Reservations();
            try
            {
                reservations.HistoryCount = Acquire(ResourceIsolationStage.ParkedRequest, owner, 1);
                reservations.HistoryBytes = Acquire(ResourceIsolationStage.RequestQueueBytes, owner, 1024);
                reservations.InputBytes = Acquire(ResourceIsolationStage.RequestQueueBytes, owner, limit);
                reservations.OutcomeBytes = Acquire(ResourceIsolationStage.RequestQueueBytes, owner, limit);
                reservations.Execution = Acquire(ResourceIsolationStage.RequestExecution, owner, 1);
                return reservations;
            }
            catch
            {
                reservations.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Uses the installed isolation provider rather than an independent Async operation quota.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private IDisposable Acquire(ResourceIsolationStage stage, ResourceIsolationOwner owner, long amount)
        {
            return m_isolation!.TryAcquire(stage, owner, amount, out IDisposable? lease, out _)
                ? lease
                : throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
        }

        /// <summary>
        /// Executes once and freezes the original response before making its outcome available.
        /// </summary>
        private async Task ExecuteAsync(Operation operation)
        {
            IServiceResponse? response = null;
            byte[]? frozen = null;
            StatusCode status = StatusCodes.Good;
            ServiceOperationOutcome kind = ServiceOperationOutcome.ServiceResponse;
            try
            {
                try
                {
                    response = await m_server.ExecuteOwnedServiceAsync(operation.Context!, operation.Request!)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    response = EndpointBase.CreateFault(m_logger, operation.Request,
                        new ServiceResultException(StatusCodes.BadRequestCancelledByClient));
                }
                catch (Exception exception)
                {
                    response = EndpointBase.CreateFault(m_logger, operation.Request, exception);
                }
                status = response.ResponseHeader.ServiceResult;
                try
                {
                    frozen = EncodeBinary(response, operation.Encoding);
                    ValidateResultSize(operation, response);
                }
                catch (ServiceResultException exception)
                {
                    kind = ServiceOperationOutcome.ResultUnavailable;
                    status = exception.StatusCode == StatusCodes.BadEncodingLimitsExceeded ||
                        exception.StatusCode == StatusCodes.BadResponseTooLarge
                            ? StatusCodes.BadResponseTooLarge : StatusCodes.BadEncodingError;
                    frozen = null;
                    m_logger.AsyncServiceResultUnavailable(operation.ClientId, status);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    kind = ServiceOperationOutcome.ResultUnavailable;
                    status = StatusCodes.BadEncodingError;
                    frozen = null;
                    m_logger.AsyncServiceResultUnavailable(operation.ClientId, status);
                }
                lock (m_gate)
                {
                    Advance(operation);
                    if (operation.State == ServiceOperationState.Pending)
                    {
                        if (frozen != null)
                        {
                            operation.Outcome = new OutcomeSnapshot(
                                frozen, operation.Encoding, operation.Reservations.OutcomeBytes!,
                                () => ReleaseOutcomeBytes(operation.Limit));
                            operation.Reservations.OutcomeBytes = null;
                            m_retainedBytes += (ulong)operation.Limit;
                        }
                        CommitOutcome(operation, kind, status);
                    }
                    ArmTimer(operation);
                }
            }
            finally
            {
                (response as IPooledEncodeable)?.Reuse();
                operation.Context?.Dispose();
                Task cancellation;
                lock (m_gate)
                {
                    operation.Context = null;
                    operation.WorkFinished = true;
                    RecordAudit(operation, ServiceOperationAuditAction.InvocationEnded);
                    cancellation = operation.CancellationTask;
                    Advance(operation);
                }
                await cancellation.ConfigureAwait(false);
                operation.Lifetime.Dispose();
                operation.Cancellation.Dispose();
                Cleanup(operation);
            }
        }

        /// <summary>
        /// Checks both complete envelopes using worst-width fresh headers and non-renewing remaining durations.
        /// </summary>
        private void ValidateResultSize(Operation operation, IServiceResponse original)
        {
            var header = new ResponseHeader
            {
                RequestHandle = uint.MaxValue,
                Timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567)
            };
            ServiceOperationInfoDataType info = Snapshot(operation);
            info.State = ServiceOperationState.OutcomeAvailable;
            info.OutcomeKind = ServiceOperationOutcome.ServiceResponse;
            info.OutcomeStatus = original.ResponseHeader.ServiceResult;
            info.ExecutionRemaining = 0;
            info.ResultRemaining = Math.Floor(operation.Retention.TotalMilliseconds);
            var result = new ExtensionObject(original);
            CheckEncodedSize(new SubmitServiceResponse
            {
                ResponseHeader = header,
                Operation = info,
                Result = result
            }, operation.Mapping, operation.Encoding);
            CheckEncodedSize(new GetServiceResultResponse
            {
                ResponseHeader = header,
                Operation = info,
                Result = result
            }, operation.Mapping, operation.Encoding);
        }

        /// <summary>
        /// Measures actual encoded envelopes, including header, metadata and the original response.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void CheckEncodedSize(
            IServiceResponse response, RequestEncoding mapping, ServiceMessageContext context)
        {
            using var stream = new CappedMemoryStream(context.MaxMessageSize);
            if (mapping == RequestEncoding.Binary)
            {
                using var binary = new BinaryEncoder(stream, context, leaveOpen: true);
                binary.EncodeMessage(response, response.BinaryEncodingId);
                _ = binary.Close();
                return;
            }
            if (mapping != RequestEncoding.Json)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingError);
            }
            using var encoder = new JsonEncoder(stream, context);
            encoder.EncodeMessage(response, response.TypeId);
            int length = encoder.Close();
            if (length > context.MaxMessageSize)
            {
                throw new ServiceResultException(StatusCodes.BadResponseTooLarge);
            }
        }

        /// <summary>
        /// Freezes a complete message without permitting encoder buffer growth beyond the reserved cap.
        /// </summary>
        private static byte[] EncodeBinary(IEncodeable message, ServiceMessageContext context)
        {
            using var stream = new CappedMemoryStream(context.MaxMessageSize);
            using var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            encoder.EncodeMessage(message, message.BinaryEncodingId);
            _ = encoder.Close();
            return stream.ToArray();
        }

        /// <summary>
        /// Separates a cancellable wrapper wait from the independently owned invocation lifetime.
        /// </summary>
        private async Task WaitAsync(Operation operation, TimeSpan wait, RequestLifetime lifetime, CancellationToken ct)
        {
            if (wait == TimeSpan.Zero)
            {
                return;
            }
            lock (m_gate)
            {
                Advance(operation);
                if (operation.State != ServiceOperationState.Pending || operation.Forgotten)
                {
                    return;
                }
            }
            lifetime.ParkSink?.NotifyParked();
            using CancellationTokenSource expiry = m_clock.CreateCancellationTokenSource(wait);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, expiry.Token);
            try
            {
                await operation.Changed.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (expiry.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                // The requested wait budget ended; this exchange reports the current state.
            }
        }

        /// <summary>
        /// Refreshes current Session authorization after waiting and before disclosing retained state.
        /// </summary>
        private async ValueTask<OperationContext> RevalidateAsync(
            OperationContext context, RequestHeader header, Operation operation, AsyncServiceAccess access)
        {
            OperationContext current = await m_server.RevalidateAsyncServiceContextAsync(context, header)
                .ConfigureAwait(false);
            try
            {
                ValidateOwner(current, operation);
                context.CancellationToken.ThrowIfCancellationRequested();
                await AuthorizeAsync(
                    current, operation.ClientId, operation.ServiceId, access, context.CancellationToken)
                    .ConfigureAwait(false);
                ValidateOwner(current, operation);
                return current;
            }
            catch
            {
                current.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Invokes an optional current application policy without exposing a Server token or retained payload.
        /// </summary>
        private ValueTask AuthorizeAsync(
            OperationContext context, Uuid clientId, NodeId serviceId, AsyncServiceAccess access, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ValidateCurrentIdentity(context);
            return m_authorizationPolicy?.AuthorizeAsync(context, clientId, serviceId, access, ct) ?? default;
        }

        /// <summary>
        /// Releases snapshot diagnostics only when the final retained or in-flight owner releases its quota lease.
        /// </summary>
        private void ReleaseOutcomeBytes(int amount)
        {
            lock (m_gate)
            {
                m_retainedBytes -= (ulong)amount;
            }
        }

        /// <summary>
        /// Keeps cumulative diagnostics from wrapping.
        /// </summary>
        private static void Increment(ref ulong counter)
        {
            if (counter != ulong.MaxValue)
            {
                counter++;
            }
        }

        /// <summary>
        /// Selects one shared result safely while concurrent collectors can release its retained ownership.
        /// </summary>
        private (ServiceOperationInfoDataType Info, OutcomeSnapshot? Snapshot, OutcomeSnapshot? Input) Select(
            Operation operation, Uuid unknownClientId)
        {
            lock (m_gate)
            {
                Advance(operation);
                if (operation.Forgotten)
                {
                    return (Unknown(unknownClientId), null, null);
                }
                OutcomeSnapshot? snapshot = operation.State == ServiceOperationState.OutcomeAvailable
                    ? operation.Outcome : null;
                snapshot?.Retain();
                OutcomeSnapshot? input = snapshot != null ? operation.Input : null;
                input?.Retain();
                return (Snapshot(operation), snapshot, input);
            }
        }

        /// <summary>
        /// Returns a fresh submission envelope, retaining snapshot and delivery storage through transport completion.
        /// </summary>
        private async ValueTask<RetainedSubmitResponse> CreateSubmissionResponseAsync(
            OperationContext context, RequestHeader header, Operation operation)
        {
            (ServiceOperationInfoDataType info, OutcomeSnapshot? snapshot, OutcomeSnapshot? input) =
                Select(operation, operation.ClientId);
            var response = new RetainedSubmitResponse(snapshot)
            {
                ResponseHeader = m_server.CreateResponse(header, context),
                Operation = info
            };
            try
            {
                response.Result = (snapshot?.Decode()) ?? default;
                await AuthorizeResultAsync(context, input, response.Result).ConfigureAwait(false);
                ValidateOwner(context, operation);
                CheckDeliverySize(response, context);
                RecordCollection(info, context.SessionId);
                return response;
            }
            catch
            {
                response.Reuse();
                throw;
            }
            finally
            {
                input?.Release();
                Cleanup(operation);
            }
        }

        /// <summary>
        /// Returns a fresh collection envelope, never replacing the stored outcome on a delivery failure.
        /// </summary>
        private async ValueTask<RetainedGetResponse> CreateCollectionResponseAsync(
            OperationContext context, RequestHeader header, Operation operation, Uuid unknownClientId)
        {
            (ServiceOperationInfoDataType info, OutcomeSnapshot? snapshot, OutcomeSnapshot? input) =
                Select(operation, unknownClientId);
            var response = new RetainedGetResponse(snapshot)
            {
                ResponseHeader = m_server.CreateResponse(header, context),
                Operation = info
            };
            try
            {
                response.Result = (snapshot?.Decode()) ?? default;
                await AuthorizeResultAsync(context, input, response.Result).ConfigureAwait(false);
                ValidateOwner(context, operation);
                CheckDeliverySize(response, context);
                RecordCollection(info, context.SessionId);
                return response;
            }
            catch
            {
                response.Reuse();
                throw;
            }
            finally
            {
                input?.Release();
                Cleanup(operation);
            }
        }

        /// <summary>
        /// Reuses the current node permission validators before disclosing successful original per-item results.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask AuthorizeResultAsync(
            OperationContext context, OutcomeSnapshot? input, ExtensionObject result)
        {
            if (input == null)
            {
                return;
            }
            IServiceRequest request = input.DecodeRequest();
            try
            {
                uint requestedDiagnostics = request.RequestHeader.ReturnDiagnostics;
                var diagnosticHeader = new RequestHeader
                {
                    ReturnDiagnostics = requestedDiagnostics & ~(uint)DiagnosticsMasks.UserPermissionAdditionalInfo
                };
                context.Session.ValidateDiagnosticInfo(diagnosticHeader);
                if ((requestedDiagnostics & (uint)DiagnosticsMasks.UserPermissionAdditionalInfo) != 0 &&
                    (diagnosticHeader.ReturnDiagnostics & (uint)DiagnosticsMasks.UserPermissionAdditionalInfo) == 0)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                if (!result.TryGetValue(out IServiceResponse? response) ||
                    m_server.CurrentInstance.NodeManager is not MasterNodeManager manager)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                await manager.ValidateRetainedServiceResultAsync(context, request, response, context.CancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                (request as IPooledEncodeable)?.Reuse();
            }
        }

        /// <summary>
        /// Honors the current Session/transport bound independently of the original outcome's retention.
        /// </summary>
        private void CheckDeliverySize(IServiceResponse response, OperationContext context)
        {
            ServiceMessageContext encoding = TransportChannelBinding.CopyContext(m_server.MessageContext);
            encoding.MaxMessageSize = GetMessageLimit(context.Session);
            try
            {
                CheckEncodedSize(response, context.ChannelContext!.MessageEncoding, encoding);
            }
            catch (ServiceResultException exception) when (
                exception.StatusCode == StatusCodes.BadEncodingLimitsExceeded)
            {
                throw new ServiceResultException(StatusCodes.BadResponseTooLarge);
            }
        }

        /// <summary>
        /// Applies action-specific state changes without conflating stopping work and releasing result recovery.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private ServiceOperationControlResult ApplyControl(Operation operation, ServiceOperationControlAction action)
        {
            switch (action)
            {
                case ServiceOperationControlAction.Cancel:
                    if (operation.WorkFinished)
                    {
                        return ServiceOperationControlResult.NotApplicable;
                    }
                    if (operation.CancellationRequested)
                    {
                        return ServiceOperationControlResult.AlreadyApplied;
                    }
                    RequestCancellation(operation);
                    return ServiceOperationControlResult.Applied;
                case ServiceOperationControlAction.Acknowledge:
                    if (operation.State == ServiceOperationState.Acknowledged)
                    {
                        return ServiceOperationControlResult.AlreadyApplied;
                    }
                    if (operation.State != ServiceOperationState.OutcomeAvailable)
                    {
                        return ServiceOperationControlResult.NotApplicable;
                    }
                    Release(operation, ServiceOperationState.Acknowledged);
                    return ServiceOperationControlResult.Applied;
                case ServiceOperationControlAction.Abandon:
                    if (operation.State == ServiceOperationState.Abandoned)
                    {
                        return ServiceOperationControlResult.AlreadyApplied;
                    }
                    if (operation.State is not (
                        ServiceOperationState.Pending or ServiceOperationState.OutcomeAvailable))
                    {
                        return ServiceOperationControlResult.NotApplicable;
                    }
                    Release(operation, ServiceOperationState.Abandoned);
                    return ServiceOperationControlResult.Applied;
                default:
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
        }

        /// <summary>
        /// Commits exactly one terminal outcome and starts its accepted retention interval.
        /// </summary>
        private void CommitOutcome(Operation operation, ServiceOperationOutcome kind, StatusCode status)
        {
            operation.State = ServiceOperationState.OutcomeAvailable;
            operation.Kind = kind;
            operation.Status = status;
            operation.OutcomeAt = m_clock.GetTimestamp();
            m_retainedBytes += 1024;
            RecordAudit(operation, ServiceOperationAuditAction.OutcomeCommitted);
            if (kind == ServiceOperationOutcome.ExecutionDeadlineExceeded)
            {
                Increment(ref m_deadlines);
            }
            operation.Changed.TrySetResult(true);
        }

        /// <summary>
        /// Releases the shared result without cancelling work and starts a separate history interval.
        /// </summary>
        private void Release(
            Operation operation, ServiceOperationState state, long? releasedAt = null, bool countAction = true)
        {
            if (operation.State is ServiceOperationState.Pending or ServiceOperationState.OutcomeAvailable)
            {
                if (operation.State == ServiceOperationState.OutcomeAvailable)
                {
                    m_retainedBytes -= 1024;
                }
                operation.State = state;
                if (countAction)
                {
                    switch (state)
                    {
                        case ServiceOperationState.Acknowledged:
                            Increment(ref m_acknowledged);
                            RecordAudit(operation, ServiceOperationAuditAction.Acknowledged);
                            break;
                        case ServiceOperationState.Abandoned:
                            Increment(ref m_abandoned);
                            RecordAudit(operation, ServiceOperationAuditAction.Abandoned);
                            break;
                        case ServiceOperationState.Expired:
                            Increment(ref m_expired);
                            RecordAudit(operation, ServiceOperationAuditAction.Expired);
                            break;
                    }
                }
                operation.ReleasedAt = releasedAt ?? m_clock.GetTimestamp();
                operation.Changed.TrySetResult(true);
            }
        }

        /// <summary>
        /// Enforces elapsed-time deadlines even when a timer callback is delayed by contention.
        /// </summary>
        private void Advance(Operation operation)
        {
            long now = m_clock.GetTimestamp();
            if (!operation.WorkFinished &&
                m_clock.GetElapsedTime(operation.AdmittedAt, now) >= operation.Execution)
            {
                RequestCancellation(operation);
                if (operation.State == ServiceOperationState.Pending)
                {
                    CommitOutcome(operation, ServiceOperationOutcome.ExecutionDeadlineExceeded, StatusCodes.BadTimeout);
                }
            }
            if (operation.State == ServiceOperationState.OutcomeAvailable &&
                m_clock.GetElapsedTime(operation.OutcomeAt, now) >= operation.Retention)
            {
                long expiredAt = unchecked(operation.OutcomeAt +
                    (long)(operation.Retention.TotalSeconds * m_clock.TimestampFrequency));
                Release(operation, ServiceOperationState.Expired, expiredAt);
            }
            if (!operation.Forgotten &&
                IsReleased(operation.State) &&
                m_clock.GetElapsedTime(operation.ReleasedAt, now) >= operation.Retention)
            {
                operation.Forgotten = true;
                RecordAudit(operation, ServiceOperationAuditAction.HistoryForgotten);
            }
        }

        /// <summary>
        /// Reports remaining intervals conservatively in whole milliseconds without renewing any window.
        /// </summary>
        private ServiceOperationInfoDataType Snapshot(Operation operation)
        {
            long now = m_clock.GetTimestamp();
            return new ServiceOperationInfoDataType
            {
                ClientOperationId = operation.ClientId,
                ServerToken = operation.Token,
                ServiceId = operation.ServiceId,
                State = operation.State,
                OutcomeKind = operation.Kind,
                OutcomeStatus = operation.Status,
                ExecutionAllowance = operation.Execution.TotalMilliseconds,
                ResultRetentionTime = operation.Retention.TotalMilliseconds,
                ExecutionRemaining = operation.State == ServiceOperationState.Pending
                    ? Remaining(operation.AdmittedAt, operation.Execution, now) : 0,
                ResultRemaining = operation.State == ServiceOperationState.OutcomeAvailable
                    ? Remaining(operation.OutcomeAt, operation.Retention, now) : 0,
                HistoryRemaining = IsReleased(operation.State)
                    ? Remaining(operation.ReleasedAt, operation.Retention, now) : 0,
                CancellationRequested = operation.CancellationRequested
            };
        }

        /// <summary>
        /// Computes a nonnegative lower bound suitable for Client-local retry timing.
        /// </summary>
        private double Remaining(long start, TimeSpan duration, long now)
        {
            return Math.Floor(Math.Max(0, (duration - m_clock.GetElapsedTime(start, now)).TotalMilliseconds));
        }

        /// <summary>
        /// Identifies payload-released states without treating Unknown as an execution conclusion.
        /// </summary>
        private static bool IsReleased(ServiceOperationState state)
        {
            return state is ServiceOperationState.Acknowledged or
                ServiceOperationState.Abandoned or ServiceOperationState.Expired;
        }

        /// <summary>
        /// Requests cancellation asynchronously; a signal never promises that the invocation stopped.
        /// </summary>
        private void RequestCancellation(Operation operation)
        {
            if (operation.WorkFinished || operation.CancellationRequested)
            {
                return;
            }
            operation.CancellationRequested = true;
            RecordAudit(operation, ServiceOperationAuditAction.CancellationRequested);
            operation.CancellationTask = ObserveCancellationAsync(
                operation.Cancellation.CancelAsync(), operation.ClientId);
        }

        /// <summary>
        /// Observes application cancellation callback failures without changing an already committed outcome.
        /// </summary>
        private async Task ObserveCancellationAsync(Task cancellation, Uuid clientId)
        {
            try
            {
                await cancellation.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                m_logger.AsyncServiceCancellationFailed(exception, clientId);
            }
        }

        /// <summary>
        /// Arms only the next unchanged execution, result or history boundary.
        /// </summary>
        private void ArmTimer(Operation operation)
        {
            long now = m_clock.GetTimestamp();
            TimeSpan due = Timeout.InfiniteTimeSpan;
            if (!operation.WorkFinished &&
                (operation.State == ServiceOperationState.Pending || !operation.CancellationRequested))
            {
                due = operation.Execution - m_clock.GetElapsedTime(operation.AdmittedAt, now);
            }
            if (operation.State == ServiceOperationState.OutcomeAvailable)
            {
                TimeSpan result = operation.Retention - m_clock.GetElapsedTime(operation.OutcomeAt, now);
                due = due == Timeout.InfiniteTimeSpan || result < due ? result : due;
            }
            else if (IsReleased(operation.State) && !operation.Forgotten)
            {
                TimeSpan history = operation.Retention - m_clock.GetElapsedTime(operation.ReleasedAt, now);
                due = due == Timeout.InfiniteTimeSpan || history < due ? history : due;
            }
            operation.Timer?.Change(due == Timeout.InfiniteTimeSpan ? due : TimeSpan.FromTicks(Math.Max(0, due.Ticks)),
                Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Makes expiry observable without requiring a caller to poll.
        /// </summary>
        private void OnTimer(Operation operation)
        {
            lock (m_gate)
            {
                Advance(operation);
                ArmTimer(operation);
            }
            Cleanup(operation);
        }

        /// <summary>
        /// Retains minimal active-work identity after history expiry, releasing each reservation only at its boundary.
        /// </summary>
        private void Cleanup(Operation operation)
        {
            IDisposable? execution = null;
            IDisposable? unusedOutcome = null;
            IDisposable? historyBytes = null;
            IDisposable? historyCount = null;
            OutcomeSnapshot? outcome = null;
            OutcomeSnapshot? input = null;
            IServiceRequest? request = null;
            ITimer? timer = null;
            lock (m_gate)
            {
                if (operation.WorkFinished)
                {
                    execution = operation.Reservations.Execution;
                    operation.Reservations.Execution = null;
                    unusedOutcome = operation.Reservations.OutcomeBytes;
                    operation.Reservations.OutcomeBytes = null;
                    request = operation.Request;
                    operation.Request = null;
                }
                if (IsReleased(operation.State) || operation.Forgotten)
                {
                    outcome = operation.Outcome;
                    operation.Outcome = null;
                    if (operation.WorkFinished)
                    {
                        input = operation.Input;
                        operation.Input = null;
                    }
                }
                if (operation.Forgotten && operation.WorkFinished)
                {
                    (ISession Session, Uuid ClientId) key = (operation.Session, operation.ClientId);
                    if (m_byClient.TryGetValue(key, out Operation? current) && ReferenceEquals(current, operation))
                    {
                        m_byClient.Remove(key);
                    }
                    if (m_byToken.TryGetValue(operation.Token, out current) && ReferenceEquals(current, operation))
                    {
                        m_byToken.Remove(operation.Token);
                    }
                    historyBytes = operation.Reservations.HistoryBytes;
                    historyCount = operation.Reservations.HistoryCount;
                    operation.Reservations.HistoryBytes = null;
                    operation.Reservations.HistoryCount = null;
                    timer = operation.Timer;
                    operation.Timer = null;
                }
            }
            FlushAudits();
            execution?.Dispose();
            input?.Release();
            unusedOutcome?.Dispose();
            outcome?.Release();
            (request as IPooledEncodeable)?.Reuse();
            historyBytes?.Dispose();
            historyCount?.Dispose();
            timer?.Dispose();
        }

        /// <summary>
        /// Ends recovery with the owning Session while keeping still-running invocation charges until it stops.
        /// </summary>
        private void OnSessionClosing(ISession session, SessionEventReason reason)
        {
            Operation[] operations;
            lock (m_gate)
            {
                operations = [.. m_byToken.Values.Where(operation => ReferenceEquals(operation.Session, session))];
                foreach (Operation operation in operations)
                {
                    Release(operation, ServiceOperationState.Abandoned, countAction: false);
                    operation.Forgotten = true;
                    RecordAudit(operation, ServiceOperationAuditAction.SessionClosed);
                    RequestCancellation(operation);
                }
            }
            foreach (Operation operation in operations)
            {
                Cleanup(operation);
            }
        }

        /// <summary>
        /// Rejects admission after provider shutdown starts.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ThrowIfStopping()
        {
            if (m_stopping)
            {
                throw new ServiceResultException(StatusCodes.BadServerHalted);
            }
        }

        /// <summary>
        /// Records only the schema's correlation, action and outcome metadata while state is synchronized.
        /// </summary>
        private void RecordAudit(Operation operation, ServiceOperationAuditAction action)
        {
            RecordAudit(operation.ClientId, operation.ServiceId, action, operation.State,
                operation.Kind, operation.Status, succeeded: true, operation.Session.Id);
        }

        /// <summary>
        /// Records authorized delivery selection only after decoding and size checks succeed.
        /// </summary>
        private void RecordCollection(ServiceOperationInfoDataType info, NodeId sessionId)
        {
            if (info.State == ServiceOperationState.OutcomeAvailable)
            {
                lock (m_gate)
                {
                    RecordAudit(info.ClientOperationId, info.ServiceId, ServiceOperationAuditAction.Collected,
                        info.State, info.OutcomeKind, info.OutcomeStatus, succeeded: true, sessionId);
                }
            }
        }

        /// <summary>
        /// Captures an audit action without a raw token, credentials or a duplicate request/result payload.
        /// </summary>
        private void RecordAudit(
            Uuid clientId, NodeId serviceId, ServiceOperationAuditAction action, ServiceOperationState state,
            ServiceOperationOutcome kind, StatusCode status, bool succeeded, NodeId sessionId)
        {
            if (m_auditServer.Auditing)
            {
                m_audits.Enqueue(new OperationAudit(
                    clientId, serviceId, action, state, kind, status, succeeded, sessionId,
                    m_clock.GetUtcNow().UtcDateTime));
            }
        }

        /// <summary>
        /// Correlates a denied lookup for privileged auditing without revealing that correlation to the caller.
        /// </summary>
        private void RecordDenied(OperationContext context, Uuid clientId, ByteString token)
        {
            lock (m_gate)
            {
                Operation? operation;
                if (clientId != Uuid.Empty)
                {
                    m_byClient.TryGetValue((context.Session, clientId), out operation);
                }
                else
                {
                    m_byToken.TryGetValue(token, out operation);
                }
                RecordAudit(operation != null ? operation.ClientId : clientId,
                    operation != null ? operation.ServiceId : NodeId.Null,
                    ServiceOperationAuditAction.Denied, ServiceOperationState.Unknown,
                    ServiceOperationOutcome.None, StatusCodes.Good, succeeded: false, context.SessionId);
            }
        }

        /// <summary>
        /// Uses the existing auditing gate and report path outside operation synchronization.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void FlushAudits()
        {
            OperationAudit[] audits;
            lock (m_gate)
            {
                audits = [.. m_audits];
                m_audits.Clear();
            }
            foreach (OperationAudit audit in audits)
            {
                if (!m_auditServer.Auditing)
                {
                    continue;
                }
                try
                {
                    ISystemContext context = m_auditServer.DefaultAuditContext;
                    var entry = new AuditServiceOperationEventState(null);
                    entry.Initialize(context, null, EventSeverity.Min,
                        new LocalizedText("Recoverable Service operation."), audit.Succeeded, audit.Timestamp);
                    if (!entry.SetChildValue(context, BrowseNames.SourceNode, Variant.From(ObjectIds.Server), false) ||
                        !entry.SetChildValue(context, BrowseNames.SourceName, Variant.From("AsyncServices"), false) ||
                        !entry.SetChildValue(context, BrowseNames.SessionId, Variant.From(audit.SessionId), false) ||
                        !entry.SetChildValue(
                            context, BrowseNames.ClientOperationId, Variant.From(audit.ClientId), false) ||
                        !entry.SetChildValue(context, BrowseNames.ServiceId, Variant.From(audit.ServiceId), false) ||
                        !entry.SetChildValue(context, BrowseNames.Action, audit.Action) ||
                        !entry.SetChildValue(context, BrowseNames.OperationState, audit.State) ||
                        !entry.SetChildValue(context, BrowseNames.OutcomeKind, audit.Kind) ||
                        !entry.SetChildValue(context, BrowseNames.OutcomeStatus, Variant.From(audit.Status), false))
                    {
                        throw new InvalidOperationException("The generated audit event is missing a contract field.");
                    }
                    m_auditServer.ReportAuditEvent(context, entry);
                }
                catch (Exception exception)
                {
                    m_logger.AsyncServiceAuditFailed(exception);
                }
            }
        }

        /// <summary>
        /// Holds bounded audit metadata; in particular there is no Server token or original payload field.
        /// </summary>
        private readonly record struct OperationAudit(
            Uuid ClientId, NodeId ServiceId, ServiceOperationAuditAction Action, ServiceOperationState State,
            ServiceOperationOutcome Kind, StatusCode Status, bool Succeeded, NodeId SessionId, DateTime Timestamp);

        /// <summary>
        /// Keeps Client IDs in one actual Session incarnation even if a Server reuses a public Session identifier.
        /// </summary>
        private sealed class SessionClientIdComparer : IEqualityComparer<(ISession Session, Uuid ClientId)>
        {
            /// <inheritdoc/>
            public bool Equals(
                (ISession Session, Uuid ClientId) x, (ISession Session, Uuid ClientId) y)
            {
                return ReferenceEquals(x.Session, y.Session) && x.ClientId == y.ClientId;
            }

            /// <inheritdoc/>
            public int GetHashCode((ISession Session, Uuid ClientId) value)
            {
                return HashCode.Combine(RuntimeHelpers.GetHashCode(value.Session), value.ClientId);
            }
        }

        /// <summary>
        /// Holds the existing isolation leases at their independent ownership boundaries.
        /// </summary>
        private sealed class Reservations : IDisposable
        {
            public IDisposable? Execution { get; set; }
            public IDisposable? InputBytes { get; set; }
            public IDisposable? OutcomeBytes { get; set; }
            public IDisposable? HistoryBytes { get; set; }
            public IDisposable? HistoryCount { get; set; }

            public void Dispose()
            {
                Execution?.Dispose();
                InputBytes?.Dispose();
                OutcomeBytes?.Dispose();
                HistoryBytes?.Dispose();
                HistoryCount?.Dispose();
            }
        }

        /// <summary>
        /// Separates admitted execution state from each independently cancellable exchange.
        /// </summary>
        private sealed class Operation
        {
            public Operation(
                AsyncServiceExecutionProvider owner, OperationContext admitted, IServiceRequest request, byte[] input,
                RequestType requestType, Uuid clientId, ByteString token, ByteString creator,
                TimeSpan execution, TimeSpan retention, ServiceMessageContext encoding, int limit,
                Reservations reservations)
            {
                Session = admitted.Session;
                ClientId = clientId;
                Token = token;
                Creator = creator;
                Execution = execution;
                Retention = retention;
                Encoding = encoding;
                Limit = limit;
                Reservations = reservations;
                Roles = admitted.UserIdentity.GrantedRoleIds.Span.ToArray();
                Request = request;
                ServiceId = ExpandedNodeId.ToNodeId(request.TypeId, encoding.NamespaceUris);
                Mapping = admitted.ChannelContext!.MessageEncoding;
                Cancellation = new CancellationTokenSource();
                Lifetime = new RequestLifetime(Cancellation.Token);
                Context = new OperationContext(admitted, request.RequestHeader, requestType, Lifetime);
                Timer = owner.m_clock.CreateTimer(
                    _ => owner.OnTimer(this), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                Input = new OutcomeSnapshot(input, encoding, reservations.InputBytes!);
                reservations.InputBytes = null;
            }

            public ISession Session { get; }
            public Uuid ClientId { get; }
            public ByteString Token { get; }
            public ByteString Creator { get; }
            public ArrayOf<NodeId> Roles { get; }
            public NodeId ServiceId { get; }
            public TimeSpan Execution { get; }
            public TimeSpan Retention { get; }
            public ServiceMessageContext Encoding { get; }
            public int Limit { get; }
            public RequestEncoding Mapping { get; }
            public Reservations Reservations { get; }
            public CancellationTokenSource Cancellation { get; }
            public RequestLifetime Lifetime { get; }
            public OperationContext? Context { get; set; }
            public IServiceRequest? Request { get; set; }
            public OutcomeSnapshot? Outcome { get; set; }
            public OutcomeSnapshot? Input { get; set; }
            public ITimer? Timer { get; set; }
            public Task Work { get; set; } = Task.CompletedTask;
            public Task CancellationTask { get; set; } = Task.CompletedTask;
            public TaskCompletionSource<bool> Changed { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public long AdmittedAt { get; set; }
            public long OutcomeAt { get; set; }
            public long ReleasedAt { get; set; }
            public ServiceOperationState State { get; set; } = ServiceOperationState.Pending;
            public ServiceOperationOutcome Kind { get; set; }
            public StatusCode Status { get; set; }
            public bool CancellationRequested { get; set; }
            public bool WorkFinished { get; set; }
            public bool Forgotten { get; set; }

            public void DisposeUnadmitted()
            {
                Timer?.Dispose();
                Context?.Dispose();
                Lifetime.Dispose();
                Cancellation.Dispose();
                (Request as IPooledEncodeable)?.Reuse();
                Input?.Release();
            }
        }

        /// <summary>
        /// Shares an immutable encoded original response while retained and in-flight owners remain charged.
        /// </summary>
        private sealed class OutcomeSnapshot(
            byte[] bytes, IServiceMessageContext context, IDisposable reservation, Action? onReleased = null)
        {
            public void Retain()
            {
                Interlocked.Increment(ref m_references);
            }

            public void Release()
            {
                if (Interlocked.Decrement(ref m_references) == 0)
                {
                    reservation.Dispose();
                    onReleased?.Invoke();
                }
            }

            public ExtensionObject Decode()
            {
                return new ExtensionObject(BinaryDecoder.DecodeMessage<IServiceResponse>(bytes, context));
            }

            public IServiceRequest DecodeRequest()
            {
                return BinaryDecoder.DecodeMessage<IServiceRequest>(bytes, context);
            }

            private int m_references = 1;
        }

        /// <summary>
        /// Holds an outgoing submission's snapshot and transferred request charge through wire serialization.
        /// </summary>
        private sealed class RetainedSubmitResponse(OutcomeSnapshot? snapshot) :
            SubmitServiceResponse, IResponseResourceRetention
        {
            public void AttachRequestResources(IDisposable resources)
            {
                m_resources = resources;
            }

            protected override void ReuseCore()
            {
                if (Interlocked.Exchange(ref m_released, 1) != 0)
                {
                    return;
                }
                if (Result.TryGetValue(out IEncodeable? original))
                {
                    (original as IPooledEncodeable)?.Reuse();
                }
                ResetForReuse();
                snapshot?.Release();
                Interlocked.Exchange(ref m_resources, null)?.Dispose();
            }

            private IDisposable? m_resources;
            private int m_released;
        }

        /// <summary>
        /// Holds an outgoing collection's snapshot and transferred request charge through wire serialization.
        /// </summary>
        private sealed class RetainedGetResponse(OutcomeSnapshot? snapshot) :
            GetServiceResultResponse, IResponseResourceRetention
        {
            public void AttachRequestResources(IDisposable resources)
            {
                m_resources = resources;
            }

            protected override void ReuseCore()
            {
                if (Interlocked.Exchange(ref m_released, 1) != 0)
                {
                    return;
                }
                if (Result.TryGetValue(out IEncodeable? original))
                {
                    (original as IPooledEncodeable)?.Reuse();
                }
                ResetForReuse();
                snapshot?.Release();
                Interlocked.Exchange(ref m_resources, null)?.Dispose();
            }

            private IDisposable? m_resources;
            private int m_released;
        }

        /// <summary>
        /// Enforces the existing message-size limit while encoding, rather than after an unbounded allocation.
        /// </summary>
        private sealed class CappedMemoryStream : MemoryStream
        {
            public CappedMemoryStream(int capacity)
                : base(new byte[capacity], 0, capacity, writable: true, publiclyVisible: true)
            {
                SetLength(0);
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                CheckCapacity(count);
                base.Write(buffer, offset, count);
            }

            public override void WriteByte(byte value)
            {
                CheckCapacity(1);
                base.WriteByte(value);
            }

#if NET8_0_OR_GREATER
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                CheckCapacity(buffer.Length);
                base.Write(buffer);
            }
#endif

            private void CheckCapacity(int count)
            {
                if (Position > Capacity - count)
                {
                    throw new ServiceResultException(StatusCodes.BadResponseTooLarge);
                }
            }
        }

        private readonly StandardServer m_server;
        private readonly AsyncServiceExecutionOptions m_options;
        private readonly TimeProvider m_clock;
        private readonly IAsyncServiceAuthorizationPolicy? m_authorizationPolicy;
        private readonly int m_maxMessageSize;
        private readonly IServerResourceIsolationProvider? m_isolation;
        private readonly ILogger m_logger;
        private readonly IAuditEventServer m_auditServer;
        private readonly Lock m_gate = new();

        private readonly Dictionary<(ISession Session, Uuid ClientId), Operation> m_byClient =
            new(new SessionClientIdComparer());

        private readonly Dictionary<ByteString, Operation> m_byToken = [];
        private readonly Queue<OperationAudit> m_audits = [];
        private bool m_stopping;
        private ulong m_retainedBytes;
        private ulong m_admitted;
        private ulong m_rejected;
        private ulong m_acknowledged;
        private ulong m_abandoned;
        private ulong m_expired;
        private ulong m_deadlines;
    }

    /// <summary>
    /// Reports bounded operation metadata without raw Server tokens or payloads.
    /// </summary>
    internal static partial class AsyncServiceExecutionProviderLog
    {
        [LoggerMessage(EventId = ServerEventIds.AsyncServiceExecution + 0, Level = LogLevel.Warning,
            Message = "Async Service {ClientOperationId} retained an unavailable-result outcome: {Status}.")]
        public static partial void AsyncServiceResultUnavailable(
            this ILogger logger, Uuid clientOperationId, StatusCode status);

        [LoggerMessage(EventId = ServerEventIds.AsyncServiceExecution + 1, Level = LogLevel.Error,
            Message = "Async Service {ClientOperationId} cancellation callback failed.")]
        public static partial void AsyncServiceCancellationFailed(
            this ILogger logger, Exception exception, Uuid clientOperationId);

        [LoggerMessage(EventId = ServerEventIds.AsyncServiceExecution + 2, Level = LogLevel.Error,
            Message = "Async Service metadata auditing failed.")]
        public static partial void AsyncServiceAuditFailed(this ILogger logger, Exception exception);
    }
}
