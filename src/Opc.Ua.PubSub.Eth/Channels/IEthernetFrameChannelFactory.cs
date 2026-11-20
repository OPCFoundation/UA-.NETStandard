/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Eth.Channels
{
    /// <summary>
    /// DI-resolvable factory that creates an
    /// <see cref="IEthernetFrameChannel"/> for a resolved network
    /// interface. One implementation is registered with the DI
    /// container; <c>AddEthTransport()</c> registers the default native
    /// factory, and <c>WithPcap()</c> replaces it with the SharpPcap
    /// backend.
    /// </summary>
    public interface IEthernetFrameChannelFactory
    {
        /// <summary>
        /// Creates a frame channel bound to <paramref name="parameters"/>.
        /// The returned channel is not yet open; callers invoke
        /// <see cref="IEthernetFrameChannel.OpenAsync"/>.
        /// </summary>
        /// <param name="parameters">Resolved channel parameters.</param>
        /// <param name="telemetry">Telemetry context for logging.</param>
        /// <param name="timeProvider">Clock used by the channel.</param>
        /// <returns>A channel ready to be opened.</returns>
        IEthernetFrameChannel Create(
            EthChannelParameters parameters,
            ITelemetryContext telemetry,
            TimeProvider timeProvider);
    }
}
