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
using System.Text;
using NUnit.Framework;
using Opc.Ua.SchemaRegistry.Formats;
using Opc.Ua.XRegistry;

namespace Opc.Ua.SchemaRegistry.Tests
{
    [TestFixture]
    [Category("SchemaRegistry")]
    public sealed class AvroSchemaFormatTests
    {
        [TestCase("\"string\"", "C70345637248018F")]
        [TestCase("{\"type\":\"fixed\",\"name\":\"D\",\"namespace\":\"n.s\",\"size\":12,\"logicalType\":\"duration\"}",
            "61CEAB14C07EF825")]
        [TestCase("{\"type\":\"record\",\"name\":\"Node\",\"namespace\":\"n\",\"fields\":[" +
            "{\"name\":\"next\",\"type\":[\"null\",\"Node\"],\"default\":null}]}", "14BCC4B6E65459A2")]
        public void FingerprintMatchesIndependentParsingCanonicalFormVectors(string source, string expected)
        {
            var provider = new AvroSchemaFormatProvider();
            Assert.That(provider.ComputeSchemaId(Encoding.UTF8.GetBytes(source)),
                Is.EqualTo(ByteString.FromHexString(expected)));
        }

        [Test]
        public void DefaultsCustomPropertiesAndNamedReferencesRemainExactNativeValues()
        {
            const string source = "{\"type\":\"record\",\"name\":\"Record\",\"namespace\":\"test\",\"x-vendor\":1.00," +
                "\"fields\":[{\"name\":\"date\",\"type\":\"string\",\"default\":\"2020-01-01T00:00:00.000Z\"}," +
                "{\"name\":\"amount\",\"type\":\"double\",\"default\":1.10}," +
                "{\"name\":\"next\",\"type\":[\"null\",\"Record\"],\"default\":null}]}";
            var provider = new AvroSchemaFormatProvider();
            var native = (AvroSchemaContentDataType)provider.Parse(Encoding.UTF8.GetBytes(source));
            var declaration = ((AvroObjectDataType)native.Root).Declaration;
            Assert.That(declaration.Name, Is.EqualTo("Record"));
            Assert.That(((RegistryStringValueDataType)declaration.Fields[0].Default).Value,
                Is.EqualTo("2020-01-01T00:00:00.000Z"));
            var amount = (RegistryNumberValueDataType)declaration.Fields[1].Default;
            Assert.That(amount.Coefficient, Is.EqualTo(ByteString.FromHexString("6E")));
            Assert.That(amount.Exponent, Is.EqualTo(-2));
            var union = (AvroUnionDataType)declaration.Fields[2].Type;
            Assert.That(((AvroNameDataType)union.Branches[1]).Name, Is.EqualTo("Record"));
            Assert.That(RegistryValues.Identical(RegistryValues.Parse(Encoding.UTF8.GetBytes(source)),
                RegistryValues.Parse(provider.Serialize(native).Span)), Is.True);
        }

        [TestCase("{\"type\":\"record\",\"name\":\"A\",\"fields\":[{\"name\":\"x\",\"type\":\"Missing\"}]}")]
        [TestCase("[\"int\",\"int\"]")]
        [TestCase("{\"type\":\"fixed\",\"name\":\"A\",\"size\":-1}")]
        [TestCase("{\"type\":\"enum\",\"name\":\"A\",\"symbols\":[\"x\",\"x\"]}")]
        [TestCase("{\"type\":\"record\",\"name\":\"A\",\"fields\":[],\"name\":\"B\"}")]
        public void InvalidGrammarAndDuplicateMembersAreRejected(string source)
        {
            var provider = new AvroSchemaFormatProvider();
            Assert.Throws<ArgumentException>(() => provider.Parse(Encoding.UTF8.GetBytes(source)));
        }

        [TestCase("{\"type\":\"record\",\"name\":\"A\",\"fields\":[{\"name\":\"x\",\"type\":\"int\",\"default\":true}]}")]
        [TestCase("{\"type\":\"record\",\"name\":\"A\",\"fields\":[{\"name\":\"x\",\"type\":[\"string\",\"null\"],\"default\":null}]}")]
        [TestCase("{\"type\":\"fixed\",\"name\":\"A\",\"size\":16.0}")]
        [TestCase("{\"type\":\"record\",\"name\":\"string\",\"fields\":[]}")]
        public void DefaultsAndIntegerGrammarFollowAvro111(string source)
        {
            var provider = new AvroSchemaFormatProvider();
            Assert.Throws<ArgumentException>(() => provider.Parse(Encoding.UTF8.GetBytes(source)));
        }

        [Test]
        public void EmptyFixedAndEmptyUnionRetainTheirNativeGrammar()
        {
            var provider = new AvroSchemaFormatProvider();
            foreach (string source in new[] { "{\"type\":\"fixed\",\"name\":\"Empty\",\"size\":0}", "[]" })
            {
                SchemaContentDataType content = provider.Parse(Encoding.UTF8.GetBytes(source));
                Assert.That(RegistryValues.Identical(RegistryValues.Parse(Encoding.UTF8.GetBytes(source)),
                    RegistryValues.Parse(provider.Serialize(content).Span)), Is.True);
            }
        }
    }
}
