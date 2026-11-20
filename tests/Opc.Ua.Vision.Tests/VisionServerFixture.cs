// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Tests
{
    /// <summary>
    /// Boots a StandardServer instance and attaches a
    /// <see cref="VisionNodeManager"/> so tests can exercise the fluent
    /// build context and node-manager lifecycle against a real address
    /// space rather than through a bag of mocks.
    /// </summary>
    internal sealed class VisionServerFixture : IAsyncDisposable
    {
        private ServerFixture<StandardServer>? m_fixture;

        public StandardServer Server { get; private set; } = null!;

        public ApplicationConfiguration Configuration =>
            m_fixture?.Config ??
            throw new InvalidOperationException("The Vision server fixture is not started.");

        public VisionNodeManager Manager { get; private set; } = null!;

        public IVisionBuildContext CreateBuildContext()
        {
            return Manager.CreateVisionBuildContext();
        }

        public async Task StartAsync(MachineVisionParts parts = MachineVisionParts.None)
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
            Server = await m_fixture.StartAsync().ConfigureAwait(false);

            IVisionModelProvider[] providers = (parts & MachineVisionParts.Assets) != 0
                ? [new VisionModelProvider(parts), new VisionMachineryStandaloneModelProvider()]
                : [new VisionModelProvider(parts)];
            Manager = new VisionNodeManager(
                Server.CurrentInstance,
                m_fixture.Config,
                providers,
                new VisionServerOptions { MachineVisionParts = parts });
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await Manager.CreateAddressSpaceAsync(externalReferences)
                .ConfigureAwait(false);
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
        /// Registers the logical sessions used by direct method tests alongside real client sessions.
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
        /// Keeps direct test identities visible to FileType session-lifetime validation.
        /// </summary>
        private sealed class DirectSessionManager : SessionManager
        {
            /// <summary>
            /// Creates the ordinary manager with the two logical direct-call sessions.
            /// </summary>
            public DirectSessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                TimeProvider timeProvider)
                : base(server, configuration, timeProvider)
            {
                m_directSessions = [CreateDirectSession(1), CreateDirectSession(2)];
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
            /// Creates a live registry entry for a direct-call identity.
            /// </summary>
            private static Mock<ISession> CreateDirectSession(uint id)
            {
                var session = new Mock<ISession>();
                session.SetupGet(value => value.Id).Returns(new NodeId(id));
                return session;
            }

            /// <summary>The direct-call registry entries owned by this fixture.</summary>
            private readonly ArrayOf<Mock<ISession>> m_directSessions;
        }
    }
}
