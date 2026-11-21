// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Host-owned ceilings for client workflows; no tool can increase these limits.
    /// </summary>
    public sealed record WorkflowOptions
    {
        /// <summary>
        /// Gets the maximum page or mutation batch size, at most 500.
        /// </summary>
        public int MaxItems { get; init; } = 500;

        /// <summary>
        /// Gets the maximum observation or temporary generation window, at most 30 seconds.
        /// </summary>
        public int MaxDurationMilliseconds { get; init; } = 30_000;

        /// <summary>
        /// Gets the maximum inline data size, at most 64 KiB.
        /// </summary>
        public int MaxInlineBytes { get; init; } = 64 * 1024;

        /// <summary>
        /// Gets the maximum retained historical cursors in this host.
        /// </summary>
        public int MaxHistoryCursors { get; init; } = 16;

        /// <summary>
        /// Gets the maximum retained historical cursors for one trusted caller.
        /// </summary>
        public int MaxHistoryCursorsPerCaller { get; init; } = 4;

        /// <summary>
        /// Gets the idle lifetime of a retained historical cursor.
        /// </summary>
        public TimeSpan HistoryCursorIdleTimeout { get; init; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets the absolute lifetime of a retained historical cursor.
        /// </summary>
        public TimeSpan HistoryCursorLifetime { get; init; } = TimeSpan.FromMinutes(30);

        /// <summary>
        /// Gets the maximum UA pages read by one historical enumeration.
        /// </summary>
        public uint MaxHistoryPages { get; init; } = 1000;

        /// <summary>
        /// Validates immutable configuration before registration.
        /// </summary>
        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxItems, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxItems, 500);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxDurationMilliseconds, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxDurationMilliseconds, 30_000);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxInlineBytes, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxInlineBytes, 64 * 1024);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxHistoryCursors, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxHistoryCursors, 128);
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxHistoryCursorsPerCaller, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxHistoryCursorsPerCaller, MaxHistoryCursors);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(HistoryCursorIdleTimeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(HistoryCursorIdleTimeout, HistoryCursorLifetime);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(HistoryCursorLifetime, TimeSpan.FromMinutes(30));
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxHistoryPages, 1u);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxHistoryPages, 100_000u);
        }
    }
}
