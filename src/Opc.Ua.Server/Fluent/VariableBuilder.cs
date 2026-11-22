// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
    /// <typeparam name="TValue">
    /// CLR type carried by the variable's <c>Value</c> attribute.
    /// </typeparam>
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
            OnRead(_ => getter());
            return this;
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> OnRead(Func<ISystemContext, TValue> getter)
        {
            if (getter == null)
            {
                throw new ArgumentNullException(nameof(getter));
            }
            OnRead(
                (context, _, ref value) =>
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
                (_, ct) => getter(ct));
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> OnRead(
            Func<ISystemContext, CancellationToken, ValueTask<TValue>> getter)
        {
            if (getter == null)
            {
                throw new ArgumentNullException(nameof(getter));
            }
            OnRead(
                async (context, _, ct) =>
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
            OnWrite((_, v) => setter(v));
            return this;
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> OnWrite(Action<ISystemContext, TValue> setter)
        {
            if (setter == null)
            {
                throw new ArgumentNullException(nameof(setter));
            }
            OnWrite(
                (context, _, ref value) =>
                {
                    setter(context, FromVariant(value)!);
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
            return OnWrite((_, v, ct) => setter(v, ct));
        }

        /// <inheritdoc/>
        public IVariableBuilder<TValue> OnWrite(
            Func<ISystemContext, TValue, CancellationToken, ValueTask> setter)
        {
            if (setter == null)
            {
                throw new ArgumentNullException(nameof(setter));
            }
            OnWrite(
                async (context, _, value, ct) =>
                {
                    TValue? typed = FromVariant(value);
                    await setter(context, typed!, ct).ConfigureAwait(false);
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
        private static TValue? FromVariant(Variant value)
        {
            object? boxed = value.AsBoxedObject(Variant.BoxingBehavior.Legacy);
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

        /// <summary>
        /// Wraps <paramref name="value"/> in a <see cref="Variant"/> via
        /// the shared <see cref="FluentVariant.ToVariant{TValue}(TValue)"/>
        /// helper (single AOT/trim suppression site).
        /// </summary>
        private static Variant ToVariant(TValue value)
        {
            return FluentVariant.ToVariant(value);
        }
    }
}
