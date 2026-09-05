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
                    parsed.RootElement, canonical, canonicalDataType);
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
        private static string? FindIncompatibility(
            JsonElement external,
            JsonElement canonical,
            string canonicalDataType)
        {
            if (external.ValueKind != JsonValueKind.Object)
            {
                return "The external schema is not a JSON object, so it is not a DataSchema.";
            }
            foreach (string term in s_dataTypeTerms)
            {
                if (ReadString(external, term) is { Length: > 0 } stated &&
                    canonicalDataType.Length != 0 &&
                    !string.Equals(stated, canonicalDataType, StringComparison.Ordinal))
                {
                    return $"The external schema states {term} '{stated}' but the affordance " +
                        $"maps to DataType '{canonicalDataType}'.";
                }
            }
            foreach (string term in s_comparedTerms)
            {
                string? externalValue = ReadString(external, term);
                string? canonicalValue = ReadString(canonical, term);
                if (externalValue is not null &&
                    canonicalValue is not null &&
                    !string.Equals(externalValue, canonicalValue, StringComparison.Ordinal))
                {
                    return $"The external schema states {term} '{externalValue}' but the " +
                        $"canonical DataSchema states '{canonicalValue}'.";
                }
            }
            return FindMemberIncompatibility(external, canonical);
        }

        /// <summary>
        /// Compares the members of an object DataSchema: every member the
        /// canonical schema declares has to be declared by the external one and
        /// with the same json type.
        /// </summary>
        private static string? FindMemberIncompatibility(
            JsonElement external,
            JsonElement canonical)
        {
            if (canonical.ValueKind != JsonValueKind.Object ||
                !canonical.TryGetProperty("properties", out JsonElement canonicalMembers) ||
                canonicalMembers.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            if (!external.TryGetProperty("properties", out JsonElement externalMembers) ||
                externalMembers.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            foreach (JsonProperty member in canonicalMembers.EnumerateObject())
            {
                if (!externalMembers.TryGetProperty(member.Name, out JsonElement externalMember))
                {
                    return $"The canonical DataSchema declares the member '{member.Name}' and " +
                        "the external schema does not.";
                }
                string? externalType = ReadString(externalMember, "type");
                string? canonicalType = ReadString(member.Value, "type");
                if (externalType is not null &&
                    canonicalType is not null &&
                    !string.Equals(externalType, canonicalType, StringComparison.Ordinal))
                {
                    return $"The member '{member.Name}' is '{canonicalType}' in the canonical " +
                        $"DataSchema and '{externalType}' in the external schema.";
                }
            }
            return null;
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
            string media = (separator < 0 ? contentType : contentType.Substring(0, separator))
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
            "type",
            "format",
            "contentEncoding"
        ];

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
