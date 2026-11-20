/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolNotificationController
    {
        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            if (IsInCallback)
            {
                throw Invalid("A notification callback cannot await its own controller's disposal.");
            }
            TaskCompletionSource<bool> completion;
            lock (m_lifetimeGate)
            {
                if (m_disposal != null)
                {
                    return new ValueTask(m_disposal);
                }
                m_stopped = true;
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                m_disposal = completion.Task;
                if (m_operations == 0)
                {
                    m_drained.TrySetResult(true);
                }
            }
            _ = CompleteDisposalAsync(completion);
            return new ValueTask(completion.Task);
        }

        /// <summary>
        /// Cancels and joins every admitted provider, publication and condition call before releasing nodes.
        /// </summary>
        /// <exception cref="AggregateException"></exception>
        private async Task CompleteDisposalAsync(TaskCompletionSource<bool> completion)
        {
            var failures = new List<Exception>();
            try
            {
                try
                {
                    await m_shutdown.CancelAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    failures.Add(error);
                }
                await m_drained.Task.ConfigureAwait(false);
                foreach (AlertBinding binding in m_alerts.Values)
                {
                    try
                    {
                        State.RemoveChild(binding.State);
                        State.RemoveReference(ReferenceTypeIds.HasCondition, false, binding.State.NodeId);
                        await DeleteAsync(binding.State).ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        failures.Add(error);
                    }
                }
                lock (m_lifetimeGate)
                {
                    m_alerts.Clear();
                }
                m_pending.Clear();
                if (m_messageType != null)
                {
                    try
                    {
                        await DeleteAsync(m_messageType).ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        failures.Add(error);
                    }
                }
                m_update.Dispose();
                m_shutdown.Dispose();
                if (failures.Count != 0)
                {
                    throw new AggregateException("Notification shutdown failed.", failures);
                }
                completion.TrySetResult(true);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        }

        /// <summary>
        /// Admits an operation without holding a lifetime gate across external callbacks.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ObjectDisposedException"></exception>
        private CancellationTokenSource BeginOperation(CancellationToken cancellationToken)
        {
            if (IsInCallback)
            {
                throw Invalid("Notification callbacks cannot reenter the same notification controller.");
            }
            lock (m_lifetimeGate)
            {
                if (m_stopped)
                {
                    throw new ObjectDisposedException(nameof(MachineToolNotificationController));
                }
                var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_shutdown.Token);
                m_operations++;
                return operation;
            }
        }

        /// <summary>
        /// Releases admission after callbacks and cleanup have completed.
        /// </summary>
        private void EndOperation()
        {
            lock (m_lifetimeGate)
            {
                if (--m_operations == 0 && m_stopped)
                {
                    m_drained.TrySetResult(true);
                }
            }
        }

        /// <summary>Protects operation admission and condition identity lookup only.</summary>
        private readonly Lock m_lifetimeGate = new();

        /// <summary>Cancels this instance's outstanding operations on removal.</summary>
        private readonly CancellationTokenSource m_shutdown = new();

        /// <summary>Completes when all admitted operations have left the controller.</summary>
        private readonly TaskCompletionSource<bool> m_drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The shared idempotent teardown completion.</summary>
        private Task? m_disposal;

        /// <summary>The number of admitted operations.</summary>
        private int m_operations;

        /// <summary>Whether removal has refused further operations.</summary>
        private volatile bool m_stopped;
    }
}
