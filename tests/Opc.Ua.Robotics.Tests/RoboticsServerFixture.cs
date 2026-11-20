// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Robotics.Server.Tests
{
    /// <summary>
    /// Boots the stock Robotics node manager and exposes a direct build context.
    /// </summary>
    internal sealed class RoboticsServerFixture : IAsyncDisposable
    {
        private ServerFixture<StandardServer>? m_fixture;

        public StandardServer Server { get; private set; } = null!;

        public ApplicationConfiguration Configuration =>
            m_fixture?.Config ??
            throw new InvalidOperationException("The Robotics server fixture is not started.");

        public RoboticsNodeManager Manager { get; private set; } = null!;

        public IRoboticsBuildContext CreateBuildContext()
        {
            return Manager.CreateRoboticsBuildContext();
        }

        public async Task StartAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(
                telemetry => new StandardServer(telemetry))
            {
                AutoAccept = true,
                SecurityNone = true
            };
            Server = await m_fixture.StartAsync().ConfigureAwait(false);

            Manager = new RoboticsNodeManager(
                Server.CurrentInstance,
                m_fixture.Config);
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await Manager.CreateAddressSpaceAsync(externalReferences)
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
