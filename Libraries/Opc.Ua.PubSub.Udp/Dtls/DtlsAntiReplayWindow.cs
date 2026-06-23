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

        private void TrimBitmap()
        {
            if (WindowSize < 64)
            {
                m_bitmap &= (1UL << WindowSize) - 1;
            }
        }

        private ulong m_highestSequenceNumber;
        private ulong m_bitmap;
        private bool m_hasHighest;
    }
}
