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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.MachineTool.WMTP;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Admits one operation per peripheral and separates authorization, cancellation and callbacks from its gate.
    /// </summary>
    internal sealed class WmtpOperationCoordinator : IAsyncDisposable
    {
        /// <summary>
        /// Creates the per-peripheral operation lifetime.
        /// </summary>
        internal WmtpOperationCoordinator(IWmtpAuthorization? authorization)
        {
            m_authorization = authorization;
        }

        /// <summary>
        /// Gets whether the caller is executing this peripheral's provider or authorization callback.
        /// </summary>
        internal bool IsInCallback => ReferenceEquals(s_executing.Value, this);

        /// <summary>
        /// Executes a provider operation. A null request denotes an explicitly trusted application refresh.
        /// </summary>
        /// <typeparam name="T">The operation result type.</typeparam>
        /// <param name="request">The caller, or null for a trusted application refresh.</param>
        /// <param name="operation">The operation to authorize.</param>
        /// <param name="execute">The provider or state work.</param>
        /// <param name="cancellationToken">Cancels admission and the work.</param>
        /// <param name="maintenance">
        /// Marks short internal freshness work without equipment calls. Maintenance is refused while any
        /// operation is active, and other operations wait for running maintenance instead of being refused,
        /// so the background age timer never surfaces to a caller as BadServerTooBusy.
        /// </param>
        internal async ValueTask<T> RunAsync<T>(
            WmtpRequest? request,
            WmtpOperation operation,
            Func<CancellationToken, ValueTask<T>> execute,
            CancellationToken cancellationToken,
            bool maintenance = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ActiveOperation active;
            while (true)
            {
                Task running;
                lock (m_gate)
                {
                    EnsureActive();
                    if (m_active == null)
                    {
                        active = new ActiveOperation(
                            request?.SessionId ?? NodeId.Null, maintenance, cancellationToken);
                        m_active = active;
                        break;
                    }
                    if (maintenance || !m_active.Maintenance || IsInCallback)
                    {
                        throw new ServiceResultException(StatusCodes.BadServerTooBusy,
                            "This peripheral already has an active operation.");
                    }
                    running = m_active.Completion.Task;
                }
                await running.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            WmtpOperationCoordinator? previous = s_executing.Value;
            s_executing.Value = this;
            try
            {
                if (request != null)
                {
                    await AuthorizeAsync(request, operation, active.Cancellation.Token).ConfigureAwait(false);
                }
                active.Cancellation.Token.ThrowIfCancellationRequested();
                return await execute(active.Cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (Volatile.Read(ref active.Aborted) != 0)
            {
                throw new ServiceResultException(StatusCodes.BadRequestCancelledByClient,
                    "The peripheral operation was explicitly aborted.");
            }
            finally
            {
                s_executing.Value = previous;
                lock (m_gate)
                {
                    m_active = null;
                }
                active.Completion.TrySetResult(true);
                active.Cancellation.Dispose();
            }
        }

        /// <summary>
        /// Aborts only the operation observed before authorization, never a later caller's replacement.
        /// </summary>
        internal async ValueTask AbortAsync(WmtpRequest request, CancellationToken cancellationToken)
        {
            ActiveOperation? active;
            lock (m_gate)
            {
                EnsureActive();
                active = m_active;
                if (m_aborts++ == 0)
                {
                    m_abortsDrained =
                        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
            WmtpOperationCoordinator? previous = s_executing.Value;
            s_executing.Value = this;
            using CancellationTokenSource abort = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_shutdown.Token);
            try
            {
                await AuthorizeAsync(request with { ActiveSessionId = active?.SessionId ?? NodeId.Null },
                    WmtpOperation.Abort, abort.Token).ConfigureAwait(false);
                abort.Token.ThrowIfCancellationRequested();
                lock (m_gate)
                {
                    EnsureActive();
                    if (!ReferenceEquals(active, m_active))
                    {
                        return;
                    }
                }
                if (active != null)
                {
                    Volatile.Write(ref active.Aborted, 1);
                    Cancel(active);
                }
            }
            finally
            {
                s_executing.Value = previous;
                lock (m_gate)
                {
                    if (--m_aborts == 0)
                    {
                        m_abortsDrained!.TrySetResult(true);
                    }
                }
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (ReferenceEquals(s_executing.Value, this))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "A provider callback cannot await disposal of its own peripheral.");
            }
            ActiveOperation? active;
            Task aborts;
            lock (m_gate)
            {
                m_closed = true;
                active = m_active;
                aborts = m_abortsDrained?.Task ?? Task.CompletedTask;
            }
            var errors = new List<Exception>();
            try
            {
                m_shutdown.Cancel();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            try
            {
                Cancel(active);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            if (active != null)
            {
                await active.Completion.Task.ConfigureAwait(false);
            }
            await aborts.ConfigureAwait(false);
            m_shutdown.Dispose();
            if (errors.Count != 0)
            {
                throw new AggregateException("Peripheral cancellation callbacks failed.", errors);
            }
        }

        /// <summary>
        /// Requires explicit positive authorization before calling equipment.
        /// </summary>
        private async ValueTask AuthorizeAsync(
            WmtpRequest request, WmtpOperation operation, CancellationToken cancellationToken)
        {
            if (m_authorization == null)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            StatusCode result = await m_authorization.AuthorizeAsync(request, operation, cancellationToken)
                .ConfigureAwait(false);
            if (!StatusCode.IsGood(result))
            {
                throw new ServiceResultException(StatusCode.IsBad(result) ? result : StatusCodes.BadUserAccessDenied);
            }
        }

        /// <summary>
        /// Cancels outside the coordinator gate; a concurrently completed operation needs no cancellation.
        /// </summary>
        private static void Cancel(ActiveOperation? active)
        {
            try
            {
                active?.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Completion won the race with cancellation; no replacement operation is affected.
            }
        }

        /// <summary>
        /// Rejects admission after shutdown.
        /// </summary>
        private void EnsureActive()
        {
            if (m_closed)
            {
                throw new ObjectDisposedException(nameof(WmtpPeripheral));
            }
        }

        /// <summary>
        /// Owns cancellation and asynchronous drainage for a single accepted call.
        /// </summary>
        private sealed class ActiveOperation
        {
            /// <summary>
            /// Creates an operation with caller-local cancellation.
            /// </summary>
            internal ActiveOperation(NodeId sessionId, bool maintenance, CancellationToken cancellationToken)
            {
                SessionId = sessionId;
                Maintenance = maintenance;
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }

            /// <summary>
            /// Gets whether this is short internal freshness work that other operations wait for.
            /// </summary>
            internal bool Maintenance { get; }

            /// <summary>
            /// Gets the actual calling session, not an invented protocol identifier.
            /// </summary>
            internal NodeId SessionId { get; }

            /// <summary>
            /// Gets this operation's cancellation source.
            /// </summary>
            internal CancellationTokenSource Cancellation { get; }

            /// <summary>
            /// Gets the asynchronously continued completion signal.
            /// </summary>
            internal TaskCompletionSource<bool> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Whether an authorized explicit AbortOperation canceled this call.</summary>
            internal int Aborted;
        }

        /// <summary>The application policy.</summary>
        private readonly IWmtpAuthorization? m_authorization;

        /// <summary>The short admission gate; no callback or await occurs while held.</summary>
        private readonly Lock m_gate = new();

        /// <summary>Cancels authorization callbacks that are not the primary active operation.</summary>
        private readonly CancellationTokenSource m_shutdown = new();

        /// <summary>The provider callback reentrancy marker.</summary>
        private static readonly AsyncLocal<WmtpOperationCoordinator?> s_executing = new();

        /// <summary>The currently admitted operation, if any.</summary>
        private ActiveOperation? m_active;

        /// <summary>Whether shutdown has started.</summary>
        private bool m_closed;

        /// <summary>The admitted abort-authorization callback count.</summary>
        private int m_aborts;

        /// <summary>The shared abort-callback drainage signal.</summary>
        private TaskCompletionSource<bool>? m_abortsDrained;
    }
}
