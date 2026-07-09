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

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Shared <see cref="Variant"/> marshalling helper used by the fluent
    /// variable surface (typed <c>OnRead</c> getters, the runtime
    /// <see cref="IValueUpdater{TValue}"/> handle, and auto-sampling).
    /// Centralises the single reflection-based boxing entry point so the
    /// AOT/trimming suppression lives in exactly one place.
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
        /// The reflection-based <c>Variant(object)</c> constructor is the
        /// only generic entry point for an open-ended <typeparamref name="TValue"/>.
        /// AOT users should prefer the per-type generated walker
        /// (<c>FluentBuilderGenerator</c>) which routes through the typed
        /// <c>Variant.From</c> overloads instead. The suppression is scoped
        /// to this single call site so trim/AOT analysis still tracks every
        /// other path through this assembly.
        /// </remarks>
        /// <typeparam name="TValue">
        /// CLR type of the value being wrapped.
        /// </typeparam>
        /// <param name="value">The value to wrap.</param>
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Trimming",
            "IL2026:RequiresUnreferencedCode",
            Justification = "Generic typed-variable bridge requires the reflection-based Variant(object) constructor.")]
        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "AOT",
            "IL3050:RequiresDynamicCode",
            Justification = "Generic typed-variable bridge requires the reflection-based Variant(object) constructor.")]
        public static Variant ToVariant<TValue>(TValue value)
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
