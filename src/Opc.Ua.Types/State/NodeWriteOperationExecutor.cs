// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Runs a complete node write operation without exposing its synchronization mechanism.
    /// </summary>
    internal sealed class NodeWriteOperationExecutor
    {
        /// <summary>
        /// Creates an executor for synchronous and asynchronous write operations.
        /// </summary>
        /// <param name="executeSynchronously">Runs one synchronous write operation.</param>
        /// <param name="executeAsynchronously">Runs one asynchronous write operation.</param>
        /// <param name="executeNotifications">Runs state-change notifications.</param>
        internal NodeWriteOperationExecutor(
            Func<Func<ServiceResult>, ServiceResult> executeSynchronously,
            Func<Func<CancellationToken, ValueTask<ServiceResult>>, CancellationToken, ValueTask<ServiceResult>>
                executeAsynchronously,
            Func<Func<CancellationToken, ValueTask>, CancellationToken, ValueTask>? executeNotifications = null)
        {
            m_executeSynchronously = executeSynchronously
                ?? throw new ArgumentNullException(nameof(executeSynchronously));
            m_executeAsynchronously = executeAsynchronously
                ?? throw new ArgumentNullException(nameof(executeAsynchronously));
            m_executeNotifications = executeNotifications ??
                ((operation, cancellationToken) => operation(cancellationToken));
        }

        /// <summary>
        /// Executes the full synchronous node write.
        /// </summary>
        /// <param name="operation">The validation, callback, and cache-commit operation.</param>
        /// <returns>The operation result.</returns>
        internal ServiceResult ExecuteSynchronously(Func<ServiceResult> operation)
        {
            return m_executeSynchronously(operation);
        }

        /// <summary>
        /// Executes the full asynchronous node write.
        /// </summary>
        /// <param name="operation">The validation, callback, and cache-commit operation.</param>
        /// <param name="cancellationToken">The operation cancellation token.</param>
        /// <returns>The operation result after its cache commit or failure.</returns>
        internal ValueTask<ServiceResult> ExecuteAsynchronouslyAsync(
            Func<CancellationToken, ValueTask<ServiceResult>> operation,
            CancellationToken cancellationToken)
        {
            return m_executeAsynchronously(operation, cancellationToken);
        }

        /// <summary>
        /// Runs state-change notifications while retaining synchronous write exclusion.
        /// </summary>
        /// <param name="operation">The notification operation.</param>
        /// <param name="cancellationToken">The notification cancellation token.</param>
        /// <returns>A task that completes when the notifications finish.</returns>
        internal ValueTask ExecuteNotificationsAsync(
            Func<CancellationToken, ValueTask> operation,
            CancellationToken cancellationToken)
        {
            return m_executeNotifications(operation, cancellationToken);
        }

        /// <summary>
        /// Runs a complete synchronous write operation.
        /// </summary>
        private readonly Func<Func<ServiceResult>, ServiceResult> m_executeSynchronously;

        /// <summary>
        /// Runs a complete asynchronous write operation.
        /// </summary>
        private readonly Func<
            Func<CancellationToken, ValueTask<ServiceResult>>,
            CancellationToken,
            ValueTask<ServiceResult>> m_executeAsynchronously;

        /// <summary>
        /// Runs state-change notifications.
        /// </summary>
        private readonly Func<Func<CancellationToken, ValueTask>, CancellationToken, ValueTask>
            m_executeNotifications;
    }
}
