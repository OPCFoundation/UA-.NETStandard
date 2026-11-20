/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace PadimSample
{
    /// <summary>
    /// Restores acquisition after a demonstration simulation attempt, including an uncertain enable result.
    /// </summary>
    internal static class DemonstrationSimulation
    {
        /// <summary>
        /// Attempts simulation, reads its result, and always requests disable without caller cancellation.
        /// </summary>
        internal static async ValueTask RunAsync(
            Func<bool, CancellationToken, ValueTask> writeStateAsync,
            Func<CancellationToken, ValueTask> readAsync,
            CancellationToken cancellationToken)
        {
            if (writeStateAsync == null)
            {
                throw new ArgumentNullException(nameof(writeStateAsync));
            }
            if (readAsync == null)
            {
                throw new ArgumentNullException(nameof(readAsync));
            }
            try
            {
                await writeStateAsync(true, cancellationToken).ConfigureAwait(false);
                await readAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await writeStateAsync(false, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }
}
