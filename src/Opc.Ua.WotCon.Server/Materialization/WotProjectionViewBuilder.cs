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
using System.Security.Cryptography;
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
        /// <param name="serverNamespaceUris">
        /// The server's namespace table, used to write a member's NodeId in the
        /// portable ExpandedNodeId form the <c>ViewVersion</c> algorithm of
        /// <i>OPC UA — WoT Binding</i> §12.6 is defined over.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="thingResolver"/> or <paramref name="nodeIndex"/> is
        /// <c>null</c>.
        /// </exception>
        public WotProjectionViewBuilder(
            IWotThingResolver thingResolver,
            IWotMaterializedNodeIndex nodeIndex,
            WotNodeSetConverterOptions? options = null,
            NamespaceTable? serverNamespaceUris = null)
        {
            m_thingResolver = thingResolver ??
                throw new ArgumentNullException(nameof(thingResolver));
            m_nodeIndex = nodeIndex ?? throw new ArgumentNullException(nameof(nodeIndex));
            m_options = options ?? new WotNodeSetConverterOptions();
            m_options.Validate();
            m_serverNamespaceUris = serverNamespaceUris;
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
            var intermediates = new Dictionary<string, WotDocument?>(StringComparer.Ordinal);
            Membership root;
            try
            {
                root = await BuildNodeAsync(
                    projectionDocument, visited, resolvedGroups, intermediates, diagnostics,
                    context, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                foreach (KeyValuePair<string, WotDocument?> entry in intermediates)
                {
                    entry.Value?.Dispose();
                }
            }
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
            Dictionary<string, WotDocument?> intermediates,
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
                await CollectMembersAsync(
                    view.Properties, WotAffordanceKind.Property, members, omissions,
                    intermediates, context, cancellationToken).ConfigureAwait(false);
                await CollectMembersAsync(
                    view.Actions, WotAffordanceKind.Action, members, omissions,
                    intermediates, context, cancellationToken).ConfigureAwait(false);
                await CollectMembersAsync(
                    view.Events, WotAffordanceKind.Event, members, omissions,
                    intermediates, context, cancellationToken).ConfigureAwait(false);
            }

            var groups = new List<WotOrganizationalGroup>();
            WotProjection? projection = WotProjection.Parse(document, diagnostics);
            if (projection is not null && !projection.OrganizingLinks.IsNull)
            {
                for (int i = 0; i < projection.OrganizingLinks.Count; i++)
                {
                    WotOrganizingLink link = projection.OrganizingLinks[i];
                    await BuildGroupAsync(
                        link, visited, resolvedGroups, intermediates, groups, omissions,
                        diagnostics, context, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            return new Membership(members.ToArrayOf(), groups.ToArrayOf(), omissions.ToArrayOf());
        }

        private async ValueTask BuildGroupAsync(
            WotOrganizingLink link,
            HashSet<string> visited,
            Dictionary<string, Membership> resolvedGroups,
            Dictionary<string, WotDocument?> intermediates,
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
                        organized, visited, resolvedGroups, intermediates, diagnostics, context,
                        cancellationToken)
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

        private async ValueTask CollectMembersAsync(
            IReadOnlyDictionary<string, JsonElement> affordances,
            WotAffordanceKind kind,
            List<NodeId> members,
            List<string> omissions,
            Dictionary<string, WotDocument?> intermediates,
            WotResolutionContext context,
            CancellationToken cancellationToken)
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
                    (nodeId, href) = await ChaseAsync(
                        reference, intermediates, context, cancellationToken)
                        .ConfigureAwait(false);
                }
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

        /// <summary>
        /// Follows a selection whose source is itself a projection document to
        /// the Node its ultimate source materialized. <c>uav:resolvedFrom</c> is
        /// by <i>OPC UA — WoT Binding</i> §12 "the reference the selection was
        /// made by", so a projection that selects from a projection names the
        /// intermediate document — and an intermediate materializes a View, not
        /// Nodes, so the index cannot locate it. <i>OPC UA — WoT Connectivity</i>
        /// §7.13 resolves such a source depth-first and organizes the Nodes the
        /// ultimate sources materialized, which is what this walk recovers.
        /// </summary>
        /// <returns>
        /// The located Node and the href it was reached through, or
        /// <see cref="NodeId.Null"/> and the deepest href the walk reached when
        /// no source in the chain is materialized here.
        /// </returns>
        private async ValueTask<(NodeId NodeId, string Href)> ChaseAsync(
            WotMaterializedAffordanceRef reference,
            Dictionary<string, WotDocument?> intermediates,
            WotResolutionContext context,
            CancellationToken cancellationToken)
        {
            // The href chain is acyclic because the resolver validates the
            // projection source graph (§12.7); the depth bound is the same
            // defence the resolver applies and stops an unvalidated chain.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string href = reference.SourceHref;
            WotMaterializedAffordanceRef current = reference;
            for (int depth = 0; depth < context.Options.MaxDepth; depth++)
            {
                if (!seen.Add(current.SourceHref))
                {
                    return (NodeId.Null, href);
                }
                WotDocument? view = await ResolveIntermediateAsync(
                    current.SourceHref, intermediates, context, cancellationToken)
                    .ConfigureAwait(false);
                if (view is null)
                {
                    return (NodeId.Null, href);
                }
                if (!TryGetAffordance(view, current.Kind, current.AffordanceName,
                        out JsonElement affordance) ||
                    !TryReadResolvedFrom(
                        affordance, out string nextHref, out WotAffordanceKind nextKind,
                        out string nextName))
                {
                    return (NodeId.Null, href);
                }
                current = new WotMaterializedAffordanceRef(
                    nextHref, nextKind, nextName, ReadAuthoredId(affordance));
                href = nextHref;
                NodeId located = m_nodeIndex.Locate(current);
                if (!located.IsNull)
                {
                    return (located, href);
                }
            }
            return (NodeId.Null, href);
        }

        /// <summary>
        /// Resolves an intermediate projection document to its resolved view,
        /// once per href. A document that is absent, unparsable or not a
        /// projection caches as <c>null</c> so the walk stops there and does not
        /// re-fetch it for every affordance that names it.
        /// </summary>
        private async ValueTask<WotDocument?> ResolveIntermediateAsync(
            string href,
            Dictionary<string, WotDocument?> intermediates,
            WotResolutionContext context,
            CancellationToken cancellationToken)
        {
            if (intermediates.TryGetValue(href, out WotDocument? cached))
            {
                return cached;
            }
            intermediates[href] = null;
            WotDocument? source = await FetchAsync(href, context, cancellationToken)
                .ConfigureAwait(false);
            if (source is null)
            {
                return null;
            }
            using (source)
            {
                if (!WotProjection.IsProjection(source))
                {
                    return null;
                }
                WotConversionResult<WotDocument> resolved = await m_resolver
                    .ResolveAsync(source, context, cancellationToken)
                    .ConfigureAwait(false);
                if (!resolved.Success || resolved.Value is null)
                {
                    resolved.Value?.Dispose();
                    return null;
                }
                intermediates[href] = resolved.Value;
                return resolved.Value;
            }
        }

        private static bool TryGetAffordance(
            WotDocument view,
            WotAffordanceKind kind,
            string name,
            out JsonElement affordance)
        {
            IReadOnlyDictionary<string, JsonElement> affordances = kind switch
            {
                WotAffordanceKind.Action => view.Actions,
                WotAffordanceKind.Event => view.Events,
                _ => view.Properties
            };
            return affordances.TryGetValue(name, out affordance);
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

        /// <summary>
        /// Computes <c>ViewVersion</c> exactly as <i>OPC UA — WoT Binding</i>
        /// §12.6 specifies: take the ExpandedNodeId of each resolved member in
        /// the portable form of §5.1.1, remove duplicates, sort those strings
        /// ascending by Unicode code point, then for each write its length in
        /// UTF-8 octets as decimal digits, a colon, the string, and U+000A;
        /// encode as UTF-8 and take the first four octets of the SHA-256 digest
        /// as a big-endian <c>UInt32</c>. A value of zero is reported as one,
        /// because OPC 10000-3 §5.4 requires a <c>ViewVersion</c> greater than
        /// zero.
        /// </summary>
        /// <remarks>
        /// The membership is a <em>set</em>: a Node the view reaches through
        /// more than one organized group (§12.7) is one member of the View and
        /// contributes once, because a View <c>Organizes</c> a Node or it does
        /// not, and the same <c>Organizes</c> Reference is not created twice. A
        /// server that counted a shared Node twice would compute a different
        /// value from one that organized it under a single group, for the same
        /// View. The length prefix is what makes the encoding injective: a
        /// NodeId string identifier may itself contain U+000A, so joining on the
        /// separator alone would let one member embedding a newline serialize as
        /// the two members it imitates. Only the members are hashed. Groups and
        /// their names are deliberately not: the clause states that
        /// <c>ViewVersion</c> records what a View contains and not how it is
        /// arranged. A <c>UInt32</c> cannot separate every membership, so the
        /// clause admits that two different memberships may still compute the
        /// same value; a client treats inequality as proof that the membership
        /// changed and equality as evidence rather than proof.
        /// </remarks>
        private uint ComputeViewVersion(Membership root)
        {
            var collected = new List<string>();
            CollectPortableMembers(root, collected);

            // The set/sort/encode/digest algorithm itself is the shared one in
            // Opc.Ua.Types, which the published Annex G.3 and Section 12.6
            // vectors are run against directly. Recomputing it here would be a
            // second implementation of one formula, and the two would drift.
            return WotPortableIdentity.ComputeViewVersion(collected.ToArrayOf());
        }

        /// <summary>
        /// Collects every resolved member of the closure, including the members
        /// of nested groups, in the portable form of §5.1.1.
        /// </summary>
        private void CollectPortableMembers(Membership membership, List<string> members)
        {
            for (int i = 0; i < membership.Members.Count; i++)
            {
                members.Add(ToPortableForm(membership.Members[i]));
            }
            for (int i = 0; i < membership.Groups.Count; i++)
            {
                WotOrganizationalGroup group = membership.Groups[i];
                CollectPortableMembers(
                    new Membership(group.OrganizedNodeIds, group.Groups, ArrayOf<string>.Empty),
                    members);
            }
        }

        /// <summary>
        /// Writes a NodeId in the portable ExpandedNodeId form of §5.1.1:
        /// <c>nsu=&lt;NamespaceUri&gt;;&lt;idtype&gt;=&lt;id&gt;</c>, with the
        /// canonical namespace-0 form used unprefixed for a Node in the base
        /// namespace.
        /// </summary>
        private string ToPortableForm(NodeId nodeId)
        {
            if (nodeId.NamespaceIndex == 0)
            {
                return nodeId.ToString();
            }
            string? uri = m_serverNamespaceUris?.GetString(nodeId.NamespaceIndex);
            if (string.IsNullOrEmpty(uri))
            {
                // Defensive: a server that cannot name its own namespace cannot
                // produce the portable form. Stay deterministic rather than
                // throwing during a materialization.
                return nodeId.ToString();
            }
            var builder = new StringBuilder("nsu=").Append(uri).Append(';');
            NodeId.Format(
                CultureInfo.InvariantCulture,
                builder,
                nodeId.IdentifierAsString,
                nodeId.IdType,
                0);
            return builder.ToString();
        }

        private static byte[] Sha256(byte[] content)
        {
            // TODO: SHA256.HashData is only available on .NET 5+; this project
            // also targets net472/net48/netstandard2.x, where the instance
            // ComputeHash API is the portable equivalent.
#pragma warning disable CA1850
            using var sha = SHA256.Create();
            return sha.ComputeHash(content);
#pragma warning restore CA1850
        }

        private readonly IWotThingResolver m_thingResolver;
        private readonly IWotMaterializedNodeIndex m_nodeIndex;
        private readonly WotNodeSetConverterOptions m_options;
        private readonly WotProjectionResolver m_resolver;
        private readonly NamespaceTable? m_serverNamespaceUris;

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
