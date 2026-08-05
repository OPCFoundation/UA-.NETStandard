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
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Builds a <see cref="WotViewProjectionPlan"/> from a WoT <em>projection
    /// document</em> (WoT Binding Section 12): it resolves the document to its
    /// resolved view with <see cref="WotProjectionResolver"/>, maps each selected
    /// affordance to the OPC UA Node already materialized from its source with an
    /// <see cref="IWotMaterializedNodeIndex"/>, grows the organizational Objects
    /// from the document's <c>ua:Organizes</c> links (Section 12.7), and computes
    /// a deterministic <c>ViewVersion</c>. It creates no affordance Node: a
    /// projection selects, it never defines.
    /// </summary>
    /// <remarks>
    /// This is the address-space-mapping sense of "projection", distinct from the
    /// runtime-NodeSet-closure <see cref="WotProjectionDocument"/> built by the
    /// coordinator for ordinary affordance-bearing documents. The builder is pure:
    /// it performs no address-space mutation. An <see cref="IWotViewProjectionHost"/>
    /// applies the plan it returns.
    /// </remarks>
    public sealed class WotProjectionViewBuilder
    {
        /// <summary>
        /// Initializes a new builder.
        /// </summary>
        /// <param name="thingResolver">
        /// The resolver used to obtain the source and organized documents. In a
        /// server this resolves from the registry snapshot.
        /// </param>
        /// <param name="nodeIndex">
        /// The index that locates the Node already materialized for a selected
        /// source affordance.
        /// </param>
        /// <param name="options">The bounded conversion options, or <c>null</c>.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="thingResolver"/> or <paramref name="nodeIndex"/> is
        /// <c>null</c>.
        /// </exception>
        public WotProjectionViewBuilder(
            IWotThingResolver thingResolver,
            IWotMaterializedNodeIndex nodeIndex,
            WotNodeSetConverterOptions? options = null)
        {
            m_thingResolver = thingResolver ??
                throw new ArgumentNullException(nameof(thingResolver));
            m_nodeIndex = nodeIndex ?? throw new ArgumentNullException(nameof(nodeIndex));
            m_options = options ?? new WotNodeSetConverterOptions();
            m_options.Validate();
            m_resolver = new WotProjectionResolver(thingResolver, m_options);
        }

        /// <summary>
        /// Builds the View plan for a projection document.
        /// </summary>
        /// <param name="projectionDocument">The projection document.</param>
        /// <param name="resolutionContext">
        /// The resolution context that bounds organized-document fetches, or
        /// <c>null</c> to create one from the configured options.
        /// </param>
        /// <param name="cancellationToken">A token that cancels the operation.</param>
        /// <returns>The build result.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="projectionDocument"/> is <c>null</c>.
        /// </exception>
        public async ValueTask<WotViewProjectionResult> BuildAsync(
            WotDocument projectionDocument,
            WotResolutionContext? resolutionContext = null,
            CancellationToken cancellationToken = default)
        {
            if (projectionDocument is null)
            {
                throw new ArgumentNullException(nameof(projectionDocument));
            }

            var diagnostics = new List<WotDiagnostic>();
            if (!WotProjection.IsProjection(projectionDocument))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ValidationError,
                    "The document is not a projection document; it carries no " +
                    "uav:projection marker and materializes no View."));
                return new WotViewProjectionResult(null, diagnostics);
            }

            WotResolutionContext context = resolutionContext ?? new WotResolutionContext();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var resolvedGroups = new Dictionary<string, Membership>(StringComparer.Ordinal);
            Membership root = await BuildNodeAsync(
                projectionDocument, visited, resolvedGroups, diagnostics, context, cancellationToken)
                .ConfigureAwait(false);
            if (HasErrors(diagnostics))
            {
                return new WotViewProjectionResult(null, diagnostics);
            }

            string scenario = ReadScenario(projectionDocument);
            var plan = new WotViewProjectionPlan(
                scenario,
                projectionDocument.Kind,
                root.Members,
                root.Groups,
                ComputeViewVersion(root),
                root.Omissions);
            return new WotViewProjectionResult(plan, diagnostics);
        }

        private async ValueTask<Membership> BuildNodeAsync(
            WotDocument document,
            HashSet<string> visited,
            Dictionary<string, Membership> resolvedGroups,
            List<WotDiagnostic> diagnostics,
            WotResolutionContext context,
            CancellationToken cancellationToken)
        {
            WotConversionResult<WotDocument> resolved = await m_resolver
                .ResolveAsync(document, context, cancellationToken)
                .ConfigureAwait(false);
            for (int i = 0; i < resolved.Diagnostics.Count; i++)
            {
                diagnostics.Add(resolved.Diagnostics[i]);
            }
            if (!resolved.Success || resolved.Value is null)
            {
                return Membership.Empty;
            }

            var members = new List<NodeId>();
            var omissions = new List<string>();
            using (WotDocument view = resolved.Value)
            {
                CollectMembers(view.Properties, WotAffordanceKind.Property, members, omissions);
                CollectMembers(view.Actions, WotAffordanceKind.Action, members, omissions);
                CollectMembers(view.Events, WotAffordanceKind.Event, members, omissions);
            }

            var groups = new List<WotOrganizationalGroup>();
            WotProjection? projection = WotProjection.Parse(document, diagnostics);
            if (projection is not null && !projection.OrganizingLinks.IsNull)
            {
                for (int i = 0; i < projection.OrganizingLinks.Count; i++)
                {
                    WotOrganizingLink link = projection.OrganizingLinks[i];
                    await BuildGroupAsync(
                        link, visited, resolvedGroups, groups, omissions, diagnostics, context,
                        cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            return new Membership(members.ToArrayOf(), groups.ToArrayOf(), omissions.ToArrayOf());
        }

        private async ValueTask BuildGroupAsync(
            WotOrganizingLink link,
            HashSet<string> visited,
            Dictionary<string, Membership> resolvedGroups,
            List<WotOrganizationalGroup> groups,
            List<string> omissions,
            List<WotDiagnostic> diagnostics,
            WotResolutionContext context,
            CancellationToken cancellationToken)
        {
            // A document already built on another path is a DAG re-visit, not a
            // cycle. Reusing it keeps a diamond graph linear; expanding it again
            // per path is exponential in the depth of the ua:Organizes graph.
            if (resolvedGroups.TryGetValue(link.Href, out Membership cached))
            {
                groups.Add(new WotOrganizationalGroup(link.RefName, cached.Members, cached.Groups));
                for (int j = 0; j < cached.Omissions.Count; j++)
                {
                    omissions.Add(cached.Omissions[j]);
                }
                return;
            }
            // Defensive cycle guard. WotProjectionResolver.ResolveAsync already
            // validates the whole ua:Organizes graph is acyclic (Section 12.7),
            // so a re-visit here indicates a graph that was not validated.
            if (!visited.Add(link.Href))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.ProjectionCycle,
                    $"The ua:Organizes graph contains a cycle at '{link.Href}'.",
                    new WotLocation(reference: link.Href)));
                return;
            }
            try
            {
                WotDocument? organized = await FetchAsync(link.Href, context, cancellationToken)
                    .ConfigureAwait(false);
                if (organized is null)
                {
                    omissions.Add(
                        $"Organizing group '{link.RefName}' is omitted from the View: its " +
                        $"document '{link.Href}' is not in this address space.");
                    return;
                }
                using (organized)
                {
                    Membership child = await BuildNodeAsync(
                        organized, visited, resolvedGroups, diagnostics, context, cancellationToken)
                        .ConfigureAwait(false);
                    resolvedGroups[link.Href] = child;
                    groups.Add(new WotOrganizationalGroup(
                        link.RefName, child.Members, child.Groups));
                    for (int j = 0; j < child.Omissions.Count; j++)
                    {
                        omissions.Add(child.Omissions[j]);
                    }
                }
            }
            finally
            {
                visited.Remove(link.Href);
            }
        }

        private void CollectMembers(
            IReadOnlyDictionary<string, JsonElement> affordances,
            WotAffordanceKind kind,
            List<NodeId> members,
            List<string> omissions)
        {
            foreach (KeyValuePair<string, JsonElement> entry in affordances)
            {
                if (!TryReadResolvedFrom(
                        entry.Value, out string href, out WotAffordanceKind sourceKind,
                        out string sourceName))
                {
                    continue;
                }
                ExpandedNodeId authoredId = ReadAuthoredId(entry.Value);
                var reference = new WotMaterializedAffordanceRef(
                    href, sourceKind, sourceName, authoredId);
                NodeId nodeId = m_nodeIndex.Locate(reference);
                if (nodeId.IsNull)
                {
                    omissions.Add(
                        $"Affordance '{entry.Key}' ({kind}) is omitted from the View: its " +
                        $"source '{href}' is not materialized in this address space.");
                    continue;
                }
                if (!members.Contains(nodeId))
                {
                    members.Add(nodeId);
                }
            }
        }

        private async ValueTask<WotDocument?> FetchAsync(
            string href,
            WotResolutionContext context,
            CancellationToken cancellationToken)
        {
            WotResolverResult result = await m_thingResolver
                .ResolveThingAsync(href, context, cancellationToken)
                .ConfigureAwait(false);
            if (!result.Found)
            {
                return null;
            }
            try
            {
                return WotDocument.Parse(result.Content, m_options);
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                return null;
            }
        }

        private static string ReadScenario(WotDocument document)
        {
            return document.TryGetUav("scenario", out JsonElement scenario) &&
                scenario.ValueKind == JsonValueKind.String
                ? scenario.GetString() ?? string.Empty
                : string.Empty;
        }

        private static bool TryReadResolvedFrom(
            JsonElement affordance,
            out string href,
            out WotAffordanceKind kind,
            out string name)
        {
            href = string.Empty;
            kind = WotAffordanceKind.Property;
            name = string.Empty;
            if (affordance.ValueKind != JsonValueKind.Object ||
                !affordance.TryGetProperty("uav:resolvedFrom", out JsonElement provenance) ||
                provenance.ValueKind != JsonValueKind.String)
            {
                return false;
            }
            string value = provenance.GetString() ?? string.Empty;
            if (value.Length == 0)
            {
                return false;
            }
            int hash = value.AsSpan().IndexOf('#');
            string fragment;
            if (hash < 0)
            {
                href = value;
                return href.Length != 0;
            }
            href = value[..hash];
            fragment = value[(hash + 1)..].TrimStart('/');
            int slash = fragment.AsSpan().IndexOf('/');
            if (slash < 0)
            {
                name = UnescapePointer(fragment);
                return true;
            }
            string kindToken = fragment[..slash];
            name = UnescapePointer(fragment[(slash + 1)..]);
            kind = kindToken switch
            {
                "properties" => WotAffordanceKind.Property,
                "actions" => WotAffordanceKind.Action,
                "events" => WotAffordanceKind.Event,
                _ => WotAffordanceKind.Property
            };
            return true;
        }

        private static ExpandedNodeId ReadAuthoredId(JsonElement affordance)
        {
            if (affordance.ValueKind != JsonValueKind.Object ||
                !affordance.TryGetProperty("uav:id", out JsonElement id) ||
                id.ValueKind != JsonValueKind.String)
            {
                return ExpandedNodeId.Null;
            }
            string value = id.GetString() ?? string.Empty;
            if (value.Length == 0)
            {
                return ExpandedNodeId.Null;
            }
            try
            {
                return ExpandedNodeId.Parse(value);
            }
            catch (ServiceResultException)
            {
                return ExpandedNodeId.Null;
            }
        }

        private static string UnescapePointer(string token)
        {
            if (!token.Contains('~', StringComparison.Ordinal))
            {
                return token;
            }
            return token
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
        }

        private static bool HasErrors(List<WotDiagnostic> diagnostics)
        {
            foreach (WotDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Severity == WotDiagnosticSeverity.Error)
                {
                    return true;
                }
            }
            return false;
        }

        private static uint ComputeViewVersion(Membership root)
        {
            var builder = new StringBuilder("wot:view;");
            AppendMembership(builder, root);
            return Fnv1A(builder.ToString());
        }

        private static void AppendMembership(StringBuilder builder, Membership membership)
        {
            var ordered = new List<string>(membership.Members.Count);
            for (int i = 0; i < membership.Members.Count; i++)
            {
                ordered.Add(membership.Members[i].ToString());
            }
            ordered.Sort(StringComparer.Ordinal);
            foreach (string id in ordered)
            {
                AppendToken(builder, id);
            }
            builder.Append('#');
            var groups = new List<WotOrganizationalGroup>(membership.Groups.Count);
            for (int i = 0; i < membership.Groups.Count; i++)
            {
                groups.Add(membership.Groups[i]);
            }
            groups.Sort(CompareGroups);
            foreach (WotOrganizationalGroup group in groups)
            {
                AppendToken(builder, group.RefName);
                builder.Append('{');
                AppendMembership(
                    builder, new Membership(group.OrganizedNodeIds, group.Groups, ArrayOf<string>.Empty));
                builder.Append('}');
            }
        }

        /// <summary>
        /// Appends a length-prefixed token. A NodeId string identifier and a
        /// <c>uav:refName</c> are both authored input and may contain any of the
        /// delimiters used here, so appending them raw would not be injective:
        /// the members <c>ns=2;s=A</c> and <c>ns=2;s=B</c> would serialize the
        /// same as the single member whose identifier is <c>A;ns=2;s=B</c>, and
        /// the two memberships would share a ViewVersion.
        /// </summary>
        private static void AppendToken(StringBuilder builder, string value)
        {
            builder.Append(value.Length).Append(':').Append(value).Append(';');
        }

        /// <summary>
        /// Orders groups by <c>uav:refName</c> and then by their own canonical
        /// serialization. <c>uav:refName</c> is optional and defaults to the
        /// empty string, so it is not unique on its own, and
        /// <c>List&lt;T&gt;.Sort</c> is not stable - ordering on it alone would
        /// let two authorings of the same membership hash differently.
        /// </summary>
        private static int CompareGroups(WotOrganizationalGroup a, WotOrganizationalGroup b)
        {
            int byName = string.CompareOrdinal(a.RefName, b.RefName);
            if (byName != 0)
            {
                return byName;
            }
            var left = new StringBuilder();
            AppendMembership(left, new Membership(a.OrganizedNodeIds, a.Groups, ArrayOf<string>.Empty));
            var right = new StringBuilder();
            AppendMembership(right, new Membership(b.OrganizedNodeIds, b.Groups, ArrayOf<string>.Empty));
            return string.CompareOrdinal(left.ToString(), right.ToString());
        }

        private static uint Fnv1A(string value)
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;
            uint hash = offsetBasis;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            foreach (byte b in bytes)
            {
                hash ^= b;
                hash *= prime;
            }
            return hash;
        }

        private readonly IWotThingResolver m_thingResolver;
        private readonly IWotMaterializedNodeIndex m_nodeIndex;
        private readonly WotNodeSetConverterOptions m_options;
        private readonly WotProjectionResolver m_resolver;

        /// <summary>
        /// The resolved membership of one projection document node: the Nodes it
        /// directly organizes, its nested organizational groups, and the notes
        /// for affordances that were omitted.
        /// </summary>
        private readonly struct Membership
        {
            public Membership(
                ArrayOf<NodeId> members,
                ArrayOf<WotOrganizationalGroup> groups,
                ArrayOf<string> omissions)
            {
                Members = members.IsNull ? ArrayOf<NodeId>.Empty : members;
                Groups = groups.IsNull ? ArrayOf<WotOrganizationalGroup>.Empty : groups;
                Omissions = omissions.IsNull ? ArrayOf<string>.Empty : omissions;
            }

            public static Membership Empty { get; } = new Membership(
                ArrayOf<NodeId>.Empty, ArrayOf<WotOrganizationalGroup>.Empty, ArrayOf<string>.Empty);

            public ArrayOf<NodeId> Members { get; }

            public ArrayOf<WotOrganizationalGroup> Groups { get; }

            public ArrayOf<string> Omissions { get; }
        }
    }
}
