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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Client.Tests
{
    [TestFixture(false)]
    [TestFixture(true)]
    [NonParallelizable]
    [Category("Client")]
    [Category("Integration")]
    public sealed partial class SessionBindingLiveTests
    {
        public SessionBindingLiveTests(bool managed)
        {
            m_managed = managed;
        }

        [Test]
        public async Task CapturedBindingReadsThroughItsAuthenticatedSessionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await WithSessionAsync(async session =>
            {
                Assert.That(session, Is.InstanceOf<ISessionBindingProvider>());
                var provider = (ISessionBindingProvider)session;
                ISessionClient binding = await provider.CreateBindingAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    DataValue serverStateValue = await binding.ReadValueAsync(
                        VariableIds.Server_ServerStatus_State, timeout.Token).ConfigureAwait(false);
                    Assert.Multiple(() =>
                    {
                        Assert.That(serverStateValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                        Assert.That(serverStateValue.WrappedValue.TryGetValue(out ServerState state), Is.True);
                        Assert.That(state, Is.EqualTo(ServerState.Running));
                        Assert.That(binding.SessionId, Is.EqualTo(session.SessionId));
                        Assert.That(binding.Endpoint.Server.ApplicationUri,
                            Is.EqualTo(session.Endpoint.Server.ApplicationUri));
                        Assert.That(binding.MessageContext.NamespaceUris, Is.Not.SameAs(session.NamespaceUris));
                        Assert.That(binding.MessageContext.NamespaceUris.ToArray(),
                            Is.EqualTo(session.NamespaceUris.ToArray()));
                        Assert.That(binding.MessageContext.ServerUris.ToArray(),
                            Is.EqualTo(session.ServerUris.ToArray()));
                    });
                }
                finally
                {
                    await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    binding.Dispose();
                }
                Assert.That(session.Connected, Is.True, "A binding does not own the original session.");
                DataValue stillConnected = await session.ReadValueAsync(
                    VariableIds.Server_ServerStatus_CurrentTime, timeout.Token).ConfigureAwait(false);
                Assert.That(stillConnected.StatusCode, Is.EqualTo(StatusCodes.Good));
            }, timeout.Token).ConfigureAwait(false);
        }

        [TestCase("namespace")]
        [TestCase("server")]
        [TestCase("namespace-aba")]
        [TestCase("server-aba")]
        [TestCase("incarnation")]
        [TestCase("transport")]
        public async Task CapturedBindingRejectsOwnerInvalidationAsync(string change)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await WithSessionAsync(async session =>
            {
                ISessionClient binding = await ((ISessionBindingProvider)session).CreateBindingAsync(timeout.Token)
                    .ConfigureAwait(false);
                string[] namespaces = session.NamespaceUris.ToArray();
                string[] servers = session.ServerUris.ToArray();
                try
                {
                    switch (change)
                    {
                        case "namespace":
                            session.NamespaceUris.Append("urn:session-binding:changed-namespace");
                            break;
                        case "server":
                            session.ServerUris.Append("urn:session-binding:changed-server");
                            break;
                        case "namespace-aba":
                            session.NamespaceUris.Append("urn:session-binding:changed-namespace");
                            session.NamespaceUris.Update(namespaces);
                            break;
                        case "server-aba":
                            session.ServerUris.Append("urn:session-binding:changed-server");
                            session.ServerUris.Update(servers);
                            break;
                        case "incarnation":
                            Session native = session is Client.ManagedSession managed
                                ? managed.InnerSession
                                : (Session)session;
                            SessionConfiguration configuration = native.SaveSessionConfiguration();
                            native.SessionCreated(configuration.SessionId, configuration.AuthenticationToken);
                            break;
                        case "transport":
                            await session.TransportChannel.ReconnectAsync(ct: timeout.Token).ConfigureAwait(false);
                            break;
                        default:
                            Assert.Fail("Unknown binding mutation.");
                            break;
                    }
                    Assert.Multiple(() =>
                    {
                        Assert.That(binding.MessageContext.NamespaceUris.ToArray(), Is.EqualTo(namespaces));
                        Assert.That(binding.MessageContext.ServerUris.ToArray(), Is.EqualTo(servers));
                    });
                    await Assert.ThatAsync(() => binding.ReadValueAsync(
                            VariableIds.Server_ServerStatus_State, timeout.Token),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
                }
                finally
                {
                    await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    binding.Dispose();
                }
            }, timeout.Token).ConfigureAwait(false);
        }

        [Test]
        public async Task CapturedBindingCancellationAndDisposalLeaveOwnerUsableAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await WithSessionAsync(async session =>
            {
                var provider = (ISessionBindingProvider)session;
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                await Assert.ThatAsync(async () => await provider.CreateBindingAsync(cancelled.Token)
                        .ConfigureAwait(false),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                ISessionClient binding = await provider.CreateBindingAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    await Assert.ThatAsync(() => binding.ReadValueAsync(
                            VariableIds.Server_ServerStatus_State, cancelled.Token),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                    await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    await Assert.ThatAsync(() => binding.ReadValueAsync(
                            VariableIds.Server_ServerStatus_State, timeout.Token),
                        Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
                    DataValue value = await session.ReadValueAsync(
                        VariableIds.Server_ServerStatus_State, timeout.Token).ConfigureAwait(false);
                    Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                }
                finally
                {
                    await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    binding.Dispose();
                }
            }, timeout.Token).ConfigureAwait(false);
        }

        [Test]
        public async Task FreshBindingAfterSameAuthorityReconnectUsesCurrentSessionAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await WithSessionAsync(async session =>
            {
                var provider = (ISessionBindingProvider)session;
                ISessionClient previous = await provider.CreateBindingAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    string applicationUri = previous.Endpoint.Server.ApplicationUri!;
                    await session.ReconnectAsync(null, null, timeout.Token).ConfigureAwait(false);
                    await Assert.ThatAsync(() => previous.ReadValueAsync(
                            VariableIds.Server_ServerStatus_State, timeout.Token),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
                    ISessionClient current = await provider.CreateBindingAsync(timeout.Token).ConfigureAwait(false);
                    try
                    {
                        DataValue value = await current.ReadValueAsync(
                            VariableIds.Server_ServerStatus_State, timeout.Token).ConfigureAwait(false);
                        Assert.Multiple(() =>
                        {
                            Assert.That(current.Endpoint.Server.ApplicationUri, Is.EqualTo(applicationUri));
                            Assert.That(current.SessionId, Is.EqualTo(session.SessionId));
                            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                        });
                    }
                    finally
                    {
                        await current.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                        current.Dispose();
                    }
                }
                finally
                {
                    await previous.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    previous.Dispose();
                }
            }, timeout.Token).ConfigureAwait(false);
        }

        [Test]
        public async Task CapturedClientRejectsAuthenticationReplacementAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await WithSessionAsync(async session =>
            {
                ISessionClient binding = await ((ISessionBindingProvider)session).CreateBindingAsync(timeout.Token)
                    .ConfigureAwait(false);
                try
                {
                    var generated = (SessionClient)binding;
                    Assert.That(() => generated.SessionCreated(session.SessionId, new NodeId("different-token", 0)),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadNotSupported));
                    await Assert.ThatAsync(async () => await binding.ReadAsync(
                            new RequestHeader { AuthenticationToken = new NodeId("different-token", 0) },
                            0, TimestampsToReturn.Neither,
                            [new ReadValueId
                            {
                                NodeId = VariableIds.Server_ServerStatus_State,
                                AttributeId = Attributes.Value
                            }],
                            timeout.Token).ConfigureAwait(false),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
                    DataValue value = await binding.ReadValueAsync(
                        VariableIds.Server_ServerStatus_State, timeout.Token).ConfigureAwait(false);
                    Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                }
                finally
                {
                    await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    binding.Dispose();
                }
            }, timeout.Token).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CapturedClientRejectsReplacementOfItsMappingSnapshotAsync(bool namespaceMapping)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await WithSessionAsync(async session =>
            {
                ISessionClient binding = await ((ISessionBindingProvider)session).CreateBindingAsync(timeout.Token)
                    .ConfigureAwait(false);
                try
                {
                    var context = (ServiceMessageContext)binding.MessageContext;
                    if (namespaceMapping)
                    {
                        context.NamespaceUris = new NamespaceTable(session.NamespaceUris.ToArray());
                    }
                    else
                    {
                        context.ServerUris = new StringTable(session.ServerUris.ToArray());
                    }
                    await Assert.ThatAsync(() => binding.ReadValueAsync(
                            VariableIds.Server_ServerStatus_State, timeout.Token),
                        Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
                    DataValue owner = await session.ReadValueAsync(
                        VariableIds.Server_ServerStatus_State, timeout.Token).ConfigureAwait(false);
                    Assert.That(owner.StatusCode, Is.EqualTo(StatusCodes.Good));
                }
                finally
                {
                    await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    binding.Dispose();
                }
            }, timeout.Token).ConfigureAwait(false);
        }

        private async Task WithSessionAsync(
            Func<ISession, Task> action,
            CancellationToken ct,
            ITransportChannelBindings? bindings = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            string root = Path.Combine(Path.GetTempPath(), "wsb-" + Guid.NewGuid().ToString("N")[..8]);
            var fixture = new ServerFixture<ReferenceServer>(context => new ReferenceServer(context))
            {
                AutoAccept = true
            };
            ReferenceServer? server = null;
            try
            {
                server = await fixture.StartAsync(root).ConfigureAwait(false);
                await using var connection = new ClientFixture(telemetry);
                await connection.LoadClientConfigurationAsync(root, "SessionBindingClient").ConfigureAwait(false);
                await using ClientChannelManager? manager = bindings is null
                    ? null
                    : new ClientChannelManager(connection.Config, telemetry, bindings,
                        reconnectPolicy: new ExponentialBackoffChannelReconnectPolicy
                        {
                            MaxAttempts = 1
                        });
                if (manager is not null)
                {
                    connection.SessionFactory = new ChannelManagerSessionFactory(manager, telemetry);
                }
                using ISession session = await connection.ConnectAsync(
                    new Uri($"{Utils.UriSchemeOpcTcp}://localhost:{fixture.Port}"),
                    SecurityPolicies.Basic256Sha256).ConfigureAwait(false);
                try
                {
                    session.KeepAliveInterval = 60000;
                    if (m_managed)
                    {
                        await using Client.ManagedSession managed = await Client.ManagedSession.CreateAsync(
                            connection.Config, connection.Endpoint, connection.SessionFactory,
                            telemetry: telemetry, ct: ct).ConfigureAwait(false);
                        await action(managed).ConfigureAwait(false);
                    }
                    else
                    {
                        await action(session).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
                server?.Dispose();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private readonly bool m_managed;
    }
}
