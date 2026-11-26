// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Exercises the service path when a session created on a None endpoint reaches another replica.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Category("Session")]
    [NonParallelizable]
    public class DistributedSessionTakeoverIntegrationTests
    {
        [Test]
        public async Task MirroredNoneSessionStaysLocalAsync()
        {
            using var payloads = new InMemorySharedKeyValueStore();
            using var authority = new SimulatedSharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(Nonce.CreateRandomNonceData(32));
            var kv = new FreshnessProtectedKeyValueStore(
                payloads, authority, protector, Guid.NewGuid(), Guid.NewGuid());
            await kv.EnrollAsync();
            var factory = new DistributedSessionManagerFactory(
                kv,
                protector,
                options: new DistributedSessionOptions { EnableFastReconnect = true });
            var activeFixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t)
            {
                SessionManagerFactory = factory
            })
            {
                SecurityNone = true
            };
            var backupFixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t)
            {
                SessionManagerFactory = factory
            })
            {
                SecurityNone = true
            };

            StandardServer? active = null;
            StandardServer? backup = null;
            try
            {
                active = await activeFixture.StartAsync();
                backup = await backupFixture.StartAsync();
                (RequestHeader activeHeader, SecureChannelContext activeChannel) =
                    await active.CreateAndActivateSessionAsync(nameof(MirroredNoneSessionStaysLocalAsync));
                var store = new SharedKeyValueSessionStore(kv, active.CurrentInstance.MessageContext, protector);
                SharedSessionEntry? original = await store.TryGetAsync(activeHeader.AuthenticationToken);
                Assert.That(original!.HasActivatedUserIdentity, Is.True);

                EndpointDescription endpoint = backup.GetEndpoints()
                    .Find(e => e.SecurityMode == MessageSecurityMode.None)!;
                var channel = new SecureChannelContext(
                    "backup-channel", endpoint, RequestEncoding.Binary, null, null, null);
                var header = new RequestHeader { AuthenticationToken = activeHeader.AuthenticationToken };
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await backup.ActivateSessionAsync(
                        channel, header, null, [], [], default, null, RequestLifetime.None));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
                Assert.That((await store.TryGetAsync(activeHeader.AuthenticationToken))!.OwnerId,
                    Is.EqualTo(original.OwnerId));

                await active.CloseSessionAsync(activeChannel, activeHeader, true, RequestLifetime.None);
                Assert.That(await store.TryGetAsync(activeHeader.AuthenticationToken), Is.Null);
            }
            finally
            {
                if (backup != null)
                {
                    await backupFixture.StopAsync();
                }
                if (active != null)
                {
                    await activeFixture.StopAsync();
                }
            }
        }
    }
}
