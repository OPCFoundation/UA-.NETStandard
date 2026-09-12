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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Features.Tests
{
    [TestFixture]
    [Category("Roles")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class RoleCertificateServiceRegressionTests : TestFixture
    {
        [TestCase(IdentityCriteriaType.Thumbprint)]
        [TestCase(IdentityCriteriaType.X509Subject)]
        public async Task ActivatedCertificateUserGetsOnlyItsOwnCertificateRoleAsync(IdentityCriteriaType type)
        {
            using Certificate userCertificate = CertificateBuilder.Create("CN=Regression User")
                .SetRSAKeySize(2048).CreateForRSA();
            var provider = new Mock<ICertificateProvider>();
            provider.Setup(value => value.GetPrivateKeyCertificateAsync(
                    It.IsAny<CertificateIdentifier>(), It.IsAny<ICertificatePasswordProvider>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<Certificate>(userCertificate.AddRef()));
            UserIdentity identity = await UserIdentity.CreateAsync(
                new CertificateIdentifier { RawData = userCertificate.RawData },
                Mock.Of<ICertificatePasswordProvider>(), provider.Object).ConfigureAwait(false);
            using Client.ISession client = await OpenAuxSessionAsync(SecurityPolicies.Basic256Sha256, identity)
                .ConfigureAwait(false);
            Server.ISession serverSession = ReferenceServer.CurrentInstance.SessionManager.GetSessions()
                .Single(value => value.Id == client.SessionId);
            Certificate application = serverSession.ClientCertificate;
            Assert.That(application.RawData, Is.Not.EqualTo(userCertificate.RawData));
            var userRule = new IdentityMappingRuleType
            {
                CriteriaType = type,
                Criteria = type == IdentityCriteriaType.Thumbprint ? userCertificate.Thumbprint : "CN=\"Regression User\""
            };
            var applicationRule = new IdentityMappingRuleType
            {
                CriteriaType = type,
                Criteria = type == IdentityCriteriaType.Thumbprint
                    ? application.Thumbprint
                    : Server.IdentityRuleValidator.NormaliseX509Subject(application.Subject)
            };
            Server.IRoleManager roles = ReferenceServer.CurrentInstance.RoleManager;
            try
            {
                Assert.That(roles.AddIdentity(ObjectIds.WellKnownRole_Operator, userRule).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                Assert.That(roles.AddIdentity(ObjectIds.WellKnownRole_Engineer, applicationRule).StatusCode,
                    Is.EqualTo(StatusCodes.Good));
                DataValue read = await client.ReadValueAsync(VariableIds.Server_ServerStatus_State).ConfigureAwait(false);
                Assert.That(read.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(serverSession.EffectiveIdentity.GrantedRoleIds.ToArray(),
                    Has.Member(ObjectIds.WellKnownRole_Operator));
                Assert.That(serverSession.EffectiveIdentity.GrantedRoleIds.ToArray(),
                    Has.No.Member(ObjectIds.WellKnownRole_Engineer));
            }
            finally
            {
                roles.RemoveIdentity(ObjectIds.WellKnownRole_Operator, userRule);
                roles.RemoveIdentity(ObjectIds.WellKnownRole_Engineer, applicationRule);
                await client.CloseAsync(5000, true).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ExcludedApplicationRoleIsNotGrantedToTheUnsignedSessionAsync()
        {
            Server.IRoleManager roles = ReferenceServer.CurrentInstance.RoleManager;
            var rule = new IdentityMappingRuleType { CriteriaType = IdentityCriteriaType.Anonymous };
            Assert.That(roles.AddIdentity(ObjectIds.WellKnownRole_Engineer, rule).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That(roles.AddApplication(ObjectIds.WellKnownRole_Engineer, "urn:blocked:application").StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That(roles.SetApplicationsExclude(ObjectIds.WellKnownRole_Engineer, true).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            try
            {
                await Session.ReadValueAsync(VariableIds.Server_ServerStatus_State).ConfigureAwait(false);
                Server.ISession unsigned = ReferenceServer.CurrentInstance.SessionManager.GetSessions()
                    .Single(value => value.Id == Session.SessionId);
                Assert.That(unsigned.EffectiveIdentity.GrantedRoleIds.ToArray(),
                    Has.No.Member(ObjectIds.WellKnownRole_Engineer));
                using Client.ISession signed = await OpenAuxSessionAsync(SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);
                try
                {
                    Server.ISession signedSession = ReferenceServer.CurrentInstance.SessionManager.GetSessions()
                        .Single(value => value.Id == signed.SessionId);
                    Assert.That(signedSession.EffectiveIdentity.GrantedRoleIds.ToArray(),
                        Has.Member(ObjectIds.WellKnownRole_Engineer));
                }
                finally
                {
                    await signed.CloseAsync(5000, true).ConfigureAwait(false);
                }
            }
            finally
            {
                roles.RemoveApplication(ObjectIds.WellKnownRole_Engineer, "urn:blocked:application");
                roles.RemoveIdentity(ObjectIds.WellKnownRole_Engineer, rule);
            }
        }
    }
}
