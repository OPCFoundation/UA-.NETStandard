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
using System.Linq;
using System.Threading;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// Indexes only live generations. The bounded occurrence tables remain
    /// owned by their generations, but an active Method can resolve an EventId
    /// delivered by a retiring generation of the same declaration and source.
    /// </summary>
    internal sealed partial class WotProjectedEventRouteRegistry
    {
        public ArrayOf<WotProjectedEventBinding> GetDescriptorBindings(NodeId notifierId)
        {
            lock (m_gate)
            {
                return m_bindings.Values.SelectMany(bindings => bindings)
                    .Where(binding => binding.Notifier.NodeId == notifierId).Distinct().ToArrayOf();
            }
        }

        public void Add(WotProjectedEventBinding binding)
        {
            lock (m_gate)
            {
                var key = (binding.ResourceXid, binding.JsonPointer);
                if (!m_bindings.TryGetValue(key, out List<WotProjectedEventBinding>? generations))
                {
                    generations = [];
                    m_bindings.Add(key, generations);
                }
                generations.Add(binding);
            }
        }

        public void Remove(WotProjectedEventBinding binding)
        {
            lock (m_gate)
            {
                ReleaseTransparentBinding(binding);
                var key = (binding.ResourceXid, binding.JsonPointer);
                if (m_bindings.TryGetValue(key, out List<WotProjectedEventBinding>? generations))
                {
                    generations.Remove(binding);
                    if (generations.Count == 0)
                    {
                        m_bindings.Remove(key);
                    }
                }
            }
        }

        public ServiceResult Resolve(
            WotProjectedEventBinding requester, ByteString eventId, out ByteString originalEventId)
        {
            WotProjectedEventBinding[] candidates;
            lock (m_gate)
            {
                candidates = m_bindings.TryGetValue(
                    (requester.ResourceXid, requester.JsonPointer), out List<WotProjectedEventBinding>? generations)
                    ? [.. generations] : [];
            }
            foreach (WotProjectedEventBinding candidate in candidates)
            {
                if (candidate.SourceCondition == requester.SourceCondition &&
                    candidate.EventTypeId == requester.EventTypeId &&
                    WotProjectedEventSource.SameEndpoint(candidate.Source.Form, requester.Source.Form) &&
                    candidate.TryResolveOwnEventId(eventId, out originalEventId))
                {
                    return ServiceResult.Good;
                }
            }
            originalEventId = default;
            return new ServiceResult(StatusCodes.BadEventIdUnknown);
        }

        public ServiceResult ResolveAction(
            WotProjectedEventBinding requester,
            ByteString eventId,
            string action,
            out WotCapturedEvent? occurrence,
            out WotCapturedConditionAction? capturedAction)
        {
            WotProjectedEventBinding[] candidates;
            lock (m_gate)
            {
                candidates = m_bindings.TryGetValue(
                    (requester.ResourceXid, requester.JsonPointer), out List<WotProjectedEventBinding>? generations)
                    ? [.. generations] : [];
            }
            foreach (WotProjectedEventBinding candidate in candidates)
            {
                if (candidate.SourceCondition == requester.SourceCondition &&
                    candidate.EventTypeId == requester.EventTypeId &&
                    WotProjectedEventSource.SameEndpoint(candidate.Source.Form, requester.Source.Form) &&
                    candidate.TryResolveOwnAction(eventId, action, out occurrence, out capturedAction))
                {
                    return ServiceResult.Good;
                }
            }
            occurrence = null;
            capturedAction = null;
            return new ServiceResult(StatusCodes.BadEventIdUnknown);
        }

        private readonly Lock m_gate = new();
        private readonly Dictionary<(string, string), List<WotProjectedEventBinding>> m_bindings = [];
    }
}
