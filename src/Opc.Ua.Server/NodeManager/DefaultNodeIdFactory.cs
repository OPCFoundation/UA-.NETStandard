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
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Selects the identifier type that a <see cref="DefaultNodeIdFactory"/> mints.
    /// </summary>
    /// <remarks>
    /// Every mode except <see cref="None"/> derives the identifier from the
    /// same canonical path (see
    /// <see cref="DefaultNodeIdFactory.CreateCanonicalPath"/>), so a node keeps the
    /// same NodeId across reloads as long as its browse path is unchanged.
    /// </remarks>
    public enum NodeIdAssignmentMode
    {
        /// <summary>
        /// The factory mints no identifiers at all.
        /// </summary>
        /// <remarks>
        /// A node manager selects this mode when it assigns NodeIds itself,
        /// typically by overriding <see cref="AsyncCustomNodeManager.New"/>.
        /// Asking a factory in this mode for an identifier is a
        /// configuration error and raises
        /// <see cref="StatusCodes.BadConfigurationError"/>.
        /// </remarks>
        None,

        /// <summary>
        /// Mints numeric identifiers from the first 32 bits of the canonical
        /// path hash.
        /// </summary>
        /// <remarks>
        /// Numeric identifiers are compact and readable in client UIs, but a
        /// 32 bit hash is the only mode of this class where a collision
        /// between two distinct browse paths is realistically possible.
        /// </remarks>
        Numeric,

        /// <summary>
        /// Mints string identifiers holding the canonical path verbatim.
        /// </summary>
        /// <remarks>
        /// This is the only mode that cannot collide, because the canonical
        /// path is injective. The identifiers are long and opaque.
        /// </remarks>
        String,

        /// <summary>
        /// Mints Guid identifiers from the first 128 bits of the canonical
        /// path hash.
        /// </summary>
        Guid,

        /// <summary>
        /// Mints opaque identifiers holding the first 128 bits of the
        /// canonical path hash.
        /// </summary>
        Opaque,

        /// <summary>
        /// Mints numeric identifiers from a sequential counter.
        /// </summary>
        /// <remarks>
        /// The only mode that does not derive from the browse path, and so
        /// the only one that stays unique when browse paths repeat - nodes
        /// that come and go under the same name, such as per-session
        /// diagnostics objects. The identifiers are compact but not stable
        /// across restarts.
        /// </remarks>
        Counter
    }

    /// <summary>
    /// Assigns deterministic NodeIds to nodes created at runtime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The classic convention for a child NodeId was to concatenate the
    /// parent identifier, an underscore and the child browse name. That form
    /// is ambiguous - <c>A_B</c> plus <c>C</c> and <c>A</c> plus <c>B_C</c>
    /// produce the same identifier - it discards the parent's identifier
    /// type, and it shifts whenever the namespace table is ordered
    /// differently.
    /// </para>
    /// <para>
    /// This class replaces it with a canonical path: a length prefixed
    /// encoding that records the parent's identifier type, qualifies
    /// cross namespace parents and browse names by URI, and therefore cannot
    /// collide on separator characters. <see cref="Mode"/> selects how that
    /// path is projected onto an identifier type.
    /// </para>
    /// <para>
    /// Instances are immutable and safe to register as a singleton. A
    /// NodeManager that owns a different namespace calls
    /// <see cref="WithDefaultNamespaceIndex"/> to get its own view of the
    /// registered factory rather than mutating it.
    /// </para>
    /// </remarks>
    public class DefaultNodeIdFactory : INodeIdFactory
    {
        /// <summary>
        /// The prefix identifying the canonical path format understood by
        /// this class.
        /// </summary>
        /// <remarks>
        /// A future revision of the format increments this prefix so that
        /// identifiers minted by different revisions stay distinguishable.
        /// </remarks>
        public const string CanonicalPathVersion = "v1";

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultNodeIdFactory"/>
        /// class.
        /// </summary>
        /// <param name="mode">The identifier type to mint.</param>
        /// <param name="defaultNamespaceIndex">
        /// The namespace index a root node is minted into. A child inherits
        /// its parent's namespace instead.
        /// </param>
        public DefaultNodeIdFactory(
            NodeIdAssignmentMode mode = NodeIdAssignmentMode.String,
            ushort defaultNamespaceIndex = 0)
        {
            Mode = mode;
            DefaultNamespaceIndex = defaultNamespaceIndex;
        }

        /// <summary>
        /// The identifier type minted by this factory.
        /// </summary>
        public NodeIdAssignmentMode Mode { get; }

        /// <summary>
        /// The namespace index every minted NodeId belongs to.
        /// </summary>
        /// <remarks>
        /// A NodeManager may only mint NodeIds in a namespace it owns, so
        /// this is never inferred from the node. A parent's namespace in
        /// particular can belong to a companion-specification model whose
        /// NodeIds are fixed by its NodeSet, or to another NodeManager.
        /// </remarks>
        public ushort DefaultNamespaceIndex { get; }

        /// <summary>
        /// Returns a factory with the same <see cref="Mode"/> that mints into
        /// the specified namespace.
        /// </summary>
        /// <remarks>
        /// A NodeManager calls this on the factory it resolved from
        /// dependency injection, so that a single registered instance can be
        /// shared by managers that own different namespaces without any of
        /// them mutating shared state. A NodeManager whose instance namespace
        /// is not its first one rebases the factory onto that namespace.
        /// </remarks>
        /// <param name="defaultNamespaceIndex">The namespace to mint into.</param>
        /// <returns>
        /// This instance when the namespace already matches, otherwise a copy.
        /// </returns>
        public virtual DefaultNodeIdFactory WithDefaultNamespaceIndex(
            ushort defaultNamespaceIndex)
        {
            if (defaultNamespaceIndex == DefaultNamespaceIndex)
            {
                return this;
            }

            return new DefaultNodeIdFactory(Mode, defaultNamespaceIndex);
        }

        /// <summary>
        /// Returns a factory that mints into the same namespace using the
        /// specified mode.
        /// </summary>
        /// <remarks>
        /// This is how a NodeManager selects its identifier style: a single
        /// declarative line in its constructor rather than a
        /// <c>New</c> override.
        /// </remarks>
        /// <param name="mode">The identifier type to mint.</param>
        /// <returns>
        /// This instance when the mode already matches, otherwise a copy.
        /// </returns>
        public virtual DefaultNodeIdFactory WithMode(NodeIdAssignmentMode mode)
        {
            if (mode == Mode)
            {
                return this;
            }

            return new DefaultNodeIdFactory(mode, DefaultNamespaceIndex);
        }

        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        /// <returns>
        /// The NodeId already set on <paramref name="node"/> if there is one,
        /// otherwise a freshly minted deterministic NodeId.
        /// </returns>
        /// <exception cref="ServiceResultException">
        /// Thrown when <see cref="Mode"/> is
        /// <see cref="NodeIdAssignmentMode.None"/>.
        /// </exception>
        public virtual NodeId New(ISystemContext context, NodeState node)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            // An identifier the caller can plausibly have chosen is kept:
            // one already in this NodeManager's own namespace, or one on a
            // node that stands on its own. NodeState.Create hands the root
            // its NodeId before running the assignment pass, so a caller
            // naming a node explicitly reaches here through the first of
            // those.
            //
            // Anything else belongs to another namespace's model. A node
            // reaches here through AssignNodeIds walking a subtree that
            // NodeState.CreateInstance copied from a type declaration, and
            // its nodes still carry that declaration's identifiers. Keeping
            // them would alias every instance of the type onto the type's own
            // nodes, and the predefined-node index takes the last writer, so
            // the type quietly becomes an instance rather than the clash
            // being reported.
            //
            // A node that must shed an identifier already in this namespace -
            // a generated instance whose type lives here too - says so by
            // clearing the NodeId first, which is what
            // ISystemContext.AssignInstanceNodeId does for a whole subtree.
            if (!node.NodeId.IsNull &&
                (node.NodeId.NamespaceIndex == DefaultNamespaceIndex ||
                    node is not BaseInstanceState { Parent: not null }))
            {
                return node.NodeId;
            }

            if (Mode == NodeIdAssignmentMode.None)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The DefaultNodeIdFactory is disabled, so no NodeId can be minted for node '{0}'. " +
                    "Assign the NodeId before the node is added, or select a mode other than None.",
                    node.BrowseName);
            }

            // a node with no stable browse path - an event instance and its
            // fields, for example - cannot be derived from, so it falls back
            // to the counter no matter which mode is selected. That keeps
            // the NodeManagers themselves free of any assignment logic.
            if (Mode == NodeIdAssignmentMode.Counter || !HasDerivablePath(node))
            {
                return NextCounterNodeId();
            }

            return CreateChildNodeId(
                GetParentNodeId(context, node),
                node.BrowseName,
                DefaultNamespaceIndex,
                context.NamespaceUris);
        }

        /// <summary>
        /// Mints the next sequential NodeId.
        /// </summary>
        /// <remarks>
        /// The counter is per factory instance. Two NodeManagers that share
        /// one instance therefore share the counter, which is what keeps
        /// their identifiers distinct while they mint into one namespace.
        /// </remarks>
        /// <returns>A NodeId no other caller of this factory has seen.</returns>
        public NodeId NextCounterNodeId()
        {
            return new NodeId(
                Utils.IncrementIdentifier(ref m_lastUsedId),
                DefaultNamespaceIndex);
        }

        /// <summary>
        /// Returns true when the node carries a browse path that a
        /// deterministic identifier can be derived from.
        /// </summary>
        /// <remarks>
        /// A node fails this test when it has no browse name, or when its
        /// parent is itself transient and carries no NodeId - an event
        /// instance and its fields, for example. Such a node has no stable
        /// path, so <see cref="New"/> falls back to the counter for it.
        /// </remarks>
        /// <param name="node">The node to test.</param>
        /// <returns>True when a deterministic identifier can be derived.</returns>
        public virtual bool HasDerivablePath(NodeState node)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (node.BrowseName.IsNull || string.IsNullOrEmpty(node.BrowseName.Name))
            {
                return false;
            }

            return node is not BaseInstanceState { Parent: { } parent }
                || !parent.NodeId.IsNull;
        }

        /// <summary>
        /// Mints the NodeId for a child of the specified parent.
        /// </summary>
        /// <param name="parentNodeId">
        /// The parent NodeId, or a null NodeId for a node that is authored at
        /// the root of the address space.
        /// </param>
        /// <param name="browseName">The browse name of the child.</param>
        /// <param name="namespaceIndex">
        /// The namespace index the minted NodeId belongs to.
        /// </param>
        /// <param name="namespaceUris">The namespace table to resolve URIs.</param>
        /// <returns>The minted NodeId.</returns>
        public NodeId CreateChildNodeId(
            NodeId parentNodeId,
            QualifiedName browseName,
            ushort namespaceIndex,
            NamespaceTable namespaceUris)
        {
            if (Mode == NodeIdAssignmentMode.None)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The DefaultNodeIdFactory is disabled, so no NodeId can be minted for '{0}'.",
                    browseName);
            }

            if (Mode == NodeIdAssignmentMode.Counter)
            {
                return NextCounterNodeId();
            }

            string canonicalPath = CreateCanonicalPath(
                parentNodeId,
                browseName,
                namespaceIndex,
                namespaceUris);

            if (Mode == NodeIdAssignmentMode.String)
            {
                return new NodeId(canonicalPath, namespaceIndex);
            }

            byte[] hash = ComputeHash(canonicalPath);

            switch (Mode)
            {
                case NodeIdAssignmentMode.Numeric:
                    return new NodeId(ToIdentifier(hash), namespaceIndex);
                case NodeIdAssignmentMode.Guid:
                    return new NodeId(ToGuid(hash), namespaceIndex);
                default:
                    byte[] opaque = new byte[GuidLength];
                    Array.Copy(hash, opaque, GuidLength);
                    return new NodeId((ByteString)opaque, namespaceIndex);
            }
        }

        /// <summary>
        /// The lowest identifier the counter ever mints.
        /// </summary>
        /// <remarks>
        /// A NodeManager usually mints into the same namespace its NodeSet
        /// occupies, and a counter that started near zero would walk into
        /// that model's numeric identifiers - the predefined-node index
        /// overwrites rather than rejects, so a long-running server would
        /// quietly replace a type node with a runtime instance. Identifiers
        /// this large are out of reach of any authored model, which makes
        /// the separation structural rather than a matter of seeding luck.
        /// </remarks>
        private const uint kCounterBase = 0x40000000;

        /// <summary>
        /// Counter behind <see cref="NodeIdAssignmentMode.Counter"/> and the
        /// fallback for nodes with no derivable browse path. The clock seeds
        /// the offset so a restart is unlikely to reissue identifiers a
        /// client still holds.
        /// </summary>
        private uint m_lastUsedId
            = kCounterBase | ((uint)DateTime.UtcNow.Ticks & 0x0FFFFFFF);

        /// <summary>
        /// Builds the canonical path that every identifier type is derived
        /// from.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The path is <c>v1:&lt;len&gt;:&lt;parent&gt;:&lt;len&gt;:&lt;browseName&gt;</c>,
        /// where each segment is itself length prefixed. A segment starts
        /// with <c>l</c> when it lives in <paramref name="namespaceIndex"/>,
        /// with <c>z</c> when it lives in namespace 0, and with <c>u</c>
        /// followed by the namespace URI otherwise. The parent segment is
        /// empty for a node authored at the root.
        /// </para>
        /// <para>
        /// Because every variable part carries its own length, no combination
        /// of parent identifier and browse name can produce the same path as
        /// a different combination.
        /// </para>
        /// </remarks>
        /// <param name="parentNodeId">
        /// The parent NodeId, or a null NodeId for a root node.
        /// </param>
        /// <param name="browseName">The browse name of the child.</param>
        /// <param name="namespaceIndex">
        /// The namespace index the minted NodeId belongs to.
        /// </param>
        /// <param name="namespaceUris">The namespace table to resolve URIs.</param>
        /// <returns>The canonical path.</returns>
        public static string CreateCanonicalPath(
            NodeId parentNodeId,
            QualifiedName browseName,
            ushort namespaceIndex,
            NamespaceTable namespaceUris)
        {
            if (browseName.IsNull || string.IsNullOrEmpty(browseName.Name))
            {
                throw new ArgumentException("The browse name is null.", nameof(browseName));
            }
            if (namespaceUris is null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            string parentText = parentNodeId.IsNull
                ? string.Empty
                : FormatParent(parentNodeId, namespaceIndex, namespaceUris);

            string browseNameText = FormatBrowseName(
                browseName,
                namespaceIndex,
                namespaceUris);

            var path = new StringBuilder(
                CanonicalPathVersion.Length + parentText.Length + browseNameText.Length + 16);

            path.Append(CanonicalPathVersion).Append(':');
            AppendLengthPrefixed(path, parentText);
            path.Append(':');
            AppendLengthPrefixed(path, browseNameText);

            return path.ToString();
        }

        /// <summary>
        /// Returns the NodeId of the node the minted identifier is derived
        /// from.
        /// </summary>
        /// <remarks>
        /// The default implementation uses the parent of a
        /// <see cref="BaseInstanceState"/>. A node manager that tracks
        /// parents outside the node hierarchy overrides this to supply them.
        /// </remarks>
        /// <param name="context">The context.</param>
        /// <param name="node">The node being assigned an identifier.</param>
        /// <returns>The parent NodeId, or a null NodeId for a root node.</returns>
        protected virtual NodeId GetParentNodeId(ISystemContext context, NodeState node)
        {
            if (node is not BaseInstanceState { Parent: { } parent })
            {
                return NodeId.Null;
            }

            // NodeIds are assigned top down, so a parent that still has no
            // NodeId means the caller assigned out of order. Deriving from an
            // empty parent would silently alias the child onto a root.
            if (parent.NodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Cannot mint a NodeId for node '{0}' because its parent '{1}' has none yet.",
                    node.BrowseName,
                    parent.BrowseName);
            }

            // the Objects folder is the default organizer of a root node and
            // carries no information about the node itself.
            if (parent.NodeId == ObjectIds.ObjectsFolder)
            {
                return NodeId.Null;
            }

            return parent.NodeId;
        }

        /// <summary>
        /// Formats the parent segment of the canonical path.
        /// </summary>
        private static string FormatParent(
            NodeId parentNodeId,
            ushort namespaceIndex,
            NamespaceTable namespaceUris)
        {
            // the identifier is formatted with its type prefix but without a
            // namespace, so that a parent keeps its identity when the
            // namespace table is ordered differently.
            var identifierBuffer = new StringBuilder();
            NodeId.Format(
                CultureInfo.InvariantCulture,
                identifierBuffer,
                parentNodeId.IdentifierAsString,
                parentNodeId.IdType,
                namespaceIndex: 0);
            string identifierText = identifierBuffer.ToString();

            if (parentNodeId.NamespaceIndex == namespaceIndex)
            {
                return Local(identifierText);
            }

            return Qualified(namespaceUris, parentNodeId.NamespaceIndex, identifierText);
        }

        /// <summary>
        /// Formats the browse name segment of the canonical path.
        /// </summary>
        private static string FormatBrowseName(
            QualifiedName browseName,
            ushort namespaceIndex,
            NamespaceTable namespaceUris)
        {
            if (browseName.NamespaceIndex == namespaceIndex)
            {
                return Local(browseName.Name!);
            }

            if (browseName.NamespaceIndex == 0)
            {
                return string.Concat(
                    "z:",
                    LengthPrefixed(browseName.Name!));
            }

            return Qualified(namespaceUris, browseName.NamespaceIndex, browseName.Name!);
        }

        /// <summary>
        /// Formats a segment that lives in the minted namespace.
        /// </summary>
        private static string Local(string text)
        {
            return string.Concat("l:", LengthPrefixed(text));
        }

        /// <summary>
        /// Formats a segment qualified by its namespace URI.
        /// </summary>
        private static string Qualified(
            NamespaceTable namespaceUris,
            ushort namespaceIndex,
            string text)
        {
            string? namespaceUri = namespaceUris.GetString(namespaceIndex);

            // A namespace with no URI cannot be named stably, so the segment
            // falls back to the index. That form is deliberately distinct
            // from the URI form, so identifiers minted while the namespace
            // was missing stay distinguishable from the ones minted once it
            // is registered rather than silently colliding with them.
            if (string.IsNullOrEmpty(namespaceUri))
            {
                return string.Concat(
                    "x:",
                    LengthPrefixed(namespaceIndex.ToString(CultureInfo.InvariantCulture)),
                    ":",
                    LengthPrefixed(text));
            }

            return string.Concat(
                "u:",
                LengthPrefixed(namespaceUri!),
                ":",
                LengthPrefixed(text));
        }

        /// <summary>
        /// Returns the text prefixed by its own length.
        /// </summary>
        private static string LengthPrefixed(string text)
        {
            return string.Concat(
                text.Length.ToString(CultureInfo.InvariantCulture),
                ":",
                text);
        }

        /// <summary>
        /// Appends the text prefixed by its own length.
        /// </summary>
        private static void AppendLengthPrefixed(StringBuilder builder, string text)
        {
            builder
                .Append(text.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(text);
        }

        /// <summary>
        /// Hashes the canonical path.
        /// </summary>
        /// <remarks>
        /// SHA256 is used for its stable, platform independent output, not
        /// for any security property. The hash never leaves the address space
        /// as anything other than a NodeId.
        /// </remarks>
        private static byte[] ComputeHash(string canonicalPath)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(canonicalPath);

#if NET5_0_OR_GREATER
            return SHA256.HashData(bytes);
#else
            using SHA256 sha256 = SHA256.Create();
            return sha256.ComputeHash(bytes);
#endif
        }

        /// <summary>
        /// Projects the hash onto a numeric identifier.
        /// </summary>
        private static uint ToIdentifier(byte[] hash)
        {
            uint identifier = ((uint)hash[0] << 24)
                | ((uint)hash[1] << 16)
                | ((uint)hash[2] << 8)
                | hash[3];

            // identifier 0 is the null NodeId, so it can never be minted.
            return identifier != 0 ? identifier : uint.MaxValue;
        }

        /// <summary>
        /// Projects the hash onto a Guid identifier.
        /// </summary>
        /// <remarks>
        /// The fields are laid out so that the canonical text form of the
        /// Guid reads the hash bytes in order. The version and variant bits
        /// mark the value as name based per RFC 9562, which keeps it distinct
        /// from a random Guid.
        /// </remarks>
        private static Guid ToGuid(byte[] hash)
        {
            byte[] bytes = new byte[GuidLength];
            Array.Copy(hash, bytes, GuidLength);

            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

            return new Guid(
                ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3],
                (ushort)((bytes[4] << 8) | bytes[5]),
                (ushort)((bytes[6] << 8) | bytes[7]),
                bytes[8],
                bytes[9],
                bytes[10],
                bytes[11],
                bytes[12],
                bytes[13],
                bytes[14],
                bytes[15]);
        }

        /// <summary>
        /// The number of hash bytes consumed by the Guid and Opaque modes.
        /// </summary>
        private const int GuidLength = 16;
    }
}
