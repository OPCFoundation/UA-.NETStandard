// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// Verifies session activation binds a policy identifier to its advertised user-token type.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    [Category("Security")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class IdentityPolicyRegressionTests : TestFixture
    {
        /// <summary>
        /// Verifies mismatched username or certificate tokens cannot use an anonymous policy or replace the session
        /// identity.
        /// </summary>
        [TestCase(UserTokenType.UserName)]
        [TestCase(UserTokenType.Certificate)]
        public async Task AnonymousPolicyCannotAuthorizeADifferentTokenTypeAsync(UserTokenType tokenType)
        {
            UserTokenPolicy anonymous = Session.Endpoint.UserIdentityTokens.ToArray()!
                .Single(policy => policy.TokenType == UserTokenType.Anonymous);
            Assert.That(Session.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.None));
            UserIdentityToken token = tokenType == UserTokenType.Certificate
                ? new X509IdentityToken
                {
                    PolicyId = anonymous.PolicyId,
                    CertificateData = Session.Endpoint.ServerCertificate
                }
                : new UserNameIdentityToken
                {
                    PolicyId = anonymous.PolicyId,
                    UserName = "wrong-policy",
                    Password = ByteString.From([1, 2, 3])
                };

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Session.ActivateSessionAsync(
                    null,
                    new SignatureData(),
                    [],
                    [],
                    new ExtensionObject(token),
                    new SignatureData(),
                    CancellationToken.None).ConfigureAwait(false)))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
            DataValue status = await Session
                .ReadValueAsync(VariableIds.Server_ServerStatus_State).ConfigureAwait(false);
            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(Session.Identity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
        }
    }
}
