// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// RFC 9147 §4.5.1 sliding anti-replay window for DTLS records.
    /// </summary>
    public sealed class DtlsAntiReplayWindow
    {
        /// <summary>
        /// Initializes a new <see cref="DtlsAntiReplayWindow"/>.
        /// </summary>
        public DtlsAntiReplayWindow(int windowSize = 64)
        {
            if (windowSize is <= 0 or > 64)
            {
                throw new System.ArgumentOutOfRangeException(nameof(windowSize), "Window size must be 1..64 records.");
            }

            WindowSize = windowSize;
        }

        /// <summary>
        /// Replay window size in records.
        /// </summary>
        public int WindowSize { get; }

        /// <summary>
        /// Returns true once for each new sequence number and false for replays or too-old records.
        /// </summary>
        public bool TryAccept(ulong sequenceNumber)
        {
            if (!m_hasHighest)
            {
                m_hasHighest = true;
                m_highestSequenceNumber = sequenceNumber;
                m_bitmap = 1;
                return true;
            }

            if (sequenceNumber > m_highestSequenceNumber)
            {
                ulong shift = sequenceNumber - m_highestSequenceNumber;
                m_bitmap = shift >= 64 ? 1 : (m_bitmap << (int)shift) | 1;
                m_highestSequenceNumber = sequenceNumber;
                TrimBitmap();
                return true;
            }

            ulong offset = m_highestSequenceNumber - sequenceNumber;
            if (offset >= (ulong)WindowSize)
            {
                return false;
            }

            ulong mask = 1UL << (int)offset;
            if ((m_bitmap & mask) != 0)
            {
                return false;
            }

            m_bitmap |= mask;
            TrimBitmap();
            return true;
        }

        /// <summary>
        /// Non-mutating check that reports whether the sequence number would be rejected as a
        /// replay or as a too-old record. Used to peek before a record is authenticated so that
        /// forged or duplicate datagrams cannot mutate the window state ahead of authentication.
        /// </summary>
        public bool IsReplay(ulong sequenceNumber)
        {
            if (!m_hasHighest)
            {
                return false;
            }

            if (sequenceNumber > m_highestSequenceNumber)
            {
                return false;
            }

            ulong offset = m_highestSequenceNumber - sequenceNumber;
            if (offset >= (ulong)WindowSize)
            {
                return true;
            }

            ulong mask = 1UL << (int)offset;
            return (m_bitmap & mask) != 0;
        }

        private void TrimBitmap()
        {
            if (WindowSize < 64)
            {
                m_bitmap &= (1UL << WindowSize) - 1;
            }
        }

        /// <summary>
        /// Indicates whether at least one record has been accepted (a highest
        /// sequence number is known).
        /// </summary>
        public bool HasHighest => m_hasHighest;

        /// <summary>
        /// Highest accepted full 64-bit sequence number, used to reconstruct the
        /// high-order bits of a truncated on-wire sequence number (RFC 9147 §4.2.2).
        /// </summary>
        public ulong HighestSequenceNumber => m_highestSequenceNumber;

        private ulong m_highestSequenceNumber;
        private ulong m_bitmap;
        private bool m_hasHighest;
    }
}
