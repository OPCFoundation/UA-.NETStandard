/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Pumps.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Pumps.Tests
{
    /// <summary>
    /// Starts a server with a <see cref="PumpsNodeManager"/> whose address
    /// space is created, without going through the hosting pipeline.
    /// </summary>
    internal sealed class PumpsServerFixture : IAsyncDisposable
    {
        private ServerFixture<StandardServer>? m_fixture;

        public PumpsNodeManager Manager { get; private set; } = null!;

        public IServerInternal Server { get; private set; } = null!;

        public ApplicationConfiguration Configuration => m_fixture!.Config;

        public async Task StartAsync(PumpsServerOptions? options = null)
        {
            m_fixture = new ServerFixture<StandardServer>(
                telemetry => new StandardServer(telemetry)
                {
                    SessionManagerFactory = new DirectSessionManagerFactory()
                })
            {
                AutoAccept = true,
                SecurityNone = true
            };

            StandardServer server = await m_fixture.StartAsync().ConfigureAwait(false);
            Server = server.CurrentInstance;
            Manager = options == null
                ? new PumpsNodeManager(server.CurrentInstance, m_fixture.Config)
                : new PumpsNodeManager(server.CurrentInstance, m_fixture.Config, options);
            await Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
        }

        public QualifiedName PumpName(string name)
        {
            return new QualifiedName(name, Manager.InstanceNamespaceIndex);
        }

        public async ValueTask DisposeAsync()
        {
            if (Manager != null)
            {
                await Manager.DisposeAsync().ConfigureAwait(false);
            }
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates the ordinary session manager with registered direct-test owners.
        /// </summary>
        private sealed class DirectSessionManagerFactory : ISessionManagerFactory
        {
            /// <inheritdoc/>
            public ISessionManager Create(
                IServerInternal server,
                ApplicationConfiguration configuration,
                TimeProvider timeProvider,
                Func<string, Certificate?> serverCertificateProvider) =>
                new DirectSessionManager(server, configuration, timeProvider);
        }

        /// <summary>
        /// Keeps the fixture's logical file owners visible to session-lifetime validation.
        /// </summary>
        private sealed class DirectSessionManager : SessionManager
        {
            /// <summary>
            /// Creates the manager and the two direct-call session entries.
            /// </summary>
            public DirectSessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                TimeProvider timeProvider)
                : base(server, configuration, timeProvider)
            {
                m_directSessions = [CreateDirectSession(42), CreateDirectSession(43)];
            }

            /// <inheritdoc/>
            public override IList<ISession> GetSessions()
            {
                IList<ISession> sessions = base.GetSessions();
                foreach (Mock<ISession> session in m_directSessions)
                {
                    sessions.Add(session.Object);
                }
                return sessions;
            }

            /// <summary>
            /// Creates a live entry for one direct-call owner.
            /// </summary>
            private static Mock<ISession> CreateDirectSession(uint id)
            {
                var session = new Mock<ISession>();
                session.SetupGet(value => value.Id).Returns(new NodeId(id));
                return session;
            }

            /// <summary>The registered direct-call owners retained by this fixture.</summary>
            private readonly ArrayOf<Mock<ISession>> m_directSessions;
        }
    }
}
