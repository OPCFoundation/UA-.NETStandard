// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Asynchronously admits initial client connect attempts.
    /// </summary>
    public interface IClientConnectGate
    {
        /// <summary>
        /// Waits until an initial connect attempt is admitted.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A lease that releases the admission permit when disposed.</returns>
        ValueTask<IDisposable> AcquireAsync(CancellationToken ct = default);
    }
}
