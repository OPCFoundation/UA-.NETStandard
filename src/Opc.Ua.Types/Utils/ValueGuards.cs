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

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Small argument and value guards used by the coordinate transforms.
    /// </summary>
    /// <remarks>
    /// These deliberately live in <c>Opc.Ua</c> rather than in <c>System</c>.
    /// An extension method on <c>System.Object</c> or <c>System.Double</c>
    /// declared in the <c>System</c> namespace would be offered to every
    /// consumer of the stack for every expression, and would compete with the
    /// platform's own helpers as those grow.
    /// </remarks>
    public static class ValueGuards
    {
        /// <summary>
        /// Throws when a reference argument is null, and returns it otherwise.
        /// </summary>
        /// <remarks>
        /// <c>ArgumentNullException.ThrowIfNull</c> is not available on every
        /// target framework the stack builds for, and does not return the
        /// validated reference, which is what lets this be used in a field
        /// initialiser or an expression body.
        /// </remarks>
        /// <typeparam name="T">The reference type to validate.</typeparam>
        /// <param name="target">The reference to validate.</param>
        /// <param name="parameterName">The argument name.</param>
        /// <returns>The non-null reference.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> is <c>null</c>.
        /// </exception>
        public static T ThrowIfNull<T>(
            this T? target,
            string parameterName)
            where T : class
        {
            return target ?? throw new ArgumentNullException(parameterName);
        }

        /// <summary>
        /// Returns true when a double is neither NaN nor infinity.
        /// </summary>
        /// <remarks>
        /// <c>double.IsFinite</c> only exists from .NET Core 3.0 onwards, so
        /// the netstandard2.0 and .NET Framework targets need this.
        /// </remarks>
        /// <param name="target">The value to test.</param>
        /// <returns>True when the value is finite.</returns>
        public static bool IsFinite(this double target)
        {
            return !double.IsNaN(target) && !double.IsInfinity(target);
        }
    }
}
