// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// Snapshot and restore interface
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface ISnapshotRestore<T>
    {
        /// <summary>
        /// Restore
        /// </summary>
        /// <param name="state"></param>
        void Restore(T state);

        /// <summary>
        /// Get state to serialize
        /// </summary>
        /// <returns></returns>
        void Snapshot(out T state);
    }
}
