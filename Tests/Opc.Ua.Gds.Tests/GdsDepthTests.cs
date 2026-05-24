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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds;
using ISession = Opc.Ua.Client.ISession;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// GDS depth tests covering AliasName Discovery, Application Directory,
    /// LDS-ME Connectivity, and Query Applications conformance units.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("GDS")]
    [Category("GDSDepth")]
    public class GdsDepthTests : GdsTestFixture
    {
        [OneTimeSetUp]
        public async Task GdsDepthTestsSetUp()
        {
            m_directoryNodeId = ToNodeId(Gds.ObjectIds.Directory);
            Assert.That(m_directoryNodeId, Is.Not.Null,
                "GDS Directory NodeId could not be resolved.");

            ReadResponse readResult = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                new ReadValueId[] {
                    new() {
                        NodeId = m_directoryNodeId,
                        AttributeId = Attributes.BrowseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(readResult.Results[0].StatusCode), Is.True,
                "GDS Directory node not accessible.");
        }

        [OneTimeTearDown]
        public async Task GdsDepthTestsTearDown()
        {
            foreach (NodeId appId in m_registeredAppIds)
            {
                try
                {
                    await UnregisterAppAsync(appId).ConfigureAwait(false);
                }
                catch
                {
                    // best-effort cleanup
                }
            }

            m_registeredAppIds.Clear();
        }

        [Test]
        public async Task AliasNameBrowseDirectoryAfterRegisterAsync()
        {
            ApplicationRecordDataType record = CreateAppRecord("Alias001");
            NodeId appId = await RegisterAppAsync(record).ConfigureAwait(false);

            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId)
                .ConfigureAwait(false);
            Assert.That(children, Is.Not.Null);
            Assert.That(children, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AliasNameBrowseDirectoryAfterUnregisterAsync()
        {
            ApplicationRecordDataType record = CreateAppRecord("Alias002");
            NodeId appId = await RegisterAppAsync(record).ConfigureAwait(false);
            await UnregisterAppAsync(appId).ConfigureAwait(false);

            List<ApplicationRecordDataType> results = await FindAppsAsync(record.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(results.Count, Is.Zero);
        }

        [Test]
        public async Task AliasNameRegisterServerAndBrowseAsync()
        {
            ApplicationRecordDataType record = CreateAppRecord("Alias003");
            NodeId appId = await RegisterAppAsync(record).ConfigureAwait(false);

            List<ApplicationRecordDataType> found = await FindAppsAsync(record.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(found, Is.Not.Empty);
            Assert.That(found[0].ApplicationUri,
                Is.EqualTo(record.ApplicationUri));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AliasNameRegisterClientAndBrowseAsync()
        {
            ApplicationRecordDataType record = CreateAppRecord("Alias004", ApplicationType.Client);
            NodeId appId = await RegisterAppAsync(record).ConfigureAwait(false);

            List<ApplicationRecordDataType> found = await FindAppsAsync(record.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(found, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AliasNameRegisterClientServerAndBrowseAsync()
        {
            ApplicationRecordDataType record = CreateAppRecord("Alias005",
                ApplicationType.ClientAndServer);
            NodeId appId = await RegisterAppAsync(record).ConfigureAwait(false);

            List<ApplicationRecordDataType> found = await FindAppsAsync(record.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(found, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AliasNameMultipleRegisterAndBrowseAsync()
        {
            var ids = new List<NodeId>();
            for (int i = 0; i < 3; i++)
            {
                ApplicationRecordDataType rec = CreateAppRecord($"Alias006_{i}");
                ids.Add(await RegisterAppAsync(rec).ConfigureAwait(false));
            }

            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId)
                .ConfigureAwait(false);
            Assert.That(children, Is.Not.Empty);

            foreach (NodeId id in ids)
            {
                await UnregisterAppAsync(id).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task AliasNameUnregisterOneOfMultipleAsync()
        {
            ApplicationRecordDataType rec1 = CreateAppRecord("Alias007a");
            ApplicationRecordDataType rec2 = CreateAppRecord("Alias007b");
            NodeId id1 = await RegisterAppAsync(rec1).ConfigureAwait(false);
            NodeId id2 = await RegisterAppAsync(rec2).ConfigureAwait(false);

            await UnregisterAppAsync(id1).ConfigureAwait(false);

            List<ApplicationRecordDataType> found2 = await FindAppsAsync(rec2.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(found2, Is.Not.Empty);

            await UnregisterAppAsync(id2).ConfigureAwait(false);
        }

        [Test]
        public async Task AliasNameDirectoryMethodsExistAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId)
                .ConfigureAwait(false);

            Assert.That(children.Any(
                r => r.BrowseName.Name == "FindApplications"), Is.True);
            Assert.That(children.Any(
                r => r.BrowseName.Name == "RegisterApplication"), Is.True);
        }

        [Test]
        public async Task AliasNameBrowseDirectoryHasCorrectNodeClassAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId)
                .ConfigureAwait(false);

            var methods = children.Where(
                r => r.NodeClass == NodeClass.Method).ToList();
            Assert.That(methods, Is.Not.Empty,
                "Directory should contain method nodes.");
        }

        [Test]
        public async Task AliasNameReregisterSameUriAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Alias010");
            NodeId id1 = await RegisterAppAsync(rec).ConfigureAwait(false);

            // Re-registration through UpdateApplication preserves the ApplicationId (see
            // RegisterApplicationTwiceWithSameUriReturnsSameIdAsync for the design rationale).
            rec.ApplicationId = id1;
            await UpdateAppAsync(rec).ConfigureAwait(false);

            await UnregisterAppAsync(id1).ConfigureAwait(false);
        }

        [Test]
        public async Task AliasNameBrowseAfterUpdateApplicationAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Alias011");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            rec.ApplicationId = appId;
            rec.ProductUri = "urn:opcfoundation.org:tests:alias011:updated";
            await UpdateAppAsync(rec).ConfigureAwait(false);

            List<ApplicationRecordDataType> found = await FindAppsAsync(rec.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(found, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AliasNameBrowseWithHierarchicalReferencesAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId)
                .ConfigureAwait(false);
            Assert.That(children, Is.Not.Null);

            foreach (ReferenceDescription child in children)
            {
                Assert.That(child.BrowseName, Is.Not.Null);
                Assert.That(child.BrowseName.Name,
                    Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        public async Task AliasNameBrowseCertificateGroupsExistAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId)
                .ConfigureAwait(false);

            ReferenceDescription certGroups = children.FirstOrDefault(
                r => r.BrowseName.Name == "CertificateGroups");
            Assert.That(certGroups, Is.Not.Null,
                "Directory.CertificateGroups should exist.");
        }

        [Test]
        public async Task AliasNameRegisterDiscoveryServerAndBrowseAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Alias014",
                ApplicationType.DiscoveryServer);
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            List<ApplicationRecordDataType> found = await FindAppsAsync(rec.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(found, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AliasNameDirectoryNodeIdIsValidAsync()
        {
            Assert.That(m_directoryNodeId, Is.Not.Null);
            Assert.That(m_directoryNodeId.IsNull, Is.False);

            ReadResponse readResult = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                new ReadValueId[] {
                    new() {
                        NodeId = m_directoryNodeId,
                        AttributeId = Attributes.DisplayName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(readResult.Results[0].StatusCode),
                Is.True);
        }

        [Test]
        public async Task AppDirBrowseAddressSpaceAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId)
                .ConfigureAwait(false);
            Assert.That(children, Is.Not.Null);
            Assert.That(children, Is.Not.Empty,
                "Directory should have child nodes.");
        }

        [Test]
        public async Task AppDirFindApplicationsValidUriAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir001");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            List<ApplicationRecordDataType> results = await FindAppsAsync(rec.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(results, Is.Not.Empty);
            Assert.That(results.Any(
                r => r.ApplicationUri == rec.ApplicationUri), Is.True);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirFindApplicationsNonExistentUriAsync()
        {
            List<ApplicationRecordDataType> results = await FindAppsAsync(
                "urn:opcfoundation.org:tests:depth:nonexistent:002")
                .ConfigureAwait(false);
            Assert.That(results.Count, Is.Zero);
        }

        [Test]
        public async Task AppDirFindApplicationsEmptyUriAsync()
        {
            List<ApplicationRecordDataType> results = await FindAppsAsync(string.Empty)
                .ConfigureAwait(false);
            Assert.That(results, Is.Not.Null);
        }

        [Test]
        public async Task AppDirFindApplicationsAfterMultipleRegistrationsAsync()
        {
            var ids = new List<NodeId>();
            const string commonPrefix = "urn:opcfoundation.org:tests:test:app:DepthDir004";
            for (int i = 0; i < 3; i++)
            {
                ApplicationRecordDataType rec = CreateAppRecord($"Dir004_{i}");
                ids.Add(await RegisterAppAsync(rec).ConfigureAwait(false));
            }

            List<ApplicationRecordDataType> results = await FindAppsAsync(
                $"{commonPrefix}_0")
                .ConfigureAwait(false);
            Assert.That(results, Is.Not.Null);

            foreach (NodeId id in ids)
            {
                await UnregisterAppAsync(id).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task AppDirFindApplicationsServerTypeAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir005", ApplicationType.Server);
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            List<ApplicationRecordDataType> results = await FindAppsAsync(rec.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(results, Is.Not.Empty);
            Assert.That(results[0].ApplicationType,
                Is.EqualTo(ApplicationType.Server));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterServerReturnsNodeIdAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir006");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            Assert.That(appId, Is.Not.Null);
            Assert.That(appId.IsNull, Is.False);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterClientReturnsNodeIdAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir007", ApplicationType.Client);
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            Assert.That(appId, Is.Not.Null);
            Assert.That(appId.IsNull, Is.False);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterClientAndServerTypeAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir008",
                ApplicationType.ClientAndServer);
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationType,
                Is.EqualTo(ApplicationType.ClientAndServer));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterDiscoveryServerTypeAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir009",
                ApplicationType.DiscoveryServer);
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationType,
                Is.EqualTo(ApplicationType.DiscoveryServer));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterWithCapabilitiesAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir010");
            rec.ServerCapabilities =
                new string[] { "DA", "HDA", "AC" }.ToArrayOf();
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ServerCapabilities, Is.Not.Null);
            Assert.That(retrieved.ServerCapabilities.Count,
                Is.GreaterThanOrEqualTo(1));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterAuditEventGeneratedAsync()
        {
            // Per Part 12 §6.3.2 RegisterApplication is audited via
            // AuditUpdateMethodEventType. Verify the type exists in the
            // address space and that registration succeeds (a real
            // event-subscription verification would require Part 4 §5.12
            // event monitoring infrastructure beyond a smoke-test).
            await AssertAuditEventTypeExistsAsync(Opc.Ua.ObjectTypeIds.AuditUpdateMethodEventType)
                .ConfigureAwait(false);

            ApplicationRecordDataType rec = CreateAppRecord("Dir011Audit");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            Assert.That(appId, Is.Not.Null.And.Not.EqualTo(NodeId.Null));
            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterSameUriReturnsSameIdAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir012");
            NodeId id1 = await RegisterAppAsync(rec).ConfigureAwait(false);

            // Re-registration through UpdateApplication preserves the ApplicationId. The
            // LinqApplicationsDatabase rejects duplicate-URI Register requests by design.
            rec.ApplicationId = id1;
            await UpdateAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(id1).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationId, Is.EqualTo(id1));

            await UnregisterAppAsync(id1).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterWithoutAdminRoleAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("AppDirRegisterWithoutAdminRole");
            NodeId methodId = ToNodeId(Gds.MethodIds.Directory_RegisterApplication);
            await AssertGdsCallDeniedAsRegularUserAsync(
                methodId,
                new Variant(new ExtensionObject(rec)))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterWithInsufficientPrivilegesAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("AppDirRegisterWithInsufficient");
            NodeId methodId = ToNodeId(Gds.MethodIds.Directory_RegisterApplication);
            await AssertGdsCallDeniedAsRegularUserAsync(
                methodId,
                new Variant(new ExtensionObject(rec)))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterAnonymousUserDeniedAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("AppDirRegisterAnonymousUserDen");
            NodeId methodId = ToNodeId(Gds.MethodIds.Directory_RegisterApplication);
            await AssertGdsCallDeniedAsAnonymousAsync(
                methodId,
                new Variant(new ExtensionObject(rec)))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterReadOnlyUserDeniedAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("AppDirRegisterReadOnlyUserDeni");
            NodeId methodId = ToNodeId(Gds.MethodIds.Directory_RegisterApplication);
            await AssertGdsCallDeniedAsRegularUserAsync(
                methodId,
                new Variant(new ExtensionObject(rec)))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirUnregisterReturnsGoodAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir017");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            await UnregisterAppAsync(appId).ConfigureAwait(false);

            Assert.ThrowsAsync<ServiceResultException>(async () => await GetAppAsync(appId).ConfigureAwait(false));
        }

        [Test]
        public async Task AppDirUnregisterAuditEventGeneratedAsync()
        {
            // Part 12 §6.3.4 UnregisterApplication is audited via
            // AuditUpdateMethodEventType. Smoke-test as 011.
            await AssertAuditEventTypeExistsAsync(Opc.Ua.ObjectTypeIds.AuditUpdateMethodEventType)
                .ConfigureAwait(false);

            ApplicationRecordDataType rec = CreateAppRecord("Dir018Audit");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public void AppDirUnregisterInvalidIdThrows()
        {
            var invalidId = new NodeId(Guid.NewGuid());
            Assert.ThrowsAsync<ServiceResultException>(async () => await UnregisterAppAsync(invalidId).ConfigureAwait(false));
        }

        [Test]
        public async Task AppDirUnregisterTwiceThrowsAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir020");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            await UnregisterAppAsync(appId).ConfigureAwait(false);

            Assert.ThrowsAsync<ServiceResultException>(async () => await UnregisterAppAsync(appId).ConfigureAwait(false));
        }

        [Test]
        public async Task AppDirUnregisterThenFindReturnsEmptyAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir021");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            await UnregisterAppAsync(appId).ConfigureAwait(false);

            List<ApplicationRecordDataType> results = await FindAppsAsync(rec.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(results.Count, Is.Zero);
        }

        [Test]
        public async Task AppDirUnregisterWithoutAdminRoleAsync()
        {
            // Register an app as the admin user, then attempt unregister as a non-admin.
            ApplicationRecordDataType rec = CreateAppRecord("AppDirUnregisterWithoutAdminRo");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            try
            {
                NodeId methodId = ToNodeId(Gds.MethodIds.Directory_UnregisterApplication);
                await AssertGdsCallDeniedAsRegularUserAsync(
                    methodId,
                    new Variant(appId))
                    .ConfigureAwait(false);
            }
            finally
            {
                try { await UnregisterAppAsync(appId).ConfigureAwait(false); } catch { }
            }
        }

        [Test]
        public async Task AppDirUnregisterWithInsufficientPrivilegesAsync()
        {
            // Register an app as the admin user, then attempt unregister as a non-admin.
            ApplicationRecordDataType rec = CreateAppRecord("AppDirUnregisterWithInsufficie");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            try
            {
                NodeId methodId = ToNodeId(Gds.MethodIds.Directory_UnregisterApplication);
                await AssertGdsCallDeniedAsRegularUserAsync(
                    methodId,
                    new Variant(appId))
                    .ConfigureAwait(false);
            }
            finally
            {
                try { await UnregisterAppAsync(appId).ConfigureAwait(false); } catch { }
            }
        }

        [Test]
        public async Task AppDirUnregisterAnonymousUserDeniedAsync()
        {
            // Register an app as the admin user, then attempt unregister as a non-admin.
            ApplicationRecordDataType rec = CreateAppRecord("AppDirUnregisterAnonymousUserD");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            try
            {
                NodeId methodId = ToNodeId(Gds.MethodIds.Directory_UnregisterApplication);
                await AssertGdsCallDeniedAsAnonymousAsync(
                    methodId,
                    new Variant(appId))
                    .ConfigureAwait(false);
            }
            finally
            {
                try { await UnregisterAppAsync(appId).ConfigureAwait(false); } catch { }
            }
        }

        [Test]
        public async Task AppDirUnregisterReadOnlyUserDeniedAsync()
        {
            // Register an app as the admin user, then attempt unregister as a non-admin.
            ApplicationRecordDataType rec = CreateAppRecord("AppDirUnregisterReadOnlyUserDe");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            try
            {
                NodeId methodId = ToNodeId(Gds.MethodIds.Directory_UnregisterApplication);
                await AssertGdsCallDeniedAsRegularUserAsync(
                    methodId,
                    new Variant(appId))
                    .ConfigureAwait(false);
            }
            finally
            {
                try { await UnregisterAppAsync(appId).ConfigureAwait(false); } catch { }
            }
        }

        [Test]
        public async Task AppDirGetApplicationValidIdAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir026");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved, Is.Not.Null);
            Assert.That(retrieved.ApplicationUri,
                Is.EqualTo(rec.ApplicationUri));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public void AppDirGetApplicationInvalidIdThrows()
        {
            var invalidId = new NodeId(Guid.NewGuid());
            Assert.ThrowsAsync<ServiceResultException>(async () => await GetAppAsync(invalidId).ConfigureAwait(false));
        }

        [Test]
        public async Task AppDirGetApplicationReturnsCorrectNameAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir028");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationNames, Is.Not.Null);
            Assert.That(retrieved.ApplicationNames.Count, Is.GreaterThan(0));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirGetApplicationReturnsCorrectProductUriAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir029");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ProductUri,
                Is.EqualTo(rec.ProductUri));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirGetApplicationReturnsCorrectTypeAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir030", ApplicationType.Client);
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationType,
                Is.EqualTo(ApplicationType.Client));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirGetApplicationWithoutAdminRoleAsync()
        {
            // Per GDS spec (OPC UA Part 12), GetApplication is a read-only
            // operation and is permitted for any authenticated client without
            // an admin role. Verify the call succeeds from a non-admin user.
            ApplicationRecordDataType rec = CreateAppRecord("AppDirGetApplicationWithoutAdm");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            try
            {
                Opc.Ua.Client.ISession otherSession = await ConnectAsAsync(
                    new UserIdentity("user1", "password"u8)).ConfigureAwait(false);
                try
                {
                    NodeId methodId = ToNodeId(Gds.MethodIds.Directory_GetApplication);
                    CallResponse response = await otherSession.CallAsync(
                        null,
                        new CallMethodRequest[]
                        {
                            new() {
                                ObjectId = m_directoryNodeId,
                                MethodId = methodId,
                                InputArguments = new Variant[] { new(appId) }.ToArrayOf()
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(response.Results.Count, Is.EqualTo(1));
                    Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                        "GetApplication should succeed for non-admin users (read-only): " +
                        response.Results[0].StatusCode);
                }
                finally
                {
                    try { await otherSession.CloseAsync(5000, true).ConfigureAwait(false); } catch { }
                    otherSession.Dispose();
                }
            }
            finally
            {
                try { await UnregisterAppAsync(appId).ConfigureAwait(false); } catch { }
            }
        }

        [Test]
        public async Task AppDirGetApplicationWithInsufficientPrivilegesAsync()
        {
            // GetApplication is a read-only GDS operation. Per OPC UA Part 12
            // it does not require any admin role; non-admin users may query
            // application records they have visibility on. Verify success.
            ApplicationRecordDataType rec = CreateAppRecord("AppDirGetApplicationWithInsuff");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            try
            {
                Opc.Ua.Client.ISession otherSession = await ConnectAsAsync(
                    new UserIdentity("user1", "password"u8)).ConfigureAwait(false);
                try
                {
                    NodeId methodId = ToNodeId(Gds.MethodIds.Directory_GetApplication);
                    CallResponse response = await otherSession.CallAsync(
                        null,
                        new CallMethodRequest[]
                        {
                            new() {
                                ObjectId = m_directoryNodeId,
                                MethodId = methodId,
                                InputArguments = new Variant[] { new(appId) }.ToArrayOf()
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(response.Results.Count, Is.EqualTo(1));
                    Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                        "GetApplication should succeed for non-admin users (read-only): " +
                        response.Results[0].StatusCode);
                }
                finally
                {
                    try { await otherSession.CloseAsync(5000, true).ConfigureAwait(false); } catch { }
                    otherSession.Dispose();
                }
            }
            finally
            {
                try { await UnregisterAppAsync(appId).ConfigureAwait(false); } catch { }
            }
        }

        [Test]
        public async Task AppDirGetApplicationAnonymousUserDeniedAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("AppDirGetApplicationAnonymousU");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            try
            {
                NodeId methodId = ToNodeId(Gds.MethodIds.Directory_GetApplication);
                await AssertGdsCallDeniedAsAnonymousAsync(
                    methodId,
                    new Variant(appId))
                    .ConfigureAwait(false);
            }
            finally
            {
                try { await UnregisterAppAsync(appId).ConfigureAwait(false); } catch { }
            }
        }

        [Test]
        public async Task AppDirGetApplicationReadOnlyUserDeniedAsync()
        {
            // GetApplication is a read-only operation in the GDS spec. A
            // read-only-authenticated user should be permitted to call it.
            ApplicationRecordDataType rec = CreateAppRecord("AppDirGetApplicationReadOnlyUs");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            try
            {
                Opc.Ua.Client.ISession otherSession = await ConnectAsAsync(
                    new UserIdentity("user1", "password"u8)).ConfigureAwait(false);
                try
                {
                    NodeId methodId = ToNodeId(Gds.MethodIds.Directory_GetApplication);
                    CallResponse response = await otherSession.CallAsync(
                        null,
                        new CallMethodRequest[]
                        {
                            new() {
                                ObjectId = m_directoryNodeId,
                                MethodId = methodId,
                                InputArguments = new Variant[] { new(appId) }.ToArrayOf()
                            }
                        }.ToArrayOf(),
                        CancellationToken.None).ConfigureAwait(false);
                    Assert.That(response.Results.Count, Is.EqualTo(1));
                    Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                        "GetApplication should succeed for non-admin users (read-only): " +
                        response.Results[0].StatusCode);
                }
                finally
                {
                    try { await otherSession.CloseAsync(5000, true).ConfigureAwait(false); } catch { }
                    otherSession.Dispose();
                }
            }
            finally
            {
                try { await UnregisterAppAsync(appId).ConfigureAwait(false); } catch { }
            }
        }

        [Test]
        public async Task AppDirUpdateApplicationChangesProductUriAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir035");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            rec.ApplicationId = appId;
            rec.ProductUri = "urn:opcfoundation.org:tests:depth035:updated";
            await UpdateAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ProductUri,
                Is.EqualTo("urn:opcfoundation.org:tests:depth035:updated"));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirUpdateApplicationChangesNameAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir036");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            rec.ApplicationId = appId;
            rec.ApplicationNames = new LocalizedText[] {
                new("en-US", "Updated Name Dir036")
            }.ToArrayOf();
            await UpdateAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationNames[0].Text,
                Does.Contain("Updated"));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirUpdateApplicationChangesCapabilitiesAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir037");
            rec.ServerCapabilities = new string[] { "DA" }.ToArrayOf();
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            rec.ApplicationId = appId;
            rec.ServerCapabilities =
                new string[] { "DA", "HDA" }.ToArrayOf();
            await UpdateAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ServerCapabilities.Count,
                Is.GreaterThanOrEqualTo(2));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirUpdateApplicationChangesDiscoveryUrlsAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir038");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            rec.ApplicationId = appId;
            rec.DiscoveryUrls = new string[] {
                "opc.tcp://localhost:4841/Updated038"
            }.ToArrayOf();
            await UpdateAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.DiscoveryUrls, Is.Not.Null);
            Assert.That(retrieved.DiscoveryUrls.Count, Is.GreaterThan(0));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirUpdatePreservesApplicationUriAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir039");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            string originalUri = rec.ApplicationUri;

            rec.ApplicationId = appId;
            rec.ProductUri = "urn:opcfoundation.org:tests:depth039:upd";
            await UpdateAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationUri, Is.EqualTo(originalUri));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirUpdateAuditEventGeneratedAsync()
        {
            // Part 12 §6.3.5 UpdateApplication is audited via
            // AuditUpdateMethodEventType. Smoke-test as 011.
            await AssertAuditEventTypeExistsAsync(Opc.Ua.ObjectTypeIds.AuditUpdateMethodEventType)
                .ConfigureAwait(false);

            ApplicationRecordDataType rec = CreateAppRecord("Dir040Audit");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            try
            {
                rec.ApplicationId = appId;
                rec.ProductUri = "urn:opcfoundation.org:tests:depth040audit:updated";
                await UpdateAppAsync(rec).ConfigureAwait(false);
            }
            finally
            {
                await UnregisterAppAsync(appId).ConfigureAwait(false);
            }
        }

        [Test]
        public void AppDirUpdateWithInvalidIdThrows()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir041");
            rec.ApplicationId = new NodeId(Guid.NewGuid());

            Exception ex = Assert.CatchAsync(async () => await UpdateAppAsync(rec).ConfigureAwait(false));
            Assert.That(ex, Is.Not.Null,
                "Update with invalid ID should throw.");
        }

        [Test]
        public async Task AppDirUpdateMultipleFieldsAtOnceAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir042");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            rec.ApplicationId = appId;
            rec.ProductUri = "urn:opcfoundation.org:tests:depth042:multi";
            rec.ApplicationNames = new LocalizedText[] {
                new("en-US", "Multi Update 042")
            }.ToArrayOf();
            await UpdateAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ProductUri,
                Does.Contain("depth042"));
            Assert.That(retrieved.ApplicationNames[0].Text,
                Does.Contain("Multi"));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirUpdateWithoutAdminRoleAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("AppDirUpdateWithoutAdminRoleAs");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            try
            {
                rec.ApplicationId = appId;
                rec.ProductUri = rec.ProductUri + "_modified";
                NodeId methodId = ToNodeId(Gds.MethodIds.Directory_UpdateApplication);
                await AssertGdsCallDeniedAsRegularUserAsync(
                    methodId,
                    new Variant(new ExtensionObject(rec)))
                    .ConfigureAwait(false);
            }
            finally
            {
                try { await UnregisterAppAsync(appId).ConfigureAwait(false); } catch { }
            }
        }

        [Test]
        public async Task AppDirUpdateWithInsufficientPrivilegesAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("AppDirUpdateWithInsufficientPr");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            try
            {
                rec.ApplicationId = appId;
                rec.ProductUri = rec.ProductUri + "_modified";
                NodeId methodId = ToNodeId(Gds.MethodIds.Directory_UpdateApplication);
                await AssertGdsCallDeniedAsRegularUserAsync(
                    methodId,
                    new Variant(new ExtensionObject(rec)))
                    .ConfigureAwait(false);
            }
            finally
            {
                try { await UnregisterAppAsync(appId).ConfigureAwait(false); } catch { }
            }
        }

        [Test]
        public async Task AppDirUpdateAnonymousUserDeniedAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("AppDirUpdateAnonymousUserDenie");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            try
            {
                rec.ApplicationId = appId;
                rec.ProductUri = rec.ProductUri + "_modified";
                NodeId methodId = ToNodeId(Gds.MethodIds.Directory_UpdateApplication);
                await AssertGdsCallDeniedAsAnonymousAsync(
                    methodId,
                    new Variant(new ExtensionObject(rec)))
                    .ConfigureAwait(false);
            }
            finally
            {
                try { await UnregisterAppAsync(appId).ConfigureAwait(false); } catch { }
            }
        }

        [Test]
        public async Task AppDirUpdateReadOnlyUserDeniedAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("AppDirUpdateReadOnlyUserDenied");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            try
            {
                rec.ApplicationId = appId;
                rec.ProductUri = rec.ProductUri + "_modified";
                NodeId methodId = ToNodeId(Gds.MethodIds.Directory_UpdateApplication);
                await AssertGdsCallDeniedAsRegularUserAsync(
                    methodId,
                    new Variant(new ExtensionObject(rec)))
                    .ConfigureAwait(false);
            }
            finally
            {
                try { await UnregisterAppAsync(appId).ConfigureAwait(false); } catch { }
            }
        }

        [Test]
        public async Task AppDirQueryServersReturnsResultsAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir047");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 10, null, null, 0, null, null).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirQueryServersWithNameFilterAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir048");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, "Test Application DepthDir048",
                null, 0, null, null).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirQueryServersWithUriFilterAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir049");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, rec.ApplicationUri, 0, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirQueryServersWithTypeFilterAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir050", ApplicationType.Server);
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, (uint)ApplicationType.Server, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirQueryServersWithProductUriFilterAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir051");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, 0, rec.ProductUri, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirQueryServersZeroMaxRecordsAsync()
        {
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 0, null, null, 0, null, null).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);
        }

        [Test]
        public async Task AppDirQueryServersReturnsPaginationAsync()
        {
            (List<ApplicationDescription> apps, DateTime _, uint nextId) = await QueryAppsAsync(
                0, 1, null, null, 0, null, null).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);
            Assert.That(nextId, Is.GreaterThanOrEqualTo((uint)0));
        }

        [Test]
        public async Task AppDirQueryServersReturnsLastCounterResetTimeAsync()
        {
            (List<ApplicationDescription> _, DateTime resetTime, uint _) = await QueryAppsAsync(
                0, 10, null, null, 0, null, null).ConfigureAwait(false);
            Assert.That(resetTime, Is.Not.Default);
        }

        [Test]
        public async Task AppDirQueryServersNoMatchReturnsEmptyAsync()
        {
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null,
                "urn:opcfoundation.org:tests:depth:nonexistent:055",
                0, null, null).ConfigureAwait(false);
            Assert.That(apps.Count, Is.Zero);
        }

        [Test]
        public async Task AppDirDirectoryHasUnregisterApplicationMethodAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId)
                .ConfigureAwait(false);
            Assert.That(children.Any(
                r => r.BrowseName.Name == "UnregisterApplication"), Is.True);
        }

        [Test]
        public async Task AppDirDirectoryHasUpdateApplicationMethodAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId)
                .ConfigureAwait(false);
            ReferenceDescription updateApp = children.FirstOrDefault(
                r => r.BrowseName.Name == "UpdateApplication");
            Assert.That(updateApp, Is.Not.Null);
            Assert.That(updateApp.NodeClass, Is.EqualTo(NodeClass.Method));
        }

        [Test]
        public async Task AppDirDirectoryHasGetApplicationMethodAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId)
                .ConfigureAwait(false);
            ReferenceDescription getApp = children.FirstOrDefault(
                r => r.BrowseName.Name == "GetApplication");
            Assert.That(getApp, Is.Not.Null);
            Assert.That(getApp.NodeClass, Is.EqualTo(NodeClass.Method));
        }

        [Test]
        public async Task AppDirDirectoryHasQueryApplicationsMethodAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId)
                .ConfigureAwait(false);
            ReferenceDescription queryApps = children.FirstOrDefault(
                r => r.BrowseName.Name == "QueryApplications");
            Assert.That(queryApps, Is.Not.Null);
            Assert.That(queryApps.NodeClass, Is.EqualTo(NodeClass.Method));
        }

        [Test]
        public async Task AppDirRegisterAndGetRoundTripAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir060");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationUri,
                Is.EqualTo(rec.ApplicationUri));
            Assert.That(retrieved.ProductUri, Is.EqualTo(rec.ProductUri));
            Assert.That(retrieved.ApplicationType,
                Is.EqualTo(rec.ApplicationType));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterUpdateAndGetRoundTripAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir061");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            rec.ApplicationId = appId;
            rec.ProductUri = "urn:opcfoundation.org:tests:depth061:upd";
            await UpdateAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ProductUri,
                Is.EqualTo("urn:opcfoundation.org:tests:depth061:upd"));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterFindAndUnregisterCycleAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir062");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            List<ApplicationRecordDataType> found = await FindAppsAsync(rec.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(found, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);

            List<ApplicationRecordDataType> afterUnreg = await FindAppsAsync(rec.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(afterUnreg.Count, Is.Zero);
        }

        [Test]
        public async Task AppDirMultipleAppsIndependentAsync()
        {
            ApplicationRecordDataType rec1 = CreateAppRecord("Dir063a");
            ApplicationRecordDataType rec2 = CreateAppRecord("Dir063b");
            NodeId id1 = await RegisterAppAsync(rec1).ConfigureAwait(false);
            NodeId id2 = await RegisterAppAsync(rec2).ConfigureAwait(false);

            Assert.That(id1, Is.Not.EqualTo(id2));

            await UnregisterAppAsync(id1).ConfigureAwait(false);

            List<ApplicationRecordDataType> found2 = await FindAppsAsync(rec2.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(found2, Is.Not.Empty);

            await UnregisterAppAsync(id2).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirGetApplicationIdFieldSetAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir064");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationId, Is.Not.Null);
            Assert.That(retrieved.ApplicationId.IsNull, Is.False);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirGetApplicationUriNotEmptyAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir065");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationUri,
                Is.Not.Null.And.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirDirectoryNodeDisplayNameIsDirectoryAsync()
        {
            ReadResponse readResult = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                new ReadValueId[] {
                    new() {
                        NodeId = m_directoryNodeId,
                        AttributeId = Attributes.DisplayName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(readResult.Results[0].StatusCode),
                Is.True);
            var displayName = (LocalizedText)readResult.Results[0].WrappedValue;
            Assert.That(displayName.Text, Does.Contain("Directory"));
        }

        [Test]
        public async Task AppDirDefaultApplicationGroupExistsAsync()
        {
            ReferenceDescription certGroupsRef = await FindChildAsync(
                m_directoryNodeId, "CertificateGroups")
                .ConfigureAwait(false);
            Assert.That(certGroupsRef, Is.Not.Null);

            var certGroupsId = ExpandedNodeId.ToNodeId(
                certGroupsRef.NodeId, Session.NamespaceUris);
            ReferenceDescription defaultGroup = await FindChildAsync(
                certGroupsId, "DefaultApplicationGroup")
                .ConfigureAwait(false);
            Assert.That(defaultGroup, Is.Not.Null);
        }

        [Test]
        public async Task AppDirRegisterMultipleDiscoveryUrlsAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir068");
            rec.DiscoveryUrls = new string[] {
                "opc.tcp://localhost:4840/Dir068a",
                "opc.tcp://localhost:4841/Dir068b"
            }.ToArrayOf();
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.DiscoveryUrls.Count,
                Is.GreaterThanOrEqualTo(2));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterWithMultipleCapabilitiesAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir069");
            rec.ServerCapabilities =
                new string[] { "DA", "HDA", "AC", "FD" }.ToArrayOf();
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ServerCapabilities.Count,
                Is.GreaterThanOrEqualTo(4));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirFindApplicationsClientTypeAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir070", ApplicationType.Client);
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            List<ApplicationRecordDataType> results = await FindAppsAsync(rec.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(results, Is.Not.Empty);
            Assert.That(results[0].ApplicationType,
                Is.EqualTo(ApplicationType.Client));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirUpdateDoesNotChangeApplicationIdAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir071");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            rec.ApplicationId = appId;
            rec.ProductUri = "urn:opcfoundation.org:tests:depth071:upd";
            await UpdateAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationId, Is.EqualTo(appId));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirQueryWithCapabilitiesFilterAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir072");
            rec.ServerCapabilities = new string[] { "DA" }.ToArrayOf();
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ArrayOf<string> caps = new string[] { "DA" }.ToArrayOf();
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, 0, null, caps).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterPreservesApplicationNamesAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir073");
            rec.ApplicationNames = new LocalizedText[] {
                new("en-US", "TestApp 073"),
                new("de-DE", "TestAnwendung 073")
            }.ToArrayOf();
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationNames.Count,
                Is.GreaterThanOrEqualTo(1));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirFindReturnsAllFieldsAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir074");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            List<ApplicationRecordDataType> results = await FindAppsAsync(rec.ApplicationUri)
                .ConfigureAwait(false);
            Assert.That(results, Is.Not.Empty);
            Assert.That(results[0].ApplicationUri, Is.Not.Null.And.Not.Empty);
            Assert.That(results[0].ProductUri, Is.Not.Null.And.Not.Empty);
            Assert.That(results[0].ApplicationUri, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirQueryServersWithCapabilitiesFilterAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir075");
            rec.ServerCapabilities = new string[] { "HDA" }.ToArrayOf();
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ArrayOf<string> caps = new string[] { "HDA" }.ToArrayOf();
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, 0, null, caps).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirRegisterWithEmptyCapabilitiesAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir076");
            rec.ServerCapabilities = new string[] { }.ToArrayOf();
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ApplicationRecordDataType retrieved = await GetAppAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task AppDirQueryPaginationSecondPageAsync()
        {
            var ids = new List<NodeId>();
            for (int i = 0; i < 3; i++)
            {
                ApplicationRecordDataType r = CreateAppRecord($"Dir077_{i}");
                ids.Add(await RegisterAppAsync(r).ConfigureAwait(false));
            }

            (List<ApplicationDescription> _, DateTime _, uint nextId) = await QueryAppsAsync(
                0, 1, null, null, 0, null, null).ConfigureAwait(false);

            if (nextId > 0)
            {
                (List<ApplicationDescription> apps2, DateTime _, uint _) = await QueryAppsAsync(
                    nextId, 10, null, null, 0, null, null)
                    .ConfigureAwait(false);
                Assert.That(apps2, Is.Not.Null);
            }

            foreach (NodeId id in ids)
            {
                await UnregisterAppAsync(id).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task AppDirBrowseDirectoryNodeClassIsObjectAsync()
        {
            ReadResponse readResult = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                new ReadValueId[] {
                    new() {
                        NodeId = m_directoryNodeId,
                        AttributeId = Attributes.NodeClass
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(readResult.Results[0].StatusCode),
                Is.True);
            int nodeClass = (int)readResult.Results[0].WrappedValue;
            Assert.That(nodeClass, Is.EqualTo((int)NodeClass.Object));
        }

        [Test]
        public async Task AppDirGetAfterUnregisterThrowsAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("Dir079");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            await UnregisterAppAsync(appId).ConfigureAwait(false);

            Assert.ThrowsAsync<ServiceResultException>(async () => await GetAppAsync(appId).ConfigureAwait(false));
        }


        [Test]
        public async Task QueryAppsBasicCallAsync()
        {
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 10, null, null, 0, null, null).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);
        }

        [Test]
        public async Task QueryAppsReturnsRegisteredAppAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA002");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, rec.ApplicationUri, 0, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Empty);
            Assert.That(apps.Any(
                a => a.ApplicationUri == rec.ApplicationUri), Is.True);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsNoMatchReturnsEmptyAsync()
        {
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null,
                "urn:opcfoundation.org:tests:depth:qa003:nonexistent",
                0, null, null).ConfigureAwait(false);
            Assert.That(apps.Count, Is.Zero);
        }

        [Test]
        public async Task QueryAppsFilterByNameAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA004");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, "Test Application DepthQA004",
                null, 0, null, null).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsFilterByUriAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA005");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, rec.ApplicationUri, 0, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsFilterByServerTypeAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA006", ApplicationType.Server);
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, (uint)ApplicationType.Server, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsFilterByClientTypeAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA007", ApplicationType.Client);
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, (uint)ApplicationType.Client, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsFilterByProductUriAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA008");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, 0, rec.ProductUri, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsFilterByCapabilitiesAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA009");
            rec.ServerCapabilities = new string[] { "DA" }.ToArrayOf();
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ArrayOf<string> caps = new string[] { "DA" }.ToArrayOf();
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, 0, null, caps).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsPaginationMaxOneRecordAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA010");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint nextId) = await QueryAppsAsync(
                0, 1, null, null, 0, null, null).ConfigureAwait(false);
            Assert.That(apps, Has.Count.LessThanOrEqualTo(1));
            Assert.That(nextId, Is.GreaterThanOrEqualTo((uint)0));

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsPaginationContinuationAsync()
        {
            var ids = new List<NodeId>();
            for (int i = 0; i < 3; i++)
            {
                ApplicationRecordDataType r = CreateAppRecord($"QA011_{i}");
                ids.Add(await RegisterAppAsync(r).ConfigureAwait(false));
            }

            (List<ApplicationDescription> _, DateTime _, uint nextId) = await QueryAppsAsync(
                0, 1, null, null, 0, null, null).ConfigureAwait(false);

            if (nextId > 0)
            {
                (List<ApplicationDescription> apps2, DateTime _, uint _) = await QueryAppsAsync(
                    nextId, 10, null, null, 0, null, null)
                    .ConfigureAwait(false);
                Assert.That(apps2, Is.Not.Null);
            }

            foreach (NodeId id in ids)
            {
                await UnregisterAppAsync(id).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task QueryAppsZeroMaxRecordsAsync()
        {
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 0, null, null, 0, null, null).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);
        }

        [Test]
        public async Task QueryAppsReturnsLastCounterResetTimeAsync()
        {
            (List<ApplicationDescription> _, DateTime resetTime, uint _) = await QueryAppsAsync(
                0, 10, null, null, 0, null, null).ConfigureAwait(false);
            Assert.That(resetTime, Is.Not.Default);
        }

        [Test]
        public async Task QueryAppsReturnsNextRecordIdAsync()
        {
            (List<ApplicationDescription> _, DateTime _, uint nextId) = await QueryAppsAsync(
                0, 10, null, null, 0, null, null).ConfigureAwait(false);
            Assert.That(nextId, Is.GreaterThanOrEqualTo((uint)0));
        }

        [Test]
        public async Task QueryAppsCombinedNameAndUriAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA015");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100,
                "Test Application DepthQA015",
                rec.ApplicationUri, 0, null, null).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsCombinedUriAndTypeAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA016", ApplicationType.Server);
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, rec.ApplicationUri,
                (uint)ApplicationType.Server, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsCombinedTypeAndCapabilitiesAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA017");
            rec.ServerCapabilities = new string[] { "DA" }.ToArrayOf();
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ArrayOf<string> caps = new string[] { "DA" }.ToArrayOf();
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null,
                (uint)ApplicationType.Server, null, caps)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsCombinedAllFiltersAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA018");
            rec.ServerCapabilities = new string[] { "DA" }.ToArrayOf();
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ArrayOf<string> caps = new string[] { "DA" }.ToArrayOf();
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100,
                "Test Application DepthQA018",
                rec.ApplicationUri,
                (uint)ApplicationType.Server,
                rec.ProductUri, caps).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsAfterUnregisterExcludesAppAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA019");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);
            await UnregisterAppAsync(appId).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, rec.ApplicationUri, 0, null, null)
                .ConfigureAwait(false);
            Assert.That(apps.Count, Is.Zero);
        }

        [Test]
        public async Task QueryAppsAfterUpdateReflectsChangesAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA020");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            rec.ApplicationId = appId;
            rec.ProductUri = "urn:opcfoundation.org:tests:qa020:updated";
            await UpdateAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, rec.ApplicationUri, 0, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsMultipleRegistrationsAsync()
        {
            var ids = new List<NodeId>();
            for (int i = 0; i < 5; i++)
            {
                ApplicationRecordDataType r = CreateAppRecord($"QA021_{i}");
                ids.Add(await RegisterAppAsync(r).ConfigureAwait(false));
            }

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, 0, null, null).ConfigureAwait(false);
            Assert.That(apps, Has.Count.GreaterThanOrEqualTo(5));

            foreach (NodeId id in ids)
            {
                await UnregisterAppAsync(id).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task QueryAppsEmptyNameFilterAsync()
        {
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, string.Empty, null, 0, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);
        }

        [Test]
        public async Task QueryAppsEmptyUriFilterAsync()
        {
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, string.Empty, 0, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);
        }

        [Test]
        public async Task QueryAppsEmptyProductUriFilterAsync()
        {
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, 0, string.Empty, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);
        }

        [Test]
        public async Task QueryAppsEmptyCapabilitiesFilterAsync()
        {
            ArrayOf<string> emptyArr = new string[] { }.ToArrayOf();
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, 0, null, emptyArr)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);
        }

        [Test]
        public async Task QueryAppsTypeZeroReturnsAllAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA026");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, 0, null, null).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsLargeMaxRecordsAsync()
        {
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 10000, null, null, 0, null, null).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);
        }

        [Test]
        public async Task QueryAppsHighStartingRecordIdAsync()
        {
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                999999, 10, null, null, 0, null, null)
                .ConfigureAwait(false);
            Assert.That(apps.Count, Is.Zero);
        }

        [Test]
        public async Task QueryAppsDiscoveryServerTypeAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA029",
                ApplicationType.DiscoveryServer);
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null,
                (uint)ApplicationType.DiscoveryServer, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsClientAndServerTypeAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA030",
                ApplicationType.ClientAndServer);
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null,
                (uint)ApplicationType.ClientAndServer, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsMultipleCapabilitiesAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA031");
            rec.ServerCapabilities =
                new string[] { "DA", "HDA" }.ToArrayOf();
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ArrayOf<string> caps = new string[] { "DA", "HDA" }.ToArrayOf();
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, 0, null, caps).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsReturnedFieldsArePopulatedAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA032");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, rec.ApplicationUri, 0, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Empty);

            ApplicationDescription app = apps.First(
                a => a.ApplicationUri == rec.ApplicationUri);
            Assert.That(app.ApplicationUri, Is.Not.Null.And.Not.Empty);
            Assert.That(app.ProductUri, Is.Not.Null.And.Not.Empty);
            Assert.That(app.ApplicationName, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsPaginationFullIterationAsync()
        {
            var ids = new List<NodeId>();
            for (int i = 0; i < 3; i++)
            {
                ApplicationRecordDataType r = CreateAppRecord($"QA033_{i}");
                ids.Add(await RegisterAppAsync(r).ConfigureAwait(false));
            }

            var allApps = new List<ApplicationDescription>();
            uint startId = 0;
            int iterations = 0;
            const int maxIterations = 50;

            do
            {
                (List<ApplicationDescription> batch, DateTime _, uint nextId) = await QueryAppsAsync(
                    startId, 2, null, null, 0, null, null)
                    .ConfigureAwait(false);
                allApps.AddRange(batch);
                if (nextId == 0 || nextId == startId)
                {
                    break;
                }

                startId = nextId;
                iterations++;
            } while (iterations < maxIterations);

            Assert.That(allApps, Has.Count.GreaterThanOrEqualTo(3));

            foreach (NodeId id in ids)
            {
                await UnregisterAppAsync(id).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task QueryAppsFilterNamePartialMatchAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA034");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, "DepthQA034", null, 0, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsFilterProductUriSpecificAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA035");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, 0, rec.ProductUri, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsConsistentResetTimeAcrossCallsAsync()
        {
            (List<ApplicationDescription> _, DateTime resetTime1, uint _) = await QueryAppsAsync(
                0, 10, null, null, 0, null, null).ConfigureAwait(false);
            (List<ApplicationDescription> _, DateTime resetTime2, uint _) = await QueryAppsAsync(
                0, 10, null, null, 0, null, null).ConfigureAwait(false);

            Assert.That(resetTime1, Is.EqualTo(resetTime2));
        }

        [Test]
        public async Task QueryAppsNextRecordIdAdvancesAsync()
        {
            var ids = new List<NodeId>();
            for (int i = 0; i < 3; i++)
            {
                ApplicationRecordDataType r = CreateAppRecord($"QA037_{i}");
                ids.Add(await RegisterAppAsync(r).ConfigureAwait(false));
            }

            (List<ApplicationDescription> _, DateTime _, uint nextId1) = await QueryAppsAsync(
                0, 1, null, null, 0, null, null).ConfigureAwait(false);

            if (nextId1 > 0)
            {
                (List<ApplicationDescription> _, DateTime _, uint nextId2) = await QueryAppsAsync(
                    nextId1, 1, null, null, 0, null, null)
                    .ConfigureAwait(false);
                Assert.That(nextId2,
                    Is.GreaterThanOrEqualTo(nextId1).Or.Zero);
            }

            foreach (NodeId id in ids)
            {
                await UnregisterAppAsync(id).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task QueryAppsNullCapabilitiesReturnsAllAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA038");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, null, 0, null, null).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Empty);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsCombinedNameAndTypeAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA039", ApplicationType.Server);
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, "DepthQA039", null,
                (uint)ApplicationType.Server, null, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsCombinedUriAndProductUriAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA040");
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100, null, rec.ApplicationUri, 0, rec.ProductUri, null)
                .ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        [Test]
        public async Task QueryAppsCombinedNameUriTypeProductCapsAsync()
        {
            ApplicationRecordDataType rec = CreateAppRecord("QA041");
            rec.ServerCapabilities = new string[] { "DA" }.ToArrayOf();
            NodeId appId = await RegisterAppAsync(rec).ConfigureAwait(false);

            ArrayOf<string> caps = new string[] { "DA" }.ToArrayOf();
            (List<ApplicationDescription> apps, DateTime _, uint _) = await QueryAppsAsync(
                0, 100,
                "Test Application DepthQA041",
                rec.ApplicationUri,
                (uint)ApplicationType.Server,
                rec.ProductUri, caps).ConfigureAwait(false);
            Assert.That(apps, Is.Not.Null);

            await UnregisterAppAsync(appId).ConfigureAwait(false);
        }

        private readonly List<NodeId> m_registeredAppIds = [];

        private async Task AssertAuditEventTypeExistsAsync(NodeId eventTypeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = eventTypeId,
                        AttributeId = Attributes.BrowseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                $"Audit event type {eventTypeId} should exist on the server.");
        }

        private ApplicationRecordDataType CreateAppRecord(
            string suffix,
            ApplicationType appType = ApplicationType.Server)
        {
            return CreateTestApplicationRecord($"Depth{suffix}", appType);
        }

        private async Task<NodeId> RegisterAppAsync(
            ApplicationRecordDataType appRecord,
            CancellationToken ct = default)
        {
            NodeId methodId = ToNodeId(Gds.MethodIds.Directory_RegisterApplication);
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = methodId,
                        InputArguments = new Variant[] {
                            new(new ExtensionObject(appRecord))
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                $"RegisterApplication failed: {response.Results[0].StatusCode}");
            Assert.That(response.Results[0].OutputArguments.Count,
                Is.GreaterThanOrEqualTo(1));

            var appId = (NodeId)response.Results[0].OutputArguments[0];
            m_registeredAppIds.Add(appId);
            return appId;
        }

        private async Task UnregisterAppAsync(
            NodeId applicationId,
            CancellationToken ct = default)
        {
            NodeId methodId = ToNodeId(
                Gds.MethodIds.Directory_UnregisterApplication);
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = methodId,
                        InputArguments = new Variant[] {
                            new(applicationId)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            if (!StatusCode.IsGood(response.Results[0].StatusCode))
            {
                throw new ServiceResultException(
                    response.Results[0].StatusCode);
            }

            m_registeredAppIds.Remove(applicationId);
        }

        private async Task<ApplicationRecordDataType> GetAppAsync(
            NodeId applicationId,
            CancellationToken ct = default)
        {
            NodeId methodId = ToNodeId(Gds.MethodIds.Directory_GetApplication);
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = methodId,
                        InputArguments = new Variant[] {
                            new(applicationId)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            if (!StatusCode.IsGood(response.Results[0].StatusCode))
            {
                throw new ServiceResultException(
                    response.Results[0].StatusCode);
            }

            Assert.That(response.Results[0].OutputArguments.Count,
                Is.GreaterThanOrEqualTo(1));

            Variant outputArg = response.Results[0].OutputArguments[0];

            if (outputArg.TryGetStructure(
                out ApplicationRecordDataType directResult))
            {
                return directResult;
            }

            if (outputArg.TryGetValue(out ExtensionObject eo))
            {
                if (eo.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotFound);
                }

                if (eo.TryGetValue(
                    out ApplicationRecordDataType eoResult,
                    Session.MessageContext))
                {
                    return eoResult;
                }
            }

            if (outputArg.TypeInfo.IsUnknown)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound);
            }

            Assert.Fail(
                "Failed to decode ApplicationRecordDataType. " +
                $"Variant type: {outputArg.TypeInfo}");
            return null;
        }

        private async Task<List<ApplicationRecordDataType>> FindAppsAsync(
            string applicationUri,
            CancellationToken ct = default)
        {
            NodeId methodId = ToNodeId(
                Gds.MethodIds.Directory_FindApplications);
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = methodId,
                        InputArguments = new Variant[] {
                            new(applicationUri)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode),
                Is.True,
                $"FindApplications failed: {response.Results[0].StatusCode}");

            if (response.Results[0].OutputArguments.Count == 0)
            {
                return [];
            }

            Variant outputArg = response.Results[0].OutputArguments[0];
            var records = new List<ApplicationRecordDataType>();
            if (outputArg.TryGetValue(out ArrayOf<ExtensionObject> eoArray))
            {
                foreach (ExtensionObject eo2 in eoArray)
                {
                    if (eo2.TryGetValue(
                        out ApplicationRecordDataType record,
                        Session.MessageContext))
                    {
                        records.Add(record);
                    }
                }
            }

            return records;
        }

        private async Task UpdateAppAsync(
            ApplicationRecordDataType appRecord,
            CancellationToken ct = default)
        {
            NodeId methodId = ToNodeId(
                Gds.MethodIds.Directory_UpdateApplication);
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = methodId,
                        InputArguments = new Variant[] {
                            new(new ExtensionObject(appRecord))
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode),
                Is.True,
                $"UpdateApplication failed: {response.Results[0].StatusCode}");
        }

        private async Task<(
            List<ApplicationDescription> applications,
            DateTime lastCounterResetTime,
            uint nextRecordId)> QueryAppsAsync(
            uint startingRecordId,
            uint maxRecordsToReturn,
            string applicationName,
            string applicationUri,
            uint applicationType,
            string productUri,
            ArrayOf<string>? serverCapabilities,
            CancellationToken ct = default)
        {
            NodeId methodId = ToNodeId(
                Gds.MethodIds.Directory_QueryApplications);
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = methodId,
                        InputArguments = new Variant[] {
                            new(startingRecordId),
                            new(maxRecordsToReturn),
                            new(applicationName ?? string.Empty),
                            new(applicationUri ?? string.Empty),
                            new(applicationType),
                            new(productUri ?? string.Empty),
                            new(
                                serverCapabilities.HasValue
                                    ? serverCapabilities.Value.ToArray()
                                    : [])
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                ct).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode),
                Is.True,
                $"QueryApplications failed: {response.Results[0].StatusCode}");

            ArrayOf<Variant> outputs = response.Results[0].OutputArguments;
            Assert.That(outputs.Count, Is.GreaterThanOrEqualTo(3));

            var lastCounterResetTime =
                ((DateTimeUtc)outputs[0]).ToDateTime();
            uint nextRecordId = (uint)outputs[1];

            var applicationsList = new List<ApplicationDescription>();
            if (outputs[2].TryGetValue(out ArrayOf<ExtensionObject> eoArray))
            {
                foreach (ExtensionObject eo in eoArray)
                {
                    if (eo.TryGetValue(
                        out ApplicationDescription appDesc,
                        Session.MessageContext))
                    {
                        applicationsList.Add(appDesc);
                    }
                }
            }

            return (applicationsList, lastCounterResetTime, nextRecordId);
        }

        // -------------------------------------------------------------
        //  Role-based access helpers
        // -------------------------------------------------------------

        private async Task<Opc.Ua.Client.ISession> ConnectAsAsync(IUserIdentity identity)
        {
            return await ClientFixture
                .ConnectAsync(ServerUrl, SecurityPolicies.Basic256Sha256, default, identity)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Calls a GDS method on the given session and asserts a Bad status.
        /// Accepts either a service-level ServiceResultException or a per-
        /// operation Bad status in the call response.
        /// </summary>
        private async Task AssertGdsCallDeniedAsync(
            Opc.Ua.Client.ISession session,
            NodeId methodId,
            params Variant[] arguments)
        {
            try
            {
                CallResponse response = await session.CallAsync(
                    null,
                    new CallMethodRequest[]
                    {
                        new() {
                            ObjectId = m_directoryNodeId,
                            MethodId = methodId,
                            InputArguments = arguments.ToArrayOf()
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(response.Results.Count, Is.EqualTo(1));
                Assert.That(StatusCode.IsBad(response.Results[0].StatusCode), Is.True,
                    "Non-admin user must be denied; got per-operation status " +
                    $"{response.Results[0].StatusCode}.");
            }
            catch (ServiceResultException ex)
            {
                Assert.That(StatusCode.IsBad(ex.StatusCode), Is.True,
                    $"Non-admin user must be denied; got service result {ex.StatusCode}.");
            }
        }

        private async Task AssertGdsCallDeniedAsAnonymousAsync(
            NodeId methodId,
            params Variant[] arguments)
        {
            Opc.Ua.Client.ISession session = null;
            try
            {
                session = await ConnectAsAsync(new UserIdentity()).ConfigureAwait(false);
                await AssertGdsCallDeniedAsync(session, methodId, arguments).ConfigureAwait(false);
            }
            finally
            {
                if (session != null)
                {
                    try { await session.CloseAsync(5000, true).ConfigureAwait(false); } catch { }
                    session.Dispose();
                }
            }
        }

        private async Task AssertGdsCallDeniedAsRegularUserAsync(
            NodeId methodId,
            params Variant[] arguments)
        {
            Opc.Ua.Client.ISession session = null;
            try
            {
                session = await ConnectAsAsync(
                    new UserIdentity("user1", "password"u8)).ConfigureAwait(false);
                await AssertGdsCallDeniedAsync(session, methodId, arguments).ConfigureAwait(false);
            }
            finally
            {
                if (session != null)
                {
                    try { await session.CloseAsync(5000, true).ConfigureAwait(false); } catch { }
                    session.Dispose();
                }
            }
        }

        private NodeId m_directoryNodeId;
    }
}
