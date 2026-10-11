/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Sessions created concurrently through one <see cref="ClientChannelManager"/>
    /// share a single channel, exactly as sessions created one after another do.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Session")]
    public sealed class SharedChannelConcurrentSessionTests : ClientTestFramework
    {
        public SharedChannelConcurrentSessionTests()
            : base(Utils.UriSchemeOpcTcp)
        {
            SingleSession = false;
        }

        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            return base.OneTimeSetUpAsync();
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// A session that attached to the shared entry while its creator was
        /// still opening the transport read the entry's certificate before a
        /// transport was installed, failed, and released its lease. The release
        /// dropped the reference count to zero and tore the entry down under the
        /// creator's open, so every session waiting on it failed with
        /// BadSecureChannelClosed.
        /// </summary>
        [Test]
        public async Task ConcurrentlyCreatedSessionsShareOneChannelAsync()
        {
            const int sessionCount = 20;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Endpoints = await ClientFixture.GetEndpointsAsync(ServerUrl).ConfigureAwait(false);
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                .ConfigureAwait(false);
            await using var manager = new ClientChannelManager(ClientFixture.Config, Telemetry);

            using var start = new ManualResetEventSlim(false);
            Task<Client.Session>[] creates = [.. Enumerable.Range(0, sessionCount).Select(i => Task.Run(() =>
            {
                start.Wait(timeout.Token);
                return Client.Session.CreateAsync(
                    manager,
                    ClientFixture.Config,
                    endpoint,
                    updateBeforeConnect: false,
                    sessionName: "Concurrent" + i,
                    ct: timeout.Token);
            }))];
            start.Set();
            try
            {
                Client.Session[] sessions = await Task.WhenAll(creates).ConfigureAwait(false);

                Assert.That(manager.GetChannelDiagnostics(), Has.Count.EqualTo(1));
                Assert.That(manager.GetChannelDiagnostics()[0].Refcount, Is.EqualTo(sessionCount));
                foreach (Client.Session session in sessions)
                {
                    DataValue value = await session
                        .ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                }
            }
            finally
            {
                foreach (Task<Client.Session> create in creates)
                {
                    if (create.Status == TaskStatus.RanToCompletion)
                    {
                        await create.Result.CloseAsync(timeout.Token).ConfigureAwait(false);
                        create.Result.Dispose();
                    }
                }
            }
        }
    }
}
