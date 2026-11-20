/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
            AddTarget(reference, nodeId is null ? null : new WotResolvedNode(nodeId, WotExpectedNodeClass.Any));
        }

        /// <summary>
        /// Adds the resolved target of a reference.
        /// </summary>
        /// <param name="target">The WoT resolved node.</param>
        /// <param name="reference">The reference text that identifies the target.</param>
        public void AddTarget(string reference, WotResolvedNode? target)
        {
            if (!m_entries.TryGetValue(reference, out Queue<WotResolvedNode?>? entries))
            {
                entries = new Queue<WotResolvedNode?>();
                m_entries.Add(reference, entries);
            }
            entries.Enqueue(target);
        }

        /// <summary>
        /// Removes the next NodeId candidate for a Thing reference while preserving insertion order.
        /// </summary>
        /// <param name="reference">The Thing reference to resolve.</param>
        /// <param name="nodeId">The next catalogued NodeId, or <c>null</c> when none was stored.</param>
        /// <returns><c>true</c> when a catalogued entry was available.</returns>
        public bool TryTake(string reference, out string? nodeId)
        {
            if (m_entries.TryGetValue(reference, out Queue<WotResolvedNode?>? entries) &&
                entries.Count > 0)
            {
                nodeId = entries.Dequeue()?.NodeId;
                return true;
            }
            nodeId = null;
            return false;
        }

        /// <summary>
        /// Attempts to peek the resolved target of a reference without consuming it.
        /// </summary>
        /// <param name="target">The WoT resolved node.</param>
        /// <param name="reference">The reference text that identifies the target.</param>
        public bool TryPeekTarget(string reference, out WotResolvedNode? target)
        {
            if (m_entries.TryGetValue(reference, out Queue<WotResolvedNode?>? entries) && entries.Count > 0)
            {
                target = entries.Peek();
                return true;
            }
            target = null;
            return false;
        }

        private readonly Dictionary<string, Queue<WotResolvedNode?>> m_entries = new(System.StringComparer.Ordinal);
    }
}
