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

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    public partial class Subscription
    {
        /// <summary>
        /// The most triggering links a subscription keeps (OPC 10000-4 §5.13.1.6).
        /// </summary>
        /// <remarks>
        /// Links are pairs of items of one subscription, so without a limit their number
        /// grows with the square of the item count. Every link costs memory and is visited
        /// under the subscription lock when the items it connects are deleted, which the
        /// publish sweep waits for. A link that would exceed the limit is refused with
        /// <see cref="StatusCodes.BadTooManyOperations"/>.
        /// </remarks>
        internal const int kMaxTriggeringLinks = 256 * 1024;

        /// <summary>
        /// The number of triggering links of the subscription.
        /// </summary>
        internal int TriggeringLinkCount
        {
            get
            {
                lock (m_lock)
                {
                    return m_triggeringLinkCount;
                }
            }
        }

        /// <summary>
        /// Records a link to a triggered item.
        /// </summary>
        /// <remarks>The caller holds <c>m_lock</c>.</remarks>
        private void AddTriggeredTarget(uint triggeredItemId)
        {
            m_triggeringLinkCount++;
            m_triggeredTargets.TryGetValue(triggeredItemId, out int links);
            m_triggeredTargets[triggeredItemId] = links + 1;
        }

        /// <summary>
        /// Forgets a link to a triggered item.
        /// </summary>
        /// <remarks>The caller holds <c>m_lock</c>.</remarks>
        private void RemoveTriggeredTarget(uint triggeredItemId)
        {
            m_triggeringLinkCount--;
            if (m_triggeredTargets.TryGetValue(triggeredItemId, out int links))
            {
                if (links <= 1)
                {
                    m_triggeredTargets.Remove(triggeredItemId);
                }
                else
                {
                    m_triggeredTargets[triggeredItemId] = links - 1;
                }
            }
        }

        /// <summary>
        /// Forgets all triggering links.
        /// </summary>
        /// <remarks>The caller holds <c>m_lock</c>.</remarks>
        private void ClearTriggeringLinks()
        {
            m_itemsToTrigger.Clear();
            m_triggeredTargets.Clear();
            m_triggeringLinkCount = 0;
        }

        /// <summary>
        /// Removes the links of a monitored item that is deleted. A deleted item that is
        /// the target of links is collected in <paramref name="deletedTargets"/>; the links
        /// to them are removed together by <see cref="RemoveDeletedTriggeredTargets"/>.
        /// </summary>
        /// <remarks>The caller holds <c>m_lock</c>.</remarks>
        private void RemoveTriggeringLinksOfDeletedItem(uint monitoredItemId, ref HashSet<uint>? deletedTargets)
        {
            if (m_triggeringLinkCount == 0)
            {
                return;
            }

            if (m_itemsToTrigger.TryGetValue(monitoredItemId, out List<ITriggeredMonitoredItem>? triggeredItems))
            {
                m_itemsToTrigger.Remove(monitoredItemId);
                foreach (ITriggeredMonitoredItem triggeredItem in triggeredItems)
                {
                    RemoveTriggeredTarget(triggeredItem.Id);
                }
            }

            if (m_triggeredTargets.ContainsKey(monitoredItemId))
            {
                (deletedTargets ??= []).Add(monitoredItemId);
            }
        }

        /// <summary>
        /// Removes the links to deleted items from every link list in one pass, so a
        /// delete costs the number of links once instead of once per deleted item.
        /// </summary>
        /// <remarks>The caller holds <c>m_lock</c>.</remarks>
        private void RemoveDeletedTriggeredTargets(HashSet<uint>? deletedTargets)
        {
            if (deletedTargets == null)
            {
                return;
            }

            List<uint>? emptyLists = null;
            foreach (KeyValuePair<uint, List<ITriggeredMonitoredItem>> link in m_itemsToTrigger)
            {
                List<ITriggeredMonitoredItem> triggeredItems = link.Value;
                int kept = 0;
                for (int ii = 0; ii < triggeredItems.Count; ii++)
                {
                    ITriggeredMonitoredItem triggeredItem = triggeredItems[ii];
                    uint id = triggeredItem.Id;
                    if (deletedTargets.Contains(id))
                    {
                        RemoveTriggeredTarget(id);
                        continue;
                    }
                    triggeredItems[kept++] = triggeredItem;
                }
                if (kept == triggeredItems.Count)
                {
                    continue;
                }
                triggeredItems.RemoveRange(kept, triggeredItems.Count - kept);
                if (kept == 0)
                {
                    (emptyLists ??= []).Add(link.Key);
                }
            }

            if (emptyLists != null)
            {
                foreach (uint triggeringItemId in emptyLists)
                {
                    m_itemsToTrigger.Remove(triggeringItemId);
                }
            }
        }

        private readonly Dictionary<uint, int> m_triggeredTargets = [];
        private int m_triggeringLinkCount;
    }
}
