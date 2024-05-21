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

using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;


namespace Opc.Ua.Core.Tests.Types.Encoders
{
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
            EncodingType encoderType,
            BuiltInType builtInType
            )
        {
            object defaultValue = TypeInfo.GetDefaultValue(builtInType);
            EncodeDecodeDataValue(encoderType, builtInType, MemoryStreamType.MemoryStream, defaultValue);
        }

        /// <summary>
        /// Verify encode and decode of a random built in type
        /// value as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeAsVariantInDataValue(
            EncodingType encoderType,
            BuiltInType builtInType
            )
        {
            Assume.That(builtInType != BuiltInType.DiagnosticInfo);
            object randomData = DataGenerator.GetRandom(builtInType);
            EncodeDecodeDataValue(encoderType, builtInType, MemoryStreamType.ArraySegmentStream, randomData);
        }

        /// <summary>
        /// Verify encode and decode of a random built in type.
        /// </summary>
        [Theory]
        [Category("BuiltInType"), Repeat(kRandomRepeats)]
        public void ReEncodeBuiltInType(
            EncodingType encoderType,
            BuiltInType builtInType
            )
        {
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
            EncodeDecode(encoderType, builtInType, MemoryStreamType.ArraySegmentStream, randomData);
        }

        /// <summary>
        /// Verify encode and decode of a default built in type value.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeDefaultValue(
            EncodingType encoderType,
            BuiltInType builtInType
            )
        {
            object randomData = TypeInfo.GetDefaultValue(builtInType);
            if (builtInType == BuiltInType.ExtensionObject)
            {
                // special case for extension object, default from TypeInfo must be null
                // or encoding of extension objects fails.
                randomData = ExtensionObject.Null;
            }
            EncodeDecode(encoderType, builtInType, MemoryStreamType.RecyclableMemoryStream, randomData);
        }

        /// <summary>
        /// Verify encode and decode of boundary built in type values.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeBoundaryValue(
            EncodingType encoderType,
            BuiltInType builtInType
            )
        {
            Array boundaryValues = DataGenerator.GetRandomArray(builtInType, true, 10, true);
            foreach (var boundaryValue in boundaryValues)
            {
                EncodeDecode(encoderType, builtInType, MemoryStreamType.MemoryStream, boundaryValue);
            }
        }

        /// <summary>
        /// Verify encode and decode of an array of a 
        /// random builtin type as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeArrayAsRandomVariantInDataValue(
            EncodingType encoderType,
            BuiltInType builtInType,
            bool useBoundaryValues,
            int arrayLength
            )
        {
            // ensure different sized arrays contain different data set
            SetRandomSeed(arrayLength);
            object randomData = DataGenerator.GetRandomArray(builtInType, useBoundaryValues, arrayLength, true);
            EncodeDecodeDataValue(encoderType, builtInType, MemoryStreamType.ArraySegmentStream, randomData);
        }

        /// <summary>
        /// Verify encode and decode of a zero length array
        /// of a builtin type as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeBuiltInTypeZeroLengthArrayAsVariantInDataValue(
            EncodingType encoderType,
            BuiltInType builtInType
            )
        {
            object randomData = DataGenerator.GetRandomArray(builtInType, false, 0, true);
            EncodeDecodeDataValue(encoderType, builtInType, MemoryStreamType.RecyclableMemoryStream, randomData);
        }

        /// <summary>
        /// Verify encode and decode of a random built in type
        /// as Variant in a DataValue.
        /// </summary>
        [Theory]
        [Category("BuiltInType"), Repeat(kRandomRepeats)]
        public void ReEncodeBuiltInTypeRandomVariantInDataValue(
            EncodingType encoderType
            )
        {
            SetRepeatedRandomSeed();
            object randomData = DataGenerator.GetRandom(BuiltInType.Variant);
            EncodeDecodeDataValue(encoderType, BuiltInType.Variant, MemoryStreamType.MemoryStream, randomData);
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
                    () => EncodeDataValue(EncodingType.Json, builtInType, MemoryStreamType.ArraySegmentStream, randomData, false)
                );
                return;
            }
            string json = EncodeDataValue(EncodingType.Json, builtInType, MemoryStreamType.MemoryStream, randomData, false);
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
            string json = EncodeDataValue(EncodingType.Json, builtInType, MemoryStreamType.RecyclableMemoryStream, randomData, false);
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
            string json = EncodeDataValue(EncodingType.Json, builtInType, MemoryStreamType.MemoryStream, randomData, false);
            PrettifyAndValidateJson(json);
        }

        /// <summary>
        /// Verify encode and decode of a VariantCollection.
        /// </summary>
        [Theory]
        [Category("BuiltInType")]
        public void ReEncodeVariantCollectionInDataValue(
            EncodingType encoderType
            )
        {
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
            EncodeDecodeDataValue(encoderType, BuiltInType.Variant, MemoryStreamType.ArraySegmentStream, variant);
        }

        /// <summary>
        /// Verify encode and decode of a Matrix in a Variant.
        /// </summary>
        [Theory]
        [Category("Array"), Repeat(kArrayRepeats)]
        public void ReEncodeVariantArrayInDataValue(
            EncodingType encoderType,
            BuiltInType builtInType
            )
        {
            SetRepeatedRandomSeed();
            Assume.That(builtInType != BuiltInType.Null);
            int arrayDimension = RandomSource.NextInt32(99) + 1;
            Array randomData = DataGenerator.GetRandomArray(builtInType, false, arrayDimension, true);
            var variant = new Variant(randomData, new TypeInfo(builtInType, 1));
            EncodeDecodeDataValue(encoderType, BuiltInType.Variant, MemoryStreamType.RecyclableMemoryStream, variant);
        }

        /// <summary>
        /// Verify encode and decode of a one dimensional Array.
        /// </summary>
        [Theory]
        [Category("Array"), Repeat(kArrayRepeats)]
        public void EncodeArray(
            EncodingType encoderType,
            BuiltInType builtInType
            )
        {
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
                using (IEncoder encoder = CreateEncoder(encoderType, Context, encoderStream, type, true, false))
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

            Assert.AreEqual(expected, result, encodeInfo);
            Assert.IsTrue(Utils.IsEqual(expected, result), "Opc.Ua.Utils.IsEqual failed to compare expected and result. " + encodeInfo);
        }

        /// <summary>
        /// Verify encode and decode of a Matrix in a Variant.
        /// </summary>
        [Theory]
        [Category("Matrix"), Repeat(kArrayRepeats)]
        public void ReEncodeVariantMatrixInDataValue(
            EncodingType encoderType,
            BuiltInType builtInType
            )
        {
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
            EncodeDecodeDataValue(encoderType, BuiltInType.Variant, MemoryStreamType.RecyclableMemoryStream, variant);
        }

        /// <summary>
        /// Verify encode of a Matrix in a Variant to non reversible JSON.
        /// </summary>
        [Theory]
        [Category("Matrix"), Repeat(kArrayRepeats)]
        public void EncodeBuiltInTypeMatrixAsVariantInDataValueToNonReversibleJson(
            BuiltInType builtInType
            )
        {
            SetRepeatedRandomSeed();
            Assume.That(builtInType != BuiltInType.Null);
            int matrixDimension = RandomSource.NextInt32(3) + 2;
            int[] dimensions = new int[matrixDimension];
            SetMatrixDimensions(dimensions);
            int elements = ElementsFromDimension(dimensions);
            Array randomData = DataGenerator.GetRandomArray(builtInType, false, elements, true);
            var variant = new Variant(new Matrix(randomData, builtInType, dimensions));
            string json = EncodeDataValue(EncodingType.Json, BuiltInType.Variant, MemoryStreamType.ArraySegmentStream, variant, false);
            _ = PrettifyAndValidateJson(json);
        }

        /// <summary>
        /// Verify encode of a Matrix in a multi dimensional array.
        /// </summary>
        [Theory]
        [Category("Matrix"), Repeat(kArrayRepeats)]
        public void EncodeMatrixInArray(
            EncodingType encoderType,
            BuiltInType builtInType)
        {
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
                using (IEncoder encoder = CreateEncoder(encoderType, Context, encoderStream, type))
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

            Assert.AreEqual(expected, result, encodeInfo);
            Assert.IsTrue(Utils.IsEqual(expected, result), "Opc.Ua.Utils.IsEqual failed to compare expected and result. " + encodeInfo);
        }

        /// <summary>
        /// Verify that decoding of a Matrix DataValue which has invalid array dimensions.
        /// </summary
        [Theory]
        [Category("Matrix")]
        public void MatrixOverflow(
            EncodingType encoderType,
            BuiltInType builtInType
            )
        {
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
                using (IEncoder encoder = CreateEncoder(encoderType, Context, encoderStream, typeof(DataValue)))
                {
                    encoder.WriteDataValue("DataValue", expected);
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
            EncodingType encoderType,
            BuiltInType builtInType
            )
        {
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
                using (IEncoder encoder = CreateEncoder(encoderType, Context, encoderStream, typeof(DataValue)))
                {
                    encoder.WriteDataValue("DataValue", expected);
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
            EncodingType encoderType,
            BuiltInType builtInType
            )
        {
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
                using (IEncoder encoder = CreateEncoder(encoderType, Context, encoderStream, type))
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
