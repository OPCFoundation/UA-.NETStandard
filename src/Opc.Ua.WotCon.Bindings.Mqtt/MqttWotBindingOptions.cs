/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using MQTTnet;
using MQTTnet.Channel;

namespace Opc.Ua.WotCon.Bindings.Mqtt
{
    /// <summary>
    /// Options for the MQTT WoT binding executor.
    /// </summary>
    public sealed class MqttWotBindingOptions
    {
        /// <summary>
        /// Gets or sets a factory that supplies an unconnected MQTT client. When
        /// <c>null</c> the executor creates one from the MQTTnet client factory.
        /// </summary>
        public Func<IMqttClient>? ClientFactory { get; set; }

        /// <summary>
        /// Gets or sets the transport stream provider used for each connection.
        /// The provider returns plaintext TCP; MQTTnet applies TLS using the original broker hostname.
        /// When absent, MQTTnet's existing socket connector remains unchanged.
        /// </summary>
        public IMqttClientStreamProvider? StreamProvider { get; set; }

        /// <summary>
        /// Gets or sets the MQTT 5 maximum packet size advertised to the broker.
        /// Zero preserves MQTTnet's existing default. Application payloads remain independently bounded
        /// by <see cref="WotBindingBounds.MaxPayloadBytes"/>.
        /// </summary>
        public uint MaximumPacketSize { get; set; }

        /// <summary>
        /// Gets or sets the client id prefix used for connections.
        /// </summary>
        public string ClientIdPrefix { get; set; } = "opcua-wot";

        /// <summary>
        /// Gets or sets the timeout awaiting a message during a read.
        /// </summary>
        public TimeSpan ReadTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Gets or sets whether username / password credentials may be sent over a
        /// plaintext <c>mqtt://</c> connection. When <c>false</c> (the default) the
        /// executor fails closed rather than leaking credentials in clear text; use
        /// an <c>mqtts://</c> href instead. Set to <c>true</c> only for explicitly
        /// accepted plaintext deployments.
        /// </summary>
        public bool AllowCredentialsOverPlaintext { get; set; }

        /// <summary>
        /// Gets or sets whether the broker's TLS certificate is validated for an
        /// <c>mqtts://</c> connection. When <c>true</c> (the default) the platform
        /// trust store, or the trust anchors resolved through the credential
        /// provider, must validate the broker certificate. Set to <c>false</c> only
        /// for explicitly accepted test deployments.
        /// </summary>
        public bool ValidateServerCertificate { get; set; } = true;
    }
}
