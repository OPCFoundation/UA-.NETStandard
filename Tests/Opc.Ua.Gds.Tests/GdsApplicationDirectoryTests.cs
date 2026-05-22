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

namespace Opc.Ua.Conformance.Tests.GDS
{
    /// <summary>
    /// compliance tests for GDS Application Directory services:
    /// RegisterApplication, UnregisterApplication, FindApplications,
    /// GetApplication, UpdateApplication, and address space browsing.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("GDS")]
    [Category("GDSApplicationDirectory")]
    public class GdsApplicationDirectoryTests : GdsTestFixture
    {
        [OneTimeSetUp]
        public async Task GdsApplicationDirectorySetUp()
        {
            // Resolve the GDS Directory object NodeId
            m_directoryNodeId = ToNodeId(Gds.ObjectIds.Directory);
            Assert.That(m_directoryNodeId, Is.Not.Null, "GDS Directory NodeId could not be resolved.");

            // Verify the Directory node is accessible
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
                "GDS Directory node not found in server address space. " +
                "Ensure the GDS node manager is enabled.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task BrowseServerDirectoryFolderExistsAsync()
        {
            // Browse the Server object to find the Directory folder.
            // The GDS Directory object has a namespace-qualified browse name.
            NodeId serverNodeId = ObjectIds.Server;
            ReferenceDescription[] children = await BrowseChildrenAsync(serverNodeId).ConfigureAwait(false);

            ReferenceDescription directory = children.FirstOrDefault(
                r => r.BrowseName.Name == "Directory");
            if (directory == null)
            {
                // The Directory object may be under the Objects folder instead
                children = await BrowseChildrenAsync(ObjectIds.ObjectsFolder)
                    .ConfigureAwait(false);
                directory = children.FirstOrDefault(
                    r => r.BrowseName.Name == "Directory");
            }

            Assert.That(directory, Is.Not.Null,
                "Directory folder not found under Server or Objects folder.");
            Assert.That(directory.NodeClass, Is.EqualTo(NodeClass.Object));
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task BrowseDirectoryCertificateGroupsExistAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId).ConfigureAwait(false);

            ReferenceDescription certGroups = children.FirstOrDefault(
                r => r.BrowseName.Name == "CertificateGroups");
            Assert.That(certGroups, Is.Not.Null,
                "Directory.CertificateGroups not found.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task BrowseDirectoryHasApplicationsFolderAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId).ConfigureAwait(false);

            bool hasApps = children.Any(
                r => r.BrowseName.Name is "Applications" or
                     "FindApplications" or
                     "RegisterApplication");

            Assert.That(hasApps, Is.True,
                "Directory should have application management nodes.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task BrowseDirectoryHasRegisterApplicationMethodAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId).ConfigureAwait(false);

            ReferenceDescription registerApp = children.FirstOrDefault(
                r => r.BrowseName.Name == "RegisterApplication");
            Assert.That(registerApp, Is.Not.Null,
                "Directory.RegisterApplication method not found.");
            Assert.That(registerApp.NodeClass, Is.EqualTo(NodeClass.Method));
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task BrowseDirectoryHasFindApplicationsMethodAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId).ConfigureAwait(false);

            ReferenceDescription findApps = children.FirstOrDefault(
                r => r.BrowseName.Name == "FindApplications");
            Assert.That(findApps, Is.Not.Null,
                "Directory.FindApplications method not found.");
            Assert.That(findApps.NodeClass, Is.EqualTo(NodeClass.Method));
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task BrowseDirectoryHasUnregisterApplicationMethodAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId).ConfigureAwait(false);

            ReferenceDescription unregApp = children.FirstOrDefault(
                r => r.BrowseName.Name == "UnregisterApplication");
            Assert.That(unregApp, Is.Not.Null,
                "Directory.UnregisterApplication method not found.");
            Assert.That(unregApp.NodeClass, Is.EqualTo(NodeClass.Method));
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task BrowseDirectoryHasGetApplicationMethodAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId).ConfigureAwait(false);

            ReferenceDescription getApp = children.FirstOrDefault(
                r => r.BrowseName.Name == "GetApplication");
            Assert.That(getApp, Is.Not.Null,
                "Directory.GetApplication method not found.");
            Assert.That(getApp.NodeClass, Is.EqualTo(NodeClass.Method));
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task BrowseDirectoryHasQueryApplicationsMethodAsync()
        {
            ReferenceDescription[] children = await BrowseChildrenAsync(m_directoryNodeId).ConfigureAwait(false);

            ReferenceDescription queryApps = children.FirstOrDefault(
                r => r.BrowseName.Name == "QueryApplications");
            Assert.That(queryApps, Is.Not.Null,
                "Directory.QueryApplications method not found.");
            Assert.That(queryApps.NodeClass, Is.EqualTo(NodeClass.Method));
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task ReadDefaultApplicationGroupExistsAsync()
        {
            ReferenceDescription certGroupsRef = await FindChildAsync(
                m_directoryNodeId, "CertificateGroups").ConfigureAwait(false);
            Assert.That(certGroupsRef, Is.Not.Null);

            var certGroupsId = ExpandedNodeId.ToNodeId(
                certGroupsRef.NodeId, Session.NamespaceUris);

            ReferenceDescription defaultGroup = await FindChildAsync(
                certGroupsId, "DefaultApplicationGroup").ConfigureAwait(false);
            Assert.That(defaultGroup, Is.Not.Null,
                "DefaultApplicationGroup not found under CertificateGroups.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task ReadDefaultApplicationGroupHasCertificateTypesAsync()
        {
            ReferenceDescription certGroupsRef = await FindChildAsync(
                m_directoryNodeId, "CertificateGroups").ConfigureAwait(false);
            Assert.That(certGroupsRef, Is.Not.Null);

            var certGroupsId = ExpandedNodeId.ToNodeId(
                certGroupsRef.NodeId, Session.NamespaceUris);
            ReferenceDescription defaultGroup = await FindChildAsync(
                certGroupsId, "DefaultApplicationGroup").ConfigureAwait(false);
            Assert.That(defaultGroup, Is.Not.Null);

            var defaultGroupId = ExpandedNodeId.ToNodeId(
                defaultGroup.NodeId, Session.NamespaceUris);
            ReferenceDescription certTypes = await FindChildAsync(
                defaultGroupId, "CertificateTypes").ConfigureAwait(false);
            Assert.That(certTypes, Is.Not.Null,
                "DefaultApplicationGroup.CertificateTypes not found.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task BrowseDirectoryTrustListNodesAsync()
        {
            ReferenceDescription certGroupsRef = await FindChildAsync(
                m_directoryNodeId, "CertificateGroups").ConfigureAwait(false);
            Assert.That(certGroupsRef, Is.Not.Null);

            var certGroupsId = ExpandedNodeId.ToNodeId(
                certGroupsRef.NodeId, Session.NamespaceUris);
            ReferenceDescription defaultGroup = await FindChildAsync(
                certGroupsId, "DefaultApplicationGroup").ConfigureAwait(false);
            Assert.That(defaultGroup, Is.Not.Null);

            var defaultGroupId = ExpandedNodeId.ToNodeId(
                defaultGroup.NodeId, Session.NamespaceUris);
            ReferenceDescription trustList = await FindChildAsync(
                defaultGroupId, "TrustList").ConfigureAwait(false);
            Assert.That(trustList, Is.Not.Null,
                "DefaultApplicationGroup.TrustList not found.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task VerifyTrustListHasOpenCloseReadWriteMethodsAsync()
        {
            ReferenceDescription certGroupsRef = await FindChildAsync(
                m_directoryNodeId, "CertificateGroups").ConfigureAwait(false);
            Assert.That(certGroupsRef, Is.Not.Null);

            var certGroupsId = ExpandedNodeId.ToNodeId(
                certGroupsRef.NodeId, Session.NamespaceUris);
            ReferenceDescription defaultGroup = await FindChildAsync(
                certGroupsId, "DefaultApplicationGroup").ConfigureAwait(false);
            Assert.That(defaultGroup, Is.Not.Null);

            var defaultGroupId = ExpandedNodeId.ToNodeId(
                defaultGroup.NodeId, Session.NamespaceUris);
            ReferenceDescription trustListRef = await FindChildAsync(
                defaultGroupId, "TrustList").ConfigureAwait(false);
            Assert.That(trustListRef, Is.Not.Null);

            var trustListId = ExpandedNodeId.ToNodeId(
                trustListRef.NodeId, Session.NamespaceUris);
            ReferenceDescription[] children = await BrowseChildrenAsync(trustListId).ConfigureAwait(false);

            Assert.That(children.Any(r => r.BrowseName.Name == "Open"), Is.True,
                "TrustList.Open method not found.");
            Assert.That(children.Any(r => r.BrowseName.Name == "Close"), Is.True,
                "TrustList.Close method not found.");
            Assert.That(children.Any(r => r.BrowseName.Name == "Read"), Is.True,
                "TrustList.Read method not found.");
            Assert.That(children.Any(r => r.BrowseName.Name == "Write"), Is.True,
                "TrustList.Write method not found.");
            Assert.That(children.Any(r => r.BrowseName.Name == "Size"), Is.True,
                "TrustList.Size property not found.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "001")]
        public async Task RegisterApplicationWithValidDescriptionReturnsGoodAsync()
        {
            Gds.ApplicationRecordDataType appRecord = CreateTestApplicationRecord("RegValid");

            NodeId appId = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);
            Assert.That(appId, Is.Not.Null);
            Assert.That(appId.IsNull, Is.False, "Returned ApplicationId should not be null.");

            // Cleanup
            await UnregisterApplicationAsync(appId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "001")]
        public async Task RegisterApplicationReturnsValidNodeIdAsync()
        {
            Gds.ApplicationRecordDataType appRecord = CreateTestApplicationRecord("RegNodeId");

            NodeId appId = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);
            Assert.That(appId, Is.Not.Null);
            Assert.That(appId.IdType, Is.Not.EqualTo(IdType.Opaque).Or.Not.Null);
            Assert.That(appId.IsNull, Is.False);

            await UnregisterApplicationAsync(appId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "001")]
        public async Task RegisterApplicationAsServerTypeAsync()
        {
            Gds.ApplicationRecordDataType appRecord = CreateTestApplicationRecord("ServerType", ApplicationType.Server);
            NodeId appId = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);

            Gds.ApplicationRecordDataType retrieved = await GetApplicationAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationType, Is.EqualTo(ApplicationType.Server));

            await UnregisterApplicationAsync(appId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "001")]
        public async Task RegisterApplicationAsClientTypeAsync()
        {
            Gds.ApplicationRecordDataType appRecord = CreateTestApplicationRecord("ClientType", ApplicationType.Client);
            NodeId appId = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);

            Gds.ApplicationRecordDataType retrieved = await GetApplicationAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationType, Is.EqualTo(ApplicationType.Client));

            await UnregisterApplicationAsync(appId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "001")]
        public async Task RegisterApplicationTwiceWithSameUriReturnsSameIdAsync()
        {
            Gds.ApplicationRecordDataType appRecord = CreateTestApplicationRecord("DupReg");

            NodeId appId1 = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);

            // Set the ApplicationId so re-registration updates the same entry
            appRecord.ApplicationId = appId1;
            NodeId appId2 = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);

            Assert.That(appId1, Is.EqualTo(appId2),
                "Registering the same URI with the same ApplicationId should return the same ApplicationId.");

            await UnregisterApplicationAsync(appId1).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "001")]
        public async Task FindApplicationsWithMatchingUriReturnsRegisteredAppAsync()
        {
            Gds.ApplicationRecordDataType appRecord = CreateTestApplicationRecord("FindMatch");
            NodeId appId = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);

            List<Gds.ApplicationRecordDataType> results = await FindApplicationsAsync(
                appRecord.ApplicationUri).ConfigureAwait(false);
            Assert.That(results, Is.Not.Empty,
                "FindApplications should return at least one match.");
            Assert.That(results.Any(r => r.ApplicationUri == appRecord.ApplicationUri),
                Is.True, "Expected application URI not found in results.");

            await UnregisterApplicationAsync(appId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "002")]
        public async Task FindApplicationsWithNonMatchingUriReturnsEmptyAsync()
        {
            List<Gds.ApplicationRecordDataType> results = await FindApplicationsAsync(
                "urn:opcfoundation.org:ctt:nonexistent:app:xyz").ConfigureAwait(false);
            Assert.That(results.Count, Is.Zero,
                "FindApplications with non-matching URI should return empty.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "040")]
        public async Task GetApplicationWithValidIdReturnsDescriptionAsync()
        {
            Gds.ApplicationRecordDataType appRecord = CreateTestApplicationRecord("GetValid");
            NodeId appId = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);

            Gds.ApplicationRecordDataType retrieved = await GetApplicationAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved, Is.Not.Null);
            Assert.That(retrieved.ApplicationUri, Is.EqualTo(appRecord.ApplicationUri));
            Assert.That(retrieved.ProductUri, Is.EqualTo(appRecord.ProductUri));

            await UnregisterApplicationAsync(appId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "042")]
        public void GetApplicationWithInvalidIdThrowsBadNotFound()
        {
            var invalidId = new NodeId(Guid.NewGuid());
            Assert.ThrowsAsync<ServiceResultException>(async () => await GetApplicationAsync(invalidId).ConfigureAwait(false));
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "040")]
        public async Task VerifyApplicationRecordDataTypeFieldsAsync()
        {
            Gds.ApplicationRecordDataType appRecord = CreateTestApplicationRecord("FieldCheck");
            NodeId appId = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);

            Gds.ApplicationRecordDataType retrieved = await GetApplicationAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ApplicationId, Is.Not.Null, "ApplicationId should not be null.");
            Assert.That(retrieved.ApplicationId.IsNull, Is.False);
            Assert.That(retrieved.ApplicationUri, Is.Not.Null.And.Not.Empty);
            Assert.That(retrieved.ApplicationNames, Is.Not.Null);
            Assert.That(retrieved.ApplicationNames.Count, Is.GreaterThan(0));
            Assert.That(retrieved.ProductUri, Is.Not.Null.And.Not.Empty);

            await UnregisterApplicationAsync(appId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "040")]
        public async Task VerifyApplicationHasServerCapabilitiesAsync()
        {
            Gds.ApplicationRecordDataType appRecord = CreateTestApplicationRecord("Capabilities");
            appRecord.ServerCapabilities = new string[] { "DA", "HDA" }.ToArrayOf();
            NodeId appId = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);

            Gds.ApplicationRecordDataType retrieved = await GetApplicationAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ServerCapabilities, Is.Not.Null);
            Assert.That(retrieved.ServerCapabilities.Count, Is.GreaterThan(0));

            await UnregisterApplicationAsync(appId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "031")]
        public async Task UpdateApplicationModifiesDescriptionAsync()
        {
            Gds.ApplicationRecordDataType appRecord = CreateTestApplicationRecord("Update");
            NodeId appId = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);

            appRecord.ApplicationId = appId;
            appRecord.ProductUri = "urn:opcfoundation.org:ctt:test:product:updated";
            await UpdateApplicationAsync(appRecord).ConfigureAwait(false);

            Gds.ApplicationRecordDataType retrieved = await GetApplicationAsync(appId).ConfigureAwait(false);
            Assert.That(retrieved.ProductUri,
                Is.EqualTo("urn:opcfoundation.org:ctt:test:product:updated"));

            await UnregisterApplicationAsync(appId).ConfigureAwait(false);
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "001")]
        public async Task UnregisterApplicationReturnsGoodAsync()
        {
            Gds.ApplicationRecordDataType appRecord = CreateTestApplicationRecord("UnregGood");
            NodeId appId = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);
            Assert.That(appId.IsNull, Is.False);

            await UnregisterApplicationAsync(appId).ConfigureAwait(false);

            // Verify the app is gone
            Assert.ThrowsAsync<ServiceResultException>(async () => await GetApplicationAsync(appId).ConfigureAwait(false));
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "020")]
        public void UnregisterApplicationWithInvalidIdThrowsBadNotFound()
        {
            var invalidId = new NodeId(Guid.NewGuid());
            Assert.ThrowsAsync<ServiceResultException>(async () => await UnregisterApplicationAsync(invalidId).ConfigureAwait(false));
        }

        private async Task<NodeId> RegisterApplicationAsync(
            Gds.ApplicationRecordDataType appRecord,
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
            Assert.That(response.Results[0].OutputArguments.Count, Is.GreaterThanOrEqualTo(1));

            return (NodeId)response.Results[0].OutputArguments[0];
        }

        private async Task UnregisterApplicationAsync(
            NodeId applicationId,
            CancellationToken ct = default)
        {
            NodeId methodId = ToNodeId(Gds.MethodIds.Directory_UnregisterApplication);
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
                throw new ServiceResultException(response.Results[0].StatusCode);
            }
        }

        private async Task<Gds.ApplicationRecordDataType> GetApplicationAsync(
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
                throw new ServiceResultException(response.Results[0].StatusCode);
            }
            Assert.That(response.Results[0].OutputArguments.Count, Is.GreaterThanOrEqualTo(1));

            Variant outputArg = response.Results[0].OutputArguments[0];

            // The output may be an ExtensionObject containing the decoded type,
            // or a Variant wrapping the structure directly.
            if (outputArg.TryGetStructure(out Gds.ApplicationRecordDataType directResult))
            {
                return directResult;
            }

            // Try extracting via ExtensionObject with the session's message context
            if (outputArg.TryGetValue(out ExtensionObject eo))
            {
                // A null/empty ExtensionObject means the application was not found
                if (eo.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound);
                }

                if (eo.TryGetValue(out Gds.ApplicationRecordDataType eoResult, Session.MessageContext))
                {
                    return eoResult;
                }
            }

            // The server returned Good but the output is null/empty - application not found
            if (outputArg.TypeInfo.IsUnknown)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound);
            }

            Assert.Fail(
                "Failed to decode ApplicationRecordDataType. " +
                $"Variant type: {outputArg.TypeInfo}, " +
                // FUTURE-AsBoxedObject-cleanup: this branch is reached only on
                // a server bug; AsBoxedObject() is acceptable here because the
                // payload is logged for diagnostics, not asserted on.
                $"Value type: {outputArg.AsBoxedObject()?.GetType()?.FullName ?? "null"}");
            return null;
        }

        private async Task UpdateApplicationAsync(
            Gds.ApplicationRecordDataType appRecord,
            CancellationToken ct = default)
        {
            NodeId methodId = ToNodeId(Gds.MethodIds.Directory_UpdateApplication);
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
                $"UpdateApplication failed: {response.Results[0].StatusCode}");
        }

        private async Task<List<Gds.ApplicationRecordDataType>> FindApplicationsAsync(
            string applicationUri,
            CancellationToken ct = default)
        {
            NodeId methodId = ToNodeId(Gds.MethodIds.Directory_FindApplications);
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
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                $"FindApplications failed: {response.Results[0].StatusCode}");

            if (response.Results[0].OutputArguments.Count == 0)
            {
                return [];
            }

            Variant outputArg = response.Results[0].OutputArguments[0];

            // Extract the array of ExtensionObjects using the proper ArrayOf<T> cast
            var records = new List<Gds.ApplicationRecordDataType>();
            if (outputArg.TryGetValue(out ArrayOf<ExtensionObject> eoArray))
            {
                foreach (ExtensionObject eo in eoArray)
                {
                    if (eo.TryGetValue(out Gds.ApplicationRecordDataType record, Session.MessageContext))
                    {
                        records.Add(record);
                    }
                }
            }
            return records;
        }

        private NodeId m_directoryNodeId;
    }
}
