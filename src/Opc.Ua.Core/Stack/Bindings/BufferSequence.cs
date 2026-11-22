// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        public BufferSequence(
            BufferManager bufferManager,
            string owner,
            BufferSegment firstSegment,
            ReadOnlySequence<byte> sequence)
        {
            m_bufferManager = bufferManager;
            m_owner = owner;
            m_firstSegment = firstSegment;
            Sequence = sequence;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            BufferSegment? segment = m_firstSegment;
            while (segment != null)
            {
                m_bufferManager.ReturnBuffer(segment.Array(), m_owner);
                segment = (BufferSegment?)segment.Next;
            }
            Sequence = ReadOnlySequence<byte>.Empty;
            m_firstSegment = null;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Returns a sequence which can be used to access the buffers.
        /// </summary>
        public ReadOnlySequence<byte> Sequence { get; private set; }

        private readonly BufferManager m_bufferManager;
        private BufferSegment? m_firstSegment;
        private readonly string m_owner;
    }
}
