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
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings;
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.Pcap.Capture.Sources;
using Opc.Ua.Pcap.DependencyInjection;
using Opc.Ua.Pcap.KeyLog;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Capture
{
    /// <summary>
    /// Environment-variable driven, non-DI bootstrap for server-side pcap
    /// capture. It mirrors the client-facing
    /// <c>AddPcapFromEnvironment()</c> behaviour for hosts that create their
    /// server through the classic <c>ApplicationInstance</c> path (which has no
    /// <see cref="IServiceProvider"/> to drive the hosted-service auto-start):
    /// a server started without the variables changes nothing, and a server
    /// started with them installs the pcap server listener binding and records
    /// inbound client→server traffic to the folder derived from the variable.
    /// The <b>same</b> variables the client uses apply
    /// (<see cref="PcapEnvironmentVariableNames.OpcuaPcapFile"/> and
    /// <see cref="PcapEnvironmentVariableNames.OpcuaKeyLogFile"/>).
    /// </summary>
    public static class PcapServerCapture
    {
        /// <summary>
        /// Inspects the process environment and, when
        /// <c>OPCUA_PCAP_FILE</c> (or, on its own, <c>OPCUA_KEYLOGFILE</c>) is
        /// set, installs the pcap server listener binding into
        /// <paramref name="serverBindings"/> and starts an in-process server
        /// capture (or a stand-alone key-log observer). Returns an
        /// <see cref="IAsyncDisposable"/> that stops the capture on the host's
        /// shutdown, or <c>null</c> when neither variable is set (a complete
        /// no-op — nothing is installed and no files are written).
        /// </summary>
        /// <remarks>
        /// Call this <b>before</b> the server's listeners open (before
        /// <c>StartAsync</c>) so the <c>opc.tcp</c> listener factory is wrapped.
        /// Anyone who can set these variables can divert OPC UA keys and traffic
        /// to attacker-controlled paths; treat them as a privileged operation
        /// and never enable in production.
        /// </remarks>
        /// <param name="serverBindings">
        /// The server's transport binding registry
        /// (<c>server.Server.TransportBindings</c>).
        /// </param>
        /// <param name="loggerFactory">Optional logger factory.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="serverBindings"/> is <c>null</c>.
        /// </exception>
        public static ValueTask<IAsyncDisposable?> TryStartFromEnvironmentAsync(
            ITransportBindingRegistry serverBindings,
            ILoggerFactory? loggerFactory = null,
            CancellationToken cancellationToken = default)
        {
            return TryStartFromEnvironmentAsync(
                serverBindings,
                static name => Environment.GetEnvironmentVariable(name),
                loggerFactory,
                cancellationToken);
        }

        /// <summary>
        /// Test-friendly overload that resolves the env vars through
        /// <paramref name="getEnvironmentVariable"/> instead of the process
        /// environment.
        /// </summary>
        internal static async ValueTask<IAsyncDisposable?> TryStartFromEnvironmentAsync(
            ITransportBindingRegistry serverBindings,
            Func<string, string?> getEnvironmentVariable,
            ILoggerFactory? loggerFactory,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(serverBindings);
            ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

            PcapEnvironmentSnapshot environment =
                PcapEnvironmentDefaults.ReadFromEnvironment(getEnvironmentVariable);
            if (!environment.HasAny)
            {
                return null;
            }

            ILoggerFactory factory = loggerFactory ?? NullLoggerFactory.Instance;
            ILogger logger = factory.CreateLogger(typeof(PcapServerCapture).FullName!);

            // A single registry is shared by the server binding wrapper and the
            // capture source so a started session observes the wrapped
            // listener's channels.
            var registry = new ChannelCaptureRegistry();
            PcapBindings.InstallServer(serverBindings, registry);

            if (environment.IsKeyLogOnly)
            {
                string keyLogPath = environment.KeyLogFilePath!;
                logger.LogWarning(
                    "Stand-alone OPC UA server key logging is ENABLED via {EnvVar}. " +
                    "Channel symmetric keys will be written to '{KeyLogFilePath}'. " +
                    "Treat this file as a secret; anyone with read access can " +
                    "decrypt recorded OPC UA traffic.",
                    PcapEnvironmentVariableNames.OpcuaKeyLogFile,
                    keyLogPath);

                StandaloneKeyLogObserver observer =
#pragma warning disable CA2000 // ownership transferred to the returned ServerCaptureHandle (disposed on host shutdown)
                    StandaloneKeyLogObserver.Create(
                        keyLogPath,
                        factory.CreateLogger<StandaloneKeyLogObserver>());
#pragma warning restore CA2000
                registry.SetObserver(observer);
                return new ServerCaptureHandle(registry, observer);
            }

            string pcapPath = environment.PcapFilePath!;
            string? envKeyLogPath = environment.KeyLogFilePath;
            string sessionFolder = ResolveSessionFolder(pcapPath);

            if (envKeyLogPath is null)
            {
                logger.LogWarning(
                    "OPC UA server pcap auto-capture is ENABLED via {PcapEnvVar}. " +
                    "Frames will be written to '{PcapFilePath}'. " +
                    "Treat the resulting files as secrets; they include the " +
                    "channel keys necessary to decrypt recorded traffic.",
                    PcapEnvironmentVariableNames.OpcuaPcapFile,
                    pcapPath);
            }
            else
            {
                logger.LogWarning(
                    "OPC UA server pcap auto-capture is ENABLED via {PcapEnvVar} (frames at " +
                    "'{PcapFilePath}') and {KeyLogEnvVar} (keys at '{KeyLogFilePath}'). " +
                    "Treat both files as secrets.",
                    PcapEnvironmentVariableNames.OpcuaPcapFile,
                    pcapPath,
                    PcapEnvironmentVariableNames.OpcuaKeyLogFile,
                    envKeyLogPath);
            }

            var manager =
#pragma warning disable CA2000 // ownership transferred to ServerCaptureHandle on success, disposed in the catch on failure
                new CaptureSessionManager(
                    new DefaultCaptureSourceFactory(registry),
                    sessionFolder,
                    factory);
#pragma warning restore CA2000

            var request = new StartCaptureRequest
            {
                Source = CaptureSourceKind.InProcessServer,
                PcapFilePath = pcapPath,
                KeyLogFilePath = envKeyLogPath,
                SessionFolder = sessionFolder,
                MaxBytes = long.MaxValue,
                MaxDurationSeconds = int.MaxValue
            };

            try
            {
                CaptureSession session = await manager
                    .StartAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                return new ServerCaptureHandle(manager, session.Id);
            }
            catch
            {
                await manager.DisposeAsync().ConfigureAwait(false);
                throw;
            }
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

        /// <summary>
        /// Stops the env-var-driven server capture (or clears the stand-alone
        /// key-log observer) when the host shuts down.
        /// </summary>
        private sealed class ServerCaptureHandle : IAsyncDisposable
        {
            private readonly CaptureSessionManager? m_manager;
            private readonly string? m_sessionId;
            private readonly ChannelCaptureRegistry? m_registry;
            private readonly StandaloneKeyLogObserver? m_observer;
            private int m_disposed;

            public ServerCaptureHandle(CaptureSessionManager manager, string sessionId)
            {
                m_manager = manager;
                m_sessionId = sessionId;
            }

            public ServerCaptureHandle(
                ChannelCaptureRegistry registry,
                StandaloneKeyLogObserver observer)
            {
                m_registry = registry;
                m_observer = observer;
            }

            public async ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return;
                }

                if (m_manager is not null && m_sessionId is not null)
                {
                    try
                    {
                        await m_manager.StopAsync(m_sessionId, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort — a failed stop must not mask shutdown.
                    }
                    await m_manager.DisposeAsync().ConfigureAwait(false);
                }

                if (m_observer is not null)
                {
                    m_registry?.TryClearObserver(m_observer);
                    await m_observer.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
