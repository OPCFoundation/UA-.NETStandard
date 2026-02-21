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
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Tests for the IEncodeable classes.
    /// </summary>
    [TestFixture]
    [Category("EncodeableTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EncodeableTypesTests : EncoderCommon
    {
        [DatapointSource]
        public Type[] TypeArray =
        [
            .. typeof(BaseObjectState).Assembly
                .GetExportedTypes()
                .Where(IsEncodeableType)
        ];

        /// <summary>
        /// Verify encode and decode of an encodeable type.
        /// </summary>
        [Theory]
        [Category("EncodeableTypes")]
        public void ActivateEncodeableType(
            [ValueSource(
                nameof(EncodingTypesReversibleCompact))] EncodingTypeGroup encoderTypeGroup,
            MemoryStreamType memoryStreamType,
            Type systemType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            var testObject = CreateDefaultEncodeableType(systemType) as IEncodeable;
            Assert.NotNull(testObject);

            if (testObject.BinaryEncodingId.IsNull)
            {
                return;
            }

            Assert.False(testObject.BinaryEncodingId.IsNull);
            Assert.False(testObject.TypeId.IsNull);
            Assert.False(testObject.XmlEncodingId.IsNull);
            Assert.AreNotEqual(testObject.TypeId, testObject.BinaryEncodingId);
            Assert.AreNotEqual(testObject.TypeId, testObject.XmlEncodingId);
            Assert.AreNotEqual(testObject.BinaryEncodingId, testObject.XmlEncodingId);
            EncodeDecode(
                encoderType,
                jsonEncodingType,
                BuiltInType.ExtensionObject,
                memoryStreamType,
                new ExtensionObject(testObject.TypeId, testObject));
        }

        [Theory]
        [Category("EncodeableTypes")]
        public void ActivateEncodeableTypeArray(
            [ValueSource(
                nameof(EncodingTypesReversibleCompact))] EncodingTypeGroup encoderTypeGroup,
            MemoryStreamType memoryStreamType,
            Type systemType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            int arrayLength = DataGenerator.GetRandomByte();
            var array = Array.CreateInstance(systemType, arrayLength);
            ExpandedNodeId dataTypeId = NodeId.Null;
            for (int i = 0; i < array.Length; i++)
            {
                var testObject = CreateDefaultEncodeableType(systemType) as IEncodeable;
                array.SetValue(testObject, i);
                if (dataTypeId.IsNull)
                {
                    dataTypeId = testObject.TypeId;
                }
            }

            const string objectName = "Array";

            byte[] buffer;
            using (MemoryStream encoderStream = CreateEncoderMemoryStream(memoryStreamType))
            {
                using (
                    IEncoder encoder = CreateEncoder(
                        encoderType,
                        Context,
                        encoderStream,
                        systemType,
                        jsonEncodingType))
                {
                    encoder.PushNamespace("urn:This:is:another:namespace");
                    encoder.WriteVariantValue(objectName, VariantHelper.CastFromWithReflectionFallback(array));
                    encoder.PopNamespace();
                }
                buffer = encoderStream.ToArray();
            }

            switch (encoderType)
            {
                case EncodingType.Json:
                    PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));
                    break;
                case EncodingType.Xml:
                    string xml = Encoding.UTF8.GetString(buffer);
                    Assert.IsTrue(
                        xml.Contains(
                            "<Array xmlns=\"urn:This:is:another:namespace\">",
                            StringComparison.Ordinal));
                    break;
                case EncodingType.Binary:
                    break;
                default:
                    NUnit.Framework.Assert.Fail($"Encoder type {encoderType} not supported.");
                    break;
            }

            object result;
            using (var decoderStream = new MemoryStream(buffer))
            using (IDecoder decoder = CreateDecoder(
                encoderType,
                Context,
                decoderStream,
                systemType))
            {
                decoder.PushNamespace("urn:This:is:another:namespace");
                result = decoder.ReadArray(
                    objectName,
                    ValueRanks.OneDimension,
                    BuiltInType.Variant,
                    systemType,
                    dataTypeId);
                decoder.PopNamespace();
            }

            TestContext.Out.WriteLine("Result:");
            TestContext.Out.WriteLine(result);

            object expected = VariantHelper.CastFromWithReflectionFallback(array);

            string encodeInfo =
                $"Encoder: {encoderType} Type: Array of {systemType}. Expected is different from result.";

            Assert.IsTrue(Utils.IsEqual(expected, result), encodeInfo);
            Assert.IsTrue(
                Utils.IsEqual(expected, result),
                "Opc.Ua.Utils.IsEqual failed to compare expected and result. " + encodeInfo);
        }

        [Theory]
        [Category("EncodeableTypes")]
        public void ActivateEncodeableTypeMatrix(
            [ValueSource(
                nameof(EncodingTypesReversibleCompact))] EncodingTypeGroup encoderTypeGroup,
            MemoryStreamType memoryStreamType,
            Type systemType)
        {
            EncodingType encoderType = encoderTypeGroup.EncoderType;
            JsonEncodingType jsonEncodingType = encoderTypeGroup.JsonEncodingType;
            int matrixDimension = RandomSource.NextInt32(2) + 2;
            int[] dimensions = new int[matrixDimension];
            SetMatrixDimensions(dimensions);
            int elementsCount = ElementsFromDimension(dimensions);
            var array = Array.CreateInstance(systemType, elementsCount);

            ExpandedNodeId dataTypeId = NodeId.Null;
            for (int i = 0; i < array.Length; i++)
            {
                var testObject = CreateDefaultEncodeableType(systemType) as IEncodeable;
                array.SetValue(testObject, i);
                if (dataTypeId.IsNull)
                {
                    dataTypeId = testObject.TypeId;
                }
            }

            const string objectName = "Matrix";
            const BuiltInType builtInType = BuiltInType.Variant;

            var matrix = new Matrix(array, builtInType, dimensions);

            byte[] buffer;
            using (MemoryStream encoderStream = CreateEncoderMemoryStream(memoryStreamType))
            {
                using (
                    IEncoder encoder = CreateEncoder(
                        encoderType,
                        Context,
                        encoderStream,
                        systemType,
                        jsonEncodingType))
                {
                    encoder.WriteVariantValue(
                        objectName,
                        VariantHelper.CastFromWithReflectionFallback(matrix.ToArray()));

                }
                buffer = encoderStream.ToArray();
            }

            if (encoderType == EncodingType.Json)
            {
                PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));
            }

            Array result;
            using (var decoderStream = new MemoryStream(buffer))
            using (IDecoder decoder = CreateDecoder(
                encoderType,
                Context,
                decoderStream,
                systemType))
            {
                result = decoder.ReadArray(
                    objectName,
                    matrix.TypeInfo.ValueRank,
                    BuiltInType.Variant,
                    systemType,
                    dataTypeId);
            }

            TestContext.Out.WriteLine("Result:");
            TestContext.Out.WriteLine(result);

            string encodeInfo =
                $"Encoder: {encoderType} Type: Matrix of {systemType}. Expected is different from result.";

            // note: Array.Equals just compares the references, so check the matrix
            var expected = VariantHelper.CastFromWithReflectionFallback(result);
            Assert.IsTrue(
                Utils.IsEqual(expected, result),
                "Opc.Ua.Utils.IsEqual failed to compare expected and result. " + encodeInfo);
        }

        /// <summary>
        /// Create an instance of an encodeable type with default values.
        /// </summary>
        /// <param name="systemType">The type to create</param>
        private static object CreateDefaultEncodeableType(Type systemType)
        {
            object instance = Activator.CreateInstance(systemType);
            SetDefaultEncodeableType(instance);
            return instance;
        }

        /// <summary>
        /// Set encodeable type properties recursively
        /// to expected default values.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void SetDefaultEncodeableType(object typeInstance)
        {
            foreach (System.Reflection.PropertyInfo property in typeInstance.GetType()
                .GetProperties())
            {
                if (property.CanWrite)
                {
                    var typeInfo = TypeInfo.Construct(property.PropertyType);
                    switch (typeInfo.BuiltInType)
                    {
                        case BuiltInType.ExtensionObject:
                            object propertyObject = property.GetValue(typeInstance);
                            if (propertyObject == null &&
                                property.PropertyType.IsAssignableFrom(typeof(ExtensionObject)))
                            {
                                property.SetValue(typeInstance, ExtensionObject.Null);
                            }
                            else if (propertyObject is not null and IEncodeable)
                            {
                                SetDefaultEncodeableType(propertyObject);
                            }
                            break;
                        case BuiltInType.Null:
                            break;
                        case BuiltInType.DataValue:
                            if (property.GetValue(typeInstance) == null)
                            {
                                property.SetValue(typeInstance, new DataValue());
                            }
                            break;
                        case BuiltInType.NodeId:
                            if (property.GetValue(typeInstance) == null)
                            {
                                property.SetValue(typeInstance, NodeId.Null);
                            }
                            break;
                        case BuiltInType.ExpandedNodeId:
                            if (property.GetValue(typeInstance) == null)
                            {
                                property.SetValue(typeInstance, ExpandedNodeId.Null);
                            }
                            break;
                        case BuiltInType.DiagnosticInfo:
                            if (property.GetValue(typeInstance) == null)
                            {
                                if (typeInfo.ValueRank == ValueRanks.Scalar)
                                {
                                    property.SetValue(typeInstance, new DiagnosticInfo());
                                }
                                else
                                {
                                    property.SetValue(typeInstance, new DiagnosticInfoCollection());
                                }
                            }
                            break;
                        case BuiltInType.Enumeration:
                            if (typeInfo.ValueRank == ValueRanks.Scalar)
                            {
                                foreach (object ii in Enum.GetValues(property.PropertyType))
                                {
                                    property.SetValue(typeInstance, ii);
                                    break;
                                }
                            }
                            break;
                        case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                            if (typeInfo.ValueRank == ValueRanks.Scalar)
                            {
                                object value = TypeInfo.GetDefaultValue(typeInfo.BuiltInType);
                                property.SetValue(typeInstance, value);
                            }
                            break;
                        default:
                            throw ServiceResultException.Unexpected(
                                $"Unexpected BuiltInType {typeInfo.BuiltInType}");
                    }
                }
            }
        }
    }
}
