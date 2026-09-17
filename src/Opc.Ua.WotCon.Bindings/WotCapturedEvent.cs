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

using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// Typed occurrence facts obtained from one native notification and its
    /// retained authenticated source binding. Presence is explicit: an absent
    /// source fact is not synthesized from a local clock or declaration.
    /// </summary>
    public sealed class WotCapturedEvent
    {
        private WotCapturedEvent(WotEventSource source)
        {
            Source = source;
        }

        /// <summary>
        /// Gets the captured source binding that supplied these facts.
        /// </summary>
        public WotEventSource Source { get; }

        /// <summary>
        /// Gets the exact selected namespace-zero EventId.
        /// </summary>
        public ByteString EventId { get; private set; }

        /// <summary>
        /// Gets whether EventId was selected and supplied.
        /// </summary>
        public bool HasEventId { get; private set; }

        /// <summary>
        /// Gets the portable source EventType.
        /// </summary>
        public ExpandedNodeId EventType { get; private set; }

        /// <summary>
        /// Gets whether EventType was selected and supplied.
        /// </summary>
        public bool HasEventType { get; private set; }

        /// <summary>
        /// Gets the portable source Node. A present null remains null.
        /// </summary>
        public ExpandedNodeId SourceNode { get; private set; }

        /// <summary>
        /// Gets whether SourceNode was selected and supplied.
        /// </summary>
        public bool HasSourceNode { get; private set; }

        /// <summary>
        /// Gets the selected portable source Condition identity.
        /// </summary>
        public ExpandedNodeId ConditionId { get; private set; }

        /// <summary>
        /// Gets whether the empty-path ConditionId selection was supplied.
        /// </summary>
        public bool HasConditionId { get; private set; }

        /// <summary>
        /// Gets the source branch. A present null denotes the main branch.
        /// </summary>
        public ExpandedNodeId BranchId { get; private set; }

        /// <summary>
        /// Gets whether namespace-zero BranchId was selected and supplied.
        /// </summary>
        public bool HasBranchId { get; private set; }

        /// <summary>
        /// Gets the source occurrence Time.
        /// </summary>
        public DateTimeUtc Time { get; private set; }

        /// <summary>
        /// Gets whether namespace-zero Time was selected and supplied.
        /// </summary>
        public bool HasTime { get; private set; }

        /// <summary>
        /// Gets the upstream receipt time, independently of local publication.
        /// </summary>
        public DateTimeUtc ReceiveTime { get; private set; }

        /// <summary>
        /// Gets whether namespace-zero ReceiveTime was selected and supplied.
        /// </summary>
        public bool HasReceiveTime { get; private set; }

        internal static WotCapturedEvent Capture(
            WotEventSource source,
            ArrayOf<WotResolvedEventSelectClause> selection,
            ArrayOf<Variant> values)
        {
            source.Validate();
            if (selection.Count != values.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "The source event does not match its captured selection.");
            }
            var result = new WotCapturedEvent(source);
            for (int index = 0; index < selection.Count; index++)
            {
                WotResolvedEventSelectClause clause = selection[index];
                Variant value = values[index];
                if (clause.IsConditionIdSelection)
                {
                    result.ConditionId = ReadNodeId(value, source.Context.NamespaceUris);
                    result.HasConditionId = true;
                    continue;
                }
                if (clause.PathElements.Count != 1)
                {
                    continue;
                }
                QualifiedName name = WotBindingValueMapper.ResolveBrowseName(
                    clause.PathElements[0], source.Context.NamespaceUris);
                if (name.NamespaceIndex != 0)
                {
                    continue;
                }
                switch (name.Name)
                {
                    case Ua.BrowseNames.EventId:
                        if (!value.TryGetValue(out ByteString eventId))
                        {
                            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
                        }
                        result.EventId = eventId;
                        result.HasEventId = true;
                        break;
                    case Ua.BrowseNames.EventType:
                        result.EventType = ReadNodeId(value, source.Context.NamespaceUris);
                        result.HasEventType = true;
                        break;
                    case Ua.BrowseNames.SourceNode:
                        result.SourceNode = ReadNodeId(value, source.Context.NamespaceUris);
                        result.HasSourceNode = true;
                        break;
                    case Ua.BrowseNames.BranchId:
                        result.BranchId = ReadNodeId(value, source.Context.NamespaceUris);
                        result.HasBranchId = true;
                        break;
                    case Ua.BrowseNames.Time:
                        if (!value.TryGetValue(out DateTimeUtc time))
                        {
                            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
                        }
                        result.Time = time;
                        result.HasTime = true;
                        break;
                    case Ua.BrowseNames.ReceiveTime:
                        if (!value.TryGetValue(out DateTimeUtc receiveTime))
                        {
                            throw new ServiceResultException(StatusCodes.BadTypeMismatch);
                        }
                        result.ReceiveTime = receiveTime;
                        result.HasReceiveTime = true;
                        break;
                    default:
                        break;
                }
            }
            return result;
        }

        private static ExpandedNodeId ReadNodeId(Variant value, NamespaceTable namespaces)
        {
            if (!value.TryGetValue(out NodeId nodeId) || nodeId.NamespaceIndex >= namespaces.Count)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "The source identity has no captured namespace mapping.");
            }
            return NodeId.ToExpandedNodeId(nodeId, namespaces);
        }
    }
}
