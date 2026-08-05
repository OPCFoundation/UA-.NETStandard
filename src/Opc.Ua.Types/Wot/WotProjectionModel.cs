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
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The endpoint a consumer of a projected affordance talks to.
    /// </summary>
    /// <remarks>
    /// See WoT Binding Section 12.5. The distinction decides which document a
    /// carried form belongs to, and therefore which base its relative
    /// <c>href</c> is resolved against.
    /// </remarks>
    public enum WotProjectionRouting
    {
        /// <summary>
        /// Consumers talk to the source document's own endpoint. This is the
        /// default when a source declares no routing.
        /// </summary>
        Source,

        /// <summary>
        /// Consumers talk to the projection host, which serves the affordance
        /// on the source's behalf.
        /// </summary>
        Projection
    }

    /// <summary>
    /// The kind of affordance a predicate filter admits.
    /// </summary>
    public enum WotAffordanceKind
    {
        /// <summary>Any affordance kind; the filter does not constrain it.</summary>
        Any,

        /// <summary>A member of <c>properties</c>.</summary>
        Property,

        /// <summary>A member of <c>actions</c>.</summary>
        Action,

        /// <summary>A member of <c>events</c>.</summary>
        Event
    }

    /// <summary>
    /// One predicate filter of a source's <c>uav:select</c> array.
    /// </summary>
    /// <remarks>
    /// See WoT Binding Section 12.3. The constraints within one filter are
    /// conjunctive and the filters of a source are disjunctive. The predicate
    /// set is deliberately closed - only affordance kind, semantic identifier
    /// and type tokens - so that a filter is decidable by inspection and two
    /// implementations cannot disagree about what it selects.
    /// </remarks>
    public sealed class WotProjectionFilter
    {
        /// <summary>
        /// Gets the affordance kind the filter admits, or
        /// <see cref="WotAffordanceKind.Any"/> when it does not constrain it.
        /// </summary>
        public WotAffordanceKind AffordanceKind { get; init; } = WotAffordanceKind.Any;

        /// <summary>
        /// Gets the semantic identifier an affordance must carry, or
        /// <c>null</c> when the filter does not constrain it.
        /// </summary>
        public string? SemanticId { get; init; }

        /// <summary>
        /// Gets the type tokens an affordance must carry.
        /// </summary>
        /// <remarks>
        /// An affordance matches only when it carries every listed value.
        /// </remarks>
        public ArrayOf<string> TypeTokens { get; init; }
    }

    /// <summary>
    /// One entry of a projection document's <c>uav:projects</c> manifest.
    /// </summary>
    /// <remarks>
    /// See WoT Binding Section 12.2. A source names a document the view is
    /// assembled from; it never edits, wraps or copies it, and the source is
    /// unaware that it is projected.
    /// </remarks>
    public sealed class WotProjectionSource
    {
        /// <summary>
        /// Gets the alias for this source, unique within the manifest.
        /// </summary>
        /// <remarks>
        /// Used for provenance and to qualify copied security scheme names as
        /// <c>&lt;sourceName&gt;_&lt;scheme name&gt;</c>.
        /// </remarks>
        public string SourceName { get; init; } = string.Empty;

        /// <summary>
        /// Gets the source document URI as authored, resolved against the
        /// projection document's base.
        /// </summary>
        public string Href { get; init; } = string.Empty;

        /// <summary>
        /// Gets the source media type, either <c>application/td+json</c> or
        /// <c>application/tm+json</c>.
        /// </summary>
        public string MediaType { get; init; } = string.Empty;

        /// <summary>
        /// Gets which endpoint consumers of this source's affordances address.
        /// </summary>
        public WotProjectionRouting Routing { get; init; } = WotProjectionRouting.Source;

        /// <summary>
        /// Gets the <c>sha-256:&lt;hex&gt;</c> digest pinning a specific source
        /// revision, or <c>null</c> when the source is not pinned.
        /// </summary>
        public string? SourceDigest { get; init; }

        /// <summary>
        /// Gets the prefix applied to bulk-selected affordance names, or
        /// <c>null</c> when names are taken unchanged.
        /// </summary>
        public string? NamePrefix { get; init; }

        /// <summary>
        /// Gets whether every affordance of the source is selected.
        /// </summary>
        public bool SelectAll { get; init; }

        /// <summary>
        /// Gets the predicate filters applied to the source.
        /// </summary>
        public ArrayOf<WotProjectionFilter> Filters { get; init; }
    }

    /// <summary>
    /// One enumerated selection of a projection document.
    /// </summary>
    /// <remarks>
    /// See WoT Binding Section 12.3. An enumerated selection names exactly one
    /// affordance through <c>tm:ref</c> and is the only form that can annotate
    /// the definition it selects.
    /// </remarks>
    public sealed class WotProjectionReference
    {
        /// <summary>
        /// Gets the affordance kind the member was declared under.
        /// </summary>
        public WotAffordanceKind AffordanceKind { get; init; }

        /// <summary>
        /// Gets the member key, which is the affordance's name in the view.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Gets the <c>tm:ref</c> value exactly as authored.
        /// </summary>
        public string Reference { get; init; } = string.Empty;

        /// <summary>
        /// Gets the members written alongside <c>tm:ref</c>, which override the
        /// referenced definition when the view is resolved.
        /// </summary>
        public JsonElement Annotations { get; init; }
    }
}
