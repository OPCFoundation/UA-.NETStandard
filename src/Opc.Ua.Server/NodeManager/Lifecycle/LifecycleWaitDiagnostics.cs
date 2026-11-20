/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Observes lifecycle waits without changing their completion or cancellation semantics.
    /// </summary>
    internal static class LifecycleWaitDiagnostics
    {
        /// <summary>
        /// Gets the elapsed wait threshold after which lifecycle diagnostics are emitted.
        /// </summary>
        internal static TimeSpan WarningThreshold { get; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Awaits an operation and emits one diagnostic if it remains pending past the threshold.
        /// </summary>
        /// <param name="operation">The operation to await.</param>
        /// <param name="timeProvider">The clock used for elapsed-time tracking.</param>
        /// <param name="warningThreshold">The threshold for emitting the diagnostic.</param>
        /// <param name="warning">The diagnostic callback.</param>
        /// <param name="cancellationToken">Cancels this wait without cancelling the operation.</param>
        internal static async Task WaitAsync(
            Task operation,
            TimeProvider timeProvider,
            TimeSpan warningThreshold,
            Action<TimeSpan> warning,
            CancellationToken cancellationToken = default)
        {
            if (operation is null)
            {
                throw new ArgumentNullException(nameof(operation));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            if (warning is null)
            {
                throw new ArgumentNullException(nameof(warning));
            }

            Task wait = operation.WaitAsync(cancellationToken);
            if (wait.IsCompleted)
            {
                await wait.ConfigureAwait(false);
                return;
            }

            long started = timeProvider.GetTimestamp();
            using var warningCancellation = new CancellationTokenSource();
            Task warningDelay = timeProvider.Delay(warningThreshold, warningCancellation.Token);
            try
            {
                if (await Task.WhenAny(wait, warningDelay).ConfigureAwait(false) == wait)
                {
                    await wait.ConfigureAwait(false);
                    return;
                }

                warning(timeProvider.GetElapsedTime(started));
                await wait.ConfigureAwait(false);
            }
            finally
            {
                warningCancellation.Cancel();
            }
        }
    }
}
