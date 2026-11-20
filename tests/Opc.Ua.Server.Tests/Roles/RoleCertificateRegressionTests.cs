/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server.Tests.Roles
{
    /// <summary>
    /// Verifies that role rules distinguish application certificates from authenticated user certificates.
    /// </summary>
    [TestFixture]
    [Category("Roles")]
    public sealed class RoleCertificateRegressionTests
    {
        /// <summary>
        /// Creates distinct application and user certificates for role identity and application-filter checks.
        /// </summary>
        [OneTimeSetUp]
        public void CreateCertificates()
        {
            m_application = CreateCertificate("CN=Role Application", "urn:role:application");
            m_user = CreateCertificate("CN=Role User", "urn:role:user");
        }

        /// <summary>
        /// Releases the application and user certificates after role-resolution cases complete.
        /// </summary>
        [OneTimeTearDown]
        public void DisposeCertificates()
        {
            m_user.Dispose();
            m_application.Dispose();
        }

        /// <summary>
        /// Verifies that application inclusion and exclusion filters require a certificate on a signed channel.
        /// </summary>
        [Test]
        public void ApplicationRestrictionsRequireSignedChannelEvenWhenExcluded(
            [Values(false, true)] bool exclude,
            [Values(MessageSecurityMode.None, MessageSecurityMode.Sign, MessageSecurityMode.SignAndEncrypt)]
            MessageSecurityMode mode,
            [Values("matching", "other", "absent")] string certificateKind)
        {
            using var manager = new RoleManager();
            AddIdentity(manager, ObjectIds.WellKnownRole_Operator, IdentityCriteriaType.AuthenticatedUser, null!);
            Assert.That(manager.AddApplication(ObjectIds.WellKnownRole_Operator, "urn:role:application").StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That(manager.SetApplicationsExclude(ObjectIds.WellKnownRole_Operator, exclude).StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Certificate application = (certificateKind switch
            {
                "matching" => m_application,
                "other" => m_user,
                _ => null
            })!;
            IList<NodeId> granted = manager.ResolveGrantedRoles(
                new UserIdentity("user", "password"u8), application,
                new EndpointDescription { SecurityMode = mode });
            bool expected = mode != MessageSecurityMode.None && certificateKind != "absent" &&
                (exclude ? certificateKind != "matching" : certificateKind == "matching");
            Assert.That(granted.Contains(ObjectIds.WellKnownRole_Operator), Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies that roles without application restrictions can be granted without an application certificate.
        /// </summary>
        [Test]
        public void EmptyApplicationFilterDoesNotRequireAnApplicationCertificate()
        {
            using var manager = new RoleManager();
            AddIdentity(manager, ObjectIds.WellKnownRole_Operator, IdentityCriteriaType.AuthenticatedUser, null!);
            IList<NodeId> granted = manager.ResolveGrantedRoles(
                new UserIdentity("user", "password"u8), null,
                new EndpointDescription { SecurityMode = MessageSecurityMode.None });
            Assert.That(granted, Has.Member(ObjectIds.WellKnownRole_Operator));
        }

        /// <summary>
        /// Verifies that subject and thumbprint identity rules use only certificate user tokens, not channel
        /// certificates.
        /// </summary>
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

        /// <summary>
        /// Registers a role identity rule and verifies that its criteria are accepted.
        /// </summary>
        private static void AddIdentity(RoleManager manager, NodeId role, IdentityCriteriaType type, string criteria)
        {
            Assert.That(manager.AddIdentity(role, new IdentityMappingRuleType
            {
                CriteriaType = type,
                Criteria = criteria
            }).StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        /// <summary>
        /// Creates an RSA certificate containing the requested subject and application URI.
        /// </summary>
        private static Certificate CreateCertificate(string subject, string uri)
        {
            return CertificateBuilder.Create(subject)
                .SetNotBefore(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .SetNotAfter(new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .AddExtension(new X509SubjectAltNameExtension(uri, ["localhost"]))
                .SetRSAKeySize(2048).CreateForRSA();
        }

        /// <summary>
        /// Supplies the secure-channel application identity used by application restrictions.
        /// </summary>
        private Certificate m_application;

        /// <summary>
        /// Supplies the authenticated certificate user identity used by subject and thumbprint rules.
        /// </summary>
        private Certificate m_user;
    }
}
