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
using System.Security;
using NUnit.Framework;
using Opc.Ua.PubSub.Transport;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Transports
{
    /// <summary>
    /// Tests the legacy MQTT connection credential transport guard.
    /// </summary>
    [TestFixture]
    public sealed class MqttCredentialTransportGuardTests
    {
        [Test]
        public void ConstructorRejectsPlaintextMqttCredentialsByDefault()
        {
#pragma warning disable UA0023
            // TODO: Replace when the legacy MQTT connection has an IPubSubApplication constructor.
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
#pragma warning restore UA0023
            PubSubConnectionDataType config = CreateConnectionConfig(
                "mqtt://localhost:1883",
                allowCredentialsOverPlaintext: false);

            Assert.That(
                () => new MqttPubSubConnection(
                    app,
                    config,
                    MessageMapping.Json,
                    NUnitTelemetryContext.Create()),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("MQTT credentials require TLS"));
        }

        [Test]
        public void ConstructorAllowsMqttsCredentialsOrExplicitPlaintextOptOut()
        {
#pragma warning disable UA0023
            // TODO: Replace when the legacy MQTT connection has an IPubSubApplication constructor.
            using UaPubSubApplication app = UaPubSubApplication.Create(NUnitTelemetryContext.Create());
#pragma warning restore UA0023
            PubSubConnectionDataType tlsConfig = CreateConnectionConfig(
                "mqtts://localhost:8883",
                allowCredentialsOverPlaintext: false);
            PubSubConnectionDataType plaintextOptOutConfig = CreateConnectionConfig(
                "mqtt://localhost:1883",
                allowCredentialsOverPlaintext: true);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new MqttPubSubConnection(
                        app,
                        tlsConfig,
                        MessageMapping.Json,
                        NUnitTelemetryContext.Create()).Dispose(),
                    Throws.Nothing);
                Assert.That(
                    () => new MqttPubSubConnection(
                        app,
                        plaintextOptOutConfig,
                        MessageMapping.Json,
                        NUnitTelemetryContext.Create()).Dispose(),
                    Throws.Nothing);
            });
        }

        private static PubSubConnectionDataType CreateConnectionConfig(
            string url,
            bool allowCredentialsOverPlaintext)
        {
            var protocolConfiguration = new MqttClientProtocolConfiguration(
                CreateSecureString("user"),
                CreateSecureString("password"));
            protocolConfiguration.ConnectionProperties =
                protocolConfiguration.ConnectionProperties.AddItem(new KeyValuePair
            {
                Key = QualifiedName.From("AllowCredentialsOverPlaintext"),
                Value = allowCredentialsOverPlaintext
            });

            return new PubSubConnectionDataType
            {
                Name = "mqtt-credential-guard",
                TransportProfileUri = Profiles.PubSubMqttJsonTransport,
                Address = new ExtensionObject(new NetworkAddressUrlDataType
                {
                    Url = url
                }),
                ConnectionProperties = protocolConfiguration.ConnectionProperties
            };
        }

        private static SecureString CreateSecureString(string value)
        {
            var secureString = new SecureString();
            foreach (char c in value)
            {
                secureString.AppendChar(c);
            }

            secureString.MakeReadOnly();
            return secureString;
        }
    }
}
