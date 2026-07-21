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
using Opc.Ua.WotCon.V2;

namespace Opc.Ua.WotCon.Binding
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
        /// <summary>Gets the stable binder identity (id + version).</summary>
        WotBindingIdentity Identity { get; }

        /// <summary>Gets the version-pinned capability snapshot.</summary>
        WotBindingCapability Capability { get; }

        /// <summary>Gets the deterministic identification rules.</summary>
        IWotBindingIdentification Identification { get; }

        /// <summary>Gets the form validator / compiler.</summary>
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

        /// <summary>Gets the URI schemes the binder handles for scheme-based identification.</summary>
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
                WotBindingMatch vocabulary = WotBindingMatch.Match(WotBindingMatchKind.Vocabulary);
                if (vocabulary.Priority > best.Priority)
                {
                    best = vocabulary;
                }
            }
            return best;
        }

        /// <summary>Gets whether the form's href scheme is handled by this binder.</summary>
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

        /// <summary>Gets whether the form object carries any member with the vocabulary prefix.</summary>
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

        /// <summary>Attempts to parse an href as an absolute URI.</summary>
        protected static bool TryParseUri(string href, out Uri uri)
            => Uri.TryCreate(href, UriKind.Absolute, out uri!) && uri is not null;

        /// <summary>Builds an endpoint descriptor from a parsed URI authority.</summary>
        protected static WotEndpointDescriptor MakeEndpoint(Uri uri)
            => new WotEndpointDescriptor(
                uri.Scheme,
                string.IsNullOrEmpty(uri.Host) ? null : uri.Host,
                uri.Port,
                uri.GetLeftPart(UriPartial.Authority));

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

        /// <summary>Extracts the lower-case URI scheme from an href, if present.</summary>
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
            return href.Substring(0, colon).ToLowerInvariant();
        }

        /// <summary>
        /// Requires a non-empty, in-bounds <c>href</c> and reports a diagnostic when
        /// it is missing or too long.
        /// </summary>
        protected bool RequireHref(
            WotAffordanceForm form, WotBindingPlanContext context, ICollection<WotBindingDiagnostic> diagnostics, out string href)
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

        /// <summary>Selects a codec for a content type, reporting when none is available.</summary>
        protected string ResolveCodec(
            WotAffordanceForm form, WotBindingPlanContext context, out WotPayloadDescriptor payload)
        {
            string contentType = string.IsNullOrEmpty(form.ContentType) ? "application/json" : form.ContentType!;
            context.Codecs.TrySelect(form.ContentType, out IWotPayloadCodec codec);
            payload = new WotPayloadDescriptor(contentType, codec.Id);
            return codec.Id;
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
                return ImmutableArray<WotCredentialReference>.Empty;
            }
            var builder = ImmutableArray.CreateBuilder<WotCredentialReference>();
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
