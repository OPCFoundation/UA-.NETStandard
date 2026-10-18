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
            SessionRequestContext binding,
            NodeId requestType,
            Func<ISessionClient, CancellationToken, ValueTask<TResult>> invoke,
            bool ownsBinding = true)
        {
            m_binding = binding;
            m_ownsBinding = ownsBinding;
            m_requestType = requestType;
            m_invoke = invoke;
            ClientOperationId = Uuid.NewUuid();
            SessionConfiguration configuration = binding.Session.SaveSessionConfiguration();
            m_authenticationToken = configuration.AuthenticationToken;
            m_logger = binding.Session.MessageContext.Telemetry.CreateLogger<AsyncServiceOperation<TResult>>();
            m_captureTransport = new CaptureTransport(this, binding.Session.TransportChannel);
            m_client = new CaptureClient(m_captureTransport, binding.Session.MessageContext.Telemetry)
            {
                ReturnDiagnostics = binding.Session.ReturnDiagnostics,
                DefaultTimeoutHint = binding.Session.DefaultTimeoutHint
            };
            m_client.SessionCreated(configuration.SessionId, configuration.AuthenticationToken);
            binding.ResponseReceived += ObserveSubmissionReply;
        }

        /// <summary>
        /// Gets whether a decoded outer ServiceFault proved that Submit rejected unsupported recovery.
        /// Transport errors and embedded original outcomes never set this flag.
        /// </summary>
        internal bool UnsupportedSubmissionRejected => Volatile.Read(ref m_unsupportedSubmissionRejected) != 0;

        /// <summary>
        /// Gets the nonzero Client ID, including before submission and after local disposal.
        /// </summary>
        public Uuid ClientOperationId { get; }

        /// <summary>
        /// Gets the original Server Session ID. A replacement Session is never adopted.
        /// </summary>
        public NodeId SessionId => m_binding.SessionId;

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
        public ControlServiceExecutionResponse? LastAcknowledgement
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
                SubmitServiceResponse response = await m_binding.Session.SubmitServiceAsync(
                    null, ClientOperationId, new ExtensionObject(request),
                    executionAllowance, resultRetentionTime, waitTime, linked.Token).ConfigureAwait(false);
                try
                {
                    ValidateMetadata(response.Operation, response.Result, queriedByClientId: true);
                    await AcceptAsync(response.Operation, response.Result, linked.Token).ConfigureAwait(false);
                    return CoreUtils.Clone(response.Operation)!;
                }
                finally
                {
                    response.Reuse();
                }
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
                GetServiceResultResponse response = await m_binding.Session.GetServiceResultAsync(
                    null, byClientId ? ClientOperationId : Uuid.Empty,
                    byClientId ? default : token, waitTime, linked.Token).ConfigureAwait(false);
                try
                {
                    ValidateMetadata(response.Operation, response.Result, byClientId);
                    await AcceptAsync(response.Operation, response.Result, linked.Token).ConfigureAwait(false);
                    return CoreUtils.Clone(response.Operation)!;
                }
                finally
                {
                    response.Reuse();
                }
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
                !ct.IsCancellationRequested && IsTransportFailure(exception.StatusCode))
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
                    !ct.IsCancellationRequested && IsTransportFailure(exception.StatusCode))
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
        public ValueTask<ControlServiceExecutionResponse> CancelAsync(CancellationToken ct = default)
        {
            return ControlAsync(ServiceOperationControlAction.Cancel, ct);
        }

        /// <summary>
        /// Acknowledges an accepted outcome. Retrying this method retries only ACK, never original work.
        /// </summary>
        public ValueTask<ControlServiceExecutionResponse> AcknowledgeAsync(CancellationToken ct = default)
        {
            return ControlAsync(ServiceOperationControlAction.Acknowledge, ct);
        }

        /// <summary>
        /// Gives up Server result recovery without requesting cancellation of work.
        /// </summary>
        public ValueTask<ControlServiceExecutionResponse> AbandonAsync(CancellationToken ct = default)
        {
            return ControlAsync(ServiceOperationControlAction.Abandon, ct);
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
                m_binding.ResponseReceived -= ObserveSubmissionReply;
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
        /// Separates a decoded pre-admission refusal from failures of transport, collection or the original Service.
        /// </summary>
        private void ObserveSubmissionReply(IServiceRequest request, IServiceResponse response)
        {
            if (request is SubmitServiceRequest submit &&
                submit.ClientOperationId == ClientOperationId &&
                response is ServiceFault &&
                response.ResponseHeader.ServiceResult == StatusCodes.BadServiceUnsupported)
            {
                Volatile.Write(ref m_unsupportedSubmissionRejected, 1);
            }
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
                    m_originalRequest = CoreUtils.Clone(request)!;
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
                info.OutcomeKind is < ServiceOperationOutcome.None or > ServiceOperationOutcome.ResultUnavailable)
            {
                throw InvalidResponse();
            }
            if (info.State == ServiceOperationState.Unknown)
            {
                if (info.ClientOperationId != (queriedByClientId ? ClientOperationId : Uuid.Empty) ||
                    !info.ServerToken.IsNull ||
                    !info.ServiceId.IsNull ||
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
                        info.OutcomeStatus != StatusCodes.BadEncodingError))
                {
                    throw InvalidResponse();
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
                    if (!payload.TryGetValue(out original, m_binding.Session.MessageContext) ||
                        original.ResponseHeader == null ||
                        original.ResponseHeader.RequestHandle != m_originalRequest!.RequestHeader.RequestHandle ||
                        original.ResponseHeader.ServiceResult != info.OutcomeStatus)
                    {
                        throw InvalidResponse();
                    }
                    ValidateOriginalResponse(original);
                    m_originalReply.TrySetResult(original);
                    try
                    {
                        value = await m_invocation!.ConfigureAwait(false);
                    }
                    catch (ServiceResultException exception) when (IsOriginalFailure(original, exception.StatusCode))
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
            if (m_accept != null)
            {
                await m_accept(m_candidate, ct).ConfigureAwait(false);
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
        /// Preserves the original ServiceFault exception and ordinary batch validation semantics.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ValidateOriginalResponse(IServiceResponse response)
        {
            if (response is ServiceFault)
            {
                if (!StatusCode.IsBad(response.ResponseHeader.ServiceResult))
                {
                    throw InvalidResponse();
                }
                return;
            }
            switch (m_originalRequest)
            {
                case ReadRequest request when response is ReadResponse read:
                    if (StatusCode.IsGood(read.ResponseHeader.ServiceResult))
                    {
                        ClientBase.ValidateResponse(read.Results, request.NodesToRead);
                        ClientBase.ValidateDiagnosticInfos(read.DiagnosticInfos, request.NodesToRead);
                    }
                    break;
                case WriteRequest request when response is WriteResponse write:
                    if (StatusCode.IsGood(write.ResponseHeader.ServiceResult))
                    {
                        ClientBase.ValidateResponse(write.Results, request.NodesToWrite);
                        ClientBase.ValidateDiagnosticInfos(write.DiagnosticInfos, request.NodesToWrite);
                    }
                    break;
                case CallRequest request when response is CallResponse call:
                    if (StatusCode.IsGood(call.ResponseHeader.ServiceResult))
                    {
                        ClientBase.ValidateResponse(call.Results, request.MethodsToCall);
                        ClientBase.ValidateDiagnosticInfos(call.DiagnosticInfos, request.MethodsToCall);
                    }
                    break;
                default:
                    throw InvalidResponse();
            }
        }

        /// <summary>
        /// Distinguishes a legitimate original/per-Method failure from a local typed-decoding failure.
        /// </summary>
        private static bool IsOriginalFailure(IServiceResponse original, StatusCode status)
        {
            if (StatusCode.IsBad(original.ResponseHeader.ServiceResult) &&
                original.ResponseHeader.ServiceResult == status)
            {
                return true;
            }
            if (original is CallResponse call)
            {
                foreach (CallMethodResult result in call.Results)
                {
                    if (StatusCode.IsBad(result.StatusCode) && result.StatusCode == status)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Serializes explicit controls while preserving their distinct wire actions.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private async ValueTask<ControlServiceExecutionResponse> ControlAsync(
            ServiceOperationControlAction action, CancellationToken ct)
        {
            ThrowIfDisposed();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_lifetime.Token);
            await m_exchanges.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (action == ServiceOperationControlAction.Acknowledge && !m_completion.Task.IsCompletedSuccessfully)
                {
                    throw new InvalidOperationException("An outcome must be accepted before acknowledging it.");
                }
                if (action == ServiceOperationControlAction.Acknowledge &&
                    AcknowledgementFailure is { } failure &&
                    IsTransportFailure(failure.StatusCode))
                {
                    await m_binding.RecoverAsync(linked.Token).ConfigureAwait(false);
                }
                return await SendControlAsync(action, linked.Token).ConfigureAwait(false);
            }
            catch (ServiceResultException error) when (action == ServiceOperationControlAction.Acknowledge)
            {
                SetAcknowledgementFailure(error.Result);
                throw;
            }
            catch (OperationCanceledException) when (action == ServiceOperationControlAction.Acknowledge)
            {
                SetAcknowledgementFailure(new ServiceResult(StatusCodes.BadRequestCancelledByClient));
                throw;
            }
            catch (Exception error) when (action == ServiceOperationControlAction.Acknowledge &&
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
                ControlServiceExecutionResponse response = await SendControlAsync(
                    ServiceOperationControlAction.Acknowledge, ct).ConfigureAwait(false);
                response.Reuse();
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
        private async ValueTask<ControlServiceExecutionResponse> SendControlAsync(
            ServiceOperationControlAction action, CancellationToken ct)
        {
            ByteString token = ServerToken;
            if (token.IsNull)
            {
                throw new InvalidOperationException("Recover the Server token before issuing a control.");
            }
            ControlServiceExecutionResponse response = await m_binding.Session.ControlServiceExecutionAsync(
                null, token, action, ct).ConfigureAwait(false);
            try
            {
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
                if (action == ServiceOperationControlAction.Acknowledge)
                {
                    if (response.Result is ServiceOperationControlResult.Applied or
                        ServiceOperationControlResult.AlreadyApplied &&
                        response.Operation.State != ServiceOperationState.Acknowledged)
                    {
                        throw InvalidResponse();
                    }
                    lock (m_state)
                    {
                        m_lastAcknowledgement = CoreUtils.Clone(response);
                        m_acknowledgementFailure = null;
                    }
                }
                return response;
            }
            catch
            {
                response.Reuse();
                throw;
            }
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
        private sealed class CaptureClient(ITransportChannel transport, ITelemetryContext telemetry) :
            SessionClient(transport, telemetry), IMethodCallRetryPolicy
        {
            /// <inheritdoc/>
            public bool AllowMethodIdFallback => false;
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

        private readonly SessionRequestContext m_binding;
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
        private int m_unsupportedSubmissionRejected;
        private Func<AsyncServiceResult<TResult>, CancellationToken, ValueTask>? m_accept;
        private AsyncServiceResult<TResult>? m_candidate;
        private ByteString m_serverToken;
        private ServiceOperationInfoDataType? m_lastOperation;
        private ControlServiceExecutionResponse? m_lastAcknowledgement;
        private ServiceResult? m_acknowledgementFailure;
        private double m_executionAllowance;
        private double m_resultRetentionTime;
        private bool m_submissionAttempted;
        private bool m_automaticAcknowledgement = true;
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
