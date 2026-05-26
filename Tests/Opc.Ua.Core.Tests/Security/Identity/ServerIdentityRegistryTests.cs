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
 *
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
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Identity
{
    /// <summary>
    /// Tests for <see cref="ServerIdentityRegistry"/> dispatch semantics:
    /// precedence, profile-URI matching, NotHandled passthrough,
    /// Reject short-circuit.
    /// </summary>
    [TestFixture]
    [Category("Identity")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ServerIdentityRegistryTests
    {
        [Test]
        public async Task EmptyRegistryReturnsNotHandledAsync()
        {
            var registry = new ServerIdentityRegistry();
            AuthenticationContext context = MakeContext(new AnonymousIdentityTokenHandler());

            AuthenticationResult result = await registry.AuthenticateAsync(context).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.NotHandled));
        }

        [Test]
        public async Task DispatchesByTokenTypeAsync()
        {
            var anonymous = new StubAuthenticator(UserTokenType.Anonymous, null);
            var userName = new StubAuthenticator(UserTokenType.UserName, null);
            var registry = new ServerIdentityRegistry(anonymous, userName);

            AuthenticationContext anonCtx = MakeContext(new AnonymousIdentityTokenHandler());
            AuthenticationResult anonResult = await registry.AuthenticateAsync(anonCtx).ConfigureAwait(false);

            Assert.That(anonResult.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(anonymous.CallCount, Is.EqualTo(1));
            Assert.That(userName.CallCount, Is.Zero);
        }

        [Test]
        public async Task SkipsAuthenticatorsWithMismatchedTokenTypeAsync()
        {
            var anonymous = new StubAuthenticator(UserTokenType.Anonymous, null);
            var userName = new StubAuthenticator(UserTokenType.UserName, null);
            var registry = new ServerIdentityRegistry(anonymous, userName);

            var userNameToken = new UserNameIdentityTokenHandler("alice", new byte[] { 0x01 });
            AuthenticationContext userNameCtx = MakeContext(userNameToken);
            AuthenticationResult userNameResult = await registry.AuthenticateAsync(userNameCtx).ConfigureAwait(false);

            Assert.That(userNameResult.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(anonymous.CallCount, Is.Zero);
            Assert.That(userName.CallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DispatchesIssuedTokensByProfileUriAsync()
        {
            var jwt = new StubAuthenticator(UserTokenType.IssuedToken, Profiles.JwtUserToken);
            var saml = new StubAuthenticator(UserTokenType.IssuedToken, "urn:other:profile");
            var registry = new ServerIdentityRegistry(jwt, saml);

            var jwtHandler = new IssuedIdentityTokenHandler(
                Profiles.JwtUserToken,
                new byte[] { 0x10 });
            AuthenticationContext context = MakeContext(jwtHandler);

            AuthenticationResult result = await registry.AuthenticateAsync(context).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(jwt.CallCount, Is.EqualTo(1));
            Assert.That(saml.CallCount, Is.Zero);
        }

        [Test]
        public async Task IssuedTokenAuthenticatorWithoutProfileMatchesAnyAsync()
        {
            // Registering with a null profile URI is the "catch-all" for issued tokens
            // — used by adapters that bridge to legacy token-validator callbacks.
            var catchAll = new StubAuthenticator(UserTokenType.IssuedToken, null);
            var registry = new ServerIdentityRegistry(catchAll);

            var jwtHandler = new IssuedIdentityTokenHandler(
                Profiles.JwtUserToken,
                new byte[] { 0x10 });
            AuthenticationContext context = MakeContext(jwtHandler);

            AuthenticationResult result = await registry.AuthenticateAsync(context).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(catchAll.CallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task NotHandledFallsThroughToNextAuthenticatorAsync()
        {
            var declining = new StubAuthenticator(UserTokenType.IssuedToken, Profiles.JwtUserToken)
            {
                ReturnOutcome = AuthenticationOutcome.NotHandled
            };
            var accepting = new StubAuthenticator(UserTokenType.IssuedToken, Profiles.JwtUserToken);
            var registry = new ServerIdentityRegistry(declining, accepting);

            var jwtHandler = new IssuedIdentityTokenHandler(
                Profiles.JwtUserToken,
                new byte[] { 0x10 });
            AuthenticationContext context = MakeContext(jwtHandler);

            AuthenticationResult result = await registry.AuthenticateAsync(context).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(declining.CallCount, Is.EqualTo(1));
            Assert.That(accepting.CallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RejectShortCircuitsRemainingAuthenticatorsAsync()
        {
            var rejector = new StubAuthenticator(UserTokenType.Anonymous, null)
            {
                ReturnOutcome = AuthenticationOutcome.Rejected
            };
            var nextOne = new StubAuthenticator(UserTokenType.Anonymous, null);
            var registry = new ServerIdentityRegistry(rejector, nextOne);

            AuthenticationContext context = MakeContext(new AnonymousIdentityTokenHandler());
            AuthenticationResult result = await registry.AuthenticateAsync(context).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(nextOne.CallCount, Is.Zero);
        }

        [Test]
        public void RegisterReplacesEntryWithSameKey()
        {
            var first = new StubAuthenticator(UserTokenType.Anonymous, null);
            var second = new StubAuthenticator(UserTokenType.Anonymous, null);
            var registry = new ServerIdentityRegistry(first);

            registry.Register(second);

            // Old instance is removed from key dispatch; only one entry of type
            // Anonymous remains accessible.
            Assert.That(registry.Unregister(first), Is.False);
            Assert.That(registry.Unregister(second), Is.True);
        }

        [Test]
        public void UnregisterReturnsFalseWhenAuthenticatorIsNotRegistered()
        {
            var registry = new ServerIdentityRegistry();
            var auth = new StubAuthenticator(UserTokenType.Anonymous, null);
            Assert.That(registry.Unregister(auth), Is.False);
        }

        [Test]
        public void RegisterRejectsNull()
        {
            var registry = new ServerIdentityRegistry();
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => registry.Register(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("authenticator"));
        }

        [Test]
        public void AcceptHelperRejectsNullIdentity()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => AuthenticationResult.Accept(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("identity"));
        }

        [Test]
        public void RejectHelperRejectsNullError()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => AuthenticationResult.Reject(null!))!;
            Assert.That(ex.ParamName, Is.EqualTo("error"));
        }

        private static AuthenticationContext MakeContext(IUserIdentityTokenHandler handler)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new AuthenticationContext(
                handler,
                new UserTokenPolicy(handler.TokenType),
                new EndpointDescription(),
                ServiceMessageContext.CreateEmpty(telemetry));
        }

        private sealed class StubAuthenticator : IUserTokenAuthenticator
        {
            public StubAuthenticator(UserTokenType tokenType, string profileUri)
            {
                TokenType = tokenType;
                IssuedTokenProfileUri = profileUri;
            }

            public UserTokenType TokenType { get; }

            public string IssuedTokenProfileUri { get; }

            public AuthenticationOutcome ReturnOutcome { get; set; } = AuthenticationOutcome.Accepted;

            public int CallCount { get; private set; }

            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                CallCount++;
                return ReturnOutcome switch
                {
                    AuthenticationOutcome.Accepted
                        => new ValueTask<AuthenticationResult>(
                            AuthenticationResult.Accept(new UserIdentity())),
                    AuthenticationOutcome.Rejected
                        => new ValueTask<AuthenticationResult>(
                            AuthenticationResult.Reject(
                                new ServiceResult(StatusCodes.BadIdentityTokenRejected))),
                    _ => new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled)
                };
            }
        }
    }
}
