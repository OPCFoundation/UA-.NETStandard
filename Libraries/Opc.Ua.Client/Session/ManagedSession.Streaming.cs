/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.Extensions.Logging;
using Opc.Ua.Client.ModelChange;
using Opc.Ua.Client.Subscriptions.Streaming;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Streaming subscription + model change tracking integration for
    /// <see cref="ManagedSession"/>. Implements lazy access to a shared
    /// streaming subscription and an optional model change tracker that
    /// invalidates the session's <see cref="INodeCache"/> when the server
    /// reports address-space changes.
    /// </summary>
    public partial class ManagedSession
    {
        private IStreamingSubscription? m_defaultStreaming;
        private IModelChangeTracker? m_modelChangeTracker;
        private readonly object m_streamingLock = new();
        private bool m_modelChangeTrackingEnabled;

        /// <summary>
        /// A shared lazy <see cref="IStreamingSubscription"/> over this
        /// session's V2 subscription manager (see
        /// <see cref="ISession.TryGetSubscriptionManager"/>). Backs both
        /// <see cref="ModelChange"/> (when enabled) and ad-hoc
        /// <see cref="IAsyncEnumerable{T}"/>-based subscriptions.
        /// </summary>
        /// <remarks>
        /// Created on first access and disposed with the session. The
        /// underlying OPC UA subscription is itself created lazily, on the
        /// first call to a <c>SubscribeXxxAsync</c> method.
        /// </remarks>
        /// <exception cref="InvalidOperationException"></exception>
        public IStreamingSubscription DefaultStreaming
        {
            get
            {
                if (m_defaultStreaming != null)
                {
                    return m_defaultStreaming;
                }

                lock (m_streamingLock)
                {
                    if (m_defaultStreaming != null)
                    {
                        return m_defaultStreaming;
                    }

                    if (!TryGetSubscriptionManager(
                            out Subscriptions.ISubscriptionManager? manager))
                    {
                        throw new InvalidOperationException(
                            "Streaming subscriptions require the V2 subscription engine. " +
                            "The session is using the classic engine; recreate the " +
                            "ManagedSession with the V2 subscription engine factory.");
                    }
                    m_defaultStreaming = new StreamingSubscription(manager);
                    return m_defaultStreaming;
                }
            }
        }

        /// <summary>
        /// Optional model change tracker. Created on first call to
        /// <see cref="EnableModelChangeTrackingAsync"/>. Subscribes to
        /// the server's <c>GeneralModelChangeEventType</c> notifier and
        /// invalidates the session's <see cref="INodeCache"/> on change.
        /// Returns <c>null</c> if tracking has never been enabled.
        /// </summary>
        public IModelChangeTracker? ModelChange => m_modelChangeTracker;

        /// <summary>
        /// Enables address-space model change tracking on this session.
        /// Creates the tracker on first call and starts pumping changes.
        /// Idempotent — subsequent calls are no-ops once enabled.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask EnableModelChangeTrackingAsync(CancellationToken ct = default)
        {
            if (m_modelChangeTrackingEnabled)
            {
                return;
            }

            IModelChangeTracker tracker;
            lock (m_streamingLock)
            {
                if (m_modelChangeTrackingEnabled)
                {
                    return;
                }

                m_modelChangeTracker ??= new ModelChangeTracker(
                    DefaultStreaming,
                    NodeCache,
                    m_logger);
                m_modelChangeTrackingEnabled = true;
                tracker = m_modelChangeTracker;
            }

            await tracker.StartTrackingAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Disables address-space model change tracking. The tracker is
        /// stopped but the streaming subscription is preserved.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        public async ValueTask DisableModelChangeTrackingAsync(CancellationToken ct = default)
        {
            IModelChangeTracker? tracker;
            lock (m_streamingLock)
            {
                if (!m_modelChangeTrackingEnabled)
                {
                    return;
                }
                m_modelChangeTrackingEnabled = false;
                tracker = m_modelChangeTracker;
            }

            if (tracker != null)
            {
                await tracker.StopTrackingAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Disposes the streaming subscription and model change tracker
        /// owned by this session. Called from
        /// <c>ManagedSession.DisposeAsync</c>.
        /// </summary>
        private async ValueTask DisposeStreamingAsync()
        {
            IModelChangeTracker? tracker;
            IStreamingSubscription? streaming;
            lock (m_streamingLock)
            {
                tracker = m_modelChangeTracker;
                streaming = m_defaultStreaming;
                m_modelChangeTracker = null;
                m_defaultStreaming = null;
                m_modelChangeTrackingEnabled = false;
            }

            if (tracker != null)
            {
                try
                {
                    await tracker.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogDebug(ex,
                        "ManagedSession: ModelChangeTracker dispose failed.");
                }
            }

            if (streaming != null)
            {
                try
                {
                    await streaming.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogDebug(ex,
                        "ManagedSession: DefaultStreaming dispose failed.");
                }
            }
        }
    }
}
