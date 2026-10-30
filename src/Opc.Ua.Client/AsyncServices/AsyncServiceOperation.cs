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
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Owns one Client operation ID, one send-once submission and a typed caller completion context.
    /// </summary>
    /// <typeparam name="TResult">The generated response or generated method projection.</typeparam>
    /// <remarks>
    /// Submit and Collect drive bounded exchanges; Completion does not start a background polling loop.
    /// A lost submission reply is recovered by Collect using the already available ClientOperationId.
    /// Disposing this local handle neither cancels nor abandons Server work.
    /// </remarks>
    public sealed class AsyncServiceOperation<TResult> : IAsyncDisposable
    {
        /// <summary>
        /// Creates local identity and generated-client plumbing without sending the original request.
        /// </summary>
        internal AsyncServiceOperation(
            RecoverableExecutionBinding binding,
            NodeId requestType,
            Func<ISessionClient, CancellationToken, ValueTask<TResult>> invoke,
            bool ownsBinding = true,
            Uuid clientOperationId = default)
        {
            m_binding = binding;
            m_ownsBinding = ownsBinding;
            m_requestType = requestType;
            m_typeWitness = binding.TypeContext?.Witness ?? default;
            m_requiresMethodProjection =
                requestType == DataTypeIds.CallRequest && typeof(TResult) != typeof(CallResponse);
            m_invoke = invoke;
            ClientOperationId = clientOperationId == Uuid.Empty ? Uuid.NewUuid() : clientOperationId;
            SessionConfiguration configuration = binding.Session.SaveSessionConfiguration();
            m_authenticationToken = configuration.AuthenticationToken;
            m_logger = binding.Session.MessageContext.Telemetry.CreateLogger<AsyncServiceOperation<TResult>>();
            m_captureTransport = new CaptureTransport(this, binding.Session.TransportChannel);
            m_client = new CaptureClient(this, m_captureTransport, binding.Session.MessageContext.Telemetry)
            {
                ReturnDiagnostics = binding.Session.ReturnDiagnostics,
                DefaultTimeoutHint = binding.Session.DefaultTimeoutHint
            };
            m_client.SessionCreated(configuration.SessionId, configuration.AuthenticationToken);
        }

        /// <summary>
        /// Gets the nonzero Client ID, including before submission and after local disposal.
        /// </summary>
        public Uuid ClientOperationId { get; }

        /// <summary>
        /// Gets the original issuing Server Session ID, even when explicit Owner recovery uses another Session.
        /// </summary>
        public NodeId SessionId => m_binding.SessionId;

        /// <summary>
        /// Gets the exact selected recovery contract; collection and replacement Sessions never downgrade it.
        /// </summary>
        public ServiceOperationRecoveryMode RecoveryMode => m_binding.Options.RecoveryMode;

        /// <summary>
        /// Gets the Server token once recovered or admitted. It is not an authorization credential.
        /// </summary>
        public ByteString ServerToken
        {
            get
            {
                lock (m_state)
                {
                    return m_serverToken;
                }
            }
        }

        /// <summary>
        /// Gets the typed caller completion context. Only a validated and accepted available outcome completes it.
        /// Unknown lookups do not complete it or establish that work did not execute.
        /// </summary>
        public Task<AsyncServiceResult<TResult>> Completion => m_completion.Task;

        /// <summary>
        /// Gets the shared binding invalidation signal.
        /// </summary>
        public Task Invalidated => m_binding.Invalidated;

        /// <summary>
        /// Gets an independent snapshot of the latest disclosed Server metadata, or null before any reply.
        /// </summary>
        public ServiceOperationInfoDataType? LastOperation
        {
            get
            {
                lock (m_state)
                {
                    return CoreUtils.Clone(m_lastOperation);
                }
            }
        }

        /// <summary>
        /// Gets the last explicit ACK response, independently of the accepted original result.
        /// </summary>
        public ControlResponseDataType? LastAcknowledgement
        {
            get
            {
                lock (m_state)
                {
                    return CoreUtils.Clone(m_lastAcknowledgement);
                }
            }
        }

        /// <summary>
        /// Gets an ACK exchange failure without turning an accepted original outcome into a failed operation.
        /// </summary>
        public ServiceResult? AcknowledgementFailure
        {
            get
            {
                lock (m_state)
                {
                    return m_acknowledgementFailure;
                }
            }
        }

        /// <summary>
        /// Gets or sets whether the enclosing typed caller operation owns the final local acceptance commit.
        /// </summary>
        internal bool DeferAcceptanceCheckpoint { get; set; }

        /// <summary>
        /// Selects explicit application acknowledgement. Configure before calling SubmitAsync.
        /// Collection still validates and accepts the outcome into Completion, but sends no automatic ACK.
        /// </summary>
        public AsyncServiceOperation<TResult> RequireExplicitAcknowledgement()
        {
            lock (m_state)
            {
                EnsureNotSubmitted();
                m_automaticAcknowledgement = false;
            }
            return this;
        }

        /// <summary>
        /// Adds an application or durable-storage acceptance step before completion and the same wire ACK.
        /// A thrown exception or cancellation leaves the outcome unacknowledged; collection may retry this step.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="accept"/> is <c>null</c>.</exception>
        public AsyncServiceOperation<TResult> WithAcceptancePolicy(
            Func<AsyncServiceResult<TResult>, CancellationToken, ValueTask> accept)
        {
            if (accept == null)
            {
                throw new ArgumentNullException(nameof(accept));
            }
            lock (m_state)
            {
                EnsureNotSubmitted();
                m_accept = accept;
            }
            return this;
        }

        /// <summary>
        /// Sends the original request once with exact requested lifetimes and a bounded or zero initial wait.
        /// Any uncertain exchange failure leaves this handle available for Collect, never another Submit.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<ServiceOperationInfoDataType> SubmitAsync(
            double executionAllowance, double resultRetentionTime, double waitTime = 0, CancellationToken ct = default)
        {
            ValidateDuration(executionAllowance, positive: true);
            ValidateDuration(resultRetentionTime, positive: true);
            ValidateDuration(waitTime, positive: false);
            ThrowIfDisposed();
            lock (m_state)
            {
                EnsureNotSubmitted();
                m_submissionAttempted = true;
                m_executionAllowance = executionAllowance;
                m_resultRetentionTime = resultRetentionTime;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_lifetime.Token);
            await m_exchanges.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                RecoverableCapabilities capabilities = await m_binding.DiscoverAsync(linked.Token)
                    .ConfigureAwait(false);
                if (!capabilities.Available ||
                    !capabilities.SupportedServices.Contains(m_requestType) ||
                    !capabilities.SupportedRecoveryModes.Contains((int)RecoveryMode))
                {
                    throw new ServiceResultException(StatusCodes.BadServiceUnsupported);
                }
                m_invocation = InvokeAsync();
                _ = ObserveInvocationAsync(m_invocation);
                if (m_invocation.IsCompleted)
                {
                    _ = await m_invocation.ConfigureAwait(false);
                }
                IServiceRequest request;
                try
                {
                    request = await m_prepared.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (!m_prepared.Task.IsCompleted)
                    {
                        await m_lifetime.CancelAsync().ConfigureAwait(false);
                    }
                    throw;
                }
                await SaveCheckpointAsync(
                    RecoverableCheckpointAcceptance.SubmissionAttempted, linked.Token).ConfigureAwait(false);
                RecoverableManagerClient manager = await m_binding.GetManagerAsync(
                    linked.Token, allowReplacement: false).ConfigureAwait(false);
                var submission = new SubmitRequestDataType
                {
                    ClientOperationId = ClientOperationId,
                    RecoveryMode = RecoveryMode,
                    Request = new ExtensionObject(request),
                    ExecutionAllowance = executionAllowance,
                    ResultRetentionTime = resultRetentionTime,
                    WaitTime = waitTime
                };
                SubmitResponseDataType response;
                m_exchangeFailure = null;
                try
                {
                    response = await manager.SubmitAsync(submission, linked.Token).ConfigureAwait(false);
                }
                catch (ServiceResultException failure)
                {
                    m_exchangeFailure = failure;
                    throw;
                }
                if (response.Operation.State == ServiceOperationState.Unknown)
                {
                    throw InvalidResponse();
                }
                ValidateMetadata(response.Operation, response.Result, queriedByClientId: true);
                await SaveCheckpointAsync(m_checkpointAcceptance, linked.Token).ConfigureAwait(false);
                await AcceptAsync(response.Operation, response.Result, linked.Token).ConfigureAwait(false);
                return CoreUtils.Clone(response.Operation)!;
            }
            finally
            {
                m_exchanges.Release();
            }
        }

        /// <summary>
        /// Performs one fresh bounded collection exchange. A missing initial token selects the Client ID form.
        /// The returned metadata is a lookup result, not a statement that the original Service succeeded.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask<ServiceOperationInfoDataType> CollectAsync(
            double waitTime = 0, CancellationToken ct = default)
        {
            ValidateDuration(waitTime, positive: false);
            ThrowIfDisposed();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_lifetime.Token);
            await m_exchanges.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (m_originalRequest == null)
                {
                    throw new InvalidOperationException("No original request has been prepared for this handle.");
                }
                ByteString token = ServerToken;
                bool byClientId = token.IsNull;
                if (m_savedOutcome is { } saved)
                {
                    ExtensionObject payload = saved.OriginalResponse == null
                        ? default : new ExtensionObject(CoreUtils.Clone(saved.OriginalResponse)!);
                    ValidateMetadata(saved.Operation!, payload, queriedByClientId: true);
                    await AcceptAsync(saved.Operation!, payload, linked.Token).ConfigureAwait(false);
                    m_savedOutcome = null;
                    return CoreUtils.Clone(saved.Operation)!;
                }
                RecoverableManagerClient manager = await m_binding.GetManagerAsync(linked.Token).ConfigureAwait(false);
                var collection = new GetResultRequestDataType
                {
                    RecoveryMode = RecoveryMode,
                    ClientOperationId = byClientId ? ClientOperationId : Uuid.Empty,
                    ServerToken = byClientId ? default : token,
                    WaitTime = waitTime
                };
                GetResultResponseDataType response;
                m_exchangeFailure = null;
                try
                {
                    response = await manager.GetResultAsync(collection, linked.Token).ConfigureAwait(false);
                }
                catch (ServiceResultException failure)
                {
                    m_exchangeFailure = failure;
                    throw;
                }
                ValidateMetadata(response.Operation, response.Result, byClientId);
                await SaveCheckpointAsync(m_checkpointAcceptance, linked.Token).ConfigureAwait(false);
                await AcceptAsync(response.Operation, response.Result, linked.Token).ConfigureAwait(false);
                return CoreUtils.Clone(response.Operation)!;
            }
            finally
            {
                m_exchanges.Release();
            }
        }

        /// <summary>
        /// Submits once and collects to a typed outcome, recovering uncertain transport replies through the Client ID.
        /// An unknown lookup remains an availability error, not evidence that the original work did not execute.
        /// </summary>
        public async ValueTask<AsyncServiceResult<TResult>> ExecuteAsync(
            double executionAllowance, double resultRetentionTime, double initialWaitTime = 0,
            double collectionWaitTime = 1000, CancellationToken ct = default)
        {
            try
            {
                _ =
                    await SubmitAsync(executionAllowance, resultRetentionTime, initialWaitTime, ct)
                        .ConfigureAwait(false);
            }
            catch (ServiceResultException exception) when (
                !ct.IsCancellationRequested &&
                ReferenceEquals(exception, m_exchangeFailure) &&
                IsTransportFailure(exception.StatusCode))
            {
                m_logger.AsyncServiceSubmissionUncertain(ClientOperationId, exception.StatusCode);
                await m_binding.RecoverAsync(ct).ConfigureAwait(false);
            }
            return await WaitForResultAsync(collectionWaitTime, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Issues fresh bounded collection exchanges until an outcome is accepted, without ever repeating submission.
        /// Transport recovery stays in the shared same-Session binding. Cancellation only stops this local wait.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<AsyncServiceResult<TResult>> WaitForResultAsync(
            double waitTime = 1000, CancellationToken ct = default)
        {
            ValidateDuration(waitTime, positive: true);
            while (!m_completion.Task.IsCompleted)
            {
                ct.ThrowIfCancellationRequested();
                ServiceOperationInfoDataType info;
                try
                {
                    info = await CollectAsync(waitTime, ct).ConfigureAwait(false);
                }
                catch (ServiceResultException exception) when (
                    !ct.IsCancellationRequested &&
                    ReferenceEquals(exception, m_exchangeFailure) &&
                    IsTransportFailure(exception.StatusCode))
                {
                    await m_binding.RecoverAsync(ct).ConfigureAwait(false);
                    continue;
                }
                if (!m_completion.Task.IsCompleted &&
                    info.State is not (ServiceOperationState.Pending or ServiceOperationState.OutcomeAvailable))
                {
                    throw new ServiceResultException(StatusCodes.BadNoData,
                        "No recoverable original outcome is disclosed. This is not evidence of non-execution.");
                }
            }
            return await m_completion.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Requests stopping work without promising rollback or abandoning recovery.
        /// </summary>
        public ValueTask<ControlResponseDataType> CancelAsync(CancellationToken ct = default)
        {
            return ControlAsync(ControlMethod.Cancel, ct);
        }

        /// <summary>
        /// Acknowledges an accepted outcome. Retrying this method retries only ACK, never original work.
        /// </summary>
        public ValueTask<ControlResponseDataType> AcknowledgeAsync(CancellationToken ct = default)
        {
            return ControlAsync(ControlMethod.Acknowledge, ct);
        }

        /// <summary>
        /// Gives up Server result recovery without requesting cancellation of work.
        /// </summary>
        public ValueTask<ControlResponseDataType> AbandonAsync(CancellationToken ct = default)
        {
            return ControlAsync(ControlMethod.Abandon, ct);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            await m_lifetime.CancelAsync().ConfigureAwait(false);
            m_originalReply.TrySetCanceled();
            m_completion.TrySetCanceled();
            await m_exchanges.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                m_client.Dispose();
                m_captureTransport.Dispose();
                if (m_ownsBinding)
                {
                    await m_binding.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                m_exchanges.Release();
                m_exchanges.Dispose();
                m_lifetime.Dispose();
            }
        }

        /// <summary>
        /// Initializes a recovery-only handle from protected state; no restored handle may invoke Submit.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal async ValueTask RestoreAsync(
            RecoverableOperationCheckpoint checkpoint, ByteString version, CancellationToken ct)
        {
            if (checkpoint.RecoveryMode != RecoveryMode ||
                checkpoint.ClientOperationId != ClientOperationId ||
                checkpoint.ServiceId != m_requestType ||
                m_binding.Identity == null ||
                !checkpoint.Identity.Matches(m_binding.Identity) ||
                (RecoveryMode == ServiceOperationRecoveryMode.Session && checkpoint.SessionId != SessionId))
            {
                throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected);
            }
            m_submissionAttempted = true;
            m_automaticAcknowledgement = false;
            m_executionAllowance = checkpoint.ExecutionAllowance;
            m_resultRetentionTime = checkpoint.ResultRetentionTime;
            m_serverToken = checkpoint.ServerToken.Copy();
            m_checkpointVersion = version;
            m_typeWitness = checkpoint.TypeWitness;
            m_checkpointAcceptance = checkpoint.Acceptance;
            m_restoredRequest = CoreUtils.Clone(checkpoint.Request);
            m_invocation = InvokeAsync();
            _ = ObserveInvocationAsync(m_invocation);
            _ = await m_prepared.Task.WaitAsync(ct).ConfigureAwait(false);
            if (checkpoint.Acceptance >= RecoverableCheckpointAcceptance.OutcomeValidated)
            {
                m_savedOutcome = checkpoint;
                if (checkpoint.Acceptance >= RecoverableCheckpointAcceptance.Accepted)
                {
                    m_restoringAccepted = true;
                    try
                    {
                        _ = await CollectAsync(ct: ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        m_restoringAccepted = false;
                    }
                }
            }
        }

        /// <summary>
        /// Commits the validated original outcome and local acceptance state before an enclosing receipt may ACK it.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal ValueTask CommitAcceptanceAsync(CancellationToken ct)
        {
            if (m_candidate == null)
            {
                throw new InvalidOperationException("No validated original outcome is available for acceptance.");
            }
            return SaveCheckpointAsync(RecoverableCheckpointAcceptance.Accepted, ct);
        }

        /// <summary>
        /// Captures one generated invocation, preserving its typed decoding continuation across uncertain submission.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private async Task<TResult> InvokeAsync()
        {
            try
            {
                TResult result = await m_invoke(m_client, m_lifetime.Token).ConfigureAwait(false);
                if (m_originalRequest == null || !m_originalReply.Task.IsCompleted)
                {
                    throw new InvalidOperationException(
                        "The callback must issue and await exactly one eligible request.");
                }
                return result;
            }
            catch (Exception exception)
            {
                m_prepared.TrySetException(exception);
                throw;
            }
        }

        /// <summary>
        /// Observes a decoder/preparation failure even if the caller abandons its local handle before collection.
        /// </summary>
        private static async Task ObserveInvocationAsync(Task<TResult> invocation)
        {
            try
            {
                _ = await invocation.ConfigureAwait(false);
            }
            catch
            {
                // Submit or Collect reports the same retained task's failure; this only prevents an unobserved task.
            }
        }

        /// <summary>
        /// Captures, rather than sends, the eligible request produced by the normal generated client/proxy.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private ValueTask<IServiceResponse> CaptureAsync(IServiceRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (m_binding.Invalidated.IsCompleted)
            {
                throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
            }
            if (request.RequestHeader != null &&
                !request.RequestHeader.AuthenticationToken.IsNull &&
                request.RequestHeader.AuthenticationToken != m_authenticationToken)
            {
                throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
            }
            lock (m_state)
            {
                if (m_originalRequest != null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState,
                        "An operation callback cannot issue another request after capture.");
                }
                if (ExpandedNodeId.ToNodeId(request.TypeId, m_client.MessageContext.NamespaceUris) == m_requestType)
                {
                    if (m_restoredRequest != null)
                    {
                        IServiceRequest projected = CoreUtils.Clone(request)!;
                        projected.RequestHeader = CoreUtils.Clone(m_restoredRequest.RequestHeader)!;
                        if (!CoreUtils.IsEqual(projected, m_restoredRequest))
                        {
                            throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                                "The restored typed invocation does not match its saved original request.");
                        }
                    }
                    m_originalRequest = CoreUtils.Clone(m_restoredRequest ?? request)!;
                    m_prepared.TrySetResult(m_originalRequest);
                    return new ValueTask<IServiceResponse>(m_originalReply.Task);
                }
            }
            if (request is not (ReadRequest or BrowseRequest or TranslateBrowsePathsToNodeIdsRequest))
            {
                throw new ServiceResultException(StatusCodes.BadServiceUnsupported,
                    "Only read-only preparation may precede the captured request.");
            }
            return m_binding.Session.TransportChannel.SendRequestAsync(request, ct);
        }

        /// <summary>
        /// Validates identity, exact lifetimes and the declared envelope before adopting a token or outcome.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateMetadata(
            ServiceOperationInfoDataType info, ExtensionObject payload, bool queriedByClientId, bool control = false)
        {
            if (info == null ||
                info.State is < ServiceOperationState.Unknown or > ServiceOperationState.Expired ||
                info.OutcomeKind is < ServiceOperationOutcome.None or > ServiceOperationOutcome.ExecutionIndeterminate)
            {
                throw InvalidResponse();
            }
            if (info.State == ServiceOperationState.Unknown)
            {
                if (info.ClientOperationId != (queriedByClientId ? ClientOperationId : Uuid.Empty) ||
                    !info.ServerToken.IsNull ||
                    !info.ServiceId.IsNull ||
                    info.RecoveryMode != ServiceOperationRecoveryMode.Unspecified ||
                    info.OutcomeKind != ServiceOperationOutcome.None ||
                    info.OutcomeStatus != StatusCodes.Good ||
                    info.CancellationRequested ||
                    info.ExecutionAllowance != 0 ||
                    info.ResultRetentionTime != 0 ||
                    info.ExecutionRemaining != 0 ||
                    info.ResultRemaining != 0 ||
                    info.HistoryRemaining != 0 ||
                    info.RetryAfter != 0 ||
                    !payload.IsNull)
                {
                    throw InvalidResponse();
                }
            }
            else
            {
                ByteString token = ServerToken;
                if (info.ClientOperationId != ClientOperationId ||
                    info.ServerToken.Length is <= 0 or > 64 ||
                    (!token.IsNull && token != info.ServerToken) ||
                    info.ServiceId != m_requestType ||
                    info.RecoveryMode != RecoveryMode ||
                    info.ExecutionAllowance != m_executionAllowance ||
                    info.ResultRetentionTime != m_resultRetentionTime ||
                    !IsRemaining(info.ExecutionRemaining, m_executionAllowance) ||
                    !IsRemaining(info.ResultRemaining, m_resultRetentionTime) ||
                    !IsRemaining(info.HistoryRemaining, m_resultRetentionTime) ||
                    !IsRemaining(info.RetryAfter, info.ExecutionRemaining))
                {
                    throw InvalidResponse();
                }
                bool pending = info.State == ServiceOperationState.Pending;
                bool available = info.State == ServiceOperationState.OutcomeAvailable;
                if ((pending &&
                    (info.OutcomeKind != ServiceOperationOutcome.None ||
                        info.OutcomeStatus != StatusCodes.Good ||
                        info.ResultRemaining != 0 ||
                        info.HistoryRemaining != 0 ||
                        (info.RetryAfter > 0 && info.RetryAfter >= info.ResultRetentionTime))) ||
                    (!pending && (info.ExecutionRemaining != 0 || info.RetryAfter != 0)) ||
                    (available && (info.OutcomeKind == ServiceOperationOutcome.None || info.HistoryRemaining != 0)) ||
                    (!available && info.ResultRemaining != 0) ||
                    ((!control && available && info.OutcomeKind == ServiceOperationOutcome.ServiceResponse) ==
                        payload.IsNull) ||
                    (info.OutcomeKind == ServiceOperationOutcome.ExecutionDeadlineExceeded &&
                        info.OutcomeStatus != StatusCodes.BadTimeout) ||
                    (info.OutcomeKind == ServiceOperationOutcome.ResultUnavailable &&
                        info.OutcomeStatus != StatusCodes.BadResponseTooLarge &&
                        info.OutcomeStatus != StatusCodes.BadEncodingError) ||
                    (info.OutcomeKind == ServiceOperationOutcome.ExecutionIndeterminate &&
                        (RecoveryMode != ServiceOperationRecoveryMode.Durable ||
                            info.OutcomeStatus != StatusCodes.BadUnexpectedError)) ||
                    (info.State is ServiceOperationState.Acknowledged or ServiceOperationState.Expired &&
                        info.OutcomeKind == ServiceOperationOutcome.None) ||
                    (info.OutcomeKind == ServiceOperationOutcome.None && info.OutcomeStatus != StatusCodes.Good))
                {
                    throw InvalidResponse();
                }
                if (m_candidate is { } accepted)
                {
                    ServiceOperationInfoDataType previous = accepted.Operation;
                    if (pending ||
                        info.OutcomeKind != previous.OutcomeKind ||
                        info.OutcomeStatus != previous.OutcomeStatus)
                    {
                        throw InvalidResponse();
                    }
                }
                lock (m_state)
                {
                    m_serverToken = info.ServerToken.Copy();
                }
            }
            lock (m_state)
            {
                m_lastOperation = CoreUtils.Clone(info);
            }
        }

        /// <summary>
        /// Accepts an available outcome only after generated decoding and any application storage policy succeeds.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask AcceptAsync(
            ServiceOperationInfoDataType info, ExtensionObject payload, CancellationToken ct)
        {
            await m_binding.VerifyTypeContextAsync(ct).ConfigureAwait(false);
            if (m_candidate != null &&
                info.State == ServiceOperationState.OutcomeAvailable &&
                info.OutcomeKind == ServiceOperationOutcome.ServiceResponse &&
                (m_candidate.OriginalResponse is not { } retained ||
                    !payload.TryGetValue(out IServiceResponse? repeated, m_binding.MessageContext) ||
                    !CoreUtils.IsEqual(repeated, retained)))
            {
                throw InvalidResponse();
            }
            if (m_completion.Task.IsCompleted || info.State != ServiceOperationState.OutcomeAvailable)
            {
                return;
            }
            if (m_candidate == null)
            {
                IServiceResponse? original = null;
                TResult value = default!;
                ServiceResultException? failure = null;
                if (info.OutcomeKind == ServiceOperationOutcome.ServiceResponse)
                {
                    if (!payload.TryGetValue(out original, m_binding.MessageContext) ||
                        original.ResponseHeader == null ||
                        original.ResponseHeader.RequestHandle != m_originalRequest!.RequestHeader.RequestHandle ||
                        original.ResponseHeader.ServiceResult != info.OutcomeStatus)
                    {
                        throw InvalidResponse();
                    }
                    original = CoreUtils.Clone(original)!;
                    RecoverableServiceRequests.ValidateResponse(m_originalRequest!, original);
                    if (StatusCode.IsBad(original.ResponseHeader.ServiceResult))
                    {
                        m_originalFailure = CaptureClient.CreateOriginalFailure(original.ResponseHeader);
                        m_originalReply.TrySetException(m_originalFailure);
                    }
                    else
                    {
                        m_originalReply.TrySetResult(CoreUtils.Clone(original)!);
                    }
                    try
                    {
                        value = await m_invocation!.ConfigureAwait(false);
                    }
                    catch (ServiceResultException exception) when (IsOriginalFailure(exception))
                    {
                        failure = exception;
                    }
                }
                else
                {
                    failure = new ServiceResultException(info.OutcomeStatus,
                        "The operation has a terminal protocol outcome; its effects may be unknown.");
                    m_originalReply.TrySetException(failure);
                }
                m_candidate = new AsyncServiceResult<TResult>(info, original, value, failure);
            }
            if (!m_restoringAccepted)
            {
                await SaveCheckpointAsync(RecoverableCheckpointAcceptance.OutcomeValidated, ct).ConfigureAwait(false);
                if (m_accept != null)
                {
                    await m_accept(m_candidate, ct).ConfigureAwait(false);
                }
                if (!DeferAcceptanceCheckpoint)
                {
                    await CommitAcceptanceAsync(ct).ConfigureAwait(false);
                }
            }
            if (!m_completion.TrySetResult(m_candidate))
            {
                return;
            }
            if (m_automaticAcknowledgement)
            {
                await AcknowledgeAcceptedAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Distinguishes a legitimate original/per-Method failure from a local typed-decoding failure.
        /// </summary>
        private bool IsOriginalFailure(ServiceResultException failure)
        {
            return ReferenceEquals(failure, m_originalFailure) ||
                m_originalFailures.Exists(original => ReferenceEquals(original, failure));
        }

        /// <summary>
        /// Serializes explicit controls while preserving their distinct wire actions.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private async ValueTask<ControlResponseDataType> ControlAsync(ControlMethod method, CancellationToken ct)
        {
            ThrowIfDisposed();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_lifetime.Token);
            await m_exchanges.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (method == ControlMethod.Acknowledge && !m_completion.Task.IsCompletedSuccessfully)
                {
                    throw new InvalidOperationException("An outcome must be accepted before acknowledging it.");
                }
                if (method == ControlMethod.Acknowledge &&
                    AcknowledgementFailure is { } failure &&
                    IsTransportFailure(failure.StatusCode))
                {
                    await m_binding.RecoverAsync(linked.Token).ConfigureAwait(false);
                }
                return await SendControlAsync(method, linked.Token).ConfigureAwait(false);
            }
            catch (ServiceResultException error) when (method == ControlMethod.Acknowledge)
            {
                SetAcknowledgementFailure(error.Result);
                throw;
            }
            catch (OperationCanceledException) when (method == ControlMethod.Acknowledge)
            {
                SetAcknowledgementFailure(new ServiceResult(StatusCodes.BadRequestCancelledByClient));
                throw;
            }
            catch (Exception error) when (method == ControlMethod.Acknowledge &&
                error is IOException or HttpRequestException or SocketException)
            {
                SetAcknowledgementFailure(new ServiceResult(StatusCodes.BadCommunicationError));
                throw;
            }
            finally
            {
                m_exchanges.Release();
            }
        }

        /// <summary>
        /// Does not let ACK transport/cancellation failures replace an accepted caller result.
        /// </summary>
        private async ValueTask AcknowledgeAcceptedAsync(CancellationToken ct)
        {
            try
            {
                _ = await SendControlAsync(ControlMethod.Acknowledge, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                SetAcknowledgementFailure(exception.Result);
            }
            catch (OperationCanceledException)
            {
                SetAcknowledgementFailure(new ServiceResult(StatusCodes.BadRequestCancelledByClient));
            }
            catch (ObjectDisposedException)
            {
                SetAcknowledgementFailure(new ServiceResult(StatusCodes.BadSessionClosed));
            }
            catch (Exception exception) when (exception is IOException or HttpRequestException or SocketException)
            {
                SetAcknowledgementFailure(new ServiceResult(StatusCodes.BadCommunicationError));
            }
        }

        /// <summary>
        /// Uses a fresh send-once control exchange and records its explicit acknowledgement disposition.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<ControlResponseDataType> SendControlAsync(ControlMethod method, CancellationToken ct)
        {
            ByteString token = ServerToken;
            if (token.IsNull)
            {
                throw new InvalidOperationException("Recover the Server token before issuing a control.");
            }
            if (method == ControlMethod.Acknowledge &&
                m_binding.Options.CheckpointStore != null &&
                m_checkpointAcceptance < RecoverableCheckpointAcceptance.Accepted)
            {
                throw new InvalidOperationException("The accepted outcome must be durably checkpointed before ACK.");
            }
            RecoverableManagerClient manager = await m_binding.GetManagerAsync(ct).ConfigureAwait(false);
            var request = new OperationControlRequestDataType { ServerToken = token };
            ControlResponseDataType response = method switch
            {
                ControlMethod.Acknowledge => await manager.AcknowledgeAsync(request, ct).ConfigureAwait(false),
                ControlMethod.Cancel => await manager.CancelAsync(request, ct).ConfigureAwait(false),
                ControlMethod.Abandon => await manager.AbandonAsync(request, ct).ConfigureAwait(false),
                _ => throw new InvalidOperationException()
            };
            if (response.Result is < ServiceOperationControlResult.Applied or
                > ServiceOperationControlResult.UnknownOperation)
            {
                throw InvalidResponse();
            }
            ValidateMetadata(response.Operation, default, queriedByClientId: false, control: true);
            if (response.Result == ServiceOperationControlResult.UnknownOperation !=
                (response.Operation.State == ServiceOperationState.Unknown))
            {
                throw InvalidResponse();
            }
            bool applied = response.Result is ServiceOperationControlResult.Applied or
                ServiceOperationControlResult.AlreadyApplied;
            if ((method == ControlMethod.Cancel && applied && !response.Operation.CancellationRequested) ||
                (method == ControlMethod.Abandon &&
                    applied &&
                    response.Operation.State != ServiceOperationState.Abandoned) ||
                (method == ControlMethod.Abandon &&
                    response.Result == ServiceOperationControlResult.NotApplicable &&
                    response.Operation.State is not
                        (ServiceOperationState.Acknowledged or ServiceOperationState.Expired)) ||
                (method == ControlMethod.Acknowledge &&
                    response.Result == ServiceOperationControlResult.NotApplicable &&
                    response.Operation.State is not (ServiceOperationState.Pending or ServiceOperationState.Abandoned or
                        ServiceOperationState.Expired)))
            {
                throw InvalidResponse();
            }
            if (method == ControlMethod.Acknowledge)
            {
                if (applied && response.Operation.State != ServiceOperationState.Acknowledged)
                {
                    throw InvalidResponse();
                }
                if (response.Result is
                    ServiceOperationControlResult.Applied or ServiceOperationControlResult.AlreadyApplied)
                {
                    await SaveCheckpointAsync(RecoverableCheckpointAcceptance.Acknowledged, ct).ConfigureAwait(false);
                }
                lock (m_state)
                {
                    m_lastAcknowledgement = CoreUtils.Clone(response);
                    m_acknowledgementFailure = null;
                }
            }
            return response;
        }

        /// <summary>
        /// Writes protected recovery state without credentials and never overwrites a concurrent collector.
        /// </summary>
        private async ValueTask SaveCheckpointAsync(RecoverableCheckpointAcceptance acceptance, CancellationToken ct)
        {
            RecoverableOperationCheckpointStore? store = m_binding.Options.CheckpointStore;
            if (store == null)
            {
                return;
            }
            if (acceptance < m_checkpointAcceptance)
            {
                acceptance = m_checkpointAcceptance;
            }
            var checkpoint = new RecoverableOperationCheckpoint(
                ClientOperationId, RecoveryMode, m_binding.Identity!, SessionId, m_requestType,
                m_binding.NamespaceUris, m_binding.ServerUris, m_executionAllowance, m_resultRetentionTime,
                m_originalRequest!, ServerToken, m_candidate?.Operation ?? LastOperation,
                m_candidate?.OriginalResponse, acceptance,
                DeferAcceptanceCheckpoint || m_accept != null, m_requiresMethodProjection, m_typeWitness);
            m_checkpointVersion = await store.SaveAsync(
                checkpoint, m_checkpointVersion, m_binding.Session.MessageContext, ct).ConfigureAwait(false);
            m_checkpointAcceptance = acceptance;
        }

        /// <summary>
        /// Reports an ACK failure using bounded metadata, never its token or the original payload.
        /// </summary>
        private void SetAcknowledgementFailure(ServiceResult error)
        {
            lock (m_state)
            {
                m_acknowledgementFailure = error;
            }
            m_logger.AsyncServiceAcknowledgementPending(ClientOperationId, error.StatusCode);
        }

        /// <summary>
        /// Checks a reported remaining duration without creating a new local expiration promise.
        /// </summary>
        private static bool IsRemaining(double value, double maximum)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= maximum;
        }

        /// <summary>
        /// Classifies only recoverable exchange failures, never the original outcome carried inside an envelope.
        /// </summary>
        private static bool IsTransportFailure(StatusCode status)
        {
            return ManagedTransportChannelLease.IsTransientChannelError(status) ||
                status == StatusCodes.BadTimeout ||
                status == StatusCodes.BadRequestTimeout;
        }

        /// <summary>
        /// Rejects invalid Client arguments before preparing or sending original work.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static void ValidateDuration(double value, bool positive)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || (positive ? value <= 0 : value < 0))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        /// <summary>
        /// Prevents changing acceptance semantics or repeating a submission once the attempt has started.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void EnsureNotSubmitted()
        {
            ThrowIfDisposed();
            if (m_submissionAttempted)
            {
                throw new InvalidOperationException("This handle can attempt submission only once.");
            }
        }

        /// <summary>
        /// Rejects use of local resources after disposal without affecting the Server operation.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(AsyncServiceOperation<>));
            }
        }

        /// <summary>
        /// Creates a response-validation error that does not disclose tokens or payloads.
        /// </summary>
        private static ServiceResultException InvalidResponse()
        {
            return new ServiceResultException(
                StatusCodes.BadDecodingError, "The recoverable Service envelope does not match this operation.");
        }

        /// <summary>
        /// Disables proxy fallback on the capturing client without inspecting ordinary clients' transports.
        /// </summary>
        private sealed class CaptureClient(
            AsyncServiceOperation<TResult> operation, ITransportChannel transport, ITelemetryContext telemetry) :
            SessionClient(transport, telemetry), IMethodCallRetryPolicy, IClientOperationContext
        {
            /// <inheritdoc/>
            public bool AllowMethodIdFallback => false;

            /// <inheritdoc/>
            public bool RequiresTypedOperationContext => true;

            /// <inheritdoc/>
            public bool HasOperationContext => true;

            /// <inheritdoc/>
            public void ObserveOriginalFailure(ServiceResultException failure)
            {
                operation.m_originalFailures.Add(failure);
            }

            /// <inheritdoc/>
            public ValueTask<TValue> ExecuteOperationAsync<TValue>(
                Func<CancellationToken, ValueTask<TValue>> invoke, CancellationToken ct)
            {
                return invoke(ct);
            }

            /// <summary>
            /// Uses the ordinary header validator to preserve diagnostics and the exact original exception instance.
            /// </summary>
            /// <exception cref="ServiceResultException"></exception>
            internal static ServiceResultException CreateOriginalFailure(ResponseHeader header)
            {
                try
                {
                    ValidateResponse(header);
                }
                catch (ServiceResultException failure)
                {
                    return failure;
                }
                throw InvalidResponse();
            }
        }

        /// <summary>
        /// Reuses generated client/proxy request building without sending the captured original request.
        /// </summary>
        private sealed class CaptureTransport(
            AsyncServiceOperation<TResult> operation, ITransportChannel metadata) :
            ITransportChannel
        {
            public TransportChannelFeatures SupportedFeatures => TransportChannelFeatures.None;
            public EndpointDescription EndpointDescription => metadata.EndpointDescription;
            public EndpointConfiguration EndpointConfiguration => metadata.EndpointConfiguration;
            public byte[] ChannelThumbprint => metadata.ChannelThumbprint;
            public byte[] ClientChannelCertificate => metadata.ClientChannelCertificate;
            public byte[] ServerChannelCertificate => metadata.ServerChannelCertificate;
            public IServiceMessageContext MessageContext => metadata.MessageContext;
            public int OperationTimeout { get; set; } = metadata.OperationTimeout;

            public ValueTask<IServiceResponse> SendRequestAsync(IServiceRequest request, CancellationToken ct = default)
            {
                return operation.CaptureAsync(request, ct);
            }

            public ValueTask ReconnectAsync(
                ITransportWaitingConnection? connection = null, CancellationToken ct = default)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported);
            }

            public ValueTask CloseAsync(CancellationToken ct = default)
            {
                return default;
            }

            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Selects a generated control Method locally; no action or mode field is added to its wire request.
        /// </summary>
        private enum ControlMethod
        {
            Acknowledge,
            Cancel,
            Abandon
        }

        private readonly RecoverableExecutionBinding m_binding;
        private readonly NodeId m_requestType;
        private readonly NodeId m_authenticationToken;
        private readonly Func<ISessionClient, CancellationToken, ValueTask<TResult>> m_invoke;
        private readonly CaptureClient m_client;
        private readonly CaptureTransport m_captureTransport;
        private readonly ILogger m_logger;
        private readonly Lock m_state = new();
        private readonly SemaphoreSlim m_exchanges = new(1, 1);
        private readonly CancellationTokenSource m_lifetime = new();

        private readonly TaskCompletionSource<IServiceRequest> m_prepared =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<IServiceResponse> m_originalReply =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<AsyncServiceResult<TResult>> m_completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Task<TResult>? m_invocation;
        private IServiceRequest? m_originalRequest;
        private readonly bool m_ownsBinding;
        private readonly bool m_requiresMethodProjection;
        private Func<AsyncServiceResult<TResult>, CancellationToken, ValueTask>? m_accept;
        private AsyncServiceResult<TResult>? m_candidate;
        private ByteString m_serverToken;
        private ServiceOperationInfoDataType? m_lastOperation;
        private ControlResponseDataType? m_lastAcknowledgement;
        private ServiceResult? m_acknowledgementFailure;
        private double m_executionAllowance;
        private double m_resultRetentionTime;
        private bool m_submissionAttempted;
        private bool m_automaticAcknowledgement = true;
        private readonly List<ServiceResultException> m_originalFailures = [];
        private ServiceResultException? m_originalFailure;
        private ServiceResultException? m_exchangeFailure;
        private IServiceRequest? m_restoredRequest;
        private RecoverableOperationCheckpoint? m_savedOutcome;
        private ByteString m_checkpointVersion;
        private ByteString m_typeWitness;

        private RecoverableCheckpointAcceptance m_checkpointAcceptance =
            RecoverableCheckpointAcceptance.SubmissionAttempted;

        private bool m_restoringAccepted;
        private int m_disposed;
    }

    /// <summary>
    /// Reports acknowledgement state independently of accepted business outcomes.
    /// </summary>
    internal static partial class AsyncServiceOperationLog
    {
        [LoggerMessage(EventId = ClientEventIds.AsyncServiceOperation, Level = LogLevel.Warning,
            Message = "Async Service {ClientOperationId} outcome was accepted; " +
                "acknowledgement is unconfirmed: {Status}.")]
        public static partial void AsyncServiceAcknowledgementPending(
            this ILogger logger, Uuid clientOperationId, StatusCode status);

        [LoggerMessage(EventId = ClientEventIds.AsyncServiceOperation + 1, Level = LogLevel.Warning,
            Message = "Async Service {ClientOperationId} submission reply is uncertain; " +
                "collecting without replay: {Status}.")]
        public static partial void AsyncServiceSubmissionUncertain(
            this ILogger logger, Uuid clientOperationId, StatusCode status);
    }
}
