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

using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// An event queue that mirrors its contents to a shared key/value store so a promoted replica can restore
    /// queued-but-unpublished events after a failover (OPC 10000-4 §6.6; extension).
    /// </summary>
    /// <remarks>
    /// After every mutation an immutable snapshot of the current queue contents is handed to the owning
    /// <see cref="SharedKeyValueMonitoredItemQueueFactory"/>, which coalesces and persists it on a non-blocking
    /// background drain. Mirroring is suspended while the queue is being repopulated during restore.
    /// </remarks>
    internal sealed class MirroringEventMonitoredItemQueue : EventMonitoredItemQueue
    {
        /// <summary>
        /// Creates a mirroring event queue.
        /// </summary>
        /// <param name="monitoredItemId">The id of the owning monitored item.</param>
        /// <param name="sink">The sink that persists queue snapshots.</param>
        /// <param name="telemetry">The telemetry context.</param>
        public MirroringEventMonitoredItemQueue(
            uint monitoredItemId,
            IMirroredQueueSink sink,
            ITelemetryContext telemetry)
            : base(false, monitoredItemId, telemetry)
        {
            m_sink = sink;
        }

        /// <inheritdoc/>
        public override void Enqueue(EventFieldList value)
        {
            base.Enqueue(value);
            Mirror();
        }

        /// <inheritdoc/>
        public override bool Dequeue(out EventFieldList value)
        {
            bool dequeued = base.Dequeue(out value);
            if (dequeued)
            {
                Mirror();
            }
            return dequeued;
        }

        /// <inheritdoc/>
        public override void SetQueueSize(uint queueSize, bool discardOldest)
        {
            base.SetQueueSize(queueSize, discardOldest);
            Mirror();
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_sink.RemoveQueue(MonitoredItemId);
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Repopulates the queue from a restored snapshot without re-mirroring the events that were just read
        /// back from the shared store.
        /// </summary>
        /// <param name="snapshot">The restored snapshot.</param>
        internal void RestoreFrom(EventQueueSnapshot snapshot)
        {
            m_mirroring = false;
            try
            {
                SetQueueSize(snapshot.QueueSize, true);
                foreach (EventFieldList value in snapshot.Events)
                {
                    Enqueue(value);
                }
            }
            finally
            {
                m_mirroring = true;
            }
        }

        /// <summary>
        /// Captures an immutable snapshot of the current queue contents ordered oldest to newest.
        /// </summary>
        /// <returns>The snapshot.</returns>
        internal EventQueueSnapshot CaptureSnapshot()
        {
            return new EventQueueSnapshot
            {
                QueueSize = QueueSize,
                Events = [.. m_events]
            };
        }

        private void Mirror()
        {
            if (m_mirroring)
            {
                m_sink.MirrorEventQueue(MonitoredItemId, CaptureSnapshot());
            }
        }

        private readonly IMirroredQueueSink m_sink;
        private volatile bool m_mirroring = true;
    }
}
