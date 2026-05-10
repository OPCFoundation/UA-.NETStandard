/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Extensibility surface for OPC UA Part 11 historical data access. A
    /// node manager that wants to expose history for a variable returns an
    /// <see cref="IHistoryDataSource"/> from its history-read / history-update
    /// callbacks; the framework then drives reads and updates through this
    /// interface. The default in-process implementation backing the
    /// <c>Quickstarts.Servers</c> reference server is <c>HistoryFile</c> in
    /// the <c>TestData</c> namespace; integrators can plug their own historian
    /// (database, time-series store, on-disk archive, …) by implementing this
    /// interface and returning an instance from their node manager.
    /// </summary>
    public interface IHistoryDataSource
    {
        /// <summary>
        /// Returns the next value in the archive.
        /// </summary>
        /// <param name="startTime">The starting time for the search.</param>
        /// <param name="isForward">Whether to search forward in time.</param>
        /// <param name="isReadModified">Whether to return modified data.</param>
        /// <param name="position">A index that must be passed to the NextRaw call.</param>
        /// <returns>The DataValue.</returns>
        DataValue FirstRaw(
            DateTimeUtc startTime,
            bool isForward,
            bool isReadModified,
            out int position);

        /// <summary>
        /// Returns the next value in the archive.
        /// </summary>
        /// <param name="lastTime">The timestamp of the last value returned.</param>
        /// <param name="isForward">Whether to search forward in time.</param>
        /// <param name="isReadModified">Whether to return modified data.</param>
        /// <param name="position">A index previously returned by the reader.</param>
        /// <returns>The DataValue.</returns>
        DataValue NextRaw(DateTimeUtc lastTime, bool isForward, bool isReadModified, ref int position);

        /// <summary>
        /// Inserts a new value at the value's source timestamp; returns
        /// BadEntryExists if a non-modified entry already occupies that slot.
        /// </summary>
        StatusCode InsertRaw(DataValue value);

        /// <summary>
        /// Replaces an existing value at the source timestamp; returns
        /// BadNoEntryExists when nothing matches.
        /// </summary>
        StatusCode ReplaceRaw(DataValue value);

        /// <summary>
        /// Inserts the value when no entry exists at the timestamp;
        /// otherwise replaces it. Mirrors PerformUpdateType.Update.
        /// </summary>
        StatusCode UpsertRaw(DataValue value);

        /// <summary>
        /// Deletes raw entries whose source timestamp is in [startTime, endTime).
        /// </summary>
        StatusCode DeleteRaw(DateTime startTime, DateTime endTime);

        /// <summary>
        /// Deletes a single entry at the specified source timestamp.
        /// </summary>
        StatusCode DeleteAtTime(DateTime sourceTimestamp);
    }
}
