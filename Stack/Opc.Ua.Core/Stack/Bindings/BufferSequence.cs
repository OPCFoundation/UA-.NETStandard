/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// A class to hold a sequence of buffers until disposed.
    /// </summary>
    public sealed class BufferSequence : IDisposable
    {
        /// <summary>
        /// The constructor to create the sequence of buffers.
        /// </summary>
        public BufferSequence(BufferManager bufferManager, string owner, BufferSegment firstSegment, ReadOnlySequence<byte> sequence)
        {
            m_bufferManager = bufferManager;
            m_owner = owner;
            m_firstSegment = firstSegment;
            m_sequence = sequence;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            BufferSegment segment = m_firstSegment;
            while (segment != null)
            {
                m_bufferManager.ReturnBuffer(segment.Array(), m_owner);
                segment = (BufferSegment)segment.Next;
            }
            m_sequence = ReadOnlySequence<byte>.Empty;
            m_firstSegment = null;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Returns a sequence which can be used to access the buffers.
        /// </summary>
        public ReadOnlySequence<byte> Sequence => m_sequence;

        #region Private 
        private BufferManager m_bufferManager;
        private BufferSegment m_firstSegment;
        private ReadOnlySequence<byte> m_sequence;
        private string m_owner;
        #endregion
    }
}
