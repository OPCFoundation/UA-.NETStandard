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
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Aas
{
    /// <summary>
    /// Represents one optional AAS metamodel field without collapsing
    /// <c>absent</c> and <c>present but empty</c>.
    /// </summary>
    /// <remarks>
    /// Clause 6.1.5 makes node presence part of the value: an absent optional
    /// field has no node, while a present empty collection has a node whose
    /// value is an empty array and a present empty object has an Object node
    /// with no children. This wrapper records that presence bit explicitly.
    /// For a collection field, use <see cref="Present(T)"/> with
    /// <c>ArrayOf&lt;TElement&gt;.Empty</c> to model "present but empty"; use
    /// <see cref="Absent"/> to model "absent".
    /// </remarks>
    /// <typeparam name="T">The field value type.</typeparam>
    public readonly record struct AasOptional<T>
        where T : notnull
    {
        /// <summary>
        /// Initializes a present optional field.
        /// </summary>
        /// <param name="value">The present field value.</param>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
        private AasOptional(T value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            IsPresent = true;
            m_value = value;
        }

        /// <summary>
        /// Gets the absent field value.
        /// </summary>
        public static AasOptional<T> Absent => default;

        /// <summary>
        /// Gets whether the metamodel field is present and must therefore
        /// materialize a node.
        /// </summary>
        public bool IsPresent { get; }

        /// <summary>
        /// Gets the present value.
        /// </summary>
        /// <exception cref="InvalidOperationException">The field is absent.</exception>
        public T Value
        {
            get
            {
                if (!IsPresent)
                {
                    throw new InvalidOperationException("The optional AAS field is absent.");
                }

                return m_value;
            }
        }

        /// <summary>
        /// Creates a present optional field.
        /// </summary>
        /// <param name="value">The field value to retain.</param>
        /// <returns>A present optional field.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <c>null</c>.</exception>
        public static AasOptional<T> Present(T value)
        {
            return new AasOptional<T>(value);
        }

        /// <summary>
        /// Reads the value without throwing.
        /// </summary>
        /// <param name="value">The present value when the return value is <c>true</c>.</param>
        /// <returns><c>true</c> when the field is present.</returns>
        public bool TryGetValue([NotNullWhen(true)] out T? value)
        {
            value = IsPresent ? m_value : default;
            return IsPresent;
        }

        private readonly T m_value;
    }
}
