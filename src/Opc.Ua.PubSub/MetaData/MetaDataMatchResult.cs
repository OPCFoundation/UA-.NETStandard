/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.MetaData
{
    /// <summary>
    /// Outcome of an <see cref="IDataSetMetaDataRegistry.TryGet"/> lookup.
    /// Distinguishes the three reasons a lookup may not return a
    /// directly-usable entry: nothing registered, a backward-compatible
    /// version drift, or a breaking version drift.
    /// </summary>
    /// <remarks>
    /// Implements the metadata-version reconciliation rules of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.9.4">
    /// Part 14 §6.2.9.4 DataSetReader DataSetMetaData</see>.
    /// </remarks>
    public enum MetaDataMatchResult
    {
        /// <summary>
        /// An entry exists with the exact MajorVersion and at least the
        /// requested MinorVersion; the returned <see cref="DataSetMetaDataType"/>
        /// is safe to use to decode payloads bound to the key.
        /// </summary>
        Match,

        /// <summary>
        /// An entry exists for the key but its MinorVersion differs from
        /// the requested MinorVersion. Per Part 14 §6.2.9.4 this is a
        /// soft-update path: the registered metadata may still decode the
        /// payload but the registry should be refreshed at the earliest
        /// opportunity.
        /// </summary>
        MinorVersionMismatch,

        /// <summary>
        /// An entry exists for the key tuple but its MajorVersion differs
        /// from the requested MajorVersion. This is a breaking change;
        /// callers must reject the payload and trigger a metadata
        /// re-acquisition before continuing.
        /// </summary>
        MajorVersionMismatch,

        /// <summary>
        /// No entry exists for the requested key tuple.
        /// </summary>
        NotFound
    }
}
