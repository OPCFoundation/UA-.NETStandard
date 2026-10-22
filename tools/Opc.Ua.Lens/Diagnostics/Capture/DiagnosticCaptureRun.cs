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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.Models;
using Opc.Ua.PubSub.Pcap;

namespace UaLens.Diagnostics.Capture
{
    /// <summary>
    /// Internal seam between the shared Core/PubSub capture managers and the desktop recording lifecycle.
    /// </summary>
    internal interface IDiagnosticCaptureRun : IAsyncDisposable
    {
        /// <summary>
        /// Gets the exact pre-copy admission budget.
        /// </summary>
        DiagnosticCaptureBudget Budget { get; }

        /// <summary>
        /// Activates only the selected workspace-owned transport observer.
        /// </summary>
        ValueTask StartAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Detaches and drains the observer, returning its finalized private PCAP path.
        /// </summary>
        ValueTask<string> StopAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Uses the shared Core capture manager with an explicit native-safe client-only source factory.
    /// </summary>
    internal sealed class CoreDiagnosticCaptureRun : IDiagnosticCaptureRun
    {
        /// <summary>
        /// Creates the run after capture consent; its private folder is not created at application registration.
        /// </summary>
        public CoreDiagnosticCaptureRun(
            IChannelCaptureRegistry registry, DiagnosticCaptureLimits limits, string folder,
            ITelemetryContext telemetry, TimeProvider? clock = null)
        {
            Budget = new DiagnosticCaptureBudget(limits, clock);
            m_limits = limits;
            m_manager = new CaptureSessionManager(
                new ClientSourceFactory(new DiagnosticClientCaptureRegistry(registry, Budget)),
                folder, telemetry.LoggerFactory, maxActiveSessions: 1);
        }

        /// <inheritdoc/>
        public DiagnosticCaptureBudget Budget { get; }

        /// <inheritdoc/>
        public async ValueTask StartAsync(CancellationToken cancellationToken)
        {
            m_session = await m_manager.StartAsync(new StartCaptureRequest
            {
                Source = CaptureSourceKind.InProcessClient,
                MaxBytes = m_limits.MaxPayloadBytes,
                MaxFrames = m_limits.MaxFrames,
                MaxDurationSeconds = m_limits.DurationSeconds
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask<string> StopAsync(CancellationToken cancellationToken)
        {
            CaptureSession session = m_session ??
                throw new InvalidOperationException("The client capture did not start.");
            await m_manager.StopAsync(session.Id, cancellationToken).ConfigureAwait(false);
            return session.Source.GetRawPcapFilePath() ??
                throw new IOException("The client capture produced no PCAP artifact.");
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_manager.DisposeAsync();
        }

        private readonly CaptureSessionManager m_manager;
        private readonly DiagnosticCaptureLimits m_limits;
        private CaptureSession? m_session;

        private sealed class ClientSourceFactory(IChannelCaptureRegistry registry) : ICaptureSourceFactory
        {
            public ICaptureSource Create(CaptureSourceKind kind, string sessionFolder, ILoggerFactory loggerFactory)
            {
                if (kind != CaptureSourceKind.InProcessClient)
                {
                    throw new NotSupportedException("This source captures only workspace-owned UA-TCP client traffic.");
                }
                return new InProcessClientCaptureSource(registry, loggerFactory);
            }
        }
    }

    /// <summary>
    /// Uses the shared PubSub manager and PCAP writer without acquiring a broker, NIC, key provider or UA session.
    /// </summary>
    internal sealed class PubSubDiagnosticCaptureRun : IDiagnosticCaptureRun
    {
        /// <summary>
        /// Creates a bounded observer for one explicitly selected UDP/UADP runtime.
        /// </summary>
        public PubSubDiagnosticCaptureRun(
            IPubSubCaptureRegistry registry, DiagnosticCaptureLimits limits, string folder,
            ITelemetryContext telemetry, TimeProvider? clock = null)
        {
            Budget = new DiagnosticCaptureBudget(limits, clock);
            m_manager = new PubSubCaptureSessionManager(
                new DiagnosticPubSubCaptureRegistry(registry, Budget), telemetry.LoggerFactory);
            m_path = Path.Combine(folder, "capture.pcap");
        }

        /// <inheritdoc/>
        public DiagnosticCaptureBudget Budget { get; }

        /// <inheritdoc/>
        public async ValueTask StartAsync(CancellationToken cancellationToken)
        {
            m_source = await m_manager.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public ValueTask<string> StopAsync(CancellationToken cancellationToken)
        {
            lock (m_gate)
            {
                m_completion ??= CompleteAsync(cancellationToken);
                return new ValueTask<string>(m_completion);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (m_completion is { } completion)
                {
                    await completion.ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (m_source is { } source)
                    {
                        await source.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await m_manager.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private async Task<string> CompleteAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            IPubSubCaptureSource source = m_source ??
                throw new InvalidOperationException("The PubSub capture did not start.");
            await m_manager.StopAsync(cancellationToken).ConfigureAwait(false);
            var writer = new PubSubPcapWriter();
            long count = await writer.WritePcapAsync(
                source.ReadCapturedFramesAsync(null, cancellationToken), m_path, cancellationToken)
                .ConfigureAwait(false);
            if (count != Budget.Snapshot().Frames)
            {
                throw new IOException("The PCAP writer did not persist every admitted PubSub datagram.");
            }
            return m_path;
        }

        private readonly Lock m_gate = new();
        private readonly PubSubCaptureSessionManager m_manager;
        private readonly string m_path;
        private IPubSubCaptureSource? m_source;
        private Task<string>? m_completion;
    }
}
