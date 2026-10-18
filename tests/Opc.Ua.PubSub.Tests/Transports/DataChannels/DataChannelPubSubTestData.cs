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
using System.Security.Cryptography;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using Opc.Ua.Tests;
using UadpDataSetMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpDataSetMessage;
using UadpNetworkMessage = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Tests.Transports.DataChannels
{
    /// <summary>
    /// Independent codec inputs and ephemeral security material shared by flow transport integration tests.
    /// </summary>
    internal static class DataChannelPubSubTestData
    {
        /// <summary>
        /// Creates a real codec context, not a transport-specific serializer.
        /// </summary>
        public static PubSubNetworkMessageContext CreateMessageContext()
        {
            return new PubSubNetworkMessageContext(
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()),
                new DataSetMetaDataRegistry(),
                new PubSubDiagnostics(PubSubDiagnosticsLevel.High),
                TimeProvider.System);
        }

        /// <summary>
        /// Encodes optional Part 14 headers and two writers, optionally applying the existing message-security wrapper.
        /// </summary>
        public static async ValueTask<ByteString> EncodeUadpAsync(bool secured)
        {
            PubSubNetworkMessageContext context = CreateMessageContext();
            var message = new UadpNetworkMessage
            {
                ContentMask = UadpNetworkMessageContentMask.PublisherId |
                    UadpNetworkMessageContentMask.GroupHeader |
                    UadpNetworkMessageContentMask.WriterGroupId |
                    UadpNetworkMessageContentMask.GroupVersion |
                    UadpNetworkMessageContentMask.NetworkMessageNumber |
                    UadpNetworkMessageContentMask.SequenceNumber |
                    UadpNetworkMessageContentMask.PayloadHeader |
                    UadpNetworkMessageContentMask.DataSetClassId |
                    UadpNetworkMessageContentMask.Timestamp |
                    UadpNetworkMessageContentMask.PicoSeconds |
                    UadpNetworkMessageContentMask.PromotedFields,
                PublisherId = PublisherId.FromUInt32(73001),
                WriterGroupId = 42,
                GroupVersion = 0x10203040,
                NetworkMessageNumber = 9,
                SequenceNumber = 65530,
                DataSetClassId = new Uuid(new Guid("195af8f3-1f39-43ae-b9bc-c3e009e48e9c")),
                Timestamp = Timestamp,
                PicoSeconds = 1234,
                PromotedFields = [new DataSetField { Name = "RoutingValue", Value = new Variant(73) }],
                DataSetMessages =
                [
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 1001,
                        ContentMask = UadpDataSetMessageContentMask.SequenceNumber |
                            UadpDataSetMessageContentMask.Timestamp |
                            UadpDataSetMessageContentMask.PicoSeconds |
                            UadpDataSetMessageContentMask.Status |
                            UadpDataSetMessageContentMask.MajorVersion |
                            UadpDataSetMessageContentMask.MinorVersion,
                        SequenceNumber = 105,
                        Timestamp = Timestamp,
                        PicoSeconds = 321,
                        MetaDataVersion = new ConfigurationVersionDataType { MajorVersion = 7, MinorVersion = 3 },
                        Status = StatusCodes.Good,
                        Fields = [new DataSetField { Name = "Counter", Value = new Variant(-1234567) }]
                    },
                    new UadpDataSetMessage
                    {
                        DataSetWriterId = 1002,
                        Fields = [new DataSetField { Name = "Text", Value = new Variant("retained \u03a9") }]
                    }
                ]
            };
            if (!secured)
            {
                return new ByteString(await new UadpEncoder().EncodeAsync(message, context).ConfigureAwait(false));
            }
            using PubSubSecurityKeyRing ring = CreateKeyRing();
            var wrapper = new UadpSecurityWrapper(
                PubSubAes256CtrPolicy.Instance,
                new StaticSecurityKeyProvider(SecurityGroup, ring),
                new RandomNonceProvider(message.PublisherId),
                new SecurityTokenWindow(),
                NUnitTelemetryContext.Create());
            ReadOnlyMemory<byte> encoded = UadpEncoder.EncodeWithSecurityBoundary(message, context, out int offset);
            return new ByteString(await wrapper.WrapAsync(
                encoded[..offset], encoded[offset..], UadpSecurityWrapOptions.SignAndEncrypt).ConfigureAwait(false));
        }

        /// <summary>
        /// Creates ephemeral keys in the existing key-provider system; no broker or SKS endpoint is contacted.
        /// </summary>
        public static PubSubSecurityKeyRing CreateKeyRing()
        {
            PubSubAes256CtrPolicy policy = PubSubAes256CtrPolicy.Instance;
            using var random = RandomNumberGenerator.Create();
            byte[] signing = new byte[policy.SigningKeyLength];
            byte[] encrypting = new byte[policy.EncryptingKeyLength];
            byte[] nonce = new byte[AesCtrNonceLayout.KeyNonceLength];
            random.GetBytes(signing);
            random.GetBytes(encrypting);
            random.GetBytes(nonce);
            var ring = new PubSubSecurityKeyRing(SecurityGroup);
            ring.SetCurrent(new PubSubSecurityKey(
                71, ByteString.From(signing), ByteString.From(encrypting), ByteString.From(nonce),
                DateTimeUtc.From(DateTime.UtcNow), TimeSpan.FromHours(1)));
            return ring;
        }

        /// <summary>The test-only security group resolved by the existing provider pipeline.</summary>
        public const string SecurityGroup = "DataChannelPubSubTestGroup";

        /// <summary>A stable nonzero timestamp for exact optional-field assertions.</summary>
        public static readonly DateTimeUtc Timestamp = DateTimeUtc.From(new DateTime(2026, 10, 16, 12, 34, 56,
            DateTimeKind.Utc));
    }
}
