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

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// A system context that carries a session, as the server hands to a method call.
    /// </summary>
    internal sealed class TestSessionContext : ISessionSystemContext
    {
        public TestSessionContext(ISystemContext inner, NodeId sessionId)
        {
            m_inner = inner;
            SessionId = sessionId;
        }

        public NodeId? SessionId { get; }

        public IUserIdentity? UserIdentity => null;

        public object? SystemHandle => m_inner.SystemHandle;

        public string? UserId => m_inner.UserId;

        public ArrayOf<string> PreferredLocales => m_inner.PreferredLocales;

        public string? AuditEntryId => m_inner.AuditEntryId;

        public NamespaceTable NamespaceUris => m_inner.NamespaceUris;

        public StringTable ServerUris => m_inner.ServerUris;

        public ITypeTable TypeTable => m_inner.TypeTable;

        public IEncodeableFactory EncodeableFactory => m_inner.EncodeableFactory;

        public INodeIdFactory? NodeIdFactory => m_inner.NodeIdFactory;

        public NodeStateFactory NodeStateFactory => m_inner.NodeStateFactory;

        public ITelemetryContext Telemetry => m_inner.Telemetry;

        private readonly ISystemContext m_inner;
    }

    /// <summary>
    /// Records the events a device reports.
    /// </summary>
    internal sealed class EventCapture
    {
        public EventCapture(NodeState source)
        {
            NodeStateReportEventHandler? previous = source.OnReportEvent;
            source.OnReportEvent = (context, node, e) =>
            {
                lock (m_lock)
                {
                    m_events.Add(e);
                }
                previous?.Invoke(context, node, e);
            };
        }

        public IReadOnlyList<IFilterTarget> Events
        {
            get
            {
                lock (m_lock)
                {
                    return m_events.ToArray();
                }
            }
        }

        public IEnumerable<T> Of<T>()
            where T : class
        {
            return Events.OfType<T>();
        }

        private readonly Lock m_lock = new();
        private readonly List<IFilterTarget> m_events = [];
    }
}
