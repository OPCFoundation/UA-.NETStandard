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

namespace Opc.Ua
{
    /// <summary>
    /// Core marker interface for a builder that has resolved a single
    /// <see cref="NodeState"/> and can walk to its children by browse
    /// name. Lives in <c>Opc.Ua.Types</c> so model-only assemblies
    /// (e.g. source-generator output for companion specifications) can
    /// reference it without taking a dependency on
    /// <c>Opc.Ua.Server</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The server-side <c>Opc.Ua.Server.Fluent.INodeBuilder</c> extends
    /// this marker and layers on additional capabilities (event source
    /// wiring, simulation, alarms, etc.). Generator-emitted typed
    /// accessor extensions reference only this Core marker so they
    /// compile cleanly inside assemblies that have no server
    /// dependency.
    /// </para>
    /// </remarks>
    public interface INodeStateBuilder
    {
        /// <summary>
        /// The resolved <see cref="NodeState"/> the builder targets.
        /// </summary>
        NodeState Node { get; }

        /// <summary>
        /// Resolves a child of the current node by browse name. Used
        /// by source-generated typed traversal wrappers to walk one
        /// segment at a time without re-resolving from the root.
        /// </summary>
        /// <param name="browseName">Browse name of the immediate child.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown when the child cannot be found.
        /// </exception>
        INodeStateBuilder Child(QualifiedName browseName);

        /// <summary>
        /// Strongly-typed sibling of <see cref="Child(QualifiedName)"/>.
        /// </summary>
        /// <typeparam name="TState">
        /// CLR <see cref="NodeState"/> type the resolved child must be
        /// assignable to.
        /// </typeparam>
        /// <param name="browseName">Browse name of the immediate child.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown when the child cannot be found or is not assignable
        /// to <typeparamref name="TState"/>.
        /// </exception>
        INodeStateBuilder<TState> Child<TState>(QualifiedName browseName)
            where TState : NodeState;
    }

    /// <summary>
    /// Strongly-typed sibling of <see cref="INodeStateBuilder"/> that
    /// pins the wrapped node's CLR type for compile-time safety.
    /// Returned by source-generated typed accessor extensions and by
    /// server-side fluent builders that participate in the typed
    /// pipeline.
    /// </summary>
    /// <typeparam name="TState">
    /// Concrete <see cref="NodeState"/> type the wrapped node is
    /// assignable to.
    /// </typeparam>
    public interface INodeStateBuilder<out TState> : INodeStateBuilder
        where TState : NodeState
    {
        /// <summary>
        /// The resolved typed node.
        /// </summary>
        new TState Node { get; }
    }

    /// <summary>
    /// Typed accessor that exposes the HasComponent children of a
    /// <see cref="NodeState"/>. Used as the entry point for generator-
    /// emitted extension methods that walk one component child per
    /// chained call without forcing the author to spell out
    /// <see cref="QualifiedName"/>s or browse paths.
    /// </summary>
    /// <typeparam name="TState">
    /// Concrete owning state type whose components are surfaced.
    /// </typeparam>
    /// <remarks>
    /// <para>
    /// Lives in <c>Opc.Ua.Types</c> (not <c>Opc.Ua.Server.Fluent</c>)
    /// so that the per-ObjectType accessor extension classes emitted by
    /// the source generator can ship inside model-only assemblies that
    /// don't reference <c>Opc.Ua.Server</c>. The server-side fluent
    /// surface provides bridging extensions
    /// (<c>Components&lt;TState&gt;()</c>) that wrap an
    /// <c>Opc.Ua.Server.Fluent.INodeBuilder&lt;TState&gt;</c> into a
    /// <see cref="IComponentAccessor{TState}"/> view for chaining.
    /// </para>
    /// </remarks>
    public interface IComponentAccessor<out TState>
        where TState : NodeState
    {
        /// <summary>
        /// The underlying typed node builder. Extension methods cast it
        /// down to obtain the per-component browse name and walk to the
        /// child via <see cref="INodeStateBuilder.Child{TState}(QualifiedName)"/>.
        /// </summary>
        INodeStateBuilder<TState> Builder { get; }
    }

    /// <summary>
    /// Typed accessor that exposes the HasProperty children of a
    /// <see cref="NodeState"/>. Sibling of
    /// <see cref="IComponentAccessor{TState}"/> for OPC UA
    /// <c>HasProperty</c> references (the model distinguishes the two
    /// because properties are leaves that participate in value reads
    /// while components recurse).
    /// </summary>
    /// <typeparam name="TState">
    /// Concrete owning state type whose properties are surfaced.
    /// </typeparam>
    public interface IPropertyAccessor<out TState>
        where TState : NodeState
    {
        /// <summary>
        /// The underlying typed node builder.
        /// </summary>
        INodeStateBuilder<TState> Builder { get; }
    }
}
