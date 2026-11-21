// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Shared <see cref="Variant"/> marshalling helper used by the fluent
    /// variable surface (typed <c>OnRead</c> getters, the runtime
    /// <see cref="IValueUpdater{TValue}"/> handle, and auto-sampling).
    /// Routes every typed value through the statically-registered
    /// <see cref="IVariantBuilder{T}"/> implementations so the bridge stays
    /// free of the obsolete <c>Variant(object)</c> constructor and of
    /// reflection.
    /// </summary>
    internal static class FluentVariant
    {
        /// <summary>
        /// Wraps <paramref name="value"/> in a <see cref="Variant"/>.
        /// Returns <see cref="Variant.Null"/> for a <see langword="null"/>
        /// input so a typed lambda never has to defend against an empty
        /// variable.
        /// </summary>
        /// <remarks>
        /// Built-in OPC UA types (booleans, numerics, strings,
        /// <see cref="ByteString"/>, <see cref="NodeId"/>,
        /// <see cref="ArrayOf{T}"/>, <see cref="MatrixOf{T}"/>, …) are
        /// converted through the explicit <see cref="IVariantBuilder{T}"/>
        /// implementations on <see cref="VariantBuilder"/>; enumerations map
        /// onto their Int32 wire representation. Both paths are CS0618- and
        /// reflection-free, so no trim/AOT suppression is required. The
        /// <see cref="IVariantBuilder{T}"/> resolution is cached per closed
        /// <typeparamref name="TValue"/> in <see cref="Builder{T}"/> so the
        /// one-time interface probe is JIT-specialized instead of repeated on
        /// every call.
        /// </remarks>
        /// <typeparam name="TValue">
        /// CLR type of the value being wrapped.
        /// </typeparam>
        /// <param name="value">The value to wrap.</param>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotSupported"/> when
        /// <typeparamref name="TValue"/> is neither a built-in
        /// Variant-convertible type nor an enumeration.
        /// </exception>
        public static Variant ToVariant<TValue>(TValue value)
        {
            if (value is null)
            {
                return Variant.Null;
            }
            // Built-in Variant-convertible types route through the statically
            // registered IVariantBuilder<T> implementations on VariantBuilder,
            // resolved once per closed TValue by the generic cache below.
            IVariantBuilder<TValue>? builder = Builder<TValue>.Instance;
            if (builder != null)
            {
                return builder.WithValue(value);
            }
            // OPC UA enumerations map onto their Int32 wire representation.
            if (value is Enum)
            {
                return Variant.From(EnumValue.From(value, typeof(TValue)));
            }
            throw ServiceResultException.Create(
                StatusCodes.BadNotSupported,
                "Cannot marshal a value of type '{0}' to a Variant through the " +
                "fluent typed surface. Use a built-in OPC UA type, an enum, or " +
                "wrap the value in an ExtensionObject.",
                typeof(TValue).FullName ?? typeof(TValue).Name);
        }

        /// <summary>
        /// Per-<typeparamref name="T"/> cache of the boxed
        /// <see cref="VariantBuilder"/> viewed as <see cref="IVariantBuilder{T}"/>.
        /// The interface probe (and the single boxing it entails) runs once per
        /// closed generic type on first use; <see langword="null"/> means
        /// <see cref="VariantBuilder"/> does not implement
        /// <see cref="IVariantBuilder{T}"/> for that type.
        /// </summary>
        /// <typeparam name="T">
        /// CLR type the cached builder converts.
        /// </typeparam>
        private static class Builder<T>
        {
            public static readonly IVariantBuilder<T>? Instance =
                default(VariantBuilder) as IVariantBuilder<T>;
        }
    }
}
