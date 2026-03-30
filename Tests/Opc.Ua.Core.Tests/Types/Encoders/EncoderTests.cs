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

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
#define SPAN_SUPPORT
#endif

using System;
using System.IO;
using System.Text;
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// A group of encoder types.
    /// </summary>
    public class EncodingTypeGroup : IFormattable
    {
        public EncodingTypeGroup(
            EncodingType encoderType,
            JsonEncodingType jsonEncodingType = JsonEncodingType.Verbose,
            bool useXmlParser = false)
        {
            EncoderType = encoderType;
            JsonEncodingType = jsonEncodingType;
            UseXmlParser = useXmlParser;
        }

        public EncodingType EncoderType { get; }

        public JsonEncodingType JsonEncodingType { get; }

        public bool UseXmlParser { get; }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (EncoderType == EncodingType.Json)
            {
                return Utils.Format("{0}:{1}", EncoderType, JsonEncodingType);
            }
            if (EncoderType == EncodingType.Xml)
            {
                return Utils.Format("{0}:{1}", EncoderType, UseXmlParser ? "Parser" : "Reader");
            }
            return Utils.Format("{0}", EncoderType);
        }
    }

    /// <summary>
    /// Tests for the IEncoder and IDecoder class.
    /// </summary>
    [TestFixture]
    [Category("Encoder")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EncoderTests : EncoderCommon
    {
        [DatapointSource]
        public int[] ArrayLength = [1, 5, 100];

        private static readonly int[] s_value = [1, 2, 3, 4, 5];
        private static readonly string[] s_valueArray = ["1", "2", "3", "4", "5"];
        private static readonly int[] s_valueArray0 = [2, 3, 10];

        /// <summary>
        /// Verify encode and decode of a default built in type
        /// value as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        [Description("Ensures the Decoder reflects the Spec.: https://reference.opcfoundation.org/Core/Part6/v105/docs/5.2.2.17 -> " +
            "SourcePicoseconds: [...] Not present if the SourcePicoseconds bit in the EncodingMask is False. " +
            "If the source timestamp is missing the Picoseconds are ignored." +
            "ServerPicoseconds: [...] Not present if the ServerPicoseconds bit in the EncodingMask is False. " +
            "If the Server timestamp is missing the Picoseconds are ignored.")]
        public void EncodeDataValueWithoutValueProperty()
        {
            var dataValue = new DataValue { SourcePicoseconds = 1 };
            EncodeDataValueWithoutValuePropertyTest(dataValue);
            dataValue = new DataValue { SourceTimestamp = new DateTime(2001, 01, 01).ToUniversalTime() };
            EncodeDataValueWithoutValuePropertyTest(dataValue);
            dataValue = new DataValue { ServerTimestamp = new DateTime(2001, 01, 02).ToUniversalTime() };
            EncodeDataValueWithoutValuePropertyTest(dataValue);
            dataValue = new DataValue { ServerPicoseconds = 2 };
            EncodeDataValueWithoutValuePropertyTest(dataValue);
            dataValue = new DataValue { StatusCode = StatusCodes.BadNotImplemented };
            EncodeDataValueWithoutValuePropertyTest(dataValue);
            dataValue = new DataValue { SourceTimestamp = new DateTime(2001, 01, 03).ToUniversalTime(), SourcePicoseconds = 3 };
            EncodeDataValueWithoutValuePropertyTest(dataValue);
            dataValue = new DataValue { SourceTimestamp = new DateTime(2001, 01, 04).ToUniversalTime(), ServerPicoseconds = 4 };
            EncodeDataValueWithoutValuePropertyTest(dataValue);
            dataValue = new DataValue { ServerTimestamp = new DateTime(2001, 01, 05).ToUniversalTime(), ServerPicoseconds = 5 };
            EncodeDataValueWithoutValuePropertyTest(dataValue);
            dataValue = new DataValue { ServerTimestamp = new DateTime(2001, 01, 06).ToUniversalTime(), SourcePicoseconds = 6 };
            EncodeDataValueWithoutValuePropertyTest(dataValue);
            dataValue = new DataValue
            {
                ServerTimestamp = new DateTime(2001, 01, 07).ToUniversalTime(),
                ServerPicoseconds = 7,
                SourceTimestamp = new DateTime(2001, 01, 08).ToUniversalTime(),
                SourcePicoseconds = 8,
                StatusCode = StatusCodes.BadNotFound
            };
            EncodeDataValueWithoutValuePropertyTest(dataValue);
        }

        private void EncodeDataValueWithoutValuePropertyTest(DataValue dataValue)
        {
            const EncodingType encoderType = EncodingType.Binary;
            const BuiltInType builtInType = BuiltInType.Null;
            const MemoryStreamType memoryStreamType = MemoryStreamType.MemoryStream;
            const JsonEncodingType jsonEncodingType = JsonEncodingType.Verbose;
            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType}";
            TestContext.Out.WriteLine(encodeInfo);
            DataValue expected = dataValue;
            Assert.That(expected, Is.Not.Null, "Expected DataValue is Null, " + encodeInfo);

            DataValue result = null;
            byte[] buffer;
            using (MemoryStream encoderStream = CreateEncoderMemoryStream(memoryStreamType))
            {
                using (
                    IEncoder encoder = CreateEncoder(
                        encoderType,
                        Context,
                        encoderStream,
                        typeof(DataValue),
                        jsonEncodingType))
                {
                    encoder.WriteDataValue("DataValue", expected);
                }
                buffer = encoderStream.ToArray();
            }

            using (var decoderStream = new MemoryStream(buffer))
            using (IDecoder decoder = CreateDecoder(
                encoderType,
                false,
                Context,
                decoderStream,
                typeof(DataValue)))
            {
                result = decoder.ReadDataValue("DataValue");
            }

            Assert.That(result, Is.Not.Null, "Resulting DataValue is Null, " + encodeInfo);
            // see: https://reference.opcfoundation.org/Core/Part6/v105/docs/5.2.2.17
            if ((expected.SourcePicoseconds != 0 && expected.SourceTimestamp == DateTimeUtc.MinValue) ||
                (expected.ServerPicoseconds != 0 && expected.ServerTimestamp == DateTimeUtc.MinValue))
            {
                Assert.That(expected, Is.Not.EqualTo(result), encodeInfo);
                Assert.That(Utils.IsEqual(expected, result), Is.False, "Opc.Ua.Utils.IsEqual failed to compare expected and result. " + encodeInfo);
            }
            else
            {
                Assert.That(expected, Is.EqualTo(result), encodeInfo);
                Assert.That(
                    Utils.IsEqual(expected, result),
                    Is.True,
                    "Opc.Ua.Utils.IsEqual failed to compare expected and result. " + encodeInfo);
            }
        }

        /// <summary>
        /// Verify encode and decode of a default built in type
        /// value as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeDefaultVariantInDataValue(
            [ValueSource(
                nameof(EncodingTypesJsonBinaryXmlAndJsonCompact))] EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            Variant defaultValue = Variant.CreateDefault(TypeInfo.Create(builtInType, ValueRanks.Scalar));
            EncodeDecodeDataValue(
                encoderType,
                jsonEncodingType,
                useXmlParser,
                builtInType,
                MemoryStreamType.MemoryStream,
                defaultValue);
        }

        /// <summary>
        /// Verify encode and decode of a random built in type
        /// value as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeAsVariantInDataValue(
            [ValueSource(
                nameof(EncodingTypesJsonBinaryXmlAndJsonCompact))] EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            Assume.That(builtInType != BuiltInType.DiagnosticInfo);
            Variant randomData = DataGenerator.GetRandomVariant(builtInType, false);
            EncodeDecodeDataValue(
                encoderType,
                jsonEncodingType,
                useXmlParser,
                builtInType,
                MemoryStreamType.ArraySegmentStream,
                randomData);
        }

        /// <summary>
        /// Verify encode and decode of an array of a
        /// random builtin type as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeArrayAsRandomVariantInDataValue(
            [ValueSource(
                nameof(EncodingTypesJsonBinaryXmlAndJsonCompact))] EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType,
            bool useBoundaryValues,
            int arrayLength)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            // ensure different sized arrays contain different data set
            SetRandomSeed(arrayLength);
            Variant randomData = DataGenerator.GetRandomArray(
                builtInType,
                arrayLength,
                useBoundaryValues,
                true);
            EncodeDecodeDataValue(
                encoderType,
                jsonEncodingType,
                useXmlParser,
                builtInType,
                MemoryStreamType.ArraySegmentStream,
                randomData);
        }

        /// <summary>
        /// Verify encode and decode of a zero length array
        /// of a builtin type as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeZeroLengthArrayAsVariantInDataValue(
            [ValueSource(
                nameof(EncodingTypesJsonBinaryXmlAndJsonCompact))] EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            Variant randomData = DataGenerator.GetRandomArray(builtInType, 0, false, true);
            EncodeDecodeDataValue(
                encoderType,
                jsonEncodingType,
                useXmlParser,
                builtInType,
                MemoryStreamType.RecyclableMemoryStream,
                randomData);
        }

        /// <summary>
        /// Verify encode and decode of a random built in type
        /// as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        [Repeat(kRandomRepeats)]
        public void ReEncodeBuiltInTypeRandomVariantInDataValue(
            [ValueSource(
                nameof(EncodingTypesJsonBinaryXmlAndJsonCompact))] EncodingTypeGroup encoderTypeGroup)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            SetRepeatedRandomSeed();
            Variant randomData = DataGenerator.GetRandomVariant();
            EncodeDecodeDataValue(
                encoderType,
                jsonEncodingType,
                useXmlParser,
                BuiltInType.Variant,
                MemoryStreamType.MemoryStream,
                randomData);
        }

        /// <summary>
        /// Validate integrity of non reversible Json encoding
        /// of a builtin type as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void EncodeBuiltInTypeAsVariantInDataValueToCompactJson(
            BuiltInType builtInType)
        {
            Variant randomData = DataGenerator.GetRandomVariant(builtInType, false);
            string json = EncodeDataValue(
                EncodingType.Json,
                builtInType,
                MemoryStreamType.MemoryStream,
                randomData,
                JsonEncodingType.Compact);
            PrettifyAndValidateJson(json);
        }

        /// <summary>
        /// Validate integrity of non reversible Json encoding
        /// of a builtin type as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void EncodeBuiltInTypeAsVariantInDataValueToVerboseJson(BuiltInType builtInType)
        {
            Variant randomData = DataGenerator.GetRandomVariant(builtInType, false);
            string json = EncodeDataValue(
                EncodingType.Json,
                builtInType,
                MemoryStreamType.MemoryStream,
                randomData,
                JsonEncodingType.Verbose);
            PrettifyAndValidateJson(json);
        }

        /// <summary>
        /// Verify non reversible Json encoding
        /// of a builtin type array as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void EncodeBuiltInTypeZeroLengthArrayAsVariantInDataValueToCompactJson(
            BuiltInType builtInType)
        {
            Variant randomData = DataGenerator.GetRandomArray(builtInType, 0);
            string json = EncodeDataValue(
                EncodingType.Json,
                builtInType,
                MemoryStreamType.MemoryStream,
                randomData,
                JsonEncodingType.Compact);
            PrettifyAndValidateJson(json);
        }

        /// <summary>
        /// Validate integrity of non reversible Json encoding
        /// of a builtin type array as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void EncodeBuiltInTypeArrayAsVariantInDataValueToVerboseJson(
            BuiltInType builtInType,
            bool useBoundaryValues,
            int arrayLength)
        {
            SetRandomSeed(arrayLength);
            Variant randomData = DataGenerator.GetRandomArray(
                builtInType,
                arrayLength,
                useBoundaryValues);
            string json = EncodeDataValue(
                EncodingType.Json,
                builtInType,
                MemoryStreamType.RecyclableMemoryStream,
                randomData,
                JsonEncodingType.Verbose);
            PrettifyAndValidateJson(json);
        }

        /// <summary>
        /// Verify Verbose Json encoding
        /// of a builtin type array as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void EncodeBuiltInTypeZeroLengthArrayAsVariantInDataValueToVerboseJson(
            BuiltInType builtInType)
        {
            Variant randomData = DataGenerator.GetRandomArray(builtInType, 0);
            string json = EncodeDataValue(
                EncodingType.Json,
                builtInType,
                MemoryStreamType.MemoryStream,
                randomData,
                JsonEncodingType.Verbose);
            PrettifyAndValidateJson(json);
        }

        /// <summary>
        /// Verify encode and decode of a Variant array
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeVariantCollectionInDataValue(
            [ValueSource(
                nameof(EncodingTypesJsonBinaryXmlAndJsonCompact))] EncodingTypeGroup encoderTypeGroup)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            var variant = Variant.From(
            [
                Variant.From(4L),
                Variant.From("test"),
                Variant.From(s_value),
                Variant.From(new long[] { 1, 2, 3, 4, 5 }),
                Variant.From(s_valueArray),
                //TODO: works as expected, but the expected need to be tweaked for the Int32 result
                //Variant.From(new TestEnumType[] { TestEnumType.One, TestEnumType.Two, TestEnumType.Hundred }),
                Variant.FromEnumeration(s_valueArray0)
            ]);
            EncodeDecodeDataValue(
                encoderType,
                jsonEncodingType,
                useXmlParser,
                BuiltInType.Variant,
                MemoryStreamType.ArraySegmentStream,
                variant);
        }

        private static string WriteByteStringData(IEncoder encoder)
        {
            encoder.WriteByteString("ByteString1", ByteString.From([0, 1, 2, 3, 4, 5]).Slice(1, 3));
            encoder.WriteByteString("ByteString2", ByteString.Empty);
#if !SPAN_SUPPORT
            encoder.WriteByteString("ByteString3", default);
#else
            encoder.WriteByteString("ByteString3", default(ByteString));
            var span = new ReadOnlySpan<byte>([0, 1, 2, 3, 4, 5], 1, 3);
            encoder.WriteByteString("ByteString4", span);

            var nullspan = new ReadOnlySpan<byte>(null);
            encoder.WriteByteString("ByteString5", nullspan);
            Assert.That(nullspan.IsEmpty, Is.True);
            Assert.That(nullspan == ReadOnlySpan<byte>.Empty, Is.True);

            ReadOnlySpan<byte> defaultspan = default;
            encoder.WriteByteString("ByteString6", defaultspan);
            Assert.That(defaultspan.IsEmpty, Is.True);
#pragma warning disable CA1508 // Actually true
            Assert.That(defaultspan == ReadOnlySpan<byte>.Empty, Is.True);
#pragma warning restore CA1508

            var emptyspan = new ReadOnlySpan<byte>([]);
            encoder.WriteByteString("ByteString7", emptyspan);
            Assert.That(emptyspan.IsEmpty, Is.True);
            Assert.That(emptyspan != ReadOnlySpan<byte>.Empty, Is.True);
#endif
            return encoder.CloseAndReturnText();
        }

        private static void ReadByteStringData(IDecoder decoder)
        {
            ByteString result = decoder.ReadByteString("ByteString1");
            Assert.That(result, Is.EqualTo(ByteString.From(new byte[] { 1, 2, 3 })));
            result = decoder.ReadByteString("ByteString2");
            Assert.That(result, Is.EqualTo(ByteString.Empty));
            result = decoder.ReadByteString("ByteString3");
            Assert.That(result, Is.EqualTo(ByteString.Empty));
#if SPAN_SUPPORT
            result = decoder.ReadByteString("ByteString4");
            Assert.That(result, Is.EqualTo(new byte[] { 1, 2, 3 }));
            result = decoder.ReadByteString("ByteString5");
            Assert.That(result, Is.EqualTo(ByteString.Empty));
            result = decoder.ReadByteString("ByteString6");
            Assert.That(result, Is.EqualTo(ByteString.Empty));
            result = decoder.ReadByteString("ByteString7");
            Assert.That(result, Is.EqualTo(ByteString.Empty));
#endif
        }

        [Test]
        [Category("WriteByteString")]
        public void BinaryEncoder_WriteByteString()
        {
            using var stream = new MemoryStream();
            string text;
            using (var encoder = new BinaryEncoder(stream, new ServiceMessageContext(Telemetry), true))
            {
                text = WriteByteStringData(encoder);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, new ServiceMessageContext(Telemetry));
            ReadByteStringData(decoder);
        }

        [Test]
        [Category("WriteByteString")]
        public void XmlEncoder_WriteByteString()
        {
            using var stream = new MemoryStream();
            XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
            using (var writer = XmlWriter.Create(stream, settings))
            using (
                var encoder = new XmlEncoder(
                    new XmlQualifiedName("ByteStrings", Ua.Types.Namespaces.OpcUaXsd),
                    writer,
                    new ServiceMessageContext(Telemetry)))
            {
                string text = WriteByteStringData(encoder);
            }
            stream.Position = 0;
            using var reader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings());
            using var decoder = new XmlDecoder(null, reader, new ServiceMessageContext(Telemetry));
            ReadByteStringData(decoder);
        }

        [Test]
        [Category("WriteByteString")]
        public void JsonEncoder_WriteByteString()
        {
            using var stream = new MemoryStream();
            string text;
            using (var encoder = new JsonEncoder(
                stream,
                new ServiceMessageContext(Telemetry)))
            {
                text = WriteByteStringData(encoder);
            }

            stream.Position = 0;
            using var decoder = new JsonDecoder(stream, new ServiceMessageContext(Telemetry));
            ReadByteStringData(decoder);
        }

        /// <summary>
        /// Verify encode and decode of a Matrix in a Variant.
        /// </summary>
        [Theory]
        [Category("Array")]
        [Repeat(kArrayRepeats)]
        public void ReEncodeVariantArrayInDataValue(
            [ValueSource(
                nameof(EncodingTypesAll))] EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            SetRepeatedRandomSeed();
            Assume.That(builtInType != BuiltInType.Null);
            int arrayDimension = RandomSource.NextInt32(99) + 1;
            Variant variant = DataGenerator.GetRandomArray(builtInType, arrayDimension, false, true);

            EncodeDecodeDataValue(
                encoderType,
                jsonEncodingType,
                useXmlParser,
                BuiltInType.Variant,
                MemoryStreamType.RecyclableMemoryStream,
                variant);
        }

        /// <summary>
        /// Verify encode and decode of a one dimensional Array.
        /// </summary>
        [Theory]
        [Category("Array")]
        [Repeat(kArrayRepeats)]
        public void EncodeArray(
            [ValueSource(nameof(EncodingTypesAll))] EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            SetRepeatedRandomSeed();
            Assume.That(builtInType != BuiltInType.Null);
            int arrayDimension = RandomSource.NextInt32(99) + 1;
            Variant randomData = DataGenerator.GetRandomArray(
                builtInType,
                arrayDimension);

            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType}";
            Type type = TypeInfo.GetSystemType(builtInType, -1);
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine("Expected:");
            TestContext.Out.WriteLine(randomData);

            byte[] buffer;
            using (MemoryStream encoderStream = CreateEncoderMemoryStream(
                MemoryStreamType.MemoryStream))
            {
                using (
                    IEncoder encoder = CreateEncoder(
                        encoderType,
                        Context,
                        encoderStream,
                        type,
                        jsonEncodingType))
                {
                    encoder.WriteVariantValue(builtInType.ToString(), randomData);
                }
                buffer = encoderStream.ToArray();
            }

            switch (encoderType)
            {
                case EncodingType.Json:
                    PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));
                    break;
            }

            Variant result;
            using (var decoderStream = new MemoryStream(buffer))
            using (IDecoder decoder = CreateDecoder(encoderType, useXmlParser, Context, decoderStream, type))
            {
                result = decoder.ReadVariantValue(builtInType.ToString(), randomData.TypeInfo);
            }

            TestContext.Out.WriteLine("Result:");
            TestContext.Out.WriteLine(result);

            Assert.That(result, Is.EqualTo(randomData), encodeInfo);
        }

        /// <summary>
        /// Verify encode and decode of a Matrix in a Variant.
        /// </summary>
        [Theory]
        [Category("Matrix")]
        [Repeat(kArrayRepeats)]
        public void ReEncodeVariantMatrixInDataValue(
            [ValueSource(
                nameof(EncodingTypesJsonBinaryXmlAndJsonCompact))] EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            SetRepeatedRandomSeed();
            Assume.That(builtInType != BuiltInType.Null);
            // reduce array dimension for arrays with large values
            int maxRand = 6;
            if (builtInType is BuiltInType.XmlElement or BuiltInType.ExtensionObject)
            {
                maxRand = 2;
            }

            int matrixDimension = RandomSource.NextInt32(maxRand) + 2;
            int[] dimensions = new int[matrixDimension];
            SetMatrixDimensions(dimensions);
            int elements = ElementsFromDimension(dimensions);
            Variant variant = DataGenerator.GetRandomMatrix(builtInType, elements, dimensions, false, true);
            EncodeDecodeDataValue(
                encoderType,
                jsonEncodingType,
                useXmlParser,
                BuiltInType.Variant,
                MemoryStreamType.RecyclableMemoryStream,
                variant);
        }

        /// <summary>
        /// Verify encode of a Matrix in a Variant to non reversible JSON.
        /// </summary>
        [Theory]
        [Category("Matrix")]
        [Repeat(kArrayRepeats)]
        public void EncodeBuiltInTypeMatrixAsVariantInDataValueToVerboseJson(
            [ValueSource(
                nameof(EncodingTypesJsonVerbose))] EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            SetRepeatedRandomSeed();
            Assume.That(builtInType != BuiltInType.Null);
            int matrixDimension = RandomSource.NextInt32(3) + 2;
            int[] dimensions = new int[matrixDimension];
            SetMatrixDimensions(dimensions);
            int elements = ElementsFromDimension(dimensions);
            Variant variant = DataGenerator.GetRandomMatrix(builtInType, elements, dimensions, false);
            string json = EncodeDataValue(
                encoderType,
                BuiltInType.Variant,
                MemoryStreamType.ArraySegmentStream,
                variant,
                jsonEncodingType);
            _ = PrettifyAndValidateJson(json);
        }

        /// <summary>
        /// Verify encode of a Matrix in a multi dimensional array.
        /// </summary>
        [Theory]
        [Category("Matrix")]
        [Repeat(kArrayRepeats)]
        public void EncodeMatrixInArray(
            [ValueSource(nameof(EncodingTypesAll))] EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            bool useXmlParser = encoderTypeGroup.UseXmlParser;
            SetRepeatedRandomSeed();
            Assume.That(builtInType != BuiltInType.Null);
            int matrixDimension = RandomSource.NextInt32(3) + 2;
            int[] dimensions = new int[matrixDimension];
            SetMatrixDimensions(dimensions);
            int elements = ElementsFromDimension(dimensions);
            Variant randomData = DataGenerator.GetRandomMatrix(builtInType, elements, dimensions);

            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType}";
            Type type = TypeInfo.GetSystemType(builtInType, -1);
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine("Expected:");
            TestContext.Out.WriteLine(randomData);

            byte[] buffer;
            using (MemoryStream encoderStream = CreateEncoderMemoryStream(
                MemoryStreamType.MemoryStream))
            {
                using (IEncoder encoder = CreateEncoder(
                    encoderType,
                    Context,
                    encoderStream,
                    type,
                    jsonEncodingType))
                {
                    encoder.WriteVariantValue(builtInType.ToString(), randomData);
                }
                buffer = encoderStream.ToArray();
            }

            switch (encoderType)
            {
                case EncodingType.Json:
                    PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));
                    break;
            }

            Variant result;
            using (var decoderStream = new MemoryStream(buffer))
            using (IDecoder decoder = CreateDecoder(encoderType, useXmlParser, Context, decoderStream, type))
            {
                result = decoder.ReadVariantValue(builtInType.ToString(), randomData.TypeInfo);
            }

            TestContext.Out.WriteLine("Result:");
            TestContext.Out.WriteLine(result);
            Assert.That(result, Is.EqualTo(randomData), encodeInfo);
        }
    }
}
