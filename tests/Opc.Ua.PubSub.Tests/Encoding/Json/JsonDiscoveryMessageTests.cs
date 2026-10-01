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
 *
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
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.Tests;
using JsonDecoder = Opc.Ua.PubSub.Encoding.Json.JsonDecoder;
using JsonDiscoveryMessage = Opc.Ua.PubSub.Encoding.Json.JsonDiscoveryMessage;
using JsonEncoder = Opc.Ua.PubSub.Encoding.Json.JsonEncoder;

namespace OpcUaPubSubJsonTests
{
    /// <summary>
    /// Round-trip coverage for the JSON discovery envelope
    /// (<c>ua-discovery</c>) carrying any of the 5 discovery-response
    /// variants per Part 14 §7.2.5.5 (sub-task 16d).
    /// </summary>
    [TestFixture]
    [Category("PubSub")]
    public sealed class JsonDiscoveryMessageTests
    {
        private static readonly string[] s_applicationLayout =
            ["MessageId", "MessageType", "PublisherId", "Timestamp", "Description", "ServerCapabilities"];
        private static readonly string[] s_endpointsLayout =
            ["MessageId", "MessageType", "PublisherId", "Timestamp", "Endpoints"];
        private static readonly string[] s_cyclicStatusLayout =
            ["MessageId", "MessageType", "PublisherId", "Timestamp", "IsCyclic", "Status", "NextReportTime"];
        private static readonly string[] s_statusLayout =
            ["MessageId", "MessageType", "PublisherId", "IsCyclic", "Status"];
        private static readonly string[] s_metaDataLayout =
        [
            "MessageId", "MessageType", "PublisherId", "DataSetWriterId", "WriterGroupName",
            "DataSetWriterName", "Timestamp", "MetaData"
        ];

        [Test]
        [TestSpec("7.2.5.5")]
        public async Task RoundTrip_ApplicationInformationAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var msg = new JsonDiscoveryMessage
            {
                MessageId = "disc-app",
                PublisherId = PublisherId.FromUInt16(0x4242),
                DiscoveryType = UadpDiscoveryType.ApplicationInformation,
                ApplicationInformation = new UadpApplicationInformation
                {
                    ApplicationName = new LocalizedText("en", "JSON Publisher"),
                    ApplicationUri = "urn:test:json:publisher",
                    ProductUri = "urn:test:product",
                    ApplicationType = ApplicationType.Server,
                    Capabilities = new[] { "UA" },
                    SupportedTransportProfiles =
                        new[] { Profiles.PubSubMqttJsonTransport },
                    SupportedSecurityPolicies = new[] { "None" }
                }
            };
            var encoder = new JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(msg, ctx)
                .ConfigureAwait(false);
            using (var document = JsonDocument.Parse(bytes))
            {
                Assert.That(document.RootElement.GetProperty("MessageType").GetString(),
                    Is.EqualTo(JsonDiscoveryMessage.MessageTypeApplication));
                Assert.That(document.RootElement.GetProperty("PublisherId").ValueKind,
                    Is.EqualTo(JsonValueKind.String));
                Assert.That(document.RootElement.GetProperty("PublisherId").GetString(),
                    Is.EqualTo("16962"));
            }

            var decoder = new JsonDecoder();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(bytes, ctx)
                .ConfigureAwait(false);

            var disc = decoded as JsonDiscoveryMessage;
            Assert.That(disc, Is.Not.Null);
            Assert.That(disc!.DiscoveryType,
                Is.EqualTo(UadpDiscoveryType.ApplicationInformation));
            Assert.That(disc.ApplicationInformation, Is.Not.Null);
            Assert.That(disc.ApplicationInformation!.ApplicationUri,
                Is.EqualTo("urn:test:json:publisher"));
            Assert.That(disc.ApplicationInformation!.ApplicationName.Text,
                Is.EqualTo("JSON Publisher"));
            Assert.That(((string[]?)disc.ApplicationInformation!.Capabilities) ?? [], Has.Length.EqualTo(1));
        }

        [Test]
        [TestSpec("7.2.5.5")]
        public async Task RoundTrip_PubSubConnectionAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var connection = new PubSubConnectionDataType
            {
                Name = "JSON-Conn",
                Enabled = true,
                PublisherId = new Variant((ushort)9000),
                TransportProfileUri = Profiles.PubSubMqttJsonTransport
            };
            var msg = new JsonDiscoveryMessage
            {
                MessageId = "disc-conn",
                PublisherId = PublisherId.FromUInt16(0x100),
                DiscoveryType = UadpDiscoveryType.PubSubConnection,
                Connection = connection
            };
            var encoder = new JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(msg, ctx)
                .ConfigureAwait(false);
            using (var document = JsonDocument.Parse(bytes))
            {
                Assert.That(document.RootElement.GetProperty("MessageType").GetString(),
                    Is.EqualTo(JsonDiscoveryMessage.MessageTypeConnection));
            }

            var decoder = new JsonDecoder();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(bytes, ctx)
                .ConfigureAwait(false);

            var disc = decoded as JsonDiscoveryMessage;
            Assert.That(disc, Is.Not.Null);
            Assert.That(disc!.DiscoveryType,
                Is.EqualTo(UadpDiscoveryType.PubSubConnection));
            Assert.That(disc.Connection, Is.Not.Null);
            Assert.That(disc.Connection!.Name, Is.EqualTo("JSON-Conn"));
            Assert.That(disc.Connection!.TransportProfileUri,
                Is.EqualTo(Profiles.PubSubMqttJsonTransport));
        }

        [Test]
        [TestSpec("7.2.5.5")]
        public async Task RoundTrip_DataSetMetaDataAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            DataSetMetaDataType meta = JsonTestUtilities.CreateMetaData("Disc-DSM");
            var msg = new JsonDiscoveryMessage
            {
                MessageId = "disc-meta",
                PublisherId = PublisherId.FromUInt16(0x200),
                DiscoveryType = UadpDiscoveryType.DataSetMetaData,
                DataSetWriterId = 5,
                MetaData = meta
            };
            var encoder = new JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(msg, ctx)
                .ConfigureAwait(false);

            using var document = JsonDocument.Parse(bytes);
            Assert.That(document.RootElement.GetProperty("MessageType").GetString(),
                Is.EqualTo(Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage.MessageTypeMetaData));

            var decoder = new JsonDecoder();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(bytes, ctx)
                .ConfigureAwait(false);

            var metaDataMessage = decoded as Opc.Ua.PubSub.Encoding.Json.JsonMetaDataMessage;
            Assert.That(metaDataMessage, Is.Not.Null);
            Assert.That(metaDataMessage!.MetaDataPayload, Is.Not.Null);
            Assert.That(metaDataMessage.MetaDataPayload!.Name, Is.EqualTo("Disc-DSM"));
            Assert.That(metaDataMessage.DataSetWriterId, Is.EqualTo(5));
        }

        [Test]
        [TestSpec("7.2.5.5")]
        public void DataSetWriterConfigurationHasNoJsonDiscoveryMessage()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var msg = new JsonDiscoveryMessage
            {
                MessageId = "disc-wcfg",
                PublisherId = PublisherId.FromUInt16(0x300),
                DiscoveryType = UadpDiscoveryType.DataSetWriterConfiguration,
                DataSetWriterIds = [1, 2, 3],
                WriterConfiguration = new WriterGroupDataType { Name = "WG-JSON", WriterGroupId = 42 }
            };

            // Part 14 §7.2.5.5 defines ua-metadata, ua-application,
            // ua-endpoints, ua-status and ua-connection only.
            Assert.That(
                async () => await new JsonEncoder().EncodeAsync(msg, ctx).ConfigureAwait(false),
                Throws.ArgumentException);
        }

        [Test]
        [TestSpec("7.2.5.5.3")]
        public async Task ApplicationMessageUsesSpecLayoutAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var msg = new JsonDiscoveryMessage
            {
                PublisherId = PublisherId.FromString("Pub"),
                DiscoveryType = UadpDiscoveryType.ApplicationInformation,
                ApplicationInformation = new UadpApplicationInformation
                {
                    ApplicationName = new LocalizedText("en", "App"),
                    ApplicationUri = "urn:app",
                    ProductUri = "urn:product",
                    ApplicationType = ApplicationType.Server,
                    Capabilities = ["LDS"]
                }
            };

            ReadOnlyMemory<byte> bytes = await new JsonEncoder().EncodeAsync(msg, ctx).ConfigureAwait(false);

            using var document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            Assert.That(PropertyNames(root), Is.EqualTo(s_applicationLayout));
            Assert.That(root.GetProperty("MessageId").GetString(), Is.Not.Empty);
            Assert.That(root.GetProperty("Timestamp").GetString(), Is.EqualTo("2026-06-15T12:00:00.0000000Z"));
            Assert.That(root.GetProperty("Description").GetProperty("ApplicationUri").GetString(),
                Is.EqualTo("urn:app"));
            Assert.That(root.GetProperty("ServerCapabilities")[0].GetString(), Is.EqualTo("LDS"));

            var decoded = await new JsonDecoder().TryDecodeAsync(bytes, ctx).ConfigureAwait(false)
                as JsonDiscoveryMessage;
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded!.Description!.ProductUri, Is.EqualTo("urn:product"));
            Assert.That(decoded.ApplicationInformation!.ApplicationType, Is.EqualTo(ApplicationType.Server));
        }

        [Test]
        [TestSpec("7.2.5.5.4")]
        public async Task EndpointsMessageUsesSpecLayoutAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var msg = new JsonDiscoveryMessage
            {
                MessageId = "m",
                PublisherId = PublisherId.FromString("Pub"),
                DiscoveryType = UadpDiscoveryType.PublisherEndpoints,
                PublisherEndpoints = [new EndpointDescription { EndpointUrl = "opc.tcp://a:4840" }]
            };

            ReadOnlyMemory<byte> bytes = await new JsonEncoder().EncodeAsync(msg, ctx).ConfigureAwait(false);

            using var document = JsonDocument.Parse(bytes);
            Assert.That(PropertyNames(document.RootElement), Is.EqualTo(s_endpointsLayout));
        }

        [TestCase(true)]
        [TestCase(false)]
        [TestSpec("7.2.5.5.5")]
        public async Task StatusMessageUsesSpecLayoutAsync(bool isCyclic)
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var next = new DateTime(2026, 6, 15, 12, 1, 0, DateTimeKind.Utc);
            var msg = new JsonDiscoveryMessage
            {
                MessageId = "m",
                PublisherId = PublisherId.FromString("Pub"),
                DiscoveryType = UadpDiscoveryType.ApplicationInformation,
                ApplicationStatus = new UadpApplicationStatus
                {
                    IsCyclic = isCyclic,
                    Status = PubSubState.Operational,
                    NextReportTime = (DateTimeUtc)next
                }
            };

            ReadOnlyMemory<byte> bytes = await new JsonEncoder().EncodeAsync(msg, ctx).ConfigureAwait(false);

            using var document = JsonDocument.Parse(bytes);
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("MessageType").GetString(), Is.EqualTo(JsonDiscoveryMessage.MessageTypeStatus));
            Assert.That(PropertyNames(root), Is.EqualTo(isCyclic ? s_cyclicStatusLayout : s_statusLayout));
            Assert.That(root.GetProperty("Status").GetInt32(), Is.EqualTo((int)PubSubState.Operational));

            var decoded = await new JsonDecoder().TryDecodeAsync(bytes, ctx).ConfigureAwait(false)
                as JsonDiscoveryMessage;
            Assert.That(decoded?.ApplicationStatus, Is.Not.Null);
            Assert.That(decoded!.ApplicationStatus!.IsCyclic, Is.EqualTo(isCyclic));
            Assert.That(decoded.ApplicationStatus.Status, Is.EqualTo(PubSubState.Operational));
            if (isCyclic)
            {
                Assert.That((DateTime)decoded.ApplicationStatus.NextReportTime, Is.EqualTo(next));
            }
        }

        [Test]
        [TestSpec("7.2.5.5.2")]
        public async Task MetaDataMessageUsesSpecLayoutAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var msg = new Opc.Ua.PubSub.Encoding.Json.JsonMetaDataMessage
            {
                MessageId = "m",
                PublisherId = PublisherId.FromString("Pub"),
                DataSetWriterId = 0,
                WriterGroupName = "WG",
                DataSetWriterName = "DSW",
                DataSetClassId = new Uuid(Guid.NewGuid()),
                MetaDataPayload = JsonTestUtilities.CreateMetaData()
            };

            ReadOnlyMemory<byte> bytes = await new JsonEncoder().EncodeAsync(msg, ctx).ConfigureAwait(false);

            using var document = JsonDocument.Parse(bytes);
            Assert.That(PropertyNames(document.RootElement), Is.EqualTo(s_metaDataLayout));
            var decoded = await new JsonDecoder().TryDecodeAsync(bytes, ctx).ConfigureAwait(false)
                as Opc.Ua.PubSub.Encoding.Json.JsonMetaDataMessage;
            Assert.That(decoded, Is.Not.Null);
            Assert.That(decoded!.WriterGroupName, Is.EqualTo("WG"));
            Assert.That(decoded.DataSetWriterName, Is.EqualTo("DSW"));
            Assert.That(decoded.MetaDataPayload!.Fields, Has.Count.EqualTo(3));
        }

        private static string[] PropertyNames(JsonElement element)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                names.Add(property.Name);
            }
            return [.. names];
        }
        [Test]
        [TestSpec("7.2.5.5")]
        public async Task RoundTrip_PublisherEndpointsAsync()
        {
            PubSubNetworkMessageContext ctx = JsonTestUtilities.NewContext();
            var ep1 = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://host-a:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#None"
            };
            var ep2 = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://host-b:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri =
                    "http://opcfoundation.org/UA/SecurityPolicy#Aes256_Sha256_RsaPss"
            };
            var msg = new JsonDiscoveryMessage
            {
                MessageId = "disc-eps",
                PublisherId = PublisherId.FromUInt16(0x400),
                DiscoveryType = UadpDiscoveryType.PublisherEndpoints,
                PublisherEndpoints = [ep1, ep2]
            };
            var encoder = new JsonEncoder();
            ReadOnlyMemory<byte> bytes = await encoder.EncodeAsync(msg, ctx)
                .ConfigureAwait(false);
            using (var document = JsonDocument.Parse(bytes))
            {
                Assert.That(document.RootElement.GetProperty("MessageType").GetString(),
                    Is.EqualTo(JsonDiscoveryMessage.MessageTypeEndpoints));
            }

            var decoder = new JsonDecoder();
            PubSubNetworkMessage? decoded = await decoder.TryDecodeAsync(bytes, ctx)
                .ConfigureAwait(false);

            var disc = decoded as JsonDiscoveryMessage;
            Assert.That(disc, Is.Not.Null);
            Assert.That(disc!.DiscoveryType,
                Is.EqualTo(UadpDiscoveryType.PublisherEndpoints));
            Assert.That(disc.PublisherEndpoints, Has.Length.EqualTo(2));
            Assert.That(disc.PublisherEndpoints[0].EndpointUrl,
                Is.EqualTo("opc.tcp://host-a:4840"));
            Assert.That(disc.PublisherEndpoints[1].SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        }
    }
}
