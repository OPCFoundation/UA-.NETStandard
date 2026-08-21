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
using System.Collections.Generic;

namespace Quickstarts.ConsoleDataChannelAudio
{
    /// <summary>
    /// The PCM format the sample streams.
    /// </summary>
    /// <remarks>
    /// Signed 16-bit little-endian mono, which is what <c>audio/L16</c> names
    /// in the IANA registry and what the data channel advertises as its
    /// ContentType. A data channel is content agnostic: the payload is opaque
    /// octets and the media type is what tells the sink how to read them.
    /// </remarks>
    internal static class AudioFormat
    {
        /// <summary>
        /// Samples per second.
        /// </summary>
        public const int SampleRate = 44100;

        /// <summary>
        /// Bits in one sample.
        /// </summary>
        public const int BitsPerSample = 16;

        /// <summary>
        /// Channels in the stream.
        /// </summary>
        public const int Channels = 1;

        /// <summary>
        /// Bytes in one sample frame.
        /// </summary>
        public const int BytesPerSample = BitsPerSample / 8 * Channels;

        /// <summary>
        /// The IANA media type the data channel advertises.
        /// </summary>
        public const string ContentType = "audio/L16";

        /// <summary>
        /// The number of bytes that carry a given duration of audio.
        /// </summary>
        /// <param name="milliseconds">The duration.</param>
        public static int BytesForDuration(int milliseconds)
        {
            return SampleRate * BytesPerSample * milliseconds / 1000;
        }
    }

    /// <summary>
    /// Renders a short melody to PCM once, so the server has something to loop
    /// without a binary asset in the repository.
    /// </summary>
    /// <remarks>
    /// The waveform is deliberately plain: a sine at the note's frequency with
    /// a short attack and release so consecutive notes do not click, mixed with
    /// a quieter octave above so it is audibly musical rather than a test tone.
    /// What matters for the sample is that it is a continuous, latency
    /// sensitive stream of opaque bytes, not that it sounds good.
    /// </remarks>
    internal static class MelodyGenerator
    {
        /// <summary>
        /// Renders the melody as little-endian PCM.
        /// </summary>
        public static byte[] Render()
        {
            var samples = new List<short>();

            foreach ((double frequency, int milliseconds) in s_melody)
            {
                AppendNote(samples, frequency, milliseconds);
            }

            byte[] pcm = new byte[samples.Count * sizeof(short)];

            for (int ii = 0; ii < samples.Count; ii++)
            {
                pcm[(ii * 2) + 0] = (byte)(samples[ii] & 0xFF);
                pcm[(ii * 2) + 1] = (byte)((samples[ii] >> 8) & 0xFF);
            }

            return pcm;
        }

        private static void AppendNote(List<short> samples, double frequency, int milliseconds)
        {
            int count = AudioFormat.SampleRate * milliseconds / 1000;

            // A rest carries silence rather than nothing, so the loop keeps a
            // constant sample rate and the sink's clock stays honest.
            if (frequency <= 0)
            {
                for (int ii = 0; ii < count; ii++)
                {
                    samples.Add(0);
                }

                return;
            }

            int envelope = Math.Min(count / 8, AudioFormat.SampleRate / 100);

            for (int ii = 0; ii < count; ii++)
            {
                double t = (double)ii / AudioFormat.SampleRate;
                double value =
                    Math.Sin(2.0 * Math.PI * frequency * t) +
                    (0.35 * Math.Sin(2.0 * Math.PI * frequency * 2.0 * t));

                double gain = 1.0;

                if (envelope > 0)
                {
                    if (ii < envelope)
                    {
                        gain = (double)ii / envelope;
                    }
                    else if (ii > count - envelope)
                    {
                        gain = (double)(count - ii) / envelope;
                    }
                }

                samples.Add((short)(value * gain * 0.28 * short.MaxValue));
            }
        }

        // Frequencies in Hz, durations in milliseconds. Zero is a rest.
        private static readonly (double Frequency, int Milliseconds)[] s_melody =
        [
            (587.33, 260),
            (587.33, 260),
            (880.00, 260),
            (880.00, 260),
            (987.77, 260),
            (987.77, 260),
            (880.00, 520),
            (783.99, 260),
            (783.99, 260),
            (739.99, 260),
            (739.99, 260),
            (659.25, 260),
            (659.25, 260),
            (587.33, 520),
            (0, 240)
        ];
    }
}
