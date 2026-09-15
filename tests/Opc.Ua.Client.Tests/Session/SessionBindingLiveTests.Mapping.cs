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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests
{
    public sealed partial class SessionBindingLiveTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task CapturedBindingRejectsSourceMappingReplacementAsync(
            bool namespaceMapping,
            bool standaloneClient)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await WithSessionAsync(async session =>
            {
                Session native = session is Client.ManagedSession managed
                    ? managed.InnerSession
                    : (Session)session;
                SessionConfiguration configuration = native.SaveSessionConfiguration();
                using var standalone = new BorrowedBindingSessionClient(
                    session.TransportChannel, session.MessageContext.Telemetry);
                standalone.SessionCreated(configuration.SessionId, configuration.AuthenticationToken);
                ISessionClient owner = standaloneClient ? standalone : session;
                var context = (ServiceMessageContext)owner.MessageContext;
                NamespaceTable namespaces = context.NamespaceUris;
                StringTable servers = context.ServerUris;
                using (ISessionClient binding = await ((ISessionBindingProvider)owner)
                    .CreateBindingAsync(timeout.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await AssertMappingOwnerRunningAsync(binding, timeout.Token).ConfigureAwait(false);
                        ReplaceMapping(context, namespaceMapping);
                        Assert.Multiple(() =>
                        {
                            Assert.That(namespaces.GetIndex("urn:session-binding:replacement"), Is.EqualTo(-1));
                            Assert.That(servers.GetIndex("urn:session-binding:replacement"), Is.EqualTo(-1));
                        });
                        await AssertMappingBindingRejectedAsync(binding, timeout.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        context.NamespaceUris = namespaces;
                        context.ServerUris = servers;
                        await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                await AssertMappingOwnerRunningAsync(owner, timeout.Token).ConfigureAwait(false);
            }, timeout.Token).ConfigureAwait(false);
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task CapturedBindingCannotReviveAfterMappingHolderAbaAsync(
            bool namespaceMapping,
            bool sourceContext,
            bool observeInvalidation)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await WithSessionAsync(async session =>
            {
                var provider = (ISessionBindingProvider)session;
                using (ISessionClient binding = await provider.CreateBindingAsync(timeout.Token).ConfigureAwait(false))
                {
                    var context = (ServiceMessageContext)(sourceContext
                        ? session.MessageContext
                        : binding.MessageContext);
                    NamespaceTable namespaces = context.NamespaceUris;
                    StringTable servers = context.ServerUris;
                    long namespaceVersion = namespaces.Version;
                    long serverVersion = servers.Version;
                    try
                    {
                        await AssertMappingOwnerRunningAsync(binding, timeout.Token).ConfigureAwait(false);
                        ReplaceMapping(context, namespaceMapping);
                        if (observeInvalidation)
                        {
                            await AssertMappingBindingRejectedAsync(binding, timeout.Token).ConfigureAwait(false);
                        }
                        context.NamespaceUris = namespaces;
                        context.ServerUris = servers;
                        Assert.Multiple(() =>
                        {
                            Assert.That(context.NamespaceUris, Is.SameAs(namespaces));
                            Assert.That(context.ServerUris, Is.SameAs(servers));
                            Assert.That(namespaces.Version, Is.EqualTo(namespaceVersion));
                            Assert.That(servers.Version, Is.EqualTo(serverVersion));
                        });
                        await AssertMappingBindingRejectedAsync(binding, timeout.Token).ConfigureAwait(false);
                        await AssertMappingBindingRejectedAsync(binding, timeout.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        context.NamespaceUris = namespaces;
                        context.ServerUris = servers;
                        await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                await AssertMappingOwnerRunningAsync(session, timeout.Token).ConfigureAwait(false);
                using ISessionClient fresh = await provider.CreateBindingAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    await AssertMappingOwnerRunningAsync(fresh, timeout.Token).ConfigureAwait(false);
                }
                finally
                {
                    await fresh.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }, timeout.Token).ConfigureAwait(false);
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task BoundResponseRejectsMappingHolderChangesAsync(
            bool namespaceMapping,
            bool sourceContext,
            bool restoreReference)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var channels = new BarrierChannelBindings();
            await WithSessionAsync(async session =>
            {
                using (ISessionClient binding = await ((ISessionBindingProvider)session)
                    .CreateBindingAsync(timeout.Token).ConfigureAwait(false))
                {
                    var context = (ServiceMessageContext)(sourceContext
                        ? session.MessageContext
                        : binding.MessageContext);
                    NamespaceTable namespaces = context.NamespaceUris;
                    StringTable servers = context.ServerUris;
                    AsyncBarrier? barrier = null;
                    try
                    {
                        await AssertMappingOwnerRunningAsync(binding, timeout.Token).ConfigureAwait(false);
                        barrier = channels.PauseNextResponse();
                        Task<DataValue> response = binding.ReadValueAsync(
                            VariableIds.Server_ServerStatus_State, timeout.Token);
                        await barrier.Entered.WaitAsync(timeout.Token).ConfigureAwait(false);
                        Assert.That(response.IsCompleted, Is.False, "The actual TCP response is held.");
                        ReplaceMapping(context, namespaceMapping);
                        if (restoreReference)
                        {
                            context.NamespaceUris = namespaces;
                            context.ServerUris = servers;
                        }
                        barrier.Release();
                        await Assert.ThatAsync(() => response,
                            Throws.TypeOf<ServiceResultException>()
                                .With.Property(nameof(ServiceResultException.StatusCode))
                                .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
                    }
                    finally
                    {
                        barrier?.Release();
                        context.NamespaceUris = namespaces;
                        context.ServerUris = servers;
                        await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                await AssertMappingOwnerRunningAsync(session, timeout.Token).ConfigureAwait(false);
            }, timeout.Token, channels).ConfigureAwait(false);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task CapturedBindingAcceptsUnchangedMappingHolderAssignmentAsync(
            bool namespaceMapping,
            bool sourceContext)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await WithSessionAsync(async session =>
            {
                using ISessionClient binding = await ((ISessionBindingProvider)session)
                    .CreateBindingAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    var context = (ServiceMessageContext)(sourceContext
                        ? session.MessageContext
                        : binding.MessageContext);
                    if (namespaceMapping)
                    {
                        NamespaceTable namespaces = context.NamespaceUris;
                        context.NamespaceUris = namespaces;
                        Assert.That(context.NamespaceUris, Is.SameAs(namespaces));
                    }
                    else
                    {
                        StringTable servers = context.ServerUris;
                        context.ServerUris = servers;
                        Assert.That(context.ServerUris, Is.SameAs(servers));
                    }
                    await AssertMappingOwnerRunningAsync(binding, timeout.Token).ConfigureAwait(false);
                }
                finally
                {
                    await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }, timeout.Token).ConfigureAwait(false);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task BindingCaptureRequiresCoherentSourceMappingsAsync(
            bool namespaceMapping,
            bool standaloneClient)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await WithSessionAsync(async session =>
            {
                Session native = session is Client.ManagedSession managed
                    ? managed.InnerSession
                    : (Session)session;
                SessionConfiguration configuration = native.SaveSessionConfiguration();
                using var standalone = new BorrowedBindingSessionClient(
                    session.TransportChannel, session.MessageContext.Telemetry);
                standalone.SessionCreated(configuration.SessionId, configuration.AuthenticationToken);
                ISessionClient owner = standaloneClient ? standalone : session;
                var provider = (ISessionBindingProvider)owner;
                var context = (ServiceMessageContext)owner.MessageContext;
                NamespaceTable namespaces = context.NamespaceUris;
                StringTable servers = context.ServerUris;
                try
                {
                    ReplaceMapping(context, namespaceMapping);
                    if (standaloneClient)
                    {
                        using ISessionClient binding = await provider.CreateBindingAsync(timeout.Token)
                            .ConfigureAwait(false);
                        try
                        {
                            Assert.Multiple(() =>
                            {
                                Assert.That(binding.MessageContext.NamespaceUris.ToArray(),
                                    Is.EqualTo(context.NamespaceUris.ToArray()));
                                Assert.That(binding.MessageContext.ServerUris.ToArray(),
                                    Is.EqualTo(context.ServerUris.ToArray()));
                            });
                            await AssertMappingOwnerRunningAsync(binding, timeout.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await Assert.ThatAsync(async () =>
                        {
                            using ISessionClient unexpected = await provider.CreateBindingAsync(timeout.Token)
                                .ConfigureAwait(false);
                            await unexpected.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                        }, Throws.TypeOf<ServiceResultException>()
                            .With.Property(nameof(ServiceResultException.StatusCode))
                            .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
                    }
                }
                finally
                {
                    context.NamespaceUris = namespaces;
                    context.ServerUris = servers;
                }
                await AssertMappingOwnerRunningAsync(owner, timeout.Token).ConfigureAwait(false);
                using ISessionClient fresh = await provider.CreateBindingAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    await AssertMappingOwnerRunningAsync(fresh, timeout.Token).ConfigureAwait(false);
                }
                finally
                {
                    await fresh.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }, timeout.Token).ConfigureAwait(false);
        }

        [Test]
        public async Task CapturedBindingCannotReviveAfterOwnerValidationRecoversAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await WithSessionAsync(async session =>
            {
                Session native = session is Client.ManagedSession managed
                    ? managed.InnerSession
                    : (Session)session;
                SessionConfiguration configuration = native.SaveSessionConfiguration();
                using var owner = new BorrowedBindingSessionClient(
                    session.TransportChannel, session.MessageContext.Telemetry);
                owner.SessionCreated(configuration.SessionId, configuration.AuthenticationToken);
                bool ownerCurrent = true;
                using (ISessionClient binding = await owner.CreateBindingWithOwnerValidationAsync(
                    () => ownerCurrent, timeout.Token).ConfigureAwait(false))
                {
                    try
                    {
                        await AssertMappingOwnerRunningAsync(binding, timeout.Token).ConfigureAwait(false);
                        ownerCurrent = false;
                        await AssertMappingBindingRejectedAsync(binding, timeout.Token).ConfigureAwait(false);
                        ownerCurrent = true;
                        await AssertMappingBindingRejectedAsync(binding, timeout.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        await binding.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                await AssertMappingOwnerRunningAsync(owner, timeout.Token).ConfigureAwait(false);
                using ISessionClient fresh = await owner.CreateBindingWithOwnerValidationAsync(
                    () => ownerCurrent, timeout.Token).ConfigureAwait(false);
                try
                {
                    await AssertMappingOwnerRunningAsync(fresh, timeout.Token).ConfigureAwait(false);
                }
                finally
                {
                    await fresh.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }, timeout.Token).ConfigureAwait(false);
        }

        private static void ReplaceMapping(ServiceMessageContext context, bool namespaceMapping)
        {
            if (namespaceMapping)
            {
                context.NamespaceUris = new NamespaceTable(context.NamespaceUris.ToArray());
                context.NamespaceUris.Append("urn:session-binding:replacement");
            }
            else
            {
                context.ServerUris = new StringTable(context.ServerUris.ToArray());
                context.ServerUris.Append("urn:session-binding:replacement");
            }
        }

        private static async Task AssertMappingOwnerRunningAsync(ISessionClient client, CancellationToken ct)
        {
            DataValue value = await client.ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(value.WrappedValue.TryGetValue(out ServerState state), Is.True);
                Assert.That(state, Is.EqualTo(ServerState.Running));
            });
        }

        private static async Task AssertMappingBindingRejectedAsync(ISessionClient binding, CancellationToken ct)
        {
            await Assert.ThatAsync(() => binding.ReadValueAsync(VariableIds.Server_ServerStatus_State, ct),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed)).ConfigureAwait(false);
        }

        private sealed class BorrowedBindingSessionClient(ITransportChannel channel, ITelemetryContext telemetry)
            : SessionClient(channel, telemetry)
        {
            public ValueTask<ISessionClient> CreateBindingWithOwnerValidationAsync(
                Func<bool> ownerCurrent,
                CancellationToken ct)
            {
                IServiceMessageContext context = MessageContext;
                return CreateBindingCoreAsync(
                    context.NamespaceUris, context.ServerUris, context.Telemetry, ownerCurrent, ct);
            }

            protected override void Dispose(bool disposing)
            {
                if (!Disposed)
                {
                    ReleaseChannel();
                    DisposeClientResources();
                }
                base.Dispose(disposing);
            }
        }
    }
}
