// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: which committed commands the consensus delivers to a store after it was
    /// restored from a <see cref="RaftKeyValueSnapshot"/>.
    /// </summary>
    public enum RaftSnapshotLogReplay
    {
        /// <summary>
        /// The consensus delivers only the commands committed after the snapshot, for example because its log was
        /// compacted up to the snapshot or it starts with an empty log. Every delivered command is applied.
        /// </summary>
        AfterSnapshot = 0,

        /// <summary>
        /// The consensus delivers its log again from the first command, for example a replica that restarts from a
        /// persisted, uncompacted log. The store skips the commands the snapshot holds and applies the rest, so no
        /// command is applied twice.
        /// </summary>
        FromFirstCommand = 1
    }
}
