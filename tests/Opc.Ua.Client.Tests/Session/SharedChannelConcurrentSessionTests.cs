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
using Opc.Ua.Bindings;
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
        /// BadSecureChannelClosed. The creator's transport is held until the
        /// second session has attached to the opening entry.
        /// </summary>
        [Test]
        public async Task SessionAttachedWhileChannelOpensWaitsForTransportAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            Endpoints = await ClientFixture.GetEndpointsAsync(ServerUrl).ConfigureAwait(false);
            ConfiguredEndpoint endpoint = await ClientFixture
                .GetEndpointAsync(ServerUrl, SecurityPolicies.Basic256Sha256, Endpoints)
                .ConfigureAwait(false);
            using var bindings = new GatedChannelBindings(
                (ITransportChannelBindings)ClientChannelManager.DefaultChannelBindings);
            await using var manager = new ClientChannelManager(ClientFixture.Config, Telemetry, bindings);

            Task<Client.Session> creating = Task.Run(() => CreateSessionAsync(manager, endpoint, "Creator", timeout.Token));
            Task<Client.Session>? attaching = null;
            try
            {
                Assert.That(bindings.CreateStarted.Wait(TimeSpan.FromSeconds(30)), Is.True);
                attaching = Task.Run(() => CreateSessionAsync(manager, endpoint, "Attached", timeout.Token));

                // The creator holds no lease while it opens, so a reference
                // count of one is the attached session. From its attach to the
                // certificate read the attached session does no I/O; the grace
                // period only lets that synchronous step run before the open is
                // released.
                while (!attaching.IsCompleted &&
                    manager.GetChannelDiagnostics() is not [{ Refcount: 1 }])
                {
                    await Task.Delay(10, timeout.Token).ConfigureAwait(false);
                }
                await Task.WhenAny(attaching, Task.Delay(500, timeout.Token)).ConfigureAwait(false);
                Assert.That(attaching.IsCompleted, Is.False,
                    "The attached session must wait for the opening channel.");

                bindings.Release();
                Client.Session[] sessions = await Task.WhenAll(creating, attaching).ConfigureAwait(false);

                Assert.That(manager.GetChannelDiagnostics(), Has.Count.EqualTo(1));
                Assert.That(manager.GetChannelDiagnostics()[0].Refcount, Is.EqualTo(2));
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
                bindings.Release();
                await CloseCreatedAsync(creating, timeout.Token).ConfigureAwait(false);
                if (attaching != null)
                {
                    await CloseCreatedAsync(attaching, timeout.Token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Many sessions created at once through one manager share one channel.
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
                return CreateSessionAsync(manager, endpoint, "Concurrent" + i, timeout.Token);
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
                    await CloseCreatedAsync(create, timeout.Token).ConfigureAwait(false);
                }
            }
        }

        private Task<Client.Session> CreateSessionAsync(
            ClientChannelManager manager,
            ConfiguredEndpoint endpoint,
            string sessionName,
            CancellationToken ct)
        {
            return Client.Session.CreateAsync(
                manager,
                ClientFixture.Config,
                endpoint,
                updateBeforeConnect: false,
                sessionName: sessionName,
                ct: ct);
        }

        private static async Task CloseCreatedAsync(Task<Client.Session> create, CancellationToken ct)
        {
            try
            {
                await create.ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }
            await create.Result.CloseAsync(ct).ConfigureAwait(false);
            create.Result.Dispose();
        }

        /// <summary>
        /// Holds the first transport the manager creates until released, so a
        /// test can attach further sessions while the shared channel opens.
        /// </summary>
        private sealed class GatedChannelBindings : ITransportChannelBindings, IDisposable
        {
            public GatedChannelBindings(ITransportChannelBindings inner)
            {
                m_inner = inner;
            }

            public ManualResetEventSlim CreateStarted { get; } = new(false);

            public ITransportChannel? Create(string uriScheme, ITelemetryContext telemetry)
            {
                if (Interlocked.Exchange(ref m_created, 1) == 0)
                {
                    CreateStarted.Set();
                    m_release.Wait(TimeSpan.FromSeconds(60));
                }
                return m_inner.Create(uriScheme, telemetry);
            }

            public void Release()
            {
                m_release.Set();
            }

            public void Dispose()
            {
                m_release.Set();
                m_release.Dispose();
                CreateStarted.Dispose();
            }

            private readonly ITransportChannelBindings m_inner;
            private readonly ManualResetEventSlim m_release = new(false);
            private int m_created;
        }
    }
}
