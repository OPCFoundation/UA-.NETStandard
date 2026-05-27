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
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Identity
{
    [TestFixture]
    [Category("Identity")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ServerIdentityRegistryAugmenterTests
    {
        [Test]
        public async Task AugmentersRunAfterAcceptedAsync()
        {
            IUserIdentity original = new NamedIdentity("original");
            IUserIdentity augmented = new NamedIdentity("augmented");
            var authenticator = new StubAuthenticator(AuthenticationResult.Accept(original));
            var augmenter = new StubAugmenter(augmented);
            var registry = new ServerIdentityRegistry(authenticator);
            registry.RegisterAugmenter(augmenter);

            AuthenticationResult result = await registry.AuthenticateAsync(MakeContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.SameAs(augmented));
            Assert.That(augmenter.InputIdentity, Is.SameAs(original));
            Assert.That(augmenter.CallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task MultipleAugmentersChainInRegistrationOrderAsync()
        {
            IUserIdentity original = new NamedIdentity("original");
            IUserIdentity first = new NamedIdentity("first");
            IUserIdentity second = new NamedIdentity("second");
            var registry = new ServerIdentityRegistry(
                new StubAuthenticator(AuthenticationResult.Accept(original)));
            var firstAugmenter = new StubAugmenter(first);
            var secondAugmenter = new StubAugmenter(second);
            registry.RegisterAugmenter(firstAugmenter);
            registry.RegisterAugmenter(secondAugmenter);

            AuthenticationResult result = await registry.AuthenticateAsync(MakeContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.SameAs(second));
            Assert.That(firstAugmenter.InputIdentity, Is.SameAs(original));
            Assert.That(secondAugmenter.InputIdentity, Is.SameAs(first));
        }

        [Test]
        public async Task AugmentersAreSkippedOnNotHandledAsync()
        {
            var registry = new ServerIdentityRegistry(new StubAuthenticator(AuthenticationResult.NotHandled));
            var augmenter = new StubAugmenter(new NamedIdentity("unused"));
            registry.RegisterAugmenter(augmenter);

            AuthenticationResult result = await registry.AuthenticateAsync(MakeContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.NotHandled));
            Assert.That(augmenter.CallCount, Is.Zero);
        }

        [Test]
        public async Task AugmentersAreSkippedOnRejectedAsync()
        {
            var registry = new ServerIdentityRegistry(
                new StubAuthenticator(AuthenticationResult.Reject(
                    new ServiceResult(StatusCodes.BadIdentityTokenRejected))));
            var augmenter = new StubAugmenter(new NamedIdentity("unused"));
            registry.RegisterAugmenter(augmenter);

            AuthenticationResult result = await registry.AuthenticateAsync(MakeContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(augmenter.CallCount, Is.Zero);
        }

        [Test]
        public async Task AugmenterReturningUnchangedIdentityIsNoOpAsync()
        {
            IUserIdentity identity = new NamedIdentity("original");
            var registry = new ServerIdentityRegistry(
                new StubAuthenticator(AuthenticationResult.Accept(identity)));
            registry.RegisterAugmenter(new IdentityPreservingAugmenter());

            AuthenticationResult result = await registry.AuthenticateAsync(MakeContext()).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.SameAs(identity));
        }

        [Test]
        public void RegisterAugmenterRejectsNull()
        {
            var registry = new ServerIdentityRegistry();

            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => registry.RegisterAugmenter(null))!;

            Assert.That(ex.ParamName, Is.EqualTo("augmenter"));
        }

        [Test]
        public void UnregisterAugmenterReturnsExpectedResults()
        {
            var registry = new ServerIdentityRegistry();
            var known = new IdentityPreservingAugmenter();
            var unknown = new IdentityPreservingAugmenter();
            registry.RegisterAugmenter(known);

            Assert.That(registry.UnregisterAugmenter(known), Is.True);
            Assert.That(registry.UnregisterAugmenter(unknown), Is.False);
        }

        [Test]
        public void AugmenterReturningNullThrowsInvalidOperationException()
        {
            var registry = new ServerIdentityRegistry(
                new StubAuthenticator(AuthenticationResult.Accept(new NamedIdentity("original"))));
            registry.RegisterAugmenter(new NullAugmenter());

            InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await registry.AuthenticateAsync(MakeContext()).ConfigureAwait(false));

            Assert.That(ex.Message, Does.Contain("IIdentityAugmenter"));
            Assert.That(ex.Message, Does.Contain("returned null"));
        }

        private static AuthenticationContext MakeContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new AuthenticationContext(
                new AnonymousIdentityTokenHandler(),
                new UserTokenPolicy(UserTokenType.Anonymous),
                new EndpointDescription(),
                ServiceMessageContext.CreateEmpty(telemetry));
        }

        private sealed class StubAuthenticator : IUserTokenAuthenticator
        {
            private readonly AuthenticationResult m_result;

            public StubAuthenticator(AuthenticationResult result)
            {
                m_result = result;
            }

            public UserTokenType TokenType => UserTokenType.Anonymous;

            public string IssuedTokenProfileUri => null;

            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                return new ValueTask<AuthenticationResult>(m_result);
            }
        }

        private sealed class StubAugmenter : IIdentityAugmenter
        {
            private readonly IUserIdentity m_result;

            public StubAugmenter(IUserIdentity result)
            {
                m_result = result;
            }

            public int CallCount { get; private set; }

            public IUserIdentity InputIdentity { get; private set; }

            public ValueTask<IUserIdentity> AugmentAsync(
                IUserIdentity identity,
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                CallCount++;
                InputIdentity = identity;
                return new ValueTask<IUserIdentity>(m_result);
            }
        }

        private sealed class IdentityPreservingAugmenter : IIdentityAugmenter
        {
            public ValueTask<IUserIdentity> AugmentAsync(
                IUserIdentity identity,
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                return new ValueTask<IUserIdentity>(identity);
            }
        }

        private sealed class NullAugmenter : IIdentityAugmenter
        {
            public ValueTask<IUserIdentity> AugmentAsync(
                IUserIdentity identity,
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                return new ValueTask<IUserIdentity>((IUserIdentity)null);
            }
        }

        private sealed class NamedIdentity : IUserIdentity
        {
            public NamedIdentity(string displayName)
            {
                DisplayName = displayName;
            }

            public string DisplayName { get; }

            public string PolicyId => string.Empty;

            public UserTokenType TokenType => UserTokenType.Anonymous;

            public XmlQualifiedName IssuedTokenType => XmlQualifiedName.Empty;

            public bool SupportsSignatures => false;

            public ArrayOf<NodeId> GrantedRoleIds => [];

            public IUserIdentityTokenHandler TokenHandler => new AnonymousIdentityTokenHandler();
        }
    }
}
