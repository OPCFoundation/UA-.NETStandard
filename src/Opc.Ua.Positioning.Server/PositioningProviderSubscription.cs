// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Positioning.Server
{
    /// <summary>
    /// Owns a provider watch loop created by <see cref="PositioningAddressSpaceBuilder"/>.
    /// </summary>
    public sealed class PositioningProviderSubscription :
        IDisposable,
        IAsyncDisposable
    {
        private readonly CancellationTokenSource m_cts;
        private int m_disposed;

        internal PositioningProviderSubscription(
            CancellationTokenSource cts,
            Task completion)
        {
            m_cts = cts;
            Completion = completion;
        }

        /// <summary>
        /// Provider loop completion.
        /// </summary>
        public Task Completion { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                m_cts.Cancel();
                m_cts.Dispose();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Dispose();
            try
            {
                await Completion.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
