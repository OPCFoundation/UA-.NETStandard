// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: an item of <see cref="IRaftSnapshotConsensus.CommittedItems"/>, a committed
    /// command or a snapshot, with its log index.
    /// </summary>
    public sealed class RaftCommittedItem
    {
        /// <summary>
        /// Creates an item.
        /// </summary>
        /// <param name="isSnapshot">Whether the item is a snapshot rather than a command.</param>
        /// <param name="index">The log index of the item.</param>
        /// <param name="data">The command, or the encoded state of a snapshot.</param>
        public RaftCommittedItem(bool isSnapshot, ulong index, ByteString data)
        {
            IsSnapshot = isSnapshot;
            Index = index;
            Data = data;
        }

        /// <summary>
        /// <c>true</c> for a snapshot: the state machine replaces its state with <see cref="Data"/>, which holds
        /// every command up to <see cref="Index"/>.
        /// </summary>
        public bool IsSnapshot { get; }

        /// <summary>
        /// The log index of the item; after applying it, the state of the state machine corresponds to this index.
        /// </summary>
        public ulong Index { get; }

        /// <summary>
        /// The command, or the encoded state of a snapshot.
        /// </summary>
        public ByteString Data { get; }
    }
}
