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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Xml;
using Microsoft.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Test;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.Encoders
{
    /// <summary>
    /// Supported memory stream types.
    /// </summary>
    public enum MemoryStreamType
    {
        MemoryStream,
        ArraySegmentStream,
        RecyclableMemoryStream
    }

    /// <summary>
    /// Base class for the encoder tests.
    /// </summary>
    [TestFixture]
    [Category("Encoder")]
    [SetCulture("en-us")]
    public class EncoderCommon
    {
        protected const int kArrayRepeats = 3;
        protected const int kRandomStart = 4840;
        protected const int kRandomRepeats = 100;
        protected const int kMaxArrayLength = 1024 * 64;
        protected const int kTestBlockSize = 0x1000;
        protected const string kApplicationUri = "uri:localhost:opcfoundation.org:EncoderCommon";
        protected RandomSource RandomSource { get; private set; }
        protected DataGenerator DataGenerator { get; private set; }
        protected IServiceMessageContext Context { get; private set; }
        protected NamespaceTable NameSpaceUris { get; private set; }
        protected StringTable ServerUris { get; private set; }
        protected BufferManager BufferManager { get; private set; }
        protected RecyclableMemoryStreamManager RecyclableMemoryManager { get; private set; }
        protected ITelemetryContext Telemetry { get; private set; }

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            Telemetry = NUnitTelemetryContext.Create();
            Context = new ServiceMessageContext(Telemetry) { MaxArrayLength = kMaxArrayLength };
            NameSpaceUris = Context.NamespaceUris;
            // namespace index 1 must be the ApplicationUri
            NameSpaceUris.GetIndexOrAppend(kApplicationUri);
            NameSpaceUris.GetIndexOrAppend(Namespaces.OpcUaGds);
            ServerUris = new StringTable();
            BufferManager = new BufferManager(nameof(EncoderCommon), kTestBlockSize, Telemetry);
            RecyclableMemoryManager = new RecyclableMemoryStreamManager(
                new RecyclableMemoryStreamManager.Options { BlockSize = kTestBlockSize });
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
        }

        [SetUp]
        protected void SetUp()
        {
            // ensure tests are reproducible, reset for every test
            RandomSource = new RandomSource(kRandomStart);
            DataGenerator = new DataGenerator(RandomSource, Telemetry);
        }

        [TearDown]
        protected void TearDown()
        {
            // ensure after every test that the Null NodeId was not modified
            Assert.True(NodeId.Null.IsNull);
        }

        /// <summary>
        /// Ensure repeated tests get different seed.
        /// </summary>
        protected void SetRepeatedRandomSeed()
        {
            int randomSeed = TestContext.CurrentContext.CurrentRepeatCount + kRandomStart;
            RandomSource = new RandomSource(randomSeed);
            DataGenerator = new DataGenerator(RandomSource, Telemetry);
        }

        /// <summary>
        /// Ensure tests are reproducible with same seed.
        /// </summary>
        protected void SetRandomSeed(int randomSeed)
        {
            RandomSource = new RandomSource(randomSeed + kRandomStart);
            DataGenerator = new DataGenerator(RandomSource, Telemetry);
        }

        [DatapointSource]
        public static readonly BuiltInType[] BuiltInTypes =
#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
        [
            .. Enum.GetValues<BuiltInType>()
#else
        [
            .. Enum.GetValues(typeof(BuiltInType))
                .Cast<BuiltInType>()
#endif
                .Where(b =>
                    b
                        is not BuiltInType.Variant
                            and not BuiltInType.DiagnosticInfo
                            and not BuiltInType.DataValue
                            and (< BuiltInType.Number or > BuiltInType.UInteger))
        ];

        [DatapointSource]
        public static readonly EncodingType[] EncoderTypes =
#if NET8_0_OR_GREATER && !NET_STANDARD_TESTS
        Enum.GetValues<EncodingType>();
#else
        (EncodingType[])Enum.GetValues(typeof(EncodingType));
#endif

        public static readonly EncodingTypeGroup[] EncodingTypesJson =
        [
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Reversible),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Compact),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.NonReversible),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Verbose)
        ];

        public static readonly EncodingTypeGroup[] EncodingTypesJsonNonReversibleVerbose =
        [
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Reversible),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Compact)
        ];

        public static readonly EncodingTypeGroup[] EncodingTypesReversibleCompact =
        [
            new EncodingTypeGroup(EncodingType.Binary),
            new EncodingTypeGroup(EncodingType.Xml),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Reversible),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Compact)
        ];

        public static readonly EncodingTypeGroup[] EncodingTypesNonReversibleVerbose =
        [
            new EncodingTypeGroup(EncodingType.Binary),
            new EncodingTypeGroup(EncodingType.Xml),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.NonReversible),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Verbose)
        ];

        public static readonly EncodingTypeGroup[] EncodingTypesAll =
        [
            new EncodingTypeGroup(EncodingType.Binary),
            new EncodingTypeGroup(EncodingType.Xml),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.NonReversible),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Reversible),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Compact),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Verbose)
        ];

        public static readonly EncodingTypeGroup[] EncodingTypesAllButJsonNonReversible =
        [
            new EncodingTypeGroup(EncodingType.Binary),
            new EncodingTypeGroup(EncodingType.Xml),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Reversible),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Compact),
            new EncodingTypeGroup(EncodingType.Json, JsonEncodingType.Verbose)
        ];

        /// <summary>
        /// Encode data value and return encoded string.
        /// </summary>
        protected string EncodeDataValue(
            EncodingType encoderType,
            BuiltInType builtInType,
            MemoryStreamType memoryStreamType,
            object data,
            JsonEncodingType encoding)
        {
            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType} Encoding:{encoding}";
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine(data);
            DataValue expected = CreateDataValue(builtInType, data);
            TestContext.Out.WriteLine("Expected:");
            TestContext.Out.WriteLine(expected);
            Assert.IsNotNull(expected, "Expected DataValue is Null, " + encodeInfo);
            using MemoryStream encoderStream = CreateEncoderMemoryStream(memoryStreamType);
            using (IEncoder encoder = CreateEncoder(
                encoderType,
                Context,
                encoderStream,
                typeof(DataValue),
                encoding))
            {
                encoder.WriteDataValue("DataValue", expected);
            }
            byte[] buffer = encoderStream.ToArray();
            return Encoding.UTF8.GetString(buffer);
        }

        /// <summary>
        /// Encode and decode a DataValue,
        /// validate the result against the input data.
        /// </summary>
        protected void EncodeDecodeDataValue(
            EncodingType encoderType,
            JsonEncodingType jsonEncodingType,
            BuiltInType builtInType,
            MemoryStreamType memoryStreamType,
            object data)
        {
            string encodeInfo = $"Encoder: {encoderType} Type:{builtInType}";
            TestContext.Out.WriteLine(encodeInfo);
            TestContext.Out.WriteLine(data);
            DataValue expected = CreateDataValue(builtInType, data);
            Assert.IsNotNull(expected, "Expected DataValue is Null, " + encodeInfo);

            string formatted = null;
            DataValue result = null;
            try
            {
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

                switch (encoderType)
                {
                    case EncodingType.Json:
                        formatted = PrettifyAndValidateJson(buffer);
                        break;
                    case EncodingType.Xml:
                        formatted = PrettifyAndValidateXml(buffer);
                        break;
                }

                using (var decoderStream = new MemoryStream(buffer))
                using (IDecoder decoder = CreateDecoder(
                    encoderType,
                    Context,
                    decoderStream,
                    typeof(DataValue)))
                {
                    result = decoder.ReadDataValue("DataValue");
                }

                Assert.IsNotNull(result, "Resulting DataValue is Null, " + encodeInfo);
                expected.Value
                    = AdjustExpectedBoundaryValues(encoderType, builtInType, expected.Value);
                Assert.AreEqual(expected, result, encodeInfo);
                Assert.IsTrue(
                    Utils.IsEqual(expected, result),
                    "Opc.Ua.Utils.IsEqual failed to compare expected and result. " + encodeInfo);
            }
            catch
            {
                TestContext.Out.WriteLine("Expected:");
                TestContext.Out.WriteLine(expected);
                if (formatted != null)
                {
                    TestContext.Out.WriteLine("Encoded:");
                    TestContext.Out.WriteLine(formatted);
                }

                TestContext.Out.WriteLine("Result:");
                if (result != null)
                {
                    TestContext.Out.WriteLine(result);
                }
            }
        }

        /// <summary>
        /// Encode and decode object, validate result.
        /// </summary>
        protected void EncodeDecode(
            EncodingType encoderType,
            JsonEncodingType jsonEncodingType,
            BuiltInType builtInType,
            MemoryStreamType memoryStreamType,
            object expected)
        {
            string formatted = null;
            object result = null;
            try
            {
                string encodeInfo = $"Encoder: {encoderType} Type:{builtInType}";
                Type type = TypeInfo.GetSystemType(builtInType, -1);
                TestContext.Out.WriteLine(encodeInfo);

                byte[] buffer;
                using (MemoryStream encoderStream = CreateEncoderMemoryStream(memoryStreamType))
                {
                    using (
                        IEncoder encoder = CreateEncoder(
                            encoderType,
                            Context,
                            encoderStream,
                            type,
                            jsonEncodingType))
                    {
                        Encode(encoder, builtInType, builtInType.ToString(), expected);
                    }
                    buffer = encoderStream.ToArray();
                }

                switch (encoderType)
                {
                    case EncodingType.Json:
                        formatted = PrettifyAndValidateJson(buffer);
                        break;
                    case EncodingType.Xml:
                        formatted = PrettifyAndValidateXml(buffer);
                        break;
                    default:
                        formatted = Encoding.UTF8.GetString(buffer);
                        break;
                }

                using (var decoderStream = new MemoryStream(buffer))
                using (IDecoder decoder = CreateDecoder(encoderType, Context, decoderStream, type))
                {
                    result = Decode(decoder, builtInType, builtInType.ToString(), type);
                }

                expected = AdjustExpectedBoundaryValues(encoderType, builtInType, expected);
                if (BuiltInType.DateTime == builtInType)
                {
                    expected = Utils.ToOpcUaUniversalTime((DateTime)expected);
                }

                Assert.AreEqual(expected, result, encodeInfo);
                Assert.IsTrue(
                    Utils.IsEqual(expected, result),
                    "Opc.Ua.Utils.IsEqual failed to compare expected and result. " + encodeInfo);
            }
            catch
            {
                // only print infos if test fails, to reduce log output
                TestContext.Out.WriteLine("Expected:");
                TestContext.Out.WriteLine(expected);
                if (result != null)
                {
                    TestContext.Out.WriteLine("Result:");
                    TestContext.Out.WriteLine(result);
                }
                if (formatted != null)
                {
                    TestContext.Out.WriteLine("Encoded:");
                    TestContext.Out.WriteLine(formatted);
                }
                throw;
            }
        }

        /// <summary>
        /// Encode object as JSON and validate against expected JSON string.
        /// </summary>
        protected void EncodeJsonVerifyResult(
            BuiltInType builtInType,
            MemoryStreamType memoryStreamType,
            object data,
            JsonEncodingType jsonEncoding,
            string expected,
            bool topLevelIsArray,
            bool includeDefaults)
        {
            string result = null;
            string formattedResult = null;
            try
            {
                string encodeInfo = $"Encoder: Json Type:{builtInType} Encoding: {jsonEncoding}";
                TestContext.Out.WriteLine(encodeInfo);
                if (!string.IsNullOrEmpty(expected))
                {
                    expected = $"{{\"{builtInType}\":" + expected + "}";
                }
                else
                {
                    expected = "{}";
                }

                bool isNumber = TypeInfo.IsNumericType(builtInType) ||
                    builtInType == BuiltInType.Boolean;
                bool includeDefaultValues = !isNumber && includeDefaults;
                bool includeDefaultNumbers = !isNumber || includeDefaults;

                byte[] buffer;
                using (MemoryStream encoderStream = CreateEncoderMemoryStream(memoryStreamType))
                {
                    using (
                        IEncoder encoder = CreateEncoder(
                            EncodingType.Json,
                            Context,
                            encoderStream,
                            typeof(DataValue),
                            jsonEncoding,
                            topLevelIsArray,
                            includeDefaultValues,
                            includeDefaultNumbers))
                    {
                        if (jsonEncoding is JsonEncodingType.Reversible or JsonEncodingType.NonReversible)
                        {
                            // encoder.SetMappingTables(nameSpaceUris, serverUris);
                        }
                        Encode(encoder, builtInType, builtInType.ToString(), data);
                    }
                    buffer = encoderStream.ToArray();
                }

                TestContext.Out.WriteLine("Result:");
                result = Encoding.UTF8.GetString(buffer);
                formattedResult = PrettifyAndValidateJson(result);
                var jsonLoadSettings = new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore,
                    LineInfoHandling = LineInfoHandling.Ignore
                };
                var resultParsed = JObject.Parse(result, jsonLoadSettings);
                var expectedParsed = JObject.Parse(expected, jsonLoadSettings);
                bool areEqual = JToken.DeepEquals(expectedParsed, resultParsed);
                Assert.IsTrue(areEqual, encodeInfo);
            }
            catch
            {
                TestContext.Out.WriteLine("Data:");
                TestContext.Out.WriteLine(data);
                TestContext.Out.WriteLine("Expected:");
                string formattedExpected = PrettifyAndValidateJson(expected);
                TestContext.Out.WriteLine(formattedExpected);
                TestContext.Out.WriteLine("Result:");
                if (!string.IsNullOrEmpty(formattedResult))
                {
                    TestContext.Out.WriteLine(formattedResult);
                }
                else
                {
                    TestContext.Out.WriteLine(result);
                }
                throw;
            }
        }

        /// <summary>
        /// Format and validate a XML document string.
        /// </summary>
        protected string PrettifyAndValidateXml(byte[] xml, bool outputFormatted = false)
        {
            try
            {
                using var reader = new MemoryStream(xml);
                using var xmlReader = XmlReader.Create(reader, Utils.DefaultXmlReaderSettings());
                var document = new XmlDocument();
                document.Load(xmlReader);

                var settings = new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    Indent = true,
                    NewLineOnAttributes = true
                };

                var stringBuilder = new StringBuilder();
                using (var xmlWriter = XmlWriter.Create(stringBuilder, settings))
                {
                    document.Save(xmlWriter);
                }
                string formattedXml = stringBuilder.ToString();
                if (outputFormatted)
                {
                    TestContext.Out.WriteLine(formattedXml);
                }
                return formattedXml;
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine(xml);
                NUnit.Framework.Assert.Fail("Invalid xml data: " + ex.Message);
            }
            return Encoding.UTF8.GetString(xml);
        }

        /// <summary>
        /// Format and validate a JSON string.
        /// </summary>
        public static string PrettifyAndValidateJson(byte[] json, bool outputFormatted = false)
        {
            return PrettifyAndValidateJson(Encoding.UTF8.GetString(json), outputFormatted);
        }

        /// <summary>
        /// Format and validate a JSON string.
        /// </summary>
        public static string PrettifyAndValidateJson(string json, bool outputFormatted = false)
        {
            try
            {
                using var stringWriter = new StringWriter();
                using var stringReader = new StringReader(json);
                using var jsonReader = new JsonTextReader(stringReader);
                using var jsonWriter = new JsonTextWriter(stringWriter)
                {
                    FloatFormatHandling = FloatFormatHandling.String,
                    Formatting = Newtonsoft.Json.Formatting.Indented,
                    Culture = System.Globalization.CultureInfo.InvariantCulture
                };
                jsonWriter.WriteToken(jsonReader);
                string formattedJson = stringWriter.ToString();
                if (outputFormatted)
                {
                    TestContext.Out.WriteLine(formattedJson);
                }
                return formattedJson;
            }
            catch (Exception ex)
            {
                TestContext.Out.WriteLine(json);
                NUnit.Framework.Assert.Fail("Invalid json data: " + ex.Message);
            }
            return json;
        }

        /// <summary>
        /// Returns various implementations of a memory stream.
        /// </summary>
        /// <returns>A MemoryStream</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        protected MemoryStream CreateEncoderMemoryStream(MemoryStreamType memoryStreamType)
        {
            switch (memoryStreamType)
            {
                case MemoryStreamType.MemoryStream:
                    return new MemoryStream(kTestBlockSize);
                case MemoryStreamType.ArraySegmentStream:
                    return new ArraySegmentStream(BufferManager);
                case MemoryStreamType.RecyclableMemoryStream:
                    return new RecyclableMemoryStream(RecyclableMemoryManager);
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(memoryStreamType),
                        memoryStreamType,
                        "Invalid MemoryStreamType specified.");
            }
        }

        /// <summary>
        /// Encoder factory for all encoding types.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        protected IEncoder CreateEncoder(
            EncodingType encoderType,
            IServiceMessageContext context,
            Stream stream,
            Type systemType,
            JsonEncodingType jsonEncoding = JsonEncodingType.Reversible,
            bool topLevelIsArray = false,
            bool includeDefaultValues = false,
            bool includeDefaultNumbers = true)
        {
            switch (encoderType)
            {
                case EncodingType.Binary:
                    Assume.That(
                        jsonEncoding == JsonEncodingType.Reversible,
                        "Binary encoding doesn't allow to set the JsonEncodingType.");
                    return new BinaryEncoder(stream, context, true);
                case EncodingType.Xml:
                    Assume.That(
                        jsonEncoding == JsonEncodingType.Reversible,
                        "Xml encoding only supports reversible option.");
                    var xmlWriter = XmlWriter.Create(stream, Utils.DefaultXmlWriterSettings());
                    return new XmlEncoder(systemType, xmlWriter, context);
                case EncodingType.Json:
                    var encoder = new JsonEncoder(
                        context,
                        jsonEncoding,
                        topLevelIsArray,
                        stream,
                        true);
                    // only deprecated encodings allow to set the default value
                    if (jsonEncoding is JsonEncodingType.Reversible or JsonEncodingType.NonReversible)
                    {
                        encoder.IncludeDefaultValues = includeDefaultValues;
                        encoder.IncludeDefaultNumberValues = includeDefaultNumbers;
                    }
                    return encoder;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(encoderType),
                        encoderType,
                        "Invalid EncoderType specified.");
            }
        }

        /// <summary>
        /// Decoder factory for all decoding types.
        /// </summary>
        protected IDecoder CreateDecoder(
            EncodingType decoderType,
            IServiceMessageContext context,
            Stream stream,
            Type systemType)
        {
            switch (decoderType)
            {
                case EncodingType.Binary:
                    return new BinaryDecoder(stream, context);
                case EncodingType.Xml:
                    var xmlReader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings());
                    return new XmlDecoder(systemType, xmlReader, context);
                case EncodingType.Json:
                    var jsonTextReader = new JsonTextReader(new StreamReader(stream));
                    return new JsonDecoder(systemType, jsonTextReader, context);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Wrap object in a DataValue.
        /// </summary>
        protected DataValue CreateDataValue(BuiltInType builtInType, object data)
        {
            var statusCode = (StatusCode)DataGenerator.GetRandom(BuiltInType.StatusCode);
            var sourceTimeStamp = (DateTime)DataGenerator.GetRandom(BuiltInType.DateTime);
            Variant variant = (builtInType == BuiltInType.Variant) && (data is Variant v)
                ? v
                : new Variant(data);
            return new DataValue(variant, statusCode, sourceTimeStamp, DateTime.UtcNow);
        }

        /// <summary>
        /// Helper for encoding of built in types.
        /// </summary>
        protected void Encode(
            IEncoder encoder,
            BuiltInType builtInType,
            string fieldName,
            object value)
        {
            bool isArray = (value?.GetType().IsArray ?? false) &&
                (builtInType != BuiltInType.ByteString);
            bool isCollection = (value is IList) && (builtInType != BuiltInType.ByteString);
            if (!isArray && !isCollection)
            {
                switch (builtInType)
                {
                    case BuiltInType.Null:
                        encoder.WriteVariant(fieldName, new Variant(value));
                        return;
                    case BuiltInType.Boolean:
                        encoder.WriteBoolean(fieldName, (bool)value);
                        return;
                    case BuiltInType.SByte:
                        encoder.WriteSByte(fieldName, (sbyte)value);
                        return;
                    case BuiltInType.Byte:
                        encoder.WriteByte(fieldName, (byte)value);
                        return;
                    case BuiltInType.Int16:
                        encoder.WriteInt16(fieldName, (short)value);
                        return;
                    case BuiltInType.UInt16:
                        encoder.WriteUInt16(fieldName, (ushort)value);
                        return;
                    case BuiltInType.Int32:
                        encoder.WriteInt32(fieldName, (int)value);
                        return;
                    case BuiltInType.UInt32:
                        encoder.WriteUInt32(fieldName, (uint)value);
                        return;
                    case BuiltInType.Int64:
                        encoder.WriteInt64(fieldName, (long)value);
                        return;
                    case BuiltInType.UInt64:
                        encoder.WriteUInt64(fieldName, (ulong)value);
                        return;
                    case BuiltInType.Float:
                        encoder.WriteFloat(fieldName, (float)value);
                        return;
                    case BuiltInType.Double:
                        encoder.WriteDouble(fieldName, (double)value);
                        return;
                    case BuiltInType.String:
                        encoder.WriteString(fieldName, (string)value);
                        return;
                    case BuiltInType.DateTime:
                        encoder.WriteDateTime(fieldName, (DateTime)value);
                        return;
                    case BuiltInType.Guid:
                        encoder.WriteGuid(fieldName, value is Guid g ? (Uuid)g : (Uuid)value);
                        return;
                    case BuiltInType.ByteString:
                        encoder.WriteByteString(fieldName, (ByteString)value);
                        return;
                    case BuiltInType.XmlElement:
                        encoder.WriteXmlElement(fieldName, (XmlElement)value);
                        return;
                    case BuiltInType.NodeId:
                        encoder.WriteNodeId(fieldName, (NodeId)value);
                        return;
                    case BuiltInType.ExpandedNodeId:
                        encoder.WriteExpandedNodeId(fieldName, (ExpandedNodeId)value);
                        return;
                    case BuiltInType.StatusCode:
                        encoder.WriteStatusCode(fieldName, (StatusCode)value);
                        return;
                    case BuiltInType.QualifiedName:
                        encoder.WriteQualifiedName(fieldName, (QualifiedName)value);
                        return;
                    case BuiltInType.LocalizedText:
                        encoder.WriteLocalizedText(fieldName, (LocalizedText)value);
                        return;
                    case BuiltInType.ExtensionObject:
                        encoder.WriteExtensionObject(fieldName, (ExtensionObject)value);
                        return;
                    case BuiltInType.DataValue:
                        encoder.WriteDataValue(fieldName, (DataValue)value);
                        return;
                    case BuiltInType.Enumeration:
                        if (value.GetType().IsEnum)
                        {
                            encoder.WriteEnumerated(fieldName, (Enum)value);
                        }
                        else
                        {
                            encoder.WriteEnumerated(fieldName, (Enumeration)value);
                        }
                        return;
                    case BuiltInType.Variant:
                        encoder.WriteVariant(fieldName, (Variant)value);
                        return;
                    case BuiltInType.DiagnosticInfo:
                        encoder.WriteDiagnosticInfo(fieldName, (DiagnosticInfo)value);
                        return;
                }
            }
            else
            {
                Type arrayType = value.GetType().GetElementType();
                var array = value as Array;
                if (builtInType == BuiltInType.Variant)
                {
                    encoder.WriteVariantArray(fieldName, (VariantCollection)value);
                    return;
                }
                else if (builtInType == BuiltInType.Enumeration)
                {
                    encoder.WriteEnumeratedArray(fieldName, array, arrayType);
                    return;
                }
            }
            NUnit.Framework.Assert.Fail($"Unknown BuiltInType {builtInType}");
        }

        /// <summary>
        /// Helper for decoding of builtin types.
        /// </summary>
        protected object Decode(
            IDecoder decoder,
            BuiltInType builtInType,
            string fieldName,
            Type type)
        {
            switch (builtInType)
            {
                case BuiltInType.Null:
                    Variant variant = decoder.ReadVariant(fieldName);
                    return variant.AsBoxedObject();
                case BuiltInType.Boolean:
                    return decoder.ReadBoolean(fieldName);
                case BuiltInType.SByte:
                    return decoder.ReadSByte(fieldName);
                case BuiltInType.Byte:
                    return decoder.ReadByte(fieldName);
                case BuiltInType.Int16:
                    return decoder.ReadInt16(fieldName);
                case BuiltInType.UInt16:
                    return decoder.ReadUInt16(fieldName);
                case BuiltInType.Int32:
                    return decoder.ReadInt32(fieldName);
                case BuiltInType.UInt32:
                    return decoder.ReadUInt32(fieldName);
                case BuiltInType.Int64:
                    return decoder.ReadInt64(fieldName);
                case BuiltInType.UInt64:
                    return decoder.ReadUInt64(fieldName);
                case BuiltInType.Float:
                    return decoder.ReadFloat(fieldName);
                case BuiltInType.Double:
                    return decoder.ReadDouble(fieldName);
                case BuiltInType.String:
                    return decoder.ReadString(fieldName);
                case BuiltInType.DateTime:
                    return decoder.ReadDateTime(fieldName);
                case BuiltInType.Guid:
                    return decoder.ReadGuid(fieldName);
                case BuiltInType.ByteString:
                    return decoder.ReadByteString(fieldName);
                case BuiltInType.XmlElement:
                    return decoder.ReadXmlElement(fieldName);
                case BuiltInType.NodeId:
                    return decoder.ReadNodeId(fieldName);
                case BuiltInType.ExpandedNodeId:
                    return decoder.ReadExpandedNodeId(fieldName);
                case BuiltInType.StatusCode:
                    return decoder.ReadStatusCode(fieldName);
                case BuiltInType.QualifiedName:
                    return decoder.ReadQualifiedName(fieldName);
                case BuiltInType.LocalizedText:
                    return decoder.ReadLocalizedText(fieldName);
                case BuiltInType.ExtensionObject:
                    return decoder.ReadExtensionObject(fieldName);
                case BuiltInType.DataValue:
                    return decoder.ReadDataValue(fieldName);
                case BuiltInType.Enumeration:
                    return type.IsEnum
                        ? decoder.ReadEnumerated(fieldName, type)
                        : decoder.ReadInt32(fieldName);
                case BuiltInType.DiagnosticInfo:
                    return decoder.ReadDiagnosticInfo(fieldName);
                case BuiltInType.Variant:
                    return decoder.ReadVariant(fieldName);
                default:
                    NUnit.Framework.Assert.Fail($"Unknown BuiltInType {builtInType}");
                    return null;
            }
        }

        /// <summary>
        /// Adjust expected values to encoder specific results.
        /// </summary>
        protected object AdjustExpectedBoundaryValues(
            EncodingType encoderType,
            BuiltInType builtInType,
            object value)
        {
            if (value == null)
            {
                return value;
            }
            if (builtInType == BuiltInType.Variant)
            {
                // decoder result will be an Int32
                if (value is Matrix enumMatrix &&
                    enumMatrix.TypeInfo.BuiltInType == BuiltInType.Enumeration)
                {
                    return new Matrix(enumMatrix.Elements, BuiltInType.Int32, enumMatrix.Dimensions)
                        .ToArray();
                }
            }
            if (encoderType == EncodingType.Binary)
            {
                if (builtInType is BuiltInType.DateTime or BuiltInType.Variant)
                {
                    if (value.GetType() == typeof(DateTime))
                    {
                        value = AdjustExpectedDateTimeBinaryEncoding((DateTime)value);
                    }

                    if (value.GetType() == typeof(DateTime[]))
                    {
                        var valueArray = (DateTime[])value;
                        for (int i = 0; i < valueArray.Length; i++)
                        {
                            valueArray[i] = AdjustExpectedDateTimeBinaryEncoding(valueArray[i]);
                        }
                    }
                }
                if (builtInType == BuiltInType.DataValue)
                {
                    var dataValue = (DataValue)value;
                    if (dataValue.Value?.GetType() == typeof(DateTime) ||
                        dataValue.Value?.GetType() == typeof(DateTime[]))
                    {
                        dataValue.Value = AdjustExpectedBoundaryValues(
                            encoderType,
                            BuiltInType.DateTime,
                            dataValue.Value);
                        return dataValue;
                    }
                }
            }

            if (value is Matrix matrix)
            {
                return matrix.ToArray();
            }

            return value;
        }

        /// <summary>
        /// Adjust DateTime results of binary encoder.
        /// </summary>
        private static DateTime AdjustExpectedDateTimeBinaryEncoding(DateTime dateTime)
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
        protected static bool IsEncodeableType(Type systemType)
        {
            if (systemType == null)
            {
                return false;
            }

            System.Reflection.TypeInfo systemTypeInfo = systemType.GetTypeInfo();
            if (systemTypeInfo.IsAbstract ||
                !typeof(IEncodeable).GetTypeInfo().IsAssignableFrom(systemTypeInfo))
            {
                return false;
            }

            return Activator.CreateInstance(systemType) is IEncodeable;
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
        /// Number of total elements is limited by <see cref="kMaxArrayLength"/>
        /// </summary>
        protected void SetMatrixDimensions(int[] dimensions)
        {
            int totalElements = 1;
            for (int i = 0; i < dimensions.Length; i++)
            {
                dimensions[i] = RandomSource.NextInt32(8) + 2;
                totalElements *= dimensions[i];
            }
            while (totalElements > kMaxArrayLength)
            {
                int random = RandomSource.NextInt32(dimensions.Length - 1);
                if (dimensions[random] > 1)
                {
                    dimensions[random]--;
                }
                totalElements = 1;
                for (int i = 0; i < dimensions.Length; i++)
                {
                    totalElements *= dimensions[i];
                }
            }
        }

        protected enum TestEnumType
        {
            [EnumMember(Value = "One_1")]
            One = 1,

            [EnumMember(Value = "Two_2")]
            Two = 2,

            [EnumMember(Value = "Three_3")]
            Three = 3,

            [EnumMember(Value = "Ten_10")]
            Ten = 10,

            [EnumMember(Value = "Hundred_100")]
            Hundred = 100
        }

        protected class FooBarEncodeable : IEncodeable, IDisposable
        {
            private static int s_count;

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
                encoder.PushNamespace(kApplicationUri);
                encoder.WriteString(FieldName, Foo);
                encoder.PopNamespace();
            }

            public void Decode(IDecoder decoder)
            {
                decoder.PushNamespace(kApplicationUri);
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
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (disposing && m_resetCounter)
                {
                    s_count = 0;
                }
                // free unmanaged resources
            }

            public virtual object Clone()
            {
                return MemberwiseClone();
            }

            public new object MemberwiseClone()
            {
                return new FooBarEncodeable(FieldName, Foo) { Count = Count };
            }

            private readonly bool m_resetCounter;
        }

        /// <summary>
        /// A simple dynamic encodeable that can handle arbitrary fields of type string
        /// </summary>
        protected class DynamicEncodeable :
            IEncodeable,
            IJsonEncodeable,
            IDisposable,
            IDynamicComplexTypeInstance
        {
            private static int s_count;

            public DynamicEncodeable()
            {
            }

            public DynamicEncodeable(
                string xmlName,
                string xmlNamespace,
                ExpandedNodeId typeId,
                ExpandedNodeId binaryEncodingId,
                ExpandedNodeId xmlEncodingId,
                ExpandedNodeId jsonEncodingId)
                : this(
                    xmlName,
                    xmlNamespace,
                    typeId,
                    binaryEncodingId,
                    xmlEncodingId,
                    jsonEncodingId,
                    (Dictionary<string, (int, string)>)null)
            {
                m_resetCounter = true;
                Count = Interlocked.Increment(ref s_count);

                m_fields = new Dictionary<string, (int, string)> { { "Foo", (1, $"bar_{Count}") } };
            }

            public DynamicEncodeable(
                string xmlName,
                string xmlNamespace,
                ExpandedNodeId typeId,
                ExpandedNodeId binaryEncodingId,
                ExpandedNodeId xmlEncodingId,
                ExpandedNodeId jsonEncodingId,
                int count)
                : this(
                    xmlName,
                    xmlNamespace,
                    typeId,
                    binaryEncodingId,
                    xmlEncodingId,
                    jsonEncodingId,
                    new Dictionary<string, (int, string)> { { "Foo", (1, $"bar_{count}") } })
            {
                Count = count;
            }

            public DynamicEncodeable(
                string xmlName,
                string xmlNamespace,
                ExpandedNodeId typeId,
                ExpandedNodeId binaryEncodingId,
                ExpandedNodeId xmlEncodingId,
                ExpandedNodeId jsonEncodingId,
                string foo)
                : this(
                    xmlName,
                    xmlNamespace,
                    typeId,
                    binaryEncodingId,
                    xmlEncodingId,
                    jsonEncodingId,
                    new Dictionary<string, (int, string)> { { "Foo", (1, foo) } })
            {
            }

            public DynamicEncodeable(
                string xmlName,
                string xmlNamespace,
                ExpandedNodeId typeId,
                ExpandedNodeId binaryEncodingId,
                ExpandedNodeId xmlEncodingId,
                ExpandedNodeId jsonEncodingId,
                Dictionary<string, (int, string)> fields)
            {
                m_xmlName = xmlName;
                m_xmlNamespace = xmlNamespace;
                TypeId = typeId;
                BinaryEncodingId = binaryEncodingId;
                XmlEncodingId = xmlEncodingId;
                JsonEncodingId = jsonEncodingId;

                m_fields = fields;
            }

            public int Count { get; set; }

            public ExpandedNodeId TypeId { get; set; }
            public ExpandedNodeId BinaryEncodingId { get; set; }
            public ExpandedNodeId XmlEncodingId { get; set; }
            public ExpandedNodeId JsonEncodingId { get; set; }

            public void Encode(IEncoder encoder)
            {
                InitializeFromFactory(encoder.Context?.Factory);
                encoder.PushNamespace(m_xmlNamespace);
                foreach (
                    KeyValuePair<string, (int FieldOrder, string Value)> field in m_fields
                        .OrderBy(kv => kv.Value.FieldOrder)
                        .ToList())
                {
                    encoder.WriteString(field.Key, field.Value.Value);
                }
                encoder.PopNamespace();
            }

            public void Decode(IDecoder decoder)
            {
                InitializeFromFactory(decoder.Context?.Factory);
                decoder.PushNamespace(m_xmlNamespace);
                foreach (
                    KeyValuePair<string, (int FieldOrder, string Value)> fieldKV in m_fields
                        .OrderBy(kv => kv.Value.FieldOrder)
                        .ToList())
                {
                    m_fields[fieldKV.Key] = (fieldKV.Value.FieldOrder, decoder.ReadString(
                        fieldKV.Key));
                }
                decoder.PopNamespace();
            }

            private void InitializeFromFactory(IEncodeableFactory factory)
            {
                if (m_fields == null)
                {
                    // When the dynamic encodeable is instantiated by a encoder/decoder,
                    // it needs to find it's type information

                    // Obtain a previously registered instance from the Factory
                    // Other systems will want to put just type information into the factory,
                    // or have other means of finding type information given an encoding id
                    DynamicEncodeable encodeable = factory is DynamicEncodeableFactory df
                        ? df.GetDynamicEncodeableForEncoding(TypeId)
                        : null;
                    // Read the type information
                    TypeId = encodeable?.TypeId ?? default;
                    XmlEncodingId = encodeable?.XmlEncodingId ?? default;
                    JsonEncodingId = encodeable?.JsonEncodingId ?? default;
                    BinaryEncodingId = encodeable?.BinaryEncodingId ?? default;
                    Count = encodeable?.Count ?? 0;
                    m_fields = encodeable?.m_fields.ToDictionary(
                        kv => kv.Key,
                        kv => (kv.Value.FieldOrder, (string)null));
                    m_xmlName = encodeable?.m_xmlName;
                    m_xmlNamespace = encodeable?.m_xmlNamespace;
                }
            }

            public bool IsEqual(IEncodeable encodeable)
            {
                if (encodeable is DynamicEncodeable de)
                {
                    return m_fields.OrderBy(kv => kv.Key)
                        .SequenceEqual(de.m_fields.OrderBy(kv => kv.Key));
                }

                return false;
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                if (disposing && m_resetCounter)
                {
                    s_count = 0;
                }
            }

            public virtual object Clone()
            {
                return MemberwiseClone();
            }

            public new object MemberwiseClone()
            {
                return new DynamicEncodeable(
                    m_xmlName,
                    m_xmlNamespace,
                    TypeId,
                    BinaryEncodingId,
                    XmlEncodingId,
                    JsonEncodingId,
                    m_fields.ToDictionary(kv => kv.Key, kv => kv.Value))
                {
                    Count = Count
                };
            }

            public XmlQualifiedName GetXmlName(IServiceMessageContext context)
            {
                InitializeFromFactory(context?.Factory);
                return new XmlQualifiedName(m_xmlName, m_xmlNamespace);
            }

            private Dictionary<string, (int FieldOrder, string Value)> m_fields;
            private string m_xmlName;
            private string m_xmlNamespace;
            private readonly bool m_resetCounter;
        }

        protected class DynamicEncodeableFactory : IEncodeableFactory
        {
            public DynamicEncodeableFactory(IEncodeableFactory factory)
            {
                m_inner = factory;
            }

            public IEnumerable<ExpandedNodeId> KnownTypeIds => m_inner.KnownTypeIds;

            public IEncodeableFactoryBuilder Builder => m_inner.Builder;

            public DynamicEncodeable GetDynamicEncodeableForEncoding(ExpandedNodeId typeId)
            {
                if (!typeId.IsNull &&
                    m_dynamicEncodeables.TryGetValue(
                        typeId,
                        out DynamicEncodeable dynamicEncodeable))
                {
                    return dynamicEncodeable;
                }
                return null;
            }

            public void AddDynamicEncodeable(DynamicEncodeable encodeable)
            {
                m_dynamicEncodeables[encodeable.XmlEncodingId] = encodeable;
                m_dynamicEncodeables[encodeable.JsonEncodingId] = encodeable;
                m_dynamicEncodeables[encodeable.BinaryEncodingId] = encodeable;
                m_dynamicEncodeables[encodeable.TypeId] = encodeable;
                m_inner.AddEncodeableType(encodeable.XmlEncodingId, typeof(DynamicEncodeable));
                m_inner.AddEncodeableType(encodeable.JsonEncodingId, typeof(DynamicEncodeable));
                m_inner.AddEncodeableType(encodeable.BinaryEncodingId, typeof(DynamicEncodeable));
                m_inner.AddEncodeableType(encodeable.TypeId, typeof(DynamicEncodeable));
            }

            public bool TryGetEncodeableType(ExpandedNodeId typeId, [NotNullWhen(true)] out IEncodeableType encodeableType)
            {
                return m_inner.TryGetEncodeableType(typeId, out encodeableType);
            }

            private readonly IEncodeableFactory m_inner;
            private readonly Dictionary<ExpandedNodeId, DynamicEncodeable> m_dynamicEncodeables = [];
        }
    }
}
