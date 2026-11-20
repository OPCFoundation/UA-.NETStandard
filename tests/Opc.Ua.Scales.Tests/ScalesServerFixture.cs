// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.Ua.Scales.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Scales.Tests
{
    /// <summary>
    /// Starts a server with a <see cref="ScalesNodeManager"/> whose address
    /// space is created, without going through the hosting pipeline.
    /// </summary>
    internal sealed class ScalesServerFixture : IAsyncDisposable
    {
        private ServerFixture<StandardServer>? m_fixture;

        public ScalesNodeManager Manager { get; private set; } = null!;

        public IServerInternal Server { get; private set; } = null!;

        public ISystemContext Context => Manager.SystemContext;

        public async Task StartAsync(ScalesServerOptions? options = null)
        {
            m_fixture = new ServerFixture<StandardServer>(
                telemetry => new StandardServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };

            StandardServer server = await m_fixture.StartAsync().ConfigureAwait(false);
            Server = server.CurrentInstance;
            Manager = options == null
                ? new ScalesNodeManager(server.CurrentInstance, m_fixture.Config)
                : new ScalesNodeManager(server.CurrentInstance, m_fixture.Config, options);
            await Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
        }

        public QualifiedName Name(string name)
        {
            return new QualifiedName(name, Manager.InstanceNamespaceIndex);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Manager != null)
                {
                    await Manager.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                if (m_fixture != null)
                {
                    await m_fixture.StopAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
