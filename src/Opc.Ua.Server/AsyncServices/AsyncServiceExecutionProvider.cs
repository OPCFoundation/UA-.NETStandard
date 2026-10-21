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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Redundancy;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Implements bounded recoverable execution through existing ordinary handlers without submission replay.
    /// </summary>
    public sealed partial class AsyncServiceExecutionProvider : IAsyncServiceExecutionProvider
    {
        /// <summary>
        /// Creates a Server-owned provider using existing message limits, admission pools and an elapsed-time clock.
        /// </summary>
        public AsyncServiceExecutionProvider(
            StandardServer server,
            ApplicationConfiguration configuration,
            AsyncServiceExecutionOptions options,
            TimeProvider? timeProvider = null,
            IAsyncServiceAuthorizationPolicy? authorizationPolicy = null,
            IRecoverableExecutionOwnerProvider? ownerProvider = null,
            IRecoverableExecutionEligibilityProvider? eligibilityProvider = null,
            ISharedKeyValueStore? durableStore = null,
            IRecordProtector? recordProtector = null,
            IRecoverableExecutionClock? durableClock = null,
            IRecoverableExecutionCodecQualifier? codecQualifier = null)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            m_options = (options ?? throw new ArgumentNullException(nameof(options))).Snapshot();
            m_clock = timeProvider ?? TimeProvider.System;
            m_authorizationPolicy = authorizationPolicy;
            m_ownerProvider = ownerProvider;
            m_eligibilityProvider = eligibilityProvider;
            m_durableStore = durableStore;
            m_recordProtector = recordProtector;
            m_durableClock = durableClock;
            m_typeCompatibility = new RecoverableExecutionTypeCompatibility(codecQualifier);
            m_logicalServerId = configuration.ApplicationUri;
            m_maxRestoredOperations = Math.Max(100, configuration.ServerConfiguration?.MaxQueuedRequestCount ?? 0);
            m_maxMessageSize = configuration.TransportQuotas?.MaxMessageSize ??
                throw new ArgumentException("Transport quotas are required.", nameof(configuration));
            m_isolation = server.ResourceIsolationProvider;
            if (m_options.RecoveryModes.Contains(ServiceOperationRecoveryMode.Durable) &&
                (durableStore == null ||
                    recordProtector == null ||
                    durableClock == null ||
                    eligibilityProvider is not IRecoverableExecutionDurableEligibilityProvider ||
                    m_maxMessageSize <= 0 ||
                    !Uri.TryCreate(m_logicalServerId, UriKind.Absolute, out _)))
            {
                throw new ArgumentException(
                    "Durable recovery requires storage, protection, qualified time and recoverable eligibility.",
                    nameof(options));
            }
            if ((m_options.RecoveryModes.Contains(ServiceOperationRecoveryMode.Owner) ||
                m_options.RecoveryModes.Contains(ServiceOperationRecoveryMode.Durable)) &&
                (ownerProvider == null ||
                    eligibilityProvider == null ||
                    m_isolation is not IRecoverableExecutionResourceIsolationProvider))
            {
                throw new ArgumentException(
                    "Owner recovery requires owner, eligibility and stable-accounting providers.", nameof(options));
            }
            m_auditServer = server.CurrentInstance;
            m_logger = server.MessageContext.Telemetry.CreateLogger<AsyncServiceExecutionProvider>();
            server.CurrentInstance.SessionManager.SessionClosing += OnSessionClosing;
        }

        /// <inheritdoc/>
        public ArrayOf<NodeId> SupportedServices =>
        [
            DataTypeIds.ReadRequest, DataTypeIds.WriteRequest, DataTypeIds.CallRequest,
            DataTypeIds.HistoryReadRequest, DataTypeIds.HistoryUpdateRequest,
            DataTypeIds.BrowseRequest, DataTypeIds.BrowseNextRequest, DataTypeIds.TranslateBrowsePathsToNodeIdsRequest,
            DataTypeIds.RegisterNodesRequest, DataTypeIds.UnregisterNodesRequest,
            DataTypeIds.AddNodesRequest, DataTypeIds.AddReferencesRequest,
            DataTypeIds.DeleteNodesRequest, DataTypeIds.DeleteReferencesRequest
        ];

        /// <inheritdoc/>
        public ArrayOf<ServiceOperationRecoveryMode> SupportedRecoveryModes =>
            m_options.RecoveryModes.Contains(ServiceOperationRecoveryMode.Durable) && m_journal?.IsReady != true
                ? m_options.RecoveryModes.Span.ToArray()
                    .Where(static mode => mode != ServiceOperationRecoveryMode.Durable).ToArray()
                : m_options.RecoveryModes;

        /// <inheritdoc/>
        public ValueTask InitializeAsync(CancellationToken ct)
        {
            return InitializeDurableAsync(ct);
        }

        /// <inheritdoc/>
        public void BindManager(RecoverableExecutionManagerState manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            var binding = new ManagerBinding(
                manager.NodeId, manager.Submit!.NodeId, manager.GetResult!.NodeId,
                manager.Acknowledge!.NodeId, manager.Cancel!.NodeId, manager.Abandon!.NodeId);
            if (Interlocked.CompareExchange(ref m_binding, binding, null) != null)
            {
                throw new InvalidOperationException("The recoverable execution manager is already bound.");
            }
        }

        /// <inheritdoc/>
        public bool IsManagerCall(CallMethodRequest request)
        {
            ManagerBinding? binding = Volatile.Read(ref m_binding);
            return binding != null &&
                request.ObjectId == binding.Manager &&
                (request.MethodId == binding.Submit ||
                    request.MethodId == binding.GetResult ||
                    request.MethodId == binding.Acknowledge ||
                    request.MethodId == binding.Cancel ||
                    request.MethodId == binding.Abandon);
        }

        /// <inheritdoc/>
        public bool IsControlRequest(IServiceRequest request)
        {
            ManagerBinding? binding = Volatile.Read(ref m_binding);
            if (binding == null || request is not CallRequest { MethodsToCall.Count: > 0 } call)
            {
                return false;
            }
            foreach (CallMethodRequest item in call.MethodsToCall)
            {
                if (!IsManagerCall(item) || item.MethodId == binding.Submit)
                {
                    return false;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public void ValidateCallResponse(OperationContext context, CallResponse response)
        {
            CheckDeliverySize(response, context);
        }

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
                    m_admitted, m_rejected, m_acknowledged, m_abandoned, m_expired, m_deadlines, m_indeterminate);
            }
        }

        /// <inheritdoc/>
        public bool CanPark(IServiceRequest request)
        {
            ManagerBinding? binding = Volatile.Read(ref m_binding);
            if (binding == null || request is not CallRequest { MethodsToCall.Count: > 0 } call)
            {
                return false;
            }
            bool canWait = false;
            foreach (CallMethodRequest item in call.MethodsToCall)
            {
                if (!IsManagerCall(item))
                {
                    return false;
                }
                canWait |= item.MethodId == binding.Submit || item.MethodId == binding.GetResult;
            }
            return canWait;
        }

        /// <inheritdoc/>
        public async ValueTask<SubmitResponseDataType> SubmitAsync(
            OperationContext context, SubmitRequestDataType request, CancellationToken ct)
        {
            using IDisposable exchange = EnterExchange();
            using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(ct, m_shutdown.Token);
            ct = shutdown.Token;
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
            Uuid clientId = request.ClientOperationId;
            ServiceOperationRecoveryMode mode = request.RecoveryMode;
            try
            {
                TimeSpan execution = ValidateDuration(
                    request.ExecutionAllowance, m_options.MinExecutionAllowance, m_options.MaxExecutionAllowance);
                TimeSpan retention = ValidateDuration(
                    request.ResultRetentionTime, m_options.MinResultRetentionTime, m_options.MaxResultRetentionTime);
                TimeSpan wait = ValidateWait(request.WaitTime);
                ValidateRecoveryMode(mode);
                if (clientId == Uuid.Empty ||
                    !request.Request.TryGetValue(out IServiceRequest? original, m_server.MessageContext) ||
                    original.RequestHeader == null)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument);
                }
                (RequestType requestType, NodeId expectedService) = original switch
                {
                    ReadRequest => (RequestType.Read, DataTypeIds.ReadRequest),
                    WriteRequest => (RequestType.Write, DataTypeIds.WriteRequest),
                    CallRequest => (RequestType.Call, DataTypeIds.CallRequest),
                    HistoryReadRequest => (RequestType.HistoryRead, DataTypeIds.HistoryReadRequest),
                    HistoryUpdateRequest => (RequestType.HistoryUpdate, DataTypeIds.HistoryUpdateRequest),
                    BrowseRequest => (RequestType.Browse, DataTypeIds.BrowseRequest),
                    BrowseNextRequest => (RequestType.BrowseNext, DataTypeIds.BrowseNextRequest),
                    TranslateBrowsePathsToNodeIdsRequest => (RequestType.TranslateBrowsePathsToNodeIds,
                        DataTypeIds.TranslateBrowsePathsToNodeIdsRequest),
                    RegisterNodesRequest => (RequestType.RegisterNodes, DataTypeIds.RegisterNodesRequest),
                    UnregisterNodesRequest => (RequestType.UnregisterNodes, DataTypeIds.UnregisterNodesRequest),
                    AddNodesRequest => (RequestType.AddNodes, DataTypeIds.AddNodesRequest),
                    AddReferencesRequest => (RequestType.AddReferences, DataTypeIds.AddReferencesRequest),
                    DeleteNodesRequest => (RequestType.DeleteNodes, DataTypeIds.DeleteNodesRequest),
                    DeleteReferencesRequest => (RequestType.DeleteReferences, DataTypeIds.DeleteReferencesRequest),
                    _ => throw new ServiceResultException(StatusCodes.BadServiceUnsupported)
                };
                if (original is CallRequest nested)
                {
                    foreach (CallMethodRequest item in nested.MethodsToCall)
                    {
                        if (IsManagerMethod(item))
                        {
                            throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
                        }
                    }
                }
                submittedService = ExpandedNodeId.ToNodeId(original.TypeId, m_server.MessageContext.NamespaceUris);
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
                    original.RequestHeader.AuthenticationToken != context.AuthenticationToken)
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
                RecoverableExecutionOwner? owner = await ResolveOwnerAsync(context, mode, ct).ConfigureAwait(false);
                if (mode == ServiceOperationRecoveryMode.Durable)
                {
                    await VerifyDurableReadinessAsync(ct).ConfigureAwait(false);
                }
                await AuthorizeAsync(context, clientId,
                    ExpandedNodeId.ToNodeId(original.TypeId, m_server.MessageContext.NamespaceUris),
                    AsyncServiceAccess.Submit, ct).ConfigureAwait(false);
                await nodeManager.ValidateRecoverableServiceEligibilityAsync(context, original, ct)
                    .ConfigureAwait(false);
                OperationKey key = CreateKey(context.Session, creator, owner, clientId);
                lock (m_gate)
                {
                    ThrowIfStopping();
                    if (m_byClient.ContainsKey(key))
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidState);
                    }
                }

                Reservations reservations = Reserve(context, context.AuthenticationToken, limit, owner, mode);
                var preparation = new AdmissionPreparation(reservations);
                IAsyncDisposable? eligibility = null;
                try
                {
                    RecoverableExecutionTypeContext? types = mode == ServiceOperationRecoveryMode.Durable
                        ? await RestoreDurableTypesAsync(limit, ct).ConfigureAwait(false) : null;
                    ServiceMessageContext encoding = TransportChannelBinding.CopyContext(
                        types?.MessageContext ?? m_server.MessageContext);
                    encoding.MaxMessageSize = limit;
                    byte[] input = types == null
                        ? EncodeBinary(original, encoding) : types.Encode(original).Span.ToArray();
                    IServiceRequest ownedRequest = types == null
                        ? BinaryDecoder.DecodeMessage<IServiceRequest>(input, encoding)
                        : types.Decode<IServiceRequest>(new ByteString(input));
                    preparation.Request = ownedRequest;
                    Uuid executionId = mode == ServiceOperationRecoveryMode.Durable
                        ? new Uuid(Guid.NewGuid()) : Uuid.Empty;
                    RecoverableExecutionIdentity? independentIdentity = null;
                    if (owner != null)
                    {
                        if (ownedRequest is BrowseNextRequest ||
                            (ownedRequest is HistoryReadRequest history &&
                                HasContinuationPoints(history.NodesToRead)))
                        {
                            throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
                        }
                        independentIdentity = new RecoverableExecutionIdentity(
                            context.UserIdentity, context.Session.Identity, encoding);
                        eligibility = (mode == ServiceOperationRecoveryMode.Durable
                            ? await ((IRecoverableExecutionDurableEligibilityProvider)m_eligibilityProvider!)
                                .AcquireDurableAsync(context, ownedRequest, owner, executionId, ct)
                                .ConfigureAwait(false)
                            : await m_eligibilityProvider!.AcquireAsync(context, ownedRequest, mode, owner, ct)
                                .ConfigureAwait(false)) ??
                            throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
                        preparation.Eligibility = eligibility;
                        await m_ownerProvider!.ValidatePersistedOwnerAsync(owner, ct).ConfigureAwait(false);
                        RecoverableExecutionOwner currentOwner = await m_ownerProvider.ResolveAsync(context, ct)
                            .ConfigureAwait(false);
                        if (currentOwner.Id != owner.Id ||
                            !EncodeBinary(ownedRequest, encoding).AsSpan().SequenceEqual(input))
                        {
                            throw new ServiceResultException(StatusCodes.BadServiceUnsupported,
                                "Eligibility must preserve the validated owner and exact original request.");
                        }
                    }
                    var operation = new Operation(
                        this, context, ownedRequest, input, requestType, clientId, NewToken(),
                        creator, execution, retention, encoding, limit, reservations, key, owner, mode,
                        independentIdentity, eligibility, types, executionId);
                    preparation.Operation = operation;
                    preparation.Request = null;
                    preparation.Eligibility = null;
                    preparation.Lease = RetainOperation(operation);
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
                        operation.Indexed = true;
                        operation.References++;
                        admitted = true;
                        preparation.Admitted = true;
                        if (mode != ServiceOperationRecoveryMode.Durable)
                        {
                            StartAdmittedOperation(operation);
                        }
                    }
                    if (mode == ServiceOperationRecoveryMode.Durable)
                    {
                        try
                        {
                            await AdmitDurableAsync(operation, ct).ConfigureAwait(false);
                        }
                        catch
                        {
                            if (!operation.AdmissionWriteStarted || m_journal?.IsReady == true)
                            {
                                RemoveUnadmittedOperation(operation);
                                admitted = false;
                                preparation.Admitted = false;
                            }
                            else
                            {
                                await FinishInvocationAsync(operation).ConfigureAwait(false);
                            }
                            throw;
                        }
                    }
                    await WaitAsync(operation, wait, operation.AdmittedAt, context.RequestLifetime, submitted: true, ct)
                        .ConfigureAwait(false);
                    using OperationContext current = await RevalidateAsync(
                        context, operation, AsyncServiceAccess.Collect, submitted: true)
                        .ConfigureAwait(false);
                    RetainedSubmitResponse response = await CreateSubmissionResponseAsync(current, operation)
                        .ConfigureAwait(false);
                    context.RetainResponseResource(response);
                    return response;
                }
                finally
                {
                    await preparation.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                if (!admitted)
                {
                    lock (m_gate)
                    {
                        Increment(ref m_rejected);
                        RecordAudit(clientId, submittedService,
                            ServiceOperationAuditAction.Rejected, ServiceOperationState.Unknown,
                            ServiceOperationOutcome.None, StatusCodes.Good, succeeded: false, context.SessionId);
                    }
                }
                if (exception is OperationCanceledException && m_shutdown.IsCancellationRequested)
                {
                    throw new ServiceResultException(StatusCodes.BadServerHalted, exception);
                }
                throw;
            }
            finally
            {
                FlushAudits();
            }
        }

        /// <inheritdoc/>
        public async ValueTask<GetResultResponseDataType> GetAsync(
            OperationContext context, GetResultRequestDataType request, CancellationToken ct)
        {
            using IDisposable exchange = EnterExchange();
            using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(ct, m_shutdown.Token);
            ct = shutdown.Token;
            try
            {
                return await GetCoreAsync(context, request, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadUserAccessDenied)
            {
                RecordDenied(context, request.ClientOperationId, request.ServerToken);
                throw;
            }
            catch (OperationCanceledException exception) when (m_shutdown.IsCancellationRequested)
            {
                throw new ServiceResultException(StatusCodes.BadServerHalted, exception);
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
        private async ValueTask<GetResultResponseDataType> GetCoreAsync(
            OperationContext context, GetResultRequestDataType request, CancellationToken ct)
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
            ValidateRecoveryMode(request.RecoveryMode);
            bool clientId = request.ClientOperationId != Uuid.Empty;
            bool token = !request.ServerToken.IsNull;
            if (clientId == token || (token && !IsToken(request.ServerToken)))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            ct.ThrowIfCancellationRequested();
            RecoverableExecutionOwner? caller = await ResolveOwnerAsync(context, request.RecoveryMode, ct)
                .ConfigureAwait(false);
            Operation? operation;
            OperationLease? operationLease = null;
            lock (m_gate)
            {
                if (clientId)
                {
                    m_byClient.TryGetValue(CreateKey(
                        context.Session, GetCreator(context.Session), caller, request.ClientOperationId),
                        out operation);
                }
                else
                {
                    m_byToken.TryGetValue(request.ServerToken, out operation);
                }
                if (operation != null &&
                    (operation.Mode != request.RecoveryMode || !IsOwner(context, operation, caller)))
                {
                    operation = null;
                }
                if (operation != null)
                {
                    ValidateOwner(context, operation, caller);
                    Advance(operation);
                    operationLease = RetainOperation(operation);
                }
            }
            using OperationLease? heldOperation = operationLease;
            await AuthorizeAsync(context, operation != null ? operation.ClientId : request.ClientOperationId,
                operation != null ? operation.ServiceId : NodeId.Null, AsyncServiceAccess.Collect, ct)
                .ConfigureAwait(false);
            long waitStarted = m_clock.GetTimestamp();
            if (operation != null)
            {
                Cleanup(operation);
                await WaitAsync(operation, wait, waitStarted, context.RequestLifetime, submitted: false, ct)
                    .ConfigureAwait(false);
                using OperationContext current = await RevalidateAsync(
                    context, operation, AsyncServiceAccess.Collect)
                    .ConfigureAwait(false);
                RetainedGetResponse response = await CreateCollectionResponseAsync(
                    current, operation, request.ClientOperationId).ConfigureAwait(false);
                context.RetainResponseResource(response);
                return response;
            }
            if (request.RecoveryMode == ServiceOperationRecoveryMode.Durable)
            {
                await VerifyDurableReadinessAsync(ct).ConfigureAwait(false);
                ValidateCurrentIdentity(context);
            }
            return new GetResultResponseDataType
            {
                Operation = Unknown(request.ClientOperationId)
            };
        }

        /// <inheritdoc/>
        public ValueTask<ControlResponseDataType> AcknowledgeAsync(
            OperationContext context, OperationControlRequestDataType request, CancellationToken ct)
        {
            return ControlAsync(context, request, AsyncServiceAccess.Acknowledge, ct);
        }

        /// <inheritdoc/>
        public ValueTask<ControlResponseDataType> CancelAsync(
            OperationContext context, OperationControlRequestDataType request, CancellationToken ct)
        {
            return ControlAsync(context, request, AsyncServiceAccess.Cancel, ct);
        }

        /// <inheritdoc/>
        public ValueTask<ControlResponseDataType> AbandonAsync(
            OperationContext context, OperationControlRequestDataType request, CancellationToken ct)
        {
            return ControlAsync(context, request, AsyncServiceAccess.Abandon, ct);
        }

        /// <summary>
        /// Applies the invoked Method's action and audits denied access without exposing a token.
        /// </summary>
        private async ValueTask<ControlResponseDataType> ControlAsync(
            OperationContext context, OperationControlRequestDataType request, AsyncServiceAccess access,
            CancellationToken ct)
        {
            using IDisposable exchange = EnterExchange();
            using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(ct, m_shutdown.Token);
            ct = shutdown.Token;
            try
            {
                return await ControlCoreAsync(context, request, access, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadUserAccessDenied)
            {
                RecordDenied(context, default, request.ServerToken);
                throw;
            }
            catch (OperationCanceledException exception) when (m_shutdown.IsCancellationRequested)
            {
                throw new ServiceResultException(StatusCodes.BadServerHalted, exception);
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
        private async ValueTask<ControlResponseDataType> ControlCoreAsync(
            OperationContext context, OperationControlRequestDataType request, AsyncServiceAccess access,
            CancellationToken ct)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            if (!IsToken(request.ServerToken))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            ct.ThrowIfCancellationRequested();
            RecoverableExecutionOwner? caller = await ResolveControlOwnerAsync(context, ct).ConfigureAwait(false);
            Operation? operation;
            OperationLease? operationLease;
            lock (m_gate)
            {
                m_byToken.TryGetValue(request.ServerToken, out operation);
                operationLease = operation == null ? null : RetainOperation(operation);
            }
            using OperationLease? heldOperation = operationLease;
            if (operation != null)
            {
                if (!IsOwner(context, operation, caller))
                {
                    operation = null;
                }
                else
                {
                    ValidateOwner(context, operation, caller);
                }
            }
            await AuthorizeAsync(context, operation != null ? operation.ClientId : Uuid.Empty,
                operation != null ? operation.ServiceId : NodeId.Null, access, ct).ConfigureAwait(false);
            if (operation == null)
            {
                if (caller != null && m_options.RecoveryModes.Contains(ServiceOperationRecoveryMode.Durable))
                {
                    await VerifyDurableReadinessAsync(ct).ConfigureAwait(false);
                    ValidateCurrentIdentity(context);
                }
                return new ControlResponseDataType
                {
                    Result = ServiceOperationControlResult.UnknownOperation,
                    Operation = Unknown(default)
                };
            }
            using OperationContext current = await RevalidateAsync(context, operation, access)
                .ConfigureAwait(false);
            if (operation.Mode == ServiceOperationRecoveryMode.Durable)
            {
                return await ControlDurableAsync(current, operation, access, ct).ConfigureAwait(false);
            }
            ServiceOperationControlResult result;
            ServiceOperationInfoDataType info;
            lock (m_gate)
            {
                ct.ThrowIfCancellationRequested();
                ValidateCurrentIdentity(current);
                Advance(operation);
                result = operation.Forgotten
                    ? ServiceOperationControlResult.UnknownOperation
                    : ApplyControl(operation, access);
                info = operation.Forgotten ? Unknown(default) : Snapshot(operation);
                ArmTimer(operation);
            }
            Cleanup(operation);
            return new ControlResponseDataType
            {
                Result = result,
                Operation = info
            };
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Task stopped;
            lock (m_gate)
            {
                if (m_stopTask == null)
                {
                    m_stopping = true;
                    if (m_exchanges == 0)
                    {
                        m_exchangesDrained.TrySetResult(true);
                    }
                    using (ExecutionContext.SuppressFlow())
                    {
                        m_stopTask = Task.Run(StopCoreAsync, CancellationToken.None);
                    }
                }
                stopped = m_stopTask;
            }
            await stopped.ConfigureAwait(false);
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                m_journal?.Dispose();
                m_shutdown.Dispose();
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
            if (value < minimum ||
                value > maximum ||
                !RecoverableExecutionDurations.TryFromMilliseconds(value, out TimeSpan duration))
            {
                throw new ServiceResultException(StatusCodes.BadOutOfRange);
            }
            return duration;
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
        /// Rejects invalid or unavailable modes before any dispatch, without downgrading.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateRecoveryMode(ServiceOperationRecoveryMode mode)
        {
            if (mode is < ServiceOperationRecoveryMode.Session or > ServiceOperationRecoveryMode.Durable)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument);
            }
            if (!m_options.RecoveryModes.Contains(mode))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }
            if (mode == ServiceOperationRecoveryMode.Durable)
            {
                RequireDurableReady();
            }
        }

        /// <summary>
        /// Checks the protocol's nonempty bounded opaque token representation.
        /// </summary>
        private static bool IsToken(ByteString token)
        {
            return token.Length is > 0 and <= 64;
        }

        /// <summary>
        /// Rejects a nested manager Method before any sibling of the original Call can execute.
        /// </summary>
        private bool IsManagerMethod(CallMethodRequest request)
        {
            ManagerBinding? binding = Volatile.Read(ref m_binding);
            if (binding == null)
            {
                return false;
            }
            if (request.ObjectId == binding.Manager ||
                request.MethodId == binding.Submit ||
                request.MethodId == binding.GetResult ||
                request.MethodId == binding.Acknowledge ||
                request.MethodId == binding.Cancel ||
                request.MethodId == binding.Abandon)
            {
                return true;
            }
            NamespaceTable namespaces = m_server.MessageContext.NamespaceUris;
            return request.MethodId == ExpandedNodeId.ToNodeId(
                    RseMethodIds.RecoverableExecutionManagerType_Submit, namespaces) ||
                request.MethodId == ExpandedNodeId.ToNodeId(
                    RseMethodIds.RecoverableExecutionManagerType_GetResult, namespaces) ||
                request.MethodId == ExpandedNodeId.ToNodeId(
                    RseMethodIds.RecoverableExecutionManagerType_Acknowledge, namespaces) ||
                request.MethodId == ExpandedNodeId.ToNodeId(
                    RseMethodIds.RecoverableExecutionManagerType_Cancel, namespaces) ||
                request.MethodId == ExpandedNodeId.ToNodeId(
                    RseMethodIds.RecoverableExecutionManagerType_Abandon, namespaces);
        }

        /// <summary>
        /// Creates a cryptographically random prototype token without exposing it to logging.
        /// </summary>
        private static ByteString NewToken()
        {
            byte[] bytes = new byte[32];
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
        private static void ValidateOwner(
            OperationContext context, Operation operation, RecoverableExecutionOwner? caller)
        {
            ValidateCurrentIdentity(context);
            if (!IsOwner(context, operation, caller) ||
                !HasRoles(operation.Roles, context.UserIdentity.GrantedRoleIds))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
        }

        /// <summary>
        /// Compares exact Session/creator or mapped-owner scope without searching weaker modes.
        /// </summary>
        private static bool IsOwner(
            OperationContext context, Operation operation, RecoverableExecutionOwner? caller)
        {
            return operation.Owner == null
                ? ReferenceEquals(context.Session, operation.Session) &&
                    GetCreator(context.Session) == operation.Creator
                : caller != null && caller.Id == operation.Owner.Id;
        }

        /// <summary>
        /// Resolves current authenticated ownership for a non-Session scope.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<RecoverableExecutionOwner?> ResolveOwnerAsync(
            OperationContext context, ServiceOperationRecoveryMode mode, CancellationToken ct)
        {
            ValidateCurrentIdentity(context);
            if (mode == ServiceOperationRecoveryMode.Session)
            {
                return null;
            }
            return await (m_ownerProvider ??
                throw new ServiceResultException(StatusCodes.BadResourceUnavailable))
                .ResolveAsync(context, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Finds the caller's optional owner scope before token lookup without disclosing a foreign record.
        /// </summary>
        private async ValueTask<RecoverableExecutionOwner?> ResolveControlOwnerAsync(
            OperationContext context, CancellationToken ct)
        {
            ValidateCurrentIdentity(context);
            if (m_ownerProvider == null)
            {
                return null;
            }
            try
            {
                return await m_ownerProvider.ResolveAsync(context, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException exception) when (
                exception.StatusCode == StatusCodes.BadUserAccessDenied ||
                exception.StatusCode == StatusCodes.BadSecurityModeRejected)
            {
                // A valid Session owner need not qualify for the optional stable-owner scope.
                return null;
            }
        }

        /// <summary>
        /// Keeps Owner and Durable Client IDs in one stable duplicate namespace.
        /// </summary>
        private static OperationKey CreateKey(
            ISession session, ByteString creator, RecoverableExecutionOwner? owner, Uuid clientId)
        {
            return owner == null
                ? new OperationKey(session, creator, default, clientId)
                : new OperationKey(null, default, owner.Id, clientId);
        }

        /// <summary>
        /// Rejects known Session-bound history inputs independently of application qualification.
        /// </summary>
        private static bool HasContinuationPoints(ArrayOf<HistoryReadValueId> nodes)
        {
            foreach (HistoryReadValueId node in nodes)
            {
                if (node.ContinuationPoint.Length > 0)
                {
                    return true;
                }
            }
            return false;
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
        private Reservations Reserve(
            OperationContext context, NodeId authenticationToken, int limit, RecoverableExecutionOwner? stableOwner,
            ServiceOperationRecoveryMode mode)
        {
            ResourceIsolationOwner owner = stableOwner == null
                ? m_isolation!.Classify(context.ChannelContext!, authenticationToken)
                : ((IRecoverableExecutionResourceIsolationProvider)m_isolation!)
                    .ClassifyRecoverableOwner(stableOwner.Id);
            return Reserve(owner, context.UserIdentity.GrantedRoleIds, stableOwner, limit, mode,
                input: true, outcome: true, execution: true, journalPayload: true);
        }

        /// <summary>
        /// Reuses the same pools for live admission and restart reconstruction, reserving only retained lifetimes.
        /// </summary>
        private Reservations Reserve(
            ResourceIsolationOwner owner, ArrayOf<NodeId> roles, RecoverableExecutionOwner? stableOwner, int limit,
            ServiceOperationRecoveryMode mode, bool input, bool outcome, bool execution, bool journalPayload)
        {
            var reservations = new Reservations();
            try
            {
                long historyBytes = 256L + (stableOwner?.ContinuityContext.Length ?? 0);
                foreach (NodeId role in roles)
                {
                    historyBytes += 64;
                    if (role.TryGetValue(out string? name))
                    {
                        historyBytes += Encoding.UTF8.GetByteCount(name);
                    }
                    else if (role.TryGetValue(out ByteString identifier))
                    {
                        historyBytes += identifier.Length;
                    }
                    if (historyBytes > limit)
                    {
                        throw new ServiceResultException(StatusCodes.BadResourceUnavailable,
                            "The retained owner identity exceeds the existing message-sized metadata bound.");
                    }
                }
                historyBytes = Math.Max(1024, historyBytes);
                if (historyBytes > limit)
                {
                    throw new ServiceResultException(StatusCodes.BadResourceUnavailable);
                }
                reservations.HistoryCharge = historyBytes;
                reservations.HistoryCount = Acquire(ResourceIsolationStage.ParkedRequest, owner, 1);
                reservations.HistoryBytes = Acquire(ResourceIsolationStage.RequestQueueBytes, owner,
                    mode == ServiceOperationRecoveryMode.Durable ? (8 * historyBytes) + 16_384 : historyBytes);
                if (input)
                {
                    reservations.InputBytes = Acquire(
                        ResourceIsolationStage.RequestQueueBytes, owner, (stableOwner == null ? 2L : 3L) * limit);
                }
                if (outcome)
                {
                    reservations.OutcomeBytes = Acquire(ResourceIsolationStage.RequestQueueBytes, owner, limit);
                }
                if (execution)
                {
                    reservations.Execution = Acquire(ResourceIsolationStage.RequestExecution, owner, 1);
                }
                if (mode == ServiceOperationRecoveryMode.Durable && journalPayload)
                {
                    reservations.JournalBytes = Acquire(
                        ResourceIsolationStage.RequestQueueBytes, owner, (16L * m_maxMessageSize) + 49_152);
                }
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
                if (operation.Mode == ServiceOperationRecoveryMode.Durable &&
                    !await BeginDurableDispatchAsync(operation).ConfigureAwait(false))
                {
                    return;
                }
                try
                {
                    lock (m_gate)
                    {
                        Advance(operation);
                    }
                    operation.Cancellation.Token.ThrowIfCancellationRequested();
                    response = await m_server.ExecuteOwnedServiceAsync(operation.Context!, operation.Request!)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    response = CreateOwnedFault(operation,
                        new ServiceResultException(StatusCodes.BadRequestCancelledByClient));
                }
                catch (Exception exception)
                {
                    response = CreateOwnedFault(operation, exception);
                }
                status = response.ResponseHeader.ServiceResult;
                try
                {
                    operation.Input!.Permissions = operation.PermissionCapture.Snapshot();
                    frozen = operation.Types == null
                        ? EncodeBinary(response, operation.Encoding)
                        : operation.Types.Encode(response).Span.ToArray();
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
                if (operation.Mode == ServiceOperationRecoveryMode.Durable)
                {
                    await CommitDurableOutcomeAsync(operation, kind, status, frozen).ConfigureAwait(false);
                }
                else
                {
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
            }
            catch (Exception exception) when (operation.Mode == ServiceOperationRecoveryMode.Durable)
            {
                WithdrawDurableReadiness(operation, exception);
            }
            finally
            {
                (response as IPooledEncodeable)?.Reuse();
                await FinishInvocationAsync(operation).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Releases invocation-only state after actual local work ends, not after a wrapper wait or public release.
        /// </summary>
        private async Task FinishInvocationAsync(Operation operation)
        {
            operation.Context?.Dispose();
            Task cancellation;
            lock (m_gate)
            {
                operation.Context = null;
                operation.LocalWorkEnded = true;
                if (operation.Mode != ServiceOperationRecoveryMode.Durable)
                {
                    operation.WorkFinished = true;
                }
                RecordAudit(operation, ServiceOperationAuditAction.InvocationEnded);
                cancellation = operation.CancellationTask;
                Advance(operation);
            }
            try
            {
                await cancellation.ConfigureAwait(false);
                bool eligibilityReleased = true;
                if (operation.Eligibility != null)
                {
                    try
                    {
                        await operation.Eligibility.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        eligibilityReleased = false;
                        m_logger.RecoverableEligibilityReleaseFailed(exception, operation.ClientId);
                        if (operation.Mode == ServiceOperationRecoveryMode.Durable)
                        {
                            WithdrawDurableReadiness(operation, exception);
                        }
                    }
                }
                if (operation.Mode == ServiceOperationRecoveryMode.Durable &&
                    eligibilityReleased &&
                    operation.JournalEntry != null &&
                    m_journal?.IsReady == true)
                {
                    try
                    {
                        await operation.JournalEntry.ConfirmTerminationAsync().ConfigureAwait(false);
                        await RefreshDurableAsync(operation, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        WithdrawDurableReadiness(operation, exception);
                    }
                }
            }
            finally
            {
                operation.Lifetime.Dispose();
                operation.Cancellation.Dispose();
                operation.LifetimesDisposed = true;
                Cleanup(operation);
            }
        }

        /// <summary>
        /// Uses ordinary fault formatting with effective diagnostics without mutating the enclosed original header.
        /// </summary>
        private ServiceFault CreateOwnedFault(Operation operation, Exception exception)
        {
            IServiceRequest formattingRequest = operation.Input!.DecodeRequest();
            try
            {
                formattingRequest.RequestHeader.ReturnDiagnostics = (uint)operation.Input.DiagnosticsMask;
                return EndpointBase.CreateFault(m_logger, formattingRequest, exception);
            }
            finally
            {
                (formattingRequest as IPooledEncodeable)?.Reuse();
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
            info.ResultRemaining =
                RecoverableExecutionDurations.RemainingMilliseconds(operation.Retention, TimeSpan.Zero);
            var result = new ExtensionObject(original);
            CheckEncodedSize(WrapResult(new SubmitResponseDataType
            {
                Operation = info,
                Result = result
            }, header), operation.Mapping, operation.Encoding);
            CheckEncodedSize(WrapResult(new GetResultResponseDataType
            {
                Operation = info,
                Result = result
            }, header), operation.Mapping, operation.Encoding);
        }

        /// <summary>
        /// Includes ordinary Call framing when measuring a scalar manager response.
        /// </summary>
        private static CallResponse WrapResult(IEncodeable response, ResponseHeader? header = null)
        {
            return new CallResponse
            {
                ResponseHeader = header ?? new ResponseHeader { RequestHandle = uint.MaxValue },
                Results = [new CallMethodResult { OutputArguments = [Variant.FromStructure(response)] }]
            };
        }

        /// <summary>
        /// Measures actual encoded envelopes, including header, metadata and the original response.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void CheckEncodedSize(
            IServiceResponse response, RequestEncoding mapping, ServiceMessageContext context)
        {
            using var stream = new RecoverableExecutionBufferStream(context.MaxMessageSize);
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
            using var stream = new RecoverableExecutionBufferStream(context.MaxMessageSize);
            using var encoder = new BinaryEncoder(stream, context, leaveOpen: true);
            encoder.EncodeMessage(message, message.BinaryEncodingId);
            _ = encoder.Close();
            return stream.ToArray();
        }

        /// <summary>
        /// Separates a cancellable wrapper wait from the independently owned invocation lifetime.
        /// </summary>
        private async Task WaitAsync(
            Operation operation, TimeSpan wait, long started, RequestLifetime lifetime, bool submitted,
            CancellationToken ct)
        {
            if (operation.Mode == ServiceOperationRecoveryMode.Durable)
            {
                await WaitDurableAsync(operation, wait, lifetime, submitted, ct).ConfigureAwait(false);
                return;
            }
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
            wait -= m_clock.GetElapsedTime(started);
            if (wait <= TimeSpan.Zero)
            {
                return;
            }
            lifetime.ParkSink?.NotifyParked();
            using CancellationTokenSource expiry = m_clock.CreateCancellationTokenSource(wait);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, expiry.Token, m_shutdown.Token);
            try
            {
                await operation.Changed.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                expiry.IsCancellationRequested && !ct.IsCancellationRequested && !m_shutdown.IsCancellationRequested)
            {
                // The requested wait budget ended; this exchange reports the current state.
            }
        }

        /// <summary>
        /// Refreshes current Session authorization after waiting and before disclosing retained state.
        /// </summary>
        private async ValueTask<OperationContext> RevalidateAsync(
            OperationContext context, Operation operation, AsyncServiceAccess access, bool submitted = false)
        {
            var header = new RequestHeader
            {
                AuthenticationToken = context.AuthenticationToken,
                RequestHandle = context.ClientHandle,
                ReturnDiagnostics = (uint)context.DiagnosticsMask,
                AuditEntryId = context.AuditEntryId
            };
            OperationContext current = await m_server.RevalidateAsyncServiceContextAsync(context, header)
                .ConfigureAwait(false);
            try
            {
                RecoverableExecutionOwner? caller = await ResolveOwnerAsync(
                    current, operation.Mode, context.CancellationToken).ConfigureAwait(false);
                ValidateOwner(current, operation, caller);
                context.CancellationToken.ThrowIfCancellationRequested();
                await AuthorizeAsync(
                    current, operation.ClientId, operation.ServiceId, access, context.CancellationToken)
                    .ConfigureAwait(false);
                ManagerBinding binding = Volatile.Read(ref m_binding) ??
                    throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
                var method = new CallMethodRequest
                {
                    ObjectId = binding.Manager,
                    MethodId = access switch
                    {
                        AsyncServiceAccess.Collect => submitted ? binding.Submit : binding.GetResult,
                        AsyncServiceAccess.Cancel => binding.Cancel,
                        AsyncServiceAccess.Acknowledge => binding.Acknowledge,
                        AsyncServiceAccess.Abandon => binding.Abandon,
                        _ => throw new ServiceResultException(StatusCodes.BadInvalidArgument)
                    }
                };
                if (m_server.CurrentInstance.NodeManager is not MasterNodeManager manager)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported);
                }
                await manager.ValidateRecoverableManagerMethodAsync(current, method, context.CancellationToken)
                    .ConfigureAwait(false);
                caller = await ResolveOwnerAsync(current, operation.Mode, context.CancellationToken)
                    .ConfigureAwait(false);
                ValidateOwner(current, operation, caller);
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
            OperationContext context, Operation operation)
        {
            await RefreshDurableAsync(operation, context.CancellationToken).ConfigureAwait(false);
            (ServiceOperationInfoDataType info, OutcomeSnapshot? snapshot, OutcomeSnapshot? input) =
                Select(operation, operation.ClientId);
            RetainedSubmitResponse? response = null;
            try
            {
                // Keep the cleanup owner visible if decoding fails.
                // TODO: remove this suppression when IDE0017 preserves cleanup-visible assignment ordering.
#pragma warning disable IDE0017
                response = new RetainedSubmitResponse(snapshot) { Operation = info };
                response.Result = (snapshot?.Decode()) ?? default;
#pragma warning restore IDE0017
                await AuthorizeResultAsync(context, input, response.Result).ConfigureAwait(false);
                ValidateOwner(context, operation,
                    await ResolveOwnerAsync(context, operation.Mode, context.CancellationToken).ConfigureAwait(false));
                CheckDeliverySize(WrapResult(response), context);
                RecordCollection(info, context.SessionId);
                RetainedSubmitResponse result = response;
                response = null;
                return result;
            }
            finally
            {
                response?.Dispose();
                input?.Release();
                Cleanup(operation);
            }
        }

        /// <summary>
        /// Returns a fresh collection envelope, never replacing the stored outcome on a delivery failure.
        /// </summary>
        private async ValueTask<RetainedGetResponse> CreateCollectionResponseAsync(
            OperationContext context, Operation operation, Uuid unknownClientId)
        {
            await RefreshDurableAsync(operation, context.CancellationToken).ConfigureAwait(false);
            (ServiceOperationInfoDataType info, OutcomeSnapshot? snapshot, OutcomeSnapshot? input) =
                Select(operation, unknownClientId);
            RetainedGetResponse? response = null;
            try
            {
                // Keep the cleanup owner visible if decoding fails.
                // TODO: remove this suppression when IDE0017 preserves cleanup-visible assignment ordering.
#pragma warning disable IDE0017
                response = new RetainedGetResponse(snapshot) { Operation = info };
                response.Result = (snapshot?.Decode()) ?? default;
#pragma warning restore IDE0017
                await AuthorizeResultAsync(context, input, response.Result).ConfigureAwait(false);
                ValidateOwner(context, operation,
                    await ResolveOwnerAsync(context, operation.Mode, context.CancellationToken).ConfigureAwait(false));
                CheckDeliverySize(WrapResult(response), context);
                RecordCollection(info, context.SessionId);
                RetainedGetResponse result = response;
                response = null;
                return result;
            }
            finally
            {
                response?.Dispose();
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
                uint requestedDiagnostics = (uint)input.DiagnosticsMask;
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
                await manager.ValidateRetainedPermissionsAsync(context, input.Permissions,
                    context.CancellationToken).ConfigureAwait(false);
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
        private ServiceOperationControlResult ApplyControl(Operation operation, AsyncServiceAccess action)
        {
            switch (action)
            {
                case AsyncServiceAccess.Cancel:
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
                case AsyncServiceAccess.Acknowledge:
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
                case AsyncServiceAccess.Abandon:
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
            m_retainedBytes += (ulong)operation.Reservations.HistoryCharge;
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
                    m_retainedBytes -= (ulong)operation.Reservations.HistoryCharge;
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
            if (operation.Mode == ServiceOperationRecoveryMode.Durable)
            {
                return;
            }
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
                // TotalSeconds can round down on .NET Framework and move the release boundary one tick early.
                long retentionTicks = (long)((decimal)operation.Retention.Ticks *
                    m_clock.TimestampFrequency /
                    TimeSpan.TicksPerSecond);
                long expiredAt = unchecked(operation.OutcomeAt + retentionTicks);
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
            if (operation.DurableRecord != null)
            {
                return operation.DurableRecord.Snapshot(operation.DurableNow);
            }
            long now = m_clock.GetTimestamp();
            return new ServiceOperationInfoDataType
            {
                ClientOperationId = operation.ClientId,
                ServerToken = operation.Token,
                ServiceId = operation.ServiceId,
                RecoveryMode = operation.Mode,
                State = operation.State,
                OutcomeKind = operation.Kind,
                OutcomeStatus = operation.Status,
                ExecutionAllowance = RecoverableExecutionDurations.ToMilliseconds(operation.Execution),
                ResultRetentionTime = RecoverableExecutionDurations.ToMilliseconds(operation.Retention),
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
            return RecoverableExecutionDurations.RemainingMilliseconds(duration, m_clock.GetElapsedTime(start, now));
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
            if (operation.WorkFinished || operation.LocalWorkEnded || operation.CancellationSignaled)
            {
                return;
            }
            if (!operation.CancellationRequested)
            {
                operation.CancellationRequested = true;
                RecordAudit(operation, ServiceOperationAuditAction.CancellationRequested);
            }
            operation.CancellationSignaled = true;
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
            if (m_stopping || operation.References == 0)
            {
                return;
            }
            if (operation.Mode == ServiceOperationRecoveryMode.Durable)
            {
                ArmDurableTimer(operation);
                return;
            }
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
                if (m_stopping || operation.References == 0)
                {
                    return;
                }
                if (operation.Mode == ServiceOperationRecoveryMode.Durable)
                {
                    StartDurableMaintenance(operation);
                    return;
                }
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
            IDisposable? journalBytes = null;
            OutcomeSnapshot? outcome = null;
            OutcomeSnapshot? input = null;
            IServiceRequest? request = null;
            ITimer? timer = null;
            bool retired = false;
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
                    if (operation.References == (operation.Indexed ? 1 : 0))
                    {
                        journalBytes = operation.Reservations.JournalBytes;
                        operation.Reservations.JournalBytes = null;
                    }
                    outcome = operation.Outcome;
                    operation.Outcome = null;
                    if (operation.WorkFinished)
                    {
                        input = operation.Input;
                        operation.Input = null;
                    }
                }
                if (operation.Forgotten && operation.WorkFinished && operation.Indexed)
                {
                    operation.Indexed = false;
                    retired = true;
                    if (m_byClient.TryGetValue(operation.Key, out Operation? current) &&
                        ReferenceEquals(current, operation))
                    {
                        m_byClient.Remove(operation.Key);
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
            journalBytes?.Dispose();
            timer?.Dispose();
            if (retired)
            {
                ReleaseOperation(operation);
            }
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
                operation.Kind, operation.Status, succeeded: true, operation.IssuingSessionId, operation.Mode);
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
                        info.State, info.OutcomeKind, info.OutcomeStatus, succeeded: true,
                        sessionId, info.RecoveryMode);
                }
            }
        }

        /// <summary>
        /// Captures an audit action without a raw token, credentials or a duplicate request/result payload.
        /// </summary>
        private void RecordAudit(
            Uuid clientId, NodeId serviceId, ServiceOperationAuditAction action, ServiceOperationState state,
            ServiceOperationOutcome kind, StatusCode status, bool succeeded, NodeId sessionId,
            ServiceOperationRecoveryMode mode = ServiceOperationRecoveryMode.Unspecified)
        {
            if (m_auditServer.Auditing)
            {
                m_audits.Enqueue(new OperationAudit(
                    clientId, serviceId, action, state, kind, status, mode, succeeded, sessionId,
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
                    m_byClient.TryGetValue(
                        CreateKey(context.Session, GetCreator(context.Session), null, clientId), out operation);
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
                            context, RseName(RseBrowseNames.ClientOperationId), Variant.From(audit.ClientId), false) ||
                        !entry.SetChildValue(context, RseName(RseBrowseNames.ServiceId), Variant.From(audit.ServiceId),
                            false) ||
                        !entry.SetChildValue(context, RseName(RseBrowseNames.Action), audit.Action) ||
                        !entry.SetChildValue(context, RseName(RseBrowseNames.OperationState), audit.State) ||
                        !entry.SetChildValue(context, RseName(RseBrowseNames.OutcomeKind), audit.Kind) ||
                        !entry.SetChildValue(context, RseName(RseBrowseNames.RecoveryMode), audit.Mode) ||
                        !entry.SetChildValue(context, RseName(RseBrowseNames.OutcomeStatus), Variant.From(audit.Status),
                            false))
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
            ServiceOperationOutcome Kind, StatusCode Status, ServiceOperationRecoveryMode Mode, bool Succeeded,
            NodeId SessionId, DateTime Timestamp);

        /// <summary>
        /// Resolves an RSE BrowseName in the Server's current namespace table.
        /// </summary>
        private QualifiedName RseName(string name)
        {
            return new QualifiedName(name, m_server.MessageContext.NamespaceUris.GetIndexOrAppend(RseNamespaces.Rse));
        }

        /// <summary>
        /// Captures live identities independently of the model's Method declaration identifiers.
        /// </summary>
        private sealed record ManagerBinding(
            NodeId Manager, NodeId Submit, NodeId GetResult, NodeId Acknowledge, NodeId Cancel, NodeId Abandon);

        /// <summary>
        /// Keeps Client IDs in one actual Session incarnation even if a Server reuses a public Session identifier.
        /// </summary>
        private sealed class OperationKeyComparer : IEqualityComparer<OperationKey>
        {
            /// <inheritdoc/>
            public bool Equals(
                OperationKey x, OperationKey y)
            {
                return ReferenceEquals(x.Session, y.Session) &&
                    x.Creator == y.Creator &&
                    x.Owner == y.Owner &&
                    x.ClientId == y.ClientId;
            }

            /// <inheritdoc/>
            public int GetHashCode(OperationKey value)
            {
                return HashCode.Combine(value.Session == null ? 0 : RuntimeHelpers.GetHashCode(value.Session),
                    value.Creator, value.Owner, value.ClientId);
            }
        }

        /// <summary>
        /// An opaque in-memory duplicate scope; it is neither logged nor treated as a bearer credential.
        /// </summary>
        private readonly record struct OperationKey(
            ISession? Session, ByteString Creator, RecoverableExecutionOwnerId Owner, Uuid ClientId);

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
            public IDisposable? JournalBytes { get; set; }
            public long HistoryCharge { get; set; }

            public void Dispose()
            {
                Execution?.Dispose();
                InputBytes?.Dispose();
                OutcomeBytes?.Dispose();
                HistoryBytes?.Dispose();
                HistoryCount?.Dispose();
                JournalBytes?.Dispose();
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
                Reservations reservations, OperationKey key, RecoverableExecutionOwner? stableOwner,
                ServiceOperationRecoveryMode mode, RecoverableExecutionIdentity? independentIdentity,
                IAsyncDisposable? eligibility, RecoverableExecutionTypeContext? types, Uuid executionId)
            {
                Session = stableOwner == null ? admitted.Session : null;
                IssuingSessionId = admitted.SessionId;
                Key = key;
                Owner = stableOwner;
                Mode = mode;
                Eligibility = eligibility;
                Types = types;
                ExecutionId = executionId;
                ClientId = clientId;
                Token = token;
                Creator = creator;
                Execution = execution;
                Retention = retention;
                Encoding = encoding;
                Limit = limit;
                Reservations = reservations;
                Roles = RecoverableExecutionIdentity.CopyRoles(admitted.UserIdentity.GrantedRoleIds);
                Request = request;
                ServiceId = ExpandedNodeId.ToNodeId(request.TypeId, encoding.NamespaceUris);
                Mapping = admitted.ChannelContext!.MessageEncoding;
                Cancellation = new CancellationTokenSource();
                Lifetime = new RequestLifetime(Cancellation.Token);
                PermissionCapture = new RecoverablePermissionCapture(limit);
                Context = new OperationContext(
                    admitted, request.RequestHeader, requestType, Lifetime, PermissionCapture, independentIdentity);
                Timer = owner.m_clock.CreateTimer(
                    _ => owner.OnTimer(this), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                Input = new OutcomeSnapshot(input, encoding, reservations.InputBytes!, types: types)
                {
                    DiagnosticsMask = Context.DiagnosticsMask
                };
                reservations.InputBytes = null;
            }

            /// <summary>
            /// Restores only recovery metadata and snapshots; no saved original request becomes executable.
            /// </summary>
            public Operation(
                AsyncServiceExecutionProvider owner, RecoverableExecutionJournal.Entry entry,
                RecoverableExecutionDurableRecord record, RecoverableExecutionTypeContext types,
                Reservations reservations)
            {
                IssuingSessionId = record.IssuingSessionId;
                Key = new OperationKey(null, default, record.Owner.Id, record.ClientId);
                Owner = record.Owner;
                Mode = ServiceOperationRecoveryMode.Durable;
                Types = types;
                ExecutionId = record.Incarnation;
                ClientId = record.ClientId;
                Token = record.Token;
                Roles = record.Roles;
                ServiceId = record.ServiceId;
                Execution = record.Execution;
                Retention = record.Retention;
                Encoding = TransportChannelBinding.CopyContext(types.MessageContext);
                Limit = record.MessageLimit;
                Mapping = record.Mapping;
                Reservations = reservations;
                Cancellation = new CancellationTokenSource();
                Lifetime = new RequestLifetime(Cancellation.Token);
                PermissionCapture = new RecoverablePermissionCapture(record.MessageLimit);
                JournalEntry = entry;
                LocalWorkEnded = true;
                State = entry.InitialState;
                Kind = entry.InitialOutcome;
                CancellationRequested = entry.InitialCancellationRequested;
                Timer = owner.m_clock.CreateTimer(
                    _ => owner.OnTimer(this), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                if (!record.Request.IsNull)
                {
                    Input = new OutcomeSnapshot(
                        record.Request.Span.ToArray(), Encoding, reservations.InputBytes!, types: types)
                    {
                        DiagnosticsMask = record.DiagnosticsMask,
                        Permissions = record.Permissions
                    };
                    reservations.InputBytes = null;
                }
            }

            public ISession? Session { get; }
            public NodeId IssuingSessionId { get; }
            public OperationKey Key { get; }
            public RecoverableExecutionOwner? Owner { get; }
            public ServiceOperationRecoveryMode Mode { get; }
            public IAsyncDisposable? Eligibility { get; }
            public RecoverableExecutionTypeContext? Types { get; }
            public Uuid ExecutionId { get; }
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
            public RecoverablePermissionCapture PermissionCapture { get; }
            public OperationContext? Context { get; set; }
            public IServiceRequest? Request { get; set; }
            public OutcomeSnapshot? Outcome { get; set; }
            public OutcomeSnapshot? Input { get; set; }
            public ITimer? Timer { get; set; }
            public Task Work { get; set; } = Task.CompletedTask;
            public Task CancellationTask { get; set; } = Task.CompletedTask;
            public Task Maintenance { get; set; } = Task.CompletedTask;
            public bool MaintenancePending { get; set; }
            public RecoverableExecutionJournal.Entry? JournalEntry { get; set; }
            public bool AdmissionWriteStarted { get; set; }
            public RecoverableExecutionDurableRecord? DurableRecord { get; set; }
            public RecoverableExecutionTime DurableNow { get; set; }
            public RecoverableExecutionRecoveredWork? RecoveredWork { get; set; }
            public bool Indexed { get; set; }
            public int References { get; set; }

            public TaskCompletionSource<bool> Changed { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public long AdmittedAt { get; set; }
            public long OutcomeAt { get; set; }
            public long ReleasedAt { get; set; }
            public ServiceOperationState State { get; set; } = ServiceOperationState.Pending;
            public ServiceOperationOutcome Kind { get; set; }
            public StatusCode Status { get; set; }
            public bool CancellationRequested { get; set; }
            public bool CancellationSignaled { get; set; }
            public bool WorkFinished { get; set; }
            public bool LocalWorkEnded { get; set; }
            public bool Forgotten { get; set; }
            public bool LifetimesDisposed { get; set; }

            public async ValueTask DisposeUnadmittedAsync()
            {
                Timer?.Dispose();
                Context?.Dispose();
                Lifetime.Dispose();
                Cancellation.Dispose();
                LifetimesDisposed = true;
                (Request as IPooledEncodeable)?.Reuse();
                Input?.Release();
                if (Eligibility != null)
                {
                    await Eligibility.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Shares an immutable encoded original response while retained and in-flight owners remain charged.
        /// </summary>
        private sealed class OutcomeSnapshot(
            byte[] bytes, IServiceMessageContext context, IDisposable reservation, Action? onReleased = null,
            RecoverableExecutionTypeContext? types = null)
        {
            /// <summary>
            /// Supplies the independently owned immutable encoding to the protected journal.
            /// </summary>
            public ByteString Encoded => new(bytes);

            /// <summary>
            /// Holds the immutable checks with the same retained and in-flight ownership as the input snapshot.
            /// </summary>
            public ArrayOf<RecoverableNodePermission> Permissions { get; set; }

            /// <summary>
            /// Captures the effective admission-time mask separately from the unmodified original request header.
            /// </summary>
            public DiagnosticsMasks DiagnosticsMask { get; set; }

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
                return new ExtensionObject(types == null
                    ? BinaryDecoder.DecodeMessage<IServiceResponse>(bytes, context)
                    : types.Decode<IServiceResponse>(new ByteString(bytes)));
            }

            public IServiceRequest DecodeRequest()
            {
                return types == null
                    ? BinaryDecoder.DecodeMessage<IServiceRequest>(bytes, context)
                    : types.Decode<IServiceRequest>(new ByteString(bytes));
            }

            private int m_references = 1;
        }

        /// <summary>
        /// Holds an outgoing submission's snapshot and transferred request charge through wire serialization.
        /// </summary>
        private sealed class RetainedSubmitResponse(OutcomeSnapshot? snapshot) :
            SubmitResponseDataType, IDisposable
        {
            public void Dispose()
            {
                Reuse();
            }

            protected override void ReuseCore()
            {
                if (Interlocked.Exchange(ref m_released, 1) != 0)
                {
                    return;
                }
                try
                {
                    if (Result.TryGetValue(out IEncodeable? original))
                    {
                        (original as IPooledEncodeable)?.Reuse();
                    }
                    ResetForReuse();
                }
                finally
                {
                    snapshot?.Release();
                }
            }

            private int m_released;
        }

        /// <summary>
        /// Holds an outgoing collection's snapshot and transferred request charge through wire serialization.
        /// </summary>
        private sealed class RetainedGetResponse(OutcomeSnapshot? snapshot) :
            GetResultResponseDataType, IDisposable
        {
            public void Dispose()
            {
                Reuse();
            }

            protected override void ReuseCore()
            {
                if (Interlocked.Exchange(ref m_released, 1) != 0)
                {
                    return;
                }
                try
                {
                    if (Result.TryGetValue(out IEncodeable? original))
                    {
                        (original as IPooledEncodeable)?.Reuse();
                    }
                    ResetForReuse();
                }
                finally
                {
                    snapshot?.Release();
                }
            }

            private int m_released;
        }

        private readonly StandardServer m_server;
        private readonly AsyncServiceExecutionOptions m_options;
        private readonly TimeProvider m_clock;
        private readonly IAsyncServiceAuthorizationPolicy? m_authorizationPolicy;
        private readonly IRecoverableExecutionOwnerProvider? m_ownerProvider;
        private readonly IRecoverableExecutionEligibilityProvider? m_eligibilityProvider;
        private readonly ISharedKeyValueStore? m_durableStore;
        private readonly IRecordProtector? m_recordProtector;
        private readonly IRecoverableExecutionClock? m_durableClock;
        private readonly RecoverableExecutionTypeCompatibility m_typeCompatibility;
        private readonly string? m_logicalServerId;
        private readonly int m_maxMessageSize;
        private readonly IServerResourceIsolationProvider? m_isolation;
        private readonly ILogger m_logger;
        private readonly IAuditEventServer m_auditServer;
        private readonly Lock m_gate = new();

        private readonly Dictionary<OperationKey, Operation> m_byClient = new(new OperationKeyComparer());

        private readonly Dictionary<ByteString, Operation> m_byToken = [];
        private readonly Queue<OperationAudit> m_audits = [];
        private ManagerBinding? m_binding;
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

        [LoggerMessage(EventId = ServerEventIds.AsyncServiceExecution + 3, Level = LogLevel.Error,
            Message = "Recoverable operation {ClientOperationId} failed to release its eligibility reservation.")]
        public static partial void RecoverableEligibilityReleaseFailed(
            this ILogger logger, Exception exception, Uuid clientOperationId);

        [LoggerMessage(EventId = ServerEventIds.AsyncServiceExecution + 4, Level = LogLevel.Error,
            Message = "Recoverable Durable qualification is unavailable for operation {ClientOperationId}: {Status}.")]
        public static partial void RecoverableDurabilityUnavailable(
            this ILogger logger, Uuid clientOperationId, StatusCode status);
    }
}
