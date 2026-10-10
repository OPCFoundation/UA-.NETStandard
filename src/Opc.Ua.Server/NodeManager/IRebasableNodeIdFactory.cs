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

namespace Opc.Ua.Server
{
    /// <summary>
    /// An <see cref="INodeIdFactory"/> that a NodeManager can point at its
    /// own namespace and ask for a specific identifier style.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A bare <see cref="INodeIdFactory"/> only mints an identifier for a
    /// node that already exists. A NodeManager needs three things a factory
    /// cannot express that way: which namespace to mint into, which
    /// identifier style to mint, and an identifier for a node it has not
    /// built yet. Those are the members here.
    /// </para>
    /// <para>
    /// The namespace is the reason this is a separate contract rather than
    /// configuration passed at construction. A namespace belongs to the
    /// NodeManager, not to the node, and a NodeManager learns its own
    /// namespace index only once the server's namespace table has been
    /// extended - which is after a factory registered in dependency
    /// injection was created. Implementations are therefore expected to be
    /// immutable: <see cref="WithDefaultNamespaceIndex"/> and
    /// <see cref="WithMode"/> return a view rather than mutating, so a
    /// single registered instance can be shared by NodeManagers that own
    /// different namespaces.
    /// </para>
    /// <para>
    /// Implement this to decorate <see cref="DefaultNodeIdFactory"/> - to
    /// special-case a subtree and delegate the rest, for instance. Assigning
    /// such a decorator to <see cref="AsyncCustomNodeManager.NodeIdFactory"/>
    /// replaces overriding <see cref="INodeIdFactory.New"/> on the
    /// NodeManager itself, which leaves the identifier rule in one object
    /// instead of spread across the NodeManager.
    /// </para>
    /// </remarks>
    public interface IRebasableNodeIdFactory : INodeIdFactory
    {
        /// <summary>
        /// The identifier type this factory mints.
        /// </summary>
        NodeIdAssignmentMode Mode { get; }

        /// <summary>
        /// The namespace index every minted NodeId belongs to.
        /// </summary>
        /// <remarks>
        /// Namespace 0 is the OPC UA namespace and never a namespace a
        /// NodeManager mints into, so a factory reporting 0 is read as one
        /// that has not been pointed at a namespace yet.
        /// </remarks>
        ushort DefaultNamespaceIndex { get; }

        /// <summary>
        /// Whether the factory refuses to mint an identifier it already gave
        /// a different browse path.
        /// </summary>
        /// <remarks>
        /// Watching costs memory that grows with the address space, so it is
        /// a server-wide decision rather than a per-NodeManager one. A mode
        /// that cannot collide reports <c>false</c> whatever it was asked
        /// for.
        /// </remarks>
        bool DetectsCollisions { get; }

        /// <summary>
        /// Returns a factory with the same <see cref="Mode"/> that mints into
        /// the specified namespace.
        /// </summary>
        /// <param name="defaultNamespaceIndex">The namespace to mint into.</param>
        /// <returns>
        /// This instance when the namespace already matches, otherwise a copy.
        /// </returns>
        IRebasableNodeIdFactory WithDefaultNamespaceIndex(ushort defaultNamespaceIndex);

        /// <summary>
        /// Returns a factory that mints into the same namespace using the
        /// specified mode.
        /// </summary>
        /// <param name="mode">The identifier type to mint.</param>
        /// <returns>
        /// This instance when the mode already matches, otherwise a copy.
        /// </returns>
        IRebasableNodeIdFactory WithMode(NodeIdAssignmentMode mode);

        /// <summary>
        /// Returns a factory that mints the same way and does or does not
        /// watch for collisions.
        /// </summary>
        /// <param name="detectCollisions">Whether to watch.</param>
        /// <returns>
        /// This instance when the answer already matches, otherwise a copy.
        /// </returns>
        IRebasableNodeIdFactory WithCollisionDetection(bool detectCollisions);

        /// <summary>
        /// Mints the next sequential NodeId.
        /// </summary>
        /// <remarks>
        /// A NodeManager falls back to this for a node whose browse path
        /// cannot yield a deterministic identifier, and for a node that must
        /// be given an identifier no other node has, whatever the
        /// <see cref="Mode"/>.
        /// </remarks>
        /// <returns>A NodeId no other caller of this factory has seen.</returns>
        NodeId NextCounterNodeId();

        /// <summary>
        /// Mints the NodeId for a child of the specified parent.
        /// </summary>
        /// <remarks>
        /// Takes a parent NodeId rather than a node, so that a caller can
        /// learn the identifier a node will be given before building it -
        /// when wiring a reference to a node the caller creates later, for
        /// instance.
        /// </remarks>
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
        NodeId CreateChildNodeId(
            NodeId parentNodeId,
            QualifiedName browseName,
            ushort namespaceIndex,
            NamespaceTable namespaceUris);
    }
}
