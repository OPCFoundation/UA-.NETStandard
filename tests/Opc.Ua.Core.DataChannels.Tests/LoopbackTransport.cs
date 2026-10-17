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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

namespace Opc.Ua.Core.DataChannels.Tests
{
    /// <summary>
    /// An in-memory transport that carries frames between two
    /// <see cref="DataChannelManager"/> instances, encoding and decoding
    /// each one on the way so the loopback exercises the real codec
    /// rather than passing objects across.
    /// </summary>
    internal sealed class LoopbackTransport : IDataChannelTransport
    {
        public LoopbackTransport(
            BufferManager bufferManager,
            TimeProvider timeProvider,
            int maxFrameBodySize = 4096)
        {
            BufferManager = bufferManager;
            TimeProvider = timeProvider;
            MaxFrameBodySize = maxFrameBodySize;
        }

        /// <summary>
        /// The manager on the far end. Set once both sides exist.
        /// </summary>
        public DataChannelManager? Peer { get; set; }

        /// <summary>
        /// When true, frames are recorded but not delivered, which is how
        /// a test withholds the connection level CREDIT frame.
        /// </summary>
        public bool DropOutbound { get; set; }

        /// <summary>
        /// Every frame handed to the transport, in order. Returns a snapshot:
        /// the scheduler appends from its own thread, so handing out the live
        /// list lets a test enumerating it race the writer.
        /// </summary>
        public IReadOnlyList<DataChannelFrame> Sent
        {
            get
            {
                lock (m_lock)
                {
                    return [.. m_sent];
                }
            }
        }

        /// <summary>
        /// The faults reported against the whole SecureChannel.
        /// </summary>
        public IReadOnlyList<DataChannelFrameError> Faults
        {
            get
            {
                lock (m_lock)
                {
                    return [.. m_faults];
                }
            }
        }

        /// <inheritdoc/>
        public DataChannelFramingMode FramingMode => DataChannelFramingMode.Inline;

        /// <inheritdoc/>
        public int MaxFrameBodySize { get; }

        /// <inheritdoc/>
        public bool HasTransportFlowControl => false;

        /// <inheritdoc/>
        public BufferManager BufferManager { get; }

        /// <inheritdoc/>
        public TimeProvider TimeProvider { get; }

        /// <inheritdoc/>
        public ValueTask SendFrameAsync(DataChannelFrame frame, CancellationToken ct)
        {
            lock (m_lock)
            {
                m_sent.Add(frame);
            }

            if (DropOutbound || Peer == null)
            {
                return default;
            }

            byte[] encoded = new byte[frame.EncodedSize];
            DataChannelFrameCodec.Encode(encoded, frame);

            if (DataChannelFrameCodec.TryDecode(
                encoded,
                0,
                out DataChannelFrame received,
                out DataChannelFrameError error))
            {
                Peer.HandleFrame(received);
            }
            else
            {
                lock (m_lock)
                {
                    m_faults.Add(error);
                }
            }

            return default;
        }

        /// <inheritdoc/>
        public void OnProtocolFault(DataChannelFrameError error)
        {
            lock (m_lock)
            {
                m_faults.Add(error);
            }
        }

        /// <summary>
        /// The number of frames of a given type handed to the transport.
        /// </summary>
        /// <param name="frameType">The type to count.</param>
        public int CountOf(DataChannelFrameType frameType)
        {
            lock (m_lock)
            {
                int count = 0;

                for (int ii = 0; ii < m_sent.Count; ii++)
                {
                    if (m_sent[ii].FrameType == frameType)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private readonly List<DataChannelFrame> m_sent = [];
        private readonly List<DataChannelFrameError> m_faults = [];
        private readonly Lock m_lock = new();
    }
}
