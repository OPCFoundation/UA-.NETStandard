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
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server.Tests.Roles
{
    [TestFixture]
    [Category("Roles")]
    public sealed class RoleCertificateRegressionTests
    {
        [OneTimeSetUp]
        public void CreateCertificates()
        {
            m_application = CreateCertificate("CN=Role Application", "urn:role:application");
            m_user = CreateCertificate("CN=Role User", "urn:role:user");
        }

        [OneTimeTearDown]
        public void DisposeCertificates()
        {
            m_user.Dispose();
            m_application.Dispose();
        }

        [Test]
        public void ApplicationRestrictionsRequireSignedChannelEvenWhenExcluded(
            [Values(false, true)] bool exclude,
            [Values(MessageSecurityMode.None, MessageSecurityMode.Sign, MessageSecurityMode.SignAndEncrypt)]
            MessageSecurityMode mode,
            [Values("matching", "other", "absent")] string certificateKind)
        {
            using var manager = new RoleManager();
            AddIdentity(manager, ObjectIds.WellKnownRole_Operator, IdentityCriteriaType.AuthenticatedUser, null);
            Assert.That(manager.AddApplication(ObjectIds.WellKnownRole_Operator, "urn:role:application").StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That(manager.SetApplicationsExclude(ObjectIds.WellKnownRole_Operator, exclude).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Certificate application = certificateKind switch
            {
                "matching" => m_application,
                "other" => m_user,
                _ => null
            };
            IList<NodeId> granted = manager.ResolveGrantedRoles(
                new UserIdentity("user", "password"u8), application,
                new EndpointDescription { SecurityMode = mode });
            bool expected = mode != MessageSecurityMode.None && certificateKind != "absent" &&
                (exclude ? certificateKind != "matching" : certificateKind == "matching");
            Assert.That(granted.Contains(ObjectIds.WellKnownRole_Operator), Is.EqualTo(expected));
        }

        [Test]
        public void EmptyApplicationFilterDoesNotRequireAnApplicationCertificate()
        {
            using var manager = new RoleManager();
            AddIdentity(manager, ObjectIds.WellKnownRole_Operator, IdentityCriteriaType.AuthenticatedUser, null);
            IList<NodeId> granted = manager.ResolveGrantedRoles(
                new UserIdentity("user", "password"u8), null,
                new EndpointDescription { SecurityMode = MessageSecurityMode.None });
            Assert.That(granted, Has.Member(ObjectIds.WellKnownRole_Operator));
        }

        [TestCase(IdentityCriteriaType.Thumbprint, UserTokenType.Certificate)]
        [TestCase(IdentityCriteriaType.X509Subject, UserTokenType.Certificate)]
        [TestCase(IdentityCriteriaType.Thumbprint, UserTokenType.Anonymous)]
        [TestCase(IdentityCriteriaType.X509Subject, UserTokenType.Anonymous)]
        [TestCase(IdentityCriteriaType.Thumbprint, UserTokenType.UserName)]
        [TestCase(IdentityCriteriaType.X509Subject, UserTokenType.UserName)]
        public void CertificateIdentityRulesUseOnlyTheAuthenticatedUserCertificate(
            IdentityCriteriaType criteriaType, UserTokenType tokenType)
        {
            using var manager = new RoleManager();
            AddIdentity(manager, ObjectIds.WellKnownRole_Operator, criteriaType,
                criteriaType == IdentityCriteriaType.Thumbprint ? m_user.Thumbprint : "CN=\"Role User\"");
            AddIdentity(manager, ObjectIds.WellKnownRole_Engineer, criteriaType,
                criteriaType == IdentityCriteriaType.Thumbprint ? m_application.Thumbprint : "CN=\"Role Application\"");
            AddIdentity(manager, ObjectIds.WellKnownRole_Observer, IdentityCriteriaType.Application, "urn:role:application");
            IUserIdentity identity = tokenType switch
            {
                UserTokenType.Certificate => new UserIdentity(new X509IdentityToken
                {
                    CertificateData = m_user.RawData.ToByteString()
                }),
                UserTokenType.UserName => new UserIdentity("user", "password"u8),
                _ => new UserIdentity()
            };
            IList<NodeId> granted = manager.ResolveGrantedRoles(
                identity, m_application, new EndpointDescription { SecurityMode = MessageSecurityMode.Sign });
            Assert.That(granted.Contains(ObjectIds.WellKnownRole_Operator),
                Is.EqualTo(tokenType == UserTokenType.Certificate));
            Assert.That(granted, Has.No.Member(ObjectIds.WellKnownRole_Engineer));
            Assert.That(granted, Has.Member(ObjectIds.WellKnownRole_Observer));
        }

        private static void AddIdentity(RoleManager manager, NodeId role, IdentityCriteriaType type, string criteria)
        {
            Assert.That(manager.AddIdentity(role, new IdentityMappingRuleType
            {
                CriteriaType = type,
                Criteria = criteria
            }).StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        private static Certificate CreateCertificate(string subject, string uri)
        {
            return CertificateBuilder.Create(subject)
                .SetNotBefore(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .AddExtension(new X509SubjectAltNameExtension(uri, ["localhost"]))
                .SetRSAKeySize(2048).CreateForRSA();
        }

        private Certificate m_application;
        private Certificate m_user;
    }
}
