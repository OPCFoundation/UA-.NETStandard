/* ========================================================================
 * Copyright (c) 2005-2018 The OPC Foundation, Inc. All rights reserved.
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

#if ECC_SUPPORT && !NETFRAMEWORK
#define SPAN_SUPPORT
#endif

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Newtonsoft.Json;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;


namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// A group of encoder types.
    /// </summary>
    public class EncodingTypeGroup : IFormattable
    {
        public EncodingTypeGroup(EncodingType encoderType, JsonEncodingType jsonEncodingType = JsonEncodingType.Reversible)
        {
            this.EncoderType = encoderType;
            this.JsonEncodingType = jsonEncodingType;
        }

        public EncodingType EncoderType { get; }
        public JsonEncodingType JsonEncodingType { get; }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (EncoderType == EncodingType.Json)
            {
                return Utils.Format("{0}:{1}", EncoderType, JsonEncodingType);
            }
            return Utils.Format("{0}", EncoderType);
        }
    }

    /// <summary>
    /// Tests for the IEncoder and IDecoder class.
    /// </summary>
    [TestFixture, Category("Encoder")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class EncoderTests : EncoderCommon
    {
        #region DataPointSources
        [DatapointSource]
        public int[] ArrayLength = new int[] { 1, 5, 100 };
        #endregion

        #region Test Methods
        /// <summary>
        /// Verify encode and decode of a default built in type
        /// value as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeDefaultVariantInDataValue(
            [ValueSource(nameof(EncodingTypesReversibleCompact))]
            EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            object defaultValue = TypeInfo.GetDefaultValue(builtInType);
            EncodeDecodeDataValue(encoderType, jsonEncodingType, builtInType, MemoryStreamType.MemoryStream, defaultValue);
        }

        /// <summary>
        /// Verify encode and decode of a random built in type
        /// value as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeAsVariantInDataValue(
            [ValueSource(nameof(EncodingTypesReversibleCompact))]
            EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            Assume.That(builtInType != BuiltInType.DiagnosticInfo);
            object randomData = DataGenerator.GetRandom(builtInType);
            EncodeDecodeDataValue(encoderType, jsonEncodingType, builtInType, MemoryStreamType.ArraySegmentStream, randomData);
        }

        /// <summary>
        /// Verify encode and decode of a random built in type.
        /// </summary>
        [Theory]
        [Category("BuiltInType"), Repeat(kRandomRepeats)]
        public void ReEncodeBuiltInType(
            [ValueSource(nameof(EncodingTypesReversibleCompact))]
            EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            SetRepeatedRandomSeed();
            object randomData = null;
            bool getRandom = true;
            while (getRandom)
            {
                getRandom = false;
                randomData = DataGenerator.GetRandom(builtInType);
                // filter a few random special cases to skip
                // as they test for unsupported objects
                switch (builtInType)
                {
                    case BuiltInType.NodeId:
                        var nodeId = (NodeId)randomData;
                        if (nodeId.IdType == IdType.Opaque &&
                            ((byte[])nodeId.Identifier).Length == 0)
                        {
                            getRandom = true;
                        }
                        break;
                    case BuiltInType.ExpandedNodeId:
                        var expandedNodeId = (ExpandedNodeId)randomData;
                        if (expandedNodeId.IdType == IdType.Opaque &&
                            ((byte[])expandedNodeId.Identifier).Length == 0)
                        {
                            getRandom = true;
                        }
                        break;
                    default:
                        break;
                }
            };
            EncodeDecode(encoderType, jsonEncodingType, builtInType, MemoryStreamType.ArraySegmentStream, randomData);
        }

        /// <summary>
        /// Verify encode and decode of a default built in type value.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeDefaultValue(
            [ValueSource(nameof(EncodingTypesReversibleCompact))]
            EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            object randomData = TypeInfo.GetDefaultValue(builtInType);
            if (builtInType == BuiltInType.ExtensionObject)
            {
                // special case for extension object, default from TypeInfo must be null
                // or encoding of extension objects fails.
                randomData = ExtensionObject.Null;
            }
            EncodeDecode(encoderType, jsonEncodingType, builtInType, MemoryStreamType.RecyclableMemoryStream, randomData);
        }

        /// <summary>
        /// Verify encode and decode of boundary built in type values.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeBoundaryValue(
            [ValueSource(nameof(EncodingTypesReversibleCompact))]
            EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            Array boundaryValues = DataGenerator.GetRandomArray(builtInType, true, 10, true);
            foreach (var boundaryValue in boundaryValues)
            {
                EncodeDecode(encoderType, jsonEncodingType, builtInType, MemoryStreamType.MemoryStream, boundaryValue);
            }
        }

        /// <summary>
        /// Verify encode and decode of an array of a 
        /// random builtin type as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeArrayAsRandomVariantInDataValue(
            [ValueSource(nameof(EncodingTypesReversibleCompact))]
            EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType,
            bool useBoundaryValues,
            int arrayLength
            )
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            // ensure different sized arrays contain different data set
            SetRandomSeed(arrayLength);
            object randomData = DataGenerator.GetRandomArray(builtInType, useBoundaryValues, arrayLength, true);
            EncodeDecodeDataValue(encoderType, jsonEncodingType, builtInType, MemoryStreamType.ArraySegmentStream, randomData);
        }

        /// <summary>
        /// Verify encode and decode of a zero length array
        /// of a builtin type as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeZeroLengthArrayAsVariantInDataValue(
            [ValueSource(nameof(EncodingTypesReversibleCompact))]
            EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType
            )
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            object randomData = DataGenerator.GetRandomArray(builtInType, false, 0, true);
            EncodeDecodeDataValue(encoderType, jsonEncodingType, builtInType, MemoryStreamType.RecyclableMemoryStream, randomData);
        }

        /// <summary>
        /// Verify encode and decode of a random built in type
        /// as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType"), Repeat(kRandomRepeats)]
        public void ReEncodeBuiltInTypeRandomVariantInDataValue(
            [ValueSource(nameof(EncodingTypesReversibleCompact))]
            EncodingTypeGroup encoderTypeGroup
            )
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            SetRepeatedRandomSeed();
            object randomData = DataGenerator.GetRandom(BuiltInType.Variant);
            EncodeDecodeDataValue(encoderType, jsonEncodingType, BuiltInType.Variant, MemoryStreamType.MemoryStream, randomData);
        }

        /// <summary>
        /// Validate integrity of non reversible Json encoding
        /// of a builtin type as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void EncodeBuiltInTypeAsVariantInDataValueToNonReversibleJson(
            BuiltInType builtInType
            )
        {
            object randomData = DataGenerator.GetRandom(builtInType);
            if (builtInType == BuiltInType.DiagnosticInfo)
            {
                Assert.Throws(
                    typeof(ServiceResultException),
                    () => EncodeDataValue(EncodingType.Json, builtInType, MemoryStreamType.ArraySegmentStream, randomData, JsonEncodingType.NonReversible)
                );
                return;
            }
            string json = EncodeDataValue(EncodingType.Json, builtInType, MemoryStreamType.MemoryStream, randomData, JsonEncodingType.NonReversible);
            PrettifyAndValidateJson(json);
        }

        /// <summary>
        /// Validate integrity of non reversible Json encoding
        /// of a builtin type as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void EncodeBuiltInTypeAsVariantInDataValueToVerboseJson(
            BuiltInType builtInType
            )
        {
            object randomData = DataGenerator.GetRandom(builtInType);
            if (builtInType == BuiltInType.DiagnosticInfo)
            {
                Assert.Throws(
                    typeof(ServiceResultException),
                    () => EncodeDataValue(EncodingType.Json, builtInType, MemoryStreamType.ArraySegmentStream, randomData, JsonEncodingType.Verbose)
                );
                return;
            }
            string json = EncodeDataValue(EncodingType.Json, builtInType, MemoryStreamType.MemoryStream, randomData, JsonEncodingType.Verbose);
            PrettifyAndValidateJson(json);
        }

        /// <summary>
        /// Validate integrity of non reversible Json encoding
        /// of a builtin type array as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void EncodeBuiltInTypeArrayAsVariantInDataValueToNonReversibleJson(
            BuiltInType builtInType,
            bool useBoundaryValues,
            int arrayLength
            )
        {
            SetRandomSeed(arrayLength);
            object randomData = DataGenerator.GetRandomArray(builtInType, useBoundaryValues, arrayLength, true);
            string json = EncodeDataValue(EncodingType.Json, builtInType, MemoryStreamType.RecyclableMemoryStream, randomData, JsonEncodingType.NonReversible);
            PrettifyAndValidateJson(json);
        }

        /// <summary>
        /// Verify non reversible Json encoding
        /// of a builtin type array as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void EncodeBuiltInTypeZeroLengthArrayAsVariantInDataValueToNonReversibleJson(
            BuiltInType builtInType
            )
        {
            object randomData = DataGenerator.GetRandomArray(builtInType, false, 0, true);
            string json = EncodeDataValue(EncodingType.Json, builtInType, MemoryStreamType.MemoryStream, randomData, JsonEncodingType.NonReversible);
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
            int arrayLength
            )
        {
            SetRandomSeed(arrayLength);
            object randomData = DataGenerator.GetRandomArray(builtInType, useBoundaryValues, arrayLength, true);
            string json = EncodeDataValue(EncodingType.Json, builtInType, MemoryStreamType.RecyclableMemoryStream, randomData, JsonEncodingType.Verbose);
            PrettifyAndValidateJson(json);
        }

        /// <summary>
        /// Verify non reversible Json encoding
        /// of a builtin type array as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void EncodeBuiltInTypeZeroLengthArrayAsVariantInDataValueToVerboseJson(
            BuiltInType builtInType
            )
        {
            object randomData = DataGenerator.GetRandomArray(builtInType, false, 0, true);
            string json = EncodeDataValue(EncodingType.Json, builtInType, MemoryStreamType.MemoryStream, randomData, JsonEncodingType.Verbose);
            PrettifyAndValidateJson(json);
        }

        /// <summary>
        /// Verify encode and decode of a VariantCollection.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeVariantCollectionInDataValue(
            [ValueSource(nameof(EncodingTypesReversibleCompact))]
            EncodingTypeGroup encoderTypeGroup)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            var variant = new VariantCollection {
                new Variant(4L),
                new Variant("test"),
                new Variant(new Int32[] {1, 2, 3, 4, 5 }),
                new Variant(new Int64[] {1, 2, 3, 4, 5 }),
                new Variant(new string[] {"1", "2", "3", "4", "5" }),
                //TODO: works as expected, but the expected need to be tweaked for the Int32 result
                //new Variant(new TestEnumType[] { TestEnumType.One, TestEnumType.Two, TestEnumType.Hundred }),
                new Variant(new Int32[] { 2, 3, 10 }, new TypeInfo(BuiltInType.Enumeration, 1))
            };
            EncodeDecodeDataValue(encoderType, jsonEncodingType, BuiltInType.Variant, MemoryStreamType.ArraySegmentStream, variant);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2265:Do not compare Span<T> to 'null' or 'default'", Justification = "Null compare works with ReadOnlySpan<byte>")]
        private string WriteByteStringData(IEncoder encoder)
        {
            encoder.WriteByteString("ByteString1", new byte[] { 0, 1, 2, 3, 4, 5 }, 1, 3);
            encoder.WriteByteString("ByteString2", null);
            encoder.WriteByteString("ByteString3", null, 1, 2);
#if SPAN_SUPPORT
            var span = new ReadOnlySpan<byte>(new byte[] { 0, 1, 2, 3, 4, 5 }, 1, 3);
            encoder.WriteByteString("ByteString4", span);

            var nullspan = new ReadOnlySpan<byte>(null);
            encoder.WriteByteString("ByteString5", nullspan);
            Assert.IsTrue(nullspan.IsEmpty);
            Assert.IsTrue(nullspan == null);

            ReadOnlySpan<byte> defaultspan = default;
            encoder.WriteByteString("ByteString6", defaultspan);
            Assert.IsTrue(defaultspan.IsEmpty);
            Assert.IsTrue(defaultspan == null);

            ReadOnlySpan<byte> emptyspan = Array.Empty<byte>();
            encoder.WriteByteString("ByteString7", emptyspan);
            Assert.IsTrue(emptyspan.IsEmpty);
            Assert.IsTrue(emptyspan != null);
#endif
            return encoder.CloseAndReturnText();
        }

        private void ReadByteStringData(IDecoder decoder)
        {
            var result = decoder.ReadByteString("ByteString1");
            Assert.AreEqual(new byte[] { 1, 2, 3 }, result);
            result = decoder.ReadByteString("ByteString2");
            Assert.AreEqual(null, result);
            result = decoder.ReadByteString("ByteString3");
            Assert.AreEqual(null, result);
#if SPAN_SUPPORT
            result = decoder.ReadByteString("ByteString4");
            Assert.AreEqual(new byte[] { 1, 2, 3 }, result);
            result = decoder.ReadByteString("ByteString5");
            Assert.AreEqual(null, result);
            result = decoder.ReadByteString("ByteString6");
            Assert.AreEqual(null, result);
            result = decoder.ReadByteString("ByteString7");
            Assert.AreEqual(Array.Empty<byte>(), result);
#endif
        }

        [Test]
        [Category("WriteByteString")]
        public void BinaryEncoder_WriteByteString()
        {
            using (var stream = new MemoryStream())
            {
                string text;
                using (IEncoder encoder = new BinaryEncoder(stream, new ServiceMessageContext(), true))
                {
                    text = WriteByteStringData(encoder);
                }
                stream.Position = 0;
                using (var decoder = new BinaryDecoder(stream, new ServiceMessageContext()))
                {
                    ReadByteStringData(decoder);
                }
            }
        }

        [Test]
        [Category("WriteByteString")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2265:Do not compare Span<T> to 'null' or 'default'", Justification = "Null compare works with ReadOnlySpan<byte>")]
        public void XmlEncoder_WriteByteString()
        {
            using (var stream = new MemoryStream())
            {
                XmlWriterSettings settings = Utils.DefaultXmlWriterSettings();
                using (XmlWriter writer = XmlWriter.Create(stream, settings))
                using (IEncoder encoder = new XmlEncoder(new XmlQualifiedName("ByteStrings", Namespaces.OpcUaXsd), writer, new ServiceMessageContext()))
                {
                    string text = WriteByteStringData(encoder);
                }
                stream.Position = 0;
                using (XmlReader reader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings()))
                using (var decoder = new XmlDecoder(null, reader, new ServiceMessageContext()))
                {
                    ReadByteStringData(decoder);
                }
            }
        }

        [Test]
        [Category("WriteByteString")]
        public void JsonEncoder_WriteByteString()
        {
            using (var stream = new MemoryStream())
            {
                string text;
                using (IEncoder encoder = new JsonEncoder(new ServiceMessageContext(), true, false, stream, true))
                {
                    text = WriteByteStringData(encoder);

                }

                stream.Position = 0;
                var jsonTextReader = new JsonTextReader(new StreamReader(stream));
                using (var decoder = new JsonDecoder(null, jsonTextReader, new ServiceMessageContext()))
                {
                    ReadByteStringData(decoder);
                }
            }
        }

        /// <summary>
        /// Verify encode and decode of a Matrix in a Variant.
        /// </summary>
        [Theory]
        [Category("Array"), Repeat(kArrayRepeats)]
        public void ReEncodeVariantArrayInDataValue(
            [ValueSource(nameof(EncodingTypesAllButJsonNonReversible))]
            EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType
            )
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            SetRepeatedRandomSeed();
            Assume.That(builtInType != BuiltInType.Null);
            int arrayDimension = RandomSource.NextInt32(99) + 1;
            Array randomData = DataGenerator.GetRandomArray(builtInType, false, arrayDimension, true);
            var variant = new Variant(randomData, new TypeInfo(builtInType, 1));
            EncodeDecodeDataValue(encoderType, jsonEncodingType, BuiltInType.Variant, MemoryStreamType.RecyclableMemoryStream, variant);
        }

        /// <summary>
        /// Verify encode and decode of a one dimensional Array.
        /// </summary>
        [Theory]
        [Category("Array"), Repeat(kArrayRepeats)]
        public void EncodeArray(
            [ValueSource(nameof(EncodingTypesAll))]
            EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            SetRepeatedRandomSeed();
            Assume.That(builtInType != BuiltInType.Null);
            int arrayDimension = RandomSource.NextInt32(99) + 1;
            Array randomData = DataGenerator.GetRandomArray(builtInType, false, arrayDimension, true);

            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType}";
            Type type = TypeInfo.GetSystemType(builtInType, -1);
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine("Expected:");
            TestContext.Out.WriteLine(randomData);

            byte[] buffer;
            using (var encoderStream = CreateEncoderMemoryStream(MemoryStreamType.MemoryStream))
            {
                using (IEncoder encoder = CreateEncoder(encoderType, Context, encoderStream, type, jsonEncodingType, false))
                {
                    encoder.WriteArray(builtInType.ToString(), randomData, ValueRanks.OneDimension, builtInType);
                }
                buffer = encoderStream.ToArray();
            }

            switch (encoderType)
            {
                case EncodingType.Json:
                    PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));
                    break;
            }

            object result;
            using (var decoderStream = new MemoryStream(buffer))
            using (IDecoder decoder = CreateDecoder(encoderType, Context, decoderStream, type))
            {
                result = decoder.ReadArray(builtInType.ToString(), ValueRanks.OneDimension, builtInType);
            }

            TestContext.Out.WriteLine("Result:");
            TestContext.Out.WriteLine(result);
            object expected = AdjustExpectedBoundaryValues(encoderType, builtInType, randomData);

            // strip the locale information from localized text for non reversible
            if (builtInType == BuiltInType.LocalizedText && jsonEncodingType == JsonEncodingType.NonReversible)
            {
                var localizedTextCollection = new LocalizedTextCollection(randomData.Length);
                foreach (var entry in randomData)
                {
                    if (entry is LocalizedText localizedText)
                    {
                        localizedTextCollection.Add(new LocalizedText(null, localizedText.Text));
                    }
                }
                expected = localizedTextCollection.ToArray();
            }

            Assert.AreEqual(expected, result, encodeInfo);
            Assert.IsTrue(Utils.IsEqual(expected, result), "Opc.Ua.Utils.IsEqual failed to compare expected and result. " + encodeInfo);
        }

        /// <summary>
        /// Verify encode and decode of a Matrix in a Variant.
        /// </summary>
        [Theory]
        [Category("Matrix"), Repeat(kArrayRepeats)]
        public void ReEncodeVariantMatrixInDataValue(
            [ValueSource(nameof(EncodingTypesReversibleCompact))]
            EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            SetRepeatedRandomSeed();
            Assume.That(builtInType != BuiltInType.Null);
            // reduce array dimension for arrays with large values
            int maxRand = 6;
            if (builtInType == BuiltInType.XmlElement || builtInType == BuiltInType.ExtensionObject)
            {
                maxRand = 2;
            }

            int matrixDimension = RandomSource.NextInt32(maxRand) + 2;
            int[] dimensions = new int[matrixDimension];
            SetMatrixDimensions(dimensions);
            int elements = ElementsFromDimension(dimensions);
            Array randomData = DataGenerator.GetRandomArray(builtInType, false, elements, true);
            var variant = new Variant(new Matrix(randomData, builtInType, dimensions));
            EncodeDecodeDataValue(encoderType, jsonEncodingType, BuiltInType.Variant, MemoryStreamType.RecyclableMemoryStream, variant);
        }

        /// <summary>
        /// Verify encode of a Matrix in a Variant to non reversible JSON.
        /// </summary>
        [Theory]
        [Category("Matrix"), Repeat(kArrayRepeats)]
        public void EncodeBuiltInTypeMatrixAsVariantInDataValueToNonReversibleVerboseJson(
            [ValueSource(nameof(EncodingTypesJsonNonReversibleVerbose))]
            EncodingTypeGroup encoderTypeGroup,
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
            Array randomData = DataGenerator.GetRandomArray(builtInType, false, elements, true);
            var variant = new Variant(new Matrix(randomData, builtInType, dimensions));
            string json = EncodeDataValue(encoderType, BuiltInType.Variant, MemoryStreamType.ArraySegmentStream, variant, jsonEncodingType);
            _ = PrettifyAndValidateJson(json);
        }

        /// <summary>
        /// Verify encode of a Matrix in a multi dimensional array.
        /// </summary>
        [Theory]
        [Category("Matrix"), Repeat(kArrayRepeats)]
        public void EncodeMatrixInArray(
            [ValueSource(nameof(EncodingTypesAll))]
            EncodingTypeGroup encoderTypeGroup,
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
            Array randomData = DataGenerator.GetRandomArray(builtInType, false, elements, true);
            var matrix = new Matrix(randomData, builtInType, dimensions);

            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType}";
            Type type = TypeInfo.GetSystemType(builtInType, -1);
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine("Expected:");
            TestContext.Out.WriteLine(matrix);

            byte[] buffer;
            using (var encoderStream = CreateEncoderMemoryStream(MemoryStreamType.MemoryStream))
            {
                using (IEncoder encoder = CreateEncoder(encoderType, Context, encoderStream, type, jsonEncodingType))
                {
                    encoder.WriteArray(builtInType.ToString(), matrix, matrix.TypeInfo.ValueRank, builtInType);
                }
                buffer = encoderStream.ToArray();
            }

            switch (encoderType)
            {
                case EncodingType.Json:
                    PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));
                    break;
            }

            object result;
            using (var decoderStream = new MemoryStream(buffer))
            using (IDecoder decoder = CreateDecoder(encoderType, Context, decoderStream, type))
            {
                result = decoder.ReadArray(builtInType.ToString(), matrix.TypeInfo.ValueRank, builtInType);
            }

            TestContext.Out.WriteLine("Result:");
            TestContext.Out.WriteLine(result);
            object expected = AdjustExpectedBoundaryValues(encoderType, builtInType, matrix);

            // strip the locale information from localized text for non reversible
            if (builtInType == BuiltInType.LocalizedText && jsonEncodingType == JsonEncodingType.NonReversible)
            {
                var localizedTextCollection = new LocalizedTextCollection(randomData.Length);
                foreach (var entry in matrix.Elements)
                {
                    if (entry is LocalizedText localizedText)
                    {
                        localizedTextCollection.Add(new LocalizedText(null, localizedText.Text));
                    }
                }

                // only compare the text portion
                if (result is Array resultArray)
                {
                    expected = localizedTextCollection.ToArray();
                    result = resultArray.OfType<LocalizedText>().ToArray();
                }
            }

            Assert.AreEqual(expected, result, encodeInfo);
            Assert.IsTrue(Utils.IsEqual(expected, result), "Opc.Ua.Utils.IsEqual failed to compare expected and result. " + encodeInfo);
        }

        /// <summary>
        /// Verify that decoding of a Matrix DataValue which has invalid array dimensions.
        /// </summary
        [Theory]
        [Category("Matrix")]
        public void MatrixOverflow(
            [ValueSource(nameof(EncodingTypesAll))]
            EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            Assume.That(builtInType != BuiltInType.Null);
            int matrixDimension = RandomSource.NextInt32(8) + 2;
            int[] dimensions = new int[matrixDimension];
            SetMatrixDimensions(dimensions);
            int elements = ElementsFromDimension(dimensions);
            Array randomData = DataGenerator.GetRandomArray(builtInType, false, elements, true);

            // create an invalid matrix to validate that the dimension overflow is catched
            var matrix = new Matrix(randomData, builtInType, dimensions);
            for (int ii = 0; ii < matrixDimension; ii++)
            {
                if (ii % 2 == 0)
                {
                    matrix.Dimensions[ii] = 0x40000001;
                }
                else
                {
                    matrix.Dimensions[ii] = 4;
                }
            }

            var variant = new Variant(matrix);

            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType}";
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine(variant);
            DataValue expected = CreateDataValue(builtInType, variant);
            Assert.IsNotNull(expected, "Expected DataValue is Null, " + encodeInfo);
            TestContext.Out.WriteLine("Expected:");
            TestContext.Out.WriteLine(expected);

            byte[] buffer;
            using (var encoderStream = CreateEncoderMemoryStream(MemoryStreamType.MemoryStream))
            {
                using (IEncoder encoder = CreateEncoder(encoderType, Context, encoderStream, typeof(DataValue), jsonEncodingType))
                {
                    if (encoderType == EncodingType.Json && jsonEncodingType == JsonEncodingType.NonReversible)
                    {
                        var sre = Assert.Throws<ServiceResultException>(() => encoder.WriteDataValue("DataValue", expected));
                        Assert.AreEqual(StatusCodes.BadEncodingLimitsExceeded, sre.StatusCode);
                        return;
                    }
                    else
                    {
                        encoder.WriteDataValue("DataValue", expected);
                    }
                }
                buffer = encoderStream.ToArray();
            }

            string jsonFormatted;
            switch (encoderType)
            {
                case EncodingType.Json:
                    jsonFormatted = PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));
                    break;
            }

            using (var decoderStream = new MemoryStream(buffer))
            using (IDecoder decoder = CreateDecoder(encoderType, Context, decoderStream, typeof(DataValue)))
            {
                // check such matrix cannot be initialized when decoding from Binary format
                // the exception is thrown before trying to construct the Matrix
                ServiceResultException sre = Assert.Throws<ServiceResultException>(
                    () => {
                        decoder.ReadDataValue("DataValue");
                    });
                Assert.AreEqual((StatusCode)StatusCodes.BadDecodingError, (StatusCode)sre.StatusCode, sre.Message);
            }
        }

        /// <summary>
        /// Verify that decoding of a Matrix DataValue which has statical provided invalid array dimensions.
        /// </summary
        [Theory]
        [Category("Matrix")]
        public void MatrixOverflowStaticDimensions(
            [ValueSource(nameof(EncodingTypesAll))]
            EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            Assume.That(builtInType != BuiltInType.Null);
            int matrixDimension = 5;
            int[] dimensions = new int[matrixDimension];
            SetMatrixDimensions(dimensions);
            int elements = ElementsFromDimension(dimensions);
            Array randomData = DataGenerator.GetRandomArray(builtInType, false, elements, true);

            var matrix = new Matrix(randomData, builtInType, dimensions);
            matrix.Dimensions[0] = 12301;
            matrix.Dimensions[1] = 13193;
            matrix.Dimensions[2] = 13418;
            matrix.Dimensions[3] = 14087;
            matrix.Dimensions[4] = 20446;

            var variant = new Variant(matrix);

            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType}";
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine(variant);
            DataValue expected = CreateDataValue(builtInType, variant);
            Assert.IsNotNull(expected, "Expected DataValue is Null, " + encodeInfo);
            TestContext.Out.WriteLine("Expected:");
            TestContext.Out.WriteLine(expected);

            byte[] buffer;
            using (var encoderStream = CreateEncoderMemoryStream(MemoryStreamType.ArraySegmentStream))
            {
                using (IEncoder encoder = CreateEncoder(encoderType, Context, encoderStream, typeof(DataValue), jsonEncodingType))
                {
                    if (encoderType == EncodingType.Json && jsonEncodingType == JsonEncodingType.NonReversible)
                    {
                        var sre = Assert.Throws<ServiceResultException>(() => encoder.WriteDataValue("DataValue", expected));
                        Assert.AreEqual(StatusCodes.BadEncodingLimitsExceeded, sre.StatusCode);
                        return;
                    }
                    else
                    {
                        encoder.WriteDataValue("DataValue", expected);
                    }
                }
                buffer = encoderStream.ToArray();
            }

            string jsonFormatted;
            switch (encoderType)
            {
                case EncodingType.Json:
                    jsonFormatted = PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));
                    break;
            }

            using (var decoderStream = new MemoryStream(buffer))
            using (IDecoder decoder = CreateDecoder(encoderType, Context, decoderStream, typeof(DataValue)))
            {
                // check such matrix cannot be initialized when decoding from Json format
                // the exception is thrown while trying to construct the Matrix 
                var sre = Assert.Throws<ServiceResultException>(
                    () => {
                        decoder.ReadDataValue("DataValue");
                    });

                Assert.AreEqual((StatusCode)StatusCodes.BadDecodingError, (StatusCode)sre.StatusCode, sre.Message);
            }
        }

        /// <summary>
        /// Verify encode of a Matrix in a multi dimensional array.
        /// </summary>
        [Theory]
        [Category("Matrix")]
        public void EncodeMatrixInArrayOverflow(
            [ValueSource(nameof(EncodingTypesAll))]
            EncodingTypeGroup encoderTypeGroup,
            BuiltInType builtInType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            Assume.That(builtInType != BuiltInType.Null);
            int matrixDimension = RandomSource.NextInt32(8) + 2;
            int[] dimensions = new int[matrixDimension];
            SetMatrixDimensions(dimensions);
            int elements = ElementsFromDimension(dimensions);
            Array randomData = DataGenerator.GetRandomArray(builtInType, false, elements, true);
            var matrix = new Matrix(randomData, builtInType, dimensions);

            for (int ii = 0; ii < matrixDimension; ii++)
            {
                if (ii % 2 == 0)
                {
                    matrix.Dimensions[ii] = 0x40000001;
                }
                else
                {
                    matrix.Dimensions[ii] = 4;
                }
            }

            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType}";
            Type type = TypeInfo.GetSystemType(builtInType, -1);
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine("Expected:");
            TestContext.Out.WriteLine(matrix);

            byte[] buffer;
            using (var encoderStream = CreateEncoderMemoryStream(MemoryStreamType.RecyclableMemoryStream))
            {
                using (IEncoder encoder = CreateEncoder(encoderType, Context, encoderStream, type, jsonEncodingType))
                {
                    switch (encoderType)
                    {
                        case EncodingType.Json:
                        {
                            // check such matrix cannot be initialized when encoded into Json format
                            // the exception is thrown while trying to WriteStructureMatrix into the arrray 
                            Assert.Throws(
                                typeof(ServiceResultException),
                                () => {
                                    encoder.WriteArray(builtInType.ToString(), matrix, matrix.TypeInfo.ValueRank, builtInType);
                                });
                            return;

                        }
                    }
                    encoder.WriteArray(builtInType.ToString(), matrix, matrix.TypeInfo.ValueRank, builtInType);
                }
                buffer = encoderStream.ToArray();
            }

            switch (encoderType)
            {
                case EncodingType.Json:
                    PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));
                    break;
            }

            using (var decoderStream = new MemoryStream(buffer))
            using (IDecoder decoder = CreateDecoder(encoderType, Context, decoderStream, type))
            {
                ServiceResultException sre = Assert.Throws<ServiceResultException>(
                    () => {
                        decoder.ReadArray(builtInType.ToString(), matrix.TypeInfo.ValueRank, builtInType);
                    });

                Assert.AreEqual((StatusCode)StatusCodes.BadEncodingLimitsExceeded, (StatusCode)sre.StatusCode, sre.Message);
            }
        }
        #endregion

        #region Private Methods
        #endregion

        #region Private Fields
        #endregion
    }
}
