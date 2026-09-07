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
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Lazily opens and caches a single live channel for one compiled form,
    /// shared by every reader/writer wired against that form for the lifetime
    /// of a projection binding runtime generation. Concurrent first use opens
    /// the channel exactly once; a failed open is evicted immediately so a
    /// later call can retry. Disposing the slot disposes the channel only if
    /// it was opened successfully — a faulted open leaves nothing to dispose
    /// and never re-surfaces the original open failure. Once disposed, the
    /// slot never opens another channel: <see cref="GetAsync"/> racing with or
    /// occurring after <see cref="DisposeAsync"/> either observes the channel
    /// already claimed for disposal or is rejected outright, so no channel
    /// this slot opens can ever escape disposal.
    /// </summary>
    internal sealed class WotBindingChannelSlot
    {
        /// <summary>
        /// Initializes a new channel slot for a compiled form.
        /// </summary>
        public WotBindingChannelSlot(WotCompiledForm form, IWotBindingChannelFactory channelFactory)
        {
            m_form = form ?? throw new ArgumentNullException(nameof(form));
            m_channelFactory = channelFactory ?? throw new ArgumentNullException(nameof(channelFactory));
        }

        /// <summary>
        /// Gets the shared channel, opening it on first use. The channel open
        /// itself is not bound to any single caller's cancellation token — it
        /// is a generation-scoped resource shared by every reader/writer wired
        /// against the same compiled form, so one caller cancelling must not
        /// tear down the open for concurrent callers.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// The slot has already been disposed, or is disposed concurrently
        /// before this call is able to reuse or start an open.
        /// </exception>
        public ValueTask<IWotBindingChannel> GetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task<IWotBindingChannel> task;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(WotBindingChannelSlot));
                }
                task = m_channelTask ??= OpenAsync();
            }
            return WaitAsync(task);
        }

        /// <summary>
        /// Marks the slot disposed so no later <see cref="GetAsync"/> call can
        /// start a new open, then disposes the cached channel if one was
        /// successfully opened (including one still opening concurrently — the
        /// disposal awaits it and disposes the result). A faulted or
        /// never-started open has no resource to release and is silently
        /// ignored so cleanup never re-reports the original open failure.
        /// Safe to call more than once; only the first call finds a channel to
        /// dispose.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            Task<IWotBindingChannel>? task;
            lock (m_gate)
            {
                task = m_channelTask;
                m_channelTask = null;
                m_disposed = true;
            }
            if (task is null)
            {
                return;
            }
            IWotBindingChannel channel;
            try
            {
                channel = await task.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return;
            }
            await channel.DisposeAsync().ConfigureAwait(false);
        }

        private Task<IWotBindingChannel> OpenAsync()
        {
            return m_channelFactory.OpenChannelAsync(m_form, CancellationToken.None).AsTask();
        }

        private async ValueTask<IWotBindingChannel> WaitAsync(Task<IWotBindingChannel> task)
        {
            try
            {
                return await task.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                lock (m_gate)
                {
                    if (ReferenceEquals(m_channelTask, task))
                    {
                        m_channelTask = null;
                    }
                }
                throw;
            }
        }

        private readonly WotCompiledForm m_form;
        private readonly IWotBindingChannelFactory m_channelFactory;
        private readonly Lock m_gate = new();
        private Task<IWotBindingChannel>? m_channelTask;
        private bool m_disposed;
    }
}
