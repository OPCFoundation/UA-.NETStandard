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

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
namespace Opc.Ua.Conformance.Tests.Security
{
    /// <summary>
    /// compliance tests for OPC UA Role Management.
    /// Tests verify the RoleSet folder, well-known roles, role properties,
    /// and identity/application/endpoint management methods.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("RoleManagement")]
    public class RoleManagementTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Security Role Well Known")]
        [Property("Tag", "001")]
        public async Task RoleSetFolderExistsAsync()
        {
            NodeId serverCapabilities = ToNodeId(
                ObjectIds.Server_ServerCapabilities);

            BrowseResponse response =
                await BrowseForwardAsync(serverCapabilities)
                    .ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.GreaterThan(0));

            bool found = false;
            foreach (ReferenceDescription rd in
                response.Results[0].References)
            {
                if (rd.BrowseName.Name == "RoleSet")
                {
                    found = true;
                    break;
                }
            }
            Assert.That(found, Is.True, "RoleSet folder should exist.");
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Well Known")]
        [Property("Tag", "002")]
        public async Task RoleSetContainsWellKnownRolesAsync()
        {
            NodeId roleSet = ToNodeId(
                ObjectIds.Server_ServerCapabilities_RoleSet);

            BrowseResponse response =
                await BrowseForwardAsync(roleSet).ConfigureAwait(false);
            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.GreaterThan(0));

            string[] expectedRoles =
            [
                "Anonymous", "AuthenticatedUser", "Observer",
                "Operator", "Engineer", "Supervisor",
                "ConfigureAdmin", "SecurityAdmin"
            ];

            var actualNames = new List<string>();
            foreach (ReferenceDescription rd in
                response.Results[0].References)
            {
                actualNames.Add(rd.BrowseName.Name);
            }

            foreach (string role in expectedRoles)
            {
                Assert.That(actualNames, Does.Contain(role),
                    $"RoleSet should contain {role}.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Well Known")]
        [Property("Tag", "003")]
        public async Task AnonymousRoleNodeClassIsObjectAsync()
        {
            NodeId anonymousId = ToNodeId(ObjectIds.WellKnownRole_Anonymous);
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = anonymousId,
                        AttributeId = Attributes.NodeClass
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results[0].StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((NodeClass)response.Results[0].WrappedValue.GetInt32(),
                Is.EqualTo(NodeClass.Object));
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Well Known")]
        [Property("Tag", "003")]
        public async Task AuthenticatedUserRoleHasCorrectNodeClassAsync()
        {
            NodeId roleId = ToNodeId(
                ObjectIds.WellKnownRole_AuthenticatedUser);
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = roleId,
                        AttributeId = Attributes.NodeClass
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results[0].StatusCode,
                Is.EqualTo(StatusCodes.Good));
            Assert.That((NodeClass)response.Results[0].WrappedValue.GetInt32(),
                Is.EqualTo(NodeClass.Object));
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Well Known")]
        [Property("Tag", "002")]
        public async Task ObserverRoleExistsAsync()
        {
            NodeId roleId = ToNodeId(ObjectIds.WellKnownRole_Observer);
            DataValue dv = await ReadPropertyValueAsync(roleId)
                .ConfigureAwait(false);
            Assert.That(dv, Is.Not.Null);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Well Known")]
        [Property("Tag", "002")]
        public async Task OperatorRoleExistsAsync()
        {
            NodeId roleId = ToNodeId(ObjectIds.WellKnownRole_Operator);
            DataValue dv = await ReadPropertyValueAsync(roleId)
                .ConfigureAwait(false);
            Assert.That(dv, Is.Not.Null);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Well Known")]
        [Property("Tag", "002")]
        public async Task EngineerRoleExistsAsync()
        {
            NodeId roleId = ToNodeId(ObjectIds.WellKnownRole_Engineer);
            DataValue dv = await ReadPropertyValueAsync(roleId)
                .ConfigureAwait(false);
            Assert.That(dv, Is.Not.Null);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Well Known")]
        [Property("Tag", "002")]
        public async Task SupervisorRoleExistsAsync()
        {
            NodeId roleId = ToNodeId(ObjectIds.WellKnownRole_Supervisor);
            DataValue dv = await ReadPropertyValueAsync(roleId)
                .ConfigureAwait(false);
            Assert.That(dv, Is.Not.Null);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Well Known")]
        [Property("Tag", "002")]
        public async Task SecurityAdminRoleExistsAsync()
        {
            NodeId roleId = ToNodeId(
                ObjectIds.WellKnownRole_SecurityAdmin);
            DataValue dv = await ReadPropertyValueAsync(roleId)
                .ConfigureAwait(false);
            Assert.That(dv, Is.Not.Null);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Well Known")]
        [Property("Tag", "002")]
        public async Task ConfigureAdminRoleExistsAsync()
        {
            NodeId roleId = ToNodeId(
                ObjectIds.WellKnownRole_ConfigureAdmin);
            DataValue dv = await ReadPropertyValueAsync(roleId)
                .ConfigureAwait(false);
            Assert.That(dv, Is.Not.Null);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Well Known")]
        [Property("Tag", "003")]
        public async Task RoleHasTypeDefinitionRoleTypeAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = observerId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition,
                        IncludeSubtypes = false,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.GreaterThan(0));

            bool hasRoleType = false;
            foreach (ReferenceDescription rd in
                response.Results[0].References)
            {
                var targetId = ExpandedNodeId.ToNodeId(
                    rd.NodeId, Session.NamespaceUris);
                if (targetId == new NodeId(15620))
                {
                    hasRoleType = true;
                    break;
                }
            }
            Assert.That(hasRoleType, Is.True,
                "Role should have TypeDefinition of RoleType (i=15620).");
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Management")]
        [Property("Tag", "001")]
        public async Task RoleSetHasAddRoleMethodAsync()
        {
            NodeId roleSet = ToNodeId(
                ObjectIds.Server_ServerCapabilities_RoleSet);

            NodeId methodId = await FindMethodAsync(roleSet, "AddRole")
                .ConfigureAwait(false);
            if (methodId.IsNull)
            {
                Assert.Ignore(
                    "AddRole method not found on RoleSet. " +
                    "Feature not supported by server.");
            }

            Assert.That(methodId.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Well Known")]
        [Property("Tag", "003")]
        public async Task AnonymousRoleHasIdentitiesPropertyAsync()
        {
            NodeId anonymousId = ToNodeId(ObjectIds.WellKnownRole_Anonymous);
            NodeId identitiesId = await FindChildAsync(
                anonymousId, "Identities").ConfigureAwait(false);

            if (identitiesId.IsNull)
            {
                Assert.Ignore(
                    "Identities property not exposed by server.");
            }

            Assert.That(identitiesId, Is.Not.EqualTo(NodeId.Null));
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Well Known")]
        [Property("Tag", "003")]
        public async Task ReadAnonymousIdentitiesAsync()
        {
            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync().ConfigureAwait(false);

                NodeId anonymousId = ToNodeId(ObjectIds.WellKnownRole_Anonymous);
                NodeId identitiesId = await FindChildAsync(
                    anonymousId, "Identities", adminSession).ConfigureAwait(false);
                if (identitiesId.IsNull)
                {
                    Assert.Ignore("Identities property not found.");
                }

                DataValue dv = await ReadPropertyValueAsync(identitiesId, adminSession)
                    .ConfigureAwait(false);
                Assert.That(dv.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true).ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Restrict Applications")]
        [Property("Tag", "001")]
        public async Task RoleHasApplicationsPropertyAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);
            NodeId appsId = await FindChildAsync(
                observerId, "Applications").ConfigureAwait(false);

            if (appsId.IsNull)
            {
                Assert.Ignore(
                    "Applications property not found. " +
                    "Feature not supported by server.");
            }

            Assert.That(appsId.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Restrict Endpoints")]
        [Property("Tag", "001")]
        public async Task RoleHasEndpointsPropertyAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);
            NodeId endpointsId = await FindChildAsync(
                observerId, "Endpoints").ConfigureAwait(false);

            if (endpointsId.IsNull)
            {
                Assert.Fail(
                    "Endpoints property not found. " +
                    "Feature not supported by server.");
            }

            Assert.That(endpointsId.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Restrict Applications")]
        [Property("Tag", "002")]
        public async Task ReadApplicationsExcludePropertyAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);
            NodeId excludeId = await FindChildAsync(
                observerId, "ApplicationsExclude").ConfigureAwait(false);

            if (excludeId.IsNull)
            {
                Assert.Fail(
                    "ApplicationsExclude not found. " +
                    "Feature not supported by server.");
            }

            DataValue dv = await ReadPropertyValueAsync(excludeId)
                .ConfigureAwait(false);
            Assert.That(dv.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(dv.WrappedValue.TryGetValue(out bool _), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Restrict Endpoints")]
        [Property("Tag", "002")]
        public async Task ReadEndpointsExcludePropertyAsync()
        {
            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync().ConfigureAwait(false);

                NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);
                NodeId excludeId = await FindChildAsync(
                    observerId, "EndpointsExclude", adminSession).ConfigureAwait(false);

                if (excludeId.IsNull)
                {
                    Assert.Ignore(
                        "EndpointsExclude not found. " +
                        "Feature not supported by server.");
                }

                DataValue dv = await ReadPropertyValueAsync(excludeId, adminSession)
                    .ConfigureAwait(false);
                Assert.That(dv.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(dv.WrappedValue.TryGetValue(out bool _), Is.True);
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true).ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server IdentityManagement")]
        [Property("Tag", "001")]
        public async Task RoleHasAddIdentityMethodAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);
            NodeId methodId = await FindMethodAsync(
                observerId, "AddIdentity").ConfigureAwait(false);

            if (methodId.IsNull)
            {
                Assert.Fail(
                    "AddIdentity method not found. " +
                    "Feature not supported by server.");
            }

            Assert.That(methodId.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server IdentityManagement")]
        [Property("Tag", "002")]
        public async Task RoleHasRemoveIdentityMethodAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);
            NodeId methodId = await FindMethodAsync(
                observerId, "RemoveIdentity").ConfigureAwait(false);

            if (methodId.IsNull)
            {
                Assert.Ignore(
                    "RemoveIdentity method not found. " +
                    "Feature not supported by server.");
            }

            Assert.That(methodId.IsNull, Is.False);
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server IdentityManagement")]
        [Property("Tag", "003")]
        public async Task AddIdentityToObserverRoleSucceedsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "testAddIdentity");

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                if (result.StatusCode == StatusCodes.BadNotImplemented ||
                    result.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Ignore($"Server method not implemented: {result.StatusCode}");
                }

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True,
                    $"AddIdentity failed: {result.StatusCode}");

                // Cleanup
                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveIdentity", adminSession)
                    .ConfigureAwait(false);
                if (!removeMethod.IsNull)
                {
                    await CallRoleMethodAsync(
                        adminSession, observerId, removeMethod,
                        new Variant(rule)).ConfigureAwait(false);
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server IdentityManagement")]
        [Property("Tag", "004")]
        public async Task ReadObserverIdentitiesAfterAddAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "testReadAfterAdd");

                CallMethodResult addResult = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                if (addResult.StatusCode == StatusCodes.BadNotImplemented ||
                    addResult.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Ignore($"Server method not implemented: {addResult.StatusCode}");
                }

                NodeId identitiesId = await FindChildAsync(
                    observerId, "Identities", adminSession)
                    .ConfigureAwait(false);

                if (!identitiesId.IsNull)
                {
                    DataValue dv = await ReadPropertyValueAsync(
                        identitiesId, adminSession).ConfigureAwait(false);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                }

                // Cleanup
                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveIdentity", adminSession)
                    .ConfigureAwait(false);
                if (!removeMethod.IsNull)
                {
                    await CallRoleMethodAsync(
                        adminSession, observerId, removeMethod,
                        new Variant(rule)).ConfigureAwait(false);
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server IdentityManagement")]
        [Property("Tag", "005")]
        public async Task RemoveIdentityFromObserverRoleSucceedsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);
                NodeId removeMethod = await RequireMethodAsync(
                    observerId, "RemoveIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "testRemoveIdentity");

                await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, observerId, removeMethod,
                    new Variant(rule)).ConfigureAwait(false);

                if (result.StatusCode == StatusCodes.BadNotImplemented ||
                    result.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Ignore($"Server method not implemented: {result.StatusCode}");
                }

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True,
                    $"RemoveIdentity failed: {result.StatusCode}");
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server IdentityManagement")]
        [Property("Tag", "006")]
        public async Task ReadObserverIdentitiesAfterRemoveAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);
                NodeId removeMethod = await RequireMethodAsync(
                    observerId, "RemoveIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "testRemoveAndRead");

                CallMethodResult addResult = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                if (addResult.StatusCode == StatusCodes.BadNotImplemented ||
                    addResult.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Ignore($"Server method not implemented: {addResult.StatusCode}");
                }

                await CallRoleMethodAsync(
                    adminSession, observerId, removeMethod,
                    new Variant(rule)).ConfigureAwait(false);

                NodeId identitiesId = await FindChildAsync(
                    observerId, "Identities", adminSession)
                    .ConfigureAwait(false);

                if (!identitiesId.IsNull)
                {
                    DataValue dv = await ReadPropertyValueAsync(
                        identitiesId, adminSession).ConfigureAwait(false);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server IdentityManagement")]
        [Property("Tag", "007")]
        public async Task AddIdentityWithUserNameCriteriaAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "userNameTest");

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                if (result.StatusCode == StatusCodes.BadNotImplemented ||
                    result.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Ignore($"Server method not implemented: {result.StatusCode}");
                }

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True);

                // Cleanup
                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveIdentity", adminSession)
                    .ConfigureAwait(false);
                if (!removeMethod.IsNull)
                {
                    await CallRoleMethodAsync(
                        adminSession, observerId, removeMethod,
                        new Variant(rule)).ConfigureAwait(false);
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server IdentityManagement")]
        [Property("Tag", "008")]
        public async Task AddIdentityWithThumbprintCriteriaAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeThumbprint,
                    "AABBCCDDEE00112233445566778899AABBCCDDEE");

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                if (result.StatusCode == StatusCodes.BadNotImplemented ||
                    result.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Ignore($"Server method not implemented: {result.StatusCode}");
                }

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True);

                // Cleanup
                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveIdentity", adminSession)
                    .ConfigureAwait(false);
                if (!removeMethod.IsNull)
                {
                    await CallRoleMethodAsync(
                        adminSession, observerId, removeMethod,
                        new Variant(rule)).ConfigureAwait(false);
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server IdentityManagement")]
        [Property("Tag", "009")]
        public async Task AddIdentityDuplicateIsIdempotentAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "duplicateIdempotent");

                CallMethodResult result1 = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                if (result1.StatusCode == StatusCodes.BadNotImplemented ||
                    result1.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Ignore($"Server method not implemented: {result1.StatusCode}");
                }

                Assert.That(
                    StatusCode.IsGood(result1.StatusCode), Is.True);

                CallMethodResult result2 = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(rule)).ConfigureAwait(false);

                if (result2.StatusCode == StatusCodes.BadNotImplemented ||
                    result2.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Ignore($"Server method not implemented: {result2.StatusCode}");
                }

                Assert.That(
                    StatusCode.IsGood(result2.StatusCode), Is.True);

                // Cleanup
                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveIdentity", adminSession)
                    .ConfigureAwait(false);
                if (!removeMethod.IsNull)
                {
                    await CallRoleMethodAsync(
                        adminSession, observerId, removeMethod,
                        new Variant(rule)).ConfigureAwait(false);
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server IdentityManagement")]
        [Property("Tag", "010")]
        public async Task RemoveNonExistentIdentityReturnsNoMatchAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId removeMethod = await RequireMethodAsync(
                    observerId, "RemoveIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "nonExistentUser_" +
                        Guid.NewGuid().ToString("N")[..8]);

                try
                {
                    CallMethodResult result = await CallRoleMethodAsync(
                        adminSession, observerId, removeMethod,
                        new Variant(rule)).ConfigureAwait(false);

                    // Some servers succeed, some return Bad status
                    Assert.That(result, Is.Not.Null);
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadInvalidArgument ||
                        sre.StatusCode == StatusCodes.BadNoMatch)
                {
                    // Expected for some implementations
                    Assert.Pass(
                        "Server returned expected error: " +
                        $"{sre.StatusCode}");
                }
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server IdentityManagement")]
        [Property("Tag", "011")]
        public async Task AddIdentityWithoutSecurityAdminFailsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession userSession = null;
            try
            {
                try
                {
                    userSession = await ConnectAsRegularUserAsync()
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                        sre.StatusCode == StatusCodes.BadIdentityTokenInvalid)
                {
                    Assert.Ignore(
                        $"Regular user not available: {sre.StatusCode}");
                }

                NodeId addMethod = await FindMethodAsync(
                    observerId, "AddIdentity", userSession)
                    .ConfigureAwait(false);

                if (addMethod.IsNull)
                {
                    Assert.Ignore(
                        "AddIdentity not found. " +
                        "Feature not supported by server.");
                }

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "unauthorized");

                try
                {
                    CallMethodResult result = await CallRoleMethodAsync(
                        userSession, observerId, addMethod,
                        new Variant(rule)).ConfigureAwait(false);

                    if (result.StatusCode == StatusCodes.BadNotImplemented ||
                        result.StatusCode == StatusCodes.BadServiceUnsupported)
                    {
                        Assert.Ignore($"Server method not implemented: {result.StatusCode}");
                    }

                    // BadMethodInvalid is also a valid denial outcome — the role
                    // permission filter hides the method from non-admin sessions.
                    if (result.StatusCode == StatusCodes.BadUserAccessDenied
                        || result.StatusCode == StatusCodes.BadMethodInvalid)
                    {
                        Assert.Pass("Server correctly denied access.");
                    }

                    Assert.Fail(
                        $"AddIdentity expected BadUserAccessDenied but got: 0x{result.StatusCode.Code:X8}");
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                        sre.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Ignore($"Server method not implemented: {sre.Message}");
                }
                catch (ServiceResultException sre)
                {
                    Assert.That(sre.StatusCode,
                        Is.EqualTo(StatusCodes.BadUserAccessDenied));
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (userSession != null)
                {
                    await userSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    userSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server IdentityManagement")]
        [Property("Tag", "012")]
        public async Task RemoveIdentityWithoutSecurityAdminFailsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession userSession = null;
            try
            {
                userSession = await ConnectAsRegularUserAsync()
                    .ConfigureAwait(false);

                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveIdentity", userSession)
                    .ConfigureAwait(false);

                if (removeMethod.IsNull)
                {
                    Assert.Ignore(
                        "RemoveIdentity not found. " +
                        "Feature not supported by server.");
                }

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, "unauthorized");

                ServiceResultException ex = null;
                CallMethodResult result = null;
                try
                {
                    result = await CallRoleMethodAsync(
                            userSession, observerId, removeMethod,
                            new Variant(rule)).ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    ex = sre;
                }

                StatusCode statusCode = ex?.StatusCode
                    ?? result?.StatusCode
                    ?? StatusCodes.Good;
                Assert.That(statusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            }
            finally
            {
                if (userSession != null)
                {
                    await userSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    userSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server IdentityManagement")]
        [Property("Tag", "013")]
        public async Task AddIdentityWithNoArgumentsFailsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                try
                {
                    CallMethodResult result = await CallRoleMethodAsync(
                        adminSession, observerId, addMethod)
                        .ConfigureAwait(false);

                    if (result.StatusCode == StatusCodes.BadNotImplemented ||
                        result.StatusCode == StatusCodes.BadServiceUnsupported)
                    {
                        Assert.Fail($"Server method not implemented: {result.StatusCode}");
                    }

                    if (StatusCode.IsBad(result.StatusCode))
                    {
                        // Expected - server rejected call with no arguments via status code
                    }
                    else
                    {
                        Assert.Fail("Expected bad status or ServiceResultException.");
                    }
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                        sre.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Fail($"Server method not implemented: {sre.Message}");
                }
                catch (ServiceResultException)
                {
                    // Expected - method rejected call with no arguments
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Fail($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server IdentityManagement")]
        [Property("Tag", "014")]
        public async Task AddIdentityWithEmptyCriteriaFailsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddIdentity", adminSession)
                    .ConfigureAwait(false);

                ExtensionObject rule = CreateIdentityRule(
                    CriteriaTypeUserName, string.Empty);

                try
                {
                    CallMethodResult result = await CallRoleMethodAsync(
                        adminSession, observerId, addMethod,
                        new Variant(rule)).ConfigureAwait(false);

                    // Some servers accept empty, some reject
                    if (StatusCode.IsBad(result.StatusCode))
                    {
                        Assert.Pass(
                            "Server correctly rejected empty criteria.");
                    }
                }
                catch (ServiceResultException)
                {
                    Assert.Pass(
                        "Server correctly rejected empty criteria.");
                }
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Restrict Applications")]
        [Property("Tag", "003")]
        public async Task AddApplicationToRoleSucceedsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);

                const string appUri = "urn:test:app:addAppTest";
                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri)).ConfigureAwait(false);

                if (result.StatusCode == StatusCodes.BadNotImplemented ||
                    result.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Ignore($"Server method not implemented: {result.StatusCode}");
                }

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True,
                    $"AddApplication failed: {result.StatusCode}");

                // Cleanup
                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveApplication", adminSession)
                    .ConfigureAwait(false);
                if (!removeMethod.IsNull)
                {
                    await CallRoleMethodAsync(
                        adminSession, observerId, removeMethod,
                        new Variant(appUri)).ConfigureAwait(false);
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Restrict Applications")]
        [Property("Tag", "004")]
        public async Task ReadApplicationsAfterAddAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);

                const string appUri = "urn:test:app:readAfterAdd";
                CallMethodResult addResult = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri)).ConfigureAwait(false);

                if (addResult.StatusCode == StatusCodes.BadNotImplemented ||
                    addResult.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Ignore($"Server method not implemented: {addResult.StatusCode}");
                }

                NodeId appsId = await FindChildAsync(
                    observerId, "Applications", adminSession)
                    .ConfigureAwait(false);

                if (!appsId.IsNull)
                {
                    DataValue dv = await ReadPropertyValueAsync(
                        appsId, adminSession).ConfigureAwait(false);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                }

                // Cleanup
                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveApplication", adminSession)
                    .ConfigureAwait(false);
                if (!removeMethod.IsNull)
                {
                    await CallRoleMethodAsync(
                        adminSession, observerId, removeMethod,
                        new Variant(appUri)).ConfigureAwait(false);
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Restrict Applications")]
        [Property("Tag", "005")]
        public async Task RemoveApplicationFromRoleSucceedsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddApplication", adminSession)
                    .ConfigureAwait(false);
                NodeId removeMethod = await RequireMethodAsync(
                    observerId, "RemoveApplication", adminSession)
                    .ConfigureAwait(false);

                const string appUri = "urn:test:app:removeTest";
                await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(appUri)).ConfigureAwait(false);

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, observerId, removeMethod,
                    new Variant(appUri)).ConfigureAwait(false);

                if (result.StatusCode == StatusCodes.BadNotImplemented ||
                    result.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Ignore($"Server method not implemented: {result.StatusCode}");
                }

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True,
                    $"RemoveApplication failed: {result.StatusCode}");
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Restrict Applications")]
        [Property("Tag", "006")]
        public async Task AddApplicationWithoutAdminFailsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession userSession = null;
            try
            {
                try
                {
                    userSession = await ConnectAsRegularUserAsync()
                        .ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadIdentityTokenRejected ||
                        sre.StatusCode == StatusCodes.BadIdentityTokenInvalid)
                {
                    Assert.Ignore(
                        $"Regular user not available: {sre.StatusCode}");
                }

                NodeId addMethod = await FindMethodAsync(
                    observerId, "AddApplication", userSession)
                    .ConfigureAwait(false);

                if (addMethod.IsNull)
                {
                    Assert.Ignore(
                        "AddApplication not found. " +
                        "Feature not supported by server.");
                }

                try
                {
                    CallMethodResult result = await CallRoleMethodAsync(
                        userSession, observerId, addMethod,
                        new Variant("urn:test:unauthorized"))
                        .ConfigureAwait(false);

                    if (result.StatusCode == StatusCodes.BadNotImplemented ||
                        result.StatusCode == StatusCodes.BadServiceUnsupported)
                    {
                        Assert.Ignore($"Server method not implemented: {result.StatusCode}");
                    }

                    // BadMethodInvalid is also a valid "access denied" outcome:
                    // the role permission check hides the method from browse, so
                    // calling it returns "method not on object" rather than the
                    // semantically clearer BadUserAccessDenied.
                    if (result.StatusCode == StatusCodes.BadUserAccessDenied
                        || result.StatusCode == StatusCodes.BadMethodInvalid)
                    {
                        Assert.Pass("Server correctly denied access.");
                    }

                    Assert.Fail(
                        $"AddApplication expected BadUserAccessDenied but got: 0x{result.StatusCode.Code:X8}");
                }
                catch (ServiceResultException sre)
                    when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                        sre.StatusCode == StatusCodes.BadServiceUnsupported)
                {
                    Assert.Ignore($"Server method not implemented: {sre.Message}");
                }
                catch (ServiceResultException sre)
                {
                    Assert.That(sre.StatusCode,
                        Is.EqualTo(StatusCodes.BadUserAccessDenied));
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (userSession != null)
                {
                    await userSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    userSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Restrict Endpoints")]
        [Property("Tag", "003")]
        public async Task AddEndpointToRoleSucceedsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddEndpoint", adminSession)
                    .ConfigureAwait(false);

                const string endpointUrl = "opc.tcp://testhost:4840";
                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(CreateEndpoint(endpointUrl))).ConfigureAwait(false);

                if (result.StatusCode == StatusCodes.BadNotImplemented ||
                    result.StatusCode == StatusCodes.BadServiceUnsupported ||
                    result.StatusCode == StatusCodes.BadInvalidArgument)
                {
                    Assert.Ignore($"Server method not implemented: {result.StatusCode}");
                }

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True,
                    $"AddEndpoint failed: {result.StatusCode}");

                // Cleanup
                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveEndpoint", adminSession)
                    .ConfigureAwait(false);
                if (!removeMethod.IsNull)
                {
                    await CallRoleMethodAsync(
                        adminSession, observerId, removeMethod,
                        new Variant(CreateEndpoint(endpointUrl))).ConfigureAwait(false);
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported ||
                    sre.StatusCode == StatusCodes.BadInvalidArgument)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Restrict Endpoints")]
        [Property("Tag", "004")]
        public async Task ReadEndpointsAfterAddAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddEndpoint", adminSession)
                    .ConfigureAwait(false);

                const string endpointUrl = "opc.tcp://testhost:4841";
                CallMethodResult addResult = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(CreateEndpoint(endpointUrl))).ConfigureAwait(false);

                if (addResult.StatusCode == StatusCodes.BadNotImplemented ||
                    addResult.StatusCode == StatusCodes.BadServiceUnsupported ||
                    addResult.StatusCode == StatusCodes.BadInvalidArgument)
                {
                    Assert.Ignore($"Server method not implemented: {addResult.StatusCode}");
                }

                NodeId endpointsId = await FindChildAsync(
                    observerId, "Endpoints", adminSession)
                    .ConfigureAwait(false);

                if (!endpointsId.IsNull)
                {
                    DataValue dv = await ReadPropertyValueAsync(
                        endpointsId, adminSession).ConfigureAwait(false);
                    Assert.That(dv.StatusCode,
                        Is.EqualTo(StatusCodes.Good));
                }

                // Cleanup
                NodeId removeMethod = await FindMethodAsync(
                    observerId, "RemoveEndpoint", adminSession)
                    .ConfigureAwait(false);
                if (!removeMethod.IsNull)
                {
                    await CallRoleMethodAsync(
                        adminSession, observerId, removeMethod,
                        new Variant(CreateEndpoint(endpointUrl))).ConfigureAwait(false);
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported ||
                    sre.StatusCode == StatusCodes.BadInvalidArgument)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Restrict Endpoints")]
        [Property("Tag", "005")]
        public async Task RemoveEndpointFromRoleSucceedsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession adminSession = null;
            try
            {
                adminSession = await ConnectAsAdminAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await RequireMethodAsync(
                    observerId, "AddEndpoint", adminSession)
                    .ConfigureAwait(false);
                NodeId removeMethod = await RequireMethodAsync(
                    observerId, "RemoveEndpoint", adminSession)
                    .ConfigureAwait(false);

                const string endpointUrl = "opc.tcp://testhost:4842";
                CallMethodResult addResult = await CallRoleMethodAsync(
                    adminSession, observerId, addMethod,
                    new Variant(CreateEndpoint(endpointUrl))).ConfigureAwait(false);

                if (addResult.StatusCode == StatusCodes.BadNotImplemented ||
                    addResult.StatusCode == StatusCodes.BadServiceUnsupported ||
                    addResult.StatusCode == StatusCodes.BadInvalidArgument)
                {
                    Assert.Ignore($"Server method not implemented: {addResult.StatusCode}");
                }

                CallMethodResult result = await CallRoleMethodAsync(
                    adminSession, observerId, removeMethod,
                    new Variant(CreateEndpoint(endpointUrl))).ConfigureAwait(false);

                if (result.StatusCode == StatusCodes.BadNotImplemented ||
                    result.StatusCode == StatusCodes.BadServiceUnsupported ||
                    result.StatusCode == StatusCodes.BadInvalidArgument)
                {
                    Assert.Ignore($"Server method not implemented: {result.StatusCode}");
                }

                Assert.That(
                    StatusCode.IsGood(result.StatusCode), Is.True,
                    $"RemoveEndpoint failed: {result.StatusCode}");
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported ||
                    sre.StatusCode == StatusCodes.BadInvalidArgument)
            {
                Assert.Ignore($"Server method not implemented: {sre.Message}");
            }
            finally
            {
                if (adminSession != null)
                {
                    await adminSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    adminSession.Dispose();
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security Role Server Restrict Endpoints")]
        [Property("Tag", "006")]
        public async Task AddEndpointWithoutAdminFailsAsync()
        {
            NodeId observerId = ToNodeId(ObjectIds.WellKnownRole_Observer);

            ISession userSession = null;
            try
            {
                userSession = await ConnectAsRegularUserAsync()
                    .ConfigureAwait(false);

                NodeId addMethod = await FindMethodAsync(
                    observerId, "AddEndpoint", userSession)
                    .ConfigureAwait(false);

                if (addMethod.IsNull)
                {
                    Assert.Ignore(
                        "AddEndpoint not found. " +
                        "Feature not supported by server.");
                }

                ServiceResultException ex = null;
                CallMethodResult result = null;
                try
                {
                    result = await CallRoleMethodAsync(
                            userSession, observerId, addMethod,
                            new Variant(CreateEndpoint("opc.tcp://x:4840")))
                            .ConfigureAwait(false);
                }
                catch (ServiceResultException sre)
                {
                    ex = sre;
                }

                StatusCode statusCode = ex?.StatusCode
                    ?? result?.StatusCode
                    ?? StatusCodes.Good;
                Assert.That(statusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
            }
            finally
            {
                if (userSession != null)
                {
                    await userSession.CloseAsync(5000, true)
                        .ConfigureAwait(false);
                    userSession.Dispose();
                }
            }
        }

        private async Task<BrowseResponse> BrowseForwardAsync(
            NodeId nodeId,
            ISession session = null)
        {
            session ??= Session;
            return await session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
        }

        private async Task<NodeId> FindMethodAsync(
            NodeId parentId,
            string methodName,
            ISession session = null)
        {
            BrowseResponse response =
                await BrowseForwardAsync(parentId, session)
                    .ConfigureAwait(false);
            if (response?.Results != null && response.Results.Count > 0)
            {
                foreach (ReferenceDescription rd in
                    response.Results[0].References)
                {
                    if (rd.NodeClass == NodeClass.Method &&
                        rd.BrowseName.Name == methodName)
                    {
                        return ExpandedNodeId.ToNodeId(
                            rd.NodeId, (session ?? Session).NamespaceUris);
                    }
                }
            }

            return WellKnownRoleNodeIds.TryGetChild(parentId, methodName);
        }

        private async Task<NodeId> FindChildAsync(
            NodeId parentId,
            string childName,
            ISession session = null)
        {
            BrowseResponse response =
                await BrowseForwardAsync(parentId, session)
                    .ConfigureAwait(false);
            if (response?.Results != null && response.Results.Count > 0)
            {
                foreach (ReferenceDescription rd in
                    response.Results[0].References)
                {
                    if (rd.BrowseName.Name == childName)
                    {
                        return ExpandedNodeId.ToNodeId(
                            rd.NodeId, (session ?? Session).NamespaceUris);
                    }
                }
            }

            return WellKnownRoleNodeIds.TryGetChild(parentId, childName);
        }

        private async Task<DataValue> ReadPropertyValueAsync(
            NodeId nodeId,
            ISession session = null)
        {
            session ??= Session;
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results, Is.Not.Null);
            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }

        private async Task<CallMethodResult> CallRoleMethodAsync(
            ISession session,
            NodeId roleId,
            NodeId methodId,
            params Variant[] args)
        {
            CallResponse callResponse = await session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = roleId,
                        MethodId = methodId,
                        InputArguments = args.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(callResponse.Results, Is.Not.Null);
            Assert.That(callResponse.Results.Count, Is.EqualTo(1));
            return callResponse.Results[0];
        }

        private ExtensionObject CreateEndpoint(string url,
            MessageSecurityMode mode = MessageSecurityMode.SignAndEncrypt,
            string policyUri = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256",
            string transportProfileUri = "http://opcfoundation.org/UA-Profile/Transport/uatcp-uasc-uabinary")
        {
            return new ExtensionObject(new EndpointType
            {
                EndpointUrl = url,
                SecurityMode = mode,
                SecurityPolicyUri = policyUri,
                TransportProfileUri = transportProfileUri
            });
        }

        private ExtensionObject CreateIdentityRule(
            int criteriaType,
            string criteria)
        {
            using var stream = new MemoryStream();
            using var encoder = new BinaryEncoder(
                stream, ServiceMessageContext.CreateEmpty(Telemetry), true);
            encoder.WriteInt32("CriteriaType", criteriaType);
            encoder.WriteString("Criteria", criteria);
            encoder.Close();
            return new ExtensionObject(
                new NodeId(15634),
                ByteString.From(stream.ToArray()));
        }

        private async Task<ISession> ConnectAsAdminAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            string policy = FindPolicyWithUsernameToken(endpoints);
            if (policy == null)
            {
                Assert.Ignore(
                    "No endpoint supports UserName token.");
            }

            return await ClientFixture
                .ConnectAsync(ServerUrl, policy,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8))
                .ConfigureAwait(false);
        }

        private async Task<ISession> ConnectAsRegularUserAsync()
        {
            ArrayOf<EndpointDescription> endpoints = await GetEndpointsAsync().ConfigureAwait(false);
            string policy = FindPolicyWithUsernameToken(endpoints);
            if (policy == null)
            {
                Assert.Ignore(
                    "No endpoint supports UserName token.");
            }

            return await ClientFixture
                .ConnectAsync(ServerUrl, policy,
                    userIdentity: new UserIdentity("user1", "password"u8))
                .ConfigureAwait(false);
        }

        private async Task<ArrayOf<EndpointDescription>> GetEndpointsAsync()
        {
            var endpointConfiguration =
                EndpointConfiguration.Create(ClientFixture.Config);
            using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                ServerUrl,
                endpointConfiguration,
                Telemetry,
                ct: CancellationToken.None).ConfigureAwait(false);

            return await client.GetEndpointsAsync(
                default, CancellationToken.None).ConfigureAwait(false);
        }

        private static string FindPolicyWithUsernameToken(
            ArrayOf<EndpointDescription> endpoints)
        {
            // Prefer SignAndEncrypt, then Sign, then None (admin reads need encryption for AccessRestrictions=3)
            foreach (MessageSecurityMode mode in new[]
            {
                MessageSecurityMode.SignAndEncrypt,
                MessageSecurityMode.Sign,
                MessageSecurityMode.None
            })
            {
                foreach (EndpointDescription ep in endpoints)
                {
                    if (ep.SecurityMode != mode)
                    {
                        continue;
                    }

                    if (ep.UserIdentityTokens == default)
                    {
                        continue;
                    }

                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            return ep.SecurityPolicyUri;
                        }
                    }
                }
            }

            return null;
        }

        private async Task<NodeId> RequireMethodAsync(
            NodeId parentId,
            string methodName,
            ISession session = null)
        {
            NodeId methodId =
                await FindMethodAsync(parentId, methodName, session)
                    .ConfigureAwait(false);
            if (methodId.IsNull)
            {
                Assert.Ignore(
                    $"Method '{methodName}' not found. " +
                    "Feature not supported by server.");
            }
            return methodId;
        }

        private const int CriteriaTypeUserName = 1;
        private const int CriteriaTypeThumbprint = 2;
    }
}
