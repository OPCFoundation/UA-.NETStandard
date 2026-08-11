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
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// A reference to one selected source affordance whose already-materialized
    /// OPC UA Node an <see cref="IWotMaterializedNodeIndex"/> resolves. It is
    /// read from the resolved view of a projection document: the source
    /// <c>href</c> and affordance identity come from <c>uav:resolvedFrom</c>, and
    /// <see cref="AuthoredId"/> from the affordance's <c>uav:id</c> when present.
    /// </summary>
    public readonly struct WotMaterializedAffordanceRef : IEquatable<WotMaterializedAffordanceRef>
    {
        /// <summary>
        /// Initializes a new affordance reference.
        /// </summary>
        /// <param name="sourceHref">The source document href the affordance was resolved from.</param>
        /// <param name="kind">The affordance kind.</param>
        /// <param name="affordanceName">The source affordance name.</param>
        /// <param name="authoredId">
        /// The authored, portable <c>uav:id</c> of the affordance, or
        /// <see cref="ExpandedNodeId.Null"/> when the source affordance declares
        /// none.
        /// </param>
        public WotMaterializedAffordanceRef(
            string sourceHref,
            WotAffordanceKind kind,
            string affordanceName,
            ExpandedNodeId authoredId)
        {
            SourceHref = sourceHref ?? string.Empty;
            Kind = kind;
            AffordanceName = affordanceName ?? string.Empty;
            AuthoredId = authoredId.IsNull ? ExpandedNodeId.Null : authoredId;
        }

        /// <summary>
        /// Gets the source document href the affordance was resolved from.
        /// </summary>
        public string SourceHref { get; }

        /// <summary>
        /// Gets the affordance kind.
        /// </summary>
        public WotAffordanceKind Kind { get; }

        /// <summary>
        /// Gets the source affordance name.
        /// </summary>
        public string AffordanceName { get; }

        /// <summary>
        /// Gets the authored, portable <c>uav:id</c> of the affordance. It is
        /// the authoritative locator when set; test it with <c>.IsNull</c>.
        /// </summary>
        public ExpandedNodeId AuthoredId { get; }

        /// <inheritdoc/>
        public bool Equals(WotMaterializedAffordanceRef other)
        {
            return Kind == other.Kind &&
                string.Equals(SourceHref, other.SourceHref, StringComparison.Ordinal) &&
                string.Equals(AffordanceName, other.AffordanceName, StringComparison.Ordinal) &&
                AuthoredId == other.AuthoredId;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is WotMaterializedAffordanceRef other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                SourceHref, (int)Kind, AffordanceName, AuthoredId);
        }

        /// <summary>
        /// Tests two references for equality.
        /// </summary>
        public static bool operator ==(
            WotMaterializedAffordanceRef left, WotMaterializedAffordanceRef right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Tests two references for inequality.
        /// </summary>
        public static bool operator !=(
            WotMaterializedAffordanceRef left, WotMaterializedAffordanceRef right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Locates the OPC UA Node already materialized for a selected source
    /// affordance of a projection document. A projection <c>Organizes</c> Nodes
    /// that were materialized from their own source documents; it never
    /// re-materializes them. When the affordance's source is not in this
    /// server's address space (for example a cross-server federation source) the
    /// index returns <see cref="NodeId.Null"/> so the affordance is omitted from
    /// the View rather than failing the load.
    /// </summary>
    public interface IWotMaterializedNodeIndex
    {
        /// <summary>
        /// Locates the already-materialized Node for a selected source
        /// affordance, or returns <see cref="NodeId.Null"/> when the source is
        /// not in this address space.
        /// </summary>
        /// <param name="affordance">The selected source affordance.</param>
        /// <returns>The materialized NodeId, or <see cref="NodeId.Null"/>.</returns>
        NodeId Locate(in WotMaterializedAffordanceRef affordance);
    }

    /// <summary>
    /// The registry-snapshot-backed <see cref="IWotMaterializedNodeIndex"/>. It
    /// resolves a selected affordance's source document through the same
    /// registry bookkeeping the dependency graph uses, then maps the affordance
    /// to the Node already materialized from that source: by the affordance's
    /// authored, portable <c>uav:id</c> when present (the authoritative locator),
    /// otherwise by the converter's deterministic generated NodeId scheme anchored
    /// at the source's materialized root Node. A source that is not present in the
    /// supplied materialized-root map is treated as out-of-address-space and
    /// yields <see cref="NodeId.Null"/>.
    /// </summary>
    public sealed class WotMaterializedNodeIndex : IWotMaterializedNodeIndex
    {
        /// <summary>
        /// Initializes a new index over the supplied materialized-root map.
        /// </summary>
        /// <param name="snapshot">The registry snapshot the sources resolve from.</param>
        /// <param name="serverNamespaceUris">
        /// The server namespace table used to map a portable <c>uav:id</c> to a
        /// server NodeId.
        /// </param>
        /// <param name="sourceRootsByXid">
        /// The map from a source resource's registry Xid to the root Node it was
        /// materialized to. A source absent from this map is treated as not in
        /// this address space.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="snapshot"/>, <paramref name="serverNamespaceUris"/> or
        /// <paramref name="sourceRootsByXid"/> is <c>null</c>.
        /// </exception>
        public WotMaterializedNodeIndex(
            WotRegistrySnapshot snapshot,
            NamespaceTable serverNamespaceUris,
            IReadOnlyDictionary<string, NodeId> sourceRootsByXid)
        {
            m_snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            m_serverNamespaceUris = serverNamespaceUris ??
                throw new ArgumentNullException(nameof(serverNamespaceUris));
            m_sourceRootsByXid = sourceRootsByXid ??
                throw new ArgumentNullException(nameof(sourceRootsByXid));
        }

        /// <inheritdoc/>
        public NodeId Locate(in WotMaterializedAffordanceRef affordance)
        {
            WotResource? source = WotDependencyGraph.Resolve(m_snapshot, affordance.SourceHref);
            if (source is null)
            {
                return NodeId.Null;
            }
            if (!m_sourceRootsByXid.TryGetValue(source.Xid, out NodeId sourceRoot) ||
                sourceRoot.IsNull)
            {
                return NodeId.Null;
            }
            if (!affordance.AuthoredId.IsNull)
            {
                NodeId byId = ExpandedNodeId.ToNodeId(affordance.AuthoredId, m_serverNamespaceUris);
                // uav:id is authored input and a projection may carry its own,
                // so an unchecked value would let a View Organizes any Node in
                // the address space. A projection only ever reaches Nodes that
                // were materialized from the source it names.
                if (!byId.IsNull && IsUnderSourceRoot(byId, sourceRoot))
                {
                    return byId;
                }
            }
            if (sourceRoot.IdType == IdType.String &&
                affordance.AffordanceName.Length != 0)
            {
                string rootLocal = sourceRoot.IdentifierAsString;
                if (rootLocal.Length != 0)
                {
                    return new NodeId(
                        rootLocal + "/" + affordance.AffordanceName, sourceRoot.NamespaceIndex);
                }
            }
            return NodeId.Null;
        }

        /// <summary>
        /// Tests that a Node is the source root or sits beneath it. Membership
        /// is decided on the namespace and the identifier prefix: a candidate
        /// extends the root when the character that follows the prefix is a
        /// delimiter rather than a continuation of the name. The delimiter is
        /// not fixed to one character because a NodeSet chooses its own — the
        /// converter synthesizes <c>/</c> and a companion-model NodeSet
        /// conventionally uses <c>.</c> — while requiring a non-alphanumeric
        /// keeps the guard tight: <c>Pump1</c> does not match <c>Pump10</c> or
        /// <c>Pump1Extra</c>.
        /// </summary>
        private static bool IsUnderSourceRoot(NodeId candidate, NodeId sourceRoot)
        {
            if (candidate.NamespaceIndex != sourceRoot.NamespaceIndex)
            {
                return false;
            }
            if (candidate == sourceRoot)
            {
                return true;
            }
            if (candidate.IdType != IdType.String || sourceRoot.IdType != IdType.String)
            {
                return false;
            }
            string root = sourceRoot.IdentifierAsString;
            string local = candidate.IdentifierAsString;
            return root.Length != 0 &&
                local.Length > root.Length &&
                !char.IsLetterOrDigit(local[root.Length]) &&
                local.StartsWith(root, StringComparison.Ordinal);
        }

        private readonly WotRegistrySnapshot m_snapshot;
        private readonly NamespaceTable m_serverNamespaceUris;
        private readonly IReadOnlyDictionary<string, NodeId> m_sourceRootsByXid;
    }
}
