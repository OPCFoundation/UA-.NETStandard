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

namespace Opc.Ua.DataChannels
{
    /// <summary>
    /// One complete application message. Disposal releases its encoded-byte and message-count
    /// reservation exactly once; a reset does not steal a message already handed to a reader.
    /// </summary>
    public sealed class DataChannelMessage : IDisposable
    {
        /// <summary>
        /// Takes ownership of a complete immutable body and its admission reservation.
        /// </summary>
        internal DataChannelMessage(ByteString payload, uint revision, uint messageId, Action release)
        {
            m_payload = payload;
            Revision = revision;
            MessageId = messageId;
            m_release = release;
        }

        /// <summary>
        /// Gets complete payload bytes valid until disposal.
        /// </summary>
        public ByteString Payload => m_payload;

        /// <summary>
        /// Gets the revision under which this message was admitted.
        /// </summary>
        public uint Revision { get; }

        /// <summary>
        /// Gets the monotonically increasing sender DATA identifier.
        /// </summary>
        public uint MessageId { get; }

        /// <summary>
        /// Gets the complete application payload length.
        /// </summary>
        public int Length => m_payload.Length;

        /// <inheritdoc/>
        public void Dispose()
        {
            Action? release = Interlocked.Exchange(ref m_release, null);
            if (release != null)
            {
                m_payload = ByteString.Empty;
                release();
            }
        }

        private ByteString m_payload;
        private Action? m_release;
    }
}
