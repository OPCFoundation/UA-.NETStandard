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

using System.Collections.Generic;
using Opc.Ua.PubSub.Diagnostics;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Configuration bag bound by the DI builder from the
    /// <c>OpcUa:PubSub</c> configuration section. Kept POCO for AOT
    /// friendliness — no init-only requirements so the configuration
    /// binder can populate at runtime.
    /// </summary>
    /// <remarks>
    /// Implements the application bootstrap surface implied by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.2">
    /// Part 14 §9.1.2</see>.
    /// </remarks>
    public sealed class PubSubApplicationOptions
    {
        /// <summary>
        /// Application identifier (usually the OPC UA application
        /// URI). When <see langword="null"/> the builder picks a
        /// default derived from the host configuration.
        /// </summary>
        public string? ApplicationId { get; set; }

        /// <summary>
        /// Diagnostics verbosity.
        /// </summary>
        public PubSubDiagnosticsLevel DiagnosticsLevel { get; set; } = PubSubDiagnosticsLevel.Medium;

        /// <summary>
        /// File path of an XML PubSub configuration to load at
        /// start-up. Mutually exclusive with
        /// <see cref="InlineConfiguration"/>; when both are set the
        /// builder throws.
        /// </summary>
        public string? ConfigurationFilePath { get; set; }

        /// <summary>
        /// Inline configuration. Convenient for samples and tests
        /// that build the configuration programmatically.
        /// </summary>
        public PubSubConfigurationDataType? InlineConfiguration { get; set; }

        /// <summary>
        /// Endpoints of Security Key Service (SKS) instances the
        /// PubSub application may pull keys from, recorded for discovery
        /// and diagnostics. Registering a working pull provider is done
        /// through the dependency-injection
        /// <c>AddPubSubSecurityKeyServiceClient(...)</c> overloads or
        /// <see cref="PubSubApplicationBuilder.AddSecurityKeyProvider"/>;
        /// this list does not by itself wire a provider.
        /// </summary>
        public IList<EndpointDescription> SecurityKeyServiceEndpoints { get; set; }
            = [];

        /// <summary>
        /// When <see langword="true"/> the builder registers UADP and
        /// JSON encoders / decoders alongside the application. Set to
        /// <see langword="false"/> when consumers want to register
        /// their own encoder set explicitly.
        /// </summary>
        public bool RegisterAllStandardEncoders { get; set; } = true;

        /// <summary>
        /// Selects whether JSON NetworkMessage encoders emit experimental schema
        /// announcements and which JSON flavor to use.
        /// </summary>
        public JsonSchemaExchangeMode JsonSchemaExchange { get; set; } = JsonSchemaExchangeMode.Disabled;

        /// <summary>
        /// When <see langword="true"/> the builder registers the
        /// default UDP transport factory. Has no effect unless
        /// <c>Opc.Ua.PubSub.Udp</c> has wired the underlying services.
        /// </summary>
        public bool RegisterUdpTransport { get; set; } = true;

        /// <summary>
        /// When <see langword="true"/> the builder registers the
        /// default MQTT transport factory pair (UADP + JSON). Has no
        /// effect unless <c>Opc.Ua.PubSub.Mqtt</c> has wired the
        /// underlying services.
        /// </summary>
        public bool RegisterMqttTransport { get; set; } = true;
    }
}
