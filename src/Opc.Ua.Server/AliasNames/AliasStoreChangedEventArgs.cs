// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
