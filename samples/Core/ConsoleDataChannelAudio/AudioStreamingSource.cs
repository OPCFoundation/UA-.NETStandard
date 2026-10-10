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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Bindings;

namespace Quickstarts.ConsoleDataChannelAudio
{
    /// <summary>
    /// A data channel source that plays a canned melody on repeat.
    /// </summary>
    /// <remarks>
    /// The source writes in real time rather than as fast as the channel will
    /// take it: a media source is paced by its own clock, and writing faster
    /// would only fill the receiver's buffer and add latency. Credit still
    /// bounds it if the consumer falls behind, and the channel reports the
    /// stall through its diagnostics rather than growing without limit.
    /// </remarks>
    internal sealed class AudioStreamingSource : IDataChannelSource, IDisposable
    {
        /// <summary>
        /// Creates the source.
        /// </summary>
        /// <param name="frameMilliseconds">How much audio one frame carries.</param>
        public AudioStreamingSource(int frameMilliseconds)
        {
            m_pcm = MelodyGenerator.Render();
            m_frameBytes = AudioFormat.BytesForDuration(frameMilliseconds);
            m_frameMilliseconds = frameMilliseconds;

            SourceNodeId = new NodeId("Speaker1", 1);
            Capabilities = new DataChannelSourceCapabilities
            {
                Direction = DataChannelDirection.SourceToSink,
                SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
                ContentType = AudioFormat.ContentType,
                MaxFrameSize = (uint)m_frameBytes,
                MaxChannels = 1,
                Priority = 1
            };
        }

        /// <summary>
        /// The Node a Client opens the channel on.
        /// </summary>
        public NodeId SourceNodeId { get; }

        /// <inheritdoc/>
        public NodeId NodeId => SourceNodeId;

        /// <inheritdoc/>
        public DataChannelSourceCapabilities Capabilities { get; }

        /// <inheritdoc/>
        public int ActiveChannelCount => m_channel == null ? 0 : 1;

        /// <summary>
        /// The length of the rendered melody, for the console banner.
        /// </summary>
        public TimeSpan LoopDuration
            => TimeSpan.FromSeconds(
                (double)m_pcm.Length / (AudioFormat.SampleRate * AudioFormat.BytesPerSample));

        /// <inheritdoc/>
        public void OnChannelOpened(DataChannel channel)
        {
            m_channel = channel;
            m_stop = new CancellationTokenSource();
            m_pump = Task.Run(() => PumpAsync(channel, m_stop.Token));
        }

        /// <inheritdoc/>
        public void OnChannelClosed(DataChannel channel, StatusCode reason)
        {
            m_channel = null;
            m_stop?.Cancel();
        }

        /// <summary>
        /// Stops the pump and waits for it, so the sample shuts down cleanly.
        /// </summary>
        public async Task StopAsync()
        {
            m_stop?.Cancel();

            if (m_pump != null)
            {
                await m_pump.ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_stop?.Dispose();
            m_stop = null;
        }

        private async Task PumpAsync(DataChannel channel, CancellationToken ct)
        {
            int offset = 0;
            var next = DateTime.UtcNow;

            try
            {
                while (!ct.IsCancellationRequested &&
                    channel.State is DataChannelState.Open or DataChannelState.Opening)
                {
                    if (channel.State == DataChannelState.Opening)
                    {
                        await Task.Delay(5, ct).ConfigureAwait(false);
                        continue;
                    }

                    int count = Math.Min(m_frameBytes, m_pcm.Length - offset);

                    // MessageStart and MessageEnd on every frame: each one is a
                    // self contained unit of audio, so a sink can play what it
                    // has without waiting for a boundary.
                    channel.Write(
                        m_pcm.AsSpan(offset, count),
                        DataChannelFrameFlags.MessageStart | DataChannelFrameFlags.MessageEnd);

                    offset += count;

                    if (offset >= m_pcm.Length)
                    {
                        offset = 0;
                    }

                    // Paced against a fixed schedule rather than by sleeping for
                    // the frame duration, so the send rate does not drift by the
                    // time each iteration takes.
                    next = next.AddMilliseconds(m_frameMilliseconds);
                    TimeSpan delay = next - DateTime.UtcNow;

                    if (delay > TimeSpan.Zero)
                    {
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        next = DateTime.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
            catch (ServiceResultException e)
            {
                // The channel closed underneath the pump, which is how a
                // CloseDataChannel while writing surfaces. Anything else is
                // worth seeing rather than losing in an unobserved task.
                if (e.StatusCode != StatusCodes.BadDataChannelClosed)
                {
                    Console.Error.WriteLine($"audio pump stopped: {e.Message}");
                }
            }
#pragma warning disable CA1031 // A sample must not die silently.
            catch (Exception e)
#pragma warning restore CA1031
            {
                Console.Error.WriteLine($"audio pump failed: {e}");
            }
        }

        private readonly byte[] m_pcm;
        private readonly int m_frameBytes;
        private readonly int m_frameMilliseconds;
        private DataChannel? m_channel;
        private CancellationTokenSource? m_stop;
        private Task? m_pump;
    }
}
