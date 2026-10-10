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
using System.Collections.Generic;
using System.Linq;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    internal sealed partial class WotProjectedEventRouteRegistry
    {
        public void PrepareTransparentSource(WotProjectedEventBinding binding, WotEventSource source)
        {
            lock (m_gate)
            {
                ValidateNodeAuthority(binding.SourceCondition, source);
                if (!binding.SourceCondition.IsNull && m_preparedSources.TryAdd(binding, source))
                {
                    if (!m_transparentNodes.TryGetValue(binding.SourceCondition, out NodeClaim? owner))
                    {
                        owner = new NodeClaim(source);
                        m_transparentNodes.Add(binding.SourceCondition, owner);
                    }
                    owner.References++;
                }
            }
        }

        public void ValidateTransparentSource(WotProjectedEventBinding binding, WotCapturedEvent captured)
        {
            lock (m_gate)
            {
                ValidateTransparentSourceCore(binding, captured);
            }
        }

        public bool AdmitTransparentEvent(WotProjectedEventBinding binding, WotCapturedEvent captured)
        {
            lock (m_gate)
            {
                ValidateTransparentSourceCore(binding, captured);
                if (!m_transparentEvents.TryGetValue(captured.EventId, out EventClaim? claim))
                {
                    var nodes = new List<ExpandedNodeId>(2);
                    if (!captured.SourceNode.IsNull)
                    {
                        nodes.Add(captured.SourceNode);
                    }
                    if (captured.HasConditionId && !captured.ConditionId.IsNull &&
                        !nodes.Contains(captured.ConditionId))
                    {
                        nodes.Add(captured.ConditionId);
                    }
                    claim = new EventClaim(captured, nodes.ToArrayOf());
                    m_transparentEvents.Add(captured.EventId, claim);
                }
                if (!claim.Owners.Add(binding))
                {
                    return false;
                }
                foreach (ExpandedNodeId node in claim.Nodes)
                {
                    if (!m_transparentNodes.TryGetValue(node, out NodeClaim? owner))
                    {
                        owner = new NodeClaim(captured.Source);
                        m_transparentNodes.Add(node, owner);
                    }
                    owner.References++;
                }
                return true;
            }
        }

        public void ReleaseTransparentEvent(WotProjectedEventBinding binding, ByteString eventId)
        {
            lock (m_gate)
            {
                ReleaseTransparentEventCore(binding, eventId);
            }
        }

        private void ValidateTransparentSourceCore(WotProjectedEventBinding binding, WotCapturedEvent captured)
        {
            ValidateNodeAuthority(captured.SourceNode, captured.Source);
            if (captured.HasConditionId)
            {
                ValidateNodeAuthority(captured.ConditionId, captured.Source);
            }
            if (m_transparentEvents.TryGetValue(captured.EventId, out EventClaim? existing) &&
                (!existing.Captured.Source.HasSameAuthority(captured.Source) ||
                    (existing.Owners.Contains(binding)
                        ? !ReferenceEquals(existing.Captured.Source, captured.Source)
                        : !ReferenceEquals(existing.Captured, captured))))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The preserved EventId cannot be admitted into another source or ambiguous generation.");
            }
        }

        private void ValidateNodeAuthority(ExpandedNodeId node, WotEventSource source)
        {
            if (!node.IsNull && m_transparentNodes.TryGetValue(node, out NodeClaim? existing) &&
                !existing.Source.HasSameAuthority(source))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "Different authenticated event authorities cannot map to one semantic source identity.");
            }
        }

        private void ReleaseTransparentBinding(WotProjectedEventBinding binding)
        {
            if (m_preparedSources.Remove(binding))
            {
                NodeClaim owner = m_transparentNodes[binding.SourceCondition];
                if (--owner.References == 0)
                {
                    m_transparentNodes.Remove(binding.SourceCondition);
                }
            }
            ByteString[] keys = m_transparentEvents
                .Where(entry => entry.Value.Owners.Contains(binding))
                .Select(entry => entry.Key).ToArray();
            foreach (ByteString key in keys)
            {
                ReleaseTransparentEventCore(binding, key);
            }
        }

        private void ReleaseTransparentEventCore(WotProjectedEventBinding binding, ByteString eventId)
        {
            if (!m_transparentEvents.TryGetValue(eventId, out EventClaim? claim) || !claim.Owners.Remove(binding))
            {
                return;
            }
            foreach (ExpandedNodeId node in claim.Nodes)
            {
                NodeClaim owner = m_transparentNodes[node];
                if (--owner.References == 0)
                {
                    m_transparentNodes.Remove(node);
                }
            }
            if (claim.Owners.Count == 0)
            {
                m_transparentEvents.Remove(eventId);
            }
        }

        private sealed class EventClaim(WotCapturedEvent captured, ArrayOf<ExpandedNodeId> nodes)
        {
            public WotCapturedEvent Captured { get; } = captured;
            public ArrayOf<ExpandedNodeId> Nodes { get; } = nodes;
            public HashSet<WotProjectedEventBinding> Owners { get; } = [];
        }

        private sealed class NodeClaim(WotEventSource source)
        {
            public WotEventSource Source { get; } = source;
            public int References { get; set; }
        }

        private readonly Dictionary<ByteString, EventClaim> m_transparentEvents = [];
        private readonly Dictionary<ExpandedNodeId, NodeClaim> m_transparentNodes = [];
        private readonly Dictionary<WotProjectedEventBinding, WotEventSource> m_preparedSources = [];
    }
}
