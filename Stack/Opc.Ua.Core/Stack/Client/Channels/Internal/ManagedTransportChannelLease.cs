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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    internal sealed class ManagedTransportChannelLease : IManagedTransportChannel
    {
        internal ManagedTransportChannelLease(
            ChannelEntry entry, IReconnectParticipant participant)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }

            m_entry = entry;
            Key = entry.Key;
            Endpoint = entry.Endpoint;
            ReverseConnection = entry.ReverseConnection;
            m_participant = participant;
            m_participantFactory = _ => Participant;
            m_active = 1;
        }

        internal ManagedTransportChannelLease(
            ChannelEntry entry,
            Func<IManagedTransportChannel, IReconnectParticipant> participantFactory)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }
            if (participantFactory == null)
            {
                throw new ArgumentNullException(nameof(participantFactory));
            }

            m_entry = entry;
            Key = entry.Key;
            Endpoint = entry.Endpoint;
            ReverseConnection = entry.ReverseConnection;
            m_active = 1;
            m_participant = participantFactory(this)
                ?? throw new InvalidOperationException("Participant factory returned null.");
            m_participantFactory = _ => Participant;
        }

        internal ChannelEntry Entry => Volatile.Read(ref m_entry);

        internal ConfiguredEndpoint Endpoint { get; }

        internal ITransportWaitingConnection? ReverseConnection { get; }

        internal Func<IManagedTransportChannel, IReconnectParticipant> ParticipantFactory => m_participantFactory;

        internal int SwapCount => Volatile.Read(ref m_swapCount);

        internal IReconnectParticipant Participant
        {
            get
            {
                lock (m_participantLock)
                {
                    return m_participant;
                }
            }
        }

        internal void RecordSwap()
        {
            Interlocked.Increment(ref m_swapCount);
        }

        internal void SwapEntry(ChannelEntry fresh)
        {
            if (fresh == null)
            {
                throw new ArgumentNullException(nameof(fresh));
            }

            Interlocked.Exchange(ref m_entry, fresh);
        }

        internal void SwapParticipantForEntry(IReconnectParticipant participant)
        {
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }

            lock (m_participantLock)
            {
                m_participant = participant;
            }
        }

        /// <summary>
        /// Atomically replace the participant associated with this
        /// lease. Retained for the obsolete
        /// <see cref="IClientChannelManager.RebindParticipant"/>
        /// compatibility path.
        /// </summary>
        internal void SwapParticipant(IReconnectParticipant participant)
        {
            if (participant == null)
            {
                throw new ArgumentNullException(nameof(participant));
            }

            ChannelEntry entry = Entry;
            string previousId;
            lock (m_participantLock)
            {
                previousId = m_participant.Id;
                m_participant = participant;
            }

            int refCount = entry.RefCount;
            int participantCount = entry.ParticipantCount;
            entry.OwnerManager.OnEntryParticipantDetached(entry, previousId, refCount, participantCount);
            entry.OwnerManager.OnEntryParticipantAttached(entry, participant.Id, refCount, participantCount);
        }

        internal bool IsActive => Interlocked.CompareExchange(ref m_active, 0, 0) == 1;

        /// <inheritdoc/>
        public ManagedChannelKey Key { get; }

        /// <inheritdoc/>
        public ChannelState State => Entry.State;

        /// <inheritdoc/>
        public IClientChannelManager Manager => Entry.OwnerManager;

        /// <inheritdoc/>
        public event Action<IManagedTransportChannel, ChannelStateChange>? StateChanged;

        internal void RaiseStateChanged(ChannelStateChange change)
        {
            try
            {
                StateChanged?.Invoke(this, change);
            }
            catch
            {
                // observer errors isolated
            }
        }

        internal void MarkReleased()
        {
            Interlocked.Exchange(ref m_active, 0);
        }

        internal void MarkActiveForSwap()
        {
            Interlocked.Exchange(ref m_active, 1);
        }

        // ---- ITransportChannel forwarding ----

        /// <inheritdoc/>
        public TransportChannelFeatures SupportedFeatures
            => Entry.Underlying?.SupportedFeatures ?? TransportChannelFeatures.None;

        /// <inheritdoc/>
        public EndpointDescription EndpointDescription
            => Entry.Underlying?.EndpointDescription ?? Entry.Endpoint.Description;

        /// <inheritdoc/>
        public EndpointConfiguration EndpointConfiguration
            => Entry.Underlying?.EndpointConfiguration ?? Entry.Endpoint.Configuration!;

        /// <inheritdoc/>
        public byte[] ChannelThumbprint
            => Entry.Underlying?.ChannelThumbprint ?? [];

        /// <inheritdoc/>
        public byte[] ClientChannelCertificate
            => Entry.Underlying?.ClientChannelCertificate ?? [];

        /// <inheritdoc/>
        public byte[] ServerChannelCertificate
            => Entry.Underlying?.ServerChannelCertificate ?? [];

        /// <inheritdoc/>
        public IServiceMessageContext MessageContext
            => Entry.Underlying?.MessageContext
            ?? Entry.OwnerManager.Configuration.CreateMessageContext();

        /// <inheritdoc/>
        public int OperationTimeout
        {
            get => Entry.Underlying?.OperationTimeout ?? 0;
            set
            {
                ITransportChannel? u = Entry.Underlying;
                if (u != null)
                {
                    u.OperationTimeout = value;
                }
            }
        }

        /// <inheritdoc/>
        public ValueTask ReconnectAsync(
            ITransportWaitingConnection? connection,
            CancellationToken ct = default)
        {
            return new ValueTask(Entry.RequestReconnectAsync(ct));
        }

        /// <inheritdoc/>
        public async ValueTask<IServiceResponse> SendRequestAsync(
            IServiceRequest request, CancellationToken ct = default)
        {
            if (ClientChannelManager.IsReactivationInProgress)
            {
                ITransportChannel? bypass = Entry.Underlying
                    ?? throw ServiceResultException.Create(
                        StatusCodes.BadSecureChannelClosed,
                        "Channel has no underlying transport.");
                return await bypass.SendRequestAsync(request, ct).ConfigureAwait(false);
            }

            await Entry.WaitForReadyAsync(ct).ConfigureAwait(false);

            ITransportChannel? underlying = Entry.Underlying
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadSecureChannelClosed,
                    "Channel has no underlying transport.");
            return await underlying.SendRequestAsync(request, ct).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken ct = default)
        {
            // CloseAsync on the lease releases the lease; the
            // underlying channel only closes when refcount→0. Used
            // by ClientBase.CloseChannelAsync and equivalents.
            return DisposeAsyncCore();
        }

        /// <summary>
        /// Synchronous dispose. Begins lease teardown asynchronously on
        /// a thread-pool thread and returns immediately. Callers that
        /// need to observe teardown completion or surface failures MUST
        /// use <see cref="CloseAsync(CancellationToken)"/>. The
        /// synchronous path exists only for compatibility with the
        /// <see cref="IDisposable"/> contract and the legacy
        /// <c>using</c> statement, and never blocks on network I/O —
        /// blocking would deadlock callers running under a synchronization
        /// context (legacy ASP.NET, WPF, WinForms).
        /// </summary>
        public void Dispose()
        {
            // Sync Dispose is best-effort. Mark the lease released
            // immediately (preventing any further SendRequestAsync /
            // ReconnectAsync calls), then push the actual network I/O
            // onto the thread pool. This decouples the synchronous
            // caller from the TCP FIN handshake — see
            // ChannelEntry.TearDownAsync — and avoids deadlocking
            // callers running under a synchronization context.
            if (Interlocked.Exchange(ref m_active, 0) == 0)
            {
                return;
            }
            ChannelEntry entry = Entry;
            _ = Task.Run(async () =>
            {
                try
                {
                    await entry.ReleaseLeaseAsync(this).ConfigureAwait(false);
                }
                catch
                {
                    // best effort — sync Dispose cannot surface async failures
                }
            });
        }

        private async ValueTask DisposeAsyncCore()
        {
            if (Interlocked.Exchange(ref m_active, 0) == 0)
            {
                return;
            }
            await Entry.ReleaseLeaseAsync(this).ConfigureAwait(false);
        }

        private ChannelEntry m_entry;
        private int m_active;
        private int m_swapCount;
        private readonly Lock m_participantLock = new();
        private readonly Func<IManagedTransportChannel, IReconnectParticipant> m_participantFactory;
        private IReconnectParticipant m_participant;
    }
}
