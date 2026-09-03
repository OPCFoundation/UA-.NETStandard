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
