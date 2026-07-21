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
using System.Collections.Immutable;

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>How a binder matched an interaction form.</summary>
    public enum WotBindingMatchKind
    {
        /// <summary>The binder does not handle the form.</summary>
        None = 0,

        /// <summary>The form's <c>href</c> URI scheme is handled by the binder.</summary>
        Scheme = 1,

        /// <summary>The form's <c>subprotocol</c> is handled by the binder.</summary>
        Subprotocol = 2,

        /// <summary>A binding-specific vocabulary term identifies the binding.</summary>
        Vocabulary = 3,

        /// <summary>A pinned identification rule (scheme + term + shape) matched.</summary>
        PinnedRule = 4,

        /// <summary>The resource explicitly pinned this binder by id / version.</summary>
        ExplicitBindingId = 5
    }

    /// <summary>
    /// The result of asking a binder whether it handles an interaction form.
    /// Selection is deterministic: the registry chooses the highest
    /// <see cref="Priority"/> and breaks ties by ordinal binder <c>id@version</c>.
    /// </summary>
    public readonly struct WotBindingMatch : IEquatable<WotBindingMatch>
    {
        private WotBindingMatch(bool isMatch, WotBindingMatchKind kind, int priority)
        {
            IsMatch = isMatch;
            Kind = kind;
            Priority = priority;
        }

        /// <summary>A "does not handle" result.</summary>
        public static WotBindingMatch NoMatch { get; } = new WotBindingMatch(false, WotBindingMatchKind.None, 0);

        /// <summary>Gets whether the binder handles the form.</summary>
        public bool IsMatch { get; }

        /// <summary>Gets the kind of the match.</summary>
        public WotBindingMatchKind Kind { get; }

        /// <summary>Gets the selection priority (higher wins).</summary>
        public int Priority { get; }

        /// <summary>Creates a positive match with an explicit priority.</summary>
        public static WotBindingMatch Match(WotBindingMatchKind kind, int priority)
            => new WotBindingMatch(true, kind, priority);

        /// <summary>
        /// Creates a positive match whose priority defaults to the match kind, so
        /// an explicit binding-id pin beats a pinned rule, which beats a
        /// vocabulary match, which beats a subprotocol match, which beats a bare
        /// scheme match.
        /// </summary>
        public static WotBindingMatch Match(WotBindingMatchKind kind)
            => new WotBindingMatch(true, kind, (int)kind * 100);

        /// <inheritdoc/>
        public bool Equals(WotBindingMatch other)
            => IsMatch == other.IsMatch && Kind == other.Kind && Priority == other.Priority;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is WotBindingMatch other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode()
            => ((IsMatch ? 1 : 0) * 31 + (int)Kind) * 31 + Priority;

        /// <summary>Equality operator.</summary>
        public static bool operator ==(WotBindingMatch left, WotBindingMatch right) => left.Equals(right);

        /// <summary>Inequality operator.</summary>
        public static bool operator !=(WotBindingMatch left, WotBindingMatch right) => !left.Equals(right);
    }

    /// <summary>
    /// Context supplied to a binder while it decides whether it handles a form:
    /// the binding ids explicitly pinned on the resource / registry and the
    /// document kind. Explicit pins let operators force a specific binder even
    /// when several could match by scheme.
    /// </summary>
    public sealed class WotBindingSelectionContext
    {
        /// <summary>An empty context (no explicit pins).</summary>
        public static WotBindingSelectionContext Empty { get; } =
            new WotBindingSelectionContext(ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);

        /// <summary>Initializes a new selection context.</summary>
        /// <param name="selectedBindingIds">Explicit binder id / key pins on the resource.</param>
        /// <param name="selectedBindingUris">Explicit binding vocabulary URI pins on the resource.</param>
        public WotBindingSelectionContext(
            ImmutableArray<string> selectedBindingIds,
            ImmutableArray<string> selectedBindingUris)
        {
            SelectedBindingIds = selectedBindingIds.IsDefault ? ImmutableArray<string>.Empty : selectedBindingIds;
            SelectedBindingUris = selectedBindingUris.IsDefault ? ImmutableArray<string>.Empty : selectedBindingUris;
        }

        /// <summary>Gets the binder id / key pins explicitly selected on the resource.</summary>
        public ImmutableArray<string> SelectedBindingIds { get; }

        /// <summary>Gets the binding vocabulary URI pins explicitly selected on the resource.</summary>
        public ImmutableArray<string> SelectedBindingUris { get; }

        /// <summary>Gets whether the identity is explicitly pinned by id, key or URI.</summary>
        public bool IsPinned(WotBindingIdentity identity)
        {
            foreach (string pin in SelectedBindingIds)
            {
                if (string.Equals(pin, identity.Id, StringComparison.Ordinal) ||
                    string.Equals(pin, identity.Key, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            foreach (string uri in SelectedBindingUris)
            {
                if (string.Equals(uri, identity.BindingUri, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Identifies whether a binder handles a given interaction form. Implementations
    /// pin exact rules (scheme, subprotocol, vocabulary term or explicit binding id)
    /// so selection is deterministic and does not rely on the URI scheme alone.
    /// </summary>
    public interface IWotBindingIdentification
    {
        /// <summary>
        /// Returns a match describing whether and how strongly this binder handles
        /// the supplied form. Returns <see cref="WotBindingMatch.NoMatch"/> when the
        /// binder does not handle the form.
        /// </summary>
        WotBindingMatch Match(WotAffordanceForm form, WotBindingSelectionContext context);
    }
}
