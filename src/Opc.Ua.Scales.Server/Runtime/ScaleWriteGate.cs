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
                    ExecuteAsynchronouslyAsync(operation, cancellationToken));
        }

        /// <summary>
        /// Gets the executor that runs a node's complete Value-write operation.
        /// </summary>
        internal NodeWriteOperationExecutor NodeWriteExecutor { get; }

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
        /// Waits until no admitted synchronous operation or asynchronous operation is active.
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
                    if (!m_asyncOwner && m_synchronousWriters == 0)
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
        /// Reserves synchronous admission unless an asynchronous operation owns the writer.
        /// </summary>
        private bool TryAdmitSynchronous()
        {
            lock (m_stateLock)
            {
                if (m_asyncOwner)
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
        /// The asynchronous waiter signal, when the writer is occupied.
        /// </summary>
        private TaskCompletionSource<bool>? m_stateChanged;

        /// <summary>
        /// The number of currently admitted synchronous operations.
        /// </summary>
        private int m_synchronousWriters;

        /// <summary>
        /// Whether an asynchronous operation owns the writer.
        /// </summary>
        private bool m_asyncOwner;
    }
}
