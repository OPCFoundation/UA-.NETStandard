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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

        /// <summary>
        /// A liveness refresh that runs while a restore is still recording this replica
        /// as the owner (a slow shared store write, for example right after the previous
        /// owner failed and the store elects a new leader) must not see the entry that
        /// still names the previous owner and drop the session the restore just
        /// activated. Dropping it closed the session, lost its mirrored history
        /// continuation points and failed the activation, so the client recreated the
        /// session (Redundancy sample StrongHistorianContinuationsSurviveActiveReplicaFailureAsync).
        /// </summary>
        [Test]
        [CancelAfter(60_000)]
        public async Task LivenessRefreshDuringRestoreOwnershipWriteKeepsTheSessionAsync(
            CancellationToken cancellationToken)
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var backupKv = new GatedSessionWriteStore(kv);
            var activeFactory = new DistributedSessionManagerFactory(
                kv,
                options: new DistributedSessionOptions { EnableFastReconnect = true });
            var backupFactory = new DistributedSessionManagerFactory(
                backupKv,
                NullRecordProtector.Instance,
                new DistributedSessionOptions { EnableFastReconnect = true });
            var activeFixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t)
            {
                SessionManagerFactory = activeFactory
            })
            {
                SecurityNone = true
            };
            var backupFixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t)
            {
                SessionManagerFactory = backupFactory
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
                    nameof(LivenessRefreshDuringRestoreOwnershipWriteKeepsTheSessionAsync)).ConfigureAwait(false);

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
                var backupManager = (DistributedSessionManager)backup.CurrentInstance.SessionManager;

                // Hold the ownership write of the restore.
                backupKv.HoldSessionWrites = true;
                Task<ActivateSessionResponse> activation = backup.ActivateSessionAsync(
                    backupChannelContext,
                    takeoverHeader,
                    null,
                    [],
                    [],
                    default,
                    null,
                    RequestLifetime.None).AsTask();
                await backupKv.SessionWriteHeld.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

                // The activation committed; the shared entry still names the active replica.
                await backupManager.RefreshLivenessAsync(cancellationToken).ConfigureAwait(false);

                backupKv.ReleaseSessionWrites();
                ActivateSessionResponse response = await activation.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);

                ServerFixtureUtils.ValidateResponse(response.ResponseHeader);
                Assert.That(
                    backupManager.GetSessions().Count(s => s.Activated),
                    Is.EqualTo(1),
                    "The restored session must stay open on the backup.");
                var mirror = new SharedKeyValueSessionStore(kv, backup.CurrentInstance.MessageContext);
                SharedSessionEntry? entry = await mirror.TryGetAsync(
                    activeHeader.AuthenticationToken,
                    cancellationToken).ConfigureAwait(false);
                Assert.That(entry, Is.Not.Null);
                Assert.That(entry!.OwnerId, Is.EqualTo(backupManager.ReplicaId));
            }
            finally
            {
                backupKv.ReleaseSessionWrites();
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

        /// <summary>
        /// Delegates to a shared store and, while <see cref="HoldSessionWrites"/> is set,
        /// holds conditional writes of session entries until they are released.
        /// </summary>
        private sealed class GatedSessionWriteStore : ISharedKeyValueStore, IDisposable
        {
            public GatedSessionWriteStore(ISharedKeyValueStore inner)
            {
                m_inner = inner;
            }

            public bool HoldSessionWrites { get; set; }

            public TaskCompletionSource<bool> SessionWriteHeld { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public void ReleaseSessionWrites()
            {
                HoldSessionWrites = false;
                m_release.TrySetResult(true);
            }

            public ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default)
            {
                return m_inner.TryGetAsync(key, ct);
            }

            public ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default)
            {
                return m_inner.SetAsync(key, value, ct);
            }

            public async ValueTask<bool> CompareAndSwapAsync(
                string key,
                ByteString expected,
                ByteString value,
                CancellationToken ct = default)
            {
                if (HoldSessionWrites && key.StartsWith("session/", StringComparison.Ordinal))
                {
                    SessionWriteHeld.TrySetResult(true);
                    await m_release.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                return await m_inner.CompareAndSwapAsync(key, expected, value, ct).ConfigureAwait(false);
            }

            public ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                return m_inner.DeleteAsync(key, ct);
            }

            public IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(
                string keyPrefix,
                CancellationToken ct = default)
            {
                return m_inner.ScanAsync(keyPrefix, ct);
            }

            public IAsyncEnumerable<KeyValueChange> WatchAsync(string keyPrefix, CancellationToken ct = default)
            {
                return m_inner.WatchAsync(keyPrefix, ct);
            }

            public void Dispose()
            {
                m_release.TrySetResult(true);
            }

            private readonly ISharedKeyValueStore m_inner;
            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
