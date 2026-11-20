// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Opc.Ua.Pcap.Bindings;
using Opc.Ua.Pcap.Models;

namespace Opc.Ua.Pcap.Capture.Sources
{
    /// <summary>
    /// Default capture source factory for the built-in pcap capture sources.
    /// </summary>
    public sealed class DefaultCaptureSourceFactory : ICaptureSourceFactory
    {
        private readonly IChannelCaptureRegistry m_registry;

        /// <summary>
        /// Constructs a factory that shares the supplied
        /// <see cref="IChannelCaptureRegistry"/> with every in-process
        /// capture source it produces.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="registry"/> is <c>null</c>.
        /// </exception>
        public DefaultCaptureSourceFactory(IChannelCaptureRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(registry);
            m_registry = registry;
        }

        /// <inheritdoc/>
        [UnconditionalSuppressMessage(
            "AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "NIC capture is conditionally created and SharpPcap requires dynamic native loading.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "NIC capture is conditionally created and SharpPcap requires dynamic native loading.")]
        public ICaptureSource Create(
            CaptureSourceKind kind,
            string sessionFolder,
            ILoggerFactory loggerFactory)
        {
            return kind switch
            {
                CaptureSourceKind.Nic => new NicCaptureSource(loggerFactory),
                CaptureSourceKind.InProcessClient
                    => new InProcessClientCaptureSource(m_registry, loggerFactory),
                CaptureSourceKind.InProcessServer
                    => new InProcessServerCaptureSource(m_registry, loggerFactory),
                CaptureSourceKind.Replay => new ReplayCaptureSource(loggerFactory),
                _ => throw new PcapDiagnosticsException($"Unsupported capture source kind '{kind}'.")
            };
        }
    }
}
