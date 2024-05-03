/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
#define STREAM_WITH_SPAN_SUPPORT
#endif

using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Provides stream access to a sequence of buffers.
    /// </summary>
    public class ArraySegmentStream : MemoryStream
    {
        #region Constructors
        /// <summary>
        /// Attaches the stream to a set of buffers
        /// </summary>
        /// <param name="buffers">The buffers.</param>
        public ArraySegmentStream(BufferCollection buffers)
        {
            m_buffers = buffers;
            m_endOfLastBuffer = 0;

            if (m_buffers.Count > 0)
            {
                m_endOfLastBuffer = m_buffers[m_buffers.Count - 1].Count;
            }

            SetCurrentBuffer(0);
        }

        /// <summary>
        /// Creates a writeable stream that creates buffers as necessary.
        /// </summary>
        /// <param name="bufferManager">The buffer manager.</param>
        /// <param name="bufferSize">Size of the buffer.</param>
        /// <param name="start">The start.</param>
        /// <param name="count">The count.</param>
        public ArraySegmentStream(
            BufferManager bufferManager,
            int bufferSize,
            int start,
            int count)
        {
            m_buffers = new BufferCollection();

            m_bufferManager = bufferManager;
            m_bufferSize = bufferSize;
            m_start = start;
            m_count = count;

            m_endOfLastBuffer = 0;

            SetCurrentBuffer(0);
        }

        /// <summary>
        /// Creates a writeable stream that creates buffers as necessary.
        /// </summary>
        /// <param name="bufferManager">The buffer manager.</param>
        public ArraySegmentStream(BufferManager bufferManager)
            : this(bufferManager, bufferManager.MaxSuggestedBufferSize, 0, bufferManager.MaxSuggestedBufferSize)
        {
        }
        #endregion

        #region IDisposable
        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (m_buffers != null && m_bufferManager != null)
                {
                    for (int ii = 0; ii < m_buffers.Count; ii++)
                    {
                        m_bufferManager.ReturnBuffer(m_buffers[ii].Array, "ArraySegmentStream.Dispose");
                    }
                    m_buffers.Clear();
                    m_buffers = null;
                }
                m_bufferManager = null;
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Returns ownership of the buffers stored in the stream.
        /// </summary>
        /// <param name="owner">The owner.</param>
        /// <returns></returns>
        public BufferCollection GetBuffers(string owner)
        {
            BufferCollection buffers = new BufferCollection(m_buffers.Count);

            for (int ii = 0; ii < m_buffers.Count; ii++)
            {
                m_bufferManager.TransferBuffer(m_buffers[ii].Array, owner);
                buffers.Add(new ArraySegment<byte>(m_buffers[ii].Array, m_buffers[ii].Offset, GetBufferCount(ii)));
            }

            ClearBuffers();

            return buffers;
        }

        /// <summary>
        /// Returns sequence of the buffers stored in the stream.
        /// </summary>
        /// <remarks>
        /// The buffers ownership is transferred to the sequence,
        /// the stream can be disposed.
        /// The new owner is responisble to dispose the sequence after use.
        /// </remarks>
        public BufferSequence GetSequence(string owner)
        {
            if (m_buffers.Count == 0)
            {
                return new BufferSequence(m_bufferManager, owner, null, ReadOnlySequence<byte>.Empty);
            }

            int endIndex = GetBufferCount(0);
            var firstSegment = new BufferSegment(m_buffers[0].Array, m_buffers[0].Offset, endIndex);
            m_bufferManager.TransferBuffer(m_buffers[0].Array, owner);
            BufferSegment nextSegment = firstSegment;
            for (int ii = 1; ii < m_buffers.Count; ii++)
            {
                m_bufferManager.TransferBuffer(m_buffers[ii].Array, owner);
                endIndex = GetBufferCount(ii);
                nextSegment = nextSegment.Append(m_buffers[ii].Array, m_buffers[ii].Offset, endIndex);
            }

            var sequence = new ReadOnlySequence<byte>(firstSegment, 0, nextSegment, endIndex);

            ClearBuffers();

            return new BufferSequence(m_bufferManager, owner, firstSegment, sequence);
        }
        #endregion

        #region Overridden Methods
        /// <inheritdoc/>
        public override bool CanRead
        {
            get { return m_buffers != null; }
        }

        /// <inheritdoc/>
        public override bool CanSeek
        {
            get { return m_buffers != null; }
        }

        /// <inheritdoc/>
        public override bool CanWrite
        {
            get { return m_buffers != null; }
        }

        /// <inheritdoc/>
        public override void Flush()
        {
            // nothing to do.
        }

        /// <inheritdoc/>
        public override long Length
        {
            get { return GetAbsoluteLength(); }
        }

        /// <inheritdoc/>
        public override long Position
        {
            get
            {
                return GetAbsolutePosition();
            }

            set
            {
                Seek(value, SeekOrigin.Begin);
            }
        }

        /// <inheritdoc/>
        public override int ReadByte()
        {
            do
            {
                // check for end of stream.
                if (m_currentBuffer.Array == null)
                {
                    return -1;
                }

                int bytesLeft = GetBufferCount(m_bufferIndex) - m_currentPosition;

                // copy the bytes requested.
                if (bytesLeft > 0)
                {
#if STREAM_WITH_SPAN_SUPPORT
                    return m_currentBuffer[m_currentPosition++];
#else
                    return m_currentBuffer.Array[m_currentBuffer.Offset + m_currentPosition++];
#endif
                }

                // move to next buffer.
                SetCurrentBuffer(m_bufferIndex + 1);

            } while (true);
        }

#if STREAM_WITH_SPAN_SUPPORT
        /// <summary>
        /// Helper to benchmark the performance of the stream.
        /// </summary>
        internal int ReadMemoryStream(Span<byte> buffer) => base.Read(buffer);

        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
        {
            int count = buffer.Length;
            int offset = 0;
            int bytesRead = 0;

            while (count > 0)
            {
                // check for end of stream.
                if (m_currentBuffer.Array == null)
                {
                    return bytesRead;
                }

                int bytesLeft = GetBufferCount(m_bufferIndex) - m_currentPosition;

                // copy the bytes requested.
                if (bytesLeft > count)
                {
                    m_currentBuffer.AsSpan(m_currentPosition, count).CopyTo(buffer.Slice(offset));
                    bytesRead += count;
                    m_currentPosition += count;
                    return bytesRead;
                }

                // copy the bytes available and move to next buffer.
                m_currentBuffer.AsSpan(m_currentPosition, bytesLeft).CopyTo(buffer.Slice(offset));
                bytesRead += bytesLeft;

                offset += bytesLeft;
                count -= bytesLeft;

                // move to next buffer.
                SetCurrentBuffer(m_bufferIndex + 1);
            }

            return bytesRead;
        }
#endif

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = 0;

            while (count > 0)
            {
                // check for end of stream.
                if (m_currentBuffer.Array == null)
                {
                    return bytesRead;
                }

                int bytesLeft = GetBufferCount(m_bufferIndex) - m_currentPosition;

                // copy the bytes requested.
                if (bytesLeft > count)
                {
                    Array.Copy(m_currentBuffer.Array, m_currentPosition + m_currentBuffer.Offset, buffer, offset, count);
                    bytesRead += count;
                    m_currentPosition += count;
                    return bytesRead;
                }

                // copy the bytes available and move to next buffer.
                Array.Copy(m_currentBuffer.Array, m_currentPosition + m_currentBuffer.Offset, buffer, offset, bytesLeft);
                bytesRead += bytesLeft;

                offset += bytesLeft;
                count -= bytesLeft;

                // move to next buffer.
                SetCurrentBuffer(m_bufferIndex + 1);
            }

            return bytesRead;
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                {
                    break;
                }

                case SeekOrigin.Current:
                {
                    offset += GetAbsolutePosition();
                    break;
                }

                case SeekOrigin.End:
                {
                    offset += GetAbsoluteLength();
                    break;
                }
            }

            if (offset < 0)
            {
                throw new IOException("Cannot seek beyond the beginning of the stream.");
            }

            // special case
            if (offset == 0)
            {
                SetCurrentBuffer(0);
                return 0;
            }

            int position = (int)offset;

            if (position > GetAbsolutePosition())
            {
                CheckEndOfStream();
            }

            for (int ii = 0; ii < m_buffers.Count; ii++)
            {
                int length = GetBufferCount(ii);

                if (offset <= length)
                {
                    SetCurrentBuffer(ii);
                    m_currentPosition = (int)offset;
                    return position;
                }

                offset -= length;
            }

            throw new IOException("Cannot seek beyond the end of the stream.");
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void WriteByte(byte value)
        {
            do
            {
                // allocate new buffer if necessary
                CheckEndOfStream();

                int bytesLeft = m_currentBuffer.Count - m_currentPosition;

                // copy the byte requested.
                if (bytesLeft >= 1)
                {
#if STREAM_WITH_SPAN_SUPPORT
                    m_currentBuffer[m_currentPosition] = value;
#else
                    m_currentBuffer.Array[m_currentBuffer.Offset + m_currentPosition] = value;
#endif
                    UpdateCurrentPosition(1);

                    return;
                }

                // move to next buffer.
                SetCurrentBuffer(m_bufferIndex + 1);

            } while (true);
        }

#if STREAM_WITH_SPAN_SUPPORT
        /// <summary>
        /// Helper to benchmark the performance of the stream.
        /// </summary>
        internal void WriteMemoryStream(ReadOnlySpan<byte> buffer) => base.Write(buffer);

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            int count = buffer.Length;
            int offset = 0;
            while (count > 0)
            {
                // check for end of stream.
                CheckEndOfStream();

                int bytesLeft = m_currentBuffer.Count - m_currentPosition;

                // copy the bytes requested.
                if (bytesLeft >= count)
                {
                    buffer.Slice(offset, count).CopyTo(m_currentBuffer.AsSpan(m_currentPosition));

                    UpdateCurrentPosition(count);

                    return;
                }

                // copy the bytes available and move to next buffer.
                buffer.Slice(offset, bytesLeft).CopyTo(m_currentBuffer.AsSpan(m_currentPosition));

                offset += bytesLeft;
                count -= bytesLeft;

                // move to next buffer.
                SetCurrentBuffer(m_bufferIndex + 1);
            }
        }
#endif

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                // check for end of stream.
                CheckEndOfStream();

                int bytesLeft = m_currentBuffer.Count - m_currentPosition;

                // copy the bytes requested.
                if (bytesLeft >= count)
                {
                    Array.Copy(buffer, offset, m_currentBuffer.Array, m_currentPosition + m_currentBuffer.Offset, count);

                    UpdateCurrentPosition(count);

                    return;
                }

                // copy the bytes available and move to next buffer.
                Array.Copy(buffer, offset, m_currentBuffer.Array, m_currentPosition + m_currentBuffer.Offset, bytesLeft);

                offset += bytesLeft;
                count -= bytesLeft;

                // move to next buffer.
                SetCurrentBuffer(m_bufferIndex + 1);
            }
        }

        /// <inheritdoc/>
        public override byte[] ToArray()
        {
            if (m_buffers == null)
            {
                throw new ObjectDisposedException(nameof(ArraySegmentStream));
            }

            int absoluteLength = GetAbsoluteLength();
            if (absoluteLength == 0)
            {
                return Array.Empty<byte>();
            }

#if NET6_0_OR_GREATER
            byte[] buffer = GC.AllocateUninitializedArray<byte>(absoluteLength);
#else
            byte[] buffer = new byte[absoluteLength];
#endif

            int offset = 0;
            for (int ii = 0; ii < m_buffers.Count; ii++)
            {
                int length = GetBufferCount(ii);
                Array.Copy(m_buffers[ii].Array, m_buffers[ii].Offset, buffer, offset, length);
                offset += length;
            }

            return buffer;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Update the current buffer count.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateCurrentPosition(int count)
        {
            m_currentPosition += count;

            if (m_bufferIndex == m_buffers.Count - 1)
            {
                if (m_endOfLastBuffer < m_currentPosition)
                {
                    m_endOfLastBuffer = m_currentPosition;
                }
            }
        }

        /// <summary>
        /// Sets the current buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetCurrentBuffer(int index)
        {
            if (index < 0 || index >= m_buffers.Count)
            {
                m_currentBuffer = new ArraySegment<byte>();
                m_currentPosition = 0;
                return;
            }

            m_bufferIndex = index;
            m_currentBuffer = m_buffers[index];
            m_currentPosition = 0;
        }

        /// <summary>
        /// Returns the total length in all buffers.
        /// </summary>
        private int GetAbsoluteLength()
        {
            int length = 0;

            for (int ii = 0; ii < m_buffers.Count; ii++)
            {
                length += GetBufferCount(ii);
            }

            return length;
        }

        /// <summary>
        /// Returns the current position.
        /// </summary>
        private int GetAbsolutePosition()
        {
            // check if at end of stream.
            if (m_currentBuffer.Array == null)
            {
                return GetAbsoluteLength();
            }

            // calculate position.
            int position = 0;

            for (int ii = 0; ii < m_bufferIndex; ii++)
            {
                position += GetBufferCount(ii);
            }

            position += m_currentPosition;

            return position;
        }

        /// <summary>
        /// Returns the number of bytes used in the buffer.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetBufferCount(int index)
        {
            if (index == m_buffers.Count - 1)
            {
                return m_endOfLastBuffer;
            }

            return m_buffers[index].Count;
        }

        /// <summary>
        /// Check if end of stream is reached and take new buffer if necessary.
        /// </summary>
        /// <exception cref="IOException">Throws if end of stream is reached.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckEndOfStream()
        {
            // check for end of stream.
            if (m_currentBuffer.Array == null)
            {
                if (m_bufferManager == null)
                {
                    throw new IOException("Attempt to write past end of stream.");
                }

                byte[] newBuffer = m_bufferManager.TakeBuffer(m_bufferSize, "ArraySegmentStream.Write");
                m_buffers.Add(new ArraySegment<byte>(newBuffer, m_start, m_count));
                m_endOfLastBuffer = 0;

                SetCurrentBuffer(m_buffers.Count - 1);
            }
        }

        /// <summary>
        /// Clears the buffers and resets the state variables.
        /// </summary>
        private void ClearBuffers()
        {
            m_buffers.Clear();
            m_bufferIndex = 0;
            m_endOfLastBuffer = 0;
            SetCurrentBuffer(0);
        }
        #endregion

        #region Private Fields
        private int m_bufferIndex;
        private ArraySegment<byte> m_currentBuffer;
        private int m_currentPosition;
        private BufferCollection m_buffers;
        private BufferManager m_bufferManager;
        private int m_start;
        private int m_count;
        private int m_bufferSize;
        private int m_endOfLastBuffer;
        #endregion
    }
}
