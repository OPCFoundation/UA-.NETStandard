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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Fuzzing code for the JSON decoder and encoder.
    /// </summary>
    public static partial class FuzzableCode
    {
        internal static JsonEncoderOptions LegacyReversibleOptions { get; } = JsonEncoderOptions.Compact with
        {
            Name = "LegacyReversible",
            IgnoreDefaultValues = false,
            ForceNamespaceUri = false
        };

        internal static JsonEncoderOptions LegacyNonReversibleOptions { get; } = JsonEncoderOptions.RawData with
        {
            Name = "LegacyNonReversible"
        };

        internal static ArrayOf<JsonEncoderOptions> JsonEncodingModes { get; } =
        [
            JsonEncoderOptions.Verbose,
            JsonEncoderOptions.Compact,
            JsonEncoderOptions.RawData,
            LegacyReversibleOptions,
            LegacyNonReversibleOptions
        ];

        /// <summary>
        /// The Json decoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzJsonDecoder(string input)
        {
            _ = FuzzJsonDecoderCore(input);
        }

        /// <summary>
        /// The Json encoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzJsonEncoder(string input)
        {
            FuzzJsonEncoderCore(input, JsonEncoderOptions.Verbose);
        }

        /// <summary>
        /// The compact Json encoder fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzJsonEncoderCompact(string input)
        {
            FuzzJsonEncoderCore(input, JsonEncoderOptions.Compact);
        }

        /// <summary>
        /// Checks RawData JSON using the decoded input's type metadata.
        /// </summary>
        public static void AflfuzzJsonEncoderRawData(string input)
        {
            FuzzJsonEncoderCore(input, JsonEncoderOptions.RawData);
        }

        /// <summary>
        /// Exercises the supported numeric-enum, namespace-index and default-value
        /// behaviors of legacy Reversible JSON, using the current wire representation.
        /// </summary>
        public static void AflfuzzJsonEncoderLegacyReversible(string input)
        {
            FuzzJsonEncoderCore(input, LegacyReversibleOptions);
        }

        /// <summary>
        /// Exercises legacy NonReversible artifact suppression and URI-based identifiers.
        /// Retired text-only LocalizedText and unwrapped Variant wire forms are not reproduced.
        /// </summary>
        public static void AflfuzzJsonEncoderLegacyNonReversible(string input)
        {
            FuzzJsonEncoderCore(input, LegacyNonReversibleOptions);
        }

        /// <summary>
        /// The Json encoder idempotent fuzz target for afl-fuzz.
        /// </summary>
        public static void AflfuzzJsonEncoderIndempotent(string input)
        {
            FuzzJsonEncoderCore(input, JsonEncoderOptions.Verbose);
        }

        /// <summary>
        /// The json decoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzJsonDecoder(ReadOnlySpan<byte> input)
        {
            _ = FuzzJsonDecoderCore(DecodeUtf8Json(input));
        }

        /// <summary>
        /// The verbose JSON encoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzJsonEncoder(ReadOnlySpan<byte> input)
        {
            AflfuzzJsonEncoder(DecodeUtf8Json(input));
        }

        /// <summary>
        /// The compact Json encoder fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzJsonEncoderCompact(ReadOnlySpan<byte> input)
        {
            AflfuzzJsonEncoderCompact(DecodeUtf8Json(input));
        }

        /// <summary>
        /// Checks RawData JSON with metadata and canonical byte/value oracles.
        /// </summary>
        public static void LibfuzzJsonEncoderRawData(ReadOnlySpan<byte> input)
        {
            AflfuzzJsonEncoderRawData(DecodeUtf8Json(input));
        }

        /// <summary>
        /// Exercises the current option mapping for legacy Reversible behavior.
        /// </summary>
        public static void LibfuzzJsonEncoderLegacyReversible(ReadOnlySpan<byte> input)
        {
            AflfuzzJsonEncoderLegacyReversible(DecodeUtf8Json(input));
        }

        /// <summary>
        /// Exercises the current option mapping for legacy NonReversible behavior.
        /// </summary>
        public static void LibfuzzJsonEncoderLegacyNonReversible(ReadOnlySpan<byte> input)
        {
            AflfuzzJsonEncoderLegacyNonReversible(DecodeUtf8Json(input));
        }

        /// <summary>
        /// The Json encoder idempotent fuzz target for libfuzzer.
        /// </summary>
        public static void LibfuzzJsonEncoderIndempotent(ReadOnlySpan<byte> input)
        {
            AflfuzzJsonEncoderIndempotent(DecodeUtf8Json(input));
        }

        /// <summary>
        /// The fuzz target for the JsonDecoder.
        /// </summary>
        /// <param name="json">A string with fuzz content.</param>
        internal static IEncodeable FuzzJsonDecoderCore(string json, bool throwAll = false)
        {
            try
            {
                using var decoder = new JsonDecoder(json, MessageContext);
                return decoder.DecodeMessage<IEncodeable>();
            }
            catch (ServiceResultException sre) when (!throwAll && IsExpectedDecodingError(sre))
            {
                return null;
            }
        }

        /// <summary>
        /// The idempotent fuzz target core for the JsonEncoder.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal static void FuzzJsonEncoderIndempotentCore(
            string serialized,
            IEncodeable encodeable,
            JsonEncoderOptions options = null,
            IServiceMessageContext context = null)
        {
            if (serialized == null || encodeable == null)
            {
                return;
            }

            options ??= JsonEncoderOptions.Verbose;
            context ??= MessageContext;
            IEncodeable encodeable2 = DecodeJsonWithMetadata(serialized, encodeable, options, context);
            string serialized2 = EncodeJsonMessage(encodeable2, options, context);
            // The artifact metadata has to come from the value that produced the payload being
            // decoded. Generation one metadata goes stale as soon as generation one normalizes.
            IEncodeable encodeable3 = DecodeJsonWithMetadata(serialized2, encodeable2, options, context);

            string encodeableTypeName = encodeable2?.GetType().Name ?? "unknown type";
            bool firstGenerationNormalized = !Utils.IsEqual(encodeable, encodeable2);
            if (firstGenerationNormalized &&
                !IsExpectedJsonSemanticLoss(encodeable, encodeable2, options, context))
            {
                throw new EncodingFidelityException(
                    $"JSON semantic round-trip failed. Type={encodeableTypeName}, Mode={options.Name}.");
            }

            if (serialized2 == null || !serialized.SequenceEqual(serialized2))
            {
                if (!IsExpectedJsonEncodingNormalization(serialized, serialized2, context) &&
                    !(firstGenerationNormalized &&
                        IsStableFromSecondGeneration(serialized2, encodeable3, options, context)))
                {
                    throw new EncodingFidelityException(
                        Utils.Format("Idempotent JSON encoding failed. Type={0}.", encodeableTypeName));
                }
            }

            if (!Utils.IsEqual(encodeable2, encodeable3) &&
                !IsExpectedJsonSemanticLoss(encodeable2, encodeable3, options, context))
            {
                throw new EncodingFidelityException(Utils.Format(
                    "Idempotent JSON 3rd gen decoding failed. Type={0}.",
                    encodeableTypeName));
            }
        }

        internal static void FuzzJsonRoundTripCore(
            IEncodeable encodeable,
            JsonEncoderOptions options,
            IServiceMessageContext context = null)
        {
            context ??= MessageContext;
            try
            {
                string serialized = EncodeJsonMessage(encodeable, options, context);
                FuzzJsonEncoderIndempotentCore(serialized, encodeable, options, context);
            }
            catch (ServiceResultException exception) when (IsExpectedJsonEncodingError(exception, encodeable, context))
            {
                return;
            }
        }

        internal static string EncodeJsonMessage(
            IEncodeable encodeable,
            JsonEncoderOptions options,
            IServiceMessageContext context = null)
        {
            using var memoryStream = new MemoryStream(0x1000);
            using var encoder = new JsonEncoder(memoryStream, context ?? MessageContext, options);
            encoder.EncodeMessage(encodeable, encodeable.TypeId);
            encoder.Close();
            return Encoding.UTF8.GetString(memoryStream.ToArray());
        }

        internal static IEncodeable DecodeJsonWithMetadata(
            string serialized,
            IEncodeable encodeable,
            JsonEncoderOptions options,
            IServiceMessageContext context)
        {
            if (options.SuppressArtifacts)
            {
                string metadata = EncodeJsonMessage(encodeable, options with { SuppressArtifacts = false }, context);
                serialized = RestoreJsonArtifacts(serialized, metadata, context);
            }

            using var decoder = new JsonDecoder(serialized, context);
            NodeId typeId = decoder.ReadNodeId("UaTypeId");
            if (typeId != ExpandedNodeId.ToNodeId(encodeable.TypeId, context.NamespaceUris))
            {
                throw new EncodingFidelityException("JSON message type changed during encoding.");
            }

            try
            {
                return decoder.ReadEncodeable<IEncodeable>("UaBody", encodeable.TypeId);
            }
            catch (ServiceResultException exception)
            {
                // The payload here is the encoder's own output, not attacker controlled bytes,
                // so a decoding error is a fidelity finding rather than a robustness one: the
                // stack produced a representation it cannot read back. Classifying it here
                // keeps genuine decoder failures on real input reported as robustness bugs.
                throw new EncodingFidelityException(
                    $"Re-decoding the encoded message failed. Type={encodeable.GetType().Name}.",
                    exception);
            }
        }

        internal static string RestoreJsonArtifacts(
            string serialized,
            string metadata,
            IServiceMessageContext context)
        {
            using var encoded = new JsonDecoder(serialized, context);
            using var schema = new JsonDecoder(metadata, context);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                RestoreJsonArtifacts(encoded.Root, schema.Root, writer);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void FuzzJsonEncoderCore(string input, JsonEncoderOptions options)
        {
            IEncodeable encodeable = FuzzJsonDecoderCore(input);
            if (encodeable != null)
            {
                FuzzJsonRoundTripCore(encodeable, options);
            }
        }

        private static bool IsExpectedJsonEncodingError(
            ServiceResultException exception,
            IEncodeable encodeable,
            IServiceMessageContext context)
        {
            if (exception.StatusCode != StatusCodes.BadEncodingError)
            {
                return false;
            }

            if (exception.InnerException is JsonException)
            {
                return true;
            }

            return HasJsonUnencodableValue(
                encodeable,
                new HashSet<ReferencePair>(ReferencePairComparer.Instance),
                context);
        }

        private static bool HasUnpairedPicoseconds(in DataValue value)
        {
            return !value.IsNull &&
                ((value.SourceTimestamp == DateTimeUtc.MinValue && value.SourcePicoseconds != 0) ||
                    (value.ServerTimestamp == DateTimeUtc.MinValue && value.ServerPicoseconds != 0));
        }

        /// <summary>
        /// A first generation encoding can legitimately normalize a value that the wire form
        /// cannot represent distinctly, for example a bodyless ExtensionObject whose TypeId
        /// resolves to a registered type. The byte level idempotence invariant then starts at
        /// the second generation: re-encoding the value decoded from the second generation must
        /// reproduce that generation byte for byte, so the encoding still reaches a fixed point
        /// after a single normalizing pass instead of oscillating or growing without bound.
        /// </summary>
        private static bool IsStableFromSecondGeneration(
            string serialized2,
            IEncodeable encodeable3,
            JsonEncoderOptions options,
            IServiceMessageContext context)
        {
            if (serialized2 == null || encodeable3 == null)
            {
                return false;
            }

            string serialized3 = EncodeJsonMessage(encodeable3, options, context);
            return StringComparer.Ordinal.Equals(serialized2, serialized3);
        }

        private static bool IsExpectedJsonSemanticLoss(
            IEncodeable original,
            IEncodeable decoded,
            JsonEncoderOptions options,
            IServiceMessageContext context)
        {
            return IsJsonEquivalent(
                original,
                decoded,
                new HashSet<ReferencePair>(ReferencePairComparer.Instance),
                options,
                context);
        }

        private static bool HasJsonUnencodableValue(
            object value,
            HashSet<ReferencePair> seen,
            IServiceMessageContext context)
        {
            if (value == null)
            {
                return false;
            }

            if (value is QualifiedName qualifiedName)
            {
                return (qualifiedName.NamespaceIndex != 0 && qualifiedName.Name == null) ||
                    !SurvivesQualifiedNameTextForm(qualifiedName, context);
            }

            if (value is DataValue dataValue)
            {
                return HasUnpairedPicoseconds(in dataValue) ||
                    HasJsonUnencodableValue(dataValue.WrappedValue, seen, context);
            }

            if (value is Variant variant)
            {
                return HasJsonUnencodableValue(GetVariantRaw(in variant), seen, context);
            }

            if (value is ExtensionObject extensionObject)
            {
                return IsJsonUnencodableExtensionObjectTypeId(in extensionObject, context) ||
                    (extensionObject.TryGetValue(out IEncodeable encodeable) &&
                        HasJsonUnencodableValue(encodeable, seen, context));
            }

            Type type = value.GetType();
            if (IsJsonSimpleType(type))
            {
                return false;
            }

            if (IsArrayOf(type))
            {
                return HasJsonUnencodableArrayOf(value, type, seen, context);
            }

            if (IsMatrixOf(type))
            {
                return HasJsonUnencodableValue(GetMatrixAsArrayOf(value, type), seen, context);
            }

            if (!type.IsValueType && !seen.Add(new ReferencePair(value, value)))
            {
                return false;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    if (HasJsonUnencodableValue(item, seen, context))
                    {
                        return true;
                    }
                }

                return false;
            }

            foreach (PropertyInfo property in GetComparableProperties(type))
            {
                if (HasJsonUnencodableValue(property.GetValue(value), seen, context))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The textual QualifiedName form is "&lt;namespace index&gt;:&lt;name&gt;", which is
        /// only unambiguous while the name itself carries no separator and parses back. A name
        /// that does not survive that form cannot be represented in the JSON string form at
        /// all, so the value is unencodable rather than lost by the decoder. Deciding this by
        /// actually formatting and re-parsing keeps the rule tied to the real encoder output
        /// instead of a hand maintained list of forbidden characters.
        /// </summary>
        private static bool SurvivesQualifiedNameTextForm(
            QualifiedName value,
            IServiceMessageContext context)
        {
            try
            {
                return QualifiedName.Parse(context, value.Format(context), false).Equals(value);
            }
            catch (ServiceResultException)
            {
                return false;
            }
        }

        private static bool IsJsonEquivalent(
            object left,
            object right,
            HashSet<ReferencePair> seen,
            JsonEncoderOptions options,
            IServiceMessageContext context)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            Type type = left.GetType();
            if (type != right.GetType())
            {
                return false;
            }

            if (left is Variant leftVariant && right is Variant rightVariant)
            {
                return IsJsonEquivalentVariant(in leftVariant, in rightVariant, seen, options, context);
            }

            if (left is DataValue leftDataValue && right is DataValue rightDataValue)
            {
                return IsJsonEquivalentDataValue(in leftDataValue, in rightDataValue, seen, options, context);
            }

            if (left is ExtensionObject leftExtensionObject &&
                right is ExtensionObject rightExtensionObject)
            {
                return IsJsonEquivalentExtensionObject(
                    in leftExtensionObject,
                    in rightExtensionObject,
                    seen,
                    options,
                    context);
            }

            if (left is QualifiedName leftQualifiedName &&
                right is QualifiedName rightQualifiedName)
            {
                return IsJsonEquivalentQualifiedName(leftQualifiedName, rightQualifiedName);
            }

            if (left is ExpandedNodeId leftExpandedNodeId &&
                right is ExpandedNodeId rightExpandedNodeId)
            {
                return AreJsonEquivalentExpandedNodeIds(leftExpandedNodeId, rightExpandedNodeId, context);
            }

            if (IsArrayOf(type))
            {
                return IsJsonEquivalentArrayOf(left, right, type, seen, options, context);
            }

            if (IsMatrixOf(type))
            {
                return IsJsonEquivalentMatrixOf(left, right, type, seen, options, context);
            }

            if (IsJsonSimpleType(type))
            {
                return left.Equals(right);
            }

            if (!type.IsValueType && !seen.Add(new ReferencePair(left, right)))
            {
                return true;
            }

            if (left is IEnumerable leftEnumerable && right is IEnumerable rightEnumerable)
            {
                return IsJsonEquivalentEnumerable(leftEnumerable, rightEnumerable, seen, options, context);
            }

            if (left is IEncodeable leftEncodeable &&
                right is IEncodeable rightEncodeable &&
                leftEncodeable.IsEqual(rightEncodeable))
            {
                return true;
            }

            bool comparedProperty = false;
            foreach (PropertyInfo property in GetComparableProperties(type))
            {
                comparedProperty = true;
                if (!IsJsonEquivalent(
                    property.GetValue(left),
                    property.GetValue(right),
                    seen,
                    options,
                    context))
                {
                    return false;
                }
            }

            return comparedProperty || left.Equals(right);
        }

        private static bool IsJsonEquivalentVariant(
            in Variant left,
            in Variant right,
            HashSet<ReferencePair> seen,
            JsonEncoderOptions options,
            IServiceMessageContext context)
        {
            return left.TypeInfo == right.TypeInfo &&
                IsJsonEquivalent(GetVariantRaw(in left), GetVariantRaw(in right), seen, options, context);
        }

        private static bool IsJsonEquivalentDataValue(
            in DataValue left,
            in DataValue right,
            HashSet<ReferencePair> seen,
            JsonEncoderOptions options,
            IServiceMessageContext context)
        {
            return left.IsNull == right.IsNull &&
                left.StatusCode.Equals(right.StatusCode, StatusCodeComparison.AllBits) &&
                left.SourceTimestamp == right.SourceTimestamp &&
                left.ServerTimestamp == right.ServerTimestamp &&
                left.SourcePicoseconds == right.SourcePicoseconds &&
                left.ServerPicoseconds == right.ServerPicoseconds &&
                IsJsonEquivalent(left.WrappedValue, right.WrappedValue, seen, options, context);
        }

        private static bool IsJsonEquivalentExtensionObject(
            in ExtensionObject left,
            in ExtensionObject right,
            HashSet<ReferencePair> seen,
            JsonEncoderOptions options,
            IServiceMessageContext context)
        {
            if (left.Equals(right))
            {
                return true;
            }

            if (left.Encoding == ExtensionObjectEncoding.None &&
                right.Encoding == ExtensionObjectEncoding.None)
            {
                return AreJsonEquivalentExpandedNodeIds(left.TypeId, right.TypeId, context);
            }

            if (IsBodylessExtensionObjectMaterialization(in left, in right, context))
            {
                return true;
            }

            if (left.TryGetAsJson(out string leftJson) &&
                right.TryGetAsJson(out string rightJson))
            {
                try
                {
                    var normalizedLeft = new ExtensionObject(left.TypeId, CanonicalizeRawJson(leftJson));
                    var normalizedRight = new ExtensionObject(right.TypeId, CanonicalizeRawJson(rightJson));
                    return normalizedLeft.Equals(normalizedRight);
                }
                catch (JsonException)
                {
                    return false;
                }
            }

            return left.TryGetValue(out IEncodeable leftEncodeable) &&
                right.TryGetValue(out IEncodeable rightEncodeable) &&
                IsJsonEquivalent(leftEncodeable, rightEncodeable, seen, options, context);
        }

        /// <summary>
        /// The legacy inline JSON form writes an encodeable body directly into the
        /// ExtensionObject envelope, so a bodyless ExtensionObject and a default constructed
        /// instance of the same type serialize to the exact same JSON. Decoding therefore
        /// materializes a default instance whenever the TypeId resolves to a registered type.
        /// That is inherent to the encoding rather than a decoder defect - the decoder only
        /// keeps the envelope bodyless when the type is unknown and no instance can be built.
        /// Accept the materialization, but only when every member of the decoded instance is
        /// still at its default; any populated member means information appeared from nowhere.
        /// </summary>
        private static bool IsBodylessExtensionObjectMaterialization(
            in ExtensionObject left,
            in ExtensionObject right,
            IServiceMessageContext context)
        {
            if (left.Encoding != ExtensionObjectEncoding.None ||
                left.TypeId.IsNull ||
                left.TryGetValue(out IEncodeable _) ||
                !right.TryGetValue(out IEncodeable decoded) ||
                decoded == null ||
                !AreJsonEquivalentExpandedNodeIds(left.TypeId, right.TypeId, context))
            {
                return false;
            }

            IEncodeable prototype;
            try
            {
                prototype = Activator.CreateInstance(decoded.GetType()) as IEncodeable;
            }
            catch (MissingMethodException)
            {
                return false;
            }

            return prototype != null && Utils.IsEqual(prototype, decoded);
        }

        private static bool IsJsonEquivalentQualifiedName(QualifiedName left, QualifiedName right)
        {
            return left.Equals(right) || (left.IsNull && right.IsNull);
        }

        private static bool AreJsonEquivalentExpandedNodeIds(
            ExpandedNodeId left,
            ExpandedNodeId right,
            IServiceMessageContext context)
        {
            if (left.IsNull || right.IsNull)
            {
                return left.IsNull == right.IsNull;
            }

            if (left == right)
            {
                return true;
            }

            NodeId leftLocal = ExpandedNodeId.ToNodeId(left, context.NamespaceUris);
            NodeId rightLocal = ExpandedNodeId.ToNodeId(right, context.NamespaceUris);
            return !leftLocal.IsNull &&
                !rightLocal.IsNull &&
                leftLocal == rightLocal;
        }

        private static bool IsExpectedJsonEncodingNormalization(
            string serialized,
            string serialized2,
            IServiceMessageContext context)
        {
            try
            {
                string canonical = CanonicalizeRawJson(serialized);
                if (!StringComparer.Ordinal.Equals(serialized, canonical))
                {
                    return false;
                }

                using JsonDocument left = JsonDocument.Parse(serialized);
                using JsonDocument right = JsonDocument.Parse(serialized2);
                return IsJsonEquivalentText(left.RootElement, right.RootElement, context);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool IsJsonEquivalentText(
            JsonElement left,
            JsonElement right,
            IServiceMessageContext context)
        {
            if (left.ValueKind != right.ValueKind)
            {
                return false;
            }

            switch (left.ValueKind)
            {
                case JsonValueKind.Object:
                    return IsJsonEquivalentObjectText(left, right, context);
                case JsonValueKind.Array:
                    return IsJsonEquivalentArrayText(left, right, context);
                case JsonValueKind.String:
                    string leftString = left.GetString();
                    string rightString = right.GetString();
                    return StringComparer.Ordinal.Equals(leftString, rightString) ||
                        AreJsonEquivalentIdentifierStrings(leftString, rightString, context);
                default:
                    return StringComparer.Ordinal.Equals(left.GetRawText(), right.GetRawText());
            }
        }

        private static bool IsJsonEquivalentObjectText(
            JsonElement left,
            JsonElement right,
            IServiceMessageContext context)
        {
            using var leftProperties = left.EnumerateObject();
            using var rightProperties = right.EnumerateObject();
            while (true)
            {
                bool leftHasValue = leftProperties.MoveNext();
                bool rightHasValue = rightProperties.MoveNext();
                if (leftHasValue != rightHasValue)
                {
                    return false;
                }
                if (!leftHasValue)
                {
                    return true;
                }
                if (!StringComparer.Ordinal.Equals(leftProperties.Current.Name, rightProperties.Current.Name) ||
                    !IsJsonEquivalentText(leftProperties.Current.Value, rightProperties.Current.Value, context))
                {
                    return false;
                }
            }
        }

        private static bool IsJsonEquivalentArrayText(
            JsonElement left,
            JsonElement right,
            IServiceMessageContext context)
        {
            if (left.GetArrayLength() != right.GetArrayLength())
            {
                return false;
            }

            for (int i = 0; i < left.GetArrayLength(); i++)
            {
                if (!IsJsonEquivalentText(left[i], right[i], context))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AreJsonEquivalentIdentifierStrings(
            string left,
            string right,
            IServiceMessageContext context)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return TryParseExpandedNodeId(left, context, out ExpandedNodeId leftId) &&
                TryParseExpandedNodeId(right, context, out ExpandedNodeId rightId) &&
                AreJsonEquivalentExpandedNodeIds(leftId, rightId, context);
        }

        private static bool TryParseExpandedNodeId(
            string text,
            IServiceMessageContext context,
            out ExpandedNodeId value)
        {
            try
            {
                value = ExpandedNodeId.Parse(text, context.NamespaceUris);
                return true;
            }
            catch (ServiceResultException)
            {
                value = ExpandedNodeId.Null;
                return false;
            }
            catch (FormatException)
            {
                value = ExpandedNodeId.Null;
                return false;
            }
        }

        private static bool HasJsonUnencodableArrayOf(
            object value,
            Type type,
            HashSet<ReferencePair> seen,
            IServiceMessageContext context)
        {
            Array array = GetArrayOfElements(value, type);
            foreach (object item in array)
            {
                if (HasJsonUnencodableValue(item, seen, context))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsJsonUnencodableExtensionObjectTypeId(
            in ExtensionObject value,
            IServiceMessageContext context)
        {
            return !value.TypeId.IsNull &&
                ExpandedNodeId.ToNodeId(value.TypeId, context.NamespaceUris).IsNull;
        }

        private static bool IsJsonEquivalentArrayOf(
            object left,
            object right,
            Type type,
            HashSet<ReferencePair> seen,
            JsonEncoderOptions options,
            IServiceMessageContext context)
        {
            bool leftIsNull = (bool)type.GetProperty(nameof(INullable.IsNull))!.GetValue(left)!;
            bool rightIsNull = (bool)type.GetProperty(nameof(INullable.IsNull))!.GetValue(right)!;
            int leftCount = (int)type.GetProperty(nameof(ArrayOf<int>.Count))!.GetValue(left)!;
            int rightCount = (int)type.GetProperty(nameof(ArrayOf<int>.Count))!.GetValue(right)!;

            if (options.IgnoreNullValues && leftCount == 0 && rightCount == 0)
            {
                return true;
            }

            return leftIsNull == rightIsNull &&
                leftCount == rightCount &&
                IsJsonEquivalentEnumerable(
                    GetArrayOfElements(left, type),
                    GetArrayOfElements(right, type),
                    seen,
                    options,
                    context);
        }

        private static bool IsJsonEquivalentMatrixOf(
            object left,
            object right,
            Type type,
            HashSet<ReferencePair> seen,
            JsonEncoderOptions options,
            IServiceMessageContext context)
        {
            bool leftIsNull = (bool)type.GetProperty(nameof(INullable.IsNull))!.GetValue(left)!;
            bool rightIsNull = (bool)type.GetProperty(nameof(INullable.IsNull))!.GetValue(right)!;
            int[] leftDimensions = (int[])type.GetProperty(nameof(MatrixOf<int>.Dimensions))!.GetValue(left)!;
            int[] rightDimensions = (int[])type.GetProperty(nameof(MatrixOf<int>.Dimensions))!.GetValue(right)!;

            return leftIsNull == rightIsNull &&
                leftDimensions.SequenceEqual(rightDimensions) &&
                IsJsonEquivalent(
                    GetMatrixAsArrayOf(left, type),
                    GetMatrixAsArrayOf(right, type),
                    seen,
                    options,
                    context);
        }

        private static Array GetArrayOfElements(object value, Type type)
        {
            object array = type.GetMethod(nameof(ArrayOf<int>.ToArray))!.Invoke(value, null)!;
            return array == null ? Array.Empty<object>() : (Array)array;
        }

        private static object GetMatrixAsArrayOf(object value, Type type)
        {
            return type.GetMethod(nameof(MatrixOf<int>.ToArrayOf), Type.EmptyTypes)!.Invoke(value, null)!;
        }

        private static bool IsJsonEquivalentEnumerable(
            IEnumerable left,
            IEnumerable right,
            HashSet<ReferencePair> seen,
            JsonEncoderOptions options,
            IServiceMessageContext context)
        {
            IEnumerator leftEnumerator = left.GetEnumerator();
            IEnumerator rightEnumerator = right.GetEnumerator();
            try
            {
                while (true)
                {
                    bool leftHasValue = leftEnumerator.MoveNext();
                    bool rightHasValue = rightEnumerator.MoveNext();
                    if (leftHasValue != rightHasValue)
                    {
                        return false;
                    }
                    if (!leftHasValue)
                    {
                        return true;
                    }
                    if (!IsJsonEquivalent(
                        leftEnumerator.Current,
                        rightEnumerator.Current,
                        seen,
                        options,
                        context))
                    {
                        return false;
                    }
                }
            }
            finally
            {
                (leftEnumerator as IDisposable)?.Dispose();
                (rightEnumerator as IDisposable)?.Dispose();
            }
        }

        private static object GetVariantRaw(in Variant value)
        {
            PropertyInfo property = typeof(Variant).GetProperty(
                "Raw",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Variant.Raw property was not found.");
            return property.GetValue(value)!;
        }

        private static IEnumerable<PropertyInfo> GetComparableProperties(Type type)
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.CanRead &&
                    property.GetIndexParameters().Length == 0 &&
                    property.Name != nameof(ArrayOf<int>.Span) &&
                    property.Name != nameof(ReadValueId.Handle))
                {
                    yield return property;
                }
            }
        }

        private static bool IsArrayOf(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ArrayOf<>);
        }

        private static bool IsMatrixOf(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(MatrixOf<>);
        }

        private static bool IsJsonSimpleType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type.IsPrimitive ||
                type.IsEnum ||
                type == typeof(string) ||
                type == typeof(decimal) ||
                type == typeof(DateTime) ||
                type == typeof(DateTimeOffset) ||
                type == typeof(DateTimeUtc) ||
                type == typeof(NodeId) ||
                type == typeof(ExpandedNodeId) ||
                type == typeof(QualifiedName) ||
                type == typeof(LocalizedText) ||
                type == typeof(StatusCode) ||
                type == typeof(ByteString) ||
                type == typeof(Uuid) ||
                type.Namespace == "System.Xml";
        }

        private static string CanonicalizeRawJson(string rawJson)
        {
            using JsonDocument document = JsonDocument.Parse(rawJson);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                document.RootElement.WriteTo(writer);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private readonly struct ReferencePair
        {
            public ReferencePair(object left, object right)
            {
                Left = left;
                Right = right;
            }

            public object Left { get; }

            public object Right { get; }
        }

        private sealed class ReferencePairComparer : IEqualityComparer<ReferencePair>
        {
            public static ReferencePairComparer Instance { get; } = new();

            public bool Equals(ReferencePair x, ReferencePair y)
            {
                return ReferenceEquals(x.Left, y.Left) &&
                    ReferenceEquals(x.Right, y.Right);
            }

            public int GetHashCode(ReferencePair obj)
            {
                return RuntimeHelpers.GetHashCode(obj.Left) ^
                    RuntimeHelpers.GetHashCode(obj.Right);
            }
        }

        private static string DecodeUtf8Json(ReadOnlySpan<byte> input)
        {
#if NETFRAMEWORK
            return Encoding.UTF8.GetString(input.ToArray());
#else
            return Encoding.UTF8.GetString(input);
#endif
        }

        private static void RestoreJsonArtifacts(JsonElement encoded, JsonElement metadata, Utf8JsonWriter writer)
        {
            if (encoded.ValueKind != metadata.ValueKind)
            {
                throw new EncodingFidelityException("RawData JSON changed the payload shape.");
            }

            switch (encoded.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    bool metadataOmitsArtifactFields =
                        metadata.TryGetProperty("SwitchField", out _) ||
                        metadata.TryGetProperty("EncodingMask", out _);
                    foreach (JsonProperty property in encoded.EnumerateObject())
                    {
                        if (!metadata.TryGetProperty(property.Name, out JsonElement fieldMetadata))
                        {
                            // Disabling SuppressArtifacts also re-enables the union SwitchField
                            // and the optional-field EncodingMask, and a union or optional
                            // structure encodes a different member set under the two option
                            // sets. Only then may the payload carry a field the metadata lacks;
                            // that is an encoder-shape difference, not a corrupted payload.
                            // Anywhere else an unknown field is still a real corruption.
                            if (!metadataOmitsArtifactFields)
                            {
                                throw new EncodingFidelityException(
                                    $"Unexpected RawData JSON field '{property.Name}'.");
                            }
                            property.WriteTo(writer);
                            continue;
                        }
                        writer.WritePropertyName(property.Name);
                        RestoreJsonArtifacts(property.Value, fieldMetadata, writer);
                    }
                    foreach (JsonProperty property in metadata.EnumerateObject())
                    {
                        if (!encoded.TryGetProperty(property.Name, out _))
                        {
                            // Only non-semantic artifacts come from metadata; never replace or repair payload values.
                            if (property.Name is not ("UaType" or "UaTypeId" or "Symbol" or
                                "SwitchField" or "EncodingMask"))
                            {
                                throw new EncodingFidelityException($"RawData JSON lost field '{property.Name}'.");
                            }
                            if (property.Name is "UaType" or "UaTypeId" or "Symbol")
                            {
                                property.WriteTo(writer);
                            }
                        }
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    if (encoded.GetArrayLength() != metadata.GetArrayLength())
                    {
                        throw new EncodingFidelityException("RawData JSON changed an array length.");
                    }
                    writer.WriteStartArray();
                    for (int i = 0; i < encoded.GetArrayLength(); i++)
                    {
                        RestoreJsonArtifacts(encoded[i], metadata[i], writer);
                    }
                    writer.WriteEndArray();
                    break;
                default:
                    encoded.WriteTo(writer);
                    break;
            }
        }
    }
}
