// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Boots the stock Machinery node manager against an in-process server and
    /// exposes a direct build context.
    /// </summary>
    internal sealed class MachineryServerFixture : IAsyncDisposable
    {
        public MachineryServerFixture(MachineryParts parts = MachineryParts.All)
        {
            Options = new MachineryServerOptions { Parts = parts };
        }

        public MachineryServerOptions Options { get; }

        public StandardServer Server { get; private set; } = null!;

        public ApplicationConfiguration Configuration =>
            m_fixture?.Config ??
            throw new InvalidOperationException("The Machinery server fixture is not started.");

        public MachineryNodeManager Manager { get; private set; } = null!;

        /// <summary>
        /// Gets the live session context used by direct method calls and their file operations.
        /// </summary>
        public ServerSystemContext SessionContext { get; private set; } = null!;

        public IMachineryBuildContext CreateBuildContext()
        {
            return Manager.CreateMachineryBuildContext();
        }

        public async Task StartAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(
                telemetry => new StandardServer(telemetry) { SessionManagerFactory = m_session.Factory })
            {
                AutoAccept = true,
                SecurityNone = true
            };
            Server = await m_fixture.StartAsync().ConfigureAwait(false);

            Manager = new MachineryNodeManager(
                Server.CurrentInstance,
                m_fixture.Config,
                Options);
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await Manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);
            SessionContext = m_session.CreateContext(Manager.SystemContext);
        }

        public async ValueTask DisposeAsync()
        {
            Manager?.Dispose();
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private ServerFixture<StandardServer>? m_fixture;

        /// <summary>The live owner used by in-process calls.</summary>
        private readonly MachineryTestSession m_session = new();
    }
}
