/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.Ua.Positioning.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Positioning.Tests
{
    internal sealed class PositioningServerFixture : IAsyncDisposable
    {
        private ServerFixture<StandardServer>? m_fixture;

        public PositioningNodeManager Manager { get; private set; } = null!;

        public async Task StartAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(
                telemetry => new StandardServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };

            StandardServer server = await m_fixture.StartAsync()
                .ConfigureAwait(false);
            Manager = new PositioningNodeManager(
                server.CurrentInstance,
                m_fixture.Config);
            await Manager.CreateAddressSpaceAsync(
                new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            Manager?.Dispose();
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }
    }
}
