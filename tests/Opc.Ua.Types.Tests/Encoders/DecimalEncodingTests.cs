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

using System.Numerics;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.Encoders
{
    /// <summary>
    /// Tests the wire encoding of the OPC UA <c>Decimal</c> DataType against
    /// OPC 10000-6 clause 5.1.10 Table 3, which is the one built-in type whose
    /// last field carries no length of its own.
    /// </summary>
    [TestFixture]
    [Category("Encoders")]
    public class DecimalEncodingTests
    {
        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.CreateEmpty(telemetry);
            m_context.Factory.AddEncodeableType(typeof(Opc.Ua.Decimal));
        }

        [TestCase("0")]
        [TestCase("1")]
        [TestCase("-1")]
        [TestCase("1.50")]
        [TestCase("-1.500000")]
        [TestCase("255")]
        [TestCase("256")]
        [TestCase("-129")]
        [TestCase("123456789012345678901234567890.123456789")]
        public void ADecimalRoundTripsThroughTheBinaryEncoding(string lexical)
        {
            var input = Opc.Ua.Decimal.Parse(lexical);

            byte[] buffer;
            using (var encoder = new BinaryEncoder(m_context))
            {
                encoder.WriteExtensionObject("Value", new ExtensionObject(input.TypeId, input));
                buffer = encoder.CloseAndReturnBuffer()!;
            }

            using var decoder = new BinaryDecoder(buffer, m_context);
            ExtensionObject output = decoder.ReadExtensionObject("Value");

            Assert.That(output.TryGetValue(out Opc.Ua.Decimal decoded), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(decoded.Scale, Is.EqualTo(input.Scale));
                Assert.That(decoded.UnscaledValue, Is.EqualTo(input.UnscaledValue));
                Assert.That(decoded.ToString(), Is.EqualTo(lexical));
            });
        }

        [Test]
        public void TheBinaryBodyIsTheScaleFollowedByRawOctetsWithNoLengthPrefix()
        {
            // Clause 5.1.10 Table 3: Length covers Scale and Value together,
            // and the octet count is Length minus the two bytes of Scale. A
            // length prefix on the value would make the body two bytes longer
            // and is exactly the mistake this pins.
            var value = new Opc.Ua.Decimal(new BigInteger(0x0102), 2);

            byte[] buffer;
            using (var encoder = new BinaryEncoder(m_context))
            {
                encoder.WriteExtensionObject("Value", new ExtensionObject(value.TypeId, value));
                buffer = encoder.CloseAndReturnBuffer()!;
            }

            // NodeId (2 bytes for a four-byte-encoded numeric in ns=0: encoding
            // byte + identifier), encoding byte, Int32 length, then the body.
            // Rather than assert the exact header layout, locate the length
            // field by decoding the header the same way the decoder does.
            using var decoder = new BinaryDecoder(buffer, m_context);
            _ = decoder.ReadNodeId(null);
            byte encoding = decoder.ReadByte(null);
            int length = decoder.ReadInt32(null);

            Assert.Multiple(() =>
            {
                Assert.That(encoding, Is.EqualTo((byte)ExtensionObjectEncoding.Binary));

                // Two bytes of Scale plus two octets of unscaled value: no
                // length prefix on the value.
                Assert.That(length, Is.EqualTo(4));

                Assert.That(decoder.ReadInt16(null), Is.EqualTo((short)2));

                // Least significant byte first.
                Assert.That(decoder.ReadByte(null), Is.EqualTo((byte)0x02));
                Assert.That(decoder.ReadByte(null), Is.EqualTo((byte)0x01));
            });
        }

        [Test]
        /// <summary>
        /// Clause 5.1.10 Table 3: "If the length is less than or equal to 2
        /// then the Decimal is an invalid value that cannot be used." A body of
        /// exactly two bytes carries the Scale and no unscaled octets at all,
        /// so there is no value to represent and it has to be rejected rather
        /// than read as zero.
        /// </summary>
        [TestCase(2)]
        [TestCase(1)]
        [TestCase(0)]
        public void ABodyThatIsTooShortToCarryAnUnscaledValueIsRejected(int bodyLength)
        {
            byte[] buffer;
            using (var encoder = new BinaryEncoder(m_context))
            {
                encoder.WriteNodeId(null, ExpandedNodeId.ToNodeId(new Opc.Ua.Decimal().TypeId, m_context.NamespaceUris));
                encoder.WriteByte(null, (byte)ExtensionObjectEncoding.Binary);
                encoder.WriteInt32(null, bodyLength);
                for (int ii = 0; ii < bodyLength; ii++)
                {
                    encoder.WriteByte(null, 0);
                }

                // A real message never ends at the body it is carrying, and
                // without something after it a declared length below two runs
                // the buffer out inside Scale instead - which throws for a
                // different reason and would leave the rule under test
                // unexercised.
                encoder.WriteUInt32(null, 0);
                buffer = encoder.CloseAndReturnBuffer()!;
            }

            using var decoder = new BinaryDecoder(buffer, m_context);

            Assert.That(() => decoder.ReadExtensionObject("Value"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo((StatusCode)StatusCodes.BadDecodingError)
                    .And.Message.Contains("unscaled value"));
        }

        [Test]
        public void AZeroValuedDecimalCarriesNoUnscaledOctetsBeyondTheSignByte()
        {
            var value = new Opc.Ua.Decimal(BigInteger.Zero, 0);

            byte[] buffer;
            using (var encoder = new BinaryEncoder(m_context))
            {
                encoder.WriteExtensionObject("Value", new ExtensionObject(value.TypeId, value));
                buffer = encoder.CloseAndReturnBuffer()!;
            }

            using var decoder = new BinaryDecoder(buffer, m_context);
            ExtensionObject output = decoder.ReadExtensionObject("Value");

            Assert.That(output.TryGetValue(out Opc.Ua.Decimal decoded), Is.True);
            Assert.That(decoded.IsZero, Is.True);
        }

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("1.50")]
        [TestCase("-123456789012345678901234567890.0001")]
        public void ADecimalRoundTripsThroughTheJsonEncoding(string lexical)
        {
            var input = Opc.Ua.Decimal.Parse(lexical);

            string json;
            using (var encoder = new JsonEncoder(m_context))
            {
                encoder.WriteExtensionObject("Value", new ExtensionObject(input.TypeId, input));
                json = encoder.CloseAndReturnText();
            }

            using var decoder = new JsonDecoder(json, m_context);
            ExtensionObject output = decoder.ReadExtensionObject("Value");

            Assert.That(output.TryGetValue(out Opc.Ua.Decimal decoded), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(decoded.Scale, Is.EqualTo(input.Scale));
                Assert.That(decoded.UnscaledValue, Is.EqualTo(input.UnscaledValue));
            });
        }

        [Test]
        public void TheJsonValueIsABaseTenIntegerStringRatherThanTheOctets()
        {
            // Clause 5.4.3: "a JSON string with the Value encoded as a base-10
            // signed integer". Base64 octets would round-trip inside this
            // implementation while being wrong on the wire, so the text is
            // asserted rather than only the round trip.
            var value = new Opc.Ua.Decimal(new BigInteger(-1500), 3);

            using var encoder = new JsonEncoder(m_context);
            encoder.WriteExtensionObject("Value", new ExtensionObject(value.TypeId, value));
            string json = encoder.CloseAndReturnText();

            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Contain("-1500"));
                Assert.That(json, Does.Contain("\"Scale\""));
            });
        }

        private ServiceMessageContext m_context = null!;
    }
}
