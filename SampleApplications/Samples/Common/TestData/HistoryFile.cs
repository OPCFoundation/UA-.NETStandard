/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Xml;
using System.IO;
using System.Text;
using System.Reflection;
using System.Threading;
using System.Globalization;
using Opc.Ua;
using Opc.Ua.Server;

namespace TestData
{
    /// <summary>
    /// Wraps a file which contains a list of historical values.
    /// </summary>
    internal class HistoryFile : IHistoryDataSource
    {
        #region Constructors
        /// <summary>
        /// Creates a new file.
        /// </summary>
        internal HistoryFile(object dataLock, List<HistoryEntry> entries)
        {
            m_lock = dataLock;
            m_entries = entries;
        }
        #endregion
        
        #region IHistoryReader Members
        /// <summary>
        /// Returns the next value in the archive.
        /// </summary>
        /// <param name="startTime">The starting time for the search.</param>
        /// <param name="isForward">Whether to search forward in time.</param>
        /// <param name="isReadModified">Whether to return modified data.</param>
        /// <param name="position">A index that must be passed to the NextRaw call. </param>
        /// <returns>The DataValue.</returns>
        public DataValue FirstRaw(DateTime startTime, bool isForward, bool isReadModified, out int position)
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
                    for (int ii = m_entries.Count-1; ii >= 0; ii--)
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

                DataValue value = new DataValue();

                value.Value = entry.Value.Value;
                value.ServerTimestamp = entry.Value.ServerTimestamp;
                value.SourceTimestamp = entry.Value.SourceTimestamp;
                value.StatusCode = entry.Value.StatusCode;

                return value;
            }
        }
        
        /// <summary>
        /// Returns the next value in the archive.
        /// </summary>
        /// <param name="lastTime">The timestamp of the last value returned.</param>
        /// <param name="isForward">Whether to search forward in time.</param>
        /// <param name="isReadModified">Whether to return modified data.</param>
        /// <param name="position">A index previously returned by the reader.</param>
        /// <returns>The DataValue.</returns>
        public DataValue NextRaw(DateTime lastTime, bool isForward, bool isReadModified, ref int position)
        {
            position++;

            lock (m_lock)
            {
                if (position < 0 || position >= m_entries.Count)
                {
                    return null;
                }

                HistoryEntry entry = m_entries[position];

                DataValue value = new DataValue();

                value.Value = entry.Value.Value;
                value.ServerTimestamp = entry.Value.ServerTimestamp;
                value.SourceTimestamp = entry.Value.SourceTimestamp;
                value.StatusCode = entry.Value.StatusCode;

                return value;
            }
        }
        #endregion
        
        #region Private Fields
        private object m_lock = new object();
        private List<HistoryEntry> m_entries;
        #endregion
    }
}
