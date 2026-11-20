// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Minimal client-channel surface that
    /// <see cref="ClientChannelManager"/> consumes when creating
    /// transport channels.
    /// </summary>
    /// <remarks>
    /// The full transport binding registry
    /// (<see cref="ITransportBindingRegistry"/>) implements this
    /// interface so a single
    /// <see cref="DefaultTransportBindingRegistry"/> instance satisfies
    /// both the channel-creation surface
    /// (<see cref="ITransportChannelBindings"/>) and the broader
    /// listener / channel registration surface
    /// (<see cref="ITransportBindingRegistry"/>).
    /// </remarks>
    public interface ITransportChannelBindings
    {
        /// <summary>
        /// Create a channel for the specified uri scheme. Returns
        /// <c>null</c> when no factory is registered for the scheme.
        /// </summary>
        /// <param name="uriScheme">The URI scheme of the transport.</param>
        /// <param name="telemetry">Telemetry context for the new channel.</param>
        ITransportChannel? Create(string uriScheme, ITelemetryContext telemetry);
    }
}
