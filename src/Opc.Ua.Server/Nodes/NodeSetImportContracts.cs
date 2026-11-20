/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Server.Nodes
{
    /// <summary>
    /// Public opt-in seam for node sources that supply NodeSet import factories.
    /// </summary>
    public interface INodeSetImportFactoryProvider
    {
        /// <summary>
        /// Gets the import factories for one source generation.
        /// </summary>
        ArrayOf<INodeSetImportFactory> GetNodeSetImportFactories();
    }

    /// <summary>
    /// Creates an empty concrete state for one NodeSet import discriminator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For Object and Variable nodes, <see cref="DiscriminatorId"/> identifies
    /// the TypeDefinition. For Method nodes it identifies the MethodDeclaration.
    /// For ObjectType, VariableType, DataType, ReferenceType, and View nodes it
    /// identifies the declaration's own NodeId.
    /// </para>
    /// <para>
    /// <see cref="CreateEmptyState"/> must use a concrete constructor directly.
    /// It must not run a normal <c>CreateInstanceOf</c> path or initialize
    /// generated children because the imported NodeSet supplies those children
    /// as flat nodes.
    /// </para>
    /// </remarks>
    public interface INodeSetImportFactory
    {
        /// <summary>
        /// Gets the node class accepted by this registration.
        /// </summary>
        NodeClass NodeClass { get; }

        /// <summary>
        /// Gets how <see cref="DiscriminatorId"/> is matched.
        /// </summary>
        NodeSetImportDiscriminator Discriminator { get; }

        /// <summary>
        /// Gets the namespace-stable discriminator registered by the factory.
        /// </summary>
        ExpandedNodeId DiscriminatorId { get; }

        /// <summary>
        /// Creates an empty concrete state without generated children.
        /// </summary>
        NodeState CreateEmptyState();
    }

    /// <summary>
    /// Selects the NodeSet field matched by an import factory.
    /// </summary>
    public enum NodeSetImportDiscriminator
    {
        /// <summary>
        /// Matches an Object or Variable TypeDefinition.
        /// </summary>
        TypeDefinition,

        /// <summary>
        /// Matches a MethodDeclarationId.
        /// </summary>
        MethodDeclaration,

        /// <summary>
        /// Matches the imported node's own NodeId.
        /// </summary>
        NodeId
    }
}
