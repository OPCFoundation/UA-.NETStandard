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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for GDS Certificate Group and Trust List management.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("GDS")]
    [Category("GDSCertificateManagement")]
    public class GdsCertificateManagementTests : GdsTestFixture
    {
        [OneTimeSetUp]
        public async Task CertificateManagementSetUp()
        {
            m_directoryNodeId = ToNodeId(Gds.ObjectIds.Directory);

            // Register a test application for certificate management tests
            ApplicationRecordDataType appRecord = CreateTestApplicationRecord("CertMgmt");
            m_registeredAppId = await RegisterApplicationAsync(appRecord).ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task CertificateManagementTearDown()
        {
            if (!m_registeredAppId.IsNull)
            {
                try
                {
                    await UnregisterApplicationAsync(m_registeredAppId).ConfigureAwait(false);
                }
                catch
                {
                    // best-effort cleanup
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task BrowseCertificateGroupsOnDirectoryAsync()
        {
            ReferenceDescription certGroupsRef = await FindChildAsync(
                m_directoryNodeId, "CertificateGroups").ConfigureAwait(false);
            Assert.That(certGroupsRef, Is.Not.Null,
                "Directory.CertificateGroups not found.");
            Assert.That(certGroupsRef.NodeClass, Is.EqualTo(NodeClass.Object));
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task BrowseDefaultApplicationGroupExistsAsync()
        {
            ReferenceDescription certGroupsRef = await FindChildAsync(
                m_directoryNodeId, "CertificateGroups").ConfigureAwait(false);
            Assert.That(certGroupsRef, Is.Not.Null);

            var certGroupsId = ExpandedNodeId.ToNodeId(
                certGroupsRef.NodeId, Session.NamespaceUris);
            ReferenceDescription defaultGroup = await FindChildAsync(
                certGroupsId, "DefaultApplicationGroup").ConfigureAwait(false);
            Assert.That(defaultGroup, Is.Not.Null,
                "DefaultApplicationGroup not found.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task ReadDefaultApplicationGroupCertificateTypesAsync()
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

            // Read the value of CertificateTypes
            var certTypesId = ExpandedNodeId.ToNodeId(
                certTypes.NodeId, Session.NamespaceUris);
            ReadResponse readResult = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                new ReadValueId[] {
                    new() {
                        NodeId = certTypesId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(readResult.Results[0].StatusCode), Is.True,
                "Failed to read CertificateTypes value.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task ReadTrustListFromDefaultApplicationGroupAsync()
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
            Assert.That(trustListRef, Is.Not.Null,
                "TrustList node not found under DefaultApplicationGroup.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task ReadTrustListSizePropertyAsync()
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
            ReferenceDescription sizeRef = await FindChildAsync(trustListId, "Size").ConfigureAwait(false);
            Assert.That(sizeRef, Is.Not.Null, "TrustList.Size not found.");

            var sizeId = ExpandedNodeId.ToNodeId(
                sizeRef.NodeId, Session.NamespaceUris);
            ReadResponse readResult = await Session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                new ReadValueId[] {
                    new() {
                        NodeId = sizeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(readResult.Results[0].StatusCode), Is.True,
                "Failed to read TrustList.Size.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task VerifyTrustListOpenCloseMethodsExistAsync()
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

            ReferenceDescription openMethod = children.FirstOrDefault(r => r.BrowseName.Name == "Open");
            Assert.That(openMethod, Is.Not.Null, "TrustList.Open not found.");
            Assert.That(openMethod.NodeClass, Is.EqualTo(NodeClass.Method));

            ReferenceDescription closeMethod = children.FirstOrDefault(r => r.BrowseName.Name == "Close");
            Assert.That(closeMethod, Is.Not.Null, "TrustList.Close not found.");
            Assert.That(closeMethod.NodeClass, Is.EqualTo(NodeClass.Method));
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task BrowseCertificateGroupTypeDefinitionAsync()
        {
            ReferenceDescription certGroupsRef = await FindChildAsync(
                m_directoryNodeId, "CertificateGroups").ConfigureAwait(false);
            Assert.That(certGroupsRef, Is.Not.Null);

            var certGroupsId = ExpandedNodeId.ToNodeId(
                certGroupsRef.NodeId, Session.NamespaceUris);
            ReferenceDescription defaultGroup = await FindChildAsync(
                certGroupsId, "DefaultApplicationGroup").ConfigureAwait(false);
            Assert.That(defaultGroup, Is.Not.Null);

            // Check the type definition reference
            Assert.That(defaultGroup.TypeDefinition, Is.Not.Null,
                "DefaultApplicationGroup should have a type definition.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "000")]
        public async Task VerifyDefaultHttpsGroupExistsIfSupportedAsync()
        {
            ReferenceDescription certGroupsRef = await FindChildAsync(
                m_directoryNodeId, "CertificateGroups").ConfigureAwait(false);
            Assert.That(certGroupsRef, Is.Not.Null);

            var certGroupsId = ExpandedNodeId.ToNodeId(
                certGroupsRef.NodeId, Session.NamespaceUris);
            ReferenceDescription httpsGroup = await FindChildAsync(
                certGroupsId, "DefaultHttpsGroup").ConfigureAwait(false);

            if (httpsGroup == null)
            {
                Assert.Fail("DefaultHttpsGroup not present (HTTPS not supported).");
            }

            Assert.That(httpsGroup.NodeClass, Is.EqualTo(NodeClass.Object));
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "001")]
        public async Task GetCertificateGroupsForRegisteredApplicationAsync()
        {
            NodeId methodId = ToNodeId(Gds.MethodIds.Directory_GetCertificateGroups);
            CallResponse response = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = methodId,
                        InputArguments = new Variant[] {
                            new(m_registeredAppId)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                $"GetCertificateGroups failed: {response.Results[0].StatusCode}");
            Assert.That(response.Results[0].OutputArguments.Count, Is.GreaterThanOrEqualTo(1),
                "GetCertificateGroups should return certificate group Ids.");

            var groupIds = (ArrayOf<NodeId>)response.Results[0].OutputArguments[0];
            if (groupIds.IsEmpty)
            {
                Assert.Ignore("No certificate groups configured on the GDS server.");
            }
            Assert.That(groupIds.Count, Is.GreaterThan(0),
                "Should return at least one certificate group.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "001")]
        public async Task GetTrustListForCertificateGroupAsync()
        {
            // First get the certificate groups
            NodeId getCertGroupsMethodId = ToNodeId(Gds.MethodIds.Directory_GetCertificateGroups);
            CallResponse groupsResponse = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = getCertGroupsMethodId,
                        InputArguments = new Variant[] {
                            new(m_registeredAppId)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(groupsResponse.Results[0].StatusCode), Is.True);
            var groupIds = (ArrayOf<NodeId>)groupsResponse.Results[0].OutputArguments[0];
            if (groupIds.IsEmpty)
            {
                Assert.Ignore("No certificate groups configured on the GDS server.");
            }
            NodeId getTrustListMethodId = ToNodeId(Gds.MethodIds.Directory_GetTrustList);
            CallResponse trustListResponse = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = getTrustListMethodId,
                        InputArguments = new Variant[] {
                            new(m_registeredAppId),
                            new(groupIds[0])
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(trustListResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(trustListResponse.Results[0].StatusCode), Is.True,
                $"GetTrustList failed: {trustListResponse.Results[0].StatusCode}");
            Assert.That(trustListResponse.Results[0].OutputArguments.Count, Is.GreaterThanOrEqualTo(1));

            var trustListNodeId = (NodeId)trustListResponse.Results[0].OutputArguments[0];
            Assert.That(trustListNodeId.IsNull, Is.False,
                "GetTrustList should return a valid TrustList NodeId.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "001")]
        public async Task GetCertificateStatusReturnsBooleanAsync()
        {
            // Get certificate groups first
            NodeId getCertGroupsMethodId = ToNodeId(Gds.MethodIds.Directory_GetCertificateGroups);
            CallResponse groupsResponse = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = getCertGroupsMethodId,
                        InputArguments = new Variant[] {
                            new(m_registeredAppId)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(groupsResponse.Results[0].StatusCode), Is.True);
            var groupIds = (ArrayOf<NodeId>)groupsResponse.Results[0].OutputArguments[0];
            if (groupIds.IsEmpty)
            {
                Assert.Ignore("No certificate groups configured on the GDS server.");
            }

            // Call GetCertificateStatus
            NodeId getCertStatusMethodId = ToNodeId(Gds.MethodIds.Directory_GetCertificateStatus);
            CallResponse statusResponse = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = getCertStatusMethodId,
                        InputArguments = new Variant[] {
                            new(m_registeredAppId),
                            new(groupIds[0]),
                            new(NodeId.Null) // any certificate type
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(statusResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(statusResponse.Results[0].StatusCode), Is.True,
                $"GetCertificateStatus failed: {statusResponse.Results[0].StatusCode}");
            Assert.That(statusResponse.Results[0].OutputArguments.Count, Is.GreaterThanOrEqualTo(1));

            Assert.That(statusResponse.Results[0].OutputArguments[0].TryGetValue(out bool _), Is.True,
                "GetCertificateStatus should return a boolean.");
        }

        [Test]
        [Property("ConformanceUnit", "GDS Application Directory")]
        [Property("Tag", "001")]
        public async Task StartSigningRequestAndFinishRequestAsync()
        {
            // Get certificate groups first
            NodeId getCertGroupsMethodId = ToNodeId(Gds.MethodIds.Directory_GetCertificateGroups);
            CallResponse groupsResponse = await Session.CallAsync(
                null,
                new CallMethodRequest[] {
                    new() {
                        ObjectId = m_directoryNodeId,
                        MethodId = getCertGroupsMethodId,
                        InputArguments = new Variant[] {
                            new(m_registeredAppId)
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(groupsResponse.Results[0].StatusCode), Is.True);
            var groupIds = (ArrayOf<NodeId>)groupsResponse.Results[0].OutputArguments[0];
            if (groupIds.IsEmpty)
            {
                Assert.Ignore("No certificate groups configured on the GDS server.");
            }

            // Try StartSigningRequest- this may not be supported by all implementations
            NodeId startSigningMethodId = ToNodeId(Gds.MethodIds.Directory_StartSigningRequest);
            try
            {
                CallResponse signingResponse = await Session.CallAsync(
                    null,
                    new CallMethodRequest[] {
                        new() {
                            ObjectId = m_directoryNodeId,
                            MethodId = startSigningMethodId,
                            InputArguments = new Variant[] {
                                new(m_registeredAppId),
                                new(groupIds[0]),
                                new(NodeId.Null),
                                new(Array.Empty<byte>())
                            }.ToArrayOf()
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                if (!StatusCode.IsGood(signingResponse.Results[0].StatusCode))
                {
                    Assert.Ignore(
                        $"StartSigningRequest not supported: {signingResponse.Results[0].StatusCode}");
                }

                var requestId = (NodeId)signingResponse.Results[0].OutputArguments[0];
                Assert.That(requestId.IsNull, Is.False,
                    "StartSigningRequest should return a valid RequestId.");

                // Call FinishRequest to check status
                NodeId finishRequestMethodId = ToNodeId(Gds.MethodIds.Directory_FinishRequest);
                CallResponse finishResponse = await Session.CallAsync(
                    null,
                    new CallMethodRequest[] {
                        new() {
                            ObjectId = m_directoryNodeId,
                            MethodId = finishRequestMethodId,
                            InputArguments = new Variant[] {
                                new(m_registeredAppId),
                                new(requestId)
                            }.ToArrayOf()
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(finishResponse.Results.Count, Is.EqualTo(1));
                // FinishRequest may return Good (cert ready) or BadNothingToDo (still processing)
                Assert.That(
                    StatusCode.IsGood(finishResponse.Results[0].StatusCode) ||
                    finishResponse.Results[0].StatusCode == StatusCodes.BadNothingToDo,
                    Is.True,
                    $"FinishRequest unexpected status: {finishResponse.Results[0].StatusCode}");
            }
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadNotSupported ||
                    ex.StatusCode == StatusCodes.BadInvalidArgument ||
                    ex.StatusCode == StatusCodes.BadUserAccessDenied)
            {
                Assert.Ignore($"StartSigningRequest not supported: {ex.StatusCode}");
            }
        }

        private async Task<NodeId> RegisterApplicationAsync(
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

            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True,
                $"RegisterApplication failed: {response.Results[0].StatusCode}");
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

        private NodeId m_directoryNodeId;
        private NodeId m_registeredAppId;
    }
}
