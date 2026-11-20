// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Moq;
using Opc.Ua.Server;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Supplies one live owner for direct, in-process result-transfer calls.
    /// </summary>
    internal sealed class MachineryTestSession
    {
        /// <summary>
        /// Registers the owner in the session manager returned by the server factory.
        /// </summary>
        public MachineryTestSession()
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId("machinery-test-session", 1));
            m_session = session.Object;
            var sessions = new Mock<ISessionManager>();
            sessions.Setup(value => value.GetSessions()).Returns(() => [m_session]);
            var factory = new Mock<ISessionManagerFactory>();
            factory.SetReturnsDefault(sessions.Object);
            Factory = factory.Object;
        }

        /// <summary>
        /// Gets the factory that exposes the owner as a live server session.
        /// </summary>
        public ISessionManagerFactory Factory { get; }

        /// <summary>
        /// Copies the node manager context with the registered session as its owner.
        /// </summary>
        public ServerSystemContext CreateContext(ServerSystemContext context)
        {
            return context.Copy(m_session);
        }

        /// <summary>The registered transfer owner.</summary>
        private readonly ISession m_session;
    }
}
