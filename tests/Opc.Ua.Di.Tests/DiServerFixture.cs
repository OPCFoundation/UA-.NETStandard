/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Opc.Ua.Di.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Test fixture that boots a minimal in-process OPC UA server with a
    /// single <see cref="DiNodeManager"/> registered. Used by the device-
    /// builder tests so they can exercise the full predefined-node
    /// pipeline (lifecycle, NodeIdFactory, type tree) without managing
    /// the boilerplate.
    /// </summary>
    internal sealed class DiServerFixture : IAsyncDisposable
    {
        /// <summary>
        /// Creates a fixture that registers a plain <see cref="DiNodeManager"/>.
        /// </summary>
        public DiServerFixture()
            : this((server, configuration) => new DiNodeManager(server, configuration))
        {
        }

        /// <summary>
        /// Creates a fixture that registers the manager the factory creates,
        /// such as a subclass a companion specification would add.
        /// </summary>
        public DiServerFixture(Func<IServerInternal, ApplicationConfiguration, DiNodeManager> createManager)
        {
            m_createManager = createManager;
        }

        public StandardServer Server { get; private set; } = null!;
        public DiNodeManager Manager { get; private set; } = null!;

        public async Task StartAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new StandardServer(t))
            {
                AutoAccept = true,
                SecurityNone = true
            };

            Server = await m_fixture.StartAsync().ConfigureAwait(false);

            // Construct the DiNodeManager against the running server and
            // populate its address space by running the standard
            // CreateAddressSpaceAsync pipeline.
            Manager = m_createManager(Server.CurrentInstance, m_fixture.Config);
            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await Manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            Manager?.Dispose();
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private readonly Func<IServerInternal, ApplicationConfiguration, DiNodeManager> m_createManager;
        private ServerFixture<StandardServer>? m_fixture;
    }
}
