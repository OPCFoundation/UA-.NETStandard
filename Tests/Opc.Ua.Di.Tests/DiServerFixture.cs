/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
        private ServerFixture<StandardServer>? m_fixture;

        public StandardServer Server { get; private set; } = null!;
        public DiNodeManager Manager { get; private set; } = null!;

        public async Task StartAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(t => new StandardServer(t))
            {
                AutoAccept = true,
                SecurityNone = true,
            };

            Server = await m_fixture.StartAsync().ConfigureAwait(false);

            // Construct the DiNodeManager against the running server and
            // populate its address space by running the standard
            // CreateAddressSpaceAsync pipeline.
            Manager = new DiNodeManager(Server.CurrentInstance, m_fixture.Config);
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
    }
}
