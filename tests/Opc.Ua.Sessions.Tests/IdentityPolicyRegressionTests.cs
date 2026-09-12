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

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Sessions.Tests
{
    [TestFixture]
    [Category("Session")]
    [Category("Security")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class IdentityPolicyRegressionTests : TestFixture
    {
        [TestCase(UserTokenType.UserName)]
        [TestCase(UserTokenType.Certificate)]
        public async Task AnonymousPolicyCannotAuthorizeADifferentTokenTypeAsync(UserTokenType tokenType)
        {
            UserTokenPolicy anonymous = Session.Endpoint.UserIdentityTokens.ToArray()
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

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await Session.ActivateSessionAsync(
                    null,
                    new SignatureData(),
                    [],
                    [],
                    new ExtensionObject(token),
                    new SignatureData(),
                    CancellationToken.None).ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
            DataValue status = await Session
                .ReadValueAsync(VariableIds.Server_ServerStatus_State).ConfigureAwait(false);
            Assert.That(status.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(Session.Identity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
        }
    }
}
