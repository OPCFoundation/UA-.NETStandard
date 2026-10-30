/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Opc.Ua;

namespace Quickstarts.ConsoleDataChannelAudio
{
    /// <summary>
    /// Consumes borrowed complete PCM messages without retaining the flow's receive buffers.
    /// </summary>
    internal interface IAudioSink : IAsyncDisposable
    {
        /// <summary>
        /// Gets the selected output for the console banner.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Consumes a payload before its DataChannelMessage owner is disposed.
        /// </summary>
        ValueTask WriteAsync(ByteString pcm, CancellationToken cancellationToken);

        /// <summary>
        /// Drains playback or flushes the finite output before the sink is disposed.
        /// </summary>
        ValueTask CompleteAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Selects the requested output explicitly; playback failure never becomes silent headless success.
    /// </summary>
    internal static class AudioSink
    {
        /// <summary>
        /// Opens an exclusively created WAV or the actual Windows device.
        /// </summary>
        public static IAudioSink Create(string? wavePath)
        {
            if (wavePath != null)
            {
                return new WaveFileAudioSink(wavePath);
            }
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("NAudio playback requires Windows. Select --wav explicitly.");
            }
            return new PlaybackAudioSink();
        }
    }

    /// <summary>
    /// Plays the received PCM using NAudio's existing Windows output implementation.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal sealed class PlaybackAudioSink : IAudioSink
    {
        /// <summary>
        /// Opens the default audio device and bounded playback buffer.
        /// </summary>
        public PlaybackAudioSink()
        {
            m_buffer = new BufferedWaveProvider(new WaveFormat(
                AudioFormat.SampleRate, AudioFormat.BitsPerSample, AudioFormat.Channels))
            {
                BufferDuration = TimeSpan.FromSeconds(2),
                DiscardOnBufferOverflow = false
            };
            m_output = new WaveOutEvent();
            try
            {
                m_output.Init(m_buffer);
                m_output.Play();
            }
            catch
            {
                m_output.Dispose();
                throw;
            }
        }

        /// <inheritdoc/>
        public string Description => "Windows default audio device";

        /// <inheritdoc/>
        public ValueTask WriteAsync(ByteString pcm, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            m_buffer.AddSamples(pcm.ToArray(), 0, pcm.Length);
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public async ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            while (m_buffer.BufferedBytes > 0)
            {
                if (m_output.PlaybackState != PlaybackState.Playing)
                {
                    throw new IOException("The output device stopped before the received PCM drained.");
                }
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            m_output.Dispose();
            return ValueTask.CompletedTask;
        }

        private readonly BufferedWaveProvider m_buffer;
        private readonly WaveOutEvent m_output;
    }

    /// <summary>
    /// Writes exactly the received PCM and finalizes a standard RIFF/WAVE header using the existing NAudio helper.
    /// </summary>
    internal sealed class WaveFileAudioSink : IAudioSink
    {
        /// <summary>
        /// Requires a new explicit path; never truncates an existing file or uses a shared temporary directory.
        /// </summary>
        public WaveFileAudioSink(string path)
        {
            m_path = path;
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            try
            {
                m_writer = new WaveFileWriter(stream,
                    new WaveFormat(AudioFormat.SampleRate, AudioFormat.BitsPerSample, AudioFormat.Channels));
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <inheritdoc/>
        public string Description => $"headless WAV: {m_path}";

        /// <inheritdoc/>
        public async ValueTask WriteAsync(ByteString pcm, CancellationToken cancellationToken)
        {
            await m_writer.WriteAsync(pcm.Memory, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            await m_writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_writer.DisposeAsync();
        }

        private readonly string m_path;
        private readonly WaveFileWriter m_writer;
    }
}
