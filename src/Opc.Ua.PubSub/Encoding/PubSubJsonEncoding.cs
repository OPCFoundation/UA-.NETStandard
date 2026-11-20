// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

#if NET5_0_OR_GREATER
using System.Buffers;
#endif
#pragma warning disable CS0618 // Type or member is obsolete

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// The type of JSON encoding to use.
    /// </summary>
    internal enum PubSubJsonEncoding
    {
        /// <summary>
        /// The compact encoding that may require a schema to interpret.
        /// </summary>
        Compact,

        /// <summary>
        /// A verbose encoding that is more useable even without a schema.
        /// </summary>
        Verbose,

        /// <summary>
        /// The reversible encoding supported for backward compatibitility.
        /// </summary>
        Reversible,

        /// <summary>
        /// The non reversible encoding supported for backward compatibitility.
        /// </summary>
        NonReversible
    }
}
