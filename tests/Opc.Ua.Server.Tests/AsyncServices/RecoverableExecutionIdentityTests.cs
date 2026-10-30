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
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Exercises in-memory capture, not authentication. All credentials and channel bytes are synthetic.
    /// </summary>
    [TestFixture]
    [Category("AsyncServices")]
    [Parallelizable(ParallelScope.All)]
    public sealed class RecoverableExecutionIdentityTests
    {
        /// <summary>
        /// Real non-issued identities may omit the legacy issued-token name without becoming ineligible for capture.
        /// </summary>
        [TestCase(UserTokenType.Anonymous)]
        [TestCase(UserTokenType.UserName)]
        public void NonIssuedIdentityCaptureDoesNotRequireAnIssuedTokenName(UserTokenType tokenType)
        {
            IUserIdentityTokenHandler handler = tokenType == UserTokenType.Anonymous
                ? new AnonymousIdentityTokenHandler()
                : new UserNameIdentityTokenHandler("synthetic-operator", new byte[] { 11, 12, 13 });
            var effective = new UserIdentity(handler);
            var authenticated = new SyntheticOwnerIdentity
            {
                Issuer = "urn:synthetic:issuer",
                Subject = "authenticated-subject"
            };
            Assert.That(effective.IssuedTokenType, Is.Null,
                "The control must use the actual non-issued UserIdentity representation.");

            var snapshot = new RecoverableExecutionIdentity(effective, authenticated, EncodingContext());

            Assert.That(snapshot.IssuedTokenType, Is.EqualTo(XmlQualifiedName.Empty));
            Assert.That(snapshot.TokenType, Is.EqualTo(tokenType));
            Assert.That(snapshot.TokenHandler, Is.Not.SameAs(handler));
            Assert.That(snapshot.TokenHandler.Token, Is.Not.SameAs(handler.Token));
            Assert.That(snapshot.Issuer, Is.EqualTo("urn:synthetic:issuer"));
            Assert.That(snapshot.Subject, Is.EqualTo("authenticated-subject"));
        }

        [Test]
        public void SnapshotCopiesEffectiveRolesAuthenticatedClaimsAndToken()
        {
            var handler = new UserNameIdentityTokenHandler("synthetic-operator", [11, 12, 13]);
            handler.Token.PolicyId = "original-policy";
            var effective = new SyntheticOwnerIdentity
            {
                DisplayName = "effective-display",
                PolicyId = "original-policy",
                TokenHandler = handler,
                EffectiveRoles = [ObjectIds.WellKnownRole_Operator],
                SupportsSignatures = true,
                IssuedTokenType = new XmlQualifiedName("original-type", "urn:synthetic:profile"),
                Issuer = "not-the-authenticated-issuer",
                Subject = "not-the-authenticated-subject"
            };
            var authenticated = new SyntheticOwnerIdentity
            {
                Issuer = "urn:synthetic:issuer",
                Subject = "authenticated-subject",
                ClaimGroups = ["maintenance"],
                ClaimRoles = ["operator"]
            };
            var audiences = new List<string> { "plant-a", "plant-b" };
            authenticated.RawClaims["aud"] = audiences;
            authenticated.RawClaims["verified"] = true;
            var snapshot = new RecoverableExecutionIdentity(effective, authenticated, EncodingContext());

            effective.EffectiveRoles[0] = ObjectIds.WellKnownRole_Anonymous;
            effective.DisplayName = "changed";
            effective.PolicyId = "changed";
            effective.SupportsSignatures = false;
            effective.IssuedTokenType = XmlQualifiedName.Empty;
            authenticated.Issuer = "changed";
            authenticated.Subject = "changed";
            authenticated.ClaimGroups[0] = "changed";
            authenticated.ClaimRoles.Clear();
            authenticated.RawClaims["verified"] = false;
            audiences[0] = "changed";
            handler.Token.PolicyId = "changed";
            handler.DecryptedPassword![0] = 99;
            ((UserNameIdentityToken)handler.Token).UserName = "changed";

            var claims = (IIdentityClaims)snapshot;
            Assert.That(snapshot.DisplayName, Is.EqualTo("effective-display"));
            Assert.That(snapshot.PolicyId, Is.EqualTo("original-policy"));
            Assert.That(snapshot.TokenType, Is.EqualTo(UserTokenType.UserName));
            Assert.That(snapshot.SupportsSignatures, Is.True);
            Assert.That(snapshot.IssuedTokenType.Name, Is.EqualTo("original-type"));
            Assert.That(snapshot.IssuedTokenType.Namespace, Is.EqualTo("urn:synthetic:profile"));
            Assert.That(snapshot.GrantedRoleIds, Is.EqualTo([ObjectIds.WellKnownRole_Operator]));
            Assert.That(claims.Issuer, Is.EqualTo("urn:synthetic:issuer"));
            Assert.That(claims.Subject, Is.EqualTo("authenticated-subject"));
            Assert.That(claims.Groups, Is.EqualTo(s_groups));
            Assert.That(claims.Roles, Is.EqualTo(s_roles));
            Assert.That(claims.Claims["aud"], Is.EqualTo(s_audiences));
            Assert.That(claims.Claims["verified"], Is.True);
            Assert.That(snapshot.TokenHandler, Is.Not.SameAs(handler));
            Assert.That(snapshot.TokenHandler.Token, Is.Not.SameAs(handler.Token));
            var copied = (UserNameIdentityTokenHandler)snapshot.TokenHandler;
            Assert.That(copied.UserName, Is.EqualTo("synthetic-operator"));
            Assert.That(copied.Token.PolicyId, Is.EqualTo("original-policy"));
            Assert.That(copied.DecryptedPassword, Is.EqualTo(new byte[] { 11, 12, 13 }));
            Assert.That(() => ((IDictionary<string, object?>)claims.Claims).Add("injected", 1),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(() => ((IList<string>)claims.Groups)[0] = "injected",
                Throws.TypeOf<NotSupportedException>());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SnapshotOwnsSyntheticTokenByteStrings(bool issued)
        {
            byte[] tokenBytes = [0x11, 0x22, 0x33];
            IUserIdentityTokenHandler handler = issued
                ? new IssuedIdentityTokenHandler(new IssuedIdentityToken
                {
                    PolicyId = "synthetic-token-policy",
                    TokenData = new ByteString(tokenBytes)
                })
                : new UserNameIdentityTokenHandler(new UserNameIdentityToken
                {
                    PolicyId = "synthetic-token-policy",
                    UserName = "synthetic-operator",
                    Password = new ByteString(tokenBytes)
                });
            var source = new SyntheticOwnerIdentity { TokenHandler = handler };
            var snapshot = new RecoverableExecutionIdentity(source, source, EncodingContext());

            Array.Clear(tokenBytes, 0, tokenBytes.Length);
            ByteString originalBytes = handler.Token is IssuedIdentityToken originalIssued
                ? originalIssued.TokenData : ((UserNameIdentityToken)handler.Token).Password;
            ByteString copiedBytes = snapshot.TokenHandler.Token is IssuedIdentityToken copiedIssued
                ? copiedIssued.TokenData : ((UserNameIdentityToken)snapshot.TokenHandler.Token).Password;

            Assert.That(originalBytes, Is.EqualTo(ByteString.From([0, 0, 0])),
                "The source control must actually expose the externally cleared backing array.");
            Assert.That(copiedBytes, Is.EqualTo(ByteString.From([0x11, 0x22, 0x33])));
            Assert.That(snapshot.TokenHandler.Token.PolicyId, Is.EqualTo("synthetic-token-policy"));
            Assert.That(snapshot.TokenHandler.Token, Is.Not.SameAs(handler.Token));
            Assert.That(snapshot.TokenType, Is.EqualTo(issued ? UserTokenType.IssuedToken : UserTokenType.UserName));
        }

        [Test]
        public void SnapshotOwnsOpaqueRoleNodeIdBytes()
        {
            byte[] roleBytes = [1, 2, 3];
            var source = new SyntheticOwnerIdentity
            {
                EffectiveRoles = [new NodeId(new ByteString(roleBytes), 2)]
            };
            var snapshot = new RecoverableExecutionIdentity(source, source, EncodingContext());

            Array.Clear(roleBytes, 0, roleBytes.Length);

            Assert.That(source.GrantedRoleIds[0].TryGetValue(out ByteString originalBytes), Is.True);
            Assert.That(originalBytes, Is.EqualTo(ByteString.From([0, 0, 0])),
                "The source role must actually share the cleared backing array.");
            Assert.That(snapshot.GrantedRoleIds, Has.Count.EqualTo(1));
            Assert.That(snapshot.GrantedRoleIds[0], Is.EqualTo(new NodeId(ByteString.From([1, 2, 3]), 2)));
            Assert.That(snapshot.GrantedRoleIds[0].TryGetValue(out ByteString copiedBytes), Is.True);
            Assert.That(copiedBytes, Is.EqualTo(ByteString.From([1, 2, 3])));
            Assert.That(snapshot.TokenType, Is.EqualTo(UserTokenType.Anonymous));
        }

        [Test]
        public void SnapshotAcceptsContractScalarAndStringListClaims()
        {
            var source = new SyntheticOwnerIdentity();
            object?[] values =
            [
                null, "text", true, (byte)1, (sbyte)-2, (short)-3, (ushort)4,
                -5, 6u, -7L, 8UL, 1.25f, 2.5d, 3.75m, new List<string> { "a", "b" }
            ];
            for (int i = 0; i < values.Length; i++)
            {
                source.RawClaims.Add("claim-" + i, values[i]);
            }
            source.RawClaims.Add("empty-list", Array.Empty<string>());

            var snapshot = new RecoverableExecutionIdentity(source, source, EncodingContext());
            var claims = (IIdentityClaims)snapshot;
            Assert.That(claims.Claims, Has.Count.EqualTo(16));
            for (int i = 0; i < values.Length - 1; i++)
            {
                Assert.That(claims.Claims["claim-" + i], Is.EqualTo(values[i]));
                if (values[i] != null)
                {
                    Assert.That(claims.Claims["claim-" + i], Is.TypeOf(values[i]!.GetType()));
                }
            }
            ((List<string>)values[14]!)[0] = "changed";
            Assert.That(claims.Claims["claim-14"], Is.EqualTo(s_stringList));
            Assert.That(claims.Claims["empty-list"], Is.Empty);
            Assert.That(snapshot.TokenType, Is.EqualTo(UserTokenType.Anonymous));
        }

        [TestCase("bytes")]
        [TestCase("dictionary")]
        [TestCase("integer-list")]
        [TestCase("object")]
        public void SnapshotRejectsUnsupportedClaimShapes(string shape)
        {
            var source = new SyntheticOwnerIdentity();
            source.RawClaims["unsupported"] = shape switch
            {
                "bytes" => new byte[] { 1 },
                "dictionary" => new Dictionary<string, object> { ["nested"] = 1 },
                "integer-list" => new List<int> { 1 },
                _ => new object()
            };

            AssertStatus(() => _ = new RecoverableExecutionIdentity(source, source, EncodingContext()),
                StatusCodes.BadServiceUnsupported);
            Assert.That(source.RawClaims, Has.Count.EqualTo(1), "Capture must not mutate its source.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CopyingHandlerMustNotRetainTheSourceHandlerOrToken(bool sameHandler)
        {
            var token = new AnonymousIdentityToken { PolicyId = "synthetic" };
            var sourceHandler = new Mock<IUserIdentityTokenHandler>();
            var copy = new Mock<IUserIdentityTokenHandler>();
            sourceHandler.SetupGet(value => value.Token).Returns(token);
            copy.SetupGet(value => value.Token).Returns(token);
            sourceHandler.Setup(value => value.Clone()).Returns(sameHandler ? sourceHandler.Object : copy.Object);
            var source = new SyntheticOwnerIdentity { TokenHandler = sourceHandler.Object };

            AssertStatus(() => _ = new RecoverableExecutionIdentity(source, source, EncodingContext()),
                StatusCodes.BadServiceUnsupported);
            sourceHandler.Verify(value => value.Clone(), Times.Once);
            Assert.That(token.PolicyId, Is.EqualTo("synthetic"));
        }

        [Test]
        public void OwnedContextCopiesLocalesChannelAndPreservesOriginalHeader()
        {
            byte[] clientProof = [1, 2, 3];
            byte[] serverProof = [4, 5, 6];
            byte[] thumbprint = [7, 8];
            byte[] endpointProof = [9, 10];
            string[] locales = ["de-DE", "en-US"];
            var identity = new SyntheticOwnerIdentity { Subject = "original-user" };
            var upstream = new SyntheticOwnerIdentity { Subject = "original-upstream" };
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://synthetic.invalid:1/owner",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                ServerCertificate = new ByteString(endpointProof)
            };
            var channel = new SecureChannelContext(
                "issuing-channel", endpoint, RequestEncoding.Json,
                clientProof, serverProof, thumbprint, IPAddress.Loopback)
            {
                HasTransportSecurity = true,
                UpstreamIdentity = upstream
            };
            Mock<ISession> session = Session(identity, locales);
            session.Setup(value => value.ValidateDiagnosticInfo(It.IsAny<RequestHeader>()))
                .Callback<RequestHeader>(header =>
                {
                    Assert.That(
                        header.ReturnDiagnostics & (uint)DiagnosticsMasks.UserPermissionAdditionalInfo, Is.Zero);
                    header.ReturnDiagnostics = (uint)DiagnosticsMasks.SymbolicId;
                });
            using var admitted = new OperationContext(
                new RequestHeader { AuthenticationToken = new NodeId("exchange-token", 2) },
                channel, RequestType.Call, RequestLifetime.None, session.Object);
            var original = new RequestHeader
            {
                AuthenticationToken = new NodeId("original-token", 2),
                RequestHandle = 71,
                TimeoutHint = 1,
                ReturnDiagnostics = (uint)DiagnosticsMasks.All,
                AuditEntryId = "original-audit",
                Timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
            };
            byte[] frozenHeader = BinaryEncoder.EncodeMessage(original, EncodingContext());
            var snapshot = new RecoverableExecutionIdentity(identity, identity, EncodingContext());
            using var owned = new OperationContext(
                admitted, original, RequestType.Read, RequestLifetime.None, independentIdentity: snapshot);

            locales[0] = "changed";
            clientProof[0] = 99;
            serverProof[0] = 99;
            thumbprint[0] = 99;
            endpointProof[0] = 99;
            endpoint.SecurityPolicyUri = SecurityPolicies.None;
            endpoint.SecurityMode = MessageSecurityMode.None;
            identity.Subject = "changed";
            upstream.Subject = "changed";
            session.SetupGet(value => value.Id).Throws(new ObjectDisposedException("synthetic-session"));
            session.SetupGet(value => value.EffectiveIdentity).Throws(new ObjectDisposedException("synthetic-session"));

            Assert.That(owned.Session, Is.Null);
            Assert.That(owned.IsOwnedInvocation, Is.True);
            Assert.That(owned.IsSessionIndependent, Is.True);
            Assert.That(owned.SessionId, Is.EqualTo(new NodeId("issuing-session", 2)));
            Assert.That(owned.AuthenticationToken, Is.EqualTo(new NodeId("exchange-token", 2)));
            Assert.That(owned.ClientHandle, Is.EqualTo(71u));
            Assert.That(owned.OperationDeadline, Is.EqualTo(DateTime.MaxValue));
            Assert.That(owned.DiagnosticsMask, Is.EqualTo(DiagnosticsMasks.SymbolicId));
            Assert.That(owned.PreferredLocales, Is.EqualTo(s_locales));
            Assert.That(((IIdentityClaims)owned.UserIdentity).Subject, Is.EqualTo("original-user"));
            Assert.That(owned.ChannelContext, Is.Not.SameAs(channel));
            Assert.That(owned.ChannelContext!.SecureChannelId, Is.EqualTo("issuing-channel"));
            Assert.That(owned.ChannelContext.MessageEncoding, Is.EqualTo(RequestEncoding.Json));
            Assert.That(owned.ChannelContext.ClientChannelCertificate, Is.EqualTo(ByteString.From([1, 2, 3])));
            Assert.That(owned.ChannelContext.ServerChannelCertificate, Is.EqualTo(ByteString.From([4, 5, 6])));
            Assert.That(owned.ChannelContext.ChannelThumbprint, Is.EqualTo(ByteString.From([7, 8])));
            Assert.That(owned.ChannelContext.EndpointDescription!.ServerCertificate,
                Is.EqualTo(ByteString.From([9, 10])));
            Assert.That(owned.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(owned.ChannelContext.EndpointDescription!.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(owned.ChannelContext.HasTransportSecurity, Is.True);
            Assert.That(owned.ChannelContext.PeerAddress, Is.EqualTo(IPAddress.Loopback));
            Assert.That(((IIdentityClaims)owned.ChannelContext.UpstreamIdentity!).Subject,
                Is.EqualTo("original-upstream"));
            Assert.That(BinaryEncoder.EncodeMessage(original, EncodingContext()), Is.EqualTo(frozenHeader),
                "Effective diagnostics must not rewrite the original header.");
            Assert.That(original.TimeoutHint, Is.EqualTo(1u));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UpstreamPrincipalIsCopiedOrRejectedRatherThanDropped(bool claimsCapable)
        {
            var source = new SyntheticOwnerIdentity();
            var upstream = new SyntheticOwnerIdentity { Subject = "upstream-subject" };
            var channel = new SecureChannelContext("channel", new EndpointDescription(), RequestEncoding.Binary)
            {
                UpstreamIdentity = claimsCapable ? upstream : new UserIdentity()
            };
            var snapshot = new RecoverableExecutionIdentity(source, source, EncodingContext());
            if (!claimsCapable)
            {
                AssertStatus(() => snapshot.CaptureChannel(channel, []), StatusCodes.BadServiceUnsupported);
                Assert.That(channel.UpstreamIdentity, Is.InstanceOf<UserIdentity>());
                return;
            }

            SecureChannelContext copied = snapshot.CaptureChannel(channel, []);
            upstream.Subject = "changed";
            Assert.That(copied.UpstreamIdentity, Is.Not.SameAs(upstream));
            Assert.That(((IIdentityClaims)copied.UpstreamIdentity!).Subject, Is.EqualTo("upstream-subject"));
            Assert.That(copied.SecureChannelId, Is.EqualTo("channel"));
        }

        [Test]
        public void MissingEndpointAndNonClaimsAuthenticatedIdentityFailClosed()
        {
            var source = new SyntheticOwnerIdentity();
            AssertStatus(() => _ = new RecoverableExecutionIdentity(source, new UserIdentity(), EncodingContext()),
                StatusCodes.BadServiceUnsupported);
            var snapshot = new RecoverableExecutionIdentity(source, source, EncodingContext());
            AssertStatus(() => snapshot.CaptureChannel(
                new SecureChannelContext("missing-endpoint", null, RequestEncoding.Binary), []),
                StatusCodes.BadSecurityModeRejected);
            Assert.That(source.RawClaims, Is.Empty);
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(391)]
        [TestCase(392)]
        [TestCase(393)]
        public void EmptyIdentityHasExactCaptureBudgetBoundary(int budget)
        {
            // Six empty principal strings at 64 bytes each; binary anonymous token is exactly 8 bytes.
            var source = new SyntheticOwnerIdentity();
            Assert.That(
                BinaryEncoder.EncodeMessage(source.TokenHandler.Token, EncodingContext()), Has.Length.EqualTo(8));
            if (budget < 392)
            {
                AssertStatus(() => _ = new RecoverableExecutionIdentity(source, source, EncodingContext(budget)),
                    StatusCodes.BadResourceUnavailable);
                return;
            }

            var snapshot = new RecoverableExecutionIdentity(source, source, EncodingContext(budget));
            Assert.That(snapshot.TokenType, Is.EqualTo(UserTokenType.Anonymous));
            Assert.That(snapshot.GrantedRoleIds, Is.Empty);
            Assert.That(((IIdentityClaims)snapshot).Claims, Is.Empty);
            source.DisplayName = budget == 392 ? "x" : "xx";
            AssertStatus(() => _ = new RecoverableExecutionIdentity(source, source, EncodingContext(budget)),
                StatusCodes.BadResourceUnavailable);
        }

        [Test]
        public void ChannelAndLocalesShareThePrincipalCaptureBudget()
        {
            var source = new SyntheticOwnerIdentity();
            var endpoint = new EndpointDescription();
            var channel = new SecureChannelContext(
                "c", endpoint, RequestEncoding.Binary, [1, 2], [3], [4]);
            int exact = 392 +
                BinaryEncoder.EncodeMessage(endpoint, EncodingContext()).Length +
                65 +
                4 +
                64 +
                Encoding.UTF8.GetByteCount("é");
            var snapshot = new RecoverableExecutionIdentity(source, source, EncodingContext(exact));
            SecureChannelContext copied = snapshot.CaptureChannel(channel, ["é"]);
            Assert.That(copied.SecureChannelId, Is.EqualTo("c"));
            Assert.That(copied.ClientChannelCertificate, Is.EqualTo(ByteString.From([1, 2])));

            var tooSmall = new RecoverableExecutionIdentity(source, source, EncodingContext(exact - 1));
            AssertStatus(() => tooSmall.CaptureChannel(channel, ["é"]), StatusCodes.BadResourceUnavailable);
            var oneExtra = new RecoverableExecutionIdentity(source, source, EncodingContext(exact));
            AssertStatus(() => oneExtra.CaptureChannel(channel, ["éx"]), StatusCodes.BadResourceUnavailable);
        }

        [Test]
        public void ClaimsListsAndEffectiveRolesConsumeTheSameCaptureBudget()
        {
            var source = new SyntheticOwnerIdentity
            {
                EffectiveRoles = [new NodeId(9)],
                ClaimGroups = ["g"],
                ClaimRoles = ["r1", "r2"]
            };
            source.RawClaims["active"] = true;
            source.RawClaims["list"] = new List<string> { "a" };
            // 392 base + 67 role + 73 group + 148 roles + 102 scalar claim + 173 list claim.
            var snapshot = new RecoverableExecutionIdentity(source, source, EncodingContext(955));
            Assert.That(snapshot.GrantedRoleIds, Is.EqualTo([new NodeId(9)]));
            Assert.That(((IIdentityClaims)snapshot).Roles, Is.EqualTo(s_budgetRoles));
            Assert.That(((IIdentityClaims)snapshot).Claims["active"], Is.True);
            AssertStatus(() => _ = new RecoverableExecutionIdentity(source, source, EncodingContext(954)),
                StatusCodes.BadResourceUnavailable);
            source.ClaimGroups[0] = "gg";
            AssertStatus(() => _ = new RecoverableExecutionIdentity(source, source, EncodingContext(955)),
                StatusCodes.BadResourceUnavailable);
        }

        [Test]
        public void UpstreamIdentityCannotObtainASecondCaptureBudget()
        {
            var source = new SyntheticOwnerIdentity();
            var upstream = new SyntheticOwnerIdentity();
            var endpoint = new EndpointDescription();
            var channel = new SecureChannelContext("c", endpoint, RequestEncoding.Binary)
            {
                UpstreamIdentity = upstream
            };
            int exact = (2 * 392) + 65 + BinaryEncoder.EncodeMessage(endpoint, EncodingContext()).Length;
            var snapshot = new RecoverableExecutionIdentity(source, source, EncodingContext(exact));
            SecureChannelContext copied = snapshot.CaptureChannel(channel, []);
            Assert.That(copied.UpstreamIdentity!.TokenType, Is.EqualTo(UserTokenType.Anonymous));
            Assert.That(copied.UpstreamIdentity.TokenHandler.Token, Is.Not.SameAs(upstream.TokenHandler.Token));
            var tooSmall = new RecoverableExecutionIdentity(source, source, EncodingContext(exact - 1));
            AssertStatus(() => tooSmall.CaptureChannel(channel, []), StatusCodes.BadResourceUnavailable);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DecryptedSyntheticTokenDataConsumesCaptureBudget(bool issued)
        {
            byte[] synthetic = [21, 22, 23];
            IUserIdentityTokenHandler handler = issued
                ? new IssuedIdentityTokenHandler(Profiles.JwtUserToken, synthetic)
                : new UserNameIdentityTokenHandler("synthetic", synthetic);
            var source = new SyntheticOwnerIdentity { TokenHandler = handler };
            int exact = 384 + BinaryEncoder.EncodeMessage(handler.Token, EncodingContext()).Length + synthetic.Length;

            var snapshot = new RecoverableExecutionIdentity(source, source, EncodingContext(exact));
            AssertStatus(() => _ = new RecoverableExecutionIdentity(source, source, EncodingContext(exact - 1)),
                StatusCodes.BadResourceUnavailable);
            if (handler is IssuedIdentityTokenHandler issuedHandler)
            {
                issuedHandler.DecryptedTokenData = [99];
                Assert.That(((IssuedIdentityTokenHandler)snapshot.TokenHandler).DecryptedTokenData,
                    Is.EqualTo(new byte[] { 21, 22, 23 }));
            }
            else
            {
                ((UserNameIdentityTokenHandler)handler).DecryptedPassword![0] = 99;
                Assert.That(((UserNameIdentityTokenHandler)snapshot.TokenHandler).DecryptedPassword,
                    Is.EqualTo(new byte[] { 21, 22, 23 }));
            }
            Assert.That(snapshot.TokenHandler.Token, Is.Not.SameAs(handler.Token));
        }

        [Test]
        public void SnapshotDoesNotExposeAnEncodeableIdentity()
        {
            var source = new SyntheticOwnerIdentity { Subject = "synthetic-private-subject" };
            var snapshot = new RecoverableExecutionIdentity(source, source, EncodingContext());

            Assert.That(snapshot, Is.Not.InstanceOf<IEncodeable>());
            Assert.That(snapshot.ToString(), Does.Not.Contain("synthetic-private-subject"));
            Assert.That(snapshot.Subject, Is.EqualTo("synthetic-private-subject"));
        }

        [Test]
        public void SessionAndHandleCancellationExcludeIndependentOwnedInvocation()
        {
            var identity = new SyntheticOwnerIdentity();
            Mock<ISession> session = Session(identity, []);
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
            using var requests = new RequestManager(server.Object, new FakeTimeProvider());
            var channel = new SecureChannelContext("channel", new EndpointDescription(), RequestEncoding.Binary);
            var header = new RequestHeader { RequestHandle = 43 };
            using var exchangeLifetime = new RequestLifetime(CancellationToken.None);
            using var dependentLifetime = new RequestLifetime(CancellationToken.None);
            using var independentLifetime = new RequestLifetime(CancellationToken.None);
            using var admitted =
                new OperationContext(header, channel, RequestType.Call, exchangeLifetime, session.Object);
            using var dependent = new OperationContext(admitted, header, RequestType.Read, dependentLifetime);
            var snapshot = new RecoverableExecutionIdentity(identity, identity, EncodingContext());
            using var independent = new OperationContext(
                admitted, header, RequestType.Read, independentLifetime, independentIdentity: snapshot);
            using IDisposable exchangeScope = requests.EnterRequestScope(admitted);
            using IDisposable dependentScope = requests.EnterRequestScope(dependent);
            using IDisposable independentScope = requests.EnterRequestScope(independent);

            requests.CancelRequests(admitted.SessionId, 43, out uint handleCount);
            Assert.That(handleCount, Is.EqualTo(1u));
            Assert.That(admitted.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(dependent.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(independent.CancellationToken.IsCancellationRequested, Is.False);

            uint sessionCount = requests.CancelSessionRequests(
                admitted.SessionId, 0, StatusCodes.BadSessionClosed);
            Assert.That(sessionCount, Is.EqualTo(1u));
            Assert.That(dependent.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(independent.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(independent.SessionId, Is.EqualTo(new NodeId("issuing-session", 2)));
            Assert.That(independent.Session, Is.Null);
        }

        private static Mock<ISession> Session(SyntheticOwnerIdentity identity, string[] locales)
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId("issuing-session", 2));
            session.SetupGet(value => value.Identity).Returns(identity);
            session.SetupGet(value => value.EffectiveIdentity).Returns(identity);
            session.SetupGet(value => value.PreferredLocales).Returns(locales);
            return session;
        }

        private static ServiceMessageContext EncodingContext(int limit = 65_536)
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.MaxMessageSize = limit;
            return context;
        }

        private static void AssertStatus(Action action, StatusCode expected)
        {
            ServiceResultException exception = Assert.Throws<ServiceResultException>(action)!;
            Assert.That(exception.StatusCode, Is.EqualTo(expected));
        }

        private static readonly string[] s_groups = ["maintenance"];
        private static readonly string[] s_roles = ["operator"];
        private static readonly string[] s_audiences = ["plant-a", "plant-b"];
        private static readonly string[] s_stringList = ["a", "b"];
        private static readonly string[] s_locales = ["de-DE", "en-US"];
        private static readonly string[] s_budgetRoles = ["r1", "r2"];
    }

    /// <summary>
    /// Deliberately mutable synthetic facts for copy/lifetime tests; this is not an authenticator.
    /// </summary>
    internal sealed class SyntheticOwnerIdentity : IUserIdentity, IIdentityClaims
    {
        public string DisplayName { get; set; } = string.Empty;
        public string PolicyId { get; set; } = string.Empty;
        public UserTokenType TokenType => TokenHandler.TokenType;
        public XmlQualifiedName IssuedTokenType { get; set; } = XmlQualifiedName.Empty;
        public bool SupportsSignatures { get; set; }
        public NodeId[] EffectiveRoles { get; set; } = [];
        public ArrayOf<NodeId> GrantedRoleIds => EffectiveRoles;
        public IUserIdentityTokenHandler TokenHandler { get; set; } = new AnonymousIdentityTokenHandler();
        public string? Issuer { get; set; }
        public string? Subject { get; set; }
        public Dictionary<string, object?> RawClaims { get; } = new(StringComparer.Ordinal);
        public List<string> ClaimGroups { get; set; } = [];
        public List<string> ClaimRoles { get; set; } = [];
        public IReadOnlyDictionary<string, object?> Claims => RawClaims;
        public IReadOnlyList<string> Groups => ClaimGroups;
        public IReadOnlyList<string> Roles => ClaimRoles;
    }
}
