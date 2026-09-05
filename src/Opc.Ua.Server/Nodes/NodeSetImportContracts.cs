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
