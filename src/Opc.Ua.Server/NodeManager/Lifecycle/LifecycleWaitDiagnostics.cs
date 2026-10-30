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
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentNullException.ThrowIfNull(warning);

            Task wait = operation.WaitAsync(cancellationToken);
            if (wait.IsCompleted)
            {
                await wait.ConfigureAwait(false);
                return;
            }

            long started = timeProvider.GetTimestamp();
            using var warningCancellation = new CancellationTokenSource();
            Task warningDelay = Task.Delay(warningThreshold, timeProvider, warningCancellation.Token);
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
