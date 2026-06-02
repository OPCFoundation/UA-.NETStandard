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

#nullable enable

using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Unit tests for <see cref="RoleAuthorizationGate"/> — the centralised
    /// SecurityAdmin + SignAndEncrypt gate used by every Part 18 §4.2 / §4.4
    /// / §5.2 admin method handler.
    /// </summary>
    [TestFixture]
    [Category("Roles")]
    [Parallelizable]
    public class RoleAuthorizationGateTests
    {
        private static ITelemetryContext Telemetry { get; } = NUnitTelemetryContext.Create();

        private static SessionSystemContext BuildContext(
            MessageSecurityMode securityMode,
            IUserIdentity identity)
        {
            var endpoint = new EndpointDescription { SecurityMode = securityMode };
            var channelContext = new SecureChannelContext("test-channel", endpoint, RequestEncoding.Binary);
            var requestHeader = new RequestHeader();
            // OperationContext implements ISessionOperationContext; the
            // SessionSystemContext's UserIdentity getter prefers the
            // operation context's identity when present, so we pass it in
            // the constructor here.
            var operationContext = new OperationContext(
                requestHeader, channelContext, RequestType.Call, RequestLifetime.None, identity);
            return new SessionSystemContext(operationContext, Telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }

        private static IUserIdentity BuildIdentity(
            UserTokenType tokenType,
            params NodeId[] grantedRoles)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(tokenType);
            identity.Setup(i => i.GrantedRoleIds).Returns(ArrayOf.Wrapped(grantedRoles));
            identity.Setup(i => i.DisplayName).Returns("test-user");
            return identity.Object;
        }

        [Test]
        public void CheckAdmin_AnonymousIdentity_ReturnsBadUserAccessDenied()
        {
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt,
                BuildIdentity(UserTokenType.Anonymous));
            ServiceResult result = RoleAuthorizationGate.CheckAdmin(ctx);
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void CheckAdmin_AuthenticatedButNotSecurityAdmin_ReturnsBadUserAccessDenied()
        {
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt,
                BuildIdentity(UserTokenType.UserName, ObjectIds.WellKnownRole_Observer));
            ServiceResult result = RoleAuthorizationGate.CheckAdmin(ctx);
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void CheckAdmin_AccessTokenRoleClaimWithoutGrantedNodeId_ReturnsBadUserAccessDenied()
        {
            var identity = new ClaimsTestIdentity(
                tokenType: UserTokenType.IssuedToken,
                roles: new[] { BrowseNames.WellKnownRole_SecurityAdmin });
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt, identity);

            ServiceResult result = RoleAuthorizationGate.CheckAdmin(ctx);

            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied),
                "RoleAuthorizationGate consumes resolved GrantedRoleIds; RoleManager maps access-token claims first.");
        }

        [Test]
        public void CheckAdmin_SecurityAdminOverSignAndEncrypt_ReturnsGood()
        {
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt,
                BuildIdentity(UserTokenType.UserName, ObjectIds.WellKnownRole_SecurityAdmin));
            ServiceResult result = RoleAuthorizationGate.CheckAdmin(ctx);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void CheckAdmin_SecurityAdminOverSignOnly_ReturnsBadSecurityModeInsufficient()
        {
            ISystemContext ctx = BuildContext(MessageSecurityMode.Sign,
                BuildIdentity(UserTokenType.UserName, ObjectIds.WellKnownRole_SecurityAdmin));
            ServiceResult result = RoleAuthorizationGate.CheckAdmin(ctx);
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public void CheckAdmin_SecurityAdminOverNoneSecurity_ReturnsBadSecurityModeInsufficient()
        {
            ISystemContext ctx = BuildContext(MessageSecurityMode.None,
                BuildIdentity(UserTokenType.UserName, ObjectIds.WellKnownRole_SecurityAdmin));
            ServiceResult result = RoleAuthorizationGate.CheckAdmin(ctx);
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public void CheckAdmin_OrdersChannelCheckBeforeRoleCheck()
        {
            // Part 18 §4.2/§4.4/§5.2 require Bad_SecurityModeInsufficient to
            // win over Bad_UserAccessDenied so client diagnostics are clear.
            ISystemContext ctx = BuildContext(MessageSecurityMode.None,
                BuildIdentity(UserTokenType.Anonymous));
            ServiceResult result = RoleAuthorizationGate.CheckAdmin(ctx);
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient),
                "SignAndEncrypt check must be evaluated before the role check.");
        }

        [Test]
        public void CheckAdmin_SecurityAdminWithNumericNamespace0NodeId_StillRecognized()
        {
            // Defensive: a custom IRoleManager could return the well-known
            // role NodeId as a raw new NodeId(15704u) rather than via
            // ObjectIds.WellKnownRole_SecurityAdmin. Both must be honoured.
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt,
                BuildIdentity(UserTokenType.UserName,
                    new NodeId(Objects.WellKnownRole_SecurityAdmin)));
            ServiceResult result = RoleAuthorizationGate.CheckAdmin(ctx);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void CheckEncryptedChannel_SignAndEncrypt_ReturnsGood()
        {
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt,
                BuildIdentity(UserTokenType.Anonymous));
            Assert.That(ServiceResult.IsGood(
                RoleAuthorizationGate.CheckEncryptedChannel(ctx)), Is.True);
        }

        [Test]
        public void CheckEncryptedChannel_SignOnly_ReturnsBadSecurityModeInsufficient()
        {
            ISystemContext ctx = BuildContext(MessageSecurityMode.Sign,
                BuildIdentity(UserTokenType.Anonymous));
            ServiceResult result = RoleAuthorizationGate.CheckEncryptedChannel(ctx);
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadSecurityModeInsufficient));
        }

        [Test]
        public void CheckSelfUserName_MatchingUser_ReturnsGood()
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("alice");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt, identity.Object);

            ServiceResult result = RoleAuthorizationGate.CheckSelfUserName(ctx, "alice");
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void CheckSelfUserName_MismatchedUser_ReturnsBadUserAccessDenied()
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("alice");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt, identity.Object);

            ServiceResult result = RoleAuthorizationGate.CheckSelfUserName(ctx, "bob");
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void CheckSelfUserName_AnonymousSession_ReturnsBadInvalidState()
        {
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt,
                BuildIdentity(UserTokenType.Anonymous));
            ServiceResult result = RoleAuthorizationGate.CheckSelfUserName(ctx, "alice");
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void CheckSelfUserName_DifferentCaseName_ReturnsBadUserAccessDenied()
        {
            // Defensive: user-name comparison must be case-sensitive. A
            // database that treats "alice" and "Alice" as distinct accounts
            // would otherwise let "Alice" change "alice"'s password if the
            // gate compared case-insensitively.
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("alice");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);
            ISystemContext ctx = BuildContext(MessageSecurityMode.SignAndEncrypt, identity.Object);

            ServiceResult result = RoleAuthorizationGate.CheckSelfUserName(ctx, "Alice");
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied),
                "CheckSelfUserName must use ordinal (case-sensitive) comparison.");
        }
    }
}
