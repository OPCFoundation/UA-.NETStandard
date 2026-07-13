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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.DependencyInjection
{
    /// <summary>
    /// <see cref="IHostedService"/> registered by
    /// <c>AddPcapFromEnvironment</c> that materializes the
    /// env-var driven capture / key-log behaviour at host start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The variable values are taken from a snapshot resolved during
    /// service registration; changing the env vars at runtime has no
    /// effect on this instance.
    /// </para>
    /// <para>
    /// Behaviour:
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="PcapEnvironmentVariableNames.OpcuaPcapFile"/> set:
    ///     calls <see cref="CaptureSessionManager.StartAsync"/> with the
    ///     literal pcap path (and literal keylog path when also set);
    ///     the resulting capture observer takes the registry slot.
    ///   </description></item>
    ///   <item><description>
    ///     Only <see cref="PcapEnvironmentVariableNames.OpcuaKeyLogFile"/>
    ///     set: installs a <see cref="StandaloneKeyLogObserver"/>
    ///     directly into <see cref="IChannelCaptureRegistry"/> without
    ///     starting a capture session.
    ///   </description></item>
    ///   <item><description>
    ///     Neither set: no-op.
    ///   </description></item>
    /// </list>
    /// In all set cases a Warning-level log line names the env var(s)
    /// consumed and the resolved path(s); the values themselves are
    /// never logged as anything other than the literal path.
    /// </para>
    /// </remarks>
    internal sealed class PcapEnvironmentAutoStartHostedService : IHostedService, IAsyncDisposable
    {
        private readonly PcapEnvironmentSnapshot m_environment;
        private readonly CaptureSessionManager m_sessionManager;
        private readonly IChannelCaptureRegistry m_registry;
        private readonly ILogger<PcapEnvironmentAutoStartHostedService> m_logger;
        private readonly ILoggerFactory m_loggerFactory;

        private string? m_capturedSessionId;
        // CA2213: m_keyLogObserver is owned by this service and is disposed in
        // StopAsync (which DisposeAsync forwards to). The analyzer cannot trace
        // the Interlocked.Exchange + await pattern across StopAsync.
#pragma warning disable CA2213
        private StandaloneKeyLogObserver? m_keyLogObserver;
#pragma warning restore CA2213
        private int m_disposed;

        /// <summary>
        /// Constructs the hosted service. Resolved by the DI container
        /// when <c>AddPcapFromEnvironment</c> registers
        /// it as an <see cref="IHostedService"/>.
        /// </summary>
        public PcapEnvironmentAutoStartHostedService(
            PcapEnvironmentSnapshot environment,
            CaptureSessionManager sessionManager,
            IChannelCaptureRegistry registry,
            ILoggerFactory? loggerFactory = null)
        {
            ArgumentNullException.ThrowIfNull(sessionManager);
            ArgumentNullException.ThrowIfNull(registry);
            m_environment = environment;
            m_sessionManager = sessionManager;
            m_registry = registry;
            m_loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            m_logger = m_loggerFactory.CreateLogger<PcapEnvironmentAutoStartHostedService>();
        }

        /// <inheritdoc/>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (!m_environment.HasAny)
            {
                return;
            }

            if (m_environment.IsKeyLogOnly)
            {
                await StartStandaloneKeyLogAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            await StartAutoCaptureAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            string? capturedSessionId = Interlocked.Exchange(ref m_capturedSessionId, null);
            if (capturedSessionId is not null)
            {
                try
                {
                    await m_sessionManager.StopAsync(capturedSessionId, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    m_logger.LogWarning(
                        ex,
                        "Failed to stop env-var auto-started capture session '{SessionId}'.",
                        capturedSessionId);
                }
            }

            StandaloneKeyLogObserver? keyLogObserver = Interlocked.Exchange(
                ref m_keyLogObserver,
                null);
            if (keyLogObserver is not null)
            {
                m_registry.TryClearObserver(keyLogObserver);
                await keyLogObserver.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        private async Task StartStandaloneKeyLogAsync(CancellationToken cancellationToken)
        {
            string keyLogPath = m_environment.KeyLogFilePath!;
            m_logger.LogWarning(
                "Stand-alone OPC UA key logging is ENABLED via {EnvVar}. " +
                "Channel symmetric keys will be written to '{KeyLogFilePath}'. " +
                "Treat this file as a secret; anyone with read access can " +
                "decrypt recorded OPC UA traffic.",
                PcapEnvironmentVariableNames.OpcuaKeyLogFile,
                keyLogPath);

            var observer = StandaloneKeyLogObserver.Create(
                keyLogPath,
                m_loggerFactory.CreateLogger<StandaloneKeyLogObserver>());

            IFrameCaptureSink? previous = m_registry.SetObserver(observer);
            if (previous is not null)
            {
                m_logger.LogWarning(
                    "An IFrameCaptureSink observer was already installed in the " +
                    "registry; it has been replaced by the env-var driven " +
                    "stand-alone keylog observer.");
            }
            m_keyLogObserver = observer;
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private async Task StartAutoCaptureAsync(CancellationToken cancellationToken)
        {
            string pcapPath = m_environment.PcapFilePath!;
            string? keyLogPath = m_environment.KeyLogFilePath;
            string sessionFolder = ResolveSessionFolder(pcapPath);

            if (keyLogPath is null)
            {
                m_logger.LogWarning(
                    "OPC UA pcap auto-capture is ENABLED via {PcapEnvVar}. " +
                    "Frames will be written to '{PcapFilePath}'. " +
                    "Treat the resulting files as secrets; they include the " +
                    "channel keys necessary to decrypt recorded traffic.",
                    PcapEnvironmentVariableNames.OpcuaPcapFile,
                    pcapPath);
            }
            else
            {
                m_logger.LogWarning(
                    "OPC UA pcap auto-capture is ENABLED via {PcapEnvVar} (frames at " +
                    "'{PcapFilePath}') and {KeyLogEnvVar} (keys at '{KeyLogFilePath}'). " +
                    "Treat both files as secrets.",
                    PcapEnvironmentVariableNames.OpcuaPcapFile,
                    pcapPath,
                    PcapEnvironmentVariableNames.OpcuaKeyLogFile,
                    keyLogPath);
            }

            var request = new StartCaptureRequest
            {
                Source = CaptureSourceKind.InProcessClient,
                PcapFilePath = pcapPath,
                KeyLogFilePath = keyLogPath,
                SessionFolder = sessionFolder,
                MaxBytes = long.MaxValue,
                MaxDurationSeconds = int.MaxValue
            };

            CaptureSession session = await m_sessionManager
                .StartAsync(request, cancellationToken)
                .ConfigureAwait(false);
            m_capturedSessionId = session.Id;
        }

        private static string ResolveSessionFolder(string pcapFilePath)
        {
            string fullPath = Path.GetFullPath(pcapFilePath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException(
                    $"Cannot resolve a parent directory for the pcap path '{pcapFilePath}'.");
            }
            return directory;
        }
    }
}
