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
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Bindings.Pcap.Audit;
using Opc.Ua.Bindings.Pcap.Bindings;
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.Capture.Sources;
using Opc.Ua.Bindings.Pcap.Formats;
using Opc.Ua.Bindings.Pcap.KeyLog;
using Opc.Ua.Bindings.Pcap.Replay;

namespace Opc.Ua.Bindings.Pcap.DependencyInjection
{
    /// <summary>
    /// Dependency injection registration helpers for the OPC UA
    /// Bindings.Pcap package.
    /// </summary>
    public static class PcapServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the default Pcap capture services AND installs the
        /// Pcap transport channel binding into
        /// <see cref="Opc.Ua.Bindings.TransportBindings.Channels"/>.
        /// After this call every OPC UA client channel created through
        /// <see cref="Opc.Ua.ClientChannelManager"/> uses a capture-aware
        /// socket; the actual recording is gated by the
        /// <see cref="IChannelCaptureRegistry"/> and turned on or off by
        /// a <see cref="CaptureSessionManager"/>.
        /// </summary>
        public static IServiceCollection AddOpcUaBindingsPcap(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            return services.AddOpcUaBindingsPcap(static _ => { });
        }

        /// <summary>
        /// Registers the Pcap capture services with caller-supplied
        /// configuration.
        /// </summary>
        public static IServiceCollection AddOpcUaBindingsPcap(
            this IServiceCollection services,
            Action<PcapOptions> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            var options = new PcapOptions();
            configure(options);

            // Shared registry: the Pcap binding installed below and every
            // CaptureSessionManager created via DI talk to the SAME
            // ChannelCaptureRegistry instance, so the
            // CapturingMessageSocket's single volatile read of
            // CurrentObserver picks up StartAsync / StopAsync writes
            // without any further coordination.
            var registry = new ChannelCaptureRegistry();
            Opc.Ua.Bindings.Pcap.Bindings.PcapBindings.Install(registry);

            services.AddSingleton(options);
            services.AddSingleton<IPcapAuditSink, LoggerPcapAuditSink>();
            if (options.EnableTamperEvidentAudit)
            {
                byte[]? tamperEvidentAuditHmacKey = options.TamperEvidentAuditHmacKey;
                if (tamperEvidentAuditHmacKey is null || tamperEvidentAuditHmacKey.Length != 32)
                {
                    throw new InvalidOperationException(
                        "PcapOptions.EnableTamperEvidentAudit is true but " +
                        "TamperEvidentAuditHmacKey is missing or not 32 bytes.");
                }

                string auditPath = options.TamperEvidentAuditFilePath
                    ?? Path.Combine(options.BaseFolder, "audit.jsonl");
                string? auditDirectory = Path.GetDirectoryName(auditPath);
                if (!string.IsNullOrEmpty(auditDirectory))
                {
                    Directory.CreateDirectory(auditDirectory);
                }

                services.AddSingleton<IPcapAuditSink>(sp =>
                    new HashChainedAuditFileSink(
                        auditPath,
                        tamperEvidentAuditHmacKey,
                        sp.GetService<ILogger<HashChainedAuditFileSink>>()));
            }

            services.TryAddSingleton<IKeyEscrowProvider, DiskKeyEscrowProvider>();
            services.AddSingleton<IChannelCaptureRegistry>(registry);
            services.AddSingleton<ICaptureSourceFactory>(provider =>
                new DefaultCaptureSourceFactory(
                    provider.GetRequiredService<IChannelCaptureRegistry>()));
            services.AddSingleton<CaptureSessionManager>(provider =>
            {
                PcapOptions configuredOptions = provider.GetRequiredService<PcapOptions>();
                ICaptureSourceFactory sourceFactory = provider.GetRequiredService<ICaptureSourceFactory>();
                ILoggerFactory loggerFactory = provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
                return new CaptureSessionManager(
                    sourceFactory,
                    configuredOptions.BaseFolder,
                    loggerFactory,
                    configuredOptions.MaxActiveSessions,
                    provider.GetService<IPcapAuditSink>(),
                    provider.GetService<IKeyEscrowProvider>());
            });

            return services;
        }

        /// <summary>
        /// Adds trace formatters to dependency injection as a singleton
        /// registry.
        /// </summary>
        public static IServiceCollection AddOpcUaBindingsPcapFormatters(
            this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton(TraceFormatterRegistry.CreateDefault());
            return services;
        }

        /// <summary>
        /// Registers pcap replay session services.
        /// </summary>
        public static IServiceCollection AddOpcUaBindingsPcapReplay(
            this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<ReplaySessionManager>();
            return services;
        }
    }

    /// <summary>
    /// Options for OPC UA Pcap binding service registration.
    /// </summary>
    public sealed class PcapOptions
    {
        /// <summary>
        /// Base directory where capture session folders are created.
        /// </summary>
        /// <remarks>
        /// Defaults to a per-user directory under
        /// <see cref="Environment.SpecialFolder.LocalApplicationData"/>:
        /// on Linux this resolves to
        /// <c>~/.local/share/OPCFoundation/opcua-pcap</c>, on Windows
        /// <c>%LOCALAPPDATA%\OPCFoundation\opcua-pcap</c>. The previous
        /// default (<see cref="Path.GetTempPath()"/>) placed artifacts in
        /// a shared, world-readable directory on multi-tenant Unix hosts.
        /// </remarks>
        public string BaseFolder { get; set; } =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OPCFoundation",
                "opcua-pcap");

        /// <summary>
        /// Maximum intended number of concurrent active sessions. Passed
        /// to the <see cref="CaptureSessionManager"/> constructor and
        /// enforced by <see cref="CaptureSessionManager.StartAsync"/>.
        /// </summary>
        public int MaxActiveSessions { get; set; } = CaptureSessionManager.DefaultMaxActiveSessions;

        /// <summary>
        /// Maximum bytes a single pcap capture file may grow to before
        /// the writer rotates to a new file with a numeric suffix
        /// (e.g. <c>capture.pcap</c> → <c>capture.001.pcap</c>). Defaults
        /// to 256 MB. Set to <c>0</c> to disable rotation (unbounded
        /// growth — legacy behavior).
        /// </summary>
        public long MaxBytesPerCapture { get; set; } = 256L * 1024 * 1024;

        /// <summary>
        /// Maximum bytes a single keylog file may grow to before the
        /// writer rotates. Defaults to 8 MB.
        /// </summary>
        public long MaxBytesPerKeylog { get; set; } = 8L * 1024 * 1024;

        /// <summary>
        /// Maximum number of rotated artifact files retained per session.
        /// When exceeded, the oldest rotated file is deleted. The currently-
        /// active writer file is always retained regardless of count.
        /// Defaults to 16. Set to <c>0</c> to disable pruning.
        /// </summary>
        public int MaxArtifactsPerSession { get; set; } = 16;

        /// <summary>
        /// Enables the <c>replay_pcap</c> tool's mock-client mode that
        /// connects to a real OPC UA endpoint and emits frames from a
        /// recorded capture. Off by default because this can be used as
        /// a relay-attack vector if the recorded capture contains
        /// authentication payloads. Combined with
        /// <see cref="AllowedReplayEndpoints"/> for endpoint scoping.
        /// </summary>
        public bool AllowMockClientReplay { get; set; }

        /// <summary>
        /// Allow-list of endpoint hostnames against which the
        /// <c>replay_pcap</c> mock-client mode is permitted to connect.
        /// An empty list (default) blocks all endpoints even when
        /// <see cref="AllowMockClientReplay"/> is true. Each entry is
        /// matched against <see cref="Uri.Host"/> using
        /// <see cref="StringComparison.OrdinalIgnoreCase"/>.
        /// </summary>
        public IReadOnlyList<string> AllowedReplayEndpoints { get; set; } =
            Array.Empty<string>();

        /// <summary>
        ///
        /// Enables the registration of <see cref="HashChainedAuditFileSink"/>
        /// as the audit-event destination. The sink writes a JSONL ledger with
        /// per-line HMAC chaining so tampering is detectable. Off by default
        /// because the chain HMAC key requires operator setup. Set
        /// <see cref="TamperEvidentAuditFilePath"/> and
        /// <see cref="TamperEvidentAuditHmacKey"/> in the same options block.
        ///
        /// </summary>
        public bool EnableTamperEvidentAudit { get; set; }

        /// <summary>
        ///
        /// File path for the tamper-evident audit ledger when
        /// <see cref="EnableTamperEvidentAudit"/> is true. Defaults to
        /// <c>audit.jsonl</c> under <see cref="BaseFolder"/>.
        ///
        /// </summary>
        public string? TamperEvidentAuditFilePath { get; set; }

        /// <summary>
        ///
        /// 32-byte HMAC key for the tamper-evident audit chain. Generate
        /// with <c>System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)</c>
        /// and store securely (e.g., environment variable, Key Vault).
        /// REQUIRED when <see cref="EnableTamperEvidentAudit"/> is true.
        ///
        /// </summary>
        public byte[]? TamperEvidentAuditHmacKey { get; set; }

        /// <summary>
        /// Enables the registration of MCP diagnostic tools that
        /// disclose symmetric channel keys
        /// (<c>dump_keys</c>, <c>decode_pcap_with_keys</c>) or perform
        /// privileged replay operations (<c>replay_pcap</c>). These tools
        /// are <b>off by default</b>; an operator must explicitly opt in
        /// because they expose the keys that protect live OPC UA traffic.
        /// </summary>
        /// <remarks>
        /// Setting this to <c>true</c> must be paired with at least one
        /// of the following controls applied at the MCP transport layer:
        /// authentication (Bearer token or mutual TLS) and audit logging.
        /// See <c>Docs/PacketCapture.md</c> for the full security model.
        /// </remarks>
        public bool EnableDiagnosticsTools { get; set; }
    }
}
