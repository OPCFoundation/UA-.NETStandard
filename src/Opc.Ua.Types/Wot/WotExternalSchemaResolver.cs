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
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// What comparing an <c>uav:externalSchema</c> against the canonical
    /// DataSchema of the affordance that names it established.
    /// </summary>
    public enum WotExternalSchemaOutcome
    {
        /// <summary>
        /// The reference was not evaluated: no provider was configured, so
        /// nothing was consulted and nothing was fetched.
        /// </summary>
        NotEvaluated,

        /// <summary>
        /// Every provider was consulted and none holds the reference, or the
        /// one that does answered with a media type this Binding does not read.
        /// </summary>
        Unresolved,

        /// <summary>
        /// The external schema describes the same data as the canonical
        /// DataSchema.
        /// </summary>
        Compatible,

        /// <summary>
        /// The external schema and the canonical DataSchema describe different
        /// data. The canonical one still decides the DataType; the
        /// disagreement is reported, not applied.
        /// </summary>
        Incompatible,

        /// <summary>
        /// More than one provider holds the reference and they answer with
        /// different bytes.
        /// </summary>
        Ambiguous
    }

    /// <summary>
    /// The result of resolving and comparing one <c>uav:externalSchema</c>
    /// reference.
    /// </summary>
    public sealed record WotExternalSchemaResult
    {
        /// <summary>
        /// Gets the reference the affordance named.
        /// </summary>
        public required string Reference { get; init; }

        /// <summary>
        /// Gets the outcome.
        /// </summary>
        public required WotExternalSchemaOutcome Outcome { get; init; }

        /// <summary>
        /// Gets the zero-based index of the provider whose answer was used, or
        /// <c>-1</c> where no provider answered.
        /// </summary>
        public int ProviderIndex { get; init; } = -1;

        /// <summary>
        /// Gets the media type the answering provider reported, or an empty
        /// string.
        /// </summary>
        public string ContentType { get; init; } = string.Empty;

        /// <summary>
        /// Gets the human-readable reason for an outcome other than
        /// <see cref="WotExternalSchemaOutcome.Compatible"/>.
        /// </summary>
        public string? Detail { get; init; }

        /// <summary>
        /// Gets the reason to report: what the resolver said, or - where it
        /// said nothing - what the outcome itself means.
        /// </summary>
        /// <remarks>
        /// Agreement has nothing to explain, so a <c>Compatible</c> result
        /// carries no <see cref="Detail"/> and reads its sentence from the
        /// outcome. The fallback covers the other outcomes too, because
        /// <see cref="WotExternalSchemaResult"/> is public: a caller that
        /// builds one is not obliged to write the sentence itself, and a
        /// diagnostic with no message would be worse than a general one.
        /// </remarks>
        public string Reason => Detail ?? DescribeOutcome(Outcome, Reference);

        private static string DescribeOutcome(
            WotExternalSchemaOutcome outcome, string reference)
        {
            return outcome switch
            {
                WotExternalSchemaOutcome.NotEvaluated =>
                    $"The external schema '{reference}' was neither fetched nor evaluated.",
                WotExternalSchemaOutcome.Compatible =>
                    $"The external schema '{reference}' agrees with the canonical " +
                    "DataSchema; the DataType the Binding derives is unchanged.",
                WotExternalSchemaOutcome.Incompatible =>
                    $"The external schema '{reference}' describes different data.",
                WotExternalSchemaOutcome.Ambiguous =>
                    $"More than one provider holds '{reference}'.",
                _ => $"The external schema '{reference}' could not be resolved."
            };
        }
    }

    /// <summary>
    /// Resolves <c>uav:externalSchema</c> references through an ordered set of
    /// providers and compares what they return against the canonical
    /// DataSchema.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The library performs no network or file I/O of its own: a provider
    /// supplies the transport, so a converter configured with no provider
    /// resolves nothing and fetches nothing. That is deliberate - an external
    /// schema reference is an arbitrary IRI in a document a consumer did not
    /// write, and following one by default would make reading a document a
    /// request to whatever the document names.
    /// </para>
    /// <para>
    /// Providers are consulted in order and the first that holds the reference
    /// settles it, which is the same first-source precedence the local context
    /// of Section 5.1.5 follows. The remaining providers are still consulted so
    /// that two providers holding <em>different</em> bytes for one reference is
    /// reported rather than silently resolved by ordering alone; a federation
    /// whose members disagree about a schema is a fact its operator needs.
    /// </para>
    /// <para>
    /// The comparison never changes anything. WoT Binding Section 6.11 makes a
    /// DataType definition and Section 5.4's definitive terms the statement of
    /// what a Variable is; an external schema is a second description of the
    /// same data, so it can agree or disagree but it cannot redefine.
    /// </para>
    /// </remarks>
    public sealed class WotExternalSchemaResolver
    {
        /// <summary>
        /// Initializes a resolver over the supplied providers, in order.
        /// </summary>
        /// <param name="providers">
        /// The providers, most authoritative first. An empty set is valid and
        /// means no reference is ever resolved.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="providers"/> is <c>null</c>.
        /// </exception>
        public WotExternalSchemaResolver(params IWotSchemaResolver[] providers)
        {
            m_providers = providers ?? throw new ArgumentNullException(nameof(providers));
        }

        /// <summary>
        /// Gets the number of configured providers.
        /// </summary>
        public int ProviderCount => m_providers.Length;

        /// <summary>
        /// The media types an external DataSchema may be delivered as. A
        /// provider that answers with anything else is answering with something
        /// this Binding cannot read as a DataSchema, which is not the same as
        /// not holding it.
        /// </summary>
        public static ArrayOf<string> ReadableContentTypes { get; } =
            new ArrayOf<string>(
            [
                "application/json",
                "application/schema+json",
                "application/ld+json",
                "application/td+json",
                "application/tm+json"
            ]);

        /// <summary>
        /// Resolves one reference and compares it against the canonical
        /// DataSchema of the affordance that named it.
        /// </summary>
        /// <param name="reference">The <c>uav:externalSchema</c> value.</param>
        /// <param name="canonical">The affordance's own DataSchema.</param>
        /// <param name="canonicalDataType">
        /// The DataType the Binding derived for the affordance, as a portable
        /// ExpandedNodeId string. It is what the external schema is checked
        /// against and is never replaced by it.
        /// </param>
        /// <param name="context">
        /// The conversion's resolution context, so an external schema counts
        /// against the same depth, document, cycle and byte bounds every other
        /// resolved document does.
        /// </param>
        /// <param name="options">The converter options.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The outcome.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="reference"/> or <paramref name="context"/> is
        /// <c>null</c>.
        /// </exception>
        public async ValueTask<WotExternalSchemaResult> ResolveAndCompareAsync(
            string reference,
            JsonElement canonical,
            string canonicalDataType,
            WotResolutionContext context,
            WotNodeSetConverterOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (reference is null)
            {
                throw new ArgumentNullException(nameof(reference));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (m_providers.Length == 0)
            {
                return new WotExternalSchemaResult
                {
                    Reference = reference,
                    Outcome = WotExternalSchemaOutcome.NotEvaluated,
                    Detail =
                        "No external schema provider is configured, so the reference was " +
                        "neither fetched nor evaluated."
                };
            }
            if (!IsAcceptableReference(reference))
            {
                return new WotExternalSchemaResult
                {
                    Reference = reference,
                    Outcome = WotExternalSchemaOutcome.Unresolved,
                    Detail =
                        $"The reference '{reference}' is not an absolute IRI or a relative " +
                        "path, so no provider can be asked for it."
                };
            }
            if (!context.TryEnter(WotResolutionKind.Schema, reference, out WotDiagnostic? blocked))
            {
                return new WotExternalSchemaResult
                {
                    Reference = reference,
                    Outcome = WotExternalSchemaOutcome.Unresolved,
                    Detail = blocked!.Message
                };
            }
            try
            {
                return await ResolveBoundedAsync(
                    reference, canonical, canonicalDataType, context, options, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                context.Leave(reference);
            }
        }

        private async ValueTask<WotExternalSchemaResult> ResolveBoundedAsync(
            string reference,
            JsonElement canonical,
            string canonicalDataType,
            WotResolutionContext context,
            WotNodeSetConverterOptions? options,
            CancellationToken cancellationToken)
        {
            byte[]? accepted = null;
            int acceptedIndex = -1;
            string acceptedContentType = string.Empty;
            string? mediaTypeDetail = null;
            bool ambiguous = false;
            for (int ii = 0; ii < m_providers.Length; ii++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WotResolverResult answer = await m_providers[ii]
                    .ResolveSchemaAsync(reference, context, cancellationToken)
                    .ConfigureAwait(false);
                if (!answer.Found)
                {
                    continue;
                }
                if (!IsReadableContentType(answer.ContentType))
                {
                    mediaTypeDetail ??=
                        $"Provider {ii.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                        $"answered '{reference}' with media type '{answer.ContentType}', which " +
                        "this Binding does not read as a DataSchema.";
                    continue;
                }
                byte[] bytes = answer.Content.ToArray();
                if (!context.TryAddBytes(reference, bytes.Length, out WotDiagnostic? blocked))
                {
                    return new WotExternalSchemaResult
                    {
                        Reference = reference,
                        Outcome = WotExternalSchemaOutcome.Unresolved,
                        ProviderIndex = ii,
                        Detail = blocked!.Message
                    };
                }
                if (accepted is null)
                {
                    accepted = bytes;
                    acceptedIndex = ii;
                    acceptedContentType = answer.ContentType ?? string.Empty;
                    continue;
                }
                if (!SameBytes(accepted, bytes))
                {
                    ambiguous = true;
                }
            }

            if (accepted is null)
            {
                return new WotExternalSchemaResult
                {
                    Reference = reference,
                    Outcome = WotExternalSchemaOutcome.Unresolved,
                    Detail = mediaTypeDetail ??
                        $"No configured provider holds the external schema '{reference}'."
                };
            }
            if (ambiguous)
            {
                return new WotExternalSchemaResult
                {
                    Reference = reference,
                    Outcome = WotExternalSchemaOutcome.Ambiguous,
                    ProviderIndex = acceptedIndex,
                    ContentType = acceptedContentType,
                    Detail =
                        $"More than one provider holds '{reference}' and they answer with " +
                        "different bytes. Provider order settles which one is read."
                };
            }

            return Compare(
                reference, accepted, acceptedIndex, acceptedContentType,
                canonical, canonicalDataType, options);
        }

        /// <summary>
        /// Compares resolved external schema bytes against the canonical
        /// DataSchema.
        /// </summary>
        private static WotExternalSchemaResult Compare(
            string reference,
            byte[] bytes,
            int providerIndex,
            string contentType,
            JsonElement canonical,
            string canonicalDataType,
            WotNodeSetConverterOptions? options)
        {
            JsonDocument parsed;
            try
            {
                parsed = JsonDocument.Parse(
                    bytes,
                    new JsonDocumentOptions
                    {
                        MaxDepth = options?.MaxJsonDepth ?? 128
                    });
            }
            catch (JsonException ex)
            {
                return new WotExternalSchemaResult
                {
                    Reference = reference,
                    Outcome = WotExternalSchemaOutcome.Unresolved,
                    ProviderIndex = providerIndex,
                    ContentType = contentType,
                    Detail = $"The external schema '{reference}' is not JSON: {ex.Message}"
                };
            }
            using (parsed)
            {
                string? incompatibility = FindIncompatibility(
                    parsed.RootElement, canonical, canonicalDataType, options?.MaxJsonDepth ?? 128);
                return new WotExternalSchemaResult
                {
                    Reference = reference,
                    Outcome = incompatibility is null
                        ? WotExternalSchemaOutcome.Compatible
                        : WotExternalSchemaOutcome.Incompatible,
                    ProviderIndex = providerIndex,
                    ContentType = contentType,
                    Detail = incompatibility
                };
            }
        }

        /// <summary>
        /// Names the first way the external schema and the canonical DataSchema
        /// describe different data, or <c>null</c> when they agree.
        /// </summary>
        /// <remarks>
        /// The comparison is deliberately about what the two say, not about
        /// what they omit: an external schema that says less than the canonical
        /// one still describes the same data, while one that says something
        /// different describes other data. The definitive DataType terms of
        /// Sections 5.4 and 6.11 are compared first, because they are the only
        /// statements that could otherwise be read as redefining the Variable.
        /// </remarks>
        internal static string? FindIncompatibility(
            JsonElement external,
            JsonElement canonical,
            string canonicalDataType,
            int maxDepth = 128)
        {
            return FindIncompatibility(external, canonical, canonicalDataType, out _, maxDepth);
        }

        internal static string? FindIncompatibility(
            JsonElement external,
            JsonElement canonical,
            string canonicalDataType,
            out string mismatchPointer,
            int maxDepth = 128)
        {
            string? error = ValidateSchemaShape(external, maxDepth, out mismatchPointer);
            if (error is not null)
            {
                return $"At '{mismatchPointer}': the external schema {error}";
            }
            error = ValidateSchemaShape(canonical, maxDepth, out mismatchPointer);
            if (error is not null)
            {
                return $"At '{mismatchPointer}': the canonical schema {error}";
            }
            return CompareSchema(
                external, canonical, canonicalDataType, string.Empty, 0, maxDepth, out mismatchPointer);
        }

        private static string? ValidateSchemaShape(JsonElement root, int maxDepth, out string pointer)
        {
            var pending = new Stack<(JsonElement Schema, string Pointer, int Depth)>();
            pending.Push((root, string.Empty, 0));
            pointer = string.Empty;
            while (pending.Count > 0)
            {
                (JsonElement schema, string path, int depth) = pending.Pop();
                pointer = path;
                if (depth > maxDepth)
                {
                    return $"exceeds the semantic depth limit of {maxDepth}.";
                }
                if (depth > 0 && schema.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    continue;
                }
                if (schema.ValueKind != JsonValueKind.Object)
                {
                    return "is not a JSON object, so it is not a DataSchema.";
                }
                if (schema.TryGetProperty("$ref", out _))
                {
                    pointer = path + "/$ref";
                    return "contains an unexpanded schema reference and cannot establish semantic compatibility.";
                }
                if (schema.TryGetProperty("type", out JsonElement type) && !SameSchemaTypes(type, type))
                {
                    pointer = path + "/type";
                    return "has an invalid type term.";
                }
                foreach (string term in s_numericTerms)
                {
                    if (schema.TryGetProperty(term, out JsonElement value) &&
                        (value.ValueKind != JsonValueKind.Number ||
                            (term == "multipleOf" && !IsPositiveSchemaNumber(value))))
                    {
                        pointer = path + "/" + term;
                        return $"has an invalid numeric {term} facet.";
                    }
                }
                foreach (string term in s_countTerms)
                {
                    if (schema.TryGetProperty(term, out JsonElement value) && !IsNonNegativeSchemaInteger(value))
                    {
                        pointer = path + "/" + term;
                        return $"has a {term} facet that is not a non-negative integer.";
                    }
                }
                foreach (string term in s_stringTerms)
                {
                    if (schema.TryGetProperty(term, out JsonElement value) && value.ValueKind != JsonValueKind.String)
                    {
                        pointer = path + "/" + term;
                        return $"has a {term} facet that is not a string.";
                    }
                }
                if (schema.TryGetProperty("uniqueItems", out JsonElement unique) &&
                    unique.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    pointer = path + "/uniqueItems";
                    return "has a uniqueItems facet that is not Boolean.";
                }
                if (schema.TryGetProperty("uav:valueRank", out JsonElement rank) &&
                    (rank.ValueKind != JsonValueKind.Number || !rank.TryGetInt32(out int valueRank) || valueRank < -3))
                {
                    pointer = path + "/uav:valueRank";
                    return "has an invalid ValueRank.";
                }
                if (schema.TryGetProperty("uav:arrayDimensions", out JsonElement dimensions))
                {
                    pointer = path + "/uav:arrayDimensions";
                    if (dimensions.ValueKind != JsonValueKind.Array)
                    {
                        return "has arrayDimensions that is not an array.";
                    }
                    foreach (JsonElement dimension in dimensions.EnumerateArray())
                    {
                        if (dimension.ValueKind != JsonValueKind.Number || !dimension.TryGetUInt32(out _))
                        {
                            return "has an array dimension that is not a UInt32.";
                        }
                    }
                }
                HashSet<string>? names = null;
                if (schema.TryGetProperty("properties", out JsonElement properties))
                {
                    pointer = path + "/properties";
                    if (properties.ValueKind != JsonValueKind.Object)
                    {
                        return "has a properties member that is not an object.";
                    }
                    names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JsonProperty member in properties.EnumerateObject())
                    {
                        if (!names.Add(member.Name))
                        {
                            return $"repeats the property '{member.Name}'.";
                        }
                        pending.Push((
                            member.Value, path + "/properties/" + EscapePointerToken(member.Name), depth + 1));
                    }
                }
                foreach (string term in s_nameSetTerms)
                {
                    if (!schema.TryGetProperty(term, out JsonElement values))
                    {
                        continue;
                    }
                    pointer = path + "/" + term;
                    if (values.ValueKind != JsonValueKind.Array)
                    {
                        return $"has a {term} member that is not an array.";
                    }
                    var stated = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JsonElement value in values.EnumerateArray())
                    {
                        if (value.ValueKind != JsonValueKind.String ||
                            value.GetString() is not { Length: > 0 } name ||
                            !stated.Add(name) ||
                            (names is not null && !names.Contains(name)))
                        {
                            return $"has a {term} member that does not name distinct declared fields.";
                        }
                    }
                    if (term == "uav:fieldOrder" && names is not null && stated.Count != names.Count)
                    {
                        return "has a uav:fieldOrder that omits declared fields.";
                    }
                }
                if (schema.TryGetProperty("enum", out JsonElement enumeration) &&
                    (enumeration.ValueKind != JsonValueKind.Array ||
                        enumeration.GetArrayLength() == 0 ||
                        !SameUnorderedValues(enumeration, enumeration)))
                {
                    pointer = path + "/enum";
                    return "has an enum that is not a non-empty set of distinct values.";
                }
                if (schema.TryGetProperty("items", out JsonElement items))
                {
                    pending.Push((items, path + "/items", depth + 1));
                }
                if (schema.TryGetProperty("additionalProperties", out JsonElement additional))
                {
                    pending.Push((additional, path + "/additionalProperties", depth + 1));
                }
                foreach (string term in s_alternativeTerms)
                {
                    if (!schema.TryGetProperty(term, out JsonElement alternatives))
                    {
                        continue;
                    }
                    pointer = path + "/" + term;
                    if (alternatives.ValueKind != JsonValueKind.Array || alternatives.GetArrayLength() == 0)
                    {
                        return $"has a {term} that is not a non-empty array of schemas.";
                    }
                    int index = 0;
                    foreach (JsonElement alternative in alternatives.EnumerateArray())
                    {
                        pending.Push((alternative,
                            path + "/" + term + "/" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            depth + 1));
                        index++;
                    }
                }
            }
            pointer = string.Empty;
            return null;
        }

        private static bool IsPositiveSchemaNumber(JsonElement value)
        {
            string number = value.GetRawText();
            if (number[0] == '-')
            {
                return false;
            }
            foreach (char character in number)
            {
                if (character is 'e' or 'E')
                {
                    break;
                }
                if (character is >= '1' and <= '9')
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsNonNegativeSchemaInteger(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Number)
            {
                return false;
            }
            string number = value.GetRawText();
            int index = number[0] == '-' ? 1 : 0;
            bool fraction = false;
            bool nonzero = false;
            int fractionalDigits = 0;
            int trailingZeros = 0;
            while (index < number.Length && number[index] is not ('e' or 'E'))
            {
                char character = number[index++];
                if (character == '.')
                {
                    fraction = true;
                    continue;
                }
                if (fraction)
                {
                    fractionalDigits++;
                }
                nonzero |= character != '0';
                trailingZeros = character == '0' ? trailingZeros + 1 : 0;
            }
            if (!nonzero)
            {
                return true;
            }
            if (number[0] == '-')
            {
                return false;
            }
            long exponent = 0;
            bool negativeExponent = false;
            if (index < number.Length)
            {
                index++;
                negativeExponent = number[index] == '-';
                if (number[index] is '-' or '+')
                {
                    index++;
                }
                while (index < number.Length)
                {
                    // Only the comparison with the coefficient length matters, not the full exponent value.
                    if (exponent <= number.Length)
                    {
                        exponent = (exponent * 10) + number[index] - '0';
                    }
                    index++;
                }
            }
            return (negativeExponent ? -exponent : exponent) >= fractionalDigits - trailingZeros;
        }

        private static string? CompareSchema(
            JsonElement external,
            JsonElement canonical,
            string canonicalDataType,
            string pointer,
            int depth,
            int maxDepth,
            out string mismatchPointer,
            bool exact = false)
        {
            mismatchPointer = pointer;
            if (depth > maxDepth)
            {
                return $"At '{pointer}': semantic schema comparison exceeds its depth limit of {maxDepth}.";
            }
            if (depth > 0 &&
                external.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                external.ValueKind == canonical.ValueKind)
            {
                mismatchPointer = string.Empty;
                return null;
            }
            if (external.ValueKind != JsonValueKind.Object)
            {
                return $"At '{pointer}': the external schema is not a JSON object, so it is not a DataSchema.";
            }
            if (canonical.ValueKind != JsonValueKind.Object)
            {
                return $"At '{pointer}': the canonical schema is not a DataSchema object.";
            }
            if (external.TryGetProperty("$ref", out _) || canonical.TryGetProperty("$ref", out _))
            {
                mismatchPointer = pointer + "/$ref";
                return $"At '{pointer}/$ref': an unexpanded schema reference cannot establish semantic compatibility.";
            }
            canonicalDataType = canonicalDataType.Length != 0
                ? canonicalDataType
                : ReadString(canonical, "uav:mapToType") ?? ReadString(canonical, "uav:dataTypeId") ?? string.Empty;
            foreach (string term in s_dataTypeTerms)
            {
                mismatchPointer = pointer + "/" + term;
                if (ReadString(external, term) is { Length: > 0 } stated &&
                    canonicalDataType.Length != 0 &&
                    WotNodeSetConverter.NormalizeExpandedNodeId(stated) !=
                        WotNodeSetConverter.NormalizeExpandedNodeId(canonicalDataType))
                {
                    return $"At '{pointer}/{term}': the external schema states {term} '{stated}' but the affordance " +
                        $"maps to DataType '{canonicalDataType}'.";
                }
            }
            bool hasExternalType = external.TryGetProperty("type", out JsonElement externalType);
            bool hasCanonicalType = canonical.TryGetProperty("type", out JsonElement canonicalType);
            mismatchPointer = pointer + "/type";
            if (hasExternalType &&
                hasCanonicalType &&
                !SameSchemaTypes(externalType, canonicalType))
            {
                return $"At '{pointer}/type': the external schema states type {externalType.GetRawText()}, " +
                    "which disagrees with " +
                    $"canonical type {canonicalType.GetRawText()}.";
            }
            if (exact && hasExternalType != hasCanonicalType)
            {
                return $"At '{pointer}/type': a schema alternative omits its canonical type.";
            }
            foreach (string term in s_comparedTerms)
            {
                mismatchPointer = pointer + "/" + term;
                bool hasExternal = external.TryGetProperty(term, out JsonElement externalValue);
                bool hasCanonical = canonical.TryGetProperty(term, out JsonElement canonicalValue);
                if (hasExternal && hasCanonical && !SameSchemaValue(externalValue, canonicalValue))
                {
                    return $"At '{pointer}/{term}': {externalValue.GetRawText()} disagrees with the " +
                        $"canonical value {canonicalValue.GetRawText()}.";
                }
                if (exact && hasExternal != hasCanonical)
                {
                    return $"At '{pointer}/{term}': the schema alternatives state different constraints.";
                }
            }
            foreach (string term in s_unorderedTerms)
            {
                mismatchPointer = pointer + "/" + term;
                bool hasExternal = external.TryGetProperty(term, out JsonElement externalValue);
                bool hasCanonical = canonical.TryGetProperty(term, out JsonElement canonicalValue);
                if (hasExternal && hasCanonical && !SameUnorderedValues(externalValue, canonicalValue))
                {
                    return $"At '{pointer}/{term}': the declared values differ from the canonical set.";
                }
                if (exact && hasExternal != hasCanonical)
                {
                    return $"At '{pointer}/{term}': a schema alternative omits a canonical constraint.";
                }
            }
            bool hasExternalMembers = external.TryGetProperty("properties", out JsonElement externalMembers);
            bool hasCanonicalMembers = canonical.TryGetProperty("properties", out JsonElement canonicalMembers);
            mismatchPointer = pointer + "/properties";
            if (hasExternalMembers && externalMembers.ValueKind != JsonValueKind.Object)
            {
                return $"At '{pointer}/properties': properties is not an object.";
            }
            if (hasCanonicalMembers && canonicalMembers.ValueKind != JsonValueKind.Object)
            {
                return $"At '{pointer}/properties': canonical properties is not an object.";
            }
            if (hasExternalMembers && hasCanonicalMembers && canonicalMembers.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty member in canonicalMembers.EnumerateObject())
                {
                    string childPointer = pointer + "/properties/" + EscapePointerToken(member.Name);
                    mismatchPointer = childPointer;
                    if (!externalMembers.TryGetProperty(member.Name, out JsonElement externalMember))
                    {
                        return $"At '{childPointer}': the canonical DataSchema declares the member " +
                            $"'{member.Name}' and the external schema does not.";
                    }
                    string? difference = CompareSchema(
                        externalMember, member.Value, string.Empty, childPointer, depth + 1, maxDepth,
                        out mismatchPointer, exact);
                    if (difference is not null)
                    {
                        return difference;
                    }
                }
                if (exact ||
                    (canonical.TryGetProperty("additionalProperties", out JsonElement additional) &&
                        additional.ValueKind == JsonValueKind.False))
                {
                    foreach (JsonProperty member in externalMembers.EnumerateObject())
                    {
                        if (!canonicalMembers.TryGetProperty(member.Name, out _))
                        {
                            mismatchPointer = pointer + "/properties/" + EscapePointerToken(member.Name);
                            return $"At '{pointer}/properties/{EscapePointerToken(member.Name)}': " +
                                "the external schema adds a field outside the canonical shape.";
                        }
                    }
                }
            }
            else if (exact && hasExternalMembers != hasCanonicalMembers)
            {
                return $"At '{pointer}/properties': a schema alternative omits its canonical members.";
            }
            bool hasExternalItems = external.TryGetProperty("items", out JsonElement externalItems);
            bool hasCanonicalItems = canonical.TryGetProperty("items", out JsonElement canonicalItems);
            mismatchPointer = pointer + "/items";
            if (hasExternalItems && hasCanonicalItems)
            {
                string? difference = CompareSchema(
                    externalItems, canonicalItems, string.Empty, pointer + "/items", depth + 1, maxDepth,
                    out mismatchPointer, exact);
                if (difference is not null)
                {
                    return difference;
                }
            }
            else if (exact && hasExternalItems != hasCanonicalItems)
            {
                return $"At '{pointer}/items': a schema alternative omits its canonical array element schema.";
            }
            foreach (string term in s_alternativeTerms)
            {
                mismatchPointer = pointer + "/" + term;
                bool hasExternal = external.TryGetProperty(term, out JsonElement externalAlternatives);
                bool hasCanonical = canonical.TryGetProperty(term, out JsonElement canonicalAlternatives);
                if (hasExternal && hasCanonical)
                {
                    if (externalAlternatives.ValueKind != JsonValueKind.Array ||
                        canonicalAlternatives.ValueKind != JsonValueKind.Array ||
                        externalAlternatives.GetArrayLength() != canonicalAlternatives.GetArrayLength())
                    {
                        return $"At '{pointer}/{term}': the schema alternatives differ.";
                    }
                    var matched = new HashSet<int>();
                    foreach (JsonElement candidate in externalAlternatives.EnumerateArray())
                    {
                        bool found = false;
                        int index = 0;
                        foreach (JsonElement expected in canonicalAlternatives.EnumerateArray())
                        {
                            if (!matched.Contains(index) &&
                                CompareSchema(
                                    candidate, expected, string.Empty, pointer + "/" + term,
                                    depth + 1, maxDepth, out mismatchPointer, exact: true) is null)
                            {
                                matched.Add(index);
                                found = true;
                                break;
                            }
                            index++;
                        }
                        if (!found)
                        {
                            mismatchPointer = pointer + "/" + term;
                            return $"At '{pointer}/{term}': an alternative differs from every canonical branch.";
                        }
                    }
                }
                else if (exact && hasExternal != hasCanonical)
                {
                    return $"At '{pointer}/{term}': a schema alternative omits a canonical choice.";
                }
            }
            mismatchPointer = string.Empty;
            return null;
        }

        private static bool SameSchemaTypes(JsonElement first, JsonElement second)
        {
            var firstTypes = new HashSet<string>(StringComparer.Ordinal);
            var secondTypes = new HashSet<string>(StringComparer.Ordinal);
            return ReadTypes(first, firstTypes) && ReadTypes(second, secondTypes) && firstTypes.SetEquals(secondTypes);

            static bool ReadTypes(JsonElement element, HashSet<string> types)
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    string? name = element.GetString();
                    if (name is not ("null" or "boolean" or "integer" or "number" or "string" or "object" or "array"))
                    {
                        return false;
                    }
                    types.Add(name);
                    return true;
                }
                if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0)
                {
                    return false;
                }
                foreach (JsonElement entry in element.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.String || !ReadTypes(entry, types))
                    {
                        return false;
                    }
                }
                return types.Count == element.GetArrayLength();
            }
        }

        private static bool SameUnorderedValues(JsonElement first, JsonElement second)
        {
            if (first.ValueKind != JsonValueKind.Array || second.ValueKind != JsonValueKind.Array)
            {
                return false;
            }
            var remaining = new List<JsonElement>();
            foreach (JsonElement value in second.EnumerateArray())
            {
                if (remaining.Exists(existing => SameSchemaValue(existing, value)))
                {
                    return false;
                }
                remaining.Add(value);
            }
            foreach (JsonElement value in first.EnumerateArray())
            {
                int index = remaining.FindIndex(existing => SameSchemaValue(existing, value));
                if (index < 0)
                {
                    return false;
                }
                remaining.RemoveAt(index);
            }
            return remaining.Count == 0;
        }

        private static bool SameSchemaValue(JsonElement first, JsonElement second)
        {
            return JsonElement.DeepEquals(first, second);
        }

        private static string EscapePointerToken(string value)
        {
            return value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets whether a reference is one a provider can be asked for at all.
        /// </summary>
        /// <remarks>
        /// An absolute IRI names a document; a relative path names one relative
        /// to the document that referenced it. Anything else - an empty value,
        /// or a fragment on its own - names nothing a provider could hold, and
        /// asking for it would only invite a provider to guess.
        /// </remarks>
        private static bool IsAcceptableReference(string reference)
        {
            if (reference.Length == 0 || reference[0] == '#')
            {
                return false;
            }

            // An absolute IRI names a document outright; anything else is read
            // as a path relative to the document that referenced it. A value
            // that is neither - a bare fragment, or a malformed IRI - names
            // nothing a provider could hold, and asking for it would only
            // invite a provider to guess.
            return Uri.IsWellFormedUriString(reference, UriKind.Absolute) ||
                Uri.IsWellFormedUriString(reference, UriKind.Relative);
        }

        private static bool IsReadableContentType(string? contentType)
        {
            if (string.IsNullOrEmpty(contentType))
            {
                // A provider that reports no media type is answering with the
                // bytes it was asked for; the parse below decides whether they
                // are a DataSchema.
                return true;
            }
            int separator = contentType!.IndexOf(';', StringComparison.Ordinal);
            string media = (separator < 0 ? contentType : contentType[..separator])
                .Trim();
            foreach (string readable in ReadableContentTypes)
            {
                if (string.Equals(media, readable, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }
            for (int ii = 0; ii < left.Length; ii++)
            {
                if (left[ii] != right[ii])
                {
                    return false;
                }
            }
            return true;
        }

        private static string? ReadString(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static readonly string[] s_dataTypeTerms =
        [
            "uav:mapToType",
            "uav:dataTypeId"
        ];

        private static readonly string[] s_comparedTerms =
        [
            "format",
            "contentEncoding",
            "minimum",
            "maximum",
            "exclusiveMinimum",
            "exclusiveMaximum",
            "multipleOf",
            "minLength",
            "maxLength",
            "pattern",
            "minItems",
            "maxItems",
            "minProperties",
            "maxProperties",
            "uniqueItems",
            "additionalProperties",
            "const",
            "uav:valueRank",
            "uav:arrayDimensions",
            "uav:fieldOrder",
            "uav:enumName"
        ];

        private static readonly string[] s_unorderedTerms = ["required", "enum"];

        private static readonly string[] s_nameSetTerms = ["required", "uav:fieldOrder"];

        private static readonly string[] s_numericTerms =
        [
            "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf"
        ];

        private static readonly string[] s_countTerms =
        [
            "minLength", "maxLength", "minItems", "maxItems", "minProperties", "maxProperties"
        ];

        private static readonly string[] s_stringTerms =
        [
            "format", "contentEncoding", "pattern", "uav:enumName", "uav:mapToType", "uav:dataTypeId"
        ];

        private static readonly string[] s_alternativeTerms = ["oneOf", "anyOf", "allOf"];

        private readonly IWotSchemaResolver[] m_providers;
    }

    /// <summary>
    /// The external schema outcomes one conversion resolved, keyed by the
    /// affordance that named them.
    /// </summary>
    /// <remarks>
    /// Resolution is asynchronous and the synthesis is not, so every reference
    /// is resolved once, before the synthesis, and the synthesis reads the
    /// answers. A conversion with no schema provider produces an empty catalog
    /// and the synthesis reports each reference as carried but not evaluated,
    /// which is what it did before providers existed.
    /// </remarks>
    internal sealed class WotExternalSchemaCatalog
    {
        public void Add(string key, WotExternalSchemaResult result)
        {
            m_results[key] = result;
        }

        public bool TryGet(string key, out WotExternalSchemaResult result)
        {
            return m_results.TryGetValue(key, out result!);
        }

        private readonly Dictionary<string, WotExternalSchemaResult> m_results =
            new(StringComparer.Ordinal);
    }
}
