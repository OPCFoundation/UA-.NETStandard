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
using System.Collections.Immutable;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// A replaceable protocol binder: the composition of a stable identity, a
    /// capability snapshot, deterministic identification and a planner. Binders
    /// are injected independently and selected by identity and pinned rules, so
    /// multiple versions of the same binding can coexist. Concrete executors are
    /// registered separately, so a binder can validate and compile plans for
    /// protocols the runtime cannot execute.
    /// </summary>
    public interface IWotProtocolBinder
    {
        /// <summary>
        /// Gets the stable binder identity (id + version).
        /// </summary>
        WotBindingIdentity Identity { get; }

        /// <summary>
        /// Gets the version-pinned capability snapshot.
        /// </summary>
        WotBindingCapability Capability { get; }

        /// <summary>
        /// Gets the deterministic identification rules.
        /// </summary>
        IWotBindingIdentification Identification { get; }

        /// <summary>
        /// Gets the form validator / compiler.
        /// </summary>
        IWotBindingPlanner Planner { get; }
    }

    /// <summary>
    /// Base class for protocol binders. It implements
    /// <see cref="IWotProtocolBinder"/>, <see cref="IWotBindingIdentification"/>
    /// and <see cref="IWotBindingPlanner"/> and provides shared validation helpers
    /// (scheme identification, operation compatibility, codec selection and
    /// secret-free security resolution) so concrete planners focus on the protocol
    /// specifics.
    /// </summary>
    public abstract class WotProtocolBinderBase : IWotProtocolBinder, IWotBindingIdentification, IWotBindingPlanner
    {
        /// <inheritdoc/>
        public abstract WotBindingIdentity Identity { get; }

        /// <inheritdoc/>
        public abstract WotBindingCapability Capability { get; }

        /// <inheritdoc/>
        public IWotBindingIdentification Identification => this;

        /// <inheritdoc/>
        public IWotBindingPlanner Planner => this;

        /// <inheritdoc/>
        public abstract WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context);

        /// <inheritdoc/>
        public abstract WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context);

        /// <summary>
        /// Gets the URI schemes the binder handles for scheme-based identification.
        /// </summary>
        protected abstract IReadOnlyCollection<string> Schemes { get; }

        /// <summary>
        /// A default scheme / vocabulary / explicit-pin identification helper. An
        /// explicit pin on the resource wins with
        /// <see cref="WotBindingMatchKind.ExplicitBindingId"/>; otherwise a form
        /// whose <c>href</c> scheme is handled matches with
        /// <see cref="WotBindingMatchKind.Scheme"/>, and a form carrying the
        /// binding's vocabulary prefix matches with the stronger
        /// <see cref="WotBindingMatchKind.Vocabulary"/>.
        /// </summary>
        protected WotBindingMatch MatchStandard(
            WotAffordanceForm form, WotBindingSelectionContext context, string? vocabularyPrefix)
        {
            if (context.IsPinned(Identity))
            {
                return WotBindingMatch.Match(WotBindingMatchKind.ExplicitBindingId);
            }
            WotBindingMatch best = WotBindingMatch.NoMatch;
            if (SchemeMatches(form))
            {
                best = WotBindingMatch.Match(WotBindingMatchKind.Scheme);
            }
            if (!string.IsNullOrEmpty(vocabularyPrefix) && HasVocabularyPrefix(form, vocabularyPrefix!))
            {
                var vocabulary = WotBindingMatch.Match(WotBindingMatchKind.Vocabulary);
                if (vocabulary.Priority > best.Priority)
                {
                    best = vocabulary;
                }
            }
            return best;
        }

        /// <summary>
        /// Gets whether the form's href scheme is handled by this binder.
        /// </summary>
        protected bool SchemeMatches(WotAffordanceForm form)
        {
            string? scheme = SchemeOf(form.Href);
            if (scheme is null)
            {
                return false;
            }
            foreach (string handled in Schemes)
            {
                if (string.Equals(scheme, handled, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets whether the form object carries any member with the vocabulary prefix.
        /// </summary>
        protected static bool HasVocabularyPrefix(WotAffordanceForm form, string prefix)
        {
            if (form.FormElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return false;
            }
            foreach (System.Text.Json.JsonProperty property in form.FormElement.EnumerateObject())
            {
                if (property.Name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Builds the form of a URI that goes on the wire.
        /// </summary>
        /// <remarks>
        /// <para>
        /// WoT Binding Section 5.4 requires every non-ASCII character of a
        /// transmitted URI to be encoded as UTF-8 bytes and each byte then
        /// percent-encoded. A URI that still carries a non-ASCII character is
        /// an IRI, and what an IRI turns into on the wire depends on the
        /// framework's IRI parsing being on, on the target's own idea of the
        /// encoding, and on a proxy in between: three ways to reach a
        /// different resource than the document names. Doing the encoding here
        /// makes the answer the same everywhere, and leaves an already-ASCII
        /// URI - including one whose percent-escapes are already written -
        /// untouched.
        /// </para>
        /// <para>
        /// The URI is therefore rebuilt from its components rather than
        /// encoded as one string: percent-encoding is defined for the path,
        /// the query and the fragment, and is <em>not</em> a legal spelling of
        /// a host. A registered name is turned into ASCII by IDNA instead, so
        /// <c>http://ü.example/x</c> transmits as
        /// <c>http://xn--tda.example/x</c> - the name that resolves - rather
        /// than as <c>http://%C3%BC.example/x</c>, which names no host at all
        /// and would be rejected or, worse, re-interpreted by an intermediary.
        /// Userinfo, an explicit port and an IPv6 literal are carried through
        /// unchanged.
        /// </para>
        /// </remarks>
        /// <param name="uri">The parsed absolute URI.</param>
        /// <returns>The ASCII-only URI to transmit.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <c>null</c>.</exception>
        protected static string ToTransmittedUri(Uri uri)
        {
            if (uri is null)
            {
                throw new ArgumentNullException(nameof(uri));
            }
            return BuildTransmittedUri(uri);
        }

        /// <summary>
        /// Builds the authority a transmitted URI carries: the scheme, the
        /// IDNA A-label host, and the userinfo and explicit port when the
        /// document states them.
        /// </summary>
        /// <remarks>
        /// This is the string an endpoint policy is evaluated against and the
        /// string a credential is scoped to. Both have to name the host the
        /// request actually reaches: a policy that blocks
        /// <c>xn--tda.example</c> while the plan carries <c>ü.example</c>
        /// blocks nothing, and a credential scoped to one spelling is not
        /// found when the other is presented.
        /// </remarks>
        /// <param name="uri">The parsed absolute URI.</param>
        /// <returns>The transmitted authority.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <c>null</c>.</exception>
        protected static string ToTransmittedAuthority(Uri uri)
        {
            if (uri is null)
            {
                throw new ArgumentNullException(nameof(uri));
            }
            var builder = new System.Text.StringBuilder(uri.Scheme);
            if (!AppendAuthority(builder, uri))
            {
                return uri.GetLeftPart(UriPartial.Authority);
            }
            return builder.ToString();
        }

        /// <summary>
        /// Gets the ASCII form of a URI's host: the IDNA A-label of a
        /// registered name, the literal of an IP address, and an empty string
        /// when the URI carries no authority.
        /// </summary>
        /// <param name="uri">The parsed absolute URI.</param>
        /// <returns>The ASCII host, without IPv6 brackets.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <c>null</c>.</exception>
        protected static string ToAsciiHost(Uri uri)
        {
            return WotEndpointValidator.ToAsciiHost(uri);
        }

        /// <summary>
        /// Rebuilds a URI as the ASCII string that goes on the wire.
        /// </summary>
        internal static string BuildTransmittedUri(Uri uri)
        {
            var builder = new System.Text.StringBuilder(uri.Scheme.Length + 16);
            builder.Append(uri.Scheme);
            if (!AppendAuthority(builder, uri))
            {
                // A URI with no authority - a mailto:, a urn:, an opaque
                // scheme - has no host to map, so the whole remainder is the
                // path-ish part and is encoded as one.
                return PercentEncodeNonAscii(uri.AbsoluteUri);
            }
            builder
                .Append(PercentEncodeNonAscii(uri.AbsolutePath))
                .Append(PercentEncodeNonAscii(uri.Query))
                .Append(PercentEncodeNonAscii(uri.Fragment));
            return builder.ToString();
        }

        /// <summary>
        /// Appends <c>://[userinfo@]host[:port]</c> to a builder that holds the
        /// scheme, and reports whether the URI has an authority at all.
        /// </summary>
        private static bool AppendAuthority(System.Text.StringBuilder builder, Uri uri)
        {
            // GetLeftPart is the only member that distinguishes a URI with an
            // authority from one without: 'mailto:a@example.com' parses with a
            // Host and a UserInfo but has no authority, and writing '//' in
            // front of it would name a different resource.
            if (uri.GetLeftPart(UriPartial.Authority).Length == 0)
            {
                return false;
            }
            string host = ToAsciiHost(uri);
            builder.Append("://");
            string userInfo = uri.UserInfo;
            if (!string.IsNullOrEmpty(userInfo))
            {
                builder.Append(PercentEncodeNonAscii(userInfo)).Append('@');
            }
            if (uri.HostNameType == UriHostNameType.IPv6)
            {
                builder.Append('[').Append(host).Append(']');
            }
            else
            {
                builder.Append(host);
            }
            // A port is written only where it is not the scheme's own. An
            // absolute URI always reports one: the scheme's default where none
            // was written, and -1 where the scheme has none - which is the
            // default, so the two cases are the same test.
            if (!uri.IsDefaultPort)
            {
                builder.Append(':').Append(
                    uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return true;
        }

        /// <summary>
        /// Percent-encodes every non-ASCII character of a URI as UTF-8 bytes.
        /// </summary>
        internal static string PercentEncodeNonAscii(string uri)
        {
            if (uri is null)
            {
                throw new ArgumentNullException(nameof(uri));
            }
            bool ascii = true;
            for (int i = 0; i < uri.Length; i++)
            {
                if (uri[i] > '\u007F')
                {
                    ascii = false;
                    break;
                }
            }
            if (ascii)
            {
                return uri;
            }

            var builder = new System.Text.StringBuilder(uri.Length + 16);
            int start = 0;
            while (start < uri.Length)
            {
                char c = uri[start];
                if (c <= '\u007F')
                {
                    builder.Append(c);
                    start++;
                    continue;
                }

                // A surrogate pair is one code point and therefore one UTF-8
                // sequence: encoding the halves separately would produce two
                // invalid sequences that no target can put back together.
                int length = char.IsHighSurrogate(c) &&
                    start + 1 < uri.Length &&
                    char.IsLowSurrogate(uri[start + 1])
                        ? 2
                        : 1;
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(
                    uri.Substring(start, length));
                foreach (byte value in bytes)
                {
                    builder.Append('%').Append(
                        value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                }
                start += length;
            }
            return builder.ToString();
        }

        /// <summary>
        /// Attempts to parse an href as an absolute URI.
        /// </summary>
        protected static bool TryParseUri(string href, out Uri uri)
        {
            return Uri.TryCreate(href, UriKind.Absolute, out uri!) && uri is not null;
        }

        /// <summary>
        /// Builds an endpoint descriptor from a parsed URI authority.
        /// </summary>
        /// <remarks>
        /// The descriptor carries the ASCII authority, which is the one the
        /// request reaches and therefore the one an endpoint policy and a
        /// credential reference have to be evaluated against.
        /// </remarks>
        protected static WotEndpointDescriptor MakeEndpoint(Uri uri)
        {
            string host = ToAsciiHost(uri);
            return new WotEndpointDescriptor(
                        uri.Scheme,
                        host.Length == 0 ? null : host,
                        uri.Port,
                        ToTransmittedAuthority(uri));
        }

        /// <summary>
        /// Builds an endpoint descriptor from an href, or a synthetic descriptor
        /// carrying the raw href when it is not a parseable absolute URI (used by
        /// document-level, non-executable bindings).
        /// </summary>
        protected static WotEndpointDescriptor MakeEndpointOrSynthetic(string? href, string scheme)
        {
            if (!string.IsNullOrEmpty(href) && TryParseUri(href!, out Uri uri))
            {
                return MakeEndpoint(uri);
            }
            return new WotEndpointDescriptor(scheme, null, -1, href ?? string.Empty);
        }

        /// <summary>
        /// Extracts the lower-case URI scheme from an href, if present.
        /// </summary>
        protected static string? SchemeOf(string? href)
        {
            if (string.IsNullOrEmpty(href))
            {
                return null;
            }
            int colon = -1;
            for (int i = 0; i < href!.Length; i++)
            {
                if (href[i] == ':')
                {
                    colon = i;
                    break;
                }
            }
            if (colon <= 0)
            {
                return null;
            }
            return href[..colon].ToLowerInvariant();
        }

        /// <summary>
        /// Requires a non-empty, in-bounds <c>href</c> and reports a diagnostic when
        /// it is missing or too long.
        /// </summary>
        protected bool RequireHref(
            WotAffordanceForm form,
            WotBindingPlanContext context,
            ICollection<WotBindingDiagnostic> diagnostics,
            out string href)
        {
            href = form.Href ?? string.Empty;
            if (string.IsNullOrEmpty(href))
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.MissingHref,
                    "The form has no href.", form.Pointer("href")));
                return false;
            }
            if (href.Length > context.Bounds.MaxUriLength)
            {
                diagnostics.Add(WotBindingDiagnostic.Error(
                    WotBindingDiagnosticCode.BoundsExceeded,
                    $"The href exceeds the maximum length of {context.Bounds.MaxUriLength}.",
                    form.Pointer("href")));
                return false;
            }
            return true;
        }

        /// <summary>
        /// Yields the (op token, capability) pairs the binder supports for the
        /// form, reporting a diagnostic for each incompatible or unsupported op.
        /// </summary>
        protected IEnumerable<(string Op, WoTBindingCapabilityEnum Capability)> ResolveOperations(
            WotAffordanceForm form, ICollection<WotBindingDiagnostic> diagnostics)
        {
            var seen = new HashSet<WoTBindingCapabilityEnum>();
            var results = new List<(string, WoTBindingCapabilityEnum)>();
            foreach (string op in form.Operations)
            {
                if (!WotOperations.IsCompatible(form.Kind, op))
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.IncompatibleOperation,
                        $"The operation '{op}' is not compatible with a {form.Kind} affordance.",
                        form.Pointer("op"), op));
                    continue;
                }
                if (!WotOperations.TryMap(op, out WoTBindingCapabilityEnum capability))
                {
                    diagnostics.Add(WotBindingDiagnostic.Warning(
                        WotBindingDiagnosticCode.UnsupportedOperation,
                        $"The operation '{op}' is not modelled by the registry.",
                        form.Pointer("op"), op));
                    continue;
                }
                if (!Capability.Supports(capability))
                {
                    diagnostics.Add(WotBindingDiagnostic.Warning(
                        WotBindingDiagnosticCode.UnsupportedOperation,
                        $"The binding '{Identity.Id}' does not support '{op}'.",
                        form.Pointer("op"), op));
                    continue;
                }
                // "unobserveproperty" and "unsubscribeevent" are teardown ops for a
                // running observe / subscribe; do not emit a duplicate entry.
                if (op is "unobserveproperty" or "unsubscribeevent")
                {
                    continue;
                }
                if (seen.Add(capability))
                {
                    results.Add((op, capability));
                }
            }
            return results;
        }

        /// <summary>
        /// Selects a codec for a content type, reporting when none is available.
        /// </summary>
        protected bool ResolveCodec(
            WotAffordanceForm form,
            WotBindingPlanContext context,
            ICollection<WotBindingDiagnostic> diagnostics,
            out WotPayloadDescriptor payload)
        {
            string contentType = string.IsNullOrEmpty(form.ContentType) ? "application/json" : form.ContentType!;
            if (!ValidateContentType(form, contentType, diagnostics))
            {
                payload = new WotPayloadDescriptor(contentType, string.Empty);
                return false;
            }
            context.Codecs.TrySelect(form.ContentType, out IWotPayloadCodec codec);
            payload = new WotPayloadDescriptor(contentType, codec.Id);
            return true;
        }

        /// <summary>
        /// Validates a WoT <c>contentType</c> value before it reaches protocol sinks.
        /// </summary>
        protected static bool ValidateContentType(
            WotAffordanceForm form,
            string contentType,
            ICollection<WotBindingDiagnostic> diagnostics)
        {
            for (int i = 0; i < contentType.Length; i++)
            {
                char ch = contentType[i];
                if (ch is '\r' or '\n' or '\0' || ch > 0x7F)
                {
                    diagnostics.Add(WotBindingDiagnostic.Error(
                        WotBindingDiagnosticCode.InvalidFieldValue,
                        "The contentType contains characters that are not permitted in an HTTP header value.",
                        form.Pointer("contentType"),
                        "contentType"));
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Resolves the form's security scheme references into secret-free
        /// credential references, reporting a diagnostic for any scheme not
        /// declared in the document's <c>securityDefinitions</c>.
        /// </summary>
        protected ImmutableArray<WotCredentialReference> ResolveSecurity(
            WotAffordanceForm form, WotBindingPlanContext context, string? endpoint,
            ICollection<WotBindingDiagnostic> diagnostics)
        {
            if (form.SecuritySchemes.IsEmpty)
            {
                return [];
            }
            ImmutableArray<WotCredentialReference>.Builder builder =
                ImmutableArray.CreateBuilder<WotCredentialReference>();
            foreach (string scheme in form.SecuritySchemes)
            {
                if (context.SecurityDefinitions.TryGetValue(scheme, out WotSecurityDefinition? definition))
                {
                    builder.Add(WotCredentialReference.FromDefinition(
                        definition, Identity.BindingUri, endpoint));
                }
                else if (!string.Equals(scheme, "nosec_sc", StringComparison.Ordinal))
                {
                    diagnostics.Add(WotBindingDiagnostic.Warning(
                        WotBindingDiagnosticCode.UnknownSecurityScheme,
                        $"The security scheme '{scheme}' is not declared in securityDefinitions.",
                        form.Pointer("security"), scheme));
                }
            }
            return builder.ToImmutable();
        }
    }
}
