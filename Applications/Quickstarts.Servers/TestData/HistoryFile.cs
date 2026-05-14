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
using System.Collections.Generic;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;

namespace TestData
{
    /// <summary>
    /// Wraps a file which contains a list of historical values.
    /// </summary>
    internal sealed class HistoryFile : IHistoryDataSource
    {
        /// <summary>
        /// Creates a new file.
        /// </summary>
        internal HistoryFile(Lock dataLock, List<HistoryEntry> entries)
        {
            m_lock = dataLock;
            m_entries = entries;
        }

        /// <summary>
        /// Returns the next value in the archive.
        /// </summary>
        /// <param name="startTime">The starting time for the search.</param>
        /// <param name="isForward">Whether to search forward in time.</param>
        /// <param name="isReadModified">Whether to return modified data.</param>
        /// <param name="position">A index that must be passed to the NextRaw call. </param>
        /// <returns>The DataValue.</returns>
        public DataValue FirstRaw(
            DateTimeUtc startTime,
            bool isForward,
            bool isReadModified,
            out int position)
        {
            position = -1;

            lock (m_lock)
            {
                if (isForward)
                {
                    for (int ii = 0; ii < m_entries.Count; ii++)
                    {
                        if (m_entries[ii].Value.ServerTimestamp >= startTime)
                        {
                            position = ii;
                            break;
                        }
                    }
                }
                else
                {
                    for (int ii = m_entries.Count - 1; ii >= 0; ii--)
                    {
                        if (m_entries[ii].Value.ServerTimestamp <= startTime)
                        {
                            position = ii;
                            break;
                        }
                    }
                }

                if (position < 0 || position >= m_entries.Count)
                {
                    return null;
                }

                HistoryEntry entry = m_entries[position];

                return new DataValue
                {
                    WrappedValue = entry.Value.WrappedValue,
                    ServerTimestamp = entry.Value.ServerTimestamp,
                    SourceTimestamp = entry.Value.SourceTimestamp,
                    StatusCode = entry.Value.StatusCode
                };
            }
        }

        /// <summary>
        /// Returns the next value in the archive.
        /// </summary>
        /// <param name="lastTime">The timestamp of the last value returned.</param>
        /// <param name="isForward">Whether to search forward in time.</param>
        /// <param name="isReadModified">Whether to return modified data.</param>
        /// <param name="position">An index previously returned by the reader.</param>
        /// <returns>The DataValue.</returns>
        public DataValue NextRaw(
            DateTimeUtc lastTime,
            bool isForward,
            bool isReadModified,
            ref int position)
        {
            position++;

            lock (m_lock)
            {
                if (position < 0 || position >= m_entries.Count)
                {
                    return null;
                }

                HistoryEntry entry = m_entries[position];

                return new DataValue
                {
                    WrappedValue = entry.Value.WrappedValue,
                    ServerTimestamp = entry.Value.ServerTimestamp,
                    SourceTimestamp = entry.Value.SourceTimestamp,
                    StatusCode = entry.Value.StatusCode
                };
            }
        }

        /// <summary>
        /// Inserts a new value into the archive at the value's source timestamp.
        /// Returns BadEntryExists if a non-modified entry already exists at that
        /// timestamp.
        /// </summary>
        public StatusCode InsertRaw(DataValue value)
        {
            if (value == null)
            {
                return StatusCodes.BadInvalidArgument;
            }

            lock (m_lock)
            {
                int existing = FindBySourceTimestamp(value.SourceTimestamp.ToDateTime());
                if (existing >= 0)
                {
                    return StatusCodes.BadEntryExists;
                }
                InsertSorted(value);
                return StatusCodes.GoodEntryInserted;
            }
        }

        /// <summary>
        /// Replaces an existing value at the given source timestamp. Returns
        /// BadNoEntryExists when nothing matches.
        /// </summary>
        public StatusCode ReplaceRaw(DataValue value)
        {
            if (value == null)
            {
                return StatusCodes.BadInvalidArgument;
            }

            lock (m_lock)
            {
                int existing = FindBySourceTimestamp(value.SourceTimestamp.ToDateTime());
                if (existing < 0)
                {
                    return StatusCodes.BadNoEntryExists;
                }
                m_entries[existing] = NewEntry(value, isModified: true);
                return StatusCodes.GoodEntryReplaced;
            }
        }

        /// <summary>
        /// Inserts the value if no entry exists at the timestamp; otherwise
        /// replaces it. Mirrors PerformUpdateType.Update semantics.
        /// </summary>
        public StatusCode UpsertRaw(DataValue value)
        {
            if (value == null)
            {
                return StatusCodes.BadInvalidArgument;
            }

            lock (m_lock)
            {
                int existing = FindBySourceTimestamp(value.SourceTimestamp.ToDateTime());
                if (existing >= 0)
                {
                    m_entries[existing] = NewEntry(value, isModified: true);
                    return StatusCodes.GoodEntryReplaced;
                }
                InsertSorted(value);
                return StatusCodes.GoodEntryInserted;
            }
        }

        /// <summary>
        /// Deletes raw entries whose source timestamp is in [startTime, endTime).
        /// </summary>
        public StatusCode DeleteRaw(DateTime startTime, DateTime endTime)
        {
            lock (m_lock)
            {
                int removed = m_entries.RemoveAll(e =>
                    e.Value.SourceTimestamp.ToDateTime() >= startTime
                    && e.Value.SourceTimestamp.ToDateTime() < endTime);
                return removed > 0
                    ? StatusCodes.Good
                    : StatusCodes.GoodNoData;
            }
        }

        /// <summary>
        /// Deletes a single entry at the specified source timestamp. Returns
        /// BadNoEntryExists when no entry matches.
        /// </summary>
        public StatusCode DeleteAtTime(DateTime sourceTimestamp)
        {
            lock (m_lock)
            {
                int existing = FindBySourceTimestamp(sourceTimestamp);
                if (existing < 0)
                {
                    return StatusCodes.BadNoEntryExists;
                }
                m_entries.RemoveAt(existing);
                return StatusCodes.Good;
            }
        }

        private int FindBySourceTimestamp(DateTime sourceTimestamp)
        {
            for (int i = 0; i < m_entries.Count; i++)
            {
                if (m_entries[i].Value.SourceTimestamp.ToDateTime() == sourceTimestamp)
                {
                    return i;
                }
            }
            return -1;
        }

        private void InsertSorted(DataValue value)
        {
            HistoryEntry entry = NewEntry(value, isModified: false);

            // Maintain sort by source timestamp (ascending).
            int idx = m_entries.Count;
            for (int i = 0; i < m_entries.Count; i++)
            {
                if (m_entries[i].Value.SourceTimestamp.ToDateTime() > entry.Value.SourceTimestamp.ToDateTime())
                {
                    idx = i;
                    break;
                }
            }
            m_entries.Insert(idx, entry);
        }

        private static HistoryEntry NewEntry(DataValue value, bool isModified)
        {
            return new HistoryEntry
            {
                Value = new DataValue
                {
                    WrappedValue = value.WrappedValue,
                    SourceTimestamp = value.SourceTimestamp,
                    ServerTimestamp = value.ServerTimestamp != DateTime.MinValue
                        ? value.ServerTimestamp
                        : DateTime.UtcNow,
                    StatusCode = value.StatusCode
                },
                IsModified = isModified
            };
        }

        private readonly Lock m_lock = new();
        private readonly List<HistoryEntry> m_entries;
    }
}
