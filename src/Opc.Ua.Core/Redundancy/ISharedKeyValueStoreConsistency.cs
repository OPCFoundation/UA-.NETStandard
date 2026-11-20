/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Describes consistency guarantees for keys routed by a shared store.
    /// </summary>
    public interface ISharedKeyValueStoreConsistency
    {
        /// <summary>
        /// Returns whether operations for <paramref name="key"/> are linearizable.
        /// </summary>
        bool IsLinearizable(string key);

        /// <summary>
        /// Returns whether <paramref name="key"/> is visible only in the current process.
        /// </summary>
        bool IsProcessLocal(string key);
    }
}
