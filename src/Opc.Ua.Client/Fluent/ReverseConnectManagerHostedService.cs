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
using Microsoft.Extensions.Hosting;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Drives eager startup and graceful shutdown of the singleton
    /// <see cref="ReverseConnectManager"/> when the application uses the
    /// .NET Generic Host. <see cref="StartAsync"/> ensures the manager's
    /// configured listeners are opened on host start;
    /// <see cref="StopAsync"/> stops the service on host shutdown. When no
    /// host is present the manager still starts lazily on first use via
    /// <see cref="ReverseConnectManager.EnsureStartedAsync"/>.
    /// </summary>
    internal sealed class ReverseConnectManagerHostedService : IHostedService
    {
        /// <summary>
        /// Initializes the hosted service with the managed reverse-connect
        /// manager.
        /// </summary>
        /// <param name="manager">The reverse-connect manager singleton.</param>
        public ReverseConnectManagerHostedService(ReverseConnectManager manager)
        {
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Cancelling the host's startup token must abort the underlying
            // reverse-connect startup, not merely this awaiter, so link the
            // token to the manager's pending start.
            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    static state => ((ReverseConnectManager)state!).CancelPendingStart(),
                    m_manager);
            try
            {
                await m_manager.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The awaiter's cancellation can resume inline and dispose the
                // registration above before it fires (a LIFO/inline-continuation
                // race), so the pending start would otherwise be left running.
                // Abort it here too; CancelPendingStart is idempotent, so a
                // double invocation with the registration is harmless.
                m_manager.CancelPendingStart();
                throw;
            }
        }

        /// <inheritdoc/>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return m_manager.StopServiceAsync(cancellationToken);
        }

        private readonly ReverseConnectManager m_manager;
    }
}
