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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Creates typed operation handles before any cancellable submission can execute work.
    /// </summary>
    public sealed class AsyncServiceClient
    {
        /// <summary>
        /// Creates a Session-mode factory with advertised defaults and caller-completion acceptance.
        /// </summary>
        public AsyncServiceClient()
            : this(new RecoverableSessionOptions())
        {
        }

        /// <summary>
        /// Creates a factory with an explicit mode, authenticated continuity and optional protected recovery storage.
        /// </summary>
        public AsyncServiceClient(RecoverableSessionOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            RecoverableSessionExecution.ValidateOptions(options);
            m_options = options with { };
        }

        /// <summary>
        /// Prepares any of the sixteen eligible generated request/response pairs without sending original work.
        /// </summary>
        /// <typeparam name="TResponse">The exact generated original response type.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
        public ValueTask<AsyncServiceOperation<TResponse>> CreateAsync<TResponse>(
            ISession session, IServiceRequest request, CancellationToken ct = default)
            where TResponse : class, IServiceResponse
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            NodeId serviceId = RecoverableServiceRequests.GetRequestType<TResponse>(request);
            IServiceRequest captured = CoreUtils.Clone(request)!;
            return CreateAsync(session, serviceId,
                (client, token) => RecoverableServiceRequests.InvokeAsync<TResponse>(client, captured, token), ct);
        }

        /// <summary>
        /// Prepares one generated Read request without batching it into separate recoverable operations.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
        public ValueTask<AsyncServiceOperation<ReadResponse>> CreateReadAsync(
            ISession session, ReadRequest request, CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            ReadRequest captured = CoreUtils.Clone(request)!;
            return CreateAsync(session, DataTypeIds.ReadRequest,
                (client, token) => client.ReadAsync(captured.RequestHeader, captured.MaxAge,
                    captured.TimestampsToReturn, captured.NodesToRead, token), ct);
        }

        /// <summary>
        /// Prepares one generated Write request, preserving its complete per-item response.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
        public ValueTask<AsyncServiceOperation<WriteResponse>> CreateWriteAsync(
            ISession session, WriteRequest request, CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            WriteRequest captured = CoreUtils.Clone(request)!;
            return CreateAsync(session, DataTypeIds.WriteRequest,
                (client, token) => client.WriteAsync(captured.RequestHeader, captured.NodesToWrite, token), ct);
        }

        /// <summary>
        /// Prepares one generated Call request without changing batch or per-item semantics.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="request"/> is <c>null</c>.</exception>
        public ValueTask<AsyncServiceOperation<CallResponse>> CreateCallAsync(
            ISession session, CallRequest request, CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            CallRequest captured = CoreUtils.Clone(request)!;
            return CreateAsync(session, DataTypeIds.CallRequest,
                (client, token) => client.CallAsync(captured.RequestHeader, captured.MethodsToCall, token), ct);
        }

        /// <summary>
        /// Captures one generated ObjectType proxy invocation and validates its typed output before acceptance/ACK.
        /// </summary>
        /// <remarks>
        /// Construct the proxy with the supplied client. Read-only preparation is allowed before the Call;
        /// another request after that Call is rejected. MethodId fallback and submission replay are disabled.
        /// </remarks>
        /// <typeparam name="TResult">The generated proxy's typed result.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="method"/> is <c>null</c>.</exception>
        public ValueTask<AsyncServiceOperation<TResult>> CreateMethodAsync<TResult>(
            ISession session,
            Func<ISessionClient, CancellationToken, ValueTask<TResult>> method,
            CancellationToken ct = default)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }
            return CreateAsync(session, DataTypeIds.CallRequest, method, ct);
        }

        /// <summary>
        /// Captures a generated method with no output arguments, such as ApplyChanges, as one ordinary Call.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="method"/> is <c>null</c>.</exception>
        public ValueTask<AsyncServiceOperation<StatusCode>> CreateMethodAsync(
            ISession session,
            Func<ISessionClient, CancellationToken, ValueTask> method,
            CancellationToken ct = default)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }
            return CreateAsync(session, DataTypeIds.CallRequest, async (client, token) =>
            {
                await method(client, token).ConfigureAwait(false);
                return StatusCodes.Good;
            }, ct);
        }

        /// <summary>
        /// Reattaches a protected checkpoint to an authenticated managed owner without ever submitting it again.
        /// Restored handles use explicit acknowledgement; previously accepted outcomes are restored locally.
        /// </summary>
        /// <typeparam name="TResponse">The exact generated original response type.</typeparam>
        /// <exception cref="InvalidOperationException"></exception>
        public async ValueTask<AsyncServiceOperation<TResponse>> ReattachAsync<TResponse>(
            ISession session, Uuid clientOperationId,
            Func<AsyncServiceResult<TResponse>, CancellationToken, ValueTask>? accept = null,
            CancellationToken ct = default) where TResponse : class, IServiceResponse
        {
            RecoverableOperationCheckpointStore store = m_options.CheckpointStore ??
                throw new InvalidOperationException("Configure protected Client recovery storage before reattachment.");
            RecoverableExecutionBinding binding = await RecoverableExecutionBinding.CaptureAsync(session, m_options, ct)
                .ConfigureAwait(false);
            AsyncServiceOperation<TResponse>? operation = null;
            try
            {
                (RecoverableOperationCheckpoint checkpoint, ByteString version) =
                    await store.LoadAsync(clientOperationId, binding.Session.MessageContext, ct).ConfigureAwait(false);
                ValidateRestoredAcceptance(checkpoint, accept != null, methodProjection: false);
                NodeId serviceId = RecoverableServiceRequests.GetRequestType<TResponse>(checkpoint.Request);
                operation = new AsyncServiceOperation<TResponse>(binding, serviceId,
                    (client, token) => RecoverableServiceRequests.InvokeAsync<TResponse>(
                        client, checkpoint.Request, token),
                    clientOperationId: clientOperationId);
                Configure(operation);
                if (accept != null)
                {
                    operation.WithAcceptancePolicy(accept);
                }
                await operation.RestoreAsync(checkpoint, version, ct).ConfigureAwait(false);
                return operation;
            }
            catch
            {
                if (operation != null)
                {
                    await operation.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await binding.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }

        /// <summary>
        /// Reattaches a generated typed Method projection. Its captured original Call must match the saved Call.
        /// No new original request or compatibility fallback may be sent.
        /// </summary>
        /// <typeparam name="TResult">The generated Method's typed result.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="method"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public async ValueTask<AsyncServiceOperation<TResult>> ReattachMethodAsync<TResult>(
            ISession session, Uuid clientOperationId,
            Func<ISessionClient, CancellationToken, ValueTask<TResult>> method,
            Func<AsyncServiceResult<TResult>, CancellationToken, ValueTask>? accept = null,
            CancellationToken ct = default)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }
            RecoverableOperationCheckpointStore store = m_options.CheckpointStore ??
                throw new InvalidOperationException("Configure protected Client recovery storage before reattachment.");
            RecoverableExecutionBinding binding = await RecoverableExecutionBinding.CaptureAsync(session, m_options, ct)
                .ConfigureAwait(false);
            AsyncServiceOperation<TResult>? operation = null;
            try
            {
                (RecoverableOperationCheckpoint checkpoint, ByteString version) =
                    await store.LoadAsync(clientOperationId, binding.Session.MessageContext, ct).ConfigureAwait(false);
                ValidateRestoredAcceptance(checkpoint, accept != null, methodProjection: true);
                if (checkpoint.Request is not CallRequest || checkpoint.ServiceId != DataTypeIds.CallRequest)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch);
                }
                operation = new AsyncServiceOperation<TResult>(
                    binding, DataTypeIds.CallRequest, method, clientOperationId: clientOperationId);
                Configure(operation);
                if (accept != null)
                {
                    operation.WithAcceptancePolicy(accept);
                }
                await operation.RestoreAsync(checkpoint, version, ct).ConfigureAwait(false);
                return operation;
            }
            catch
            {
                if (operation != null)
                {
                    await operation.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await binding.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }

        /// <summary>
        /// Captures the shared managed lifecycle seam and transfers only local context ownership to the handle.
        /// </summary>
        /// <typeparam name="TResult">The generated response or method result.</typeparam>
        private async ValueTask<AsyncServiceOperation<TResult>> CreateAsync<TResult>(
            ISession session, NodeId requestType,
            Func<ISessionClient, CancellationToken, ValueTask<TResult>> invoke, CancellationToken ct)
        {
            RecoverableExecutionBinding context = await RecoverableExecutionBinding.CaptureAsync(session, m_options, ct)
                .ConfigureAwait(false);
            AsyncServiceOperation<TResult>? operation = null;
            try
            {
                operation = new AsyncServiceOperation<TResult>(context, requestType, invoke);
                Configure(operation);
                return operation;
            }
            catch
            {
                if (operation != null)
                {
                    await operation.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    await context.DisposeAsync().ConfigureAwait(false);
                }
                throw;
            }
        }

        /// <summary>
        /// Applies the explicitly supplied application policy to the final typed handle result.
        /// </summary>
        /// <typeparam name="TResult">The generated response or Method projection.</typeparam>
        private void Configure<TResult>(AsyncServiceOperation<TResult> operation)
        {
            if (m_options.AcceptAsync is { } accept)
            {
                operation.WithAcceptancePolicy(async (outcome, token) =>
                {
                    RecoverableAcceptanceContext context;
                    try
                    {
                        context = RecoverableAcceptanceContext.Create(outcome.GetValueOrThrow(), null);
                    }
                    catch (ServiceResultException failure)
                    {
                        context = RecoverableAcceptanceContext.ForFailure(failure.Result, null);
                    }
                    await accept(context, token).ConfigureAwait(false);
                });
            }
        }

        /// <summary>
        /// Prevents restart from replacing unfinished typed or application acceptance with decoding alone.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        private void ValidateRestoredAcceptance(
            RecoverableOperationCheckpoint checkpoint, bool applicationAcceptance, bool methodProjection)
        {
            if (checkpoint.Acceptance >= RecoverableCheckpointAcceptance.Accepted)
            {
                return;
            }
            if (checkpoint.RequiresMethodProjection && !methodProjection)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "This checkpoint requires reattachment through its original typed Method projection.");
            }
            if (checkpoint.RequiresApplicationAcceptance && !applicationAcceptance && m_options.AcceptAsync == null)
            {
                throw new InvalidOperationException(
                    "Supply the original application or enclosing-operation acceptance policy before reattachment.");
            }
        }

        private readonly RecoverableSessionOptions m_options;
    }
}
