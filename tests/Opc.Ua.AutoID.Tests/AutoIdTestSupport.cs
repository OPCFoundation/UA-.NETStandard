// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
