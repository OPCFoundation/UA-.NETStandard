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
using System.Xml;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Fuzzing code for individual built-in type decoders.
    /// </summary>
    public static partial class FuzzableCode
    {
        private const string BuiltInFieldName = "Value";

        /// <summary>
        /// The NodeId binary decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzNodeIdBinary(Stream stream)
        {
            using MemoryStream memoryStream = PrepareArraySegmentStream(stream);
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadNodeId(BuiltInFieldName));
        }

        /// <summary>
        /// The NodeId binary decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzNodeIdBinary(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadNodeId(BuiltInFieldName));
        }

        /// <summary>
        /// The NodeId JSON decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzNodeIdJson(string input)
        {
            FuzzJsonBuiltInType(input, decoder => _ = decoder.ReadNodeId(BuiltInFieldName));
        }

        /// <summary>
        /// The NodeId JSON decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzNodeIdJson(ReadOnlySpan<byte> input)
        {
#if NETFRAMEWORK
            string json = Encoding.UTF8.GetString(input.ToArray());
#else
            string json = Encoding.UTF8.GetString(input);
#endif
            FuzzJsonBuiltInType(json, decoder => _ = decoder.ReadNodeId(BuiltInFieldName));
        }

        /// <summary>
        /// The NodeId XML decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzNodeIdXml(Stream stream)
        {
            FuzzXmlBuiltInType(stream, decoder => _ = decoder.ReadNodeId(BuiltInFieldName));
        }

        /// <summary>
        /// The NodeId XML decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzNodeIdXml(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzXmlBuiltInType(memoryStream, decoder => _ = decoder.ReadNodeId(BuiltInFieldName));
        }

        /// <summary>
        /// The ExpandedNodeId binary decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzExpandedNodeIdBinary(Stream stream)
        {
            using MemoryStream memoryStream = PrepareArraySegmentStream(stream);
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadExpandedNodeId(BuiltInFieldName));
        }

        /// <summary>
        /// The ExpandedNodeId binary decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzExpandedNodeIdBinary(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadExpandedNodeId(BuiltInFieldName));
        }

        /// <summary>
        /// The ExpandedNodeId JSON decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzExpandedNodeIdJson(string input)
        {
            FuzzJsonBuiltInType(input, decoder => _ = decoder.ReadExpandedNodeId(BuiltInFieldName));
        }

        /// <summary>
        /// The ExpandedNodeId JSON decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzExpandedNodeIdJson(ReadOnlySpan<byte> input)
        {
#if NETFRAMEWORK
            string json = Encoding.UTF8.GetString(input.ToArray());
#else
            string json = Encoding.UTF8.GetString(input);
#endif
            FuzzJsonBuiltInType(json, decoder => _ = decoder.ReadExpandedNodeId(BuiltInFieldName));
        }

        /// <summary>
        /// The ExpandedNodeId XML decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzExpandedNodeIdXml(Stream stream)
        {
            FuzzXmlBuiltInType(stream, decoder => _ = decoder.ReadExpandedNodeId(BuiltInFieldName));
        }

        /// <summary>
        /// The ExpandedNodeId XML decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzExpandedNodeIdXml(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzXmlBuiltInType(memoryStream, decoder => _ = decoder.ReadExpandedNodeId(BuiltInFieldName));
        }

        /// <summary>
        /// The Variant binary decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzVariantBinary(Stream stream)
        {
            using MemoryStream memoryStream = PrepareArraySegmentStream(stream);
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadVariant(BuiltInFieldName));
        }

        /// <summary>
        /// The Variant binary decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzVariantBinary(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadVariant(BuiltInFieldName));
        }

        /// <summary>
        /// The Variant JSON decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzVariantJson(string input)
        {
            FuzzJsonBuiltInType(input, decoder => _ = decoder.ReadVariant(BuiltInFieldName));
        }

        /// <summary>
        /// The Variant JSON decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzVariantJson(ReadOnlySpan<byte> input)
        {
#if NETFRAMEWORK
            string json = Encoding.UTF8.GetString(input.ToArray());
#else
            string json = Encoding.UTF8.GetString(input);
#endif
            FuzzJsonBuiltInType(json, decoder => _ = decoder.ReadVariant(BuiltInFieldName));
        }

        /// <summary>
        /// The Variant XML decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzVariantXml(Stream stream)
        {
            FuzzXmlBuiltInType(stream, decoder => _ = decoder.ReadVariant(BuiltInFieldName));
        }

        /// <summary>
        /// The Variant XML decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzVariantXml(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzXmlBuiltInType(memoryStream, decoder => _ = decoder.ReadVariant(BuiltInFieldName));
        }

        /// <summary>
        /// The ExtensionObject binary decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzExtensionObjectBinary(Stream stream)
        {
            using MemoryStream memoryStream = PrepareArraySegmentStream(stream);
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadExtensionObject(BuiltInFieldName));
        }

        /// <summary>
        /// The ExtensionObject binary decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzExtensionObjectBinary(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadExtensionObject(BuiltInFieldName));
        }

        /// <summary>
        /// The ExtensionObject JSON decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzExtensionObjectJson(string input)
        {
            FuzzJsonBuiltInType(input, decoder => _ = decoder.ReadExtensionObject(BuiltInFieldName));
        }

        /// <summary>
        /// The ExtensionObject JSON decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzExtensionObjectJson(ReadOnlySpan<byte> input)
        {
#if NETFRAMEWORK
            string json = Encoding.UTF8.GetString(input.ToArray());
#else
            string json = Encoding.UTF8.GetString(input);
#endif
            FuzzJsonBuiltInType(json, decoder => _ = decoder.ReadExtensionObject(BuiltInFieldName));
        }

        /// <summary>
        /// The ExtensionObject XML decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzExtensionObjectXml(Stream stream)
        {
            FuzzXmlBuiltInType(stream, decoder => _ = decoder.ReadExtensionObject(BuiltInFieldName));
        }

        /// <summary>
        /// The ExtensionObject XML decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzExtensionObjectXml(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzXmlBuiltInType(memoryStream, decoder => _ = decoder.ReadExtensionObject(BuiltInFieldName));
        }

        /// <summary>
        /// The DataValue binary decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzDataValueBinary(Stream stream)
        {
            using MemoryStream memoryStream = PrepareArraySegmentStream(stream);
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadDataValue(BuiltInFieldName));
        }

        /// <summary>
        /// The DataValue binary decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzDataValueBinary(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadDataValue(BuiltInFieldName));
        }

        /// <summary>
        /// The DataValue JSON decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzDataValueJson(string input)
        {
            FuzzJsonBuiltInType(input, decoder => _ = decoder.ReadDataValue(BuiltInFieldName));
        }

        /// <summary>
        /// The DataValue JSON decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzDataValueJson(ReadOnlySpan<byte> input)
        {
#if NETFRAMEWORK
            string json = Encoding.UTF8.GetString(input.ToArray());
#else
            string json = Encoding.UTF8.GetString(input);
#endif
            FuzzJsonBuiltInType(json, decoder => _ = decoder.ReadDataValue(BuiltInFieldName));
        }

        /// <summary>
        /// The DataValue XML decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzDataValueXml(Stream stream)
        {
            FuzzXmlBuiltInType(stream, decoder => _ = decoder.ReadDataValue(BuiltInFieldName));
        }

        /// <summary>
        /// The DataValue XML decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzDataValueXml(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzXmlBuiltInType(memoryStream, decoder => _ = decoder.ReadDataValue(BuiltInFieldName));
        }

        /// <summary>
        /// The DiagnosticInfo binary decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzDiagnosticInfoBinary(Stream stream)
        {
            using MemoryStream memoryStream = PrepareArraySegmentStream(stream);
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadDiagnosticInfo(BuiltInFieldName));
        }

        /// <summary>
        /// The DiagnosticInfo binary decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzDiagnosticInfoBinary(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadDiagnosticInfo(BuiltInFieldName));
        }

        /// <summary>
        /// The DiagnosticInfo JSON decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzDiagnosticInfoJson(string input)
        {
            FuzzJsonBuiltInType(input, decoder => _ = decoder.ReadDiagnosticInfo(BuiltInFieldName));
        }

        /// <summary>
        /// The DiagnosticInfo JSON decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzDiagnosticInfoJson(ReadOnlySpan<byte> input)
        {
#if NETFRAMEWORK
            string json = Encoding.UTF8.GetString(input.ToArray());
#else
            string json = Encoding.UTF8.GetString(input);
#endif
            FuzzJsonBuiltInType(json, decoder => _ = decoder.ReadDiagnosticInfo(BuiltInFieldName));
        }

        /// <summary>
        /// The DiagnosticInfo XML decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzDiagnosticInfoXml(Stream stream)
        {
            FuzzXmlBuiltInType(stream, decoder => _ = decoder.ReadDiagnosticInfo(BuiltInFieldName));
        }

        /// <summary>
        /// The DiagnosticInfo XML decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzDiagnosticInfoXml(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzXmlBuiltInType(memoryStream, decoder => _ = decoder.ReadDiagnosticInfo(BuiltInFieldName));
        }

        /// <summary>
        /// The QualifiedName binary decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzQualifiedNameBinary(Stream stream)
        {
            using MemoryStream memoryStream = PrepareArraySegmentStream(stream);
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadQualifiedName(BuiltInFieldName));
        }

        /// <summary>
        /// The QualifiedName binary decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzQualifiedNameBinary(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadQualifiedName(BuiltInFieldName));
        }

        /// <summary>
        /// The QualifiedName JSON decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzQualifiedNameJson(string input)
        {
            FuzzJsonBuiltInType(input, decoder => _ = decoder.ReadQualifiedName(BuiltInFieldName));
        }

        /// <summary>
        /// The QualifiedName JSON decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzQualifiedNameJson(ReadOnlySpan<byte> input)
        {
#if NETFRAMEWORK
            string json = Encoding.UTF8.GetString(input.ToArray());
#else
            string json = Encoding.UTF8.GetString(input);
#endif
            FuzzJsonBuiltInType(json, decoder => _ = decoder.ReadQualifiedName(BuiltInFieldName));
        }

        /// <summary>
        /// The QualifiedName XML decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzQualifiedNameXml(Stream stream)
        {
            FuzzXmlBuiltInType(stream, decoder => _ = decoder.ReadQualifiedName(BuiltInFieldName));
        }

        /// <summary>
        /// The QualifiedName XML decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzQualifiedNameXml(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzXmlBuiltInType(memoryStream, decoder => _ = decoder.ReadQualifiedName(BuiltInFieldName));
        }

        /// <summary>
        /// The LocalizedText binary decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzLocalizedTextBinary(Stream stream)
        {
            using MemoryStream memoryStream = PrepareArraySegmentStream(stream);
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadLocalizedText(BuiltInFieldName));
        }

        /// <summary>
        /// The LocalizedText binary decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzLocalizedTextBinary(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzBinaryBuiltInType(memoryStream, decoder => _ = decoder.ReadLocalizedText(BuiltInFieldName));
        }

        /// <summary>
        /// The LocalizedText JSON decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzLocalizedTextJson(string input)
        {
            FuzzJsonBuiltInType(input, decoder => _ = decoder.ReadLocalizedText(BuiltInFieldName));
        }

        /// <summary>
        /// The LocalizedText JSON decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzLocalizedTextJson(ReadOnlySpan<byte> input)
        {
#if NETFRAMEWORK
            string json = Encoding.UTF8.GetString(input.ToArray());
#else
            string json = Encoding.UTF8.GetString(input);
#endif
            FuzzJsonBuiltInType(json, decoder => _ = decoder.ReadLocalizedText(BuiltInFieldName));
        }

        /// <summary>
        /// The LocalizedText XML decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzLocalizedTextXml(Stream stream)
        {
            FuzzXmlBuiltInType(stream, decoder => _ = decoder.ReadLocalizedText(BuiltInFieldName));
        }

        /// <summary>
        /// The LocalizedText XML decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzLocalizedTextXml(ReadOnlySpan<byte> input)
        {
            using var memoryStream = new MemoryStream(input.ToArray());
            FuzzXmlBuiltInType(memoryStream, decoder => _ = decoder.ReadLocalizedText(BuiltInFieldName));
        }

        private static void FuzzBinaryBuiltInType(MemoryStream stream, Action<IDecoder> decode)
        {
            try
            {
                using var decoder = new BinaryDecoder(stream, MessageContext);
                decode(decoder);
            }
            catch (ServiceResultException sre) when (IsExpectedDecodingException(sre))
            {
            }
        }

        private static void FuzzJsonBuiltInType(string json, Action<IDecoder> decode)
        {
            try
            {
                using var decoder = new JsonDecoder(json, MessageContext);
                decode(decoder);
            }
            catch (ServiceResultException sre) when (IsExpectedDecodingException(sre))
            {
            }
        }

        private static void FuzzXmlBuiltInType(Stream stream, Action<IDecoder> decode)
        {
            try
            {
                using XmlReader reader = XmlReader.Create(stream, Utils.DefaultXmlReaderSettings());
                reader.MoveToContent();
                using var decoder = new XmlDecoder(reader, MessageContext);
                decode(decoder);
            }
            catch (XmlException)
            {
            }
            catch (InvalidOperationException ex) when (ex.Message == "Stack empty.")
            {
            }
            catch (ServiceResultException sre) when (IsExpectedDecodingException(sre))
            {
            }
        }

        private static bool IsExpectedDecodingException(ServiceResultException sre)
        {
            return sre.StatusCode == StatusCodes.BadDecodingError ||
                sre.StatusCode == StatusCodes.BadEncodingLimitsExceeded ||
                sre.StatusCode == StatusCodes.BadNodeIdInvalid;
        }
    }
}
