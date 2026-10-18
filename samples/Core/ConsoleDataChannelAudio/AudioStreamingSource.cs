/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using Opc.Ua;

namespace Quickstarts.ConsoleDataChannelAudio
{
    /// <summary>
    /// Supplies complete sample-aligned opaque PCM messages from the existing generated melody.
    /// Network lifecycle and buffer ownership belong to the common flow facade, not this audio source.
    /// </summary>
    internal sealed class AudioStreamingSource
    {
        /// <summary>
        /// Renders the melody once without introducing a media codec or binary fixture.
        /// </summary>
        public AudioStreamingSource(int frameMilliseconds)
        {
            m_pcm = MelodyGenerator.Render();
            m_messageBytes = AudioFormat.BytesForDuration(frameMilliseconds);
        }

        /// <summary>
        /// Returns one complete immutable message, including across a melody loop boundary.
        /// </summary>
        public ByteString GetMessage(int sequence)
        {
            byte[] message = new byte[m_messageBytes];
            int offset = checked((int)((long)sequence * m_messageBytes % m_pcm.Length));
            int written = 0;
            while (written < message.Length)
            {
                int count = Math.Min(message.Length - written, m_pcm.Length - offset);
                m_pcm.Span.Slice(offset, count).CopyTo(message.AsSpan(written));
                written += count;
                offset = (offset + count) % m_pcm.Length;
            }
            return new ByteString(message);
        }

        private readonly ByteString m_pcm;
        private readonly int m_messageBytes;
    }
}
