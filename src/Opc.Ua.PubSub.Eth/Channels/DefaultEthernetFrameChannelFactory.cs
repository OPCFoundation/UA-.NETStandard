// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Runtime.InteropServices;

namespace Opc.Ua.PubSub.Eth.Channels
{
    /// <summary>
    /// Default <see cref="IEthernetFrameChannelFactory"/> that selects an
    /// in-repo native backend by operating system: Linux
    /// <c>AF_PACKET</c> and macOS BPF. On other platforms (for example
    /// Windows) it throws, directing callers to register the SharpPcap
    /// backend via <c>WithPcap()</c> or inject a custom factory.
    /// </summary>
    /// <remarks>
    /// Mirrors the platform-dispatch model used elsewhere in the stack
    /// (for example the DTLS native backend). Both built-in backends use
    /// libc P/Invoke and are NativeAOT-compatible.
    /// </remarks>
    public sealed class DefaultEthernetFrameChannelFactory : IEthernetFrameChannelFactory
    {
        /// <inheritdoc/>
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return new AfPacketEthernetFrameChannel(parameters, telemetry, timeProvider);
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return new BpfEthernetFrameChannel(parameters, telemetry, timeProvider);
            }
            throw new PlatformNotSupportedException(
                "The native OPC UA PubSub Ethernet backend supports Linux (AF_PACKET) and macOS (BPF). " +
                "On Windows or other platforms, register the SharpPcap backend via WithPcap() or inject a " +
                "custom IEthernetFrameChannelFactory (for example the in-memory loopback backend).");
        }
    }
}
