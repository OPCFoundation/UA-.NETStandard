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

using Opc.Ua.Export;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Nodes
{
    /// <summary>
    /// Extends the existing fluent NodeManager builder with node creation.
    /// </summary>
    /// <remarks>
    /// Creation is compositional: returned builders are the same fluent
    /// builders used by source-generated and runtime NodeSet managers.
    /// NodeIds are assigned before a builder is returned. A browse name
    /// with namespace index zero uses the source's first namespace. A default
    /// <c>parentId</c> places instance nodes below the standard Objects folder.
    /// </remarks>
    public interface INodeGraphBuilder : INodeManagerBuilder
    {
        /// <summary>
        /// Imports a NodeSet2 document into this graph generation.
        /// </summary>
        /// <remarks>
        /// Imported nodes retain their NodeSet-defined NodeIds, attributes,
        /// values, references, and parent relationships. Multiple calls form
        /// one import batch and are linked once before registration. Every
        /// namespace containing imported nodes must be declared by the owning
        /// <see cref="INodeSource"/>.
        /// </remarks>
        /// <param name="nodeSet">The parsed NodeSet2 document.</param>
        void Import(UANodeSet nodeSet);

        /// <summary>
        /// Adds an already constructed state and preserves caller-assigned NodeIds.
        /// </summary>
        /// <typeparam name="TState">The concrete state type.</typeparam>
        /// <param name="node">The node or node subtree to add.</param>
        /// <param name="parentId">
        /// The parent NodeId. The default places instance nodes below
        /// <see cref="ObjectIds.ObjectsFolder"/>.
        /// </param>
        /// <returns>A typed fluent builder for the added node.</returns>
        INodeBuilder<TState> Add<TState>(
            TState node,
            NodeId parentId = default)
            where TState : NodeState;

        /// <summary>
        /// Creates a folder.
        /// </summary>
        INodeBuilder<FolderState> AddFolder(
            QualifiedName browseName,
            NodeId parentId = default);

        /// <summary>
        /// Creates an object.
        /// </summary>
        INodeBuilder<BaseObjectState> AddObject(
            QualifiedName browseName,
            NodeId parentId = default,
            NodeId typeDefinitionId = default);

        /// <summary>
        /// Creates a typed data variable.
        /// </summary>
        /// <typeparam name="TValue">The CLR type of the variable value.</typeparam>
        IVariableBuilder<TValue> AddVariable<TValue>(
            QualifiedName browseName,
            NodeId parentId = default);

        /// <summary>
        /// Creates an executable method.
        /// </summary>
        INodeBuilder<MethodState> AddMethod(
            QualifiedName browseName,
            NodeId parentId = default);
    }
}
