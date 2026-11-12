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
using System.Xml;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Identity;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// OPC 10000-4 7.40.6: once the identity token of a Session has expired, the
    /// Session's credentials are invalid. A Publish request that was accepted before
    /// the expiration and waits in the queue must not deliver a NotificationMessage
    /// after it.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [NonParallelizable]
    public class IdentityTokenExpiryPublishTests : TestFixture
    {
        private static readonly TimeSpan s_tokenLifetime = TimeSpan.FromSeconds(4);
        private const double kPublishingInterval = 200;
        // the next keep-alive is due 30 x 200 ms = 6 s after the first message,
        // well after the expiration
        private const uint kKeepAliveCount = 30;

        [Test]
        public async Task QueuedPublishDeliversNothingAfterTheTokenExpiredAsync()
        {
            var augmenter = new ExpiringSysAdminAugmenter(s_tokenLifetime);
            ReferenceServer.CurrentInstance.IdentityRegistry.RegisterAugmenter(augmenter);
            ISession? session = null;
            try
            {
                session = await ConnectAsSysAdminAsync().ConfigureAwait(false);
                if (session == null)
                {
                    Assert.Ignore("The server exposes no endpoint with a UserName token policy.");
                }
                DateTime expiresAt = augmenter.LastExpiresAt;
                Assert.That(expiresAt, Is.Not.Default, "the identity was not wrapped");

                CreateSubscriptionResponse created = await session.CreateSubscriptionAsync(
                    null,
                    kPublishingInterval,
                    kKeepAliveCount * 3,
                    kKeepAliveCount,
                    0,
                    true,
                    0,
                    CancellationToken.None).ConfigureAwait(false);

                // the first message (a keep-alive) is returned in the first cycle
                PublishResponse first = await session.PublishAsync(null, default, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(first.SubscriptionId, Is.EqualTo(created.SubscriptionId));

                // this request is accepted before the expiration and parks until the
                // next keep-alive, which is due after the expiration
                if (DateTime.UtcNow > expiresAt - TimeSpan.FromSeconds(1))
                {
                    Assert.Inconclusive("The setup took too long to park a Publish before the token expired.");
                }
                Task<PublishResponse> parked = session
                    .PublishAsync(new RequestHeader { TimeoutHint = 30_000 }, default, CancellationToken.None)
                    .AsTask();

                TimeSpan untilExpired = expiresAt - DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
                if (untilExpired > TimeSpan.Zero)
                {
                    await Task.Delay(untilExpired).ConfigureAwait(false);
                }
                Assert.That(parked.IsCompleted, Is.False,
                    "the Publish must still be parked when the token expires");

                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await parked.ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
            }
            finally
            {
                ReferenceServer.CurrentInstance.IdentityRegistry.UnregisterAugmenter(augmenter);
                if (session != null)
                {
                    // CloseSession is still served after the expiration
                    await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    session.Dispose();
                }
            }
        }

        /// <summary>
        /// Gives the sysadmin identity a token expiration a fixed time after activation.
        /// </summary>
        private sealed class ExpiringSysAdminAugmenter : IIdentityAugmenter
        {
            private readonly TimeSpan m_lifetime;
            private long m_lastExpiresAtTicks;

            public ExpiringSysAdminAugmenter(TimeSpan lifetime)
            {
                m_lifetime = lifetime;
            }

            public DateTime LastExpiresAt => new(Interlocked.Read(ref m_lastExpiresAtTicks), DateTimeKind.Utc);

            public ValueTask<AuthenticationResult> AugmentAsync(
                IUserIdentity identity,
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                if (!string.Equals(identity.DisplayName, "sysadmin", StringComparison.Ordinal))
                {
                    return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
                }
                DateTime expiresAt = DateTime.UtcNow + m_lifetime;
                Interlocked.Exchange(ref m_lastExpiresAtTicks, expiresAt.Ticks);
                return new ValueTask<AuthenticationResult>(
                    AuthenticationResult.Accept(new ExpiringIdentity(identity, expiresAt)));
            }
        }

        private sealed class ExpiringIdentity : IUserIdentity, IIdentityExpiration
        {
            private readonly IUserIdentity m_inner;

            public ExpiringIdentity(IUserIdentity inner, DateTime expiresAt)
            {
                m_inner = inner;
                ExpiresAt = expiresAt;
            }

            public DateTime? ExpiresAt { get; }

            public string DisplayName => m_inner.DisplayName;

            public string PolicyId => m_inner.PolicyId;

            public UserTokenType TokenType => m_inner.TokenType;

            public XmlQualifiedName IssuedTokenType => m_inner.IssuedTokenType;

            public bool SupportsSignatures => m_inner.SupportsSignatures;

            public ArrayOf<NodeId> GrantedRoleIds => m_inner.GrantedRoleIds;

            public IUserIdentityTokenHandler TokenHandler => m_inner.TokenHandler;
        }
    }
}
