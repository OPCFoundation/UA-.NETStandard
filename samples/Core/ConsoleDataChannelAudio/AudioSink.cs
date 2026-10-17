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
using System.IO;
using NAudio.Wave;

namespace Quickstarts.ConsoleDataChannelAudio
{
    /// <summary>
    /// Consumes the PCM a data channel delivers.
    /// </summary>
    internal interface IAudioSink : IDisposable
    {
        /// <summary>
        /// What the sink does with the audio, for the console banner.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Accepts one frame of audio.
        /// </summary>
        /// <param name="pcm">Little-endian 16-bit mono samples.</param>
        void Write(ReadOnlySpan<byte> pcm);
    }

    /// <summary>
    /// Chooses the sink that suits the platform.
    /// </summary>
    internal static class AudioSink
    {
        /// <summary>
        /// Creates a sink that plays the audio where the platform can, and
        /// writes it to a WAV file where it cannot.
        /// </summary>
        /// <remarks>
        /// NAudio's output devices wrap WASAPI, WaveOut, DirectSound and ASIO,
        /// all of which are Windows interfaces; NAudio 2.x has no ALSA or
        /// CoreAudio backend. Rather than fail on Linux and macOS, the sample
        /// writes a WAV there, which still demonstrates that the bytes arrived
        /// intact and in order.
        /// </remarks>
        public static IAudioSink Create()
        {
            if (OperatingSystem.IsWindows())
            {
                return new PlaybackAudioSink();
            }

            return new WaveFileAudioSink();
        }
    }

    /// <summary>
    /// Plays the audio through the default output device.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal sealed class PlaybackAudioSink : IAudioSink
    {
        /// <summary>
        /// Creates the sink and opens the output device.
        /// </summary>
        public PlaybackAudioSink()
        {
            var format = new WaveFormat(
                AudioFormat.SampleRate,
                AudioFormat.BitsPerSample,
                AudioFormat.Channels);

            m_buffer = new BufferedWaveProvider(format)
            {
                // Two seconds is far more than the stream needs and is there to
                // absorb a scheduling hiccup, not to hide a slow consumer.
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = true
            };

            m_output = new WaveOutEvent();
            m_output.Init(m_buffer);
            m_output.Play();
        }

        /// <inheritdoc/>
        public string Description => "playing through the default output device";

        /// <inheritdoc/>
        public void Write(ReadOnlySpan<byte> pcm)
        {
            m_buffer.AddSamples(pcm.ToArray(), 0, pcm.Length);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_output.Dispose();
        }

        private readonly BufferedWaveProvider m_buffer;
        private readonly WaveOutEvent m_output;
    }

    /// <summary>
    /// Writes the audio to a WAV file, for platforms where NAudio cannot play.
    /// </summary>
    internal sealed class WaveFileAudioSink : IAudioSink
    {
        /// <summary>
        /// Creates the sink and opens the file.
        /// </summary>
        /// <remarks>
        /// The file goes in a directory created fresh for this run rather than
        /// at a predictable path under the shared temp directory, and it is
        /// opened with CreateNew. A fixed name in a world-writable directory
        /// can be pre-created as a symlink by any local user, and an open that
        /// truncates would then follow it and overwrite the target.
        /// </remarks>
        public WaveFileAudioSink()
        {
            DirectoryInfo directory = Directory.CreateTempSubdirectory("ConsoleDataChannelAudio");
            m_path = Path.Combine(directory.FullName, "audio.wav");

            var stream = new FileStream(
                m_path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read);

            m_writer = new WaveFileWriter(
                stream,
                new WaveFormat(
                    AudioFormat.SampleRate,
                    AudioFormat.BitsPerSample,
                    AudioFormat.Channels));
        }

        /// <inheritdoc/>
        public string Description => $"writing to {m_path}";

        /// <inheritdoc/>
        public void Write(ReadOnlySpan<byte> pcm)
        {
            m_writer.Write(pcm.ToArray(), 0, pcm.Length);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_writer.Dispose();
        }

        private readonly string m_path;
        private readonly WaveFileWriter m_writer;
    }
}
