/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
