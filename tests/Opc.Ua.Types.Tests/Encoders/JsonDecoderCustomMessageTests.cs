// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Runtime.Serialization;
using NUnit.Framework;

namespace Opc.Ua.Types.Tests.Encoders
{
    [TestFixture]
    [Category("Json")]
    public sealed class JsonDecoderCustomMessageTests
    {
        [Test]
        public void CustomMessageNormalizesTypeIdAndInfersEncodingMask()
        {
            var context = ServiceMessageContext.Create(null);
            context.NamespaceUris.GetIndexOrAppend(OptionalEncodeable.NamespaceUri);
            context.Factory.Builder
                .AddType(OptionalEncodeable.DataTypeId, typeof(OptionalEncodeable))
                .AddType(OptionalEncodeable.BinaryId, typeof(OptionalEncodeable))
                .AddType(OptionalEncodeable.XmlId, typeof(OptionalEncodeable))
                .Commit();
            var expected = new OptionalEncodeable
            {
                EncodingMask = 1,
                Required = 42,
                Optional = 84
            };

            string json;
            using (var encoder = new JsonEncoder(context))
            {
                encoder.EncodeMessage(expected, expected.TypeId);
                json = encoder.CloseAndReturnText();
            }
            using var decoder = new JsonDecoder(json, context);
            OptionalEncodeable actual =
                decoder.DecodeMessage<OptionalEncodeable>();

            Assert.Multiple(() =>
            {
                Assert.That(actual.EncodingMask, Is.EqualTo(1));
                Assert.That(actual.Required, Is.EqualTo(42));
                Assert.That(actual.Optional, Is.EqualTo(84));
            });
        }

        [DataContract(Namespace = NamespaceUri)]
        public sealed class OptionalEncodeable : IEncodeable
        {
            public const string NamespaceUri = "urn:opcfoundation:json:test";

            public static readonly ExpandedNodeId DataTypeId =
                new(9001, NamespaceUri);

            public static readonly ExpandedNodeId BinaryId =
                new(9002, NamespaceUri);

            public static readonly ExpandedNodeId XmlId =
                new(9003, NamespaceUri);

            public uint EncodingMask { get; set; }

            public int Required { get; set; }

            public int Optional { get; set; }

            public ExpandedNodeId TypeId => DataTypeId;

            public ExpandedNodeId BinaryEncodingId => BinaryId;

            public ExpandedNodeId XmlEncodingId => XmlId;

            public void Encode(IEncoder encoder)
            {
                encoder.PushNamespace(NamespaceUri);
                encoder.WriteEncodingMask(EncodingMask);
                encoder.WriteInt32(nameof(Required), Required);
                if ((EncodingMask & 1) != 0)
                {
                    encoder.WriteInt32(nameof(Optional), Optional);
                }
                encoder.PopNamespace();
            }

            public void Decode(IDecoder decoder)
            {
                decoder.PushNamespace(NamespaceUri);
                EncodingMask = decoder.ReadEncodingMask([nameof(Optional)]);
                Required = decoder.ReadInt32(nameof(Required));
                if ((EncodingMask & 1) != 0)
                {
                    Optional = decoder.ReadInt32(nameof(Optional));
                }
                decoder.PopNamespace();
            }

            public bool IsEqual(IEncodeable? encodeable)
            {
                return encodeable is OptionalEncodeable other &&
                    EncodingMask == other.EncodingMask &&
                    Required == other.Required &&
                    Optional == other.Optional;
            }

            public object Clone()
            {
                return new OptionalEncodeable
                {
                    EncodingMask = EncodingMask,
                    Required = Required,
                    Optional = Optional
                };
            }
        }
    }
}
