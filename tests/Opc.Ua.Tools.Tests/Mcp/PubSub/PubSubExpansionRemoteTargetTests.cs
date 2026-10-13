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

#if NET10_0
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Tools.Tests.McpCompanion;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Distinguishes remote configuration grants from local transport activation and checks every broker topic.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionRemoteTargetTests
    {
        [TestCase(PubSubTransportKind.Mqtt, "Publisher")]
        [TestCase(PubSubTransportKind.Mqtt, "Subscriber")]
        [TestCase(PubSubTransportKind.Kafka, "Publisher")]
        [TestCase(PubSubTransportKind.Kafka, "Subscriber")]
        public async Task RemoteBrokerConfigurationRequiresBothTopicsAndTheExactDirectionWithoutConnectingAsync(
            PubSubTransportKind kind, string mode)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            string scheme = kind == PubSubTransportKind.Mqtt ? "mqtt" : "kafka";
            string operation = mode == "Publisher" ? "configure-publish" : "configure-subscribe";
            PubSubApplicationInput input = PubSubExpansionTestContext.Input(mode: mode) with
            {
                Connection = new PubSubConnectionInput
                {
                    Transport = kind,
                    Encoding = PubSubMessageEncoding.Json,
                    Endpoint = scheme + "://broker.test:1883",
                    Topic = "data",
                    MetadataTopic = "metadata"
                }
            };
            var source = new McpPubSubDataSetSource(input, new McpPubSubOptions(),
                fixture.Remote.Context, fixture.Clock);
            PubSubConfigurationDataType configuration = McpPubSubConfiguration.Build(input, source.BuildMetaData());
            IMcpOperationPolicy policy = fixture.Services.GetRequiredService<IMcpOperationPolicy>();
            var io = new ForbiddenNetwork();
            var rule = new McpNetworkRule
            {
                Scheme = scheme,
                Host = "broker.test",
                Port = 1883,
                Networks = ["127.0.0.0/8"],
                Capability = McpCapability.PubSubConfiguration,
                Operations = [operation],
                Resources = ["data", "metadata"]
            };
            var allowed = new McpNetworkPolicy(policy, [rule], io, io);
            var missingMetadata = new McpNetworkPolicy(policy, [rule with { Resources = ["data"] }], io, io);
            var wrongDirection = new McpNetworkPolicy(policy,
                [rule with { Operations = [mode == "Publisher" ? "configure-subscribe" : "configure-publish"] }],
                io, io);

            Assert.That(() => McpPubSubRemoteTargets.Require(configuration, new McpPubSubOptions(), allowed),
                Throws.Nothing);
            ServiceResultException? metadataError = Assert.Throws<ServiceResultException>(() =>
                McpPubSubRemoteTargets.Require(configuration, new McpPubSubOptions(), missingMetadata));
            ServiceResultException? directionError = Assert.Throws<ServiceResultException>(() =>
                McpPubSubRemoteTargets.Require(configuration, new McpPubSubOptions(), wrongDirection));

            Assert.That(metadataError!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(directionError!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(io.Attempts, Is.Zero);
            Assert.That(fixture.Transports.Factories, Is.Zero);
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        [TestCase("Publisher")]
        [TestCase("Subscriber")]
        public async Task MissingBrokerSettingsCannotBypassTopicAuthorizationAsync(string mode)
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            PubSubApplicationInput input = PubSubExpansionTestContext.Input(mode: mode) with
            {
                Connection = new PubSubConnectionInput
                {
                    Transport = PubSubTransportKind.Mqtt,
                    Encoding = PubSubMessageEncoding.Json,
                    Endpoint = "mqtt://broker.test:1883",
                    Topic = "data",
                    MetadataTopic = "metadata"
                }
            };
            var source = new McpPubSubDataSetSource(input, new McpPubSubOptions(),
                fixture.Remote.Context, fixture.Clock);
            PubSubConfigurationDataType configuration = McpPubSubConfiguration.Build(input, source.BuildMetaData());
            if (mode == "Publisher")
            {
                configuration.Connections[0].WriterGroups[0].DataSetWriters[0].TransportSettings = ExtensionObject.Null;
            }
            else
            {
                configuration.Connections[0].ReaderGroups[0].DataSetReaders[0].TransportSettings = ExtensionObject.Null;
            }

            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                McpPubSubRemoteTargets.Require(configuration, new McpPubSubOptions(),
                    fixture.Services.GetRequiredService<McpNetworkPolicy>()));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(fixture.Remote.Calls, Is.Empty);
        }

        [Test]
        public async Task RuntimeCredentialReferencesCannotBeEmbeddedInRemoteConnectionConfigurationsAsync()
        {
            var options = new McpPubSubOptions
            {
                ModelRules =
                [
                    new McpPubSubModelRule
                    {
                        SessionName = PubSubExpansionTestContext.SessionName,
                        NodeId = ObjectIds.PublishSubscribe.ToString(),
                        Operations = ["pubsub_configuration_add_connection"]
                    }
                ]
            };
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                options: options).ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            fixture.Remote.Type(ObjectIds.PublishSubscribe, ObjectTypeIds.PublishSubscribeType);
            PubSubApplicationInput input = PubSubExpansionTestContext.Input() with
            {
                Connection = new PubSubConnectionInput
                {
                    Transport = PubSubTransportKind.Mqtt,
                    Endpoint = "mqtts://broker.test:8883",
                    Topic = "data",
                    MetadataTopic = "metadata",
                    CredentialReference = "alias:mqtt-password",
                    UserName = "test-user"
                }
            };

            CallToolResult result = await fixture.Services.GetRequiredService<PubSubConfigurationTools>()
                .AddConnectionAsync(input, PubSubExpansionTestContext.SessionName).ConfigureAwait(false);

            RemoteModelTestSession.ArgumentError(result);
            Assert.That(fixture.Remote.Calls, Is.Empty);
            Assert.That(RemoteModelTestSession.Payload(result).GetRawText(), Does.Not.Contain("alias:mqtt-password"));
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        /// <summary>
        /// Any resolution or connection attempt makes a configuration-only test fail immediately.
        /// </summary>
        private sealed class ForbiddenNetwork : IMcpDnsResolver, IMcpTcpConnector
        {
            internal int Attempts { get; private set; }

            public ValueTask<ArrayOf<IPAddress>> ResolveAsync(string host, CancellationToken ct = default)
            {
                Attempts++;
                throw new InvalidOperationException("Configuration must not resolve an endpoint.");
            }

            public ValueTask<Stream> ConnectAsync(IPEndPoint endpoint, CancellationToken ct = default)
            {
                Attempts++;
                throw new InvalidOperationException("Configuration must not open a connection.");
            }
        }
    }
}
#endif
