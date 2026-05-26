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

using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

// Test fixtures construct short-lived literal arrays inline as method
// arguments; the per-call allocation cost is irrelevant for tests and
// keeping the data adjacent to the assertion improves readability.
#pragma warning disable CA1861 // Avoid constant arrays as arguments

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Unit tests for <see cref="RoleManager"/> covering OPC UA Part 18 §4
    /// compliance: well-known roles registration, default identities, reserved-
    /// role immutability, identity criteria validation, exclude semantics for
    /// applications and endpoints, default-field-aware endpoint comparison,
    /// CustomConfiguration handling, RaiseConfigurationChanged events, and
    /// spec-correct status codes for every mutator.
    /// </summary>
    [TestFixture]
    [Category("Roles")]
    [Parallelizable]
    public class RoleManagerTests
    {
        private static IdentityMappingRuleType AuthenticatedUser()
        {
            return new() { CriteriaType = IdentityCriteriaType.AuthenticatedUser };
        }

        private static IdentityMappingRuleType UserName(string name)
        {
            return new() { CriteriaType = IdentityCriteriaType.UserName, Criteria = name };
        }

        private static IdentityMappingRuleType Thumbprint(string thumbprint)
        {
            return new() { CriteriaType = IdentityCriteriaType.Thumbprint, Criteria = thumbprint };
        }

        private static IdentityMappingRuleType X509Subject(string subject)
        {
            return new() { CriteriaType = IdentityCriteriaType.X509Subject, Criteria = subject };
        }

        // ----------------------------------------------------------------
        // Well-known roles + default identities (Part 18 §4.3)
        // ----------------------------------------------------------------

        [Test]
        public void Constructor_RegistersAllNineWellKnownRoles()
        {
            using var manager = new RoleManager();
            IReadOnlyList<NodeId> roleIds = manager.RoleIds;

            Assert.That(roleIds, Has.Member(ObjectIds.WellKnownRole_Anonymous));
            Assert.That(roleIds, Has.Member(ObjectIds.WellKnownRole_AuthenticatedUser));
            Assert.That(roleIds, Has.Member(ObjectIds.WellKnownRole_TrustedApplication));
            Assert.That(roleIds, Has.Member(ObjectIds.WellKnownRole_Observer));
            Assert.That(roleIds, Has.Member(ObjectIds.WellKnownRole_Operator));
            Assert.That(roleIds, Has.Member(ObjectIds.WellKnownRole_Engineer));
            Assert.That(roleIds, Has.Member(ObjectIds.WellKnownRole_Supervisor));
            Assert.That(roleIds, Has.Member(ObjectIds.WellKnownRole_ConfigureAdmin));
            Assert.That(roleIds, Has.Member(ObjectIds.WellKnownRole_SecurityAdmin));
            Assert.That(roleIds, Has.Count.EqualTo(9));
        }

        [Test]
        public void Constructor_PrePopulatesAnonymousRoleWithSpecMandatedIdentities()
        {
            using var manager = new RoleManager();
            RoleEntry? entry = manager.GetRole(ObjectIds.WellKnownRole_Anonymous);
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.Identities, Has.Count.EqualTo(2));
            Assert.That(entry.Identities.Any(r => r.CriteriaType == IdentityCriteriaType.Anonymous), Is.True);
            Assert.That(entry.Identities.Any(r => r.CriteriaType == IdentityCriteriaType.AuthenticatedUser), Is.True);
        }

        [Test]
        public void Constructor_PrePopulatesAuthenticatedUserRoleWithSpecMandatedIdentity()
        {
            using var manager = new RoleManager();
            RoleEntry? entry = manager.GetRole(ObjectIds.WellKnownRole_AuthenticatedUser);
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.Identities, Has.Count.EqualTo(1));
            Assert.That(entry.Identities[0].CriteriaType, Is.EqualTo(IdentityCriteriaType.AuthenticatedUser));
        }

        [Test]
        public void Constructor_PrePopulatesTrustedApplicationRoleWithSpecMandatedIdentity()
        {
            using var manager = new RoleManager();
            RoleEntry? entry = manager.GetRole(ObjectIds.WellKnownRole_TrustedApplication);
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.Identities, Has.Count.EqualTo(1));
            Assert.That(entry.Identities[0].CriteriaType, Is.EqualTo(IdentityCriteriaType.TrustedApplication));
        }

        [Test]
        public void Constructor_ConfigurableWellKnownRolesHaveNoDefaultIdentities()
        {
            using var manager = new RoleManager();
            foreach (NodeId roleId in new[]
                     {
                         ObjectIds.WellKnownRole_Observer,
                         ObjectIds.WellKnownRole_Operator,
                         ObjectIds.WellKnownRole_Engineer,
                         ObjectIds.WellKnownRole_Supervisor,
                         ObjectIds.WellKnownRole_ConfigureAdmin,
                         ObjectIds.WellKnownRole_SecurityAdmin
                     })
            {
                RoleEntry? entry = manager.GetRole(roleId);
                Assert.That(entry, Is.Not.Null);
                Assert.That(entry!.Identities, Is.Empty, $"{entry.BrowseName} should have no default identities.");
            }
        }

        // ----------------------------------------------------------------
        // Reserved-role immutability (Part 18 §4.3)
        // ----------------------------------------------------------------

        [Test]
        public void AddIdentity_OnAnonymousRole_ReturnsBadRequestNotAllowed()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(ObjectIds.WellKnownRole_Anonymous, UserName("x"));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
        }

        [Test]
        public void AddIdentity_OnAuthenticatedUserRole_ReturnsBadRequestNotAllowed()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(ObjectIds.WellKnownRole_AuthenticatedUser, UserName("x"));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
        }

        [Test]
        public void AddIdentity_OnTrustedApplicationRole_ReturnsBadRequestNotAllowed()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(ObjectIds.WellKnownRole_TrustedApplication, UserName("x"));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadRequestNotAllowed));
        }

        [Test]
        public void RemoveRole_OnReservedRole_ReturnsBadRequestNotAllowed()
        {
            using var manager = new RoleManager();
            foreach (NodeId reserved in new[]
                     {
                         ObjectIds.WellKnownRole_Anonymous,
                         ObjectIds.WellKnownRole_AuthenticatedUser,
                         ObjectIds.WellKnownRole_TrustedApplication
                     })
            {
                ServiceResult result = manager.RemoveRole(reserved);
                Assert.That(result.StatusCode,
                    Is.EqualTo(StatusCodes.BadRequestNotAllowed),
                    $"RemoveRole on reserved role {reserved} should fail.");
            }
        }

        [Test]
        public void RemoveRole_OnConfigurableWellKnownRole_Succeeds()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.RemoveRole(ObjectIds.WellKnownRole_Observer);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(manager.GetRole(ObjectIds.WellKnownRole_Observer), Is.Null);
        }

        // ----------------------------------------------------------------
        // Status codes (Part 18 §4.4.5-§4.4.10)
        // ----------------------------------------------------------------

        [Test]
        public void AddIdentity_DuplicateRule_ReturnsBadAlreadyExists()
        {
            using var manager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                manager.AddIdentity(ObjectIds.WellKnownRole_Observer, UserName("alice"))), Is.True);
            ServiceResult duplicate = manager.AddIdentity(
                ObjectIds.WellKnownRole_Observer, UserName("alice"));
            Assert.That(duplicate.StatusCode,
                Is.EqualTo(StatusCodes.BadAlreadyExists));
        }

        [Test]
        public void RemoveIdentity_NotPresent_ReturnsBadNotFound()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.RemoveIdentity(
                ObjectIds.WellKnownRole_Observer, UserName("alice"));
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public void AddApplication_DuplicateUri_ReturnsBadAlreadyExists()
        {
            using var manager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                manager.AddApplication(ObjectIds.WellKnownRole_Observer, "urn:app")), Is.True);
            ServiceResult duplicate = manager.AddApplication(
                ObjectIds.WellKnownRole_Observer, "urn:app");
            Assert.That(duplicate.StatusCode,
                Is.EqualTo(StatusCodes.BadAlreadyExists));
        }

        [Test]
        public void AddEndpoint_DuplicateEndpoint_ReturnsBadAlreadyExists()
        {
            using var manager = new RoleManager();
            var ep = new EndpointType { EndpointUrl = "opc.tcp://srv:4840" };
            Assert.That(ServiceResult.IsGood(
                manager.AddEndpoint(ObjectIds.WellKnownRole_Observer, ep)), Is.True);
            ServiceResult duplicate = manager.AddEndpoint(ObjectIds.WellKnownRole_Observer, ep);
            Assert.That(duplicate.StatusCode,
                Is.EqualTo(StatusCodes.BadAlreadyExists));
        }

        [Test]
        public void RemoveRole_OnUnknownRole_ReturnsBadNodeIdUnknown()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.RemoveRole(new NodeId(99999u));
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void AddIdentity_OnUnknownRole_ReturnsBadNodeIdUnknown()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(new NodeId(99999u), UserName("x"));
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        // ----------------------------------------------------------------
        // Identity criteria validation (Part 18 §4.4.3)
        // ----------------------------------------------------------------

        [Test]
        public void AddIdentity_AnonymousWithNonEmptyCriteria_ReturnsBadInvalidArgument()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(ObjectIds.WellKnownRole_Observer,
                new IdentityMappingRuleType
                {
                    CriteriaType = IdentityCriteriaType.Anonymous,
                    Criteria = "not-empty"
                });
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void AddIdentity_ThumbprintWithLowerCase_ReturnsBadInvalidArgument()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(ObjectIds.WellKnownRole_Observer,
                Thumbprint("aabbccddee"));
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void AddIdentity_ThumbprintWithSpaces_ReturnsBadInvalidArgument()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(ObjectIds.WellKnownRole_Observer,
                Thumbprint("AA BB CC"));
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void AddIdentity_ThumbprintValidUpperHex_Succeeds()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(ObjectIds.WellKnownRole_Observer,
                Thumbprint("AABBCCDDEE0011"));
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void AddIdentity_X509SubjectInvalidFormat_ReturnsBadInvalidArgument()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(ObjectIds.WellKnownRole_Observer,
                X509Subject("CN=NoQuotes"));
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void AddIdentity_X509SubjectValidFormat_Succeeds()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(ObjectIds.WellKnownRole_Observer,
                X509Subject("CN=\"User Name\"/O=\"Company\""));
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        // ----------------------------------------------------------------
        // ResolveGrantedRoles
        // ----------------------------------------------------------------

        [Test]
        public void ResolveGrantedRoles_AnonymousIdentity_GrantsAnonymousRole()
        {
            using var manager = new RoleManager();
            var identity = new UserIdentity();
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, clientCertificate: null, endpoint: null);
            Assert.That(roles, Has.Member(ObjectIds.WellKnownRole_Anonymous));
        }

        [Test]
        public void ResolveGrantedRoles_AnonymousIdentity_DoesNotGrantAuthenticatedUserRole()
        {
            using var manager = new RoleManager();
            var identity = new UserIdentity();
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, null, null);
            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_AuthenticatedUser));
        }

        [Test]
        public void ResolveGrantedRoles_AnonymousIdentity_DoesNotGrantTrustedApplicationWithoutSignedChannel()
        {
            using var manager = new RoleManager();
            var identity = new UserIdentity();
            using Certificate cert = CertificateBuilder.Create("CN=Test")
                .SetRSAKeySize(CertificateFactory.DefaultKeySize).CreateForRSA();
            // No endpoint = no signed channel ⇒ TrustedApplication rule not satisfied.
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, cert, null);
            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_TrustedApplication));
        }

        [Test]
        public void ResolveGrantedRoles_AnonymousIdentityWithCertAndSignedChannel_GrantsTrustedApplication()
        {
            using var manager = new RoleManager();
            var identity = new UserIdentity();
            using Certificate cert = CertificateBuilder.Create("CN=Test")
                .SetRSAKeySize(CertificateFactory.DefaultKeySize).CreateForRSA();
            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.Sign };
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity, cert, endpoint);
            Assert.That(roles, Has.Member(ObjectIds.WellKnownRole_TrustedApplication));
        }

        [Test]
        public void ResolveGrantedRoles_UserNameMatchesIdentityRule_GrantsRole()
        {
            using var manager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                manager.AddIdentity(ObjectIds.WellKnownRole_Operator, UserName("operator1"))), Is.True);

            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("operator1");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);

            IList<NodeId> roles = manager.ResolveGrantedRoles(identity.Object, null, null);
            Assert.That(roles, Has.Member(ObjectIds.WellKnownRole_Operator));
        }

        [Test]
        public void ResolveGrantedRoles_RoleWithEmptyIdentitiesAndCustomConfigFalse_NotGranted()
        {
            using var manager = new RoleManager();
            // Engineer starts empty; CustomConfiguration defaults to false.
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("alice");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity.Object, null, null);
            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_Engineer));
        }

        [Test]
        public void ResolveGrantedRoles_RoleWithEmptyIdentitiesAndCustomConfigTrue_Granted()
        {
            using var manager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                manager.SetCustomConfiguration(ObjectIds.WellKnownRole_Engineer, true)), Is.True);

            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("alice");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity.Object, null, null);
            Assert.That(roles, Has.Member(ObjectIds.WellKnownRole_Engineer));
        }

        // ----------------------------------------------------------------
        // AddRole
        // ----------------------------------------------------------------

        [Test]
        public void AddRole_NewName_AllocatesDynamicNodeId()
        {
            using var manager = new RoleManager();
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend("http://example.org/custom");

            ServiceResult result = manager.AddRole("CustomRole",
                "http://example.org/custom", namespaces, defaultNamespaceIndex: 0,
                out NodeId newId);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(newId.IsNull, Is.False);
            Assert.That(newId.NamespaceIndex, Is.EqualTo(1));
            Assert.That(manager.GetRole(newId), Is.Not.Null);
        }

        [Test]
        public void AddRole_WellKnownNameUnderOpcUaNamespace_ReusesWellKnownNodeId()
        {
            using var manager = new RoleManager();
            // Remove existing well-known role so we can re-add it via AddRole.
            Assert.That(ServiceResult.IsGood(manager.RemoveRole(ObjectIds.WellKnownRole_Observer)), Is.True);

            var namespaces = new NamespaceTable();
            ServiceResult result = manager.AddRole(
                BrowseNames.WellKnownRole_Observer,
                Ua.Namespaces.OpcUa,
                namespaces,
                defaultNamespaceIndex: 0,
                out NodeId newId);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(newId, Is.EqualTo(ObjectIds.WellKnownRole_Observer));
        }

        [Test]
        public void AddRole_DuplicateName_ReturnsBadAlreadyExists()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddRole(
                BrowseNames.WellKnownRole_Observer,
                Ua.Namespaces.OpcUa,
                new NamespaceTable(),
                defaultNamespaceIndex: 0,
                out _);
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadAlreadyExists));
        }

        [Test]
        public void AddRole_EmptyName_ReturnsBadInvalidArgument()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddRole(string.Empty, null, new NamespaceTable(), 0, out _);
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        // ----------------------------------------------------------------
        // RoleConfigurationChanged event
        // ----------------------------------------------------------------

        [Test]
        public void AddIdentity_OnSuccess_RaisesRoleConfigurationChangedEvent()
        {
            using var manager = new RoleManager();
            RoleConfigurationChangedEventArgs? captured = null;
            manager.RoleConfigurationChanged += (_, e) => captured = e;

            Assert.That(ServiceResult.IsGood(
                manager.AddIdentity(ObjectIds.WellKnownRole_Observer, UserName("a"))), Is.True);
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Kind, Is.EqualTo(RoleConfigurationChangeKind.IdentityAdded));
            Assert.That(captured.RoleId, Is.EqualTo(ObjectIds.WellKnownRole_Observer));
        }

        [Test]
        public void AddIdentity_OnFailure_DoesNotRaiseEvent()
        {
            using var manager = new RoleManager();
            bool raised = false;
            manager.RoleConfigurationChanged += (_, _) => raised = true;

            // Anonymous role is reserved → AddIdentity fails.
            Assert.That(ServiceResult.IsBad(
                manager.AddIdentity(ObjectIds.WellKnownRole_Anonymous, UserName("a"))), Is.True);
            Assert.That(raised, Is.False);
        }

        // ----------------------------------------------------------------
        // Application / Endpoint exclude semantics (Part 18 §4.4.1)
        // ----------------------------------------------------------------

        [Test]
        public void ResolveGrantedRoles_ApplicationFilterInclude_RestrictsToListedApplications()
        {
            using var manager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                manager.AddIdentity(ObjectIds.WellKnownRole_Operator, AuthenticatedUser())), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.AddApplication(ObjectIds.WellKnownRole_Operator, "urn:listed:app")), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.SetApplicationsExclude(ObjectIds.WellKnownRole_Operator, false)), Is.True);

            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("op");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);

            // No cert ⇒ client app URI is empty ⇒ doesn't match the include list
            // ⇒ role not granted.
            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.Sign };
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity.Object, clientCertificate: null, endpoint);
            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_Operator),
                "ApplicationUri does not match the include list — role must not be granted.");
        }

        [Test]
        public void EndpointTypeComparer_DefaultFieldsAreWildcards()
        {
            // Rule with no SecurityMode/PolicyUri/TransportProfileUri should
            // match any candidate that has the matching URL — those default
            // fields act as wildcards per §4.4.2.
            var rule = new EndpointType { EndpointUrl = "opc.tcp://srv:4840" };
            var candidate = new EndpointType
            {
                EndpointUrl = "opc.tcp://srv:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = "http://example/Policy",
                TransportProfileUri = "http://example/Profile"
            };
            Assert.That(EndpointTypeComparer.Matches(rule, candidate), Is.True);
        }

        // ----------------------------------------------------------------
        // Gap 2: UserName rule case-sensitivity
        // ----------------------------------------------------------------

        [Test]
        public void ResolveGrantedRoles_UserNameRuleDifferentCase_DoesNotGrantRole()
        {
            using var manager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                manager.AddIdentity(ObjectIds.WellKnownRole_Operator, UserName("operator1"))), Is.True);

            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("Operator1");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);

            IList<NodeId> roles = manager.ResolveGrantedRoles(identity.Object, null, null);
            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_Operator),
                "UserName matching must be ordinal (case-sensitive).");
        }

        // ----------------------------------------------------------------
        // Gap 3: Application include / exclude semantics — all 4 quadrants
        // ----------------------------------------------------------------

        [Test]
        public void ResolveGrantedRoles_ApplicationFilterInclude_MatchingUri_GrantsRole()
        {
            using var manager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                manager.AddIdentity(ObjectIds.WellKnownRole_Operator, AuthenticatedUser())), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.AddApplication(ObjectIds.WellKnownRole_Operator, "urn:listed:app")), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.SetApplicationsExclude(ObjectIds.WellKnownRole_Operator, false)), Is.True);

            using Certificate cert = CertificateBuilder.Create("CN=Test")
                .AddExtension(new X509SubjectAltNameExtension("urn:listed:app", ["localhost"]))
                .SetRSAKeySize(CertificateFactory.DefaultKeySize).CreateForRSA();
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("op");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);

            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.Sign };
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity.Object, cert, endpoint);
            Assert.That(roles, Has.Member(ObjectIds.WellKnownRole_Operator),
                "Include mode with a matching ApplicationUri must grant the role.");
        }

        [Test]
        public void ResolveGrantedRoles_ApplicationFilterExclude_MatchingUri_DeniesRole()
        {
            using var manager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                manager.AddIdentity(ObjectIds.WellKnownRole_Operator, AuthenticatedUser())), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.AddApplication(ObjectIds.WellKnownRole_Operator, "urn:blocked:app")), Is.True);
            // Operator starts with ApplicationsExclude=true (default for newly
            // created roles per Part 18 §4.2.2). Make this explicit.
            Assert.That(ServiceResult.IsGood(
                manager.SetApplicationsExclude(ObjectIds.WellKnownRole_Operator, true)), Is.True);

            using Certificate cert = CertificateBuilder.Create("CN=Test")
                .AddExtension(new X509SubjectAltNameExtension("urn:blocked:app", ["localhost"]))
                .SetRSAKeySize(CertificateFactory.DefaultKeySize).CreateForRSA();
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("op");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);

            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.Sign };
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity.Object, cert, endpoint);
            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_Operator),
                "Exclude mode with a matching ApplicationUri must deny the role.");
        }

        [Test]
        public void ResolveGrantedRoles_ApplicationFilterExclude_NonMatchingUri_GrantsRole()
        {
            using var manager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                manager.AddIdentity(ObjectIds.WellKnownRole_Operator, AuthenticatedUser())), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.AddApplication(ObjectIds.WellKnownRole_Operator, "urn:blocked:app")), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.SetApplicationsExclude(ObjectIds.WellKnownRole_Operator, true)), Is.True);

            using Certificate cert = CertificateBuilder.Create("CN=Test")
                .AddExtension(new X509SubjectAltNameExtension("urn:other:app", ["localhost"]))
                .SetRSAKeySize(CertificateFactory.DefaultKeySize).CreateForRSA();
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("op");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);

            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.Sign };
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity.Object, cert, endpoint);
            Assert.That(roles, Has.Member(ObjectIds.WellKnownRole_Operator),
                "Exclude mode with a non-matching ApplicationUri must grant the role.");
        }

        // ----------------------------------------------------------------
        // Gap 4: Endpoint include / exclude semantics — all 4 quadrants
        // ----------------------------------------------------------------

        [Test]
        public void ResolveGrantedRoles_EndpointFilterInclude_MatchingEndpoint_GrantsRole()
        {
            using var manager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                manager.AddIdentity(ObjectIds.WellKnownRole_Operator, AuthenticatedUser())), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.AddEndpoint(ObjectIds.WellKnownRole_Operator,
                    new EndpointType { EndpointUrl = "opc.tcp://srv:4840" })), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.SetEndpointsExclude(ObjectIds.WellKnownRole_Operator, false)), Is.True);

            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("op");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);

            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://srv:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt
            };
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity.Object, null, endpoint);
            Assert.That(roles, Has.Member(ObjectIds.WellKnownRole_Operator));
        }

        [Test]
        public void ResolveGrantedRoles_EndpointFilterInclude_NonMatchingEndpoint_DeniesRole()
        {
            using var manager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                manager.AddIdentity(ObjectIds.WellKnownRole_Operator, AuthenticatedUser())), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.AddEndpoint(ObjectIds.WellKnownRole_Operator,
                    new EndpointType { EndpointUrl = "opc.tcp://allowed:4840" })), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.SetEndpointsExclude(ObjectIds.WellKnownRole_Operator, false)), Is.True);

            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("op");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);

            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://other:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt
            };
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity.Object, null, endpoint);
            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_Operator));
        }

        [Test]
        public void ResolveGrantedRoles_EndpointFilterExclude_MatchingEndpoint_DeniesRole()
        {
            using var manager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                manager.AddIdentity(ObjectIds.WellKnownRole_Operator, AuthenticatedUser())), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.AddEndpoint(ObjectIds.WellKnownRole_Operator,
                    new EndpointType { EndpointUrl = "opc.tcp://blocked:4840" })), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.SetEndpointsExclude(ObjectIds.WellKnownRole_Operator, true)), Is.True);

            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("op");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);

            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://blocked:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt
            };
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity.Object, null, endpoint);
            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_Operator));
        }

        [Test]
        public void ResolveGrantedRoles_EndpointFilterExclude_NonMatchingEndpoint_GrantsRole()
        {
            using var manager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                manager.AddIdentity(ObjectIds.WellKnownRole_Operator, AuthenticatedUser())), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.AddEndpoint(ObjectIds.WellKnownRole_Operator,
                    new EndpointType { EndpointUrl = "opc.tcp://blocked:4840" })), Is.True);
            Assert.That(ServiceResult.IsGood(
                manager.SetEndpointsExclude(ObjectIds.WellKnownRole_Operator, true)), Is.True);

            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("op");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);

            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://allowed:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt
            };
            IList<NodeId> roles = manager.ResolveGrantedRoles(identity.Object, null, endpoint);
            Assert.That(roles, Has.Member(ObjectIds.WellKnownRole_Operator));
        }

        // ----------------------------------------------------------------
        // Identity claims: GroupId and Role rule paths
        // ----------------------------------------------------------------

        [Test]
        public void ResolveGrantedRoles_GroupIdRuleWithoutClaims_DoesNotGrantRole()
        {
            using var manager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                manager.AddIdentity(ObjectIds.WellKnownRole_Operator,
                    new IdentityMappingRuleType
                    {
                        CriteriaType = IdentityCriteriaType.GroupId,
                        Criteria = "admins"
                    })), Is.True);

            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns("alice");
            identity.Setup(i => i.GrantedRoleIds).Returns([]);

            IList<NodeId> roles = manager.ResolveGrantedRoles(identity.Object, null, null);
            Assert.That(roles, Has.No.Member(ObjectIds.WellKnownRole_Operator),
                "GroupId rules require an IIdentityClaims.Groups value on the identity.");
        }

        [Test]
        public void ResolveGrantedRoles_RoleCriteriaGrantedRoleNodeIdRequiresLegacyFlag()
        {
            string grantedRoleCriteria = ObjectIds.WellKnownRole_AuthenticatedUser.ToString();
            var identity = new ClaimsTestIdentity(
                tokenType: UserTokenType.UserName,
                roles: new[] { "Operator" });

            using var defaultManager = new RoleManager();
            Assert.That(ServiceResult.IsGood(
                defaultManager.AddIdentity(ObjectIds.WellKnownRole_Operator,
                    new IdentityMappingRuleType
                    {
                        CriteriaType = IdentityCriteriaType.Role,
                        Criteria = grantedRoleCriteria
                    })), Is.True);
            IList<NodeId> defaultRoles = defaultManager.ResolveGrantedRoles(identity, null, null);
            Assert.That(defaultRoles, Has.No.Member(ObjectIds.WellKnownRole_Operator));

            using var legacyManager = new RoleManager(
                new RoleConfigurationOptions { LegacyRoleCriteriaMatchesGrantedRoles = true });
            Assert.That(ServiceResult.IsGood(
                legacyManager.AddIdentity(ObjectIds.WellKnownRole_Operator,
                    new IdentityMappingRuleType
                    {
                        CriteriaType = IdentityCriteriaType.Role,
                        Criteria = grantedRoleCriteria
                    })), Is.True);
            IList<NodeId> legacyRoles = legacyManager.ResolveGrantedRoles(identity, null, null);
            Assert.That(legacyRoles, Has.Member(ObjectIds.WellKnownRole_Operator));
        }

        // ----------------------------------------------------------------
        // Gap 6: AuthenticatedUser / TrustedApplication non-empty criteria
        // ----------------------------------------------------------------

        [TestCase(IdentityCriteriaType.AuthenticatedUser)]
        [TestCase(IdentityCriteriaType.TrustedApplication)]
        public void AddIdentity_NonEmptyCriteriaForImpliedRule_ReturnsBadInvalidArgument(IdentityCriteriaType criteriaType)
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(ObjectIds.WellKnownRole_Observer,
                new IdentityMappingRuleType
                {
                    CriteriaType = criteriaType,
                    Criteria = "must-be-empty"
                });
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        // ----------------------------------------------------------------
        // Gap 7: UserName / Role / GroupId / Application empty criteria
        // ----------------------------------------------------------------

        [TestCase(IdentityCriteriaType.UserName)]
        [TestCase(IdentityCriteriaType.Role)]
        [TestCase(IdentityCriteriaType.GroupId)]
        [TestCase(IdentityCriteriaType.Application)]
        public void AddIdentity_EmptyCriteriaForExplicitRule_ReturnsBadInvalidArgument(IdentityCriteriaType criteriaType)
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(ObjectIds.WellKnownRole_Observer,
                new IdentityMappingRuleType
                {
                    CriteriaType = criteriaType,
                    Criteria = string.Empty
                });
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        // ----------------------------------------------------------------
        // Gap 8 + 9: Thumbprint boundary chars + odd-length / empty
        // ----------------------------------------------------------------

        [Test]
        public void AddIdentity_ThumbprintWithFullHexAlphabet_Succeeds()
        {
            // Exercise every hex character including the digit and letter
            // boundaries '0', '9', 'A' and 'F' so boundary mutations on the
            // hex-range checks (c <= '9', c <= 'F') are caught.
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(ObjectIds.WellKnownRole_Observer,
                Thumbprint("0123456789ABCDEF"));
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void AddIdentity_ThumbprintOddLength_ReturnsBadInvalidArgument()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(ObjectIds.WellKnownRole_Observer,
                Thumbprint("ABC"));
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument),
                "Odd-length thumbprints are not valid hex strings.");
        }

        [Test]
        public void AddIdentity_ThumbprintEmpty_ReturnsBadInvalidArgument()
        {
            using var manager = new RoleManager();
            ServiceResult result = manager.AddIdentity(ObjectIds.WellKnownRole_Observer,
                Thumbprint(string.Empty));
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidArgument));
        }
    }
}
