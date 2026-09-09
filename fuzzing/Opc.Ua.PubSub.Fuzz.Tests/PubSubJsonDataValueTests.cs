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

using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Json;

namespace Opc.Ua.Fuzzing
{
    [TestFixture]
    [Category("Fuzzing")]
    public sealed class PubSubJsonDataValueTests
    {
        [Test]
        public void InlineVariantTypeDoesNotDiscardDataValueStatus()
        {
            const string payload = """
                {"Count":{"UaType":6,"Value":42,"Status":{"Code":1073741824}}}
                """;

            DataSetField field = DecodeField(payload);

            Assert.That(field.Encoding, Is.EqualTo(PubSubFieldEncoding.DataValue));
            Assert.That(field.Value.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
            Assert.That(field.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
        }

        [Test]
        public void InlineMatrixDimensionsDoNotDiscardDataValueStatus()
        {
            const string payload = """
                {"Count":{"UaType":6,"Value":[1,2,3,4],"Dimensions":[2,2],"Status":{"Code":1073741824}}}
                """;

            DataSetField field = DecodeField(payload);

            Assert.That(field.Encoding, Is.EqualTo(PubSubFieldEncoding.DataValue));
            Assert.That(field.Value.TryGetValue(out MatrixOf<int> value), Is.True);
            MatrixOf<int> expected = new int[,] { { 1, 2 }, { 3, 4 } };
            Assert.That(value, Is.EqualTo(expected));
            Assert.That(field.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
        }

        [TestCase("""{"Count":{"UaType":6,"Value":42}}""")]
        [TestCase("""{"Count":{"UaType":6,"Value":42,"Dimensions":[]}}""")]
        [TestCase("""{"Count":{"UaType":6,"Value":42,"Status":{"Code":1073741824},"Extra":true}}""")]
        public void InlineVariantsWithoutAnUnambiguousDataValueEnvelopeRemainVariants(string payload)
        {
            DataSetField field = DecodeField(payload);

            Assert.That(field.Encoding, Is.EqualTo(PubSubFieldEncoding.Variant));
            Assert.That(field.Value.TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(42));
            Assert.That(field.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(field.SourceTimestamp, Is.EqualTo(default(DateTimeUtc)));
            Assert.That(field.ServerTimestamp, Is.EqualTo(default(DateTimeUtc)));
        }

        private static DataSetField DecodeField(string payload)
        {
            using var document = JsonDocument.Parse(payload);
            ArrayOf<DataSetField> fields = JsonFieldDecoder.DecodeFields(
                document.RootElement,
                null,
                JsonEncodingMode.Verbose,
                FuzzableCode.NewContext().MessageContext);
            Assert.That(fields.Count, Is.EqualTo(1));
            Assert.That(fields[0].Name, Is.EqualTo("Count"));
            return fields[0];
        }
    }
}
