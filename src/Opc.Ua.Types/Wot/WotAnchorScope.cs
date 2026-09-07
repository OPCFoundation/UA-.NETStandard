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
 *
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

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The anchor a relative <c>uav:browsePath</c> resolves against, tracked
    /// down the scopes of a document as WoT Binding Section 5.1.4 states it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The clause names two sources in order: the nearest enclosing
    /// <c>uav:browsePathAnchor</c>, and failing that the nearest enclosing
    /// <c>uav:id</c> - the Node the document or affordance describes. The two
    /// are tracked separately rather than collapsed on the way down, because
    /// the order is over <em>kinds</em> and not over depth: an anchor stated at
    /// the root outranks a <c>uav:id</c> stated on the affordance beneath it,
    /// while an anchor stated on that affordance outranks the one at the root.
    /// </para>
    /// <para>
    /// A relative path with neither is invalid. The clause is explicit that a
    /// consumer shall not fall back to the AddressSpace root, because that
    /// resolves the same text against a different Node.
    /// </para>
    /// </remarks>
    /// <param name="Anchor">
    /// The nearest enclosing <c>uav:browsePathAnchor</c>, or <c>null</c>.
    /// </param>
    /// <param name="Identity">
    /// The nearest enclosing <c>uav:id</c>, or <c>null</c>.
    /// </param>
    internal readonly record struct WotAnchorScope(string? Anchor, string? Identity)
    {
        /// <summary>
        /// The scope of a document that has stated nothing yet.
        /// </summary>
        public static WotAnchorScope None { get; }

        /// <summary>
        /// Gets the Node a relative browse path in this scope resolves
        /// against, or <c>null</c> where the scope anchors nothing.
        /// </summary>
        public string? Effective => Anchor ?? Identity;

        /// <summary>
        /// Gets whether a relative browse path in this scope has a starting
        /// Node.
        /// </summary>
        public bool IsAnchored => !string.IsNullOrEmpty(Effective);

        /// <summary>
        /// Enters a nested scope that states the given terms, letting each
        /// term seen here replace the one inherited from outside.
        /// </summary>
        /// <param name="anchor">
        /// The scope's own <c>uav:browsePathAnchor</c>, or <c>null</c>.
        /// </param>
        /// <param name="identity">
        /// The scope's own <c>uav:id</c>, or <c>null</c>.
        /// </param>
        /// <returns>The scope a member of this one resolves in.</returns>
        public WotAnchorScope Enter(string? anchor, string? identity)
        {
            return new WotAnchorScope(
                string.IsNullOrEmpty(anchor) ? Anchor : anchor,
                string.IsNullOrEmpty(identity) ? Identity : identity);
        }

        /// <summary>
        /// Enters the scope a JSON object states.
        /// </summary>
        /// <param name="element">The object whose members state the terms.</param>
        /// <returns>The scope a member of that object resolves in.</returns>
        public WotAnchorScope Enter(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return this;
            }
            return Enter(ReadTerm(element, AnchorTerm), ReadTerm(element, IdentityTerm));
        }

        /// <summary>
        /// The term that states what a relative browse path resolves against.
        /// </summary>
        public const string AnchorTerm = "uav:browsePathAnchor";

        /// <summary>
        /// The term that identifies the Node a scope describes, which anchors
        /// a relative browse path where no anchor is stated.
        /// </summary>
        public const string IdentityTerm = "uav:id";

        /// <summary>
        /// Reads one of the two terms, treating anything that is not a
        /// non-empty string as unstated.
        /// </summary>
        /// <param name="element">The object to read from.</param>
        /// <param name="term">The term to read.</param>
        /// <returns>The stated value, or <c>null</c>.</returns>
        public static string? ReadTerm(JsonElement element, string term)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(term, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String &&
                value.GetString() is { Length: > 0 } stated)
            {
                return stated;
            }
            return null;
        }

        /// <summary>
        /// Reads one of the two terms from a mutable document, by the same rule
        /// the immutable form uses, so a projection and a validation cannot
        /// disagree about what a scope states.
        /// </summary>
        /// <param name="element">The object to read from.</param>
        /// <param name="term">The term to read.</param>
        /// <returns>The stated value, or <c>null</c>.</returns>
        public static string? ReadTerm(JsonObject element, string term)
        {
            if (element is not null &&
                element.TryGetPropertyValue(term, out JsonNode? node) &&
                node is JsonValue value &&
                value.TryGetValue(out string? stated) &&
                stated is { Length: > 0 })
            {
                return stated;
            }
            return null;
        }
    }
}
