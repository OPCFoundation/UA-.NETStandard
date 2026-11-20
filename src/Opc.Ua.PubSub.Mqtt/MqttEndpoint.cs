/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.PubSub.Mqtt
{
    /// <summary>
    /// Parsed MQTT endpoint URL produced by
    /// <see cref="MqttEndpointParser"/>. Carries the materialised
    /// host / port plus a flag selecting plaintext vs TLS so
    /// transport call sites do not re-parse the URL.
    /// </summary>
    /// <remarks>
    /// Implements the addressing surface of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4">
    /// Part 14 §7.3.4 Broker transport (MQTT)</see>. The
    /// <c>mqtt</c> / <c>mqtts</c> URI distinction is the only
    /// scheme-derived signal for TLS; per Part 14 §7.3.4.4 the
    /// negotiated TLS layer is independent of the MQTT protocol
    /// version selection.
    /// </remarks>
    /// <param name="Uri">Parsed broker URI.</param>
    /// <param name="UseTls">
    /// <see langword="true"/> when the URI scheme was <c>mqtts</c>.
    /// </param>
    public readonly record struct MqttEndpoint(Uri Uri, bool UseTls)
    {
        /// <summary>
        /// Convenience accessor — host portion of <see cref="Uri"/>.
        /// </summary>
        public string Host => Uri.Host;

        /// <summary>
        /// Convenience accessor — port portion of <see cref="Uri"/>.
        /// </summary>
        public int Port => Uri.Port;
    }
}
