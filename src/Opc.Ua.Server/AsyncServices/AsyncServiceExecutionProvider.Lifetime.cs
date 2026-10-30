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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    public sealed partial class AsyncServiceExecutionProvider
    {
        /// <summary>
        /// Keeps public exchanges inside the provider's asynchronous shutdown lifetime.
        /// </summary>
        private ActivityLease EnterExchange()
        {
            lock (m_gate)
            {
                ThrowIfStopping();
                return TrackActivity();
            }
        }

        /// <summary>
        /// Tracks already-owned work or maintenance before queueing it, preventing a shutdown scheduling gap.
        /// </summary>
        private ActivityLease TrackActivity()
        {
            lock (m_gate)
            {
                m_exchanges++;
                return new ActivityLease(this);
            }
        }

        /// <summary>
        /// Releases a tracked activity after its final state publication and resource cleanup.
        /// </summary>
        private void ReleaseActivity()
        {
            lock (m_gate)
            {
                m_exchanges--;
                if (m_stopping && m_exchanges == 0)
                {
                    m_exchangesDrained.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// Protects one indexed operation through an exchange, invocation or maintenance callback.
        /// </summary>
        private OperationLease RetainOperation(Operation operation)
        {
            lock (m_gate)
            {
                operation.References++;
                return new OperationLease(this, operation);
            }
        }

        /// <summary>
        /// Disposes per-operation synchronization only after its index and every active use have released it.
        /// </summary>
        private void ReleaseOperation(Operation operation)
        {
            bool last;
            bool idle;
            lock (m_gate)
            {
                operation.References--;
                last = operation.References == 0;
                idle = operation.References == (operation.Indexed ? 1 : 0);
            }
            if (idle)
            {
                Cleanup(operation);
            }
            if (last)
            {
                operation.JournalEntry?.Dispose();
                if (!operation.LifetimesDisposed)
                {
                    operation.Lifetime.Dispose();
                    operation.Cancellation.Dispose();
                    operation.LifetimesDisposed = true;
                }
            }
        }

        /// <summary>
        /// Starts exactly one independently owned invocation after its mode's admission has committed.
        /// </summary>
        private void StartAdmittedOperation(Operation operation)
        {
            Increment(ref m_admitted);
            RecordAudit(operation, ServiceOperationAuditAction.Admitted);
            ArmTimer(operation);
            if (m_stopping)
            {
                RequestCancellation(operation);
            }
            OperationLease? lease = null;
            ActivityLease? activity = null;
            try
            {
                lease = RetainOperation(operation);
                activity = TrackActivity();
                OperationLease ownedLease = lease;
                ActivityLease ownedActivity = activity;
                using (ExecutionContext.SuppressFlow())
                {
                    operation.Work = Task.Run(async () =>
                    {
                        using (ownedActivity)
                        using (ownedLease)
                        {
                            await ExecuteAsync(operation).ConfigureAwait(false);
                        }
                    }, CancellationToken.None);
                }
                lease = null;
                activity = null;
            }
            finally
            {
                lease?.Dispose();
                activity?.Dispose();
            }
        }

        /// <summary>
        /// Removes a reservation only when the journal still proves that admission did not commit.
        /// </summary>
        private void RemoveUnadmittedOperation(Operation operation)
        {
            lock (m_gate)
            {
                if (operation.Indexed)
                {
                    operation.Indexed = false;
                    m_byClient.Remove(operation.Key);
                    m_byToken.Remove(operation.Token);
                    ReleaseOperation(operation);
                }
            }
        }

        /// <summary>
        /// Drains exchanges, maintenance and actual local work without turning durable outcomes into abandonment.
        /// </summary>
        private async Task StopCoreAsync()
        {
            await ObserveCancellationAsync(m_shutdown.CancelAsync(), Uuid.Empty).ConfigureAwait(false);
            Operation[] operations;
            OperationLease[] held;
            lock (m_gate)
            {
                operations = [.. m_byToken.Values];
                held = [.. operations.Select(RetainOperation)];
                foreach (Operation operation in operations)
                {
                    operation.Timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                }
            }
            m_server.CurrentInstance.SessionManager.SessionClosing -= OnSessionClosing;
            try
            {
                foreach (Operation operation in operations)
                {
                    if (operation.Mode == ServiceOperationRecoveryMode.Durable &&
                        !operation.WorkFinished &&
                        operation.JournalEntry != null &&
                        m_journal?.IsReady == true)
                    {
                        try
                        {
                            _ = await operation.JournalEntry.ControlAsync(
                                AsyncServiceAccess.Cancel, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            WithdrawDurableReadiness(operation, exception);
                        }
                    }
                    lock (m_gate)
                    {
                        if (operation.Mode != ServiceOperationRecoveryMode.Durable)
                        {
                            Release(operation, ServiceOperationState.Abandoned, countAction: false);
                            operation.Forgotten = true;
                            RecordAudit(operation, ServiceOperationAuditAction.HistoryForgotten);
                        }
                        RequestCancellation(operation);
                    }
                }
                await m_exchangesDrained.Task.ConfigureAwait(false);
                foreach (Operation operation in operations)
                {
                    await operation.CancellationTask.ConfigureAwait(false);
                    if (operation.RecoveredWork != null)
                    {
                        try
                        {
                            await operation.RecoveredWork.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            WithdrawDurableReadiness(operation, exception);
                        }
                        operation.RecoveredWork = null;
                    }
                    bool indexed;
                    lock (m_gate)
                    {
                        if (operation.State == ServiceOperationState.OutcomeAvailable)
                        {
                            m_retainedBytes -= (ulong)operation.Reservations.HistoryCharge;
                        }
                        indexed = operation.Indexed;
                        if (indexed)
                        {
                            operation.Indexed = false;
                            m_byClient.Remove(operation.Key);
                            m_byToken.Remove(operation.Token);
                        }
                    }
                    operation.Timer?.Dispose();
                    operation.Timer = null;
                    operation.Input?.Release();
                    operation.Input = null;
                    operation.Outcome?.Release();
                    operation.Outcome = null;
                    operation.Reservations.Dispose();
                    if (indexed)
                    {
                        ReleaseOperation(operation);
                    }
                }
            }
            finally
            {
                foreach (OperationLease lease in held)
                {
                    lease.Dispose();
                }
                FlushAudits();
            }
        }

        /// <summary>
        /// Owns pre-admission resources until the operation index takes responsibility for their full lifetime.
        /// </summary>
        private sealed class AdmissionPreparation(Reservations reservations) : IAsyncDisposable
        {
            /// <summary>
            /// Gets or sets the decoded request before it is transferred to an operation.
            /// </summary>
            public IServiceRequest? Request { get; set; }

            /// <summary>
            /// Gets or sets the independent eligibility reservation before it is transferred.
            /// </summary>
            public IAsyncDisposable? Eligibility { get; set; }

            /// <summary>
            /// Gets or sets the prepared, but not necessarily admitted, operation.
            /// </summary>
            public Operation? Operation { get; set; }

            /// <summary>
            /// Gets or sets the submission's independent operation use.
            /// </summary>
            public OperationLease? Lease { get; set; }

            /// <summary>
            /// Gets or sets whether the index owns the operation, including uncertain durable admission.
            /// </summary>
            public bool Admitted { get; set; }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                try
                {
                    if (!Admitted)
                    {
                        try
                        {
                            if (Operation != null)
                            {
                                await Operation.DisposeUnadmittedAsync().ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            reservations.Dispose();
                        }
                    }
                }
                finally
                {
                    try
                    {
                        (Request as IPooledEncodeable)?.Reuse();
                        if (Eligibility != null)
                        {
                            await Eligibility.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        Lease?.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Counts one idempotently released provider activity without exposing synchronization to callers.
        /// </summary>
        private sealed class ActivityLease(AsyncServiceExecutionProvider owner) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_released, 1) == 0)
                {
                    owner.ReleaseActivity();
                }
            }

            private int m_released;
        }

        /// <summary>
        /// Counts an operation use independently of public payload retention and transport delivery.
        /// </summary>
        private sealed class OperationLease(AsyncServiceExecutionProvider owner, Operation operation) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref m_released, 1) == 0)
                {
                    owner.ReleaseOperation(operation);
                }
            }

            private int m_released;
        }

        private readonly CancellationTokenSource m_shutdown = new();

        private readonly TaskCompletionSource<bool> m_exchangesDrained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int m_exchanges;
        private Task? m_stopTask;
        private int m_disposed;
    }
}
