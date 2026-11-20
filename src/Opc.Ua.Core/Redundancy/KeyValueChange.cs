/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: the kind of mutation reported by a <see cref="KeyValueChange"/>.
    /// </summary>
    public enum KeyValueChangeKind
    {
        /// <summary>
        /// The key was created or updated to a new value.
        /// </summary>
        Set,

        /// <summary>
        /// The key was removed.
        /// </summary>
        Delete
    }

    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a single change observed on an <see cref="ISharedKeyValueStore"/>
    /// change-feed (<see cref="ISharedKeyValueStore.WatchAsync"/>).
    /// </summary>
    public sealed record KeyValueChange
    {
        /// <summary>
        /// The kind of mutation.
        /// </summary>
        public KeyValueChangeKind Kind { get; init; }

        /// <summary>
        /// The affected key.
        /// </summary>
        public string Key { get; init; } = string.Empty;

        /// <summary>
        /// The new value for <see cref="KeyValueChangeKind.Set"/> changes;
        /// a null <see cref="ByteString"/> for deletions.
        /// </summary>
        public ByteString Value { get; init; }
    }
}
