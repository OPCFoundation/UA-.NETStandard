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
using Opc.Ua.Extrusion.Line;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// Drives the mandatory extrusion-line material interface independently of optional job management.
    /// </summary>
    public sealed class ExtrusionLineController : IAsyncDisposable
    {
        /// <summary>
        /// Creates the line controller on the existing generated sibling type.
        /// </summary>
        internal ExtrusionLineController(ExtrusionRuntime runtime, ExtrusionLine_InterfaceState node)
        {
            m_runtime = runtime;
            Node = node;
            m_events = new ExtrusionEventEmitter(runtime, node.MaterialList!);
        }

        /// <summary>
        /// Gets the real generated line.
        /// </summary>
        public ExtrusionLine_InterfaceState Node { get; }

        /// <summary>
        /// Requests missing material information through the declared RequestAddMaterial event.
        /// </summary>
        public async ValueTask RequestMaterialAsync(
            string materialId,
            DateTimeUtc sourceTimestamp,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(materialId) || sourceTimestamp == DateTimeUtc.MinValue)
            {
                throw new ArgumentException("A material identifier and actual source timestamp are required.");
            }
            ThrowIfDisposed();
            using ExtrusionOperationLifetime.Operation pending = m_lifetime.Begin(cancellationToken);
            await m_gate.WaitAsync(pending.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                RequestAddMaterialEventState notification =
                    m_runtime.SystemContext.CreateInstanceOfRequestAddMaterialEventType();
                notification.Id!.Value = materialId;
                await m_events.ReportAsync(notification, m_eventType, Node.MaterialList!,
                    sourceTimestamp, "Extrusion material information requested.").ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.CompareExchange(ref m_disposalStarted, 1, 0) == 0)
            {
                try
                {
                    try
                    {
                        await m_lifetime.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        m_gate.Dispose();
                    }
                    m_disposed.TrySetResult(true);
                }
                catch (Exception failure)
                {
                    m_disposed.TrySetException(failure);
                }
            }
            await m_disposed.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Makes the mandatory material list an event source even when JobGroups is absent.
        /// </summary>
        internal async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            using ExtrusionOperationLifetime.Operation pending = m_lifetime.Begin(cancellationToken);
            Node.MaterialList!.EventNotifier = EventNotifiers.SubscribeToEvents;
            Node.AddNotifier(m_runtime.SystemContext, Opc.Ua.ReferenceTypeIds.HasNotifier, false, Node.MaterialList);
            Node.MaterialList.AddNotifier(m_runtime.SystemContext, Opc.Ua.ReferenceTypeIds.HasNotifier, true, Node);
            m_eventType = await m_events.EnsureTypeAsync(Line.ObjectTypeIds.RequestAddMaterialEventType,
                "RequestAddMaterial", pending.Token).ConfigureAwait(false);
        }

        /// <summary>
        /// Rejects new events once the owner stops the line.
        /// </summary>
        private void ThrowIfDisposed()
        {
            m_lifetime.ThrowIfStopped();
        }

        /// <summary>The existing owner.</summary>
        private readonly ExtrusionRuntime m_runtime;

        /// <summary>The actual material-list notifier.</summary>
        private readonly ExtrusionEventEmitter m_events;

        /// <summary>Serializes event creation/reporting.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>Cancels and drains active and queued line event work.</summary>
        private readonly ExtrusionOperationLifetime m_lifetime = new(nameof(ExtrusionLineController));

        /// <summary>The concrete application-owned material event type.</summary>
        private NodeId m_eventType;

        /// <summary>Claims shutdown without holding a lock while cancellation callbacks execute.</summary>
        private int m_disposalStarted;

        /// <summary>Shares terminal completion, including release of the drained gate.</summary>
        private readonly TaskCompletionSource<bool> m_disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
