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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings;

namespace Opc.Ua.Bindings.Pcap.KeyLog
{
    /// <summary>
    /// Stand-alone <see cref="IFrameCaptureSink"/> observer that writes
    /// only <see cref="ChannelKeyMaterial"/> snapshots (no frames) to a
    /// single keylog file. Used by the env-var driven
    /// <c>AddOpcUaBindingsPcapFromEnvironment</c> registration when the
    /// caller asks for key-logging without packet capture
    /// (SSLKEYLOGFILE-style behaviour).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Frame callbacks (<see cref="IFrameCaptureSink.OnFrameSent"/> and
    /// <see cref="IFrameCaptureSink.OnFrameReceived"/>) are no-ops so
    /// the channel send / receive hot path pays only the cost of a
    /// single virtual call when a stand-alone keylog is active.
    /// </para>
    /// <para>
    /// Token activations are pushed through a bounded
    /// <see cref="System.Threading.Channels.Channel{T}"/> to a single
    /// background writer task; the
    /// <see cref="IFrameCaptureSink.OnTokenActivated"/> callback never
    /// blocks on disk I/O. Overflow uses
    /// <see cref="System.Threading.Channels.BoundedChannelFullMode.DropOldest"/>
    /// so the observer degrades gracefully under sustained load.
    /// </para>
    /// </remarks>
    internal sealed class StandaloneKeyLogObserver : IFrameCaptureSink, IAsyncDisposable
    {
        private const int kQueueCapacity = 1024;

        private readonly IKeyLogWriter m_writer;
        private readonly ILogger m_logger;
        private readonly Channel<ChannelKeyMaterial> m_queue;
        private readonly Task m_workerTask;
        private int m_disposed;

        /// <summary>
        /// Constructs a new stand-alone keylog observer that owns the
        /// supplied <see cref="IKeyLogWriter"/>. The writer is disposed
        /// when this observer is disposed.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="writer"/> is <c>null</c>.
        /// </exception>
        public StandaloneKeyLogObserver(IKeyLogWriter writer, ILogger? logger = null)
        {
            ArgumentNullException.ThrowIfNull(writer);
            m_writer = writer;
            m_logger = logger ?? NullLogger.Instance;
            m_queue = Channel.CreateBounded<ChannelKeyMaterial>(
                new BoundedChannelOptions(kQueueCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.DropOldest
                });
            m_workerTask = Task.Run(RunQueueWorkerAsync, CancellationToken.None);
        }

        /// <summary>
        /// Path of the keylog file being written.
        /// </summary>
        public string FilePath => m_writer.FilePath;

        /// <summary>
        /// Constructs a <see cref="StandaloneKeyLogObserver"/> by
        /// selecting an <see cref="IKeyLogWriter"/> implementation based
        /// on the file extension of <paramref name="keyLogFilePath"/>.
        /// <c>.txt</c> selects the NSS-style
        /// <see cref="UaKeyLogTextWriter"/>; everything else
        /// (including <c>.json</c> and <c>.uakeys.json</c>) selects the
        /// JSON-lines <see cref="UaKeyLogJsonWriter"/>.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="keyLogFilePath"/> is null or whitespace.
        /// </exception>
        public static StandaloneKeyLogObserver Create(
            string keyLogFilePath,
            ILogger? logger = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keyLogFilePath);

            string? directory = Path.GetDirectoryName(keyLogFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // CA2000: ownership of the writer transfers to the
            // StandaloneKeyLogObserver constructor; the observer disposes
            // the writer in its DisposeAsync. The constructor performs
            // only a field assignment, a Channel.CreateBounded with valid
            // options, and a Task.Run, none of which throw under normal
            // operation, so the analyzer's "before all references" branch
            // is unreachable in practice.
#pragma warning disable CA2000
            IKeyLogWriter writer = IsTextFormat(keyLogFilePath)
                ? new UaKeyLogTextWriter(keyLogFilePath)
                : new UaKeyLogJsonWriter(keyLogFilePath);
#pragma warning restore CA2000
            return new StandaloneKeyLogObserver(writer, logger);
        }

        /// <inheritdoc/>
        void IFrameCaptureSink.OnFrameSent(uint channelId, ReadOnlySpan<byte> chunk)
        {
        }

        /// <inheritdoc/>
        void IFrameCaptureSink.OnFrameReceived(uint channelId, ReadOnlySpan<byte> chunk)
        {
        }

        /// <inheritdoc/>
        void IFrameCaptureSink.OnTokenActivated(
            uint channelId,
            ChannelToken currentToken,
            ChannelToken? previousToken)
        {
            if (Volatile.Read(ref m_disposed) != 0 || currentToken is null)
            {
                return;
            }

            ChannelKeyMaterial material;
            try
            {
                // CA2000: ownership of ChannelKeyMaterial transfers to the
                // queue worker which disposes it after the write completes.
#pragma warning disable CA2000
                material = ChannelKeyMaterial.From(currentToken);
#pragma warning restore CA2000
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(
                    ex,
                    "Failed to snapshot channel token material for stand-alone keylog.");
                return;
            }

            if (!m_queue.Writer.TryWrite(material))
            {
                material.Dispose();
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            m_queue.Writer.TryComplete();
            try
            {
                await m_workerTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                m_logger.LogWarning(
                    ex,
                    "Stand-alone keylog observer worker terminated with an exception during dispose.");
            }

            await m_writer.DisposeAsync().ConfigureAwait(false);
        }

        private static bool IsTextFormat(string keyLogFilePath)
        {
            string extension = Path.GetExtension(keyLogFilePath);
            return string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase);
        }

        private async Task RunQueueWorkerAsync()
        {
            try
            {
                while (await m_queue.Reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (m_queue.Reader.TryRead(out ChannelKeyMaterial? material))
                    {
                        try
                        {
                            await m_writer.AppendAsync(material, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            m_logger.LogWarning(
                                ex,
                                "Failed to persist key material to stand-alone keylog at '{Path}'.",
                                m_writer.FilePath);
                        }
                        finally
                        {
                            material.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(
                    ex,
                    "Stand-alone keylog observer worker terminated unexpectedly.");
            }
        }
    }
}
