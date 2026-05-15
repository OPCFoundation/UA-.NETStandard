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
    /// Default <see cref="IVariableBuilder{TValue}"/> implementation.
    /// Wraps each typed convenience overload as the appropriate slot
    /// delegate on the underlying <see cref="BaseVariableState"/> and
    /// performs <see cref="Variant"/> marshalling on every read / write.
    /// </summary>
    /// <typeparam name="TValue"></typeparam>
    internal sealed class VariableBuilder<TValue> :
        NodeBuilder<BaseVariableState>,
        IVariableBuilder<TValue>
    {
        public VariableBuilder(INodeManagerBuilder parent, BaseVariableState variable)
            : base((NodeManagerBuilder)parent, variable)
        {
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> OnRead(Func<TValue> getter)
        {
            if (getter == null)
            {
                throw new ArgumentNullException(nameof(getter));
            }
            OnRead((ISystemContext _) => getter());
            return this;
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> OnRead(Func<ISystemContext, TValue> getter)
        {
            if (getter == null)
            {
                throw new ArgumentNullException(nameof(getter));
            }
            ((INodeBuilder)this).OnRead(
                (ISystemContext context, NodeState _, ref Variant value) =>
                {
                    value = ToVariant(getter(context));
                    return ServiceResult.Good;
                });
            return this;
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> OnRead(
            Func<CancellationToken, ValueTask<TValue>> getter)
        {
            if (getter == null)
            {
                throw new ArgumentNullException(nameof(getter));
            }
            return OnRead(
                (ISystemContext _, CancellationToken ct) => getter(ct));
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> OnRead(
            Func<ISystemContext, CancellationToken, ValueTask<TValue>> getter)
        {
            if (getter == null)
            {
                throw new ArgumentNullException(nameof(getter));
            }
            ((INodeBuilder)this).OnRead(
                async (ISystemContext context, NodeState _, CancellationToken ct) =>
                {
                    TValue typed = await getter(context, ct).ConfigureAwait(false);
                    return new AttributeSimpleReadResult(
                        ServiceResult.Good,
                        ToVariant(typed));
                });
            return this;
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> OnWrite(Action<TValue> setter)
        {
            if (setter == null)
            {
                throw new ArgumentNullException(nameof(setter));
            }
            OnWrite((ISystemContext _, TValue v) => setter(v));
            return this;
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> OnWrite(Action<ISystemContext, TValue> setter)
        {
            if (setter == null)
            {
                throw new ArgumentNullException(nameof(setter));
            }
            ((INodeBuilder)this).OnWrite(
                (ISystemContext context, NodeState _, ref Variant value) =>
                {
                    setter(context, FromVariant(value));
                    return ServiceResult.Good;
                });
            return this;
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> OnWrite(
            Func<TValue, CancellationToken, ValueTask> setter)
        {
            if (setter == null)
            {
                throw new ArgumentNullException(nameof(setter));
            }
            return OnWrite((ISystemContext _, TValue v, CancellationToken ct) => setter(v, ct));
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> OnWrite(
            Func<ISystemContext, TValue, CancellationToken, ValueTask> setter)
        {
            if (setter == null)
            {
                throw new ArgumentNullException(nameof(setter));
            }
            ((INodeBuilder)this).OnWrite(
                async (ISystemContext context, NodeState _, Variant value, CancellationToken ct) =>
                {
                    TValue typed = FromVariant(value);
                    await setter(context, typed, ct).ConfigureAwait(false);
                    return new AttributeWriteResult(ServiceResult.Good);
                });
            return this;
        }

        /// <summary>
        /// Casts the boxed <see cref="Variant"/> to
        /// <typeparamref name="TValue"/>. Returns the default value when
        /// the variant is null so a typed lambda never has to defend
        /// against an empty variable.
        /// </summary>
        private static TValue FromVariant(Variant value)
        {
            object boxed = value.AsBoxedObject(Variant.BoxingBehavior.Legacy);
            if (boxed is null)
            {
                return default;
            }
            if (boxed is TValue typed)
            {
                return typed;
            }
            // Fall through to checked cast — gives a clear InvalidCastException
            // (vs. a confusing pattern-match miss) when the model and the user
            // disagree about the variable type.
            return (TValue)boxed;
        }

        // The reflection-based Variant(object) constructor is the only
        // generic entry point for an open-ended TValue. AOT users should
        // prefer the per-type generated walker (FluentBuilderGenerator) which
        // routes through the typed Variant.From overloads instead. The
        // suppression is scoped to this single call site so trim/AOT
        // analysis still tracks every other path through this assembly.
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "Generic typed-variable bridge requires the reflection-based Variant(object) constructor.")]
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "Generic typed-variable bridge requires the reflection-based Variant(object) constructor.")]
        private static Variant ToVariant(TValue value)
        {
            if (value is null)
            {
                return Variant.Null;
            }
#pragma warning disable CS0618 // Variant(object) is obsolete on net472
            return new Variant(value);
#pragma warning restore CS0618
        }
    }
}
