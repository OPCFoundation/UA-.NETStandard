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

namespace Opc.Ua.Server.Fluent
{
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
    /// Obtain an instance via <see cref="TypedAccessorExtensions.Components{TState}"/>:
    /// </para>
    /// <code>
    /// builder.Node&lt;PumpState&gt;("Pump #1")
    ///        .Components().Operational()
    ///        .Components().Measurements()
    ///        .Properties().DifferentialPressure()
    ///        .OnRead(getter);
    /// </code>
    /// <para>
    /// The interface only exposes the wrapped <see cref="Builder"/>; the
    /// per-type accessor methods are emitted as extension methods by the
    /// source generator, keeping this assembly free of model knowledge.
    /// </para>
    /// </remarks>
    public interface IComponentAccessor<out TState>
        where TState : NodeState
    {
        /// <summary>
        /// The underlying typed node builder. Extension methods cast it
        /// down to obtain the per-component browse name and walk to the
        /// child via <see cref="INodeBuilder.Child{TState}(QualifiedName)"/>.
        /// </summary>
        INodeBuilder<TState> Builder { get; }
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
        INodeBuilder<TState> Builder { get; }
    }
}
