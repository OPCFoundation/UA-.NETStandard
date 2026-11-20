// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.OpenUsd.Scene
{
    /// <summary>
    /// The shape a <see cref="UsdValue"/> carries.
    /// </summary>
    /// <remarks>
    /// The kinds mirror what a <c>.usda</c> document can author for an attribute
    /// value. Several kinds share a CLR representation but are kept apart because
    /// USD prints them differently: a tuple is <c>(1, 2, 3)</c> while an array is
    /// <c>[1, 2, 3]</c>, and a token is bare where a string is quoted.
    /// </remarks>
    public enum UsdValueKind
    {
        /// <summary>
        /// No authored value.
        /// </summary>
        Null = 0,

        /// <summary>
        /// <c>true</c> or <c>false</c>.
        /// </summary>
        Boolean,

        /// <summary>
        /// An integral value.
        /// </summary>
        Integer,

        /// <summary>
        /// A floating point value.
        /// </summary>
        Double,

        /// <summary>
        /// A quoted string.
        /// </summary>
        String,

        /// <summary>
        /// A bare word, such as an enumerator or an interpolation mode.
        /// </summary>
        Token,

        /// <summary>
        /// An asset path, authored as <c>@path@</c>.
        /// </summary>
        AssetPath,

        /// <summary>
        /// A prim path reference, authored as <c>&lt;/Path&gt;</c>.
        /// </summary>
        PathReference,

        /// <summary>
        /// A fixed arity group such as <c>float3</c>, authored as <c>(a, b, c)</c>.
        /// </summary>
        Tuple,

        /// <summary>
        /// A sequence, authored as <c>[a, b, c]</c>.
        /// </summary>
        Array,

        /// <summary>
        /// A matrix, authored as a tuple of row tuples.
        /// </summary>
        Matrix,

        /// <summary>
        /// A nested metadata dictionary, authored as <c>{ ... }</c>.
        /// </summary>
        Dictionary
    }
}
