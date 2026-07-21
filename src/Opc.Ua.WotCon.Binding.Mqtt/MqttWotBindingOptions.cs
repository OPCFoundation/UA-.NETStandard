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

namespace Opc.Ua.WotCon.Binding.Mqtt
{
    /// <summary>Options for the MQTT WoT binding executor.</summary>
    public sealed class MqttWotBindingOptions
    {
        /// <summary>
        /// Gets or sets a factory that supplies an unconnected MQTT client. When
        /// <c>null</c> the executor creates one from the MQTTnet client factory.
        /// The return type is <see cref="object"/> to keep the MQTTnet dependency
        /// out of callers that only configure the executor; it must be an
        /// <c>MQTTnet.IMqttClient</c>.
        /// </summary>
        public Func<object>? ClientFactory { get; set; }

        /// <summary>Gets or sets the client id prefix used for connections.</summary>
        public string ClientIdPrefix { get; set; } = "opcua-wot";

        /// <summary>Gets or sets the timeout awaiting a message during a read.</summary>
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
