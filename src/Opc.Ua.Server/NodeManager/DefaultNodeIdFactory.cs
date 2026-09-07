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
using System.Buffers;
using System.Collections.Concurrent;
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
        /// The default. Numeric identifiers are the most compact form on the
        /// wire and the most readable in client UIs. A 32 bit hash is also
        /// the only mode of this class where a collision between two distinct
        /// browse paths is realistically possible, so the factory records
        /// what it mints and raises
        /// <see cref="StatusCodes.BadConfigurationError"/> if two paths ever
        /// land on one identifier rather than letting one node silently
        /// replace the other.
        /// </remarks>
        Numeric,

        /// <summary>
        /// Mints string identifiers holding the canonical path verbatim.
        /// </summary>
        /// <remarks>
        /// This is the only mode that cannot collide, because it keeps the
        /// canonical path whole and two different paths are never the same
        /// text. The identifiers are long and opaque.
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
    public class DefaultNodeIdFactory : IRebasableNodeIdFactory
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
        /// The namespace index every minted NodeId belongs to, root and child
        /// alike. A parent's namespace is never inherited: it can belong to a
        /// companion-specification model whose NodeIds are fixed by its
        /// NodeSet, or to another NodeManager.
        /// </param>
        /// <param name="detectCollisions">
        /// Whether to refuse an identifier already given to a different
        /// browse path, or <c>null</c> to follow
        /// <see cref="DetectCollisionsByDefault"/>. A mode that cannot
        /// collide never watches, whatever this says.
        /// </param>
        public DefaultNodeIdFactory(
            NodeIdAssignmentMode mode = NodeIdAssignmentMode.Numeric,
            ushort defaultNamespaceIndex = 0,
            bool? detectCollisions = null)
            : this(
                mode,
                defaultNamespaceIndex,
                detectCollisions ?? DetectCollisionsByDefault,
                new ConcurrentDictionary<ushort, AllocationState>())
        {
        }

        /// <summary>
        /// Creates a view of an existing factory that keeps its allocation
        /// state.
        /// </summary>
        /// <remarks>
        /// The <c>With…</c> methods route here so that every view derived from
        /// one factory shares one counter and one record of minted
        /// identifiers per namespace. Rebuilding them per view would let two
        /// NodeManagers that own the same namespace hand out the same counter
        /// values - their seeds come from the clock and would be created
        /// moments apart - and would hide a collision between identifiers
        /// minted through different views.
        /// </remarks>
        private DefaultNodeIdFactory(
            NodeIdAssignmentMode mode,
            ushort defaultNamespaceIndex,
            bool detectCollisionsRequested,
            ConcurrentDictionary<ushort, AllocationState> allocationStates)
        {
            Mode = mode;
            DefaultNamespaceIndex = defaultNamespaceIndex;

            // the requested policy is kept as asked rather than as it applies
            // to this mode, so that WithMode(String).WithMode(Numeric) comes
            // back watching rather than silently giving the answer up on the
            // way through a mode that cannot collide.
            m_detectCollisionsRequested = detectCollisionsRequested;
            m_allocationStates = allocationStates;
            m_allocation = allocationStates.GetOrAdd(
                defaultNamespaceIndex,
                static _ => new AllocationState());
        }

        /// <summary>
        /// Whether a factory watches for collisions unless told otherwise.
        /// </summary>
        /// <remarks>
        /// <para>
        /// On in a debug build, off otherwise. The record costs memory that
        /// grows with the address space and a lookup on every mint, which is
        /// worth paying while a model is being developed - when a collision
        /// is a bug to find - and not in production, where the odds are
        /// remote and the cost is permanent.
        /// </para>
        /// <para>
        /// This is the fallback for a factory built without an explicit
        /// answer. A server sets <c>StandardServer.DetectNodeIdCollisions</c>
        /// to decide for all of its NodeManagers at once; the test fixtures
        /// turn it on there so the checking is exercised whatever
        /// configuration the stack was built in.
        /// </para>
        /// </remarks>
        public static bool DetectCollisionsByDefault { get; set; }
#if DEBUG
            = true;
#else
            = false;
#endif

        /// <summary>
        /// Whether two distinct canonical paths can produce one identifier in
        /// the specified mode.
        /// </summary>
        /// <remarks>
        /// <see cref="NodeIdAssignmentMode.String"/> keeps the path whole, so
        /// distinct paths stay distinct text.
        /// <see cref="NodeIdAssignmentMode.Counter"/> does not derive from a
        /// path at all. The rest truncate a hash.
        /// </remarks>
        private static bool CanCollide(NodeIdAssignmentMode mode)
        {
            return mode is NodeIdAssignmentMode.Numeric
                or NodeIdAssignmentMode.Guid
                or NodeIdAssignmentMode.Opaque;
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

        /// <inheritdoc/>
        /// <remarks>
        /// A mode that cannot collide never watches, however the factory was
        /// configured. The configured answer itself survives a trip through
        /// such a mode, so switching back turns watching on again.
        /// </remarks>
        public bool DetectsCollisions => m_detectCollisionsRequested && CanCollide(Mode);

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

            return new DefaultNodeIdFactory(
                Mode,
                defaultNamespaceIndex,
                m_detectCollisionsRequested,
                m_allocationStates);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Explicit so that the public method keeps returning the concrete
        /// type: a caller holding a <see cref="DefaultNodeIdFactory"/> can go
        /// on chaining without a cast, while a caller holding the interface
        /// stays on the interface.
        /// </remarks>
        IRebasableNodeIdFactory IRebasableNodeIdFactory.WithDefaultNamespaceIndex(
            ushort defaultNamespaceIndex)
        {
            return WithDefaultNamespaceIndex(defaultNamespaceIndex);
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

            return new DefaultNodeIdFactory(
                mode,
                DefaultNamespaceIndex,
                m_detectCollisionsRequested,
                m_allocationStates);
        }

        /// <summary>
        /// Returns a factory that mints the same way and does or does not
        /// watch for collisions.
        /// </summary>
        /// <remarks>
        /// A NodeManager calls this with its server's setting, so that one
        /// answer covers every NodeManager the server hosts.
        /// </remarks>
        /// <param name="detectCollisions">Whether to watch.</param>
        /// <returns>
        /// This instance when the answer already matches, otherwise a copy.
        /// </returns>
        public virtual DefaultNodeIdFactory WithCollisionDetection(bool detectCollisions)
        {
            if (detectCollisions == m_detectCollisionsRequested)
            {
                return this;
            }

            return new DefaultNodeIdFactory(
                Mode,
                DefaultNamespaceIndex,
                detectCollisions,
                m_allocationStates);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Explicit for the same reason as
        /// <see cref="WithDefaultNamespaceIndex"/>.
        /// </remarks>
        IRebasableNodeIdFactory IRebasableNodeIdFactory.WithCollisionDetection(bool detectCollisions)
        {
            return WithCollisionDetection(detectCollisions);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Explicit for the same reason as
        /// <see cref="WithDefaultNamespaceIndex"/>.
        /// </remarks>
        IRebasableNodeIdFactory IRebasableNodeIdFactory.WithMode(NodeIdAssignmentMode mode)
        {
            return WithMode(mode);
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
            for (int attempt = 0; attempt < kMaxCounterAttempts; attempt++)
            {
                var nodeId = new NodeId(
                    Utils.IncrementIdentifier(ref m_allocation.LastUsedId),
                    DefaultNamespaceIndex);

                // Under Numeric the counter mints into the same space the
                // hash does - the counter base is a 32 bit value like any
                // other - so a counter identifier can land on one already
                // derived from a browse path. The counter is the side with
                // freedom to move, so it steps over the clash instead of
                // reporting it.
                if (!DetectsCollisions ||
                    m_allocation.MintedIdentifiers.TryAdd(nodeId, kCounterWitness))
                {
                    return nodeId;
                }
            }

            throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "No free counter NodeId was found in namespace {0} after {1} attempts.",
                DefaultNamespaceIndex,
                kMaxCounterAttempts);
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

            ValidatePathArguments(browseName, namespaceUris);

            StringBuilder? parentIdentifier = FormatParentIdentifier(parentNodeId);

            int length = MeasureCanonicalPath(
                parentNodeId,
                parentIdentifier,
                browseName,
                namespaceIndex,
                namespaceUris);

            // the path is scratch: it is projected onto an identifier and, in
            // every mode but String, never becomes a string at all. A short
            // one lives on the stack, a long one comes from the pool.
            Span<char> stack = stackalloc char[kMaxStackallocChars];
            char[]? rented = length > kMaxStackallocChars
                ? ArrayPool<char>.Shared.Rent(length)
                : null;

            try
            {
                Span<char> buffer = rented is null ? stack : rented.AsSpan();

                var writer = new PathBuilder(buffer);
                WriteCanonicalPath(
                    ref writer,
                    parentNodeId,
                    parentIdentifier,
                    browseName,
                    namespaceIndex,
                    namespaceUris);

                // the written length rather than the measured one: a rented
                // buffer is longer than the path and carries whatever the
                // previous tenant left in it.
                return MintFromPath(buffer[..writer.Length], namespaceIndex);
            }
            finally
            {
                if (rented is not null)
                {
                    ArrayPool<char>.Shared.Return(rented);
                }
            }
        }

        /// <summary>
        /// Projects a canonical path onto an identifier of the configured
        /// type and records it so a later collision is caught.
        /// </summary>
        private NodeId MintFromPath(ReadOnlySpan<char> canonicalPath, ushort namespaceIndex)
        {
            if (Mode == NodeIdAssignmentMode.String)
            {
                // A canonical path is longer than the parent identifier it
                // encodes, so a parent close to the limit expands past it.
                // Publishing the NodeId anyway would put a non-conformant
                // identifier into the address space, which a client is
                // entitled to reject.
                if (canonicalPath.Length > kMaxStringIdentifierLength)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "The canonical path is {0} characters, over the {1} the OPC UA " +
                        "String identifier allows (Part 3, 8.2.4). Select a hashing mode " +
                        "- Numeric, Guid or Opaque - whose identifiers are a fixed size.",
                        canonicalPath.Length,
                        kMaxStringIdentifierLength);
                }

                return new NodeId(ToStringValue(canonicalPath), namespaceIndex);
            }

            Span<byte> hash = stackalloc byte[HashLength];
            ComputeHash(canonicalPath, hash);

            NodeId nodeId;

            switch (Mode)
            {
                case NodeIdAssignmentMode.Numeric:
                    nodeId = new NodeId(ToIdentifier(hash), namespaceIndex);
                    break;
                case NodeIdAssignmentMode.Guid:
                    nodeId = new NodeId(ToGuid(hash), namespaceIndex);
                    break;
                default:
                    nodeId = new NodeId((ByteString)hash[..GuidLength].ToArray(), namespaceIndex);
                    break;
            }

            GuardAgainstCollision(nodeId, hash, canonicalPath);

            return nodeId;
        }

        /// <summary>
        /// Raises an error when a second browse path lands on an identifier
        /// this factory already minted for a different one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Minting the same path twice is normal - <c>AssignNodeIds</c> walks
        /// a subtree on every create pass - so the record keeps a witness of
        /// the path alongside the identifier and only a *differing* witness
        /// is a collision. The witness is the tail of the same hash, so two
        /// paths would have to agree on the identifier and on a further 64
        /// bits before a real collision could pass as a re-mint.
        /// </para>
        /// <para>
        /// The alternative to raising is worse than a failed start: the
        /// predefined-node index takes the last writer, so a collision would
        /// silently replace one node with the other and the address space
        /// would be quietly wrong.
        /// </para>
        /// </remarks>
        private void GuardAgainstCollision(
            NodeId nodeId,
            ReadOnlySpan<byte> hash,
            ReadOnlySpan<char> canonicalPath)
        {
            if (!DetectsCollisions)
            {
                return;
            }

            ulong witness = ToWitness(hash);

            if (m_allocation.MintedIdentifiers.GetOrAdd(nodeId, witness) == witness)
            {
                return;
            }

            throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "The NodeId '{0}' was already minted from a different browse path, so '{1}' " +
                "cannot be given it. Two distinct browse paths hashed to one {2} identifier. " +
                "Select NodeIdAssignmentMode.String, which cannot collide, or Guid or Opaque, " +
                "which spread the same hash over 128 bits.",
                nodeId,
                ToStringValue(canonicalPath),
                Mode);
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
            ValidatePathArguments(browseName, namespaceUris);

            StringBuilder? parentIdentifier = FormatParentIdentifier(parentNodeId);

            int length = MeasureCanonicalPath(
                parentNodeId,
                parentIdentifier,
                browseName,
                namespaceIndex,
                namespaceUris);

            Span<char> stack = stackalloc char[kMaxStackallocChars];
            char[]? rented = length > kMaxStackallocChars
                ? ArrayPool<char>.Shared.Rent(length)
                : null;

            try
            {
                Span<char> buffer = rented is null ? stack : rented.AsSpan();

                var writer = new PathBuilder(buffer);
                WriteCanonicalPath(
                    ref writer,
                    parentNodeId,
                    parentIdentifier,
                    browseName,
                    namespaceIndex,
                    namespaceUris);

                return ToStringValue(buffer[..writer.Length]);
            }
            finally
            {
                if (rented is not null)
                {
                    ArrayPool<char>.Shared.Return(rented);
                }
            }
        }

        /// <summary>
        /// Rejects arguments no canonical path can be built from.
        /// </summary>
        private static void ValidatePathArguments(
            QualifiedName browseName,
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
        }

        /// <summary>
        /// Formats the parent's identifier with its type prefix but without a
        /// namespace, so that a parent keeps its identity when the namespace
        /// table is ordered differently.
        /// </summary>
        /// <remarks>
        /// Produced once and read by both passes over the buffer.
        /// <c>NodeId.Format</c> is the authority on the spelling, so the path
        /// keeps the exact text clients already hold.
        /// </remarks>
        /// <returns>The identifier text, or <c>null</c> for a root node.</returns>
        private static StringBuilder? FormatParentIdentifier(NodeId parentNodeId)
        {
            if (parentNodeId.IsNull)
            {
                return null;
            }

            var identifier = new StringBuilder();

            NodeId.Format(
                CultureInfo.InvariantCulture,
                identifier,
                parentNodeId.IdentifierAsString,
                parentNodeId.IdType,
                namespaceIndex: 0);

            return identifier;
        }

        /// <summary>
        /// Returns the length of the canonical path without writing it.
        /// </summary>
        private static int MeasureCanonicalPath(
            NodeId parentNodeId,
            StringBuilder? parentIdentifier,
            QualifiedName browseName,
            ushort namespaceIndex,
            NamespaceTable namespaceUris)
        {
            var measure = PathBuilder.Measuring();

            WriteCanonicalPath(
                ref measure,
                parentNodeId,
                parentIdentifier,
                browseName,
                namespaceIndex,
                namespaceUris);

            return measure.Length;
        }

        /// <summary>
        /// Writes the canonical path, or measures it when the builder is
        /// measuring.
        /// </summary>
        /// <remarks>
        /// One routine serves both passes so the measured length can never
        /// drift from the written one.
        /// </remarks>
        private static void WriteCanonicalPath(
            ref PathBuilder builder,
            NodeId parentNodeId,
            StringBuilder? parentIdentifier,
            QualifiedName browseName,
            ushort namespaceIndex,
            NamespaceTable namespaceUris)
        {
            // each segment is prefixed by its own length, so the segment has
            // to be measured before it can be written.
            var parentMeasure = PathBuilder.Measuring();
            WriteParentSegment(
                ref parentMeasure, parentNodeId, parentIdentifier, namespaceIndex, namespaceUris);

            var browseMeasure = PathBuilder.Measuring();
            WriteBrowseNameSegment(ref browseMeasure, browseName, namespaceIndex, namespaceUris);

            builder.Append(CanonicalPathVersion);
            builder.Append(':');
            builder.AppendNumber(parentMeasure.Length);
            builder.Append(':');
            WriteParentSegment(
                ref builder, parentNodeId, parentIdentifier, namespaceIndex, namespaceUris);
            builder.Append(':');
            builder.AppendNumber(browseMeasure.Length);
            builder.Append(':');
            WriteBrowseNameSegment(ref builder, browseName, namespaceIndex, namespaceUris);
        }

        /// <summary>
        /// Writes the parent segment. It is empty for a root node.
        /// </summary>
        private static void WriteParentSegment(
            ref PathBuilder builder,
            NodeId parentNodeId,
            StringBuilder? parentIdentifier,
            ushort namespaceIndex,
            NamespaceTable namespaceUris)
        {
            if (parentIdentifier is null)
            {
                return;
            }

            if (parentNodeId.NamespaceIndex == namespaceIndex)
            {
                WriteLocal(ref builder, parentIdentifier);
                return;
            }

            WriteQualified(
                ref builder, namespaceUris, parentNodeId.NamespaceIndex, parentIdentifier);
        }

        /// <summary>
        /// Writes the browse name segment.
        /// </summary>
        private static void WriteBrowseNameSegment(
            ref PathBuilder builder,
            QualifiedName browseName,
            ushort namespaceIndex,
            NamespaceTable namespaceUris)
        {
            if (browseName.NamespaceIndex == namespaceIndex)
            {
                WriteLocal(ref builder, browseName.Name!);
                return;
            }

            if (browseName.NamespaceIndex == 0)
            {
                builder.Append("z:");
                WriteLengthPrefixed(ref builder, browseName.Name!);
                return;
            }

            WriteQualified(ref builder, namespaceUris, browseName.NamespaceIndex, browseName.Name!);
        }

        /// <summary>
        /// Writes a segment that lives in the minted namespace.
        /// </summary>
        private static void WriteLocal(ref PathBuilder builder, string text)
        {
            builder.Append("l:");
            WriteLengthPrefixed(ref builder, text);
        }

        /// <inheritdoc cref="WriteLocal(ref PathBuilder,string)"/>
        private static void WriteLocal(ref PathBuilder builder, StringBuilder text)
        {
            builder.Append("l:");
            WriteLengthPrefixed(ref builder, text);
        }

        /// <summary>
        /// Writes a segment qualified by its namespace URI.
        /// </summary>
        private static void WriteQualified(
            ref PathBuilder builder,
            NamespaceTable namespaceUris,
            ushort namespaceIndex,
            string text)
        {
            if (WriteNamespaceQualifier(ref builder, namespaceUris, namespaceIndex))
            {
                WriteLengthPrefixed(ref builder, text);
            }
        }

        /// <inheritdoc cref="WriteQualified(ref PathBuilder,NamespaceTable,ushort,string)"/>
        private static void WriteQualified(
            ref PathBuilder builder,
            NamespaceTable namespaceUris,
            ushort namespaceIndex,
            StringBuilder text)
        {
            if (WriteNamespaceQualifier(ref builder, namespaceUris, namespaceIndex))
            {
                WriteLengthPrefixed(ref builder, text);
            }
        }

        /// <summary>
        /// Writes the namespace part of a qualified segment, up to and
        /// including the separator before the text.
        /// </summary>
        private static bool WriteNamespaceQualifier(
            ref PathBuilder builder,
            NamespaceTable namespaceUris,
            ushort namespaceIndex)
        {
            string? namespaceUri = namespaceUris.GetString(namespaceIndex);

            // A namespace with no URI cannot be named stably, so the segment
            // falls back to the index. That form is deliberately distinct
            // from the URI form, so identifiers minted while the namespace
            // was missing stay distinguishable from the ones minted once it
            // is registered rather than silently colliding with them.
            if (string.IsNullOrEmpty(namespaceUri))
            {
                builder.Append("x:");
                builder.AppendNumber(PathBuilder.DigitCount(namespaceIndex));
                builder.Append(':');
                builder.AppendNumber(namespaceIndex);
                builder.Append(':');
                return true;
            }

            builder.Append("u:");
            WriteLengthPrefixed(ref builder, namespaceUri!);
            builder.Append(':');
            return true;
        }

        /// <summary>
        /// Writes the text prefixed by its own length.
        /// </summary>
        private static void WriteLengthPrefixed(ref PathBuilder builder, string text)
        {
            builder.AppendNumber(text.Length);
            builder.Append(':');
            builder.Append(text);
        }

        /// <inheritdoc cref="WriteLengthPrefixed(ref PathBuilder,string)"/>
        private static void WriteLengthPrefixed(ref PathBuilder builder, StringBuilder text)
        {
            builder.AppendNumber(text.Length);
            builder.Append(':');
            builder.Append(text);
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
        /// Returns the span as a string.
        /// </summary>
        /// <remarks>
        /// The frameworks without a span based string constructor copy once.
        /// That is still one allocation against the ten the segment by
        /// segment concatenation this replaced needed, and it is only reached
        /// in <see cref="NodeIdAssignmentMode.String"/> and when reporting a
        /// collision.
        /// </remarks>
        private static string ToStringValue(ReadOnlySpan<char> value)
        {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            return new string(value);
#else
            return new string(value.ToArray());
#endif
        }

        /// <summary>
        /// Hashes the canonical path into the destination.
        /// </summary>
        /// <remarks>
        /// SHA256 is used for its stable, platform independent output, not
        /// for any security property. The hash never leaves the address space
        /// as anything other than a NodeId.
        /// </remarks>
        private static void ComputeHash(ReadOnlySpan<char> canonicalPath, Span<byte> destination)
        {
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            int byteCount = Encoding.UTF8.GetByteCount(canonicalPath);

            Span<byte> stack = stackalloc byte[kMaxStackallocBytes];
            byte[]? rented = byteCount > kMaxStackallocBytes
                ? ArrayPool<byte>.Shared.Rent(byteCount)
                : null;

            try
            {
                Span<byte> bytes = rented is null ? stack : rented.AsSpan();
                int written = Encoding.UTF8.GetBytes(canonicalPath, bytes);

#if NET5_0_OR_GREATER
                SHA256.HashData(bytes[..written], destination);
#else
                using SHA256 sha256 = SHA256.Create();
                sha256.TryComputeHash(bytes[..written], destination, out _);
#endif
            }
            finally
            {
                if (rented is not null)
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
#else
            byte[] bytes = Encoding.UTF8.GetBytes(canonicalPath.ToArray());

            using SHA256 sha256 = SHA256.Create();
            sha256.ComputeHash(bytes).CopyTo(destination);
#endif
        }

        /// <summary>
        /// Projects the tail of the hash onto the witness that tells a
        /// re-mint of one path from a collision between two.
        /// </summary>
        /// <remarks>
        /// The tail is used because no mode derives its identifier from it,
        /// so the witness carries bits the identifier does not.
        /// </remarks>
        private static ulong ToWitness(ReadOnlySpan<byte> hash)
        {
            ulong witness = 0;

            for (int ii = HashLength - sizeof(ulong); ii < HashLength; ii++)
            {
                witness = (witness << 8) | hash[ii];
            }

            return witness;
        }

        /// <summary>
        /// Projects the hash onto a numeric identifier.
        /// </summary>
        private static uint ToIdentifier(ReadOnlySpan<byte> hash)
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
        private static Guid ToGuid(ReadOnlySpan<byte> hash)
        {
            Span<byte> bytes = stackalloc byte[GuidLength];
            hash[..GuidLength].CopyTo(bytes);

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

        /// <summary>
        /// The size of a SHA256 hash.
        /// </summary>
        private const int HashLength = 32;

        /// <summary>
        /// The longest String identifier OPC UA allows, from Part 3, 8.2.4.
        /// </summary>
        private const int kMaxStringIdentifierLength = 4096;

        /// <summary>
        /// The witness recorded for a counter identifier, which has no browse
        /// path behind it.
        /// </summary>
        /// <remarks>
        /// A path derived witness is the tail of a SHA256 hash, so it can in
        /// principle be this value too. That costs nothing: the only effect
        /// would be treating one collision as a re-mint, at a probability of
        /// 2^-64 on top of the collision itself.
        /// </remarks>
        private const ulong kCounterWitness = 0;

        /// <summary>
        /// How many counter values are tried before giving up on finding one
        /// no browse path has already claimed.
        /// </summary>
        private const int kMaxCounterAttempts = 1024;

        /// <summary>
        /// The longest canonical path built on the stack. Longer ones come
        /// from <see cref="ArrayPool{T}"/>.
        /// </summary>
        /// <remarks>
        /// A path is a version prefix, a parent identifier and a browse name,
        /// each length prefixed, plus a namespace URI when either crosses a
        /// namespace. That fits well inside this for ordinary models, so the
        /// pool is the exception rather than the rule.
        /// </remarks>
        private const int kMaxStackallocChars = 256;

        /// <summary>
        /// The longest UTF-8 encoding of a path hashed on the stack.
        /// </summary>
        /// <remarks>
        /// A canonical path is overwhelmingly ASCII, where the encoding is
        /// the same length as the path, but a browse name or namespace URI
        /// may carry any Unicode, so this allows the worst case expansion of
        /// a stack sized path.
        /// </remarks>
        private const int kMaxStackallocBytes = kMaxStackallocChars * 3;

        /// <summary>
        /// The identifiers minted so far, each with a witness of the browse
        /// path it came from, or <c>null</c> in a mode that cannot collide.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Scoped to the factory instance, which is scoped to a namespace:
        /// <see cref="WithDefaultNamespaceIndex"/> hands a NodeManager that
        /// owns another namespace its own copy, and identifiers in different
        /// namespaces cannot collide. NodeManagers sharing one namespace
        /// share the instance and so are checked against each other.
        /// </para>
        /// <para>
        /// It grows with the number of distinct browse paths minted, and is
        /// never pruned: an identifier handed to a client stays spoken for
        /// even after the node goes away, so re-minting it for a different
        /// path is exactly the collision this catches.
        /// </para>
        /// </remarks>
        private readonly ConcurrentDictionary<ushort, AllocationState> m_allocationStates;

        /// <summary>
        /// The allocation state for <see cref="DefaultNamespaceIndex"/>.
        /// </summary>
        private readonly AllocationState m_allocation;

        /// <summary>
        /// The collision policy as configured, before the mode is consulted.
        /// </summary>
        private readonly bool m_detectCollisionsRequested;

        /// <summary>
        /// The identifiers handed out for one namespace, and the counter that
        /// mints the sequential ones.
        /// </summary>
        /// <remarks>
        /// Held per namespace and shared by every view derived from one
        /// factory, so that two NodeManagers owning the same namespace draw
        /// from one counter and are checked against each other. Identifiers in
        /// different namespaces cannot collide, so they get separate state.
        /// </remarks>
        private sealed class AllocationState
        {
            /// <summary>
            /// Counter behind <see cref="NodeIdAssignmentMode.Counter"/> and
            /// the fallback for nodes with no derivable browse path. The clock
            /// seeds the offset so a restart is unlikely to reissue
            /// identifiers a client still holds.
            /// </summary>
            public uint LastUsedId
                = kCounterBase | ((uint)DateTime.UtcNow.Ticks & 0x0FFFFFFF);

            /// <summary>
            /// The identifiers minted so far, each with a witness of the
            /// browse path it came from.
            /// </summary>
            public readonly ConcurrentDictionary<NodeId, ulong> MintedIdentifiers = new();
        }

        /// <summary>
        /// Writes a canonical path into a span, or measures one without a
        /// span to write into.
        /// </summary>
        /// <remarks>
        /// Both passes run the same code, so the length reserved for a
        /// segment cannot drift from the length written into it.
        /// </remarks>
        private ref struct PathBuilder
        {
            /// <summary>
            /// Creates a builder that writes into the buffer.
            /// </summary>
            public PathBuilder(Span<char> buffer)
            {
                m_buffer = buffer;
                m_writing = true;
                Length = 0;
            }

            /// <summary>
            /// Creates a builder that counts characters without writing.
            /// </summary>
            public static PathBuilder Measuring()
            {
                return default;
            }

            /// <summary>
            /// The number of characters written, or counted.
            /// </summary>
            public int Length { get; private set; }

            /// <summary>
            /// Appends one character.
            /// </summary>
            public void Append(char value)
            {
                if (m_writing)
                {
                    m_buffer[Length] = value;
                }

                Length++;
            }

            /// <summary>
            /// Appends the text.
            /// </summary>
            public void Append(ReadOnlySpan<char> text)
            {
                if (m_writing)
                {
                    text.CopyTo(m_buffer[Length..]);
                }

                Length += text.Length;
            }

            /// <summary>
            /// Appends the builder's content without materialising it.
            /// </summary>
            public void Append(StringBuilder text)
            {
                if (m_writing)
                {
                    for (int ii = 0; ii < text.Length; ii++)
                    {
                        m_buffer[Length + ii] = text[ii];
                    }
                }

                Length += text.Length;
            }

            /// <summary>
            /// Appends the invariant decimal form of a non-negative number.
            /// </summary>
            public void AppendNumber(int value)
            {
                int digits = DigitCount(value);

                if (m_writing)
                {
                    for (int ii = Length + digits - 1; ii >= Length; ii--)
                    {
                        m_buffer[ii] = (char)('0' + (value % 10));
                        value /= 10;
                    }
                }

                Length += digits;
            }

            /// <summary>
            /// The number of decimal digits a non-negative number is written
            /// as.
            /// </summary>
            public static int DigitCount(int value)
            {
                int digits = 1;

                for (int rest = value; rest >= 10; rest /= 10)
                {
                    digits++;
                }

                return digits;
            }

            private readonly Span<char> m_buffer;
            private readonly bool m_writing;
        }
    }
}
