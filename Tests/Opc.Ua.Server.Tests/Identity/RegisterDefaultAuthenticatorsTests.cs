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

#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Server.Tests.Identity
{
    [TestFixture]
    [Category("Identity")]
    public class RegisterDefaultAuthenticatorsTests
    {
        [Test]
        public void RegisterDefaultAuthenticatorsUserDatabaseOverloadRegistersAnonymousAndUserName()
        {
            var registry = new RecordingRegistry();
            registry.RegisterDefaultAuthenticators(Mock.Of<IUserDatabase>());

            Assert.That(registry.Authenticators, Has.Count.EqualTo(2));
            Assert.That(registry.Authenticators[0], Is.TypeOf<AnonymousAuthenticator>());
            Assert.That(registry.Authenticators[1], Is.TypeOf<UserNamePasswordAuthenticator>());
        }

        [Test]
        public void RegisterDefaultAuthenticatorsAddsX509WhenCertificateValidatorSupplied()
        {
            var registry = new RecordingRegistry();
            registry.RegisterDefaultAuthenticators(
                Mock.Of<IUserDatabase>(),
                Mock.Of<ICertificateValidatorEx>());

            Assert.That(registry.Authenticators, Has.Count.EqualTo(3));
            Assert.That(registry.Authenticators[2], Is.TypeOf<X509Authenticator>());
        }

        [Test]
        public void RegisterDefaultAuthenticatorsAddsJwtWhenTokenValidatorSupplied()
        {
            var registry = new RecordingRegistry();
            registry.RegisterDefaultAuthenticators(
                Mock.Of<IUserDatabase>(),
                userCertificateValidator: null,
                jwtTokenValidator: (handler, ct) => new ValueTask<IUserIdentity?>((IUserIdentity)null));

            Assert.That(registry.Authenticators, Has.Count.EqualTo(3));
            Assert.That(registry.Authenticators[2], Is.TypeOf<JwtAuthenticator>());
            var jwt = (JwtAuthenticator)registry.Authenticators[2];
            Assert.That(jwt.IssuedTokenProfileUri, Is.EqualTo(Profiles.JwtUserToken));
        }

        [Test]
        public void RegisterDefaultAuthenticatorsReturnsSameRegistryInstance()
        {
            var registry = new RecordingRegistry();
            IServerIdentityRegistry result = registry.RegisterDefaultAuthenticators(Mock.Of<IUserDatabase>());
            Assert.That(result, Is.SameAs(registry));
        }

        [Test]
        public void RegisterDefaultAuthenticatorsRejectsNullUserDatabase()
        {
            var registry = new RecordingRegistry();
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => registry.RegisterDefaultAuthenticators((IUserDatabase)null));
            Assert.That(ex.ParamName, Is.EqualTo("userDatabase"));
        }

        [Test]
        public void RegisterDefaultAuthenticatorsDelegateOverloadAllExtrasRegisters()
        {
            var registry = new RecordingRegistry();
            registry.RegisterDefaultAuthenticators(
                verifyUserName: (handler, ct) => new ValueTask<IUserIdentity>(new UserIdentity()),
                verifyX509: (handler, ct) => new ValueTask<IUserIdentity>(new UserIdentity()),
                verifyJwt: (handler, ct) => new ValueTask<IUserIdentity?>((IUserIdentity)null));

            Assert.That(registry.Authenticators, Has.Count.EqualTo(4));
            Assert.That(registry.Authenticators[0], Is.TypeOf<AnonymousAuthenticator>());
            Assert.That(registry.Authenticators[1], Is.TypeOf<UserNamePasswordAuthenticator>());
            Assert.That(registry.Authenticators[2], Is.TypeOf<X509Authenticator>());
            Assert.That(registry.Authenticators[3], Is.TypeOf<JwtAuthenticator>());
        }

        [Test]
        public void RegisterDefaultAuthenticatorsDelegateOverloadRejectsNullUserNameVerifier()
        {
            var registry = new RecordingRegistry();
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => registry.RegisterDefaultAuthenticators(verifyUserName: null));
            Assert.That(ex.ParamName, Is.EqualTo("verifyUserName"));
        }

        [Test]
        public void RegisterDefaultAuthenticatorsUserDatabaseOverloadRejectsNullRegistry()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => ((IServerIdentityRegistry)null).RegisterDefaultAuthenticators(Mock.Of<IUserDatabase>()));
            Assert.That(ex.ParamName, Is.EqualTo("registry"));
        }

        [Test]
        public void RegisterDefaultAuthenticatorsDelegateOverloadRejectsNullRegistry()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
                () => ((IServerIdentityRegistry)null).RegisterDefaultAuthenticators(
                    (handler, ct) => new ValueTask<IUserIdentity>(new UserIdentity())));
            Assert.That(ex.ParamName, Is.EqualTo("registry"));
        }

        [Test]
        public async Task UserDatabaseVerifierAcceptsValidCredentialsAsync()
        {
            var database = new FakeUserDatabase { CredentialsValid = true };

            UserNamePasswordAuthenticator authenticator = GetUserNamePasswordAuthenticator(database);
            AuthenticationResult result = await authenticator
                .AuthenticateAsync(CreateContext("alice", [1, 2, 3, 4]))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.Not.Null);
            Assert.That(result.Identity!.DisplayName, Is.EqualTo("alice"));
        }

        [Test]
        public async Task UserDatabaseVerifierRejectsInvalidCredentialsAsync()
        {
            var database = new FakeUserDatabase { CredentialsValid = false };

            UserNamePasswordAuthenticator authenticator = GetUserNamePasswordAuthenticator(database);
            AuthenticationResult result = await authenticator
                .AuthenticateAsync(CreateContext("alice", [1, 2, 3, 4]))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task UserDatabaseVerifierRejectsEmptyUserNameAsync()
        {
            UserNamePasswordAuthenticator authenticator =
                GetUserNamePasswordAuthenticator(new FakeUserDatabase());
            AuthenticationResult result = await authenticator
                .AuthenticateAsync(CreateContext(string.Empty, [1, 2, 3, 4]))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task UserDatabaseVerifierRejectsEmptyPasswordAsync()
        {
            UserNamePasswordAuthenticator authenticator =
                GetUserNamePasswordAuthenticator(new FakeUserDatabase());
            AuthenticationResult result = await authenticator
                .AuthenticateAsync(CreateContext("alice", []))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadIdentityTokenInvalid));
        }

        private static UserNamePasswordAuthenticator GetUserNamePasswordAuthenticator(IUserDatabase database)
        {
            var registry = new RecordingRegistry();
            registry.RegisterDefaultAuthenticators(database);
            return (UserNamePasswordAuthenticator)registry.Authenticators[1];
        }

        private static AuthenticationContext CreateContext(string userName, byte[] password)
        {
            var handler = new UserNameIdentityTokenHandler(userName, password);
            return new AuthenticationContext(
                handler,
                new UserTokenPolicy(),
                new EndpointDescription(),
                null!);
        }

        private sealed class FakeUserDatabase : IUserDatabase
        {
            public bool CredentialsValid { get; set; }

            public bool CheckCredentials(string userName, ReadOnlySpan<byte> password)
            {
                return CredentialsValid;
            }

            public bool CreateUser(string userName, ReadOnlySpan<byte> password, ICollection<Role> roles)
            {
                return true;
            }

            public bool DeleteUser(string userName)
            {
                return true;
            }

            public ICollection<Role> GetUserRoles(string userName)
            {
                return [];
            }

            public IReadOnlyList<UserManagementDataType> GetUsers()
            {
                return [];
            }

            public bool ChangePassword(
                string userName,
                ReadOnlySpan<byte> oldPassword,
                ReadOnlySpan<byte> newPassword)
            {
                return true;
            }
        }

        private sealed class RecordingRegistry : IServerIdentityRegistry
        {
            public List<IUserTokenAuthenticator> Authenticators { get; } = [];

            public void Register(IUserTokenAuthenticator authenticator)
            {
                Authenticators.Add(authenticator);
            }

            public bool Unregister(IUserTokenAuthenticator authenticator)
            {
                return Authenticators.Remove(authenticator);
            }

            public void RegisterAugmenter(IIdentityAugmenter augmenter)
            {
            }

            public bool UnregisterAugmenter(IIdentityAugmenter augmenter)
            {
                return false;
            }

            public ValueTask<AuthenticationResult> AuthenticateAsync(
                AuthenticationContext context,
                CancellationToken ct = default)
            {
                return new ValueTask<AuthenticationResult>(AuthenticationResult.NotHandled);
            }
        }
    }
}
