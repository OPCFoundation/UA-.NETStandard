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
using System.IO;
using System.Text;
using NUnit.Framework;

namespace Opc.Ua.XRegistry.Tests
{
    [TestFixture]
    [Category("XRegistry")]
    [Parallelizable]
    public sealed class RegistryValueTests
    {
        [TestCase("9007199254740993", "20000000000001", 0L, true, false)]
        [TestCase("1.00", "64", -2L, false, false)]
        [TestCase("-0.00", "00", -2L, false, true)]
        [TestCase("1e200", "01", 200L, false, false)]
        [TestCase("-129", "FF7F", 0L, true, false)]
        public void NumbersKeepTheirExactNativeForm(
            string json,
            string coefficient,
            long exponent,
            bool isInteger,
            bool negativeZero)
        {
            RegistryValueDataType value = RegistryValues.Parse(Encoding.UTF8.GetBytes(json));
            Assert.That(value, Is.TypeOf<RegistryNumberValueDataType>());
            var number = (RegistryNumberValueDataType)value;
            Assert.Multiple(() =>
            {
                Assert.That(number.Coefficient, Is.EqualTo(ByteString.FromHexString(coefficient)));
                Assert.That(number.Exponent, Is.EqualTo(exponent));
                Assert.That(number.IsInteger, Is.EqualTo(isInteger));
                Assert.That(number.NegativeZero, Is.EqualTo(negativeZero));
                Assert.That(number.Kind, Is.EqualTo(3));
            });
            RegistryValueDataType roundTrip = RegistryValues.Parse(RegistryValues.ToJson(value).Span);
            Assert.That(RegistryValues.Identical(value, roundTrip), Is.True);
        }

        [TestCase("1", "1.0", false)]
        [TestCase("1", "1e0", false)]
        [TestCase("0.0", "-0.0", false)]
        [TestCase("1.0", "1.00", false)]
        [TestCase("true", "1", false)]
        [TestCase("null", "{}", false)]
        [TestCase("[]", "null", false)]
        [TestCase("[1,2]", "[2,1]", false)]
        [TestCase("{\"a\":1,\"b\":null}", "{\"b\":null,\"a\":1}", true)]
        public void CommitIdentityPreservesValueFormButNotObjectMemberOrder(
            string first,
            string second,
            bool expected)
        {
            RegistryValueDataType left = RegistryValues.Parse(Encoding.UTF8.GetBytes(first));
            RegistryValueDataType right = RegistryValues.Parse(Encoding.UTF8.GetBytes(second));
            Assert.That(RegistryValues.Identical(left, right), Is.EqualTo(expected));
        }

        [Test]
        public void NullNativeArrayIsRejectedInsteadOfBecomingEmptyJson()
        {
            var array = new RegistryArrayValueDataType { Kind = 4, Items = ArrayOf<RegistryValueDataType>.Null };
            Assert.That(array.Items.IsNull, Is.True);
            Assert.That(() => RegistryValues.Validate(array), Throws.ArgumentException);
            Assert.That(() => RegistryValues.ToJson(array), Throws.ArgumentException);
            Assert.That(() => RegistryValues.Identical(array, RegistryValues.Parse("[]"u8)), Throws.ArgumentException);
            using var stream = new MemoryStream(ByteString.FromHexString("04000000FFFFFFFF").ToArray());
            using var decoder = new BinaryDecoder(stream, ServiceMessageContext.Create(null), true);
            var decoded = new RegistryArrayValueDataType();
            decoded.Decode(decoder);
            Assert.That(decoded.Items.IsNull, Is.True);
            Assert.That(() => RegistryValues.Validate(decoded), Throws.ArgumentException);
        }

        [Test]
        public void NativeChangesDistinguishExplicitNullAndRemovalAndLeaveTheSourceUntouched()
        {
            RegistryValueDataType original = RegistryValues.Parse(
                Encoding.UTF8.GetBytes("{\"name\":\"old\",\"items\":[1,2]}"));
            ArrayOf<RegistryChangeDataType> changes =
            [
                new RegistryChangeDataType
                {
                    Operation = 0,
                    Path = [new RegistryPathElementDataType { Kind = 0, Name = "name" }],
                    Value = new RegistryNullValueDataType { Kind = 0 }
                },
                new RegistryChangeDataType
                {
                    Operation = 1,
                    Path =
                    [
                        new RegistryPathElementDataType { Kind = 0, Name = "items" },
                        new RegistryPathElementDataType { Kind = 1, Index = 0, Name = string.Empty }
                    ],
                    Value = null!
                }
            ];
            RegistryValueDataType changed = RegistryValues.ApplyChanges(original, changes);
            Assert.That(Encoding.UTF8.GetString(RegistryValues.ToJson(original).ToArray()),
                Is.EqualTo("{\"name\":\"old\",\"items\":[1,2]}"));
            Assert.That(Encoding.UTF8.GetString(RegistryValues.ToJson(changed).ToArray()),
                Is.EqualTo("{\"name\":null,\"items\":[2]}"));
        }

        [Test]
        public void InvalidNativeChangeBatchDoesNotManufactureParentsOrMutateItsInput()
        {
            RegistryValueDataType original = RegistryValues.Parse(Encoding.UTF8.GetBytes("{\"name\":\"old\"}"));
            ArrayOf<RegistryChangeDataType> changes =
            [
                new RegistryChangeDataType
                {
                    Operation = 0,
                    Path = [new RegistryPathElementDataType { Kind = 0, Name = "name" }],
                    Value = new RegistryStringValueDataType { Kind = 2, Value = "new" }
                },
                new RegistryChangeDataType
                {
                    Operation = 0,
                    Path =
                    [
                        new RegistryPathElementDataType { Kind = 0, Name = "missing" },
                        new RegistryPathElementDataType { Kind = 0, Name = "child" }
                    ],
                    Value = new RegistryNullValueDataType { Kind = 0 }
                }
            ];
            Assert.Throws<ArgumentException>(() => RegistryValues.ApplyChanges(original, changes));
            Assert.That(Encoding.UTF8.GetString(RegistryValues.ToJson(original).ToArray()),
                Is.EqualTo("{\"name\":\"old\"}"));
        }

        [Test]
        public void IndependentBinaryVectorsPreserveInheritedFieldsAndConcreteEncodingIds()
        {
            string[] documents = ["1.00", "-0.00", "{\"vendor\":[9007199254740993,false,null]}"];
            string[] expected =
            [
                "020100B20701000113000000030000000100000064FEFFFFFFFFFFFFFF0000",
                "020100B20701000113000000030000000100000000FEFFFFFFFFFFFFFF0001",
                "020100B8070100016C00000005000000010000000600000076656E646F72020100B4070100014E000000" +
                "0400000003000000020100B207010001190000000300000007000000200000000000010000000000000000" +
                "0100020100AE07010001050000000100000000020100AC070100010400000000000000"
            ];
            var context = ServiceMessageContext.Create(null);
            context.NamespaceUris.GetIndexOrAppend(XRegistryWellKnown.XRegistryNamespaceUri);
            context.Factory.Builder.AddOpcUaXRegistry().Commit();
            for (int index = 0; index < documents.Length; index++)
            {
                RegistryValueDataType value = RegistryValues.Parse(Encoding.UTF8.GetBytes(documents[index]));
                ByteString wire = ByteString.FromHexString(expected[index]);
                using var stream = new MemoryStream();
                using (var encoder = new BinaryEncoder(stream, context, true))
                {
                    encoder.WriteExtensionObject("Value", new ExtensionObject(value));
                }
                Assert.That(ByteString.From(stream.ToArray()), Is.EqualTo(wire), documents[index]);
                stream.Position = 0;
                using var decoder = new BinaryDecoder(stream, context, true);
                ExtensionObject decoded = decoder.ReadExtensionObject("Value");
                Assert.That(decoded.TryGetValue(out RegistryValueDataType? restored), Is.True);
                Assert.That(RegistryValues.Identical(value, restored!), Is.True);
            }
        }

        [Test]
        public void NativeFieldTraversalUsesTheGeneratedEncoderWithoutJsonOrReflection()
        {
            var number = new RegistryNumberValueDataType
            {
                Kind = 3,
                Coefficient = ByteString.FromHexString("64"),
                Exponent = -2,
                IsInteger = false,
                NegativeZero = false
            };
            ArrayOf<RegistryNativeField> fields = RegistryNativeFields.Read(
                number, ServiceMessageContext.Create(null));
            Assert.That(fields.Count, Is.EqualTo(5));
            Assert.That(fields[0].Name, Is.EqualTo("Kind"));
            Assert.That(fields[0].Value.TryGetValue(out uint kind), Is.True);
            Assert.That(kind, Is.EqualTo(3));
            Assert.That(fields[1].Name, Is.EqualTo("Coefficient"));
            Assert.That(fields[1].Value.TryGetValue(out ByteString coefficient), Is.True);
            Assert.That(coefficient, Is.EqualTo(ByteString.FromHexString("64")));
            Assert.That(fields[2].Value.TryGetValue(out long exponent), Is.True);
            Assert.That(exponent, Is.EqualTo(-2));
        }

        [Test]
        public void UnknownValueSubtypesCannotLoseFieldsThroughTheJsonBridge()
        {
            var value = new VendorStringValue { Kind = 2, Value = "base", VendorValue = "retained" };
            Assert.Throws<ArgumentException>(() => RegistryValues.ToJson(value));
        }

        private sealed class VendorStringValue : RegistryStringValueDataType
        {
            public string VendorValue { get; set; } = string.Empty;
        }

        [Test]
        public void DuplicateNamesAndDepthLimitsAreRejected()
        {
            Assert.Throws<ArgumentException>(() =>
                RegistryValues.Parse(Encoding.UTF8.GetBytes("{\"x\":1,\"x\":2}")));
            Assert.Throws<ArgumentException>(() =>
                RegistryValues.Parse(Encoding.UTF8.GetBytes("[[[1]]]"), maxDepth: 2));
        }
    }
}
