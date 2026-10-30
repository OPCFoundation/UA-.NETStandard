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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// Application-wide ceilings, shared by configured and Session-authorized contexts alike.
    /// These are independent of Server Session/request isolation and also work without a Server.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed record DataChannelApplicationLimits
    {
        /// <summary>
        /// Gets the simultaneous context limit.
        /// </summary>
        public int MaxContexts { get; init; } = 128;

        /// <summary>
        /// Gets the simultaneous flow limit.
        /// </summary>
        public int MaxFlows { get; init; } = 1024;

        /// <summary>
        /// Gets the reserved receive-window ceiling, including pending/previous revisions.
        /// </summary>
        public long MaxReservedBytes { get; init; } = 512L * 1024 * 1024;

        /// <summary>
        /// Gets the encoded send, receive and control bytes retained across all contexts.
        /// </summary>
        public long MaxBufferedBytes { get; init; } = 512L * 1024 * 1024;

        /// <summary>
        /// Gets the queued and application-owned message count ceiling.
        /// </summary>
        public int MaxMessages { get; init; } = 16384;

        /// <summary>
        /// Gets the simultaneous operation ceiling.
        /// </summary>
        public int MaxOperations { get; init; } = 1024;

        /// <summary>
        /// Gets the unredeemed grant ceiling.
        /// </summary>
        public int MaxGrants { get; init; } = 1024;

        /// <summary>
        /// Gets the unanswered probe ceiling.
        /// </summary>
        public int MaxProbes { get; init; } = 128;

        /// <summary>
        /// Gets the simultaneous native stream ceiling, including management streams.
        /// </summary>
        public int MaxStreams { get; init; } = 2048;

        /// <summary>
        /// Gets the bounded context anti-reuse history capacity.
        /// Exhaustion refuses new contexts rather than forgetting closed identities.
        /// </summary>
        public int MaxContextHistory { get; init; } = 131072;
    }

    /// <summary>
    /// Exact resource counters for application diagnostics.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public readonly record struct DataChannelResourceUsage(
        int Contexts,
        int Flows,
        long ReservedBytes,
        long BufferedBytes,
        int Messages,
        int Operations,
        int Grants,
        int Probes,
        int Streams);

    /// <summary>
    /// Shared, atomic admission accounting. Register one instance per application, not per connection.
    /// Reservations are retained while messages are application-owned, even after context closure.
    /// </summary>
    [global::System.Diagnostics.CodeAnalysis.Experimental(DataChannelFeature.ExperimentalDiagnosticId,
        UrlFormat = DataChannelFeature.ExperimentalUrlFormat)]
    public sealed class DataChannelApplicationBudget
    {
        /// <summary>
        /// Creates a bounded accounting provider.
        /// </summary>
        public DataChannelApplicationBudget(DataChannelApplicationLimits? limits = null)
        {
            m_limits = limits ?? new DataChannelApplicationLimits();
            if (m_limits.MaxContexts <= 0 ||
                m_limits.MaxFlows <= 0 ||
                m_limits.MaxReservedBytes <= 0 ||
                m_limits.MaxBufferedBytes <= 0 ||
                m_limits.MaxMessages <= 0 ||
                m_limits.MaxOperations <= 0 ||
                m_limits.MaxGrants <= 0 ||
                m_limits.MaxProbes <= 0 ||
                m_limits.MaxStreams <= 0 ||
                m_limits.MaxContextHistory <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limits));
            }
        }

        /// <summary>
        /// Gets an atomic accounting snapshot.
        /// </summary>
        public DataChannelResourceUsage Usage
        {
            get
            {
                lock (m_gate)
                {
                    return new DataChannelResourceUsage(
                        (int)m_used[(int)FlowResource.Context],
                        (int)m_used[(int)FlowResource.Flow],
                        m_used[(int)FlowResource.ReservedBytes],
                        m_used[(int)FlowResource.BufferedBytes],
                        (int)m_used[(int)FlowResource.Message],
                        (int)m_used[(int)FlowResource.Operation],
                        (int)m_used[(int)FlowResource.Grant],
                        (int)m_used[(int)FlowResource.Probe],
                        (int)m_used[(int)FlowResource.Stream]);
                }
            }
        }

        /// <summary>
        /// Atomically reserves additional carrier-retained bytes, native jobs and transport control streams.
        /// Do not double-charge the logical flow/management streams already owned by DataChannelContext.
        /// A false result leaves all dimensions unchanged; the successful lease releases exactly once.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public bool TryReserveCarrier(
            long bufferedBytes,
            int streams,
            int operations,
            [NotNullWhen(true)] out DataChannelCarrierReservation? reservation)
        {
            if (bufferedBytes < 0 || streams < 0 || operations < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferedBytes));
            }
            lock (m_gate)
            {
                if (bufferedBytes > m_limits.MaxBufferedBytes -
                    m_used[(int)FlowResource.BufferedBytes] -
                    m_unusedControlBytes ||
                    streams > m_limits.MaxStreams - m_used[(int)FlowResource.Stream] ||
                    operations > m_limits.MaxOperations - m_used[(int)FlowResource.Operation])
                {
                    reservation = null;
                    return false;
                }
                reservation = new DataChannelCarrierReservation(this, bufferedBytes, streams, operations);
                m_used[(int)FlowResource.BufferedBytes] += bufferedBytes;
                m_used[(int)FlowResource.Stream] += streams;
                m_used[(int)FlowResource.Operation] += operations;
                return true;
            }
        }

        /// <summary>
        /// Atomically releases a carrier lease and wakes application admission waiters.
        /// </summary>
        internal void ReleaseCarrier(long bufferedBytes, int streams, int operations)
        {
            TaskSignal signal;
            lock (m_gate)
            {
                m_used[(int)FlowResource.BufferedBytes] -= bufferedBytes;
                m_used[(int)FlowResource.Stream] -= streams;
                m_used[(int)FlowResource.Operation] -= operations;
                signal = m_changed;
                m_changed = new TaskSignal();
            }
            signal.Complete();
        }

        /// <summary>
        /// Records a new context identity without allowing closed identifiers to be reused.
        /// </summary>
        internal bool RegisterContext(Guid contextId)
        {
            lock (m_gate)
            {
                return contextId != Guid.Empty &&
                    m_contextIds.Count < m_limits.MaxContextHistory &&
                    m_contextIds.Add(contextId);
            }
        }

        /// <summary>
        /// Owns one admitted carrier allocation independently of application DATA-message ownership.
        /// </summary>
        public sealed class DataChannelCarrierReservation : IDisposable
        {
            /// <summary>
            /// Creates a lease only after all shared application limits have been checked atomically.
            /// </summary>
            internal DataChannelCarrierReservation(
                DataChannelApplicationBudget budget, long bufferedBytes, int streams, int operations)
            {
                m_budget = budget;
                m_bufferedBytes = bufferedBytes;
                m_streams = streams;
                m_operations = operations;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                DataChannelApplicationBudget? budget = Interlocked.Exchange(ref m_budget, null);
                budget?.ReleaseCarrier(m_bufferedBytes, m_streams, m_operations);
            }

            private DataChannelApplicationBudget? m_budget;
            private readonly long m_bufferedBytes;
            private readonly int m_streams;
            private readonly int m_operations;
        }

        /// <summary>
        /// Attempts a single atomic resource reservation.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal bool TryReserve(FlowResource resource, long amount)
        {
            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }
            lock (m_gate)
            {
                long maximum = resource switch
                {
                    FlowResource.Context => m_limits.MaxContexts,
                    FlowResource.Flow => m_limits.MaxFlows,
                    FlowResource.ReservedBytes => m_limits.MaxReservedBytes,
                    FlowResource.BufferedBytes => m_limits.MaxBufferedBytes - m_unusedControlBytes,
                    FlowResource.Message => m_limits.MaxMessages - m_unusedControlMessages,
                    FlowResource.Operation => m_limits.MaxOperations,
                    FlowResource.Grant => m_limits.MaxGrants,
                    FlowResource.Probe => m_limits.MaxProbes,
                    FlowResource.Stream => m_limits.MaxStreams,
                    _ => throw new ArgumentOutOfRangeException(nameof(resource))
                };
                if (amount > maximum - m_used[(int)resource])
                {
                    return false;
                }
                m_used[(int)resource] += amount;
                return true;
            }
        }

        /// <summary>
        /// Reserves a complete message atomically without publishing a capacity change on refused admission.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal bool TryReserveMessage(long bufferedBytes)
        {
            if (bufferedBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferedBytes));
            }
            lock (m_gate)
            {
                if (bufferedBytes > m_limits.MaxBufferedBytes -
                    m_used[(int)FlowResource.BufferedBytes] -
                    m_unusedControlBytes ||
                    m_used[(int)FlowResource.Message] >= m_limits.MaxMessages - m_unusedControlMessages)
                {
                    return false;
                }
                m_used[(int)FlowResource.BufferedBytes] += bufferedBytes;
                m_used[(int)FlowResource.Message]++;
                return true;
            }
        }

        /// <summary>
        /// Reserves one received message and its retained body storage with disposable ownership.
        /// </summary>
        internal bool TryReserveMessageLease(
            long bufferedBytes,
            [NotNullWhen(true)] out DataChannelMessageReservation? reservation)
        {
            if (bufferedBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferedBytes));
            }
            lock (m_gate)
            {
                if (bufferedBytes > m_limits.MaxBufferedBytes -
                    m_used[(int)FlowResource.BufferedBytes] -
                    m_unusedControlBytes ||
                    m_used[(int)FlowResource.Message] >= m_limits.MaxMessages - m_unusedControlMessages)
                {
                    reservation = null;
                    return false;
                }
                reservation = new DataChannelMessageReservation(this, bufferedBytes);
                m_used[(int)FlowResource.BufferedBytes] += bufferedBytes;
                m_used[(int)FlowResource.Message]++;
                return true;
            }
        }

        /// <summary>
        /// Releases a received-message lease atomically and wakes admission waiters.
        /// </summary>
        private void ReleaseMessageLease(long bufferedBytes)
        {
            TaskSignal signal;
            lock (m_gate)
            {
                m_used[(int)FlowResource.BufferedBytes] -= bufferedBytes;
                m_used[(int)FlowResource.Message]--;
                signal = m_changed;
                m_changed = new TaskSignal();
            }
            signal.Complete();
        }

        /// <summary>
        /// Owns one atomic received-message and retained-body reservation.
        /// </summary>
        internal sealed class DataChannelMessageReservation : IDisposable
        {
            /// <summary>
            /// Creates a reservation after all message and byte limits have been checked.
            /// </summary>
            internal DataChannelMessageReservation(DataChannelApplicationBudget budget, long bufferedBytes)
            {
                m_budget = budget;
                m_bufferedBytes = bufferedBytes;
            }

            /// <summary>
            /// Transfers the existing charge to the context's application-message release path.
            /// </summary>
            internal void TransferOwnership()
            {
                if (Interlocked.Exchange(ref m_budget, null) == null)
                {
                    throw new InvalidOperationException("The message reservation has already been released.");
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                DataChannelApplicationBudget? budget = Interlocked.Exchange(ref m_budget, null);
                budget?.ReleaseMessageLease(m_bufferedBytes);
            }

            private DataChannelApplicationBudget? m_budget;
            private readonly long m_bufferedBytes;
        }

        /// <summary>
        /// Withholds per-context control capacity from DATA without counting unused capacity as retained storage.
        /// </summary>
        internal bool TryReserveControlHeadroom([NotNullWhen(true)] out ControlHeadroom? headroom)
        {
            lock (m_gate)
            {
                if (ControlHeadroomBytes > m_limits.MaxBufferedBytes -
                    m_used[(int)FlowResource.BufferedBytes] -
                    m_unusedControlBytes ||
                    ControlHeadroomMessages > m_limits.MaxMessages -
                        m_used[(int)FlowResource.Message] -
                        m_unusedControlMessages)
                {
                    headroom = null;
                    return false;
                }
                m_unusedControlBytes += ControlHeadroomBytes;
                m_unusedControlMessages += ControlHeadroomMessages;
                headroom = new ControlHeadroom(this);
                return true;
            }
        }

        /// <summary>
        /// Draws from this context's guaranteed capacity before competing for unreserved application capacity.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private bool TryReserveControl(ControlHeadroom headroom, int cost)
        {
            if (cost < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cost));
            }
            lock (m_gate)
            {
                long coveredBytes = Math.Min(cost, Math.Max(0, ControlHeadroomBytes - headroom.Bytes));
                int coveredMessages = headroom.Messages < ControlHeadroomMessages ? 1 : 0;
                if (headroom.Released ||
                    cost - coveredBytes > m_limits.MaxBufferedBytes -
                        m_used[(int)FlowResource.BufferedBytes] -
                        m_unusedControlBytes ||
                    1 - coveredMessages > m_limits.MaxMessages -
                        m_used[(int)FlowResource.Message] -
                        m_unusedControlMessages)
                {
                    return false;
                }
                m_unusedControlBytes -= coveredBytes;
                m_unusedControlMessages -= coveredMessages;
                m_used[(int)FlowResource.BufferedBytes] += cost;
                m_used[(int)FlowResource.Message]++;
                headroom.Bytes += cost;
                headroom.Messages++;
                return true;
            }
        }

        /// <summary>
        /// Releases actual control ownership and replenishes only a still-live context's guaranteed headroom.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void ReleaseControl(ControlHeadroom headroom, int cost)
        {
            TaskSignal signal;
            lock (m_gate)
            {
                if (cost < 0 || headroom.Bytes < cost || headroom.Messages <= 0)
                {
                    throw new InvalidOperationException("Unbalanced data channel control release.");
                }
                long previousUnused = Math.Max(0, ControlHeadroomBytes - headroom.Bytes);
                headroom.Bytes -= cost;
                headroom.Messages--;
                if (!headroom.Released)
                {
                    m_unusedControlBytes += Math.Max(0, ControlHeadroomBytes - headroom.Bytes) - previousUnused;
                    if (headroom.Messages < ControlHeadroomMessages)
                    {
                        m_unusedControlMessages++;
                    }
                }
                m_used[(int)FlowResource.BufferedBytes] -= cost;
                m_used[(int)FlowResource.Message]--;
                signal = m_changed;
                m_changed = new TaskSignal();
            }
            signal.Complete();
        }

        /// <summary>
        /// Returns unused headroom at closure while outstanding control storage remains accounted until released.
        /// </summary>
        private void ReleaseControlHeadroom(ControlHeadroom headroom)
        {
            TaskSignal signal;
            lock (m_gate)
            {
                if (headroom.Released)
                {
                    return;
                }
                headroom.Released = true;
                m_unusedControlBytes -= Math.Max(0, ControlHeadroomBytes - headroom.Bytes);
                m_unusedControlMessages -= Math.Max(0, ControlHeadroomMessages - headroom.Messages);
                signal = m_changed;
                m_changed = new TaskSignal();
            }
            signal.Complete();
        }

        /// <summary>
        /// Releases exactly one prior reservation and signals waiters outside accounting state.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal void Release(FlowResource resource, long amount)
        {
            TaskSignal signal;
            lock (m_gate)
            {
                if (amount < 0 || m_used[(int)resource] < amount)
                {
                    throw new InvalidOperationException("Unbalanced data channel resource release.");
                }
                m_used[(int)resource] -= amount;
                signal = m_changed;
                m_changed = new TaskSignal();
            }
            signal.Complete();
        }

        /// <summary>
        /// Gets a capacity-change signal without retaining per-waiter accounting state.
        /// </summary>
        internal System.Threading.Tasks.Task CapacityChanged
        {
            get
            {
                lock (m_gate)
                {
                    return m_changed.Task;
                }
            }
        }

        /// <summary>
        /// Owns capacity for a directional barrier, its peer barrier, CREDIT and ACK, including storage overlap.
        /// Larger establishment controls still require additional application admission.
        /// </summary>
        internal sealed class ControlHeadroom(DataChannelApplicationBudget budget) : IDisposable
        {
            /// <summary>
            /// Gets or sets retained control bytes under the application budget's synchronization.
            /// </summary>
            internal long Bytes { get; set; }

            /// <summary>
            /// Gets or sets retained control messages under the application budget's synchronization.
            /// </summary>
            internal int Messages { get; set; }

            /// <summary>
            /// Gets or sets whether unused capacity has been returned by context closure.
            /// </summary>
            internal bool Released { get; set; }

            /// <summary>
            /// Reserves one complete control before retaining encoded or decoded storage.
            /// </summary>
            internal bool TryReserve(int cost)
            {
                return budget.TryReserveControl(this, cost);
            }

            /// <summary>
            /// Releases exactly one control after its final owner finishes.
            /// </summary>
            internal void Release(int cost)
            {
                budget.ReleaseControl(this, cost);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                budget.ReleaseControlHeadroom(this);
            }
        }

        /// <summary>
        /// Capacity for a fixed-field revision exchange and its encoding/decoding overlap.
        /// </summary>
        internal const int ControlHeadroomBytes = 512;

        /// <summary>
        /// Slots for both directional barriers plus CREDIT and ACK, independent of DATA-message capacity.
        /// </summary>
        internal const int ControlHeadroomMessages = 4;

        private readonly Lock m_gate = new();
        private readonly DataChannelApplicationLimits m_limits;
        private readonly long[] m_used = new long[9];
        private readonly HashSet<Guid> m_contextIds = [];
        private TaskSignal m_changed = new();
        private long m_unusedControlBytes;
        private int m_unusedControlMessages;
    }

    /// <summary>
    /// Distinguishes resources that cannot be substituted for each other.
    /// </summary>
    internal enum FlowResource
    {
        Context,
        Flow,
        ReservedBytes,
        BufferedBytes,
        Message,
        Operation,
        Grant,
        Probe,
        Stream
    }

    /// <summary>
    /// Asynchronously completed notification, with no inline user continuations.
    /// </summary>
    internal sealed class TaskSignal
    {
        /// <summary>
        /// Gets the completion task.
        /// </summary>
        internal System.Threading.Tasks.Task Task => m_source.Task;

        /// <summary>
        /// Wakes all current waiters.
        /// </summary>
        internal void Complete()
        {
            m_source.TrySetResult(true);
        }

        private readonly System.Threading.Tasks.TaskCompletionSource<bool> m_source =
            new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
