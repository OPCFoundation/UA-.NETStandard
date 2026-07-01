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
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Pcap
{
    /// <summary>
    /// Owns the lifetime of a single in-process PubSub capture: it creates
    /// the <see cref="InProcessPubSubCaptureSource"/>, installs it on the
    /// shared <see cref="IPubSubCaptureRegistry"/>, and exposes the captured
    /// frames / keys for dissection. Only one capture may be active at a time.
    /// </summary>
    public sealed class PubSubCaptureSessionManager : IAsyncDisposable
    {
        /// <summary>
        /// Initializes a new <see cref="PubSubCaptureSessionManager"/>.
        /// </summary>
        /// <param name="registry">
        /// The capture registry shared with the PubSub transports.
        /// </param>
        /// <param name="loggerFactory">Optional logger factory.</param>
        public PubSubCaptureSessionManager(
            IPubSubCaptureRegistry registry,
            ILoggerFactory? loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(registry);
            m_registry = registry;
            m_loggerFactory = loggerFactory;
        }

        /// <summary>
        /// The active capture source, or <see langword="null"/> when no
        /// capture is running.
        /// </summary>
        public IPubSubCaptureSource? ActiveSource => Volatile.Read(ref m_active);

        /// <summary>
        /// Starts a new in-process capture session. Throws if one is already
        /// running.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The started capture source.</returns>
        public async ValueTask<IPubSubCaptureSource> StartAsync(
            CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_active is not null)
                {
                    throw new InvalidOperationException(
                        "A PubSub capture session is already active.");
                }
                var source = new InProcessPubSubCaptureSource(
                    m_registry,
                    m_loggerFactory?.CreateLogger<InProcessPubSubCaptureSource>());
                await source.StartAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_active, source);
                return source;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Stops the active capture session if one is running. The returned
        /// source remains readable for replay until disposed.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// The stopped source, or <see langword="null"/> if none was active.
        /// </returns>
        public async ValueTask<IPubSubCaptureSource?> StopAsync(
            CancellationToken cancellationToken = default)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                InProcessPubSubCaptureSource? source = m_active;
                if (source is null)
                {
                    return null;
                }
                await source.StopAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref m_active, null);
                return source;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            InProcessPubSubCaptureSource? source = Interlocked.Exchange(ref m_active, null);
            if (source is not null)
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
            m_gate.Dispose();
        }

        private readonly IPubSubCaptureRegistry m_registry;
        private readonly ILoggerFactory? m_loggerFactory;
        private readonly SemaphoreSlim m_gate = new(1, 1);
        private InProcessPubSubCaptureSource? m_active;
    }
}
