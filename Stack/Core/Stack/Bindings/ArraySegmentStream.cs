/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.IO;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Provides stream access to a sequence of buffers.
    /// </summary>
    public class ArraySegmentStream : Stream
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
                m_endOfLastBuffer = m_buffers[m_buffers.Count-1].Count;
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
            int           bufferSize,
            int           start,
            int           count)

        {
            m_buffers = new BufferCollection();
            
            m_bufferManager = bufferManager;
            m_bufferSize = bufferSize;
            m_start = start;
            m_count = count;

            m_endOfLastBuffer = 0;
            
            SetCurrentBuffer(0);
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

            m_buffers.Clear();

            return buffers;
        }
        #endregion
        
        #region Overridden Methods
        /// <summary cref="Stream.CanRead" />
        public override bool CanRead
        {
            get { return true; }
        }
        
        /// <summary cref="Stream.CanSeek" />
        public override bool CanSeek
        {
            get { return true; }
        }
        
        /// <summary cref="Stream.CanWrite" />
        public override bool CanWrite
        {
            get { return true; }
        }
        
        /// <summary cref="Stream.Flush" />
        public override void Flush()
        {
            // nothing to do.
        }
        
        /// <summary cref="Stream.Length" />
        public override long Length
        {
            get { return GetAbsoluteLength(); }
        }
        
        /// <summary cref="Stream.Position" />
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
        
        /// <summary cref="Stream.Read" />
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
                count  -= bytesLeft;
                
                // move to next buffer.
                SetCurrentBuffer(m_bufferIndex+1);
            }

            return bytesRead;
        }
        
        /// <summary cref="Stream.Seek" />
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

            int position = (int)offset;
            
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
        
        /// <summary cref="Stream.SetLength" />
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
        
        /// <summary cref="Stream.Write" />
        public override void Write(byte[] buffer, int offset, int count)
        {
            while (count > 0)
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

                    // initialize buffer to zero (for debugging only).
                    #if TRACK_MEMORY
                    for (int ii = 0; ii < m_bufferSize; ii++)
                    {
                        newBuffer[ii] = 0;
                    }
                    #endif

                    SetCurrentBuffer(m_buffers.Count-1);
                }

                int bytesLeft = m_currentBuffer.Count - m_currentPosition;

                // copy the bytes requested.
                if (bytesLeft >= count)
                {
                    Array.Copy(buffer, offset, m_currentBuffer.Array, m_currentPosition + m_currentBuffer.Offset, count);
                    
                    m_currentPosition += count;

                    if (m_bufferIndex == m_buffers.Count-1)
                    {
                        if (m_endOfLastBuffer < m_currentPosition)
                        {
                            m_endOfLastBuffer = m_currentPosition;
                        }
                    }

                    return;
                }
                
                // copy the bytes available and move to next buffer.
                Array.Copy(buffer, offset, m_currentBuffer.Array, m_currentPosition + m_currentBuffer.Offset, bytesLeft);
                
                offset += bytesLeft;
                count  -= bytesLeft;
                
                // move to next buffer.
                SetCurrentBuffer(m_bufferIndex+1);
            }
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Sets the current buffer.
        /// </summary>
        private void SetCurrentBuffer(int index)
        {
            if (index < 0 || index >= m_buffers.Count)
            {
                m_currentBuffer   = new ArraySegment<byte>();
                m_currentPosition = 0;
                return;
            }

            m_bufferIndex     = index;
            m_currentBuffer   = m_buffers[index];
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
        private int GetBufferCount(int index)
        {
            if (index == m_buffers.Count-1)
            {
                return m_endOfLastBuffer;
            }

            return m_buffers[index].Count;
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
