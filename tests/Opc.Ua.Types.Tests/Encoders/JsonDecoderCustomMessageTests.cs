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

            public bool IsEqual(IEncodeable encodeable)
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
