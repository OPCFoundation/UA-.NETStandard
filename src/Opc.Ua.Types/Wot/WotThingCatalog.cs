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
 *
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

using System.Collections.Generic;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Tracks generated Thing Description references so WoT conversion can preserve stable NodeIds
    /// when multiple documents reference the same Thing.
    /// </summary>
    internal sealed class WotThingCatalog
    {
        /// <summary>
        /// Adds a generated NodeId candidate for a Thing reference.
        /// </summary>
        /// <param name="reference">The Thing reference used as the catalog key.</param>
        /// <param name="nodeId">
        /// The NodeId associated with the reference, or <c>null</c> when none was generated.
        /// </param>
        public void Add(string reference, string? nodeId)
        {
            if (!m_entries.TryGetValue(reference, out Queue<string?>? entries))
            {
                entries = new Queue<string?>();
                m_entries.Add(reference, entries);
            }
            entries.Enqueue(nodeId);
        }

        /// <summary>
        /// Removes the next NodeId candidate for a Thing reference while preserving insertion order.
        /// </summary>
        /// <param name="reference">The Thing reference to resolve.</param>
        /// <param name="nodeId">The next catalogued NodeId, or <c>null</c> when none was stored.</param>
        /// <returns><c>true</c> when a catalogued entry was available.</returns>
        public bool TryTake(string reference, out string? nodeId)
        {
            if (m_entries.TryGetValue(reference, out Queue<string?>? entries) &&
                entries.Count > 0)
            {
                nodeId = entries.Dequeue();
                return true;
            }
            nodeId = null;
            return false;
        }

        private readonly Dictionary<string, Queue<string?>> m_entries = new(System.StringComparer.Ordinal);
    }
}
