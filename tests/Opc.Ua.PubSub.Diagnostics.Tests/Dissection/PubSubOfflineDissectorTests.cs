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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using UadpDataSetMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Pcap.Tests.Dissection
{
    [TestFixture]
    [Category("PubSub")]
    public sealed class PubSubOfflineDissectorTests
    {
        [Test]
        public async Task DissectAsyncDecodesCleartextUadpDataSetsAsync()
        {
            ReadOnlyMemory<byte> bytes = await EncodeUadpAsync().ConfigureAwait(false);
            PubSubOfflineDissector dissector = new(NewContext());

            PubSubDissectionResult result = await dissector.DissectAsync(CreateFrame(bytes, "opc.udp.uadp"))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsDecoded, Is.True);
                Assert.That(result.IsUndecodable, Is.False);
                Assert.That(result.MessageType, Is.EqualTo(PubSubDissectionMessageType.Uadp));
                Assert.That(result.SecurityState, Is.EqualTo(PubSubDissectionSecurityState.None));
                Assert.That(result.WriterGroupId, Is.EqualTo((ushort)7));
                Assert.That(result.DataSetWriterIds.ToArray(), Is.EqualTo(new ushort[] { 100 }));
                Assert.That(result.DataSets, Has.Count.EqualTo(1));
                Assert.That(result.DataSets[0].Fields[0].Value, Is.EqualTo(new Variant(42)));
            });
        }

        [Test]
        public async Task DissectAsyncDecodesJsonDataSetsAsync()
        {
            ReadOnlyMemory<byte> bytes = await EncodeJsonAsync().ConfigureAwait(false);
            PubSubOfflineDissector dissector = new(NewContext());

            PubSubDissectionResult result = await dissector.DissectAsync(CreateFrame(bytes, "opc.mqtt.json"))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsDecoded, Is.True);
                Assert.That(result.MessageType, Is.EqualTo(PubSubDissectionMessageType.Json));
                Assert.That(result.DataSets, Has.Count.EqualTo(1));
                Assert.That(result.DataSets[0].DataSetWriterId, Is.EqualTo((ushort)101));
                Assert.That(result.DataSets[0].Fields[0].Name, Is.EqualTo("Temperature"));
                Assert.That(result.DataSets[0].Fields[0].Value, Is.EqualTo(new Variant(25.5)));
            });
        }

        [Test]
        public async Task DissectAsyncReturnsUndecodableForMalformedBytesAsync()
        {
            PubSubOfflineDissector dissector = new(NewContext());
            PubSubCaptureFrame frame = CreateFrame(new byte[] { 0xFF, 0x00, 0x01 }, "opc.udp.uadp");

            PubSubDissectionResult result = await dissector.DissectAsync(frame).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsDecoded, Is.False);
                Assert.That(result.IsUndecodable, Is.True);
                Assert.That(result.MessageType, Is.EqualTo(PubSubDissectionMessageType.Uadp));
                Assert.That(result.DiagnosticMessage, Is.Not.Empty);
            });
        }

        [Test]
        public async Task DissectAsyncReportsSecuredUadpNeedsKeyWithoutResolverAsync()
        {
            SecuredFrame secured = await BuildSecuredFrameAsync().ConfigureAwait(false);
            PubSubOfflineDissector dissector = new(NewContext());

            PubSubDissectionResult result = await dissector.DissectAsync(CreateFrame(secured.Frame, "opc.udp.uadp"))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsDecoded, Is.False);
                Assert.That(result.IsUndecodable, Is.False);
                Assert.That(result.SecurityState, Is.EqualTo(PubSubDissectionSecurityState.Encrypted));
                Assert.That(result.SecurityTokenId, Is.EqualTo(secured.Material.TokenId));
                Assert.That(result.DiagnosticMessage, Is.EqualTo("encrypted (key required)"));
            });
        }

        [Test]
        public async Task DissectAsyncDecryptsSecuredUadpWithCapturedKeyAsync()
        {
            SecuredFrame secured = await BuildSecuredFrameAsync().ConfigureAwait(false);
            using CapturedKeyLogKeyResolver resolver = new([secured.Material]);
            PubSubOfflineDissector dissector = new(
                NewContext(),
                resolver,
                secured.Material.SecurityGroupId,
                secured.Material.SecurityPolicyUri);

            PubSubDissectionResult result = await dissector.DissectAsync(CreateFrame(secured.Frame, "opc.udp.uadp"))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.IsDecoded, Is.True);
                Assert.That(result.SecurityState, Is.EqualTo(PubSubDissectionSecurityState.Encrypted));
                Assert.That(result.SecurityTokenId, Is.EqualTo(secured.Material.TokenId));
                Assert.That(result.DiagnosticMessage, Is.EqualTo("decrypted"));
                Assert.That(result.DataSets, Has.Count.EqualTo(1));
                Assert.That(result.DataSets[0].Fields[0].Value, Is.EqualTo(new Variant(42)));
            });
        }

        [Test]
        public void DissectionResultRecordsSupportValueSemantics()
        {
            PubSubDissectionResult first = new()
            {
                Timestamp = Timestamp,
                Direction = PubSubCaptureDirection.Inbound,
                TransportProfileUri = "uadp",
                PayloadLength = 2,
                MessageType = PubSubDissectionMessageType.Uadp,
                SecurityState = PubSubDissectionSecurityState.None,
                PublisherId = PublisherId.FromUInt16(10),
                DataSetWriterIds = [1],
                DataSets =
                [
                    new PubSubDissectedDataSet
                    {
                        DataSetWriterId = 1,
                        SequenceNumber = 2,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        Status = StatusCodes.Good,
                        Fields =
                        [
                            new PubSubDissectedField
                            {
                                Name = "Field",
                                Value = new Variant("value"),
                                StatusCode = StatusCodes.Good,
                                Encoding = PubSubFieldEncoding.Variant
                            }
                        ]
                    }
                ]
            };

            PubSubDissectionResult second = first with { };

            Assert.Multiple(() =>
            {
                Assert.That(second, Is.EqualTo(first));
                Assert.That(second.DataSets[0].Fields[0].Name, Is.EqualTo("Field"));
                Assert.That((int)PubSubDissectionMessageType.Unknown, Is.Zero);
                Assert.That((int)PubSubDissectionSecurityState.Signed, Is.EqualTo(1));
            });
        }

        private static async Task<ReadOnlyMemory<byte>> EncodeUadpAsync()
        {
            var message = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromUInt16(700),
                WriterGroupId = 7,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 100,
                        SequenceNumber = 9,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [new DataSetField { Value = new Variant(42) }]
                    }
                ]
            };
            return await new UadpEncoder().EncodeAsync(message, NewContext()).ConfigureAwait(false);
        }

        private static async Task<ReadOnlyMemory<byte>> EncodeJsonAsync()
        {
            var message = new Encoding.Json.JsonNetworkMessage
            {
                MessageId = "diagnostics-json",
                PublisherId = PublisherId.FromUInt16(701),
                DataSetMessages =
                [
                    new Encoding.Json.JsonDataSetMessage
                    {
                        DataSetWriterId = 101,
                        SequenceNumber = 10,
                        MessageType = PubSubDataSetMessageType.KeyFrame,
                        Fields =
                        [
                            new DataSetField
                            {
                                Name = "Temperature",
                                Value = new Variant(25.5),
                                Encoding = PubSubFieldEncoding.Variant
                            }
                        ]
                    }
                ]
            };
            var encoder = new Encoding.Json.JsonEncoder(
                Encoding.Json.JsonEncodingMode.Verbose);
            return await encoder.EncodeAsync(message, NewContext()).ConfigureAwait(false);
        }

        private static async Task<SecuredFrame> BuildSecuredFrameAsync()
        {
            PubSubAes256CtrPolicy policy = PubSubAes256CtrPolicy.Instance;
            PubSubKeyMaterial material = BuildKeyMaterial(
                "diagnostics-group",
                1234,
                policy.PolicyUri,
                policy.SigningKeyLength,
                policy.EncryptingKeyLength,
                policy.NonceLength);
            using PubSubSecurityKeyRing ring = new(material.SecurityGroupId);
            using PubSubSecurityKey securityKey = new(
                material.TokenId,
                ByteString.Create(material.SigningKey.ToArray()),
                ByteString.Create(material.EncryptingKey.ToArray()),
                ByteString.Create(material.KeyNonce.ToArray()),
                DateTimeUtc.From(DateTime.UtcNow),
                TimeSpan.FromMinutes(30));
            ring.SetCurrent(securityKey);
            using RandomNonceProvider nonceProvider = new(PublisherId.FromUInt32(0));
            var window = new SecurityTokenWindow();
            var wrapper = new UadpSecurityWrapper(
                policy,
                new StaticSecurityKeyProvider(material.SecurityGroupId, ring),
                nonceProvider,
                window,
                TestTelemetryContext.Instance);

            var message = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.PayloadHeader,
                PublisherId = PublisherId.FromUInt16(700),
                WriterGroupId = 7,
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 100,
                        FieldEncoding = PubSubFieldEncoding.Variant,
                        Fields = [new DataSetField { Value = new Variant(42) }]
                    }
                ]
            };
            ReadOnlyMemory<byte> encoded = UadpEncoder.EncodeWithSecurityBoundary(
                message,
                NewContext(),
                out int payloadOffset);
            ReadOnlyMemory<byte> secured = await wrapper.WrapAsync(
                encoded[..payloadOffset],
                encoded[payloadOffset..],
                UadpSecurityWrapOptions.SignAndEncrypt).ConfigureAwait(false);
            return new SecuredFrame(secured.ToArray(), material);
        }

        private static PubSubKeyMaterial BuildKeyMaterial(
            string securityGroupId,
            uint tokenId,
            string securityPolicyUri,
            int signingKeyLength,
            int encryptingKeyLength,
            int nonceLength)
        {
            byte[] signing = Fill(signingKeyLength, tokenId, 31);
            byte[] encrypting = Fill(encryptingKeyLength, tokenId, 17);
            byte[] nonce = Fill(nonceLength, tokenId, 7);
            return new PubSubKeyMaterial(securityGroupId, tokenId, securityPolicyUri, signing, encrypting, nonce);
        }

        private static byte[] Fill(int length, uint tokenId, byte multiplier)
        {
            byte[] bytes = new byte[length];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(((tokenId * multiplier) + (uint)i + 1u) & 0xFF);
            }
            return bytes;
        }

        private static PubSubCaptureFrame CreateFrame(ReadOnlyMemory<byte> data, string profile)
        {
            return new PubSubCaptureFrame(
                Timestamp,
                PubSubCaptureDirection.Inbound,
                profile,
                data,
                "239.0.0.1:4840");
        }

        private static PubSubNetworkMessageContext NewContext()
        {
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(null!),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.Low),
                TimeProvider.System);
        }

        private static readonly DateTimeOffset Timestamp =
            new(2026, 6, 21, 9, 0, 0, TimeSpan.Zero);

        private sealed record SecuredFrame(byte[] Frame, PubSubKeyMaterial Material);

        private sealed class TestTelemetryContext : TelemetryContextBase
        {
            private TestTelemetryContext()
                : base(NullLoggerFactory.Instance)
            {
            }

            public static TestTelemetryContext Instance { get; } = new();
        }
    }
}
