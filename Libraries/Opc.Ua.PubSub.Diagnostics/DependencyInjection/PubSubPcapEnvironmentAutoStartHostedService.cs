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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Pcap.DependencyInjection
{
    /// <summary>
    /// <see cref="IHostedService"/> registered by
    /// <c>AddPubSubPcapFromEnvironment</c> when
    /// <c>OPCUA_PUBSUB_PCAP_FILE</c> is set: it starts an in-process PubSub
    /// capture on host start and flushes the captured frames to the
    /// configured pcap / pcapng file on host stop.
    /// </summary>
    internal sealed class PubSubPcapEnvironmentAutoStartHostedService
        : IHostedService, IAsyncDisposable
    {
        public PubSubPcapEnvironmentAutoStartHostedService(
            IPubSubCaptureRegistry registry,
            PubSubPcapEnvironmentOptions options,
            ILoggerFactory? loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(options);
            m_options = options;
            m_loggerFactory = loggerFactory;
            m_logger = loggerFactory?.CreateLogger<PubSubPcapEnvironmentAutoStartHostedService>();
            m_manager = new PubSubCaptureSessionManager(registry, loggerFactory);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!m_options.IsEnabled)
            {
                return;
            }
            m_source = await m_manager.StartAsync(cancellationToken).ConfigureAwait(false);
            m_logger?.LogInformation(
                "PubSub capture auto-started; frames will be written to {PcapFile} on shutdown.",
                m_options.PcapFilePath);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            IPubSubCaptureSource? source = m_source;
            m_source = null;
            if (source is null || m_options.PcapFilePath is null)
            {
                return;
            }
            await m_manager.StopAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var writer = new PubSubPcapWriter();
                bool pcapNg = m_options.PcapFilePath.EndsWith(
                    ".pcapng", StringComparison.OrdinalIgnoreCase);
                long written = pcapNg
                    ? await writer.WritePcapNgAsync(
                        source.ReadCapturedFramesAsync(null, cancellationToken),
                        m_options.PcapFilePath,
                        cancellationToken).ConfigureAwait(false)
                    : await writer.WritePcapAsync(
                        source.ReadCapturedFramesAsync(null, cancellationToken),
                        m_options.PcapFilePath,
                        cancellationToken).ConfigureAwait(false);
                m_logger?.LogInformation(
                    "Wrote {Count} PubSub frames to {PcapFile}.",
                    written,
                    m_options.PcapFilePath);
            }
            catch (Exception ex)
            {
                m_logger?.LogError(ex,
                    "Failed to write PubSub capture to {PcapFile}.",
                    m_options.PcapFilePath);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await m_manager.DisposeAsync().ConfigureAwait(false);
        }

        private readonly PubSubPcapEnvironmentOptions m_options;
        private readonly ILoggerFactory? m_loggerFactory;
        private readonly ILogger? m_logger;
        private readonly PubSubCaptureSessionManager m_manager;
        private IPubSubCaptureSource? m_source;
    }
}
