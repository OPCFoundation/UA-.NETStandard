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

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// Runs scale mutations while excluding synchronous writes from asynchronous notifications.
    /// </summary>
    internal sealed class ScaleWriteGate
    {
        /// <summary>
        /// Identifies a notification flow that may reenter this gate for a nested asynchronous write.
        /// </summary>
        private sealed class NotificationScope
        {
            /// <summary>
            /// Creates a notification scope for this gate.
            /// </summary>
            /// <param name="gate">The gate that owns the notification scope.</param>
            internal NotificationScope(ScaleWriteGate gate)
            {
                Gate = gate;
            }

            /// <summary>
            /// The gate that owns the notification scope.
            /// </summary>
            internal ScaleWriteGate Gate { get; }

            /// <summary>
            /// Gets whether the notification callback is still active.
            /// </summary>
            internal bool IsActive => Volatile.Read(ref m_isActive) != 0;

            /// <summary>
            /// Marks the notification callback as complete.
            /// </summary>
            internal void Deactivate()
            {
                Volatile.Write(ref m_isActive, 0);
            }

            /// <summary>
            /// The active state of the notification callback.
            /// </summary>
            private int m_isActive = 1;
        }

        /// <summary>
        /// Tracks a correlated publication without retaining rejection in completed captured contexts.
        /// </summary>
        private sealed class CorrelatedPublicationScope
        {
            /// <summary>
            /// Gets whether the publication is still active.
            /// </summary>
            internal bool IsActive => Volatile.Read(ref m_active) != 0;

            /// <summary>
            /// Releases the restriction in every context that captured the publication.
            /// </summary>
            internal void Deactivate()
            {
                Volatile.Write(ref m_active, 0);
            }

            /// <summary>The shared publication activity marker.</summary>
            private int m_active = 1;
        }

        /// <summary>
        /// Creates the admission coordinator and its node-bound write executor.
        /// </summary>
        internal ScaleWriteGate()
        {
            NodeWriteExecutor = new NodeWriteOperationExecutor(
                operation => TryExecuteSynchronously(operation, out ServiceResult result)
                    ? result
                    : ServiceResult.Create(
                        StatusCodes.BadInvalidState,
                        "A scale write is awaiting an asynchronous state notification."),
                (operation, cancellationToken) =>
                    ExecuteAsynchronouslyAsync(operation, cancellationToken),
                (operation, cancellationToken) =>
                    ExecuteNotificationsAsync(operation, cancellationToken));
        }

        /// <summary>
        /// Gets the executor that runs a node's complete Value-write operation.
        /// </summary>
        internal NodeWriteOperationExecutor NodeWriteExecutor { get; }

        /// <summary>
        /// Gets whether the current flow is publishing a correlated statistic reset.
        /// </summary>
        internal bool IsCorrelatedPublicationActive => m_correlatedPublication.Value?.IsActive == true;

        /// <summary>
        /// Publishes a correlated update while rejecting writes nested in its notification flow.
        /// </summary>
        /// <param name="operation">The publication already running under asynchronous admission.</param>
        internal async ValueTask ExecuteCorrelatedPublicationAsync(Func<ValueTask> operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            CorrelatedPublicationScope? previous = m_correlatedPublication.Value;
            var scope = new CorrelatedPublicationScope();
            m_correlatedPublication.Value = scope;
            try
            {
                await operation().ConfigureAwait(false);
            }
            finally
            {
                scope.Deactivate();
                m_correlatedPublication.Value = previous;
            }
        }

        /// <summary>
        /// Rejects a nested service write during correlated publication without waiting for its own caller.
        /// </summary>
        /// <param name="operation">The complete service write.</param>
        /// <param name="cancellationToken">Cancels before admission.</param>
        internal ValueTask<ServiceResult> ExecuteAsynchronouslyAsync(
            Func<CancellationToken, ValueTask<ServiceResult>> operation,
            CancellationToken cancellationToken)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return IsCorrelatedPublicationActive
                ? new ValueTask<ServiceResult>(CorrelatedPublicationRejected())
                : ExecuteAsynchronouslyAsync<ServiceResult>(operation, cancellationToken);
        }

        /// <summary>
        /// Runs synchronous work unless an asynchronous operation owns the writer.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="operation">The complete synchronous operation.</param>
        /// <param name="result">The operation result, or the default value when rejected.</param>
        /// <returns>Whether the operation ran.</returns>
        internal bool TryExecuteSynchronously<T>(Func<T> operation, out T result)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            if (!TryAdmitSynchronous())
            {
                result = default!;
                return false;
            }

            try
            {
                result = operation();
                return true;
            }
            finally
            {
                ReleaseSynchronous();
            }
        }

        /// <summary>
        /// Runs synchronous work unless an asynchronous operation owns the writer.
        /// </summary>
        /// <param name="operation">The complete synchronous operation.</param>
        /// <returns>Whether the operation ran.</returns>
        internal bool TryExecuteSynchronously(Action operation)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            return TryExecuteSynchronously(
                () =>
                {
                    operation();
                    return true;
                },
                out _);
        }

        /// <summary>
        /// Transfers a completed synchronous mutation to inline notification admission without an unprotected interval.
        /// </summary>
        /// <typeparam name="T">The mutation result type.</typeparam>
        /// <param name="operation">The complete mutation.</param>
        /// <param name="notifications">The inline notification delivery.</param>
        /// <param name="result">The mutation result, or the default value when rejected.</param>
        /// <returns>Whether the mutation and notification delivery ran.</returns>
        internal bool TryExecuteWithNotifications<T>(Func<T> operation, Action notifications, out T result)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            if (notifications == null)
            {
                throw new ArgumentNullException(nameof(notifications));
            }
            if (!TryAdmitSynchronous())
            {
                result = default!;
                return false;
            }

            bool notifying = false;
            try
            {
                result = operation();
                TransferSynchronousToNotifications();
                notifying = true;
                NotificationScope? previousScope = m_notificationScope.Value;
                var scope = new NotificationScope(this);
                m_notificationScope.Value = scope;
                try
                {
                    notifications();
                }
                finally
                {
                    scope.Deactivate();
                    m_notificationScope.Value = previousScope;
                }
                return true;
            }
            finally
            {
                if (notifying)
                {
                    ExitNotifications();
                }
                else
                {
                    ReleaseSynchronous();
                }
            }
        }

        /// <summary>
        /// Runs synchronous work and throws when an asynchronous operation owns the writer.
        /// </summary>
        /// <param name="operation">The complete synchronous operation.</param>
        internal void ExecuteSynchronously(Action operation)
        {
            if (!TryExecuteSynchronously(operation))
            {
                throw WriteInProgress();
            }
        }

        /// <summary>
        /// Runs synchronous work and throws when an asynchronous operation owns the writer.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="operation">The complete synchronous operation.</param>
        /// <returns>The operation result.</returns>
        internal T ExecuteSynchronously<T>(Func<T> operation)
        {
            if (!TryExecuteSynchronously(operation, out T result))
            {
                throw WriteInProgress();
            }
            return result;
        }

        /// <summary>
        /// Runs an operation after prior synchronous work completes and holds asynchronous ownership
        /// until the operation returns.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="operation">The complete asynchronous operation.</param>
        /// <param name="cancellationToken">Cancels while waiting to enter.</param>
        /// <returns>The operation result.</returns>
        internal async ValueTask<T> ExecuteAsynchronouslyAsync<T>(
            Func<CancellationToken, ValueTask<T>> operation,
            CancellationToken cancellationToken)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (IsCorrelatedPublicationActive)
            {
                throw new ServiceResultException(CorrelatedPublicationRejected());
            }
            await EnterAsynchronousAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ReleaseAsynchronous();
            }
        }

        /// <summary>
        /// Commits a report before atomically transferring writer ownership to its notification flow.
        /// </summary>
        /// <param name="operation">The cache mutation, without report callbacks.</param>
        /// <param name="notifications">The notifications collected by the mutation.</param>
        /// <param name="cancellationToken">Cancels before mutation admission.</param>
        internal async ValueTask<ServiceResult> ExecuteWithNotificationsAsync(
            Func<CancellationToken, ValueTask<ServiceResult>> operation,
            Func<ValueTask> notifications,
            CancellationToken cancellationToken)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            if (notifications == null)
            {
                throw new ArgumentNullException(nameof(notifications));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (IsCorrelatedPublicationActive)
            {
                return CorrelatedPublicationRejected();
            }
            await EnterAsynchronousAsync(cancellationToken).ConfigureAwait(false);
            bool notifying = false;
            try
            {
                ServiceResult result = await operation(cancellationToken).ConfigureAwait(false);
                TaskCompletionSource<bool>? stateChanged;
                lock (m_stateLock)
                {
                    m_notificationsInProgress++;
                    m_asyncOwner = false;
                    stateChanged = m_stateChanged;
                    m_stateChanged = null;
                }
                notifying = true;
                stateChanged?.TrySetResult(true);
                NotificationScope? previous = m_notificationScope.Value;
                var scope = new NotificationScope(this);
                m_notificationScope.Value = scope;
                try
                {
                    await notifications().ConfigureAwait(false);
                }
                finally
                {
                    scope.Deactivate();
                    m_notificationScope.Value = previous;
                }
                return result;
            }
            finally
            {
                if (notifying)
                {
                    ExitNotifications();
                }
                else
                {
                    ReleaseAsynchronous();
                }
            }
        }

        /// <summary>
        /// Runs asynchronous state-change notifications while rejecting synchronous writes.
        /// </summary>
        /// <remarks>
        /// Nested asynchronous writes from the notification flow may reenter this gate. Other asynchronous writes
        /// wait until the notification callback completes.
        /// </remarks>
        /// <param name="operation">The notification operation.</param>
        /// <param name="cancellationToken">Cancels while waiting for active synchronous writers.</param>
        internal async ValueTask ExecuteNotificationsAsync(
            Func<CancellationToken, ValueTask> operation,
            CancellationToken cancellationToken)
        {
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (IsCorrelatedPublicationActive)
            {
                throw new ServiceResultException(CorrelatedPublicationRejected());
            }

            await EnterNotificationsAsync(cancellationToken).ConfigureAwait(false);
            NotificationScope? previousScope = m_notificationScope.Value;
            var scope = new NotificationScope(this);
            m_notificationScope.Value = scope;
            try
            {
                await operation(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                scope.Deactivate();
                m_notificationScope.Value = previousScope;
                ExitNotifications();
            }
        }

        /// <summary>
        /// Runs synchronous work while admitting every distinct writer or rejects before work begins.
        /// </summary>
        /// <typeparam name="T">The result type.</typeparam>
        /// <param name="gates">The writers that must admit the operation.</param>
        /// <param name="operation">The complete synchronous operation.</param>
        /// <param name="result">The operation result, or the default value when rejected.</param>
        /// <returns>Whether the operation ran.</returns>
        internal static bool TryExecuteSynchronously<T>(
            IEnumerable<ScaleWriteGate> gates,
            Func<T> operation,
            out T result)
        {
            if (gates == null)
            {
                throw new ArgumentNullException(nameof(gates));
            }
            if (operation == null)
            {
                throw new ArgumentNullException(nameof(operation));
            }

            var admitted = new List<ScaleWriteGate>();
            foreach (ScaleWriteGate gate in gates)
            {
                if (admitted.Contains(gate))
                {
                    continue;
                }
                if (!gate.TryAdmitSynchronous())
                {
                    for (int ii = admitted.Count - 1; ii >= 0; ii--)
                    {
                        admitted[ii].ReleaseSynchronous();
                    }
                    result = default!;
                    return false;
                }
                admitted.Add(gate);
            }

            try
            {
                result = operation();
                return true;
            }
            finally
            {
                for (int ii = admitted.Count - 1; ii >= 0; ii--)
                {
                    admitted[ii].ReleaseSynchronous();
                }
            }
        }

        /// <summary>
        /// Runs synchronous work for every distinct writer and throws if an asynchronous operation owns one.
        /// </summary>
        /// <param name="gates">The writers that must admit the operation.</param>
        /// <param name="operation">The complete synchronous operation.</param>
        internal static void ExecuteSynchronously(IEnumerable<ScaleWriteGate> gates, Action operation)
        {
            if (!TryExecuteSynchronously(
                gates,
                () =>
                {
                    operation();
                    return true;
                },
                out _))
            {
                throw WriteInProgress();
            }
        }

        /// <summary>
        /// Waits for writer admission, allowing nested writes from the active notification flow.
        /// </summary>
        /// <param name="cancellationToken">Cancels while waiting to enter.</param>
        private async ValueTask EnterAsynchronousAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task wait;
                lock (m_stateLock)
                {
                    if (!m_asyncOwner &&
                        m_synchronousWriters == 0 &&
                        (m_notificationsInProgress == 0 || IsNotificationScopeActive))
                    {
                        m_asyncOwner = true;
                        return;
                    }
                    wait = (m_stateChanged ??= new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                }

                await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Reserves notification admission and waits for active writers to finish.
        /// </summary>
        /// <param name="cancellationToken">Cancels while waiting for admitted writers.</param>
        private async ValueTask EnterNotificationsAsync(CancellationToken cancellationToken)
        {
            bool registered = false;
            try
            {
                while (true)
                {
                    Task wait;
                    lock (m_stateLock)
                    {
                        if (!registered)
                        {
                            m_notificationsInProgress++;
                            registered = true;
                        }
                        if (m_synchronousWriters == 0 && !m_asyncOwner)
                        {
                            return;
                        }
                        wait = (m_stateChanged ??= new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously)).Task;
                    }

                    await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                if (registered)
                {
                    ExitNotifications();
                }
                throw;
            }
        }

        /// <summary>
        /// Reserves synchronous admission unless a write or notification owns the writer.
        /// </summary>
        private bool TryAdmitSynchronous()
        {
            lock (m_stateLock)
            {
                if (m_asyncOwner || m_notificationsInProgress != 0)
                {
                    return false;
                }
                m_synchronousWriters++;
                return true;
            }
        }

        /// <summary>
        /// Releases synchronous admission and wakes asynchronous waiters when the writer is idle.
        /// </summary>
        private void ReleaseSynchronous()
        {
            TaskCompletionSource<bool>? stateChanged = null;
            lock (m_stateLock)
            {
                m_synchronousWriters--;
                if (m_synchronousWriters == 0)
                {
                    stateChanged = m_stateChanged;
                    m_stateChanged = null;
                }
            }
            stateChanged?.TrySetResult(true);
        }

        /// <summary>
        /// Releases mutation ownership while retaining notification admission and waking nested asynchronous writers.
        /// </summary>
        private void TransferSynchronousToNotifications()
        {
            TaskCompletionSource<bool>? stateChanged = null;
            lock (m_stateLock)
            {
                m_notificationsInProgress++;
                m_synchronousWriters--;
                if (m_synchronousWriters == 0)
                {
                    stateChanged = m_stateChanged;
                    m_stateChanged = null;
                }
            }
            stateChanged?.TrySetResult(true);
        }

        /// <summary>
        /// Releases asynchronous ownership and wakes queued operations.
        /// </summary>
        private void ReleaseAsynchronous()
        {
            TaskCompletionSource<bool>? stateChanged;
            lock (m_stateLock)
            {
                m_asyncOwner = false;
                stateChanged = m_stateChanged;
                m_stateChanged = null;
            }
            stateChanged?.TrySetResult(true);
        }

        /// <summary>
        /// Releases notification admission.
        /// </summary>
        private void ExitNotifications()
        {
            TaskCompletionSource<bool>? stateChanged = null;
            lock (m_stateLock)
            {
                m_notificationsInProgress--;
                if (m_notificationsInProgress == 0 && !m_asyncOwner && m_synchronousWriters == 0)
                {
                    stateChanged = m_stateChanged;
                    m_stateChanged = null;
                }
            }
            stateChanged?.TrySetResult(true);
        }

        /// <summary>
        /// Gets whether the current flow is inside this gate's notification callback.
        /// </summary>
        private bool IsNotificationScopeActive =>
            m_notificationScope.Value is { IsActive: true } scope && ReferenceEquals(scope.Gate, this);

        /// <summary>
        /// Rejects a write that cannot run until the correlated notification invoking it returns.
        /// </summary>
        private static ServiceResult CorrelatedPublicationRejected()
        {
            return ServiceResult.Create(
                StatusCodes.BadInvalidState, "A product statistic reset is delivering notifications in this flow.");
        }

        /// <summary>
        /// Creates the explicit status used when synchronous work overlaps an asynchronous operation.
        /// </summary>
        private static ServiceResultException WriteInProgress()
        {
            return new ServiceResultException(
                StatusCodes.BadInvalidState,
                "A scale write is awaiting an asynchronous state notification.");
        }

        /// <summary>
        /// Protects writer counts and ownership.
        /// </summary>
        private readonly Lock m_stateLock = new();

        /// <summary>
        /// Carries notification reentrancy through awaited callbacks.
        /// </summary>
        private readonly AsyncLocal<NotificationScope?> m_notificationScope = new();

        /// <summary>
        /// Limits correlated-publication rejection to its own active asynchronous flow.
        /// </summary>
        private readonly AsyncLocal<CorrelatedPublicationScope?> m_correlatedPublication = new();

        /// <summary>
        /// The asynchronous waiter signal, when the writer is occupied.
        /// </summary>
        private TaskCompletionSource<bool>? m_stateChanged;

        /// <summary>
        /// The number of currently admitted synchronous operations.
        /// </summary>
        private int m_synchronousWriters;

        /// <summary>
        /// The number of active inline notifications and active or pending asynchronous notification operations.
        /// </summary>
        private int m_notificationsInProgress;

        /// <summary>
        /// Whether an asynchronous operation owns the writer.
        /// </summary>
        private bool m_asyncOwner;
    }
}
