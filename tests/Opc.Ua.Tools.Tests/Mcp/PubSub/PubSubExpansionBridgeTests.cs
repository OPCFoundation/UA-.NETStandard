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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Transcoding;
using Opc.Ua.Tools.Tests.McpCompanion;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Declarative-only transformation, security and owned bridge lifecycle contracts.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionBridgeTests
    {
        [Test]
        public async Task DeclarativeTransformsPreserveValueTypeAndActualMetadataVersionAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            PubSubBridgeInput route = Route() with
            {
                RenameFields = [new PubSubFieldRenameInput { Source = "Count", Target = "Counter" }],
                SelectFields = ["Counter"],
                PublisherId = new PubSubPublisherIdInput
                {
                    Type = PublisherIdType.Byte,
                    Value = PubSubExpansionTestContext.Element("3")
                },
                WriterGroupId = 17
            };
            McpPubSubBridge.Validate(route, new McpPubSubOptions());
            var transcoder = new NetworkMessageTranscoder(McpPubSubBridge.Specification(route).BuildSpec());
            var context = new TranscodeContext(new PubSubNetworkMessageContext(
                fixture.Remote.Context, new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                fixture.Clock), fixture.Sessions.Telemetry);
            var source = new UadpNetworkMessage
            {
                PublisherId = PublisherId.FromUInt16(42),
                WriterGroupId = 1,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 1,
                        MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 29, MinorVersion = 7 },
                        Fields =
                        [
                            new DataSetField { Name = "Count", Value = Variant.From(7) },
                            new DataSetField { Name = "Enabled", Value = Variant.From(false) }
                        ]
                    }
                ]
            };

            ArrayOf<PubSubNetworkMessage> output = await transcoder.TranscodeAsync(source, context)
                .ConfigureAwait(false);

            Assert.That(output, Has.Count.EqualTo(1));
            Assert.That(output[0].PublisherId.Type, Is.EqualTo(PublisherIdType.Byte));
            Assert.That(output[0].PublisherId.ToVariant().TryGetValue(out byte publisher), Is.True);
            Assert.That(publisher, Is.EqualTo(3));
            Assert.That(output[0].WriterGroupId, Is.EqualTo(17));
            Assert.That(output[0].DataSetMessages[0].Fields, Has.Count.EqualTo(1));
            Assert.That(output[0].DataSetMessages[0].Fields[0].Name, Is.EqualTo("Counter"));
            Assert.That(output[0].DataSetMessages[0].Fields[0].Value.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(7));
            Assert.That(output[0].DataSetMessages[0].MetaDataVersion.MajorVersion, Is.EqualTo(29));
            Assert.That(output[0].DataSetMessages[0].MetaDataVersion.MinorVersion, Is.EqualTo(7));
            Assert.That(source.DataSetMessages[0].Fields, Has.Count.EqualTo(2),
                "Transforms must not mutate the source.");
        }

        [Test]
        public void SecurityDowngradeRequiresBothIndependentHostAndRouteConsent()
        {
            PubSubBridgeInput route = Route();
            route = route with
            {
                Source = route.Source with
                {
                    Security = new PubSubSecurityInput
                    {
                        GroupId = "line",
                        PolicyUri = PubSubSecurityPolicyUri.PubSubAes128Ctr,
                        KeyReference = "issued:test",
                        Resource = "urn:test:line",
                        TokenId = 7,
                        ExpiresAt = new DateTimeOffset(2026, 10, 12, 1, 0, 0, TimeSpan.Zero)
                    }
                }
            };

            ServiceResultException? noConsent = Assert.Throws<ServiceResultException>(() =>
                McpPubSubBridge.Validate(route, new McpPubSubOptions()));
            ServiceResultException? routeOnly = Assert.Throws<ServiceResultException>(() =>
                McpPubSubBridge.Validate(route with { AllowSecurityDowngrade = true }, new McpPubSubOptions()));
            Assert.That(noConsent!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityModeRejected));
            Assert.That(routeOnly!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(() => McpPubSubBridge.Validate(route with { AllowSecurityDowngrade = true },
                new McpPubSubOptions { AllowSecurityDowngrade = true }), Throws.Nothing);
        }

        [Test]
        public async Task CreatingAndRemovingStoppedBridgeDoesNotActivateEitherEndpointAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable owner = fixture.Enter();
            PubSubBridgeTools tools = fixture.Services.GetRequiredService<PubSubBridgeTools>();

            CallToolResult created = await tools.CreateAsync(Route()).ConfigureAwait(false);
            string handle = RemoteModelTestSession.Payload(created).GetProperty("handle").GetString()!;
            CallToolResult state = await tools.StatusAsync(handle).ConfigureAwait(false);
            CallToolResult removed = await tools.RemoveAsync(handle).ConfigureAwait(false);

            Assert.That(RemoteModelTestSession.Payload(state).GetProperty("running").GetBoolean(), Is.False);
            Assert.That(RemoteModelTestSession.Payload(state)
                .GetProperty("targetCyclicPublishing").GetBoolean(), Is.False);
            Assert.That(RemoteModelTestSession.Payload(removed).GetProperty("removed").GetBoolean(), Is.True);
            Assert.That(fixture.Transports.Factories, Is.Zero);
            Assert.That(fixture.Registry.List(), Is.Empty);
        }

        [Test]
        public void ScriptsAndCollidingFieldTransformsAreNotAcceptedAsDeclarativeRoutes()
        {
            PubSubBridgeInput route = Route();
            JsonNode serialized = JsonSerializer.SerializeToNode(
                route, McpPubSubExpansionJsonContext.Default.PubSubBridgeInput)!;
            serialized["script"] = "arbitrary-code";

            Assert.That(() => JsonSerializer.Deserialize(serialized.ToJsonString(),
                McpPubSubExpansionJsonContext.Default.PubSubBridgeInput), Throws.TypeOf<JsonException>());
            Assert.That(() => McpPubSubBridge.Validate(route with
            {
                RenameFields = [new PubSubFieldRenameInput { Source = "Count", Target = "Enabled" }]
            }, new McpPubSubOptions()), Throws.ArgumentException);
            Assert.That(() => McpPubSubBridge.Validate(route with
            {
                SelectFields = ["Count"],
                ExcludeFields = ["Enabled"]
            }, new McpPubSubOptions()), Throws.ArgumentException);
        }

        private static PubSubBridgeInput Route()
        {
            return new PubSubBridgeInput
            {
                Name = "bridge",
                Source = PubSubExpansionTestContext.Input("source", "Subscriber"),
                Target = PubSubExpansionTestContext.Input("target")
            };
        }
    }
}
#endif
