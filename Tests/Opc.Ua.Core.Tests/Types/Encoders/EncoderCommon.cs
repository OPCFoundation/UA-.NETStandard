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
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opc.Ua.Test;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Base class for the encoder tests.
    /// </summary>
    [TestFixture, Category("Encoder")]
    [SetCulture("en-us")]
    public class EncoderCommon
    {
        protected const int RandomStart = 4840;
        protected const int RandomRepeats = 100;
        protected const string ApplicationUri = "uri:localhost:opcfoundation.org:EncoderCommon";
        protected RandomSource RandomSource { get; private set; }
        protected DataGenerator DataGenerator { get; private set; }
        protected ServiceMessageContext Context { get; private set; }
        protected NamespaceTable NameSpaceUris { get; private set; }
        protected StringTable ServerUris { get; private set; }

        #region Test Setup
        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            Context = new ServiceMessageContext();
            NameSpaceUris = Context.NamespaceUris;
            // namespace index 1 must be the ApplicationUri
            NameSpaceUris.GetIndexOrAppend(ApplicationUri);
            NameSpaceUris.GetIndexOrAppend(Namespaces.OpcUaGds);
            ServerUris = new StringTable();
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {
            // ensure tests are reproducible, reset for every test
            RandomSource = new RandomSource(RandomStart);
            DataGenerator = new DataGenerator(RandomSource);
        }

        [TearDown]
        protected void TearDown()
        {
        }

        /// <summary>
        /// Ensure repeated tests get different seed.
        /// </summary>
        protected void SetRepeatedRandomSeed()
        {
            int randomSeed = TestContext.CurrentContext.Random.Next() + RandomStart;
            RandomSource = new RandomSource(randomSeed);
            DataGenerator = new DataGenerator(RandomSource);
        }

        /// <summary>
        /// Ensure tests are reproducible with same seed.
        /// </summary>
        protected void SetRandomSeed(int randomSeed)
        {
            RandomSource = new RandomSource(randomSeed + RandomStart);
            DataGenerator = new DataGenerator(RandomSource);
        }
        #endregion

        #region DataPointSources
        [DatapointSource]
        public static BuiltInType[] BuiltInTypes = ((BuiltInType[])Enum.GetValues(typeof(BuiltInType)))
            .ToList().Where(b =>
                (b != BuiltInType.Variant) &&
                (b != BuiltInType.DiagnosticInfo) &&
                (b != BuiltInType.DataValue) &&
                (b < BuiltInType.Number || b > BuiltInType.UInteger)
             ).ToArray();

        [DatapointSource]
        public static EncodingType[] EncoderTypes = (EncodingType[])Enum.GetValues(typeof(EncodingType));
        #endregion

        #region Protected Methods
        /// <summary>
        /// Encode data value and return encoded string.
        /// </summary>
        protected string EncodeDataValue(
            EncodingType encoderType,
            BuiltInType builtInType,
            object data,
            bool useReversibleEncoding = true
            )
        {
            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType} Reversible:{useReversibleEncoding}";
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine(data);
            DataValue expected = CreateDataValue(builtInType, data);
            TestContext.Out.WriteLine("Expected:");
            TestContext.Out.WriteLine(expected);
            Assert.IsNotNull(expected, "Expected DataValue is Null, " + encodeInfo);
            var encoderStream = new MemoryStream();
            IEncoder encoder = CreateEncoder(encoderType, Context, encoderStream, typeof(DataValue), useReversibleEncoding);
            encoder.WriteDataValue("DataValue", expected);
            Dispose(encoder);
            var buffer = encoderStream.ToArray();
            return Encoding.UTF8.GetString(buffer);
        }

        /// <summary>
        /// Encode and decode a DataValue,
        /// validate the result against the input data.
        /// </summary>
        protected void EncodeDecodeDataValue(
            EncodingType encoderType,
            BuiltInType builtInType,
            object data
            )
        {
            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType}";
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine(data);
            DataValue expected = CreateDataValue(builtInType, data);
            Assert.IsNotNull(expected, "Expected DataValue is Null, " + encodeInfo);
            TestContext.Out.WriteLine("Expected:");
            TestContext.Out.WriteLine(expected);
            var encoderStream = new MemoryStream();
            IEncoder encoder = CreateEncoder(encoderType, Context, encoderStream, typeof(DataValue));
            encoder.WriteDataValue("DataValue", expected);
            Dispose(encoder);
            var buffer = encoderStream.ToArray();
            string jsonFormatted;
            switch (encoderType)
            {
                case EncodingType.Json:
                    jsonFormatted = PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));
                    break;
            }
            var decoderStream = new MemoryStream(buffer);
            IDecoder decoder = CreateDecoder(encoderType, Context, decoderStream, typeof(DataValue));
            DataValue result = decoder.ReadDataValue("DataValue");
            Dispose(decoder);
            TestContext.Out.WriteLine("Result:");
            TestContext.Out.WriteLine(result);
            Assert.IsNotNull(result, "Resulting DataValue is Null, " + encodeInfo);
            expected.Value = AdjustExpectedBoundaryValues(encoderType, builtInType, expected.Value);
            Assert.AreEqual(expected, result, encodeInfo);
            Assert.IsTrue(Utils.IsEqual(expected, result), "Opc.Ua.Utils.IsEqual failed to compare expected and result. " + encodeInfo);
        }

        /// <summary>
        /// Encode and decode object, validate result.
        /// </summary>
        protected void EncodeDecode(
            EncodingType encoderType,
            BuiltInType builtInType,
            object expected
            )
        {
            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType}";
            Type type = TypeInfo.GetSystemType(builtInType, -1);
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine("Expected:");
            TestContext.Out.WriteLine(expected);
            var encoderStream = new MemoryStream();
            IEncoder encoder = CreateEncoder(encoderType, Context, encoderStream, type);
            Encode(encoder, builtInType, builtInType.ToString(), expected);
            Dispose(encoder);
            var buffer = encoderStream.ToArray();
            string jsonFormatted;
            switch (encoderType)
            {
                case EncodingType.Json:
                    jsonFormatted = PrettifyAndValidateJson(Encoding.UTF8.GetString(buffer));
                    break;
            }
            var decoderStream = new MemoryStream(buffer);
            IDecoder decoder = CreateDecoder(encoderType, Context, decoderStream, type);
            object result = Decode(decoder, builtInType, builtInType.ToString(), type);
            Dispose(decoder);
            TestContext.Out.WriteLine("Result:");
            TestContext.Out.WriteLine(result);
            expected = AdjustExpectedBoundaryValues(encoderType, builtInType, expected);
            if (BuiltInType.DateTime == builtInType)
            {
                expected = Utils.ToOpcUaUniversalTime((DateTime)expected);
            }
            Assert.AreEqual(expected, result, encodeInfo);
            Assert.IsTrue(Opc.Ua.Utils.IsEqual(expected, result), "Opc.Ua.Utils.IsEqual failed to compare expected and result. " + encodeInfo);
        }

        /// <summary>
        /// Encode object as JSON and validate against expected JSON string.
        /// </summary>
        protected void EncodeJsonVerifyResult(
            BuiltInType builtInType,
            object data,
            bool useReversibleEncoding,
            string expected,
            bool topLevelIsArray,
            bool includeDefaults
            )
        {
            string encodeInfo = $"Encoder: Json Type:{builtInType} Reversible: {useReversibleEncoding}";
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine("Data:");
            TestContext.Out.WriteLine(data);
            TestContext.Out.WriteLine("Expected:");
            if (!String.IsNullOrEmpty(expected))
            {
                expected = $"{{\"{builtInType}\":" + expected + "}";
            }
            else
            {
                expected = "{}";
            }
            var formattedExpected = PrettifyAndValidateJson(expected);
            var encoderStream = new MemoryStream();
            bool isNumber = TypeInfo.IsNumericType(builtInType) || builtInType == BuiltInType.Boolean;
            bool includeDefaultValues = !isNumber ? includeDefaults : false;
            bool includeDefaultNumbers = isNumber ? includeDefaults : true;
            IEncoder encoder = CreateEncoder(EncodingType.Json, Context, encoderStream, typeof(DataValue),
                useReversibleEncoding, topLevelIsArray, includeDefaultValues, includeDefaultNumbers);
            //encoder.SetMappingTables(_nameSpaceUris, _serverUris);
            Encode(encoder, builtInType, builtInType.ToString(), data);
            Dispose(encoder);
            var buffer = encoderStream.ToArray();
            TestContext.Out.WriteLine("Result:");
            var result = Encoding.UTF8.GetString(buffer);
            var formattedResult = PrettifyAndValidateJson(result);
            var jsonLoadSettings = new JsonLoadSettings() {
                CommentHandling = CommentHandling.Ignore,
                LineInfoHandling = LineInfoHandling.Ignore
            };
            var resultParsed = JObject.Parse(result, jsonLoadSettings);
            var expectedParsed = JObject.Parse(expected, jsonLoadSettings);
            var areEqual = JToken.DeepEquals(expectedParsed, resultParsed);
            Assert.IsTrue(areEqual, encodeInfo);
        }

        /// <summary>
        /// Format and validate a JSON string.
        /// </summary>
        protected string PrettifyAndValidateJson(string json)
        {
            try
            {
                using (var stringWriter = new StringWriter())
                using (var stringReader = new StringReader(json))
                {
                    var jsonReader = new JsonTextReader(stringReader);
                    var jsonWriter = new JsonTextWriter(stringWriter) {
                        Formatting = Newtonsoft.Json.Formatting.Indented,
                        Culture = System.Globalization.CultureInfo.InvariantCulture
                    };
                    jsonWriter.WriteToken(jsonReader);
                    string formattedJson = stringWriter.ToString();
                    TestContext.Out.WriteLine(formattedJson);
                    return formattedJson;
                }
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine(json);
                Assert.Fail("Invalid json data: " + ex.Message);
            }
            return json;
        }

        /// <summary>
        /// Encoder factory for all encoding types.
        /// </summary>
        /// <returns></returns>
        protected IEncoder CreateEncoder(
            EncodingType encoderType,
            ServiceMessageContext context,
            Stream stream,
            Type systemType,
            bool useReversibleEncoding = true,
            bool topLevelIsArray = false,
            bool includeDefaultValues = false,
            bool includeDefaultNumbers = true
            )
        {
            switch (encoderType)
            {
                case EncodingType.Binary:
                    Assume.That(useReversibleEncoding, "Binary encoding only supports reversible option.");
                    return new BinaryEncoder(stream, context);
                case EncodingType.Xml:
                    Assume.That(useReversibleEncoding, "Xml encoding only supports reversible option.");
                    var xmlWriter = XmlWriter.Create(stream);
                    return new XmlEncoder(systemType, xmlWriter, context);
                case EncodingType.Json:
                    var streamWriter = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
                    return new JsonEncoder(context, useReversibleEncoding, streamWriter, topLevelIsArray) {
                        IncludeDefaultValues = includeDefaultValues,
                        IncludeDefaultNumberValues = includeDefaultNumbers
                    };
            }
            return null;
        }

        /// <summary>
        /// Decoder factory for all decoding types.
        /// </summary>
        protected IDecoder CreateDecoder(
            EncodingType decoderType,
            ServiceMessageContext context,
            Stream stream,
            Type systemType
            )
        {
            switch (decoderType)
            {
                case EncodingType.Binary:
                    return new BinaryDecoder(stream, context);
                case EncodingType.Xml:
                    var xmlReader = XmlReader.Create(stream);
                    return new XmlDecoder(systemType, xmlReader, context);
                case EncodingType.Json:
                    var jsonTextReader = new JsonTextReader(new StreamReader(stream));
                    return new JsonDecoder(systemType, jsonTextReader, context);
            }
            return null;
        }

        /// <summary>
        /// Wrap object in a DataValue.
        /// </summary>
        protected DataValue CreateDataValue(BuiltInType builtInType, object data)
        {
            StatusCode statusCode = (StatusCode)DataGenerator.GetRandom(BuiltInType.StatusCode);
            DateTime sourceTimeStamp = (DateTime)DataGenerator.GetRandom(BuiltInType.DateTime);
            Variant variant = (builtInType == BuiltInType.Variant) && (data is Variant) ? (Variant)data : new Variant(data);
            return new DataValue(variant, statusCode, sourceTimeStamp, DateTime.UtcNow);
        }

        /// <summary>
        /// Standard dispose.
        /// </summary>
        protected void Dispose(object o)
        {
            var dispose = o as IDisposable;
            dispose?.Dispose();
        }

        /// <summary>
        /// Helper for encoding of built in types.
        /// </summary>
        protected void Encode(IEncoder encoder, BuiltInType builtInType, string fieldName, object value)
        {
            bool isArray = (value?.GetType().IsArray ?? false) && (builtInType != BuiltInType.ByteString);
            bool isCollection = (value is IList) && (builtInType != BuiltInType.ByteString);
            if (!isArray && !isCollection)
            {
                switch (builtInType)
                {
                    case BuiltInType.Null: { encoder.WriteVariant(fieldName, new Variant(value)); return; }
                    case BuiltInType.Boolean: { encoder.WriteBoolean(fieldName, (bool)value); return; }
                    case BuiltInType.SByte: { encoder.WriteSByte(fieldName, (sbyte)value); return; }
                    case BuiltInType.Byte: { encoder.WriteByte(fieldName, (byte)value); return; }
                    case BuiltInType.Int16: { encoder.WriteInt16(fieldName, (short)value); return; }
                    case BuiltInType.UInt16: { encoder.WriteUInt16(fieldName, (ushort)value); return; }
                    case BuiltInType.Int32: { encoder.WriteInt32(fieldName, (int)value); return; }
                    case BuiltInType.UInt32: { encoder.WriteUInt32(fieldName, (uint)value); return; }
                    case BuiltInType.Int64: { encoder.WriteInt64(fieldName, (long)value); return; }
                    case BuiltInType.UInt64: { encoder.WriteUInt64(fieldName, (ulong)value); return; }
                    case BuiltInType.Float: { encoder.WriteFloat(fieldName, (float)value); return; }
                    case BuiltInType.Double: { encoder.WriteDouble(fieldName, (double)value); return; }
                    case BuiltInType.String: { encoder.WriteString(fieldName, (string)value); return; }
                    case BuiltInType.DateTime: { encoder.WriteDateTime(fieldName, (DateTime)value); return; }
                    case BuiltInType.Guid: { encoder.WriteGuid(fieldName, (Uuid)value); return; }
                    case BuiltInType.ByteString: { encoder.WriteByteString(fieldName, (byte[])value); return; }
                    case BuiltInType.XmlElement: { encoder.WriteXmlElement(fieldName, (XmlElement)value); return; }
                    case BuiltInType.NodeId: { encoder.WriteNodeId(fieldName, (NodeId)value); return; }
                    case BuiltInType.ExpandedNodeId: { encoder.WriteExpandedNodeId(fieldName, (ExpandedNodeId)value); return; }
                    case BuiltInType.StatusCode: { encoder.WriteStatusCode(fieldName, (StatusCode)value); return; }
                    case BuiltInType.QualifiedName: { encoder.WriteQualifiedName(fieldName, (QualifiedName)value); return; }
                    case BuiltInType.LocalizedText: { encoder.WriteLocalizedText(fieldName, (LocalizedText)value); return; }
                    case BuiltInType.ExtensionObject: { encoder.WriteExtensionObject(fieldName, (ExtensionObject)value); return; }
                    case BuiltInType.DataValue: { encoder.WriteDataValue(fieldName, (DataValue)value); return; }
                    case BuiltInType.Enumeration:
                    {
                        if (value.GetType().IsEnum)
                        {
                            encoder.WriteEnumerated(fieldName, (Enum)value);
                        }
                        else
                        {
                            encoder.WriteEnumerated(fieldName, (Enumeration)value);
                        }
                        return;
                    }
                    case BuiltInType.Variant: { encoder.WriteVariant(fieldName, (Variant)value); return; }
                    case BuiltInType.DiagnosticInfo: { encoder.WriteDiagnosticInfo(fieldName, (DiagnosticInfo)value); return; }
                }
            }
            else
            {
                Type arrayType = value.GetType().GetElementType();
                IEnumerable enumerable = value as IEnumerable;
                Array array = value as Array;
                switch (builtInType)
                {
                    case BuiltInType.Variant: { encoder.WriteVariantArray(fieldName, (VariantCollection)value); return; }
                    case BuiltInType.Enumeration:
                    {
                        encoder.WriteEnumeratedArray(fieldName, array, arrayType);
                        return;
                    }
                }
            }
            Assert.Fail($"Unknown BuiltInType {builtInType}");
        }


        /// <summary>
        /// Helper for decoding of builtin types.
        /// </summary>
        protected object Decode(IDecoder decoder, BuiltInType builtInType, string fieldName, Type type)
        {
            switch (builtInType)
            {
                case BuiltInType.Null: { var variant = decoder.ReadVariant(fieldName); return variant.Value; }
                case BuiltInType.Boolean: { return decoder.ReadBoolean(fieldName); }
                case BuiltInType.SByte: { return decoder.ReadSByte(fieldName); }
                case BuiltInType.Byte: { return decoder.ReadByte(fieldName); }
                case BuiltInType.Int16: { return decoder.ReadInt16(fieldName); }
                case BuiltInType.UInt16: { return decoder.ReadUInt16(fieldName); }
                case BuiltInType.Int32: { return decoder.ReadInt32(fieldName); }
                case BuiltInType.UInt32: { return decoder.ReadUInt32(fieldName); }
                case BuiltInType.Int64: { return decoder.ReadInt64(fieldName); }
                case BuiltInType.UInt64: { return decoder.ReadUInt64(fieldName); }
                case BuiltInType.Float: { return decoder.ReadFloat(fieldName); }
                case BuiltInType.Double: { return decoder.ReadDouble(fieldName); }
                case BuiltInType.String: { return decoder.ReadString(fieldName); }
                case BuiltInType.DateTime: { return decoder.ReadDateTime(fieldName); }
                case BuiltInType.Guid: { return decoder.ReadGuid(fieldName); }
                case BuiltInType.ByteString: { return decoder.ReadByteString(fieldName); }
                case BuiltInType.XmlElement: { return decoder.ReadXmlElement(fieldName); }
                case BuiltInType.NodeId: { return decoder.ReadNodeId(fieldName); }
                case BuiltInType.ExpandedNodeId: { return decoder.ReadExpandedNodeId(fieldName); }
                case BuiltInType.StatusCode: { return decoder.ReadStatusCode(fieldName); }
                case BuiltInType.QualifiedName: { return decoder.ReadQualifiedName(fieldName); }
                case BuiltInType.LocalizedText: { return decoder.ReadLocalizedText(fieldName); }
                case BuiltInType.ExtensionObject: { return decoder.ReadExtensionObject(fieldName); }
                case BuiltInType.DataValue: { return decoder.ReadDataValue(fieldName); }
                case BuiltInType.Enumeration:
                {
                    return type.IsEnum ? decoder.ReadEnumerated(fieldName, type) : (object)decoder.ReadInt32(fieldName);
                }
                case BuiltInType.DiagnosticInfo: { return decoder.ReadDiagnosticInfo(fieldName); }
                case BuiltInType.Variant: { return decoder.ReadVariant(fieldName); }
            }
            Assert.Fail($"Unknown BuiltInType {builtInType}");
            return null;
        }

        /// <summary>
        /// Adjust expected values to encoder specific results.
        /// </summary>
        protected object AdjustExpectedBoundaryValues(EncodingType encoderType, BuiltInType builtInType, object value)
        {
            if (value == null)
            {
                return value;
            }
            if (builtInType == BuiltInType.Variant)
            {
                // decoder result will be an Int32
                var matrix = value as Matrix;
                if (matrix?.TypeInfo.BuiltInType == BuiltInType.Enumeration)
                {
                    return new Matrix(matrix.Elements, BuiltInType.Int32, matrix.Dimensions);
                }
            }
            if (encoderType == EncodingType.Binary)
            {
                if (builtInType == BuiltInType.DateTime || builtInType == BuiltInType.Variant)
                {
                    if (value.GetType() == typeof(DateTime))
                    {
                        value = AdjustExpectedDateTimeBinaryEncoding((DateTime)value);
                    }

                    if (value.GetType() == typeof(DateTime[]))
                    {
                        DateTime[] valueArray = (DateTime[])value;
                        for (int i = 0; i < valueArray.Length; i++)
                        {
                            valueArray[i] = AdjustExpectedDateTimeBinaryEncoding(valueArray[i]);
                        }
                    }
                }
                if (builtInType == BuiltInType.DataValue)
                {
                    DataValue dataValue = (DataValue)value;
                    if (dataValue.Value?.GetType() == typeof(DateTime) || dataValue.Value?.GetType() == typeof(DateTime[]))
                    {
                        dataValue.Value = AdjustExpectedBoundaryValues(encoderType, BuiltInType.DateTime, dataValue.Value);
                        return dataValue;
                    }
                }
            }
            return value;
        }

        /// <summary>
        /// Adjust DateTime results of binary encoder.
        /// </summary>
        private DateTime AdjustExpectedDateTimeBinaryEncoding(DateTime dateTime)
        {
            if (dateTime == DateTime.MaxValue || dateTime == DateTime.MinValue)
            {
                return dateTime;
            }
            dateTime = Utils.ToOpcUaUniversalTime(dateTime);
            return dateTime <= Utils.TimeBase ? DateTime.MinValue : dateTime;
        }

        /// <summary>
        /// Helper to add escaped quotes to a string.
        /// </summary>
        protected static string Quotes(string json)
        {
            return "\"" + json + "\"";
        }

        /// <summary>
        /// Return true if system Type is IEncodeable.
        /// </summary>
        protected static bool IsEncodeableType(System.Type systemType)
        {
            if (systemType == null)
            {
                return false;
            }

            var systemTypeInfo = systemType.GetTypeInfo();
            if (systemTypeInfo.IsAbstract ||
                !typeof(IEncodeable).GetTypeInfo().IsAssignableFrom(systemTypeInfo))
            {
                return false;
            }

            IEncodeable encodeable = Activator.CreateInstance(systemType) as IEncodeable;

            if (encodeable == null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Calculates the number of elements from a dimension array.
        /// </summary>
        protected static int ElementsFromDimension(int[] dimensions)
        {
            int elements = 1;
            for (int i = 0; i < dimensions.Length; i++)
            {
                if (dimensions[i] != 0)
                {
                    elements *= dimensions[i];
                }
            }
            return elements;
        }

        /// <summary>
        /// Sets random array dimensions between 2 and 10.
        /// </summary>
        protected void SetMatrixDimensions(int[] dimensions)
        {
            for (int i = 0; i < dimensions.Length; i++)
            {
                dimensions[i] = RandomSource.NextInt32(8) + 2;
            }
        }

        protected enum TestEnumType
        {
            /// <remarks />
            [EnumMember(Value = "One_1")]
            One = 1,

            /// <remarks />
            [EnumMember(Value = "Two_2")]
            Two = 2,

            /// <remarks />
            [EnumMember(Value = "Three_3")]
            Three = 3,

            /// <remarks />
            [EnumMember(Value = "Ten_10")]
            Ten = 10,

            /// <remarks />
            [EnumMember(Value = "Hundred_100")]
            Hundred = 100,
        }
        #endregion

        #region Protected classes
        protected class FooBarEncodeable : IEncodeable, IDisposable
        {
            private static int s_count = 0;

            public FooBarEncodeable()
            {
                m_resetCounter = true;
                Count = Interlocked.Increment(ref s_count);
                Foo = $"bar_{Count}";
                FieldName = nameof(Foo);
            }

            public FooBarEncodeable(int count)
            {
                Count = count;
                Foo = $"bar_{Count}";
                FieldName = nameof(Foo);
            }

            public FooBarEncodeable(string foo)
            {
                Foo = foo;
                FieldName = nameof(Foo);
            }

            public FooBarEncodeable(string fieldname, string foo)
            {
                Foo = foo;
                FieldName = fieldname;
            }

            public string Foo { get; set; }
            public string FieldName { get; set; }
            public int Count { get; set; }

            public ExpandedNodeId TypeId { get; }
            public ExpandedNodeId BinaryEncodingId { get; }
            public ExpandedNodeId XmlEncodingId { get; }

            public void Encode(IEncoder encoder)
            {
                encoder.PushNamespace(ApplicationUri);
                encoder.WriteString(FieldName, Foo);
                encoder.PopNamespace();
            }

            public void Decode(IDecoder decoder)
            {
                decoder.PushNamespace(ApplicationUri);
                Foo = decoder.ReadString(FieldName);
                decoder.PopNamespace();
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                if (encodeable is FooBarEncodeable de)
                {
                    return Foo == de.Foo;
                }

                return false;
            }

            public void Dispose()
            {
                if (m_resetCounter)
                {
                    s_count = 0;
                }
            }

            private bool m_resetCounter;
        }
        #endregion
    }

}
