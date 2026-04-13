/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Encoding
{
    [TestFixture(Description = "Coverage tests for PubSubJsonDecoder")]
    [Parallelizable]
    public class PubSubJsonDecoderTests
    {
        private ServiceMessageContext m_context;

        [SetUp]
        public void SetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            m_context = ServiceMessageContext.Create(telemetry);
        }

        [Test]
        public void ConstructorWithStringCreatesDecoder()
        {
            const string json = /*lang=json,strict*/ "{\"Field1\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Assert.That(decoder.Context, Is.Not.Null);
        }

        [Test]
        public void ConstructorWithNullContextThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                () => new PubSubJsonDecoder("{}", null));
        }

        [Test]
        public void ReadBooleanReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Flag\": true}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            bool result = decoder.ReadBoolean("Flag");
            Assert.That(result, Is.True);
        }

        [Test]
        public void ReadInt32ReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Number\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            int result = decoder.ReadInt32("Number");
            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void ReadUInt32ReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Value\": 100}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            uint result = decoder.ReadUInt32("Value");
            Assert.That(result, Is.EqualTo(100));
        }

        [Test]
        public void ReadStringReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Name\": \"Test\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            string result = decoder.ReadString("Name");
            Assert.That(result, Is.EqualTo("Test"));
        }

        [Test]
        public void ReadDoubleReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Value\": 3.14}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            double result = decoder.ReadDouble("Value");
            Assert.That(result, Is.EqualTo(3.14).Within(0.001));
        }

        [Test]
        public void ReadFloatReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Value\": 1.5}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            float result = decoder.ReadFloat("Value");
            Assert.That(result, Is.EqualTo(1.5f).Within(0.01f));
        }

        [Test]
        public void ReadByteReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Value\": 255}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            byte result = decoder.ReadByte("Value");
            Assert.That(result, Is.EqualTo(255));
        }

        [Test]
        public void ReadSByteReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Value\": -1}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            sbyte result = decoder.ReadSByte("Value");
            Assert.That(result, Is.EqualTo(-1));
        }

        [Test]
        public void ReadInt16ReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Value\": -32000}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            short result = decoder.ReadInt16("Value");
            Assert.That(result, Is.EqualTo(-32000));
        }

        [Test]
        public void ReadUInt16ReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Value\": 65000}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ushort result = decoder.ReadUInt16("Value");
            Assert.That(result, Is.EqualTo(65000));
        }

        [Test]
        public void ReadInt64ReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Value\": \"999999999\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            long result = decoder.ReadInt64("Value");
            Assert.That(result, Is.EqualTo(999999999));
        }

        [Test]
        public void ReadUInt64ReturnsCorrectValue()
        {
            const string json = /*lang=json,strict*/ "{\"Value\": \"999999999\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            ulong result = decoder.ReadUInt64("Value");
            Assert.That(result, Is.EqualTo(999999999));
        }

        [Test]
        public void ReadMissingFieldReturnsDefault()
        {
            const string json = /*lang=json,strict*/ "{\"Other\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            int result = decoder.ReadInt32("Missing");
            Assert.That(result, Is.EqualTo(0));
        }

        [Test]
        public void ReadSwitchFieldWithSwitchFieldKeyReturnsSwitchValue()
        {
            const string json = /*lang=json,strict*/ "{\"SwitchField\": 2, \"Value\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var switches = new List<string> { "Option1", "Option2", "Option3" };
            uint index = decoder.ReadSwitchField(switches, out string fieldName);

            Assert.That(index, Is.EqualTo(2));
            Assert.That(fieldName, Is.EqualTo("Value"));
        }

        [Test]
        public void ReadSwitchFieldWithoutSwitchFieldKeyMatchesByFieldName()
        {
            const string json = /*lang=json,strict*/ "{\"Option2\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var switches = new List<string> { "Option1", "Option2", "Option3" };
            uint index = decoder.ReadSwitchField(switches, out string fieldName);

            Assert.That(index, Is.EqualTo(2));
            Assert.That(fieldName, Is.EqualTo("Option2"));
        }

        [Test]
        public void ReadSwitchFieldWithNullSwitchesReturnsZero()
        {
            const string json = /*lang=json,strict*/ "{\"SwitchField\": 2}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            uint index = decoder.ReadSwitchField(null, out string fieldName);
            Assert.That(index, Is.EqualTo(0));
        }

        [Test]
        public void ReadSwitchFieldWithIndexExceedingSwitchCountReturnsIndex()
        {
            const string json = /*lang=json,strict*/ "{\"SwitchField\": 10}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var switches = new List<string> { "Option1", "Option2" };
            uint index = decoder.ReadSwitchField(switches, out string fieldName);

            Assert.That(index, Is.EqualTo(10));
        }

        [Test]
        public void ReadSwitchFieldWithNoMatchReturnsZero()
        {
            const string json = /*lang=json,strict*/ "{\"UnrelatedField\": 42}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var switches = new List<string> { "Option1", "Option2" };
            uint index = decoder.ReadSwitchField(switches, out string fieldName);

            Assert.That(index, Is.EqualTo(0));
        }

        [Test]
        public void ReadEncodingMaskWithEncodingMaskKeyReturnsValue()
        {
            const string json = /*lang=json,strict*/ "{\"EncodingMask\": 7}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var masks = new List<string> { "Field1", "Field2", "Field3" };
            uint result = decoder.ReadEncodingMask(masks);

            Assert.That(result, Is.EqualTo(7));
        }

        [Test]
        public void ReadEncodingMaskWithoutKeyComputesMaskFromFields()
        {
            const string json = /*lang=json,strict*/ "{\"Field1\": 1, \"Field3\": 3}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var masks = new List<string> { "Field1", "Field2", "Field3" };
            uint result = decoder.ReadEncodingMask(masks);

            Assert.That(result & 0x01, Is.EqualTo(1), "Field1 bit should be set");
            Assert.That(result & 0x02, Is.EqualTo(0), "Field2 bit should not be set");
            Assert.That(result & 0x04, Is.EqualTo(4), "Field3 bit should be set");
        }

        [Test]
        public void ReadEncodingMaskWithNullMasksReturnsZero()
        {
            const string json = /*lang=json,strict*/ "{\"Field1\": 1}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            uint result = decoder.ReadEncodingMask(null);
            Assert.That(result, Is.EqualTo(0));
        }

        [Test]
        public void ReadEncodingMaskWithEmptyObjectReturnsZero()
        {
            const string json = "{}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var masks = new List<string> { "Field1", "Field2" };
            uint result = decoder.ReadEncodingMask(masks);

            Assert.That(result, Is.EqualTo(0));
        }

        [Test]
        public void SetMappingTablesWithNullsDoesNotThrow()
        {
            const string json = "{}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Assert.DoesNotThrow(() => decoder.SetMappingTables(null, null));
        }

        [Test]
        public void SetMappingTablesWithValidTablesDoesNotThrow()
        {
            const string json = "{}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Assert.DoesNotThrow(
                () => decoder.SetMappingTables(new NamespaceTable(), new StringTable()));
        }

        [Test]
        public void PushAndPopStructureNavigatesJson()
        {
            const string json = /*lang=json,strict*/ "{\"Outer\": {\"Inner\": 42}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            decoder.PushStructure("Outer");
            int value = decoder.ReadInt32("Inner");
            decoder.Pop();

            Assert.That(value, Is.EqualTo(42));
        }

        [Test]
        public void ReadNullStringReturnsNull()
        {
            const string json = /*lang=json,strict*/ "{\"Value\": null}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            string result = decoder.ReadString("Value");
            Assert.That(result, Is.Null);
        }

        [Test]
        public void DecodeMessageFromBufferWithNullContextThrowsArgumentNullException()
        {
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes("{}");
            Assert.Throws<ArgumentNullException>(
                () => PubSubJsonDecoder.DecodeMessage<ReadResponse>(buffer, null));
        }

        [Test]
        public void ReadDateTimeReturnsValue()
        {
            const string isoDate = "2024-01-15T10:30:00Z";
            string json = "{\"Timestamp\": \"" + isoDate + "\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            DateTimeUtc result = decoder.ReadDateTime("Timestamp");
            Assert.That(((DateTime)result).Year, Is.EqualTo(2024));
            Assert.That(((DateTime)result).Month, Is.EqualTo(1));
        }

        [Test]
        public void ReadGuidReturnsValue()
        {
            Guid expected = Guid.NewGuid();
            string json = "{\"Id\": \"" + expected + "\"}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            Uuid result = decoder.ReadGuid("Id");
            Assert.That((Guid)result, Is.EqualTo(expected));
        }

        [Test]
        public void DisposeMultipleTimesDoesNotThrow()
        {
            var decoder = new PubSubJsonDecoder("{}", m_context);
            decoder.Dispose();
            Assert.DoesNotThrow(decoder.Dispose);
        }

        [Test]
        public void ReadSwitchFieldWithSwitchFieldAndNoValueKeyUsesFieldName()
        {
            const string json = /*lang=json,strict*/ "{\"SwitchField\": 1}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var switches = new List<string> { "First", "Second" };
            uint index = decoder.ReadSwitchField(switches, out string fieldName);

            Assert.That(index, Is.EqualTo(1));
            Assert.That(fieldName, Is.EqualTo("First"));
        }

        [Test]
        public void ReadEncodingMaskComputesBitmaskCorrectlyForAllFields()
        {
            const string json = /*lang=json,strict*/ "{\"A\": 1, \"B\": 2, \"C\": 3}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            var masks = new List<string> { "A", "B", "C" };
            uint result = decoder.ReadEncodingMask(masks);

            Assert.That(result, Is.EqualTo(7));
        }

        [Test]
        public void ConstructorWithEmptyJsonCreatesDecoder()
        {
            using var decoder = new PubSubJsonDecoder("{}", m_context);
            Assert.That(decoder.Context, Is.SameAs(m_context));
        }

        [Test]
        public void ReadNestedStructure()
        {
            const string json = /*lang=json,strict*/ "{\"Level1\": {\"Level2\": {\"Value\": 99}}}";
            using var decoder = new PubSubJsonDecoder(json, m_context);

            decoder.PushStructure("Level1");
            decoder.PushStructure("Level2");
            int value = decoder.ReadInt32("Value");
            decoder.Pop();
            decoder.Pop();

            Assert.That(value, Is.EqualTo(99));
        }
    }
}
