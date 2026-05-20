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

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// Event payload raised by <c>IAliasNameStore.Changed</c> after
    /// any successful mutation that affects the contents of one of the
    /// store's categories. The new <see cref="LastChange"/> value reflects
    /// the updated Part 17 <c>LastChange</c> property (Part 17 §6.3.1) and
    /// MUST advance monotonically per category — though wraparound of the
    /// underlying <c>VersionTime</c> (<c>uint</c>) is permitted, so
    /// consumers comparing values should test for inequality rather than
    /// strict greater-than.
    /// </summary>
    public sealed class AliasStoreChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the
        /// <see cref="AliasStoreChangedEventArgs"/> class.
        /// </summary>
        /// <param name="categoryId">The <see cref="NodeId"/> of the affected
        /// category.</param>
        /// <param name="lastChange">The category's new
        /// <c>LastChange</c> value (<c>VersionTime</c>).</param>
        public AliasStoreChangedEventArgs(NodeId categoryId, uint lastChange)
        {
            if (categoryId.IsNull)
            {
                throw new ArgumentException(
                    "categoryId must not be null.",
                    nameof(categoryId));
            }
            CategoryId = categoryId;
            LastChange = lastChange;
        }

        /// <summary>
        /// The affected category's <see cref="NodeId"/>.
        /// </summary>
        public NodeId CategoryId { get; }

        /// <summary>
        /// The category's new <c>LastChange</c> value.
        /// </summary>
        public uint LastChange { get; }
    }
}
