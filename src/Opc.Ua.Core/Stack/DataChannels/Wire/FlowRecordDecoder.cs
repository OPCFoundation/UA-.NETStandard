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

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Lets an owner validate a fixed header or reserve capacity before body storage is allocated.
    /// </summary>
    internal delegate void FlowHeaderValidator(in FlowRecordHeader header);

    /// <summary>
    /// Decodes one bounded incomplete record per stream. The caller owns serialization and aggregate admission.
    /// </summary>
    internal sealed class FlowRecordDecoder : IDisposable
    {
        /// <summary>
        /// Creates a decoder with immutable local bounds.
        /// </summary>
        internal FlowRecordDecoder(FlowCodecLimits limits, FlowHeaderValidator? validateHeader = null)
        {
            m_limits = limits ?? throw new ArgumentNullException(nameof(limits));
            m_validateHeader = validateHeader;
        }

        /// <summary>
        /// Gets the bytes retained for the single incomplete record.
        /// </summary>
        internal int BufferedBytes => m_headerBytes + m_bodyBytes;

        /// <summary>
        /// Gets the reserved body storage, excluding the fixed 13-byte header.
        /// </summary>
        internal int AllocatedBodyBytes => m_body?.Length ?? 0;

        /// <inheritdoc/>
        public void Dispose()
        {
            Clear();
            m_header = [];
            m_disposed = true;
        }

        /// <summary>
        /// Consumes at most one record. A successful result owns its body; unconsumed bytes remain with the caller.
        /// </summary>
        internal bool Read(ReadOnlySpan<byte> input, out int consumed, out FlowRecord record)
        {
            EnsureReadable();
            consumed = 0;
            record = default;
            try
            {
                while (m_headerBytes < FlowRecordCodec.HeaderSize && consumed < input.Length)
                {
                    int milestone = m_headerBytes < 4 ? 4 : m_headerBytes < 5 ? 5 : FlowRecordCodec.HeaderSize;
                    int take = Math.Min(milestone - m_headerBytes, input.Length - consumed);
                    input.Slice(consumed, take).CopyTo(m_header.AsSpan(m_headerBytes));
                    consumed += take;
                    m_headerBytes += take;
                    FlowRecordCodec.InspectPrefix(m_header.AsSpan(0, m_headerBytes), m_limits);
                }
                if (m_headerBytes != FlowRecordCodec.HeaderSize)
                {
                    return false;
                }
                FlowRecordHeader header = FlowRecordCodec.ReadHeader(m_header, m_limits);
                if (!m_headerValidated)
                {
                    m_validateHeader?.Invoke(in header);
                    m_headerValidated = true;
                }
                if (header.BodyLength == 0)
                {
                    record = new FlowRecord(header.Kind, header.Revision, header.MessageId, ByteString.Empty);
                    Clear();
                    return true;
                }
                m_body ??= new byte[header.BodyLength];
                int count = Math.Min(header.BodyLength - m_bodyBytes, input.Length - consumed);
                input.Slice(consumed, count).CopyTo(m_body.AsSpan(m_bodyBytes));
                consumed += count;
                m_bodyBytes += count;
                if (m_bodyBytes != header.BodyLength)
                {
                    return false;
                }
                record = new FlowRecord(header.Kind, header.Revision, header.MessageId, new ByteString(m_body));
                Clear();
                return true;
            }
            catch
            {
                Clear();
                m_faulted = true;
                throw;
            }
        }

        /// <summary>
        /// Ends the stream, rejecting an incomplete prefix or body. Completion is not a container boundary.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal void Complete()
        {
            if (m_completed)
            {
                return;
            }
            EnsureReadable();
            if (BufferedBytes != 0)
            {
                Clear();
                m_faulted = true;
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
            m_completed = true;
        }

        /// <summary>
        /// Rejects reads after a terminal condition.
        /// </summary>
        /// <exception cref="ObjectDisposedException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        private void EnsureReadable()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(FlowRecordDecoder));
            }
            if (m_faulted || m_completed)
            {
                throw new InvalidOperationException("The record decoder has ended.");
            }
        }

        /// <summary>
        /// Releases the incomplete record without affecting a body already returned to the caller.
        /// </summary>
        private void Clear()
        {
            m_body = null;
            m_bodyBytes = 0;
            m_headerBytes = 0;
            m_headerValidated = false;
        }

        /// <summary>The immutable local decoder bounds.</summary>
        private readonly FlowCodecLimits m_limits;

        /// <summary>The fixed-size header staging area.</summary>
        private byte[] m_header = new byte[FlowRecordCodec.HeaderSize];

        /// <summary>The owner's optional pre-allocation guard.</summary>
        private readonly FlowHeaderValidator? m_validateHeader;

        /// <summary>The single bounded incomplete body.</summary>
        private byte[]? m_body;

        /// <summary>The received header bytes.</summary>
        private int m_headerBytes;

        /// <summary>The received body bytes.</summary>
        private int m_bodyBytes;

        /// <summary>Whether decoding has failed.</summary>
        private bool m_faulted;

        /// <summary>Whether the current header has passed the owner's guard.</summary>
        private bool m_headerValidated;

        /// <summary>Whether end-of-stream has been confirmed.</summary>
        private bool m_completed;

        /// <summary>Whether the decoder has been disposed.</summary>
        private bool m_disposed;
    }
}
