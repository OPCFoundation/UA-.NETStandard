/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#if NET8_0_OR_GREATER

using System;
using System.Diagnostics.CodeAnalysis;
using Opc.Ua.PubSub.Eth.Channels;

namespace Opc.Ua.PubSub.Eth.Channels
{
    /// <summary>
    /// <see cref="IEthernetFrameChannelFactory"/> that creates SharpPcap
    /// (libpcap / Npcap) frame channels. Registered through
    /// <c>WithPcap()</c> to provide cross-platform / Windows Layer-2 frame
    /// I/O without the privileged native AF_PACKET / BPF backends.
    /// </summary>
    /// <remarks>
    /// SharpPcap dynamically loads the native libpcap / Npcap library. The
    /// SharpPcap surface is isolated to this type and
    /// <see cref="PcapEthernetFrameChannel"/>; the suppression keeps the
    /// rest of the assembly trim / NativeAOT clean while the
    /// <c>Opc.Ua.Aot.Tests</c> evaluation verifies the backend actually
    /// runs under NativeAOT.
    /// </remarks>
    public sealed class PcapEthernetFrameChannelFactory : IEthernetFrameChannelFactory
    {
        /// <inheritdoc/>
        [UnconditionalSuppressMessage(
            "AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "SharpPcap dynamically loads native libpcap/Npcap; verified by Opc.Ua.Aot.Tests.")]
        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "SharpPcap dynamically loads native libpcap/Npcap; verified by Opc.Ua.Aot.Tests.")]
        public IEthernetFrameChannel Create(
            EthChannelParameters parameters,
            ITelemetryContext telemetry,
            TimeProvider timeProvider)
        {
            if (parameters is null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            return new PcapEthernetFrameChannel(parameters, telemetry, timeProvider);
        }
    }
}

#endif
