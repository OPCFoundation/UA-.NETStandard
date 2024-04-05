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
    /// Helper to build a ReadOnlySequence from a set of buffers.
    /// </summary>
    public sealed class BufferSegment : ReadOnlySequenceSegment<byte>
    {
        /// <summary>
        /// Returns the base array of the buffer.
        /// </summary>
        public byte[] Array() => m_array;

        /// <summary>
        /// Constructor for a buffer segment.
        /// </summary>
        public BufferSegment(byte[] array, int offset, int length)
        {
            Memory = new ReadOnlyMemory<byte>(array, offset, length);
            m_array = array;
        }

        /// <summary>
        /// Appends a buffer to the sequence.
        /// </summary>
        public BufferSegment Append(byte[] array, int offset, int length)
        {
            var segment = new BufferSegment(array, offset, length) {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = segment;
            return segment;
        }

        #region Private Fields
        private byte[] m_array;
        #endregion
    }
}
