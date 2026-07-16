/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Strongly-typed view of a variable node whose value attribute is
    /// known at compile time to carry <typeparamref name="TValue"/>.
    /// Surfaces convenience overloads of <c>OnRead</c>/<c>OnWrite</c>
    /// that accept simple <see cref="Func{TResult}"/> /
    /// <see cref="Action{T}"/> shaped lambdas — the framework marshals
    /// to and from <see cref="Variant"/> automatically.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Obtained via
    /// <see cref="INodeManagerBuilder.Variable{TValue}(string)"/>,
    /// <see cref="INodeManagerBuilder.Variable{TValue}(NodeId)"/> or by
    /// calling <see cref="VariableBuilderExtensions.AsVariable{TValue}(INodeBuilder)"/>
    /// on an existing <see cref="INodeBuilder"/>.
    /// </para>
    /// <para>
    /// The simple convenience overloads do not surface index range, data
    /// encoding, or per-call status code information; use the underlying
    /// <see cref="INodeBuilder"/> overloads (e.g.
    /// <see cref="INodeBuilder.OnRead(NodeValueEventHandlerAsync)"/>) when
    /// that level of control is needed.
    /// </para>
    /// </remarks>
    /// <typeparam name="TValue">
    /// CLR type that the variable's <c>Value</c> attribute is expected to
    /// carry. Reads cast the underlying <see cref="Variant"/> to
    /// this type; writes wrap the supplied value in a new
    /// <see cref="Variant"/>.
    /// </typeparam>
    public interface IVariableBuilder<TValue> : INodeBuilder<BaseVariableState>
    {
        /// <summary>
        /// Wires a synchronous typed getter. The framework invokes
        /// <paramref name="getter"/> on every read and applies index range
        /// / data encoding / copy policy post-processing to the returned
        /// value.
        /// </summary>
        IVariableBuilder<TValue> OnRead(Func<TValue> getter);

        /// <summary>
        /// Wires a synchronous typed getter that receives the calling
        /// <see cref="ISystemContext"/>.
        /// </summary>
        IVariableBuilder<TValue> OnRead(Func<ISystemContext, TValue> getter);

        /// <summary>
        /// Wires an asynchronous typed getter. The handler runs without
        /// holding <c>lock(this)</c>; the framework still applies index
        /// range / data encoding / copy policy post-processing.
        /// </summary>
        IVariableBuilder<TValue> OnRead(
            Func<CancellationToken, ValueTask<TValue>> getter);

        /// <summary>
        /// Wires an asynchronous typed getter that receives the calling
        /// <see cref="ISystemContext"/> and a <see cref="CancellationToken"/>.
        /// </summary>
        IVariableBuilder<TValue> OnRead(
            Func<ISystemContext, CancellationToken, ValueTask<TValue>> getter);

        /// <summary>
        /// Wires a synchronous typed setter. Index-range writes are not
        /// supported through this overload (use the
        /// <see cref="NodeValueEventHandler"/> overload on
        /// <see cref="INodeBuilder.OnWrite(NodeValueEventHandler)"/>).
        /// </summary>
        IVariableBuilder<TValue> OnWrite(Action<TValue> setter);

        /// <summary>
        /// Wires a synchronous typed setter that receives the calling
        /// <see cref="ISystemContext"/>.
        /// </summary>
        IVariableBuilder<TValue> OnWrite(Action<ISystemContext, TValue> setter);

        /// <summary>
        /// Wires an asynchronous typed setter. Runs without holding
        /// <c>lock(this)</c>.
        /// </summary>
        IVariableBuilder<TValue> OnWrite(
            Func<TValue, CancellationToken, ValueTask> setter);

        /// <summary>
        /// Wires an asynchronous typed setter that receives the calling
        /// <see cref="ISystemContext"/>.
        /// </summary>
        IVariableBuilder<TValue> OnWrite(
            Func<ISystemContext, TValue, CancellationToken, ValueTask> setter);
    }

    /// <summary>
    /// Convenience extensions that bridge a non-generic
    /// <see cref="INodeBuilder"/> into the strongly-typed
    /// <see cref="IVariableBuilder{TValue}"/> view.
    /// </summary>
    public static class VariableBuilderExtensions
    {
        /// <summary>
        /// Returns a <see cref="IVariableBuilder{TValue}"/> view of the
        /// resolved node. Throws if the node is not a
        /// <see cref="BaseVariableState"/>.
        /// </summary>
        /// <typeparam name="TValue">
        /// CLR type carried by the variable's <c>Value</c> attribute.
        /// </typeparam>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="builder"/> is null.
        /// </exception>
        /// <exception cref="ServiceResultException">
        /// The resolved node is not a <see cref="BaseVariableState"/>.
        /// </exception>
        public static IVariableBuilder<TValue> AsVariable<TValue>(this INodeBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (builder.Node is not BaseVariableState variable)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "Cannot get a typed variable view of node '{0}': not a BaseVariableState (actual: {1}).",
                    builder.Node.BrowseName,
                    builder.Node.GetType().Name);
            }

            return new VariableBuilder<TValue>(builder.Builder, variable);
        }
    }
}
