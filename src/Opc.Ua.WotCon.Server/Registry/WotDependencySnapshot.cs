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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Linq;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.XRegistry;

namespace Opc.Ua.WotCon.Server.Registry
{
    /// <summary>
    /// An immutable configured registry origin, independent of document URI aliases.
    /// </summary>
    public sealed class WotRegistryOrigin
    {
        /// <summary>
        /// Initializes one of the two authoritative xRegistry origin forms.
        /// </summary>
        /// <param name="originUri">The origin URI used to initialize the instance.</param>
        /// <param name="serverUri">The server URI used to initialize the instance.</param>
        /// <param name="registryNodeId">The registry node ID used to initialize the instance.</param>
        public WotRegistryOrigin(string originUri, string serverUri = "", ExpandedNodeId registryNodeId = default)
        {
            _ = originUri ?? throw new ArgumentNullException(nameof(originUri));
            _ = serverUri ?? throw new ArgumentNullException(nameof(serverUri));
            bool uriForm = WotRegistryIdentity.IsAbsoluteUri(originUri) &&
                serverUri.Length == 0 && registryNodeId.IsNull;
            bool nodeForm = originUri.Length == 0 && WotRegistryIdentity.IsAbsoluteUri(serverUri) &&
                !registryNodeId.IsNull && !string.IsNullOrEmpty(registryNodeId.NamespaceUri);
            if (!uriForm && !nodeForm)
            {
                throw new ArgumentException("A registry origin must have exactly one complete authoritative form.");
            }
            OriginUri = originUri;
            ServerUri = serverUri;
            RegistryNodeId = registryNodeId;
        }

        /// <summary>
        /// Gets the configured stable origin URI, or empty for the application/root form.
        /// </summary>
        public string OriginUri { get; }

        /// <summary>
        /// Gets the trusted ApplicationUri, or empty for the stable URI form.
        /// </summary>
        public string ServerUri { get; }

        /// <summary>
        /// Gets the portable registry-root NodeId.
        /// </summary>
        public ExpandedNodeId RegistryNodeId { get; }

        /// <summary>
        /// Creates a caller-owned value using the existing xRegistry model contract.
        /// </summary>
        public RegistryOriginDataType ToDataType()
        {
            return new RegistryOriginDataType
            {
                OriginUri = OriginUri,
                ServerUri = ServerUri,
                RegistryNodeId = RegistryNodeId
            };
        }
    }

    /// <summary>
    /// The immutable identity and content pin of one resolved dependency edge.
    /// </summary>
    public sealed class WotDependencyTargetPin
    {
        /// <summary>
        /// Initializes an exact target pin.
        /// </summary>
        /// <param name="edgeIndex">The edge index used to initialize the instance.</param>
        /// <param name="originRegistry">The origin registry used to initialize the instance.</param>
        /// <param name="versionXid">The version xid used to initialize the instance.</param>
        /// <param name="documentUri">The document URI used to initialize the instance.</param>
        /// <param name="versionNodeId">The version node ID used to initialize the instance.</param>
        /// <param name="contentDigest">The content digest used to initialize the instance.</param>
        public WotDependencyTargetPin(
            uint edgeIndex,
            WotRegistryOrigin? originRegistry,
            string versionXid,
            string documentUri,
            ExpandedNodeId versionNodeId,
            ByteString contentDigest)
        {
            _ = versionXid ?? throw new ArgumentNullException(nameof(versionXid));
            _ = documentUri ?? throw new ArgumentNullException(nameof(documentUri));
            if (contentDigest.Length != 32 ||
                (originRegistry is null && (versionXid.Length != 0 || !versionNodeId.IsNull)) ||
                (originRegistry is not null && versionXid.Length == 0))
            {
                throw new ArgumentException("A dependency target requires a complete origin-scoped content pin.");
            }
            EdgeIndex = edgeIndex;
            OriginRegistry = originRegistry;
            VersionXid = versionXid;
            DocumentUri = documentUri;
            VersionNodeId = versionNodeId;
            m_contentDigest = ByteString.From(contentDigest.Span.ToArray());
        }

        /// <summary>
        /// Gets the corresponding resolved edge index.
        /// </summary>
        public uint EdgeIndex { get; }

        /// <summary>
        /// Gets the registry origin, or null for a non-registry provider document.
        /// </summary>
        public WotRegistryOrigin? OriginRegistry { get; }

        /// <summary>
        /// Gets the origin-relative exact Version Xid.
        /// </summary>
        public string VersionXid { get; }

        /// <summary>
        /// Gets the actual document identity/location, not fetch permission.
        /// </summary>
        public string DocumentUri { get; }

        /// <summary>
        /// Gets the portable exact-Version NodeId, never a registry-root NodeId.
        /// </summary>
        public ExpandedNodeId VersionNodeId { get; }

        /// <summary>
        /// Gets the original bytes' complete SHA-256 digest.
        /// </summary>
        public ByteString ContentDigest => ByteString.From(m_contentDigest.Span.ToArray());

        /// <summary>
        /// Converts the pinned dependency target to its OPC UA data representation, including an origin when present.
        /// </summary>
        internal WoTResolvedDependencyTargetDataType ToDataType()
        {
            var result = new WoTResolvedDependencyTargetDataType
            {
                EdgeIndex = EdgeIndex,
                VersionXid = VersionXid,
                DocumentUri = DocumentUri,
                VersionNodeId = VersionNodeId,
                ContentDigest = ContentDigest
            };
            if (OriginRegistry is not null)
            {
                result.OriginRegistry = OriginRegistry.ToDataType();
                result.EncodingMask = (uint)WoTResolvedDependencyTargetDataTypeFields.OriginRegistry;
            }
            return result;
        }

        private readonly ByteString m_contentDigest;
    }

    /// <summary>
    /// An immutable committed graph or completed actual dependency attempt, owned by
    /// an exact Version in the existing registry snapshot.
    /// </summary>
    public sealed class WotDependencySnapshot
    {
        /// <summary>
        /// Initializes a complete typed dependency observation.
        /// </summary>
        /// <param name="sourceVersionId">The source version ID used to initialize the instance.</param>
        /// <param name="generation">The generation used to initialize the instance.</param>
        /// <param name="requestId">The request ID used to initialize the instance.</param>
        /// <param name="resolvedAt">The resolved at used to initialize the instance.</param>
        /// <param name="isCommitted">The committed flag used to initialize <c>WotDependencySnapshot</c>.</param>
        /// <param name="effectiveInputDigest">The effective input digest used to initialize the instance.</param>
        /// <param name="edges">The edges used to initialize the instance.</param>
        /// <param name="targets">The targets used to initialize the instance.</param>
        public WotDependencySnapshot(
            string sourceVersionId,
            uint generation,
            string requestId,
            DateTime resolvedAt,
            bool isCommitted,
            ByteString effectiveInputDigest,
            ArrayOf<WotDependency> edges,
            ArrayOf<WotDependencyTargetPin> targets)
            : this(sourceVersionId, generation, requestId, resolvedAt, isCommitted, effectiveInputDigest,
                new ValidatedGraph(edges, targets))
        {
        }

        /// <summary>
        /// Initializes a new <c>WotDependencySnapshot</c> instance.
        /// </summary>
        /// <param name="sourceVersionId">The source version ID used to initialize the instance.</param>
        /// <param name="generation">The generation used to initialize the instance.</param>
        /// <param name="requestId">The request ID used to initialize the instance.</param>
        /// <param name="resolvedAt">The resolved at used to initialize the instance.</param>
        /// <param name="isCommitted">The committed flag used to initialize <c>WotDependencySnapshot</c>.</param>
        /// <param name="effectiveInputDigest">The effective input digest used to initialize the instance.</param>
        /// <param name="graph">The graph used to initialize the instance.</param>
        internal WotDependencySnapshot(
            string sourceVersionId,
            uint generation,
            string requestId,
            DateTime resolvedAt,
            bool isCommitted,
            ByteString effectiveInputDigest,
            ValidatedGraph graph)
        {
            _ = sourceVersionId ?? throw new ArgumentNullException(nameof(sourceVersionId));
            _ = requestId ?? throw new ArgumentNullException(nameof(requestId));
            if ((isCommitted && effectiveInputDigest.Length != 32) ||
                (effectiveInputDigest.Length != 0 && effectiveInputDigest.Length != 32))
            {
                throw new ArgumentException("A committed dependency snapshot requires a complete input fingerprint.");
            }
            SourceVersionId = sourceVersionId;
            Generation = generation;
            RequestId = requestId;
            ResolvedAt = resolvedAt;
            IsCommitted = isCommitted;
            m_effectiveInputDigest = ByteString.From(effectiveInputDigest.Span.ToArray());
            Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        }

        /// <summary>
        /// Gets the exact source Version.
        /// </summary>
        public string SourceVersionId { get; }

        /// <summary>
        /// Gets the actual committed refresh generation at this observation.
        /// </summary>
        public uint Generation { get; }

        /// <summary>
        /// Gets the originating request identifier.
        /// </summary>
        public string RequestId { get; }

        /// <summary>
        /// Gets when the actual dependency attempt completed.
        /// </summary>
        public DateTime ResolvedAt { get; }

        /// <summary>
        /// Gets whether this graph accompanied successful activation.
        /// </summary>
        public bool IsCommitted { get; }

        /// <summary>
        /// Gets the complete fingerprint, or empty for incomplete preparation.
        /// </summary>
        public ByteString EffectiveInputDigest => ByteString.From(m_effectiveInputDigest.Span.ToArray());

        /// <summary>
        /// Gets a caller-owned array of the original immutable semantic edges.
        /// </summary>
        public ArrayOf<WotDependency> Edges => Graph.Edges.Span.ToArray().ToArrayOf();

        /// <summary>
        /// Gets caller-owned copies of the exact origin/content target pins.
        /// </summary>
        public ArrayOf<WotDependencyTargetPin> Targets => Graph.Targets.Span.ToArray().ToArrayOf();

        /// <summary>
        /// Gets the graph for <c>WotDependencySnapshot</c>.
        /// </summary>
        internal ValidatedGraph Graph { get; }

        /// <summary>
        /// Creates an independent value for the generated registry Property contract.
        /// Mutating the returned value cannot change this observation.
        /// </summary>
        public WoTDependencySnapshotDataType ToDataType()
        {
            ArrayOf<WoTDependencyDataType> edges = Graph.Edges.ConvertAll(edge => new WoTDependencyDataType
            {
                SourceXid = edge.SourceXid,
                TargetXid = string.Empty,
                TargetUri = edge.TargetHref,
                RefType = edge.RefType,
                Resolved = edge.Resolved
            });
            foreach (WotDependencyTargetPin target in Graph.Targets)
            {
                edges[(int)target.EdgeIndex].TargetXid = target.VersionXid;
            }
            return new WoTDependencySnapshotDataType
            {
                SourceVersionId = SourceVersionId,
                Generation = Generation,
                RequestId = RequestId,
                ResolvedAt = ResolvedAt,
                IsCommitted = IsCommitted,
                EffectiveInputDigest = EffectiveInputDigest,
                Edges = edges,
                Targets = Graph.Targets.ConvertAll(target => target.ToDataType())
            };
        }

        private readonly ByteString m_effectiveInputDigest;

        /// <summary>
        /// Holds the validated canonical graph associated with a dependency snapshot.
        /// </summary>
        internal sealed class ValidatedGraph
        {
            /// <summary>
            /// Initializes a new <c>ValidatedGraph</c> instance.
            /// </summary>
            /// <param name="edges">The edges used to initialize the instance.</param>
            /// <param name="targets">The targets used to initialize the instance.</param>
            internal ValidatedGraph(ArrayOf<WotDependency> edges, ArrayOf<WotDependencyTargetPin> targets)
            {
                Edges = edges.Span.ToArray().ToArrayOf();
                Targets = targets.Span.ToArray().ToArrayOf();
                var indices = new HashSet<uint>();
                foreach (WotDependencyTargetPin target in Targets)
                {
                    if (target.EdgeIndex >= Edges.Count || !Edges[(int)target.EdgeIndex].Resolved ||
                        !indices.Add(target.EdgeIndex))
                    {
                        throw new ArgumentException("A target must identify exactly one resolved dependency edge.");
                    }
                }
                if (Edges.ToList().Count(edge => edge.Resolved) != Targets.Count)
                {
                    throw new ArgumentException("Every resolved dependency edge must have exactly one target pin.");
                }
            }

            /// <summary>
            /// Gets the edges for <c>ValidatedGraph</c>.
            /// </summary>
            internal ArrayOf<WotDependency> Edges { get; }

            /// <summary>
            /// Gets the targets for <c>ValidatedGraph</c>.
            /// </summary>
            internal ArrayOf<WotDependencyTargetPin> Targets { get; }
        }
    }
}
