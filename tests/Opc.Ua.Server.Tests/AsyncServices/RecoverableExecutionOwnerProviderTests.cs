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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Tests local stable-owner mapping and original persisted proofs, not recovery-mode execution.
    /// Sessions represent already authenticated Core state; no transport or certificate store is opened.
    /// </summary>
    [TestFixture]
    [Category("AsyncServices")]
    [Parallelizable]
    public sealed class RecoverableExecutionOwnerProviderTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_certificate = CertificateBuilder
                .Create("CN=RecoverableExecutionOwnerProviderTests")
                .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                .CreateForRSA();
            m_replacementCertificate = CertificateBuilder
                .Create("CN=RecoverableExecutionOwnerProviderTests")
                .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                .CreateForRSA();
            m_certificateDigest = HashCertificate(m_certificate);
            m_replacementDigest = HashCertificate(m_replacementCertificate);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_replacementCertificate?.Dispose();
            m_certificate?.Dispose();
        }

        [TestCase("authorityId")]
        [TestCase("applicationId")]
        [TestCase("userId")]
        public void OwnerIdRejectsZeroIdentifier(string parameterName)
        {
            Uuid authority = parameterName == "authorityId" ? Uuid.Empty : s_authorityId;
            Uuid application = parameterName == "applicationId" ? Uuid.Empty : s_applicationId;
            Uuid user = parameterName == "userId" ? Uuid.Empty : s_userId;

            Assert.That(
                () => new RecoverableExecutionOwnerId(authority, application, user),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo(parameterName));
        }

        [TestCase("authority")]
        [TestCase("application")]
        [TestCase("user")]
        public void OwnerIdEqualityIncludesAuthorityApplicationAndUser(string changedPart)
        {
            RecoverableExecutionOwnerId owner = ExpectedOwnerId;
            var equal = new RecoverableExecutionOwnerId(s_authorityId, s_applicationId, s_userId);
            var different = new RecoverableExecutionOwnerId(
                changedPart == "authority" ? s_otherAuthorityId : s_authorityId,
                changedPart == "application" ? s_otherApplicationId : s_applicationId,
                changedPart == "user" ? s_otherUserId : s_userId);
            var keys = new HashSet<RecoverableExecutionOwnerId> { owner, equal, different };

            Assert.That(owner.AuthorityId, Is.EqualTo(s_authorityId));
            Assert.That(owner.ApplicationId, Is.EqualTo(s_applicationId));
            Assert.That(owner.UserId, Is.EqualTo(s_userId));
            Assert.That(owner, Is.EqualTo(equal));
            Assert.That(owner, Is.Not.EqualTo(different));
            Assert.That(owner.GetHashCode(), Is.EqualTo(equal.GetHashCode()));
            Assert.That(keys, Has.Count.EqualTo(2));
        }

        [Test]
        public void OwnerIdToStringIsRedacted()
        {
            string diagnostic = ExpectedOwnerId.ToString();

            Assert.That(diagnostic, Is.EqualTo("RecoverableExecutionOwnerId"));
            Assert.That(diagnostic, Does.Not.Contain(s_authorityId.ToString())
                .And.Not.Contain(s_applicationId.ToString())
                .And.Not.Contain(s_userId.ToString()));
        }

        [Test]
        public void OwnerRejectsDefaultId()
        {
            Assert.That(
                () => new RecoverableExecutionOwner(default, [1]),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("id"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void OwnerRejectsEmptyContinuityContext(bool useDefault)
        {
            ByteString context = useDefault ? default : ByteString.Empty;

            Assert.That(
                () => new RecoverableExecutionOwner(ExpectedOwnerId, context),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("continuityContext"));
        }

        [Test]
        public void OwnerCopiesContinuityContext()
        {
            byte[] bytes = [0x42];
            var owner = new RecoverableExecutionOwner(ExpectedOwnerId, new ByteString(bytes));

            bytes[0] = 0x99;
            byte[] exported = owner.ContinuityContext.ToArray();
            exported[0] = 0x77;

            ByteString expected = [0x42];
            Assert.That(owner.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(owner.ContinuityContext, Is.EqualTo(expected));
        }

        [Test]
        public void ApplicationMappingRejectsZeroId()
        {
            Assert.That(
                () => new RecoverableExecutionApplicationMapping(Uuid.Empty, m_certificateDigest),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("applicationId"));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(31)]
        [TestCase(33)]
        public void ApplicationMappingRejectsInvalidDigestLength(int length)
        {
            ByteString digest = length < 0 ? default : new ByteString(new byte[length]);

            Assert.That(
                () => new RecoverableExecutionApplicationMapping(s_applicationId, digest),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("certificateSha256"));
        }

        [Test]
        public void ApplicationMappingCopiesSha256Digest()
        {
            byte[] bytes = m_certificateDigest.ToArray();
            var mapping = new RecoverableExecutionApplicationMapping(s_applicationId, new ByteString(bytes));

            bytes[0] ^= 0xff;
            byte[] exported = mapping.CertificateSha256.ToArray();
            exported[31] ^= 0xff;

            Assert.That(mapping.ApplicationId, Is.EqualTo(s_applicationId));
            Assert.That(mapping.CertificateSha256.Length, Is.EqualTo(32));
            Assert.That(mapping.CertificateSha256, Is.EqualTo(m_certificateDigest));
        }

        [Test]
        public void UserMappingRejectsZeroId()
        {
            Assert.That(
                () => new RecoverableExecutionUserMapping(Uuid.Empty, kIssuer, kSubject),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("userId"));
        }

        [TestCase(null, kSubject, "issuer")]
        [TestCase("", kSubject, "issuer")]
        [TestCase(" \t", kSubject, "issuer")]
        [TestCase(kIssuer, null, "subject")]
        [TestCase(kIssuer, "", "subject")]
        [TestCase(kIssuer, " \t", "subject")]
        public void UserMappingRejectsMissingIssuerOrSubject(string? issuer, string? subject, string parameterName)
        {
            Assert.That(
                () => new RecoverableExecutionUserMapping(s_userId, issuer!, subject!),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo(parameterName));
        }

        [Test]
        public void ProviderRejectsZeroAuthority()
        {
            Assert.That(
                () => new ConfiguredRecoverableExecutionOwnerProvider(
                    Uuid.Empty, DefaultApplications(), DefaultUsers()),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("authorityId"));
        }

        [TestCase("applications", true)]
        [TestCase("applications", false)]
        [TestCase("users", true)]
        [TestCase("users", false)]
        public void ProviderRejectsEmptyMappingArrays(string parameterName, bool useDefault)
        {
            ArrayOf<RecoverableExecutionApplicationMapping> applications = DefaultApplications();
            ArrayOf<RecoverableExecutionUserMapping> users = DefaultUsers();
            if (parameterName == "applications")
            {
                applications = useDefault ? default : [];
            }
            else
            {
                users = useDefault ? default : [];
            }

            Assert.That(
                () => new ConfiguredRecoverableExecutionOwnerProvider(s_authorityId, applications, users),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo(parameterName));
        }

        [TestCase("applications", true)]
        [TestCase("applications", false)]
        [TestCase("users", true)]
        [TestCase("users", false)]
        public void ProviderRejectsNullMappingEntries(string parameterName, bool nullFirst)
        {
            ArrayOf<RecoverableExecutionApplicationMapping> applications = DefaultApplications();
            ArrayOf<RecoverableExecutionUserMapping> users = DefaultUsers();
            if (parameterName == "applications")
            {
                applications = nullFirst ? [null!, applications[0]] : [applications[0], null!];
            }
            else
            {
                users = nullFirst ? [null!, users[0]] : [users[0], null!];
            }

            Assert.That(
                () => new ConfiguredRecoverableExecutionOwnerProvider(s_authorityId, applications, users),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo(parameterName));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ProviderRejectsDuplicateApplicationKeys(bool conflictingId)
        {
            var duplicate = new RecoverableExecutionApplicationMapping(
                conflictingId ? s_otherApplicationId : s_applicationId,
                new ByteString(m_certificateDigest.ToArray()));

            Assert.That(
                () => new ConfiguredRecoverableExecutionOwnerProvider(
                    s_authorityId, [DefaultApplications()[0], duplicate], DefaultUsers()),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("applications"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ProviderRejectsDuplicateUserKeys(bool conflictingId)
        {
            var duplicate = new RecoverableExecutionUserMapping(
                conflictingId ? s_otherUserId : s_userId, kIssuer, kSubject);

            Assert.That(
                () => new ConfiguredRecoverableExecutionOwnerProvider(
                    s_authorityId, DefaultApplications(), [DefaultUsers()[0], duplicate]),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("users"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ProviderRejectsUnencodableClaims(bool invalidIssuer)
        {
            var user = new RecoverableExecutionUserMapping(
                s_userId, invalidIssuer ? "\ud800" : kIssuer, invalidIssuer ? kSubject : "\ud800");

            Assert.That(
                () => new ConfiguredRecoverableExecutionOwnerProvider(s_authorityId, DefaultApplications(), [user]),
                Throws.TypeOf<EncoderFallbackException>());
        }

        [Test]
        public async Task ProviderSnapshotsMappingArraysAsync()
        {
            RecoverableExecutionApplicationMapping[] applications =
                [new(s_applicationId, m_certificateDigest)];
            RecoverableExecutionUserMapping[] users = [new(s_userId, kIssuer, kSubject)];
            var provider = new ConfiguredRecoverableExecutionOwnerProvider(
                s_authorityId,
                new ArrayOf<RecoverableExecutionApplicationMapping>(
                    new ReadOnlyMemory<RecoverableExecutionApplicationMapping>(applications)),
                new ArrayOf<RecoverableExecutionUserMapping>(
                    new ReadOnlyMemory<RecoverableExecutionUserMapping>(users)));

            applications[0] = new RecoverableExecutionApplicationMapping(s_otherApplicationId, m_replacementDigest);
            users[0] = new RecoverableExecutionUserMapping(s_otherUserId, kIssuer, kOtherSubject);
            using OperationContext original = CreateContext(CreateSession(m_certificate));
            using OperationContext replacement = CreateContext(CreateSession(
                m_replacementCertificate, CreateIdentity(subject: kOtherSubject).Object));

            RecoverableExecutionOwner owner = await provider.ResolveAsync(original).ConfigureAwait(false);

            Assert.That(owner.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(owner.ContinuityContext.Slice(1, 32), Is.EqualTo(m_certificateDigest));
            await provider.ValidatePersistedOwnerAsync(owner).ConfigureAwait(false);
            await AssertResolveStatusAsync(provider, replacement, StatusCodes.BadUserAccessDenied)
                .ConfigureAwait(false);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ResolveSeparatesApplicationsAndUsersAsync(bool otherApplication, bool otherUser)
        {
            var provider = new ConfiguredRecoverableExecutionOwnerProvider(
                s_authorityId,
                [
                    new(s_applicationId, m_certificateDigest),
                    new(s_otherApplicationId, m_replacementDigest)
                ],
                [
                    new(s_userId, kIssuer, kSubject),
                    new(s_otherUserId, kIssuer, kOtherSubject)
                ]);
            using OperationContext context = CreateContext(CreateSession(
                otherApplication ? m_replacementCertificate : m_certificate,
                CreateIdentity(subject: otherUser ? kOtherSubject : kSubject).Object));

            RecoverableExecutionOwner owner = await provider.ResolveAsync(context).ConfigureAwait(false);

            var expected = new RecoverableExecutionOwnerId(
                s_authorityId,
                otherApplication ? s_otherApplicationId : s_applicationId,
                otherUser ? s_otherUserId : s_userId);
            Assert.That(owner.Id, Is.EqualTo(expected));
            Assert.That(owner.ContinuityContext.Slice(1, 32),
                Is.EqualTo(otherApplication ? m_replacementDigest : m_certificateDigest));
            await provider.ValidatePersistedOwnerAsync(owner).ConfigureAwait(false);
        }

        [Test]
        public async Task ResolveAcceptsSignWithoutEncryptionAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            Mock<ISession> session = CreateSession(m_certificate);
            session.Object.EndpointDescription.SecurityMode = MessageSecurityMode.Sign;
            using OperationContext context = CreateContext(session);

            RecoverableExecutionOwner owner = await provider.ResolveAsync(context).ConfigureAwait(false);

            Assert.That(owner.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(context.ChannelContext!.ProvidesIntegrity, Is.True);
            Assert.That(context.ChannelContext.ProvidesEncryption, Is.False);
            Assert.That(owner.ContinuityContext.Slice(1, 32), Is.EqualTo(m_certificateDigest));
        }

        [TestCase(UserTokenType.Certificate)]
        [TestCase(UserTokenType.IssuedToken)]
        public async Task ResolveAcceptsAuthenticatedCertificateAndIssuedTokensAsync(UserTokenType tokenType)
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            Mock<IUserIdentity> identity = CreateIdentity();
            identity.SetupGet(value => value.TokenType).Returns(tokenType);
            using OperationContext context = CreateContext(
                CreateSession(m_certificate, identity.Object, tokenType: tokenType));

            RecoverableExecutionOwner owner = await provider.ResolveAsync(context).ConfigureAwait(false);

            Assert.That(owner.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(owner.ContinuityContext.Slice(1, 32), Is.EqualTo(m_certificateDigest));
            await provider.ValidatePersistedOwnerAsync(owner).ConfigureAwait(false);
            identity.VerifyGet(value => value.TokenHandler, Times.Never);
        }

        [Test]
        public async Task ResolveReplacementSessionIgnoresSessionNamespaceUriAndDisplayNameAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            Mock<ISession> first = CreateSession(m_certificate);
            Mock<ISession> replacement = CreateSession(
                m_certificate, CreateIdentity(displayName: "Renamed display").Object, channelId: "replacement-channel");
            replacement.SetupGet(value => value.Id).Returns(new NodeId("replacement-session", 47));
            replacement.SetupGet(value => value.ClientApplicationUri).Returns("urn:changed:claimed-uri");
            using OperationContext firstContext = CreateContext(first);
            using OperationContext replacementContext = CreateContext(replacement);

            RecoverableExecutionOwner original = await provider.ResolveAsync(firstContext).ConfigureAwait(false);
            RecoverableExecutionOwner renewed = await provider.ResolveAsync(replacementContext).ConfigureAwait(false);

            Assert.That(original.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(renewed.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(firstContext.SessionId, Is.Not.EqualTo(replacementContext.SessionId));
            Assert.That(renewed.ContinuityContext.Slice(1, 32), Is.EqualTo(m_certificateDigest));
            await provider.ValidatePersistedOwnerAsync(original).ConfigureAwait(false);
        }

        [Test]
        public async Task ResolveExplicitCertificateAliasesShareOwnerButNotEvidenceAsync()
        {
            var provider = new ConfiguredRecoverableExecutionOwnerProvider(
                s_authorityId,
                [new(s_applicationId, m_certificateDigest), new(s_applicationId, m_replacementDigest)],
                DefaultUsers());
            using OperationContext originalContext = CreateContext(CreateSession(m_certificate));
            using OperationContext renewedContext = CreateContext(CreateSession(m_replacementCertificate));

            RecoverableExecutionOwner original = await provider.ResolveAsync(originalContext).ConfigureAwait(false);
            RecoverableExecutionOwner renewed = await provider.ResolveAsync(renewedContext).ConfigureAwait(false);

            Assert.That(original.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(renewed.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(original.ContinuityContext.Slice(1, 32), Is.EqualTo(m_certificateDigest));
            Assert.That(renewed.ContinuityContext.Slice(1, 32), Is.EqualTo(m_replacementDigest));
            Assert.That(renewed.ContinuityContext, Is.Not.EqualTo(original.ContinuityContext));
            await provider.ValidatePersistedOwnerAsync(original).ConfigureAwait(false);
            await provider.ValidatePersistedOwnerAsync(renewed).ConfigureAwait(false);
        }

        [TestCase("urn:rse04:another-issuer", kSubject)]
        [TestCase(kIssuer, kOtherSubject)]
        public async Task ResolveExplicitUserAliasesShareOwnerButNotEvidenceAsync(string issuer, string subject)
        {
            var provider = new ConfiguredRecoverableExecutionOwnerProvider(
                s_authorityId, DefaultApplications(),
                [new(s_userId, kIssuer, kSubject), new(s_userId, issuer, subject)]);
            using OperationContext originalContext = CreateContext(CreateSession(m_certificate));
            using OperationContext aliasContext = CreateContext(
                CreateSession(m_certificate, CreateIdentity(issuer, subject).Object));

            RecoverableExecutionOwner original = await provider.ResolveAsync(originalContext).ConfigureAwait(false);
            RecoverableExecutionOwner alias = await provider.ResolveAsync(aliasContext).ConfigureAwait(false);

            Assert.That(original.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(alias.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(alias.ContinuityContext, Is.Not.EqualTo(original.ContinuityContext));
            Assert.That(alias.ContinuityContext.Slice(1, 32), Is.EqualTo(m_certificateDigest));
            await provider.ValidatePersistedOwnerAsync(original).ConfigureAwait(false);
            await provider.ValidatePersistedOwnerAsync(alias).ConfigureAwait(false);
        }

        [Test]
        public async Task ResolveUnmappedRenewalRejectsMatchingApplicationUriAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            using OperationContext original = CreateContext(CreateSession(m_certificate));
            using OperationContext renewal = CreateContext(CreateSession(m_replacementCertificate));

            RecoverableExecutionOwner owner = await provider.ResolveAsync(original).ConfigureAwait(false);

            Assert.That(owner.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(original.Session.ClientApplicationUri, Is.EqualTo(kClaimedApplicationUri));
            Assert.That(renewal.Session.ClientApplicationUri, Is.EqualTo(kClaimedApplicationUri));
            Assert.That(m_replacementDigest, Is.Not.EqualTo(m_certificateDigest));
            await AssertResolveStatusAsync(provider, renewal, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
        }

        [TestCase("urn:rse04:unmapped-issuer", kSubject)]
        [TestCase(kIssuer, "unmapped-subject")]
        public async Task ResolveUnmappedUserRejectsMatchingDisplayNameAsync(string issuer, string subject)
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            Mock<IUserIdentity> originalIdentity = CreateIdentity();
            Mock<IUserIdentity> unmappedIdentity = CreateIdentity(issuer, subject);
            using OperationContext original = CreateContext(CreateSession(m_certificate, originalIdentity.Object));
            using OperationContext unmapped = CreateContext(CreateSession(m_certificate, unmappedIdentity.Object));

            RecoverableExecutionOwner owner = await provider.ResolveAsync(original).ConfigureAwait(false);

            Assert.That(owner.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(originalIdentity.Object.DisplayName, Is.EqualTo(kDisplayName));
            Assert.That(unmappedIdentity.Object.DisplayName, Is.EqualTo(kDisplayName));
            await AssertResolveStatusAsync(provider, unmapped, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
        }

        [Test]
        public async Task ResolveRejectsAnonymousTokenAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            using OperationContext context = CreateContext(
                CreateSession(m_certificate, CreateIdentity().Object, tokenType: UserTokenType.Anonymous));

            await AssertResolveStatusAsync(provider, context, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
        }

        [Test]
        public async Task ResolveRejectsIdentityWithoutClaimsAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            var identity = new Mock<IUserIdentity>(MockBehavior.Strict);
            identity.SetupGet(value => value.DisplayName).Returns(kDisplayName);
            identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
            using OperationContext context = CreateContext(CreateSession(m_certificate, identity.Object));

            await AssertResolveStatusAsync(provider, context, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
        }

        [TestCase(null, kSubject)]
        [TestCase("", kSubject)]
        [TestCase(" ", kSubject)]
        [TestCase(kIssuer, null)]
        [TestCase(kIssuer, "")]
        [TestCase(kIssuer, " ")]
        public async Task ResolveRejectsMissingIssuerOrSubjectAsync(string? issuer, string? subject)
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            using OperationContext context = CreateContext(
                CreateSession(m_certificate, CreateIdentity(issuer, subject).Object));

            await AssertResolveStatusAsync(provider, context, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ResolveRejectsAbsentOrUnvalidatedCertificateAsync(bool absent)
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            Mock<ISession> session = CreateSession(m_certificate);
            if (absent)
            {
                session.SetupGet(value => value.ClientCertificate).Returns((Certificate)null!);
            }
            else
            {
                ClientCertificateProvenance.SetValidated(session.Object, false);
            }
            var channel = new SecureChannelContext(
                kChannelId, session.Object.EndpointDescription, RequestEncoding.Binary, m_certificate.RawData)
            {
                HasTransportSecurity = true
            };
            using OperationContext context = CreateContext(session, channel);

            await AssertResolveStatusAsync(provider, context, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);

            Assert.That(channel.HasTransportSecurity, Is.True);
            Assert.That(channel.ClientChannelCertificate.Length, Is.EqualTo(m_certificate.RawData.Length));
        }

        [Test]
        public async Task ResolveRejectsNoneApplicationPolicyDespiteTlsAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            Mock<ISession> session = CreateSession(m_certificate);
            EndpointDescription endpoint = session.Object.EndpointDescription;
            endpoint.SecurityPolicyUri = SecurityPolicies.None;
            endpoint.SecurityMode = MessageSecurityMode.None;
            var channel = new SecureChannelContext(kChannelId, endpoint, RequestEncoding.Binary)
            {
                HasTransportSecurity = true
            };
            using OperationContext context = CreateContext(session, channel);

            await AssertResolveStatusAsync(provider, context, StatusCodes.BadSecurityModeRejected)
                .ConfigureAwait(false);

            Assert.That(channel.ProvidesEncryption, Is.True);
            Assert.That(channel.ProvidesIntegrity, Is.True);
        }

        [Test]
        public async Task ResolveRejectsMissingSessionAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            using var context = new OperationContext(
                new RequestHeader(), new SecureChannelContext(kChannelId, SignedEndpoint(), RequestEncoding.Binary),
                RequestType.Read, RequestLifetime.None, CreateIdentity().Object);

            await AssertResolveStatusAsync(provider, context, StatusCodes.BadSessionNotActivated).ConfigureAwait(false);
        }

        [TestCase("inactive")]
        [TestCase("closing")]
        [TestCase("expired")]
        public async Task ResolveRejectsInactiveClosingOrExpiredSessionAsync(string state)
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            Mock<ISession> session = CreateSession(m_certificate);
            session.SetupGet(value => value.Activated).Returns(state != "inactive");
            session.SetupGet(value => value.IsClosing).Returns(state == "closing");
            session.SetupGet(value => value.HasExpired).Returns(state == "expired");
            using OperationContext context = CreateContext(session);
            StatusCode expected = state == "inactive"
                ? StatusCodes.BadSessionNotActivated
                : StatusCodes.BadSessionClosed;

            await AssertResolveStatusAsync(provider, context, expected).ConfigureAwait(false);
        }

        [TestCase("missing-channel")]
        [TestCase("missing-endpoint")]
        [TestCase("wrong-channel")]
        public async Task ResolveRejectsMissingOrMismatchedChannelAsync(string fault)
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            Mock<ISession> session = CreateSession(m_certificate);
            var channel = new SecureChannelContext(
                fault == "wrong-channel" ? "unbound-channel" : kChannelId,
                fault == "missing-endpoint" ? null : SignedEndpoint(), RequestEncoding.Binary);
            using OperationContext context = fault == "missing-channel"
                ? new OperationContext(session.Object, DiagnosticsMasks.None)
                : CreateContext(session, channel);

            await AssertResolveStatusAsync(provider, context, StatusCodes.BadSecureChannelIdInvalid)
                .ConfigureAwait(false);
        }

        [TestCase("null-policy")]
        [TestCase("empty-policy")]
        [TestCase("different-policy")]
        [TestCase("weaker-mode")]
        [TestCase("stronger-mode")]
        public async Task ResolveRejectsMissingPolicyOrEndpointMismatchAsync(string fault)
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            Mock<ISession> session = CreateSession(m_certificate);
            EndpointDescription channelEndpoint = SignedEndpoint();
            switch (fault)
            {
                case "null-policy":
                    // Matching empty endpoints must not bypass the application-signing policy requirement.
                    channelEndpoint.SecurityPolicyUri = null;
                    session.Object.EndpointDescription.SecurityPolicyUri = null;
                    break;
                case "empty-policy":
                    channelEndpoint.SecurityPolicyUri = string.Empty;
                    session.Object.EndpointDescription.SecurityPolicyUri = string.Empty;
                    break;
                case "different-policy":
                    channelEndpoint.SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss;
                    break;
                case "weaker-mode":
                    channelEndpoint.SecurityMode = MessageSecurityMode.Sign;
                    break;
                case "stronger-mode":
                    session.Object.EndpointDescription.SecurityMode = MessageSecurityMode.Sign;
                    break;
            }
            using OperationContext context = CreateContext(
                session, new SecureChannelContext(kChannelId, channelEndpoint, RequestEncoding.Binary));

            await AssertResolveStatusAsync(provider, context, StatusCodes.BadSecurityModeRejected)
                .ConfigureAwait(false);
        }

        [TestCase("stale-before")]
        [TestCase("stale-during")]
        [TestCase("effective-before")]
        [TestCase("effective-during")]
        public async Task ResolveRejectsStaleOrChangedEffectiveIdentityAsync(string fault)
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            IUserIdentity original = CreateIdentity().Object;
            IUserIdentity changed = CreateIdentity().Object;
            Mock<ISession> session = CreateSession(m_certificate, original);
            using OperationContext context = CreateContext(session);
            switch (fault)
            {
                case "stale-before":
                    session.SetupGet(value => value.IsIdentityStale).Returns(true);
                    break;
                case "stale-during":
                    session.SetupSequence(value => value.IsIdentityStale).Returns(false).Returns(true);
                    break;
                case "effective-before":
                    session.SetupGet(value => value.EffectiveIdentity).Returns(changed);
                    break;
                case "effective-during":
                    session.SetupSequence(value => value.EffectiveIdentity).Returns(original).Returns(changed);
                    break;
            }

            await AssertResolveStatusAsync(provider, context, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);

            Assert.That(context.UserIdentity, Is.SameAs(original));
        }

        [Test]
        public async Task ResolveRejectsAuthenticatedIdentityChangeDuringResolutionAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            Mock<IUserIdentity> original = CreateIdentity();
            IUserIdentity changed = CreateIdentity().Object;
            Mock<ISession> session = CreateSession(m_certificate, original.Object);
            using OperationContext context = CreateContext(session);
            original.As<IIdentityClaims>().SetupGet(value => value.Subject)
                .Callback(() => session.SetupGet(value => value.Identity).Returns(changed))
                .Returns(kSubject);

            await AssertResolveStatusAsync(provider, context, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);

            Assert.That(session.Object.Identity, Is.SameAs(changed));
            Assert.That(context.UserIdentity, Is.SameAs(original.Object));
            Assert.That(session.Object.EffectiveIdentity, Is.SameAs(original.Object));
        }

        [TestCase("inactive")]
        [TestCase("closing")]
        [TestCase("expired")]
        [TestCase("channel")]
        public async Task ResolveRechecksSessionStateBeforeReturningAsync(string state)
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            Mock<ISession> session = CreateSession(m_certificate);
            using OperationContext context = CreateContext(session);
            StatusCode expected = StatusCodes.BadSessionClosed;
            switch (state)
            {
                case "inactive":
                    session.SetupSequence(value => value.Activated).Returns(true).Returns(false);
                    expected = StatusCodes.BadSessionNotActivated;
                    break;
                case "closing":
                    session.SetupSequence(value => value.IsClosing).Returns(false).Returns(true);
                    break;
                case "expired":
                    session.SetupSequence(value => value.HasExpired).Returns(false).Returns(true);
                    break;
                case "channel":
                    session.SetupSequence(value => value.IsSecureChannelValid(kChannelId)).Returns(true).Returns(false);
                    expected = StatusCodes.BadSecureChannelIdInvalid;
                    break;
            }

            await AssertResolveStatusAsync(provider, context, expected).ConfigureAwait(false);
        }

        [TestCase("URN:rse04:é", kUnicodeSubject)]
        [TestCase(kUnicodeIssuer, "zoë/使用者")]
        [TestCase("urn:rse04:é ", kUnicodeSubject)]
        [TestCase(kUnicodeIssuer, "Zoë/使用者 ")]
        [TestCase("urn:rse04:e\u0301", kUnicodeSubject)]
        [TestCase(kUnicodeIssuer, "Zoe\u0308/使用者")]
        public async Task ResolveUsesExactOrdinalClaimsAsync(string issuer, string subject)
        {
            var provider = new ConfiguredRecoverableExecutionOwnerProvider(
                s_authorityId, DefaultApplications(), [new(s_userId, kUnicodeIssuer, kUnicodeSubject)]);
            using OperationContext context = CreateContext(
                CreateSession(m_certificate, CreateIdentity(issuer, subject).Object));

            await AssertResolveStatusAsync(provider, context, StatusCodes.BadUserAccessDenied).ConfigureAwait(false);
        }

        [Test]
        public async Task ResolveRejectsNullContextAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();

            ArgumentNullException exception = (await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await provider.ResolveAsync(null!).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.ParamName, Is.EqualTo("context"));
        }

        [Test]
        public async Task ResolveHonorsCancellationBeforeSessionAccessAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            Mock<ISession> session = CreateSession(m_certificate);
            using OperationContext context = CreateContext(session);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            session.Invocations.Clear();

            OperationCanceledException exception = (await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await provider.ResolveAsync(context, cancellation.Token).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
            session.VerifyNoOtherCalls();
        }

        [Test]
        public async Task PersistedOwnerSurvivesProviderRecreationWithoutSessionAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider originalProvider = CreateProvider();
            Mock<ISession> session = CreateSession(m_certificate);
            using OperationContext context = CreateContext(session);
            RecoverableExecutionOwner captured = await originalProvider.ResolveAsync(context).ConfigureAwait(false);
            var persisted = new RecoverableExecutionOwner(
                captured.Id, new ByteString(captured.ContinuityContext.ToArray()));
            ConfiguredRecoverableExecutionOwnerProvider recreated = CreateProvider();
            session.SetupGet(value => value.Activated).Returns(false);
            session.SetupGet(value => value.IsClosing).Returns(true);
            ClientCertificateProvenance.SetValidated(session.Object, false);
            // Moq verifies returned mocks recursively; discard the earlier Resolve calls as well.
            Mock.Get(session.Object.Identity).Invocations.Clear();
            Mock.Get(session.Object.IdentityToken).Invocations.Clear();
            session.Invocations.Clear();

            await recreated.ValidatePersistedOwnerAsync(persisted).ConfigureAwait(false);

            Assert.That(persisted.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(persisted.ContinuityContext.Slice(1, 32), Is.EqualTo(m_certificateDigest));
            session.VerifyNoOtherCalls();
        }

        [Test]
        public async Task PersistedOwnerRejectsDifferentAuthorityAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider original = CreateProvider();
            using OperationContext context = CreateContext(CreateSession(m_certificate));
            RecoverableExecutionOwner owner = await original.ResolveAsync(context).ConfigureAwait(false);
            var changedAuthority = new ConfiguredRecoverableExecutionOwnerProvider(
                s_otherAuthorityId, DefaultApplications(), DefaultUsers());

            await AssertPersistedUnavailableAsync(changedAuthority, owner).ConfigureAwait(false);
        }

        [TestCase("authority")]
        [TestCase("application")]
        [TestCase("user")]
        public async Task PersistedOwnerRejectsWrongOwnerIdsAsync(string wrongPart)
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            using OperationContext context = CreateContext(CreateSession(m_certificate));
            RecoverableExecutionOwner owner = await provider.ResolveAsync(context).ConfigureAwait(false);
            var wrongId = new RecoverableExecutionOwnerId(
                wrongPart == "authority" ? s_otherAuthorityId : s_authorityId,
                wrongPart == "application" ? s_otherApplicationId : s_applicationId,
                wrongPart == "user" ? s_otherUserId : s_userId);
            var inconsistent = new RecoverableExecutionOwner(wrongId, owner.ContinuityContext);

            await AssertPersistedUnavailableAsync(provider, inconsistent).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PersistedOwnerRequiresOriginalCertificateAliasAsync(bool remapped)
        {
            ConfiguredRecoverableExecutionOwnerProvider originalProvider = CreateProvider();
            using OperationContext originalContext = CreateContext(CreateSession(m_certificate));
            RecoverableExecutionOwner original =
                await originalProvider.ResolveAsync(originalContext).ConfigureAwait(false);
            var renewalMapping = new RecoverableExecutionApplicationMapping(s_applicationId, m_replacementDigest);
            ArrayOf<RecoverableExecutionApplicationMapping> currentMappings = remapped
                ? [renewalMapping, new(s_otherApplicationId, m_certificateDigest)]
                : [renewalMapping];
            var current = new ConfiguredRecoverableExecutionOwnerProvider(
                s_authorityId, currentMappings, DefaultUsers());
            using OperationContext renewalContext = CreateContext(CreateSession(m_replacementCertificate));

            RecoverableExecutionOwner renewed = await current.ResolveAsync(renewalContext).ConfigureAwait(false);

            Assert.That(original.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(renewed.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(renewed.ContinuityContext, Is.Not.EqualTo(original.ContinuityContext));
            await current.ValidatePersistedOwnerAsync(renewed).ConfigureAwait(false);
            await AssertPersistedUnavailableAsync(current, original).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PersistedOwnerRequiresOriginalUserAliasAsync(bool remapped)
        {
            ConfiguredRecoverableExecutionOwnerProvider originalProvider = CreateProvider();
            using OperationContext originalContext = CreateContext(CreateSession(m_certificate));
            RecoverableExecutionOwner original =
                await originalProvider.ResolveAsync(originalContext).ConfigureAwait(false);
            var newAlias = new RecoverableExecutionUserMapping(s_userId, kIssuer, kOtherSubject);
            ArrayOf<RecoverableExecutionUserMapping> currentMappings = remapped
                ? [newAlias, new(s_otherUserId, kIssuer, kSubject)]
                : [newAlias];
            var current = new ConfiguredRecoverableExecutionOwnerProvider(
                s_authorityId, DefaultApplications(), currentMappings);
            using OperationContext aliasContext = CreateContext(
                CreateSession(m_certificate, CreateIdentity(subject: kOtherSubject).Object));

            RecoverableExecutionOwner alias = await current.ResolveAsync(aliasContext).ConfigureAwait(false);

            Assert.That(original.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(alias.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(alias.ContinuityContext, Is.Not.EqualTo(original.ContinuityContext));
            await current.ValidatePersistedOwnerAsync(alias).ConfigureAwait(false);
            await AssertPersistedUnavailableAsync(current, original).ConfigureAwait(false);
        }

        [TestCase("version-only")]
        [TestCase("fixed-header")]
        [TestCase("issuer")]
        [TestCase("subject-length")]
        [TestCase("subject")]
        public async Task PersistedOwnerRejectsTruncatedEvidenceAsync(string boundary)
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            using OperationContext context = CreateContext(CreateSession(m_certificate));
            RecoverableExecutionOwner owner = await provider.ResolveAsync(context).ConfigureAwait(false);
            byte[] bytes = owner.ContinuityContext.ToArray();
            int subjectOffset = 37 + Encoding.UTF8.GetByteCount(kIssuer);
            int truncatedLength = boundary switch
            {
                "version-only" => 1,
                "fixed-header" => 40,
                "issuer" => subjectOffset - 1,
                "subject-length" => subjectOffset + 3,
                "subject" => bytes.Length - 1,
                _ => throw new ArgumentOutOfRangeException(nameof(boundary))
            };
            var truncated = new RecoverableExecutionOwner(
                owner.Id, new ByteString(bytes.AsMemory(0, truncatedLength)));

            await AssertPersistedUnavailableAsync(provider, truncated).ConfigureAwait(false);
        }

        [TestCase("version-zero")]
        [TestCase("version-future")]
        [TestCase("unmapped-certificate")]
        [TestCase("issuer-zero")]
        [TestCase("issuer-negative")]
        [TestCase("issuer-oversize")]
        [TestCase("issuer-overflow")]
        [TestCase("subject-zero")]
        [TestCase("subject-negative")]
        [TestCase("subject-short")]
        [TestCase("subject-long")]
        [TestCase("subject-overflow")]
        [TestCase("trailing-byte")]
        public async Task PersistedOwnerRejectsMalformedEvidenceAsync(string fault)
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            using OperationContext context = CreateContext(CreateSession(m_certificate));
            RecoverableExecutionOwner owner = await provider.ResolveAsync(context).ConfigureAwait(false);
            byte[] bytes = owner.ContinuityContext.ToArray();
            int subjectOffset = 37 + Encoding.UTF8.GetByteCount(kIssuer);
            switch (fault)
            {
                case "version-zero":
                    bytes[0] = 0;
                    break;
                case "version-future":
                    bytes[0] = 2;
                    break;
                case "unmapped-certificate":
                    bytes[1] ^= 0xff;
                    break;
                case "issuer-zero":
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(33, 4), 0);
                    break;
                case "issuer-negative":
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(33, 4), -1);
                    break;
                case "issuer-oversize":
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(33, 4), bytes.Length - 40);
                    break;
                case "issuer-overflow":
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(33, 4), int.MaxValue);
                    break;
                case "subject-zero":
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(subjectOffset, 4), 0);
                    break;
                case "subject-negative":
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(subjectOffset, 4), -1);
                    break;
                case "subject-short":
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(subjectOffset, 4), kSubject.Length - 1);
                    break;
                case "subject-long":
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(subjectOffset, 4), kSubject.Length + 1);
                    break;
                case "subject-overflow":
                    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(subjectOffset, 4), int.MaxValue);
                    break;
                case "trailing-byte":
                    Array.Resize(ref bytes, bytes.Length + 1);
                    break;
            }
            var malformed = new RecoverableExecutionOwner(owner.Id, new ByteString(bytes));

            await AssertPersistedUnavailableAsync(provider, malformed).ConfigureAwait(false);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task PersistedOwnerRejectsInvalidUtf8EvenWhenReplacementTextIsMappedAsync(bool corruptIssuer)
        {
            // If decoding ever becomes permissive, U+FFFD must not turn malformed evidence into a valid alias.
            var provider = new ConfiguredRecoverableExecutionOwnerProvider(
                s_authorityId, DefaultApplications(),
                [
                    new(s_userId, kIssuer, kSubject),
                    new(s_userId, "\ufffdrn:rse04:issuer", kSubject),
                    new(s_userId, kIssuer, "\ufffdperator")
                ]);
            using OperationContext context = CreateContext(CreateSession(m_certificate));
            RecoverableExecutionOwner owner = await provider.ResolveAsync(context).ConfigureAwait(false);
            byte[] bytes = owner.ContinuityContext.ToArray();
            int subjectOffset = 37 + Encoding.UTF8.GetByteCount(kIssuer);
            bytes[corruptIssuer ? 37 : subjectOffset + 4] = 0xff;
            var malformed = new RecoverableExecutionOwner(owner.Id, new ByteString(bytes));

            await AssertPersistedUnavailableAsync(provider, malformed).ConfigureAwait(false);
        }

        [TestCase("i", "s")]
        [TestCase(kUnicodeIssuer, kUnicodeSubject)]
        [TestCase(" urn:測定:issuer ", " 受信者/👩‍🔬 ")]
        public async Task ClaimsRoundTripWithExactUtf8ByteLengthsAsync(string issuer, string subject)
        {
            var mapping = new RecoverableExecutionUserMapping(s_userId, issuer, subject);
            var provider = new ConfiguredRecoverableExecutionOwnerProvider(
                s_authorityId, DefaultApplications(), [mapping]);
            using OperationContext context = CreateContext(
                CreateSession(m_certificate, CreateIdentity(issuer, subject).Object));

            RecoverableExecutionOwner owner = await provider.ResolveAsync(context).ConfigureAwait(false);

            byte[] bytes = owner.ContinuityContext.ToArray();
            int issuerLength = Encoding.UTF8.GetByteCount(issuer);
            int subjectLength = Encoding.UTF8.GetByteCount(subject);
            int subjectOffset = 37 + issuerLength;
            Assert.That(owner.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(mapping.UserId, Is.EqualTo(s_userId));
            Assert.That(mapping.Issuer, Is.EqualTo(issuer));
            Assert.That(mapping.Subject, Is.EqualTo(subject));
            Assert.That(bytes, Has.Length.EqualTo(41 + issuerLength + subjectLength));
            Assert.That(bytes[0], Is.EqualTo(1));
            Assert.That(owner.ContinuityContext.Slice(1, 32), Is.EqualTo(m_certificateDigest));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(33, 4)), Is.EqualTo(issuerLength));
            Assert.That(Encoding.UTF8.GetString(bytes, 37, issuerLength), Is.EqualTo(issuer));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(subjectOffset, 4)),
                Is.EqualTo(subjectLength));
            Assert.That(Encoding.UTF8.GetString(bytes, subjectOffset + 4, subjectLength), Is.EqualTo(subject));
            var recreated = new ConfiguredRecoverableExecutionOwnerProvider(
                s_authorityId, DefaultApplications(), [new(s_userId, issuer, subject)]);
            await recreated.ValidatePersistedOwnerAsync(
                new RecoverableExecutionOwner(owner.Id, new ByteString(bytes))).ConfigureAwait(false);
        }

        [TestCase("URN:rse04:é", kUnicodeSubject)]
        [TestCase(kUnicodeIssuer, "zoë/使用者")]
        [TestCase("urn:rse04:é ", kUnicodeSubject)]
        [TestCase(kUnicodeIssuer, "Zoë/使用者 ")]
        [TestCase("urn:rse04:e\u0301", kUnicodeSubject)]
        [TestCase(kUnicodeIssuer, "Zoe\u0308/使用者")]
        public async Task PersistedOwnerUsesExactOrdinalClaimsAsync(string issuer, string subject)
        {
            var originalProvider = new ConfiguredRecoverableExecutionOwnerProvider(
                s_authorityId, DefaultApplications(), [new(s_userId, kUnicodeIssuer, kUnicodeSubject)]);
            using OperationContext originalContext = CreateContext(
                CreateSession(m_certificate, CreateIdentity(kUnicodeIssuer, kUnicodeSubject).Object));
            RecoverableExecutionOwner original =
                await originalProvider.ResolveAsync(originalContext).ConfigureAwait(false);
            var changed = new ConfiguredRecoverableExecutionOwnerProvider(
                s_authorityId, DefaultApplications(), [new(s_userId, issuer, subject)]);
            using OperationContext changedContext = CreateContext(
                CreateSession(m_certificate, CreateIdentity(issuer, subject).Object));

            RecoverableExecutionOwner changedOwner = await changed.ResolveAsync(changedContext).ConfigureAwait(false);

            Assert.That(changedOwner.Id, Is.EqualTo(ExpectedOwnerId));
            await changed.ValidatePersistedOwnerAsync(changedOwner).ConfigureAwait(false);
            await AssertPersistedUnavailableAsync(changed, original).ConfigureAwait(false);
        }

        [Test]
        public async Task PersistedOwnerRejectsNullOwnerAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();

            ArgumentNullException exception = (await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await provider.ValidatePersistedOwnerAsync(null!).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.ParamName, Is.EqualTo("owner"));
        }

        [Test]
        public async Task PersistedOwnerHonorsCancellationBeforeEvidenceValidationAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            var invalidEvidence = new RecoverableExecutionOwner(ExpectedOwnerId, [0xff]);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            OperationCanceledException exception = (await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await provider.ValidatePersistedOwnerAsync(invalidEvidence, cancellation.Token).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
        }

        [Test]
        public async Task UncancelledTokensAllowBothOperationsAsync()
        {
            ConfiguredRecoverableExecutionOwnerProvider provider = CreateProvider();
            using OperationContext context = CreateContext(CreateSession(m_certificate));
            using var cancellation = new CancellationTokenSource();

            RecoverableExecutionOwner owner = await provider.ResolveAsync(context, cancellation.Token)
                .ConfigureAwait(false);
            await provider.ValidatePersistedOwnerAsync(owner, cancellation.Token).ConfigureAwait(false);

            Assert.That(owner.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(owner.ContinuityContext.Slice(1, 32), Is.EqualTo(m_certificateDigest));
        }

        [Test]
        public async Task WithRecoverableExecutionOwnersRegistersOnlyMappingProviderAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var policy = new Mock<IAsyncServiceAuthorizationPolicy>(MockBehavior.Strict);
            services.AddSingleton(policy.Object);
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "OwnerMappingTests";
                options.ApplicationUri = "urn:rse04:owner-test-server";
                options.ProductUri = "urn:rse04:owner-test-product";
                options.AsyncServices.Enabled = false;
                options.AsyncServices.MaxExecutionAllowance = 90_000;
            });
            int registrationsBefore = services.Count;

            IOpcUaServerBuilder returned = builder.WithRecoverableExecutionOwners(
                s_authorityId, DefaultApplications(), DefaultUsers());

            Assert.That(returned, Is.SameAs(builder));
            Assert.That(services, Has.Count.EqualTo(registrationsBefore + 1));
            Assert.That(services, Has.Exactly(1).Matches<ServiceDescriptor>(descriptor =>
                descriptor is not null &&
                descriptor.ServiceType == typeof(IRecoverableExecutionOwnerProvider) &&
                descriptor.Lifetime == ServiceLifetime.Singleton));
            await using ServiceProvider container = services.BuildServiceProvider();
            IRecoverableExecutionOwnerProvider provider =
                container.GetRequiredService<IRecoverableExecutionOwnerProvider>();
            Assert.That(container.GetRequiredService<IRecoverableExecutionOwnerProvider>(), Is.SameAs(provider));
            AsyncServiceExecutionOptions options = container.GetRequiredService<IOptions<OpcUaServerOptions>>()
                .Value.AsyncServices;
            Assert.That(options.Enabled, Is.False);
            Assert.That(options.MaxExecutionAllowance, Is.EqualTo(90_000));
            Assert.That(container.GetService<IAsyncServiceExecutionProviderFactory>(), Is.Null);
            Assert.That(container.GetService<IAsyncServiceExecutionProvider>(), Is.Null);
            Assert.That(container.GetRequiredService<IAsyncServiceAuthorizationPolicy>(), Is.SameAs(policy.Object));

            Mock<IUserIdentity> authenticated = CreateIdentity();
            Mock<IUserIdentity> effective = CreateIdentity("urn:effective-only", "not-an-owner-mapping");
            using OperationContext context = CreateContext(
                CreateSession(m_certificate, authenticated.Object, effective.Object));

            RecoverableExecutionOwner owner = await provider.ResolveAsync(context).ConfigureAwait(false);
            await provider.ValidatePersistedOwnerAsync(owner).ConfigureAwait(false);

            Assert.That(owner.Id, Is.EqualTo(ExpectedOwnerId));
            Assert.That(owner.ContinuityContext.Slice(1, 32), Is.EqualTo(m_certificateDigest));
            Assert.That(context.UserIdentity, Is.SameAs(effective.Object));
            Assert.That(effective.Object.GrantedRoleIds, Is.EqualTo(ArrayOf<NodeId>.Empty));
            authenticated.VerifyGet(value => value.TokenHandler, Times.Never);
            effective.VerifyGet(value => value.TokenHandler, Times.Never);
            policy.VerifyNoOtherCalls();
        }

        [Test]
        public void WithRecoverableExecutionOwnersRejectsNullBuilder()
        {
            Assert.That(
                () => OpcUaServerBuilderExtensions.WithRecoverableExecutionOwners(
                    null!, s_authorityId, DefaultApplications(), DefaultUsers()),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("builder"));
        }

        [TestCase("authorityId")]
        [TestCase("applications")]
        [TestCase("users")]
        public void WithRecoverableExecutionOwnersRejectsInvalidMappingsWithoutRegistration(string parameterName)
        {
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });
            var originalRegistrations = new ServiceDescriptor[services.Count];
            services.CopyTo(originalRegistrations, 0);
            Uuid authority = parameterName == "authorityId" ? Uuid.Empty : s_authorityId;
            ArrayOf<RecoverableExecutionApplicationMapping> applications =
                parameterName == "applications" ? default : DefaultApplications();
            ArrayOf<RecoverableExecutionUserMapping> users =
                parameterName == "users" ? default : DefaultUsers();

            Assert.That(
                () => builder.WithRecoverableExecutionOwners(authority, applications, users),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo(parameterName));

            Assert.That(services, Is.EqualTo(originalRegistrations));
        }

        private ConfiguredRecoverableExecutionOwnerProvider CreateProvider()
        {
            return new ConfiguredRecoverableExecutionOwnerProvider(
                s_authorityId, DefaultApplications(), DefaultUsers());
        }

        private ArrayOf<RecoverableExecutionApplicationMapping> DefaultApplications()
        {
            return [new RecoverableExecutionApplicationMapping(s_applicationId, m_certificateDigest)];
        }

        private static ArrayOf<RecoverableExecutionUserMapping> DefaultUsers()
        {
            return [new RecoverableExecutionUserMapping(s_userId, kIssuer, kSubject)];
        }

        private static Mock<IUserIdentity> CreateIdentity(
            string? issuer = kIssuer,
            string? subject = kSubject,
            string displayName = kDisplayName)
        {
            var identity = new Mock<IUserIdentity>(MockBehavior.Strict);
            Mock<IIdentityClaims> claims = identity.As<IIdentityClaims>();
            claims.SetupGet(value => value.Issuer).Returns(issuer);
            claims.SetupGet(value => value.Subject).Returns(subject);
            identity.SetupGet(value => value.DisplayName).Returns(displayName);
            identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
            identity.SetupGet(value => value.GrantedRoleIds).Returns([]);
            return identity;
        }

        private static Mock<ISession> CreateSession(
            Certificate certificate,
            IUserIdentity? identity = null,
            IUserIdentity? effectiveIdentity = null,
            string channelId = kChannelId,
            UserTokenType tokenType = UserTokenType.UserName)
        {
            identity ??= CreateIdentity().Object;
            var token = new Mock<IUserIdentityTokenHandler>(MockBehavior.Strict);
            token.SetupGet(value => value.TokenType).Returns(tokenType);
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Activated).Returns(true);
            session.SetupGet(value => value.IsClosing).Returns(false);
            session.SetupGet(value => value.HasExpired).Returns(false);
            session.SetupGet(value => value.IsIdentityStale).Returns(false);
            session.SetupGet(value => value.Identity).Returns(identity);
            session.SetupGet(value => value.IdentityToken).Returns(token.Object);
            session.SetupGet(value => value.EffectiveIdentity).Returns(effectiveIdentity ?? identity);
            session.SetupGet(value => value.ClientCertificate).Returns(certificate);
            session.SetupGet(value => value.EndpointDescription).Returns(SignedEndpoint());
            session.SetupGet(value => value.PreferredLocales).Returns([]);
            session.SetupGet(value => value.Id).Returns(new NodeId("original-session", 2));
            session.SetupGet(value => value.SecureChannelId).Returns(channelId);
            session.SetupGet(value => value.ClientApplicationUri).Returns(kClaimedApplicationUri);
            session.Setup(value => value.IsSecureChannelValid(It.IsAny<string>()))
                .Returns((string candidate) => candidate == channelId);
            return session;
        }

        private static EndpointDescription SignedEndpoint()
        {
            return new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost/owner-tests",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
        }

        private static OperationContext CreateContext(
            Mock<ISession> session,
            SecureChannelContext? channel = null)
        {
            channel ??= new SecureChannelContext(
                session.Object.SecureChannelId, session.Object.EndpointDescription, RequestEncoding.Binary);
            return new OperationContext(
                new RequestHeader(), channel, RequestType.Read, RequestLifetime.None, session.Object);
        }

        private static ByteString HashCertificate(Certificate certificate)
        {
#if NET8_0_OR_GREATER
            return new ByteString(SHA256.HashData(certificate.RawData));
#else
            using var sha256 = SHA256.Create();
            return new ByteString(sha256.ComputeHash(certificate.RawData));
#endif
        }

        private static async Task AssertResolveStatusAsync(
            ConfiguredRecoverableExecutionOwnerProvider provider,
            OperationContext context,
            StatusCode statusCode)
        {
            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.ResolveAsync(context).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(statusCode));
        }

        private static async Task AssertPersistedUnavailableAsync(
            ConfiguredRecoverableExecutionOwnerProvider provider,
            RecoverableExecutionOwner owner)
        {
            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await provider.ValidatePersistedOwnerAsync(owner).ConfigureAwait(false)).ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
        }

        private static RecoverableExecutionOwnerId ExpectedOwnerId =>
            new(s_authorityId, s_applicationId, s_userId);

        private static readonly Uuid s_authorityId = new("10000000-0000-0000-0000-000000000001");
        private static readonly Uuid s_otherAuthorityId = new("10000000-0000-0000-0000-000000000002");
        private static readonly Uuid s_applicationId = new("20000000-0000-0000-0000-000000000001");
        private static readonly Uuid s_otherApplicationId = new("20000000-0000-0000-0000-000000000002");
        private static readonly Uuid s_userId = new("30000000-0000-0000-0000-000000000001");
        private static readonly Uuid s_otherUserId = new("30000000-0000-0000-0000-000000000002");
        private const string kIssuer = "urn:rse04:issuer";
        private const string kSubject = "operator";
        private const string kOtherSubject = "reviewer";
        private const string kUnicodeIssuer = "urn:rse04:é";
        private const string kUnicodeSubject = "Zoë/使用者";
        private const string kDisplayName = "Shared display name";
        private const string kClaimedApplicationUri = "urn:rse04:claimed-client";
        private const string kChannelId = "owner-channel";
        private Certificate m_certificate = null!;
        private Certificate m_replacementCertificate = null!;
        private ByteString m_certificateDigest;
        private ByteString m_replacementDigest;
    }
}
