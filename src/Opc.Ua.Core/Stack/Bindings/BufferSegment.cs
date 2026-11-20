// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
        public byte[] Array()
        {
            return m_array;
        }

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
            var segment = new BufferSegment(array, offset, length)
            {
                RunningIndex = RunningIndex + Memory.Length
            };
            Next = segment;
            return segment;
        }

        private readonly byte[] m_array;
    }
}
