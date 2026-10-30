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

namespace Opc.Ua.DataChannels.Wire
{
    /// <summary>
    /// Validates a direct-frame header and reserves aggregate capacity before body allocation.
    /// </summary>
    internal delegate IDisposable? DirectDeliveryFrameHeaderValidator(in DirectDeliveryFrameHeader header);

    /// <summary>
    /// Decodes one bounded incomplete direct ordered-stream frame at a time.
    /// The caller owns serialization and aggregate admission.
    /// </summary>
    internal sealed class DirectDeliveryFrameDecoder : IDisposable
    {
        /// <summary>
        /// Creates a decoder with immutable local bounds and an optional admission callback.
        /// </summary>
        internal DirectDeliveryFrameDecoder(
            FlowCodecLimits limits,
            DirectDeliveryFrameHeaderValidator? validateHeader = null)
        {
            m_limits = limits ?? throw new ArgumentNullException(nameof(limits));
            m_validateHeader = validateHeader;
            m_header = new byte[DirectDeliveryFrameCodec.HeaderSize];
        }

        /// <summary>
        /// Creates a decoder after reserving its parser storage and one direct stream in the shared budget.
        /// </summary>
        internal DirectDeliveryFrameDecoder(
            FlowCodecLimits limits,
            DataChannelApplicationBudget budget,
            DirectDeliveryFrameHeaderValidator validateHeader)
        {
            m_limits = limits ?? throw new ArgumentNullException(nameof(limits));
            if (budget is null)
            {
                throw new ArgumentNullException(nameof(budget));
            }
            m_validateHeader = validateHeader ?? throw new ArgumentNullException(nameof(validateHeader));
            m_requiresAdmissionReservation = true;
            if (!budget.TryReserveCarrier(
                ParserStorageBytes,
                streams: 1,
                operations: 0,
                out m_parserReservation))
            {
                throw new DataChannelException(DataChannelResult.WouldBlock);
            }
            try
            {
                m_header = new byte[DirectDeliveryFrameCodec.HeaderSize];
            }
            catch
            {
                ReleaseParserReservation();
                throw;
            }
        }

        /// <summary>
        /// Gets the bytes retained for the single incomplete frame.
        /// </summary>
        internal int BufferedBytes => m_headerBytes + m_bodyBytes;

        /// <summary>
        /// Gets the allocated frame body size.
        /// </summary>
        internal int AllocatedBodyBytes => m_body?.Length ?? 0;

        /// <summary>
        /// Gets whether a validated header is waiting for aggregate capacity before body allocation.
        /// </summary>
        internal bool IsWaitingForCapacity { get; private set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            Clear();
            ReleaseParserReservation();
            m_header = [];
            m_disposed = true;
        }

        /// <summary>
        /// Consumes at most one complete frame. Unconsumed bytes remain with the caller.
        /// </summary>
        /// <exception cref="FlowCodecException"></exception>
        internal bool Read(
            ReadOnlySpan<byte> input,
            out int consumed,
            out DirectDeliveryFrame frame,
            bool deferAdmission = false)
        {
            EnsureReadable();
            consumed = 0;
            frame = default;
            try
            {
                while (m_headerBytes < DirectDeliveryFrameCodec.HeaderSize && consumed < input.Length)
                {
                    int milestone = m_headerBytes < DirectDeliveryFrameCodec.LengthPrefixSize
                        ? DirectDeliveryFrameCodec.LengthPrefixSize
                        : DirectDeliveryFrameCodec.HeaderSize;
                    int take = Math.Min(milestone - m_headerBytes, input.Length - consumed);
                    input.Slice(consumed, take).CopyTo(m_header.AsSpan(m_headerBytes));
                    consumed += take;
                    m_headerBytes += take;
                    DirectDeliveryFrameCodec.InspectPrefix(m_header.AsSpan(0, m_headerBytes), m_limits);
                }
                if (m_headerBytes != DirectDeliveryFrameCodec.HeaderSize)
                {
                    return false;
                }

                DirectDeliveryFrameHeader header =
                    DirectDeliveryFrameCodec.ReadHeader(m_header, m_limits);
                if (!m_headerValidated)
                {
                    IDisposable? reservation = m_validateHeader?.Invoke(in header);
                    if (m_requiresAdmissionReservation && reservation is null)
                    {
                        if (deferAdmission)
                        {
                            IsWaitingForCapacity = true;
                            return false;
                        }
                        throw new DataChannelException(DataChannelResult.WouldBlock);
                    }
                    IsWaitingForCapacity = false;
                    if (reservation is not null)
                    {
                        m_reservation = new DirectDeliveryFrameReservation(reservation);
                    }
                    m_headerValidated = true;
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

                frame = DirectDeliveryFrameCodec.DecodeBody(
                    header.Kind,
                    new ByteString(m_body),
                    m_limits,
                    m_reservation);
                m_reservation = null;
                Clear();
                return true;
            }
            catch
            {
                Clear();
                ReleaseParserReservation();
                m_faulted = true;
                throw;
            }
        }

        /// <summary>
        /// Ends the stream and rejects an incomplete prefix or frame body.
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
                ReleaseParserReservation();
                m_faulted = true;
                throw new FlowCodecException(FlowCodecError.Truncated);
            }
            ReleaseParserReservation();
            m_completed = true;
        }

        private void EnsureReadable()
        {
            if (m_disposed)
            {
                throw new ObjectDisposedException(nameof(DirectDeliveryFrameDecoder));
            }
            if (m_faulted || m_completed)
            {
                throw new InvalidOperationException("The direct-frame decoder has ended.");
            }
        }

        private void Clear()
        {
            m_reservation?.Dispose();
            m_reservation = null;
            m_body = null;
            m_bodyBytes = 0;
            m_headerBytes = 0;
            m_headerValidated = false;
            IsWaitingForCapacity = false;
        }

        private void ReleaseParserReservation()
        {
            m_parserReservation?.Dispose();
            m_parserReservation = null;
        }

        /// <summary>
        /// Fixed header storage retained for the lifetime of one direct stream parser.
        /// </summary>
        internal const int ParserStorageBytes = DirectDeliveryFrameCodec.HeaderSize;

        private readonly FlowCodecLimits m_limits;
        private readonly DirectDeliveryFrameHeaderValidator? m_validateHeader;
        private readonly bool m_requiresAdmissionReservation;
        private DataChannelApplicationBudget.DataChannelCarrierReservation? m_parserReservation;
        private byte[] m_header;
        private byte[]? m_body;
        private DirectDeliveryFrameReservation? m_reservation;
        private int m_headerBytes;
        private int m_bodyBytes;
        private bool m_headerValidated;
        private bool m_faulted;
        private bool m_completed;
        private bool m_disposed;
    }
}
