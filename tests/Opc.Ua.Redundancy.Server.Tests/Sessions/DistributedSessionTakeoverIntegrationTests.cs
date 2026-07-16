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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Two-replica session takeover tests for distributed session mirroring.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Category("Session")]
    [NonParallelizable]
    public class DistributedSessionTakeoverIntegrationTests
    {
        [Test]
        public async Task SessionCreatedOnActiveCanActivateOnBackupAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var factory = new DistributedSessionManagerFactory(
                kv,
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
                active = await activeFixture.StartAsync().ConfigureAwait(false);
                backup = await backupFixture.StartAsync().ConfigureAwait(false);
                (RequestHeader activeHeader, _) = await active.CreateAndActivateSessionAsync(
                    nameof(SessionCreatedOnActiveCanActivateOnBackupAsync)).ConfigureAwait(false);

                EndpointDescription backupEndpoint = backup.GetEndpoints()
                    .Find(e => e.SecurityMode == MessageSecurityMode.None)!;
                var backupChannelContext = new SecureChannelContext(
                    "backup-channel",
                    backupEndpoint,
                    RequestEncoding.Binary,
                    null,
                    null,
                    null);
                var takeoverHeader = new RequestHeader
                {
                    AuthenticationToken = activeHeader.AuthenticationToken
                };

                ActivateSessionResponse response = await backup.ActivateSessionAsync(
                    backupChannelContext,
                    takeoverHeader,
                    null,
                    [],
                    [],
                    default,
                    null,
                    RequestLifetime.None).ConfigureAwait(false);

                ServerFixtureUtils.ValidateResponse(response.ResponseHeader);
                Assert.That(response.ServerNonce.IsNull, Is.False);

                await backup.CloseSessionAsync(
                    backupChannelContext,
                    takeoverHeader,
                    true,
                    RequestLifetime.None).ConfigureAwait(false);
                var mirror = new SharedKeyValueSessionStore(kv, backup.CurrentInstance.MessageContext);
                SharedSessionEntry? afterClose = await mirror.TryGetAsync(activeHeader.AuthenticationToken).ConfigureAwait(false);
                Assert.That(afterClose, Is.Null, "backup ownership must close and remove the mirrored session");
            }
            finally
            {
                if (backup != null)
                {
                    await backupFixture.StopAsync().ConfigureAwait(false);
                }
                if (active != null)
                {
                    await activeFixture.StopAsync().ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task SessionTakeoverPreservesAuthenticationTokenAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var factory = new DistributedSessionManagerFactory(kv,
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
                active = await activeFixture.StartAsync().ConfigureAwait(false);
                backup = await backupFixture.StartAsync().ConfigureAwait(false);
                (RequestHeader activeHeader, _) = await active.CreateAndActivateSessionAsync(
                    nameof(SessionTakeoverPreservesAuthenticationTokenAsync)).ConfigureAwait(false);

                NodeId originalToken = activeHeader.AuthenticationToken;
                EndpointDescription backupEndpoint = backup.GetEndpoints()
                    .Find(e => e.SecurityMode == MessageSecurityMode.None)!;
                var backupChannelContext = new SecureChannelContext(
                    "backup-channel",
                    backupEndpoint,
                    RequestEncoding.Binary,
                    null,
                    null,
                    null);
                var takeoverHeader = new RequestHeader
                {
                    AuthenticationToken = originalToken
                };

                ActivateSessionResponse response = await backup.ActivateSessionAsync(
                    backupChannelContext,
                    takeoverHeader,
                    null,
                    [],
                    [],
                    default,
                    null,
                    RequestLifetime.None).ConfigureAwait(false);

                ServerFixtureUtils.ValidateResponse(response.ResponseHeader);
                Assert.That(takeoverHeader.AuthenticationToken, Is.EqualTo(originalToken));
            }
            finally
            {
                if (backup != null)
                {
                    await backupFixture.StopAsync().ConfigureAwait(false);
                }
                if (active != null)
                {
                    await activeFixture.StopAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
