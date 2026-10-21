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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Runs normal caller operations through the existing bounded recovery engine and a caller-level receipt.
    /// </summary>
    internal sealed class RecoverableSessionExecution
    {
        /// <summary>
        /// Captures immutable settings for one owning managed session.
        /// </summary>
        internal RecoverableSessionExecution(
            ManagedSession owner, RecoverableSessionOptions options, ITelemetryContext telemetry)
        {
            m_owner = owner;
            m_options = options with { };
            m_logger = telemetry.CreateLogger<RecoverableSessionExecution>();
        }

        /// <summary>
        /// Gets whether this flow is executing a caller operation.
        /// </summary>
        internal bool HasOperationContext => m_operation.Value != null;

        /// <summary>
        /// Prevents MethodId fallback after a recoverable Call has produced an outcome.
        /// </summary>
        internal bool AllowMethodIdFallback => m_operation.Value?.HasRecoverableCall != true;

        /// <summary>
        /// Gets whether this flow is performing internal session maintenance.
        /// </summary>
        internal bool IsSuppressed => m_suppression.Value != 0;

        /// <summary>
        /// Records a specific validated original failure without conflating equal local error codes.
        /// </summary>
        internal void ObserveOriginalFailure(ServiceResultException failure)
        {
            m_operation.Value?.RecordOriginalFailure(failure);
        }

        /// <summary>
        /// Keeps internal maintenance outside application recovery and acceptance.
        /// </summary>
        internal IDisposable Suppress()
        {
            int previous = m_suppression.Value;
            m_suppression.Value = previous + 1;
            return new OrdinaryDispatchScope(this, previous);
        }

        /// <summary>
        /// Rejects invalid explicit budgets before connecting.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static void ValidateOptions(RecoverableSessionOptions? options)
        {
            if (options == null)
            {
                return;
            }
            if (options.RecoveryMode is
                < ServiceOperationRecoveryMode.Session or > ServiceOperationRecoveryMode.Durable)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Select an explicit recovery mode.");
            }
            if ((options.RecoveryMode != ServiceOperationRecoveryMode.Session || options.CheckpointStore != null) &&
                options.IdentityProvider == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Recovery requires an explicit authenticated identity provider.");
            }
            if (options.RecoveryMode == ServiceOperationRecoveryMode.Durable && options.CheckpointStore == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported,
                    "Durable Client recovery requires protected durable checkpoint storage.");
            }
            if (options.ExecutionAllowance is { } execution)
            {
                ValidateBudget(execution);
            }
            if (options.ResultRetentionTime is { } retention)
            {
                ValidateBudget(retention);
            }
        }

        /// <summary>
        /// Enters an operation-local receipt scope in the caller's execution context.
        /// </summary>
        internal RecoverableAcknowledgementScope BeginScope(
            bool requireReceipt, Func<RecoverableAcceptanceContext, CancellationToken, ValueTask>? accept)
        {
            ThrowIfAccepting();
            ScopeFrame? parent = m_scope.Value;
            var scope = new RecoverableAcknowledgementScope(requireReceipt, accept, current =>
            {
                if (!ReferenceEquals(m_scope.Value?.Scope, current))
                {
                    throw new InvalidOperationException("Acknowledgement scopes must be disposed in nesting order.");
                }
                m_scope.Value = parent;
            });
            m_scope.Value = new ScopeFrame(scope, parent);
            return scope;
        }

        /// <summary>
        /// Refuses excluded application dispatch when a real receipt is required.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal void ValidateOrdinaryDispatch()
        {
            if (IsSuppressed)
            {
                return;
            }
            ThrowIfAccepting();
            if (m_scope.Value?.Scope.RequireRecoverableReceipt == true)
            {
                throw new ServiceResultException(
                    StatusCodes.BadServiceUnsupported, "This Service cannot provide the required recoverable receipt.");
            }
            if (m_operation.Value is { } group)
            {
                group.MarkOrdinary();
            }
            else if (m_scope.Value?.Scope is { } scope)
            {
                scope.Claim(m_owner.SessionId).MarkOrdinary();
            }
        }

        /// <summary>
        /// Returns the actual caller completion task before starting any acknowledgement.
        /// </summary>
        /// <typeparam name="TResult">The final typed caller result.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <c>null</c>.</exception>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "RunAsync owns automatic groups; an explicit scope owns deferred groups. " +
                "TODO: teach disposal analysis this asynchronous handoff.")]
        internal ValueTask<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> operation, CancellationToken ct)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            ThrowIfAccepting();
            if (m_operation.Value is { } current &&
                ReferenceEquals(current.Scope, m_scope.Value?.Scope))
            {
                return operation(ct);
            }
            ct.ThrowIfCancellationRequested();
            RecoverableAcknowledgementScope? scope = m_scope.Value?.Scope;
            RecoverableOperationGroup group = scope?.Claim(m_owner.SessionId) ??
                new RecoverableOperationGroup(m_owner.SessionId, false, null);
            var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = RunAsync(group, scope, operation, completion, ct);
            return new ValueTask<TResult>(completion.Task);
        }

        /// <summary>
        /// Captures the complete original Read before any discovery await.
        /// </summary>
        internal ValueTask<ReadResponse> ReadAsync(ReadRequest request, CancellationToken ct)
        {
            ReadRequest snapshot = CoreUtils.Clone(request)!;
            return ExecuteAsync(token => DispatchAsync(
                DataTypeIds.ReadRequest,
                (client, cancellation) => client.ReadAsync(snapshot.RequestHeader, snapshot.MaxAge,
                    snapshot.TimestampsToReturn, snapshot.NodesToRead, cancellation),
                token), ct);
        }

        /// <summary>
        /// Captures the complete original Write without splitting its batch.
        /// </summary>
        internal ValueTask<WriteResponse> WriteAsync(WriteRequest request, CancellationToken ct)
        {
            WriteRequest snapshot = CoreUtils.Clone(request)!;
            return ExecuteAsync(token => DispatchAsync(
                DataTypeIds.WriteRequest,
                (client, cancellation) => client.WriteAsync(
                    snapshot.RequestHeader, snapshot.NodesToWrite, cancellation),
                token), ct);
        }

        /// <summary>
        /// Captures the complete original Call without changing Method or batch semantics.
        /// </summary>
        internal ValueTask<CallResponse> CallAsync(CallRequest request, CancellationToken ct)
        {
            CallRequest snapshot = CoreUtils.Clone(request)!;
            return ExecuteAsync(token => DispatchAsync(
                DataTypeIds.CallRequest,
                (client, cancellation) => client.CallAsync(
                    snapshot.RequestHeader, snapshot.MethodsToCall, cancellation),
                token), ct);
        }

        /// <summary>
        /// Captures any other eligible original Service through the same caller-level acceptance engine.
        /// </summary>
        /// <typeparam name="TResponse">The exact generated original response type.</typeparam>
        internal ValueTask<TResponse> RequestAsync<TResponse>(IServiceRequest request, CancellationToken ct)
            where TResponse : class, IServiceResponse
        {
            NodeId serviceId = RecoverableServiceRequests.GetRequestType<TResponse>(request);
            IServiceRequest snapshot = CoreUtils.Clone(request)!;
            return ExecuteAsync(token => DispatchAsync(serviceId,
                (client, cancellation) => RecoverableServiceRequests.InvokeAsync<TResponse>(
                    client, snapshot, cancellation), token), ct);
        }

        /// <summary>
        /// Completes typed validation and local acceptance before confirming receipt.
        /// </summary>
        /// <typeparam name="TResult">The final typed caller result.</typeparam>
        private async Task RunAsync<TResult>(
            RecoverableOperationGroup group,
            RecoverableAcknowledgementScope? scope,
            Func<CancellationToken, ValueTask<TResult>> operation,
            TaskCompletionSource<TResult> completion,
            CancellationToken ct)
        {
            RecoverableOperationGroup? previous = m_operation.Value;
            m_operation.Value = group;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, group.Token);
            bool accepted = false;
            bool accepting = false;
            Func<RecoverableAcceptanceContext, CancellationToken, ValueTask>? accept =
                scope?.AcceptAsync ?? m_options.AcceptAsync;
            try
            {
                TResult result = await operation(linked.Token).ConfigureAwait(false);
                accepting = true;
                if (accept != null)
                {
                    await AcceptAsync(group, accept,
                        RecoverableAcceptanceContext.Create(result, group.Receipt), linked.Token).ConfigureAwait(false);
                }
                await group.CommitAcceptanceAsync(linked.Token).ConfigureAwait(false);
                accepted = group.TryAccept(() => completion.TrySetResult(result));
                if (!accepted)
                {
                    completion.TrySetCanceled(group.Token);
                }
            }
            catch (ServiceResultException error) when (!accepting && group.IsOriginalFailure(error))
            {
                // A failed original outcome can be received and accepted; it remains a failed caller operation.
                try
                {
                    if (accept != null)
                    {
                        await AcceptAsync(group, accept,
                            RecoverableAcceptanceContext.ForFailure(error.Result, group.Receipt), linked.Token)
                            .ConfigureAwait(false);
                    }
                    await group.CommitAcceptanceAsync(linked.Token).ConfigureAwait(false);
                    accepted = group.TryAccept(() => completion.TrySetException(error));
                    if (!accepted)
                    {
                        completion.TrySetCanceled(group.Token);
                    }
                }
                catch (OperationCanceledException cancelled)
                {
                    completion.TrySetCanceled(cancelled.CancellationToken);
                }
                catch (Exception acceptanceError)
                {
                    completion.TrySetException(acceptanceError);
                }
            }
            catch (OperationCanceledException error)
            {
                completion.TrySetCanceled(error.CancellationToken);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
            finally
            {
                if (scope == null)
                {
                    try
                    {
                        if (accepted && group.Receipt != null)
                        {
                            await group.AcknowledgeAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                    catch (Exception error)
                    {
                        StatusCode status = error switch
                        {
                            ServiceResultException service => service.StatusCode,
                            OperationCanceledException => StatusCodes.BadRequestCancelledByClient,
                            _ => StatusCodes.BadUnexpectedError
                        };
                        m_logger.AcknowledgementUnconfirmed(status, group.SessionId);
                    }
                    finally
                    {
                        try
                        {
                            await group.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception error)
                        {
                            m_logger.OperationCleanupFailed(error, group.SessionId);
                        }
                    }
                }
                m_operation.Value = previous;
            }
        }

        /// <summary>
        /// Chooses one ordinary dispatch or the existing recoverable operation engine.
        /// </summary>
        /// <typeparam name="TResponse">The unchanged original Service response.</typeparam>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "The caller group owns registered handles until receipt disposal; failed registration " +
                "disposes locally. TODO: teach disposal analysis the group registration transfer.")]
        private async ValueTask<TResponse> DispatchAsync<TResponse>(
            NodeId serviceId,
            Func<ISessionClient, CancellationToken, ValueTask<TResponse>> invoke,
            CancellationToken ct) where TResponse : class, IServiceResponse
        {
            RecoverableOperationGroup group = m_operation.Value ??
                throw new InvalidOperationException("No caller completion context exists.");
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, group.Token);
            ct = linked.Token;
            RecoverableCapabilities capabilities = await group.GetCapabilitiesAsync(() => DiscoverAsync(group, ct))
                .ConfigureAwait(false);
            RecoverableExecutionBinding binding = group.Binding ??
                throw new InvalidOperationException("No recoverable ownership binding exists.");
            if (binding.Invalidated.IsCompleted)
            {
                throw new ServiceResultException(StatusCodes.BadSessionIdInvalid);
            }
            if (group.UsedOrdinary ||
                !capabilities.Available ||
                !capabilities.SupportedServices.Contains(serviceId) ||
                !capabilities.SupportedRecoveryModes.Contains((int)m_options.RecoveryMode))
            {
                if (m_options.RecoveryMode != ServiceOperationRecoveryMode.Session)
                {
                    throw new ServiceResultException(StatusCodes.BadServiceUnsupported,
                        "The explicitly selected recovery mode and Service must be supported without downgrade.");
                }
                group.MarkOrdinary();
                return await invoke(binding.Session, ct).ConfigureAwait(false);
            }
            double execution = m_options.ExecutionAllowance ?? capabilities.ExecutionAllowance;
            double retention = m_options.ResultRetentionTime ?? capabilities.ResultRetentionTime;
            ValidateBudget(execution);
            ValidateBudget(retention);
            var owned = new OwnedOperation<TResponse>(binding, serviceId, invoke);
            try
            {
                group.Add(owned);
            }
            catch
            {
                await owned.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            AsyncServiceOperation<TResponse> operation = owned.Operation;
            group.RecordOutcome(serviceId == DataTypeIds.CallRequest);
            AsyncServiceResult<TResponse> outcome = await operation.ExecuteAsync(execution, retention, ct: ct)
                .ConfigureAwait(false);
            try
            {
                return outcome.GetValueOrThrow();
            }
            catch (ServiceResultException error)
            {
                group.RecordOriginalFailure(error);
                throw;
            }
        }

        /// <summary>
        /// Reads capability metadata through the bound ordinary path, without recursive recovery.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        private async Task<RecoverableCapabilities> DiscoverAsync(RecoverableOperationGroup group, CancellationToken ct)
        {
            RecoverableExecutionBinding binding = await RecoverableExecutionBinding.CaptureAsync(m_owner, m_options, ct)
                .ConfigureAwait(false);
            if (!group.TryAttachBinding(binding))
            {
                await binding.DisposeAsync().ConfigureAwait(false);
                throw new ObjectDisposedException(nameof(RecoverableAcknowledgementScope));
            }
            return await binding.DiscoverAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Prevents acceptance callbacks from recursively issuing another same-session operation.
        /// </summary>
        private static async ValueTask AcceptAsync(
            RecoverableOperationGroup group,
            Func<RecoverableAcceptanceContext, CancellationToken, ValueTask> accept,
            RecoverableAcceptanceContext context, CancellationToken ct)
        {
            group.AcceptanceInProgress = true;
            try
            {
                await accept(context, ct).ConfigureAwait(false);
            }
            finally
            {
                group.AcceptanceInProgress = false;
            }
        }

        /// <summary>
        /// Rejects callback reentry before any original dispatch.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void ThrowIfAccepting()
        {
            if (m_operation.Value?.AcceptanceInProgress == true)
            {
                throw new InvalidOperationException(
                    "An acceptance callback cannot start another operation on the same session.");
            }
        }

        /// <summary>
        /// Applies the positive finite millisecond contract.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void ValidateBudget(double budget)
        {
            if (double.IsNaN(budget) || double.IsInfinity(budget) || budget <= 0)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                    "Recovery budgets must be positive finite milliseconds.");
            }
        }

        /// <summary>
        /// Keeps a typed existing protocol handle owned by its caller operation.
        /// </summary>
        /// <typeparam name="TResponse">The original Service response type.</typeparam>
        private sealed class OwnedOperation<TResponse> : IRecoverableOperation
        {
            /// <summary>
            /// Creates a handle that borrows the group's same-Session binding.
            /// </summary>
            public OwnedOperation(RecoverableExecutionBinding binding, NodeId serviceId,
                Func<ISessionClient, CancellationToken, ValueTask<TResponse>> invoke)
            {
                Operation = new AsyncServiceOperation<TResponse>(binding, serviceId, invoke, ownsBinding: false);
                Operation.RequireExplicitAcknowledgement();
                Operation.DeferAcceptanceCheckpoint = true;
            }

            /// <summary>
            /// Gets the existing protocol handle.
            /// </summary>
            public AsyncServiceOperation<TResponse> Operation { get; }

            /// <inheritdoc/>
            public bool HasOutcome => Operation.Completion.IsCompletedSuccessfully;

            /// <inheritdoc/>
            public bool Acknowledged { get; private set; }

            /// <inheritdoc/>
            public ValueTask CommitAcceptanceAsync(CancellationToken ct)
            {
                return Operation.CommitAcceptanceAsync(ct);
            }

            /// <inheritdoc/>
            public async ValueTask AcknowledgeAsync(CancellationToken ct)
            {
                ControlResponseDataType response = await Operation.AcknowledgeAsync(ct).ConfigureAwait(false);
                if (response.Result is not (ServiceOperationControlResult.Applied or
                    ServiceOperationControlResult.AlreadyApplied))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNoData, "The Server did not confirm acknowledgement.");
                }
                Acknowledged = true;
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return Operation.DisposeAsync();
            }
        }

        /// <summary>
        /// Isolates nested receipt scopes in each asynchronous flow.
        /// </summary>
        private sealed record ScopeFrame(RecoverableAcknowledgementScope Scope, ScopeFrame? Parent);

        /// <summary>
        /// Restores only the current flow's maintenance suppression.
        /// </summary>
        private sealed class OrdinaryDispatchScope(RecoverableSessionExecution owner, int previous) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                owner.m_suppression.Value = previous;
            }
        }

        private readonly ManagedSession m_owner;
        private readonly RecoverableSessionOptions m_options;
        private readonly ILogger m_logger;
        private readonly AsyncLocal<RecoverableOperationGroup?> m_operation = new();
        private readonly AsyncLocal<ScopeFrame?> m_scope = new();
        private readonly AsyncLocal<int> m_suppression = new();
    }

    /// <summary>
    /// The capability values observed for one bound logical operation, never a renewable lease.
    /// </summary>
    internal sealed record RecoverableCapabilities(
        bool Available, ArrayOf<NodeId> SupportedServices, ArrayOf<int> SupportedRecoveryModes,
        double ExecutionAllowance, double ResultRetentionTime, RecoverableManagerClient? Manager) : IDisposable
    {
        /// <summary>
        /// Gets a conclusive pre-submission absence, never an interpretation of a failed Submit exchange.
        /// </summary>
        internal static RecoverableCapabilities Unsupported { get; } = new(false, default, default, 0, 0, null);

        /// <inheritdoc/>
        public void Dispose()
        {
            Manager?.Dispose();
        }
    }

    /// <summary>
    /// Reports receipt failures without replacing accepted caller results or logging operation tokens.
    /// </summary>
    internal static partial class RecoverableSessionExecutionLog
    {
        [LoggerMessage(EventId = ClientEventIds.RecoverableSessionExecution, Level = LogLevel.Warning,
            Message = "Caller result accepted for Session {SessionId}; " +
                "recoverable acknowledgement is unconfirmed: {Status}.")]
        internal static partial void AcknowledgementUnconfirmed(
            this ILogger logger, StatusCode status, NodeId sessionId);

        [LoggerMessage(EventId = ClientEventIds.RecoverableSessionExecution + 1, Level = LogLevel.Warning,
            Message = "Local recoverable operation cleanup failed for Session {SessionId}.")]
        internal static partial void OperationCleanupFailed(this ILogger logger, Exception error, NodeId sessionId);
    }
}
