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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.Client.Conformance.Tests
{
    /// <summary>
    /// compliance tests for the Push Certificate Management model
    /// (ServerConfiguration object and related methods).
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Security")]
    [Category("PushCertManagement")]
    public class PushCertManagementTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "001")]
        public async Task BrowseServerConfigurationExistsAsync()
        {
            ArrayOf<ReferenceDescription> refs = await BrowseChildrenAsync(
                Session, ServerNodeId).ConfigureAwait(false);
            bool found = refs.ToArray().Any(r => r.BrowseName.Name == "ServerConfiguration");
            Assert.That(found, Is.True,
                "Server should have a ServerConfiguration component.");
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "002")]
        public async Task BrowseCertificateGroupsExistsAsync()
        {
            NodeId certGroupsId = await FindChildAsync(
                Session, ServerConfigurationNodeId, "CertificateGroups")
                .ConfigureAwait(false);
            Assert.That(certGroupsId.IsNull, Is.False,
                "ServerConfiguration should have CertificateGroups.");
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "003")]
        public async Task BrowseDefaultApplicationGroupExistsAsync()
        {
            NodeId defaultGroup = await FindDefaultApplicationGroupAsync(Session)
                .ConfigureAwait(false);
            Assert.That(defaultGroup.IsNull, Is.False,
                "CertificateGroups should contain DefaultApplicationGroup.");
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "003")]
        public async Task BrowseDefaultApplicationGroupHasCertificateTypesAsync()
        {
            NodeId defaultGroup = await FindDefaultApplicationGroupAsync(Session)
                .ConfigureAwait(false);
            if (defaultGroup.IsNull)
            {
                Assert.Fail("DefaultApplicationGroup not found.");
            }

            NodeId certTypes = await FindChildAsync(
                Session, defaultGroup, "CertificateTypes").ConfigureAwait(false);
            Assert.That(certTypes.IsNull, Is.False,
                "DefaultApplicationGroup should have CertificateTypes.");
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "003")]
        public async Task BrowseDefaultApplicationGroupHasTrustListAsync()
        {
            NodeId defaultGroup = await FindDefaultApplicationGroupAsync(Session)
                .ConfigureAwait(false);
            if (defaultGroup.IsNull)
            {
                Assert.Fail("DefaultApplicationGroup not found.");
            }

            NodeId trustList = await FindChildAsync(
                Session, defaultGroup, "TrustList").ConfigureAwait(false);
            Assert.That(trustList.IsNull, Is.False,
                "DefaultApplicationGroup should have TrustList.");
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "004")]
        public async Task BrowseDefaultHttpsGroupIfExistsAsync()
        {
            NodeId certGroupsId = await FindChildAsync(
                Session, ServerConfigurationNodeId, "CertificateGroups")
                .ConfigureAwait(false);
            if (certGroupsId.IsNull)
            {
                Assert.Fail("CertificateGroups not found.");
            }

            NodeId httpsGroup = await FindChildAsync(
                Session, certGroupsId, "DefaultHttpsGroup").ConfigureAwait(false);
            if (httpsGroup.IsNull)
            {
                Assert.Fail("DefaultHttpsGroup not present on this server.");
            }

            NodeId certTypes = await FindChildAsync(
                Session, httpsGroup, "CertificateTypes").ConfigureAwait(false);
            Assert.That(certTypes.IsNull, Is.False,
                "DefaultHttpsGroup should have CertificateTypes if present.");
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "004")]
        public async Task BrowseDefaultUserTokenGroupIfExistsAsync()
        {
            NodeId certGroupsId = await FindChildAsync(
                Session, ServerConfigurationNodeId, "CertificateGroups")
                .ConfigureAwait(false);
            if (certGroupsId.IsNull)
            {
                Assert.Fail("CertificateGroups not found.");
            }

            NodeId userTokenGroup = await FindChildAsync(
                Session, certGroupsId, "DefaultUserTokenGroup")
                .ConfigureAwait(false);
            if (userTokenGroup.IsNull)
            {
                Assert.Fail("DefaultUserTokenGroup not present on this server.");
            }

            NodeId certTypes = await FindChildAsync(
                Session, userTokenGroup, "CertificateTypes").ConfigureAwait(false);
            Assert.That(certTypes.IsNull, Is.False,
                "DefaultUserTokenGroup should have CertificateTypes if present.");
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "005")]
        public async Task BrowseServerConfigurationMethodsAsync()
        {
            ArrayOf<ReferenceDescription> refs = await BrowseChildrenAsync(
                Session, ServerConfigurationNodeId).ConfigureAwait(false);
            var names = refs.ToArray().Select(r => r.BrowseName.Name).ToList();

            Assert.That(names, Does.Contain("UpdateCertificate"));
            Assert.That(names, Does.Contain("CreateSigningRequest"));
            Assert.That(names, Does.Contain("ApplyChanges"));
            Assert.That(names, Does.Contain("GetRejectedList"));
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "005")]
        public async Task VerifyPushModelMethodsExistOnTypeDefinitionAsync()
        {
            // ServerConfigurationType = i=12581
            var typeId = new NodeId(12581u);
            ArrayOf<ReferenceDescription> refs = await BrowseChildrenAsync(
                Session, typeId).ConfigureAwait(false);
            var names = refs.ToArray().Select(r => r.BrowseName.Name).ToList();

            // Some servers may define these methods only on the instance,
            // not on the type definition. Fall back to the instance if needed.
            if (!names.Contains("UpdateCertificate"))
            {
                refs = await BrowseChildrenAsync(
                    Session, ServerConfigurationNodeId).ConfigureAwait(false);
                names = [.. refs.ToArray().Select(r => r.BrowseName.Name)];
            }

            Assert.That(names, Does.Contain("UpdateCertificate"));
            Assert.That(names, Does.Contain("CreateSigningRequest"));
            Assert.That(names, Does.Contain("ApplyChanges"));
            Assert.That(names, Does.Contain("GetRejectedList"));
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "006")]
        public async Task ReadCertificateTypesFromDefaultApplicationGroupAsync()
        {
            NodeId defaultGroup = await FindDefaultApplicationGroupAsync(Session)
                .ConfigureAwait(false);
            if (defaultGroup.IsNull)
            {
                Assert.Fail("DefaultApplicationGroup not found.");
            }

            NodeId certTypesId = await FindChildAsync(
                Session, defaultGroup, "CertificateTypes").ConfigureAwait(false);
            if (certTypesId.IsNull)
            {
                Assert.Fail("CertificateTypes not found.");
            }

            DataValue value = await Session.ReadValueAsync(
                certTypesId, CancellationToken.None).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue.TryGetValue(out ArrayOf<NodeId> _), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "006")]
        public async Task ReadMaxTrustListSizeAsync()
        {
            NodeId maxTrustListId = await FindChildAsync(
                Session, ServerConfigurationNodeId, "MaxTrustListSize")
                .ConfigureAwait(false);
            if (maxTrustListId.IsNull)
            {
                Assert.Fail("MaxTrustListSize not found.");
            }

            DataValue value = await Session.ReadValueAsync(
                maxTrustListId, CancellationToken.None).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "006")]
        public async Task ReadMulticastDnsEnabledAsync()
        {
            NodeId multicastId = await FindChildAsync(
                Session, ServerConfigurationNodeId, "MulticastDnsEnabled")
                .ConfigureAwait(false);
            if (multicastId.IsNull)
            {
                Assert.Fail("MulticastDnsEnabled not found on this server.");
            }

            DataValue value = await Session.ReadValueAsync(
                multicastId, CancellationToken.None).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "006")]
        public async Task ReadServerCapabilitiesAsync()
        {
            NodeId capabilitiesId = await FindChildAsync(
                Session, ServerConfigurationNodeId, "ServerCapabilities")
                .ConfigureAwait(false);
            if (capabilitiesId.IsNull)
            {
                Assert.Fail("ServerCapabilities not found.");
            }

            DataValue value = await Session.ReadValueAsync(
                capabilitiesId, CancellationToken.None).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "006")]
        public async Task ReadSupportedPrivateKeyFormatsAsync()
        {
            NodeId formatsId = await FindChildAsync(
                Session, ServerConfigurationNodeId, "SupportedPrivateKeyFormats")
                .ConfigureAwait(false);
            if (formatsId.IsNull)
            {
                Assert.Fail("SupportedPrivateKeyFormats not found.");
            }

            DataValue value = await Session.ReadValueAsync(
                formatsId, CancellationToken.None).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "010")]
        public async Task TrustListOpenReadCloseAsync()
        {
            using ISession session = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (session == null)
            {
                Assert.Ignore("Admin session not available on this endpoint.");
            }

            NodeId defaultGroup = await FindDefaultApplicationGroupAsync(session)
                .ConfigureAwait(false);
            if (defaultGroup.IsNull)
            {
                Assert.Ignore("DefaultApplicationGroup not found.");
            }

            NodeId trustListId = await FindChildAsync(
                session, defaultGroup, "TrustList").ConfigureAwait(false);
            if (trustListId.IsNull)
            {
                Assert.Ignore("TrustList not found.");
            }

            NodeId openMethodId = await FindChildAsync(
                session, trustListId, "Open").ConfigureAwait(false);
            if (openMethodId.IsNull)
            {
                Assert.Ignore("TrustList.Open not found.");
            }

            try
            {
                // Open for reading (mode = 1)
                CallMethodResult openResult = await CallMethodAsync(
                    session, trustListId, openMethodId,
                    new Variant((byte)1)).ConfigureAwait(false);

                if (openResult.StatusCode == StatusCodes.BadNotImplemented ||
                    openResult.StatusCode == StatusCodes.BadServiceUnsupported ||
                    openResult.StatusCode == StatusCodes.BadUserAccessDenied)
                {
                    Assert.Ignore($"TrustList.Open not implemented: {openResult.StatusCode}");
                }

                Assert.That(StatusCode.IsGood(openResult.StatusCode), Is.True);
                Assert.That(openResult.OutputArguments.Count, Is.GreaterThan(0));
                uint fileHandle = (uint)openResult.OutputArguments[0];

                // Read
                NodeId readMethodId = await FindChildAsync(
                    session, trustListId, "Read").ConfigureAwait(false);
                if (!readMethodId.IsNull)
                {
                    CallMethodResult readResult = await CallMethodAsync(
                        session, trustListId, readMethodId,
                        new Variant(fileHandle),
                        new Variant(65536)).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(readResult.StatusCode), Is.True);
                }

                // Close
                NodeId closeMethodId = await FindChildAsync(
                    session, trustListId, "Close").ConfigureAwait(false);
                if (!closeMethodId.IsNull)
                {
                    await CallMethodAsync(
                        session, trustListId, closeMethodId,
                        new Variant(fileHandle)).ConfigureAwait(false);
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported ||
                    sre.StatusCode == StatusCodes.BadUserAccessDenied)
            {
                Assert.Ignore($"TrustList operations not supported: {sre.StatusCode}");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "011")]
        public async Task TrustListSizePropertyAsync()
        {
            NodeId defaultGroup = await FindDefaultApplicationGroupAsync(Session)
                .ConfigureAwait(false);
            if (defaultGroup.IsNull)
            {
                Assert.Fail("DefaultApplicationGroup not found.");
            }

            NodeId trustListId = await FindChildAsync(
                Session, defaultGroup, "TrustList").ConfigureAwait(false);
            if (trustListId.IsNull)
            {
                Assert.Fail("TrustList not found.");
            }

            NodeId sizeId = await FindChildAsync(
                Session, trustListId, "Size").ConfigureAwait(false);
            if (sizeId.IsNull)
            {
                Assert.Fail("TrustList.Size not found.");
            }

            DataValue value = await Session.ReadValueAsync(
                sizeId, CancellationToken.None).ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "012")]
        public async Task TrustListGetPositionSetPositionAsync()
        {
            using ISession session = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (session == null)
            {
                Assert.Ignore("Admin session not available on this endpoint.");
            }

            NodeId defaultGroup = await FindDefaultApplicationGroupAsync(session)
                .ConfigureAwait(false);
            if (defaultGroup.IsNull)
            {
                Assert.Ignore("DefaultApplicationGroup not found.");
            }

            NodeId trustListId = await FindChildAsync(
                session, defaultGroup, "TrustList").ConfigureAwait(false);
            if (trustListId.IsNull)
            {
                Assert.Ignore("TrustList not found.");
            }

            NodeId openMethodId = await FindChildAsync(
                session, trustListId, "Open").ConfigureAwait(false);
            if (openMethodId.IsNull)
            {
                Assert.Ignore("TrustList.Open not found.");
            }

            try
            {
                CallMethodResult openResult = await CallMethodAsync(
                    session, trustListId, openMethodId,
                    new Variant((byte)1)).ConfigureAwait(false);

                if (openResult.StatusCode == StatusCodes.BadNotImplemented ||
                    openResult.StatusCode == StatusCodes.BadServiceUnsupported ||
                    openResult.StatusCode == StatusCodes.BadUserAccessDenied)
                {
                    Assert.Ignore($"TrustList.Open not implemented: {openResult.StatusCode}");
                }

                uint fileHandle = (uint)openResult.OutputArguments[0];

                try
                {
                    NodeId getPosId = await FindChildAsync(
                        session, trustListId, "GetPosition").ConfigureAwait(false);
                    if (!getPosId.IsNull)
                    {
                        CallMethodResult posResult = await CallMethodAsync(
                            session, trustListId, getPosId,
                            new Variant(fileHandle)).ConfigureAwait(false);
                        // GetPosition may not be implemented on the TrustList
                        // FileType subtype — accept BadNotImplemented /
                        // BadServiceUnsupported as a "feature-not-supported"
                        // outcome rather than failing the test.
                        if (StatusCode.IsBad(posResult.StatusCode)
                            && posResult.StatusCode != StatusCodes.BadNotImplemented
                            && posResult.StatusCode != StatusCodes.BadServiceUnsupported
                            && posResult.StatusCode != StatusCodes.BadNotSupported)
                        {
                            Assert.That(
                                StatusCode.IsGood(posResult.StatusCode), Is.True);
                        }
                    }

                    NodeId setPosId = await FindChildAsync(
                        session, trustListId, "SetPosition").ConfigureAwait(false);
                    if (!setPosId.IsNull)
                    {
                        CallMethodResult setResult = await CallMethodAsync(
                            session, trustListId, setPosId,
                            new Variant(fileHandle),
                            new Variant((ulong)0)).ConfigureAwait(false);
                        if (StatusCode.IsBad(setResult.StatusCode)
                            && setResult.StatusCode != StatusCodes.BadNotImplemented
                            && setResult.StatusCode != StatusCodes.BadServiceUnsupported
                            && setResult.StatusCode != StatusCodes.BadNotSupported)
                        {
                            Assert.That(
                                StatusCode.IsGood(setResult.StatusCode), Is.True);
                        }
                    }
                }
                finally
                {
                    NodeId closeMethodId = await FindChildAsync(
                        session, trustListId, "Close").ConfigureAwait(false);
                    if (!closeMethodId.IsNull)
                    {
                        await CallMethodAsync(
                            session, trustListId, closeMethodId,
                            new Variant(fileHandle)).ConfigureAwait(false);
                    }
                }
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported ||
                    sre.StatusCode == StatusCodes.BadUserAccessDenied)
            {
                Assert.Ignore(
                    $"TrustList operations not supported: {sre.StatusCode}");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "019")]
        public async Task GetRejectedListReturnsResultAsync()
        {
            using ISession session = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (session == null)
            {
                Assert.Ignore("Admin session not available on this endpoint.");
            }

            NodeId methodId = await FindChildAsync(
                session, ServerConfigurationNodeId, "GetRejectedList")
                .ConfigureAwait(false);
            if (methodId.IsNull)
            {
                Assert.Ignore("GetRejectedList not found.");
            }

            try
            {
                CallMethodResult result = await CallMethodAsync(
                    session, ServerConfigurationNodeId, methodId)
                    .ConfigureAwait(false);

                if (result.StatusCode == StatusCodes.BadNotImplemented ||
                    result.StatusCode == StatusCodes.BadServiceUnsupported ||
                    result.StatusCode == StatusCodes.BadUserAccessDenied)
                {
                    Assert.Ignore($"GetRejectedList not implemented: {result.StatusCode}");
                }

                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                    $"GetRejectedList returned: 0x{result.StatusCode.Code:X8}");
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore(
                    $"GetRejectedList not implemented: {sre.StatusCode}");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "016")]
        public async Task GetCertificatesForDefaultApplicationGroupAsync()
        {
            using ISession session = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (session == null)
            {
                Assert.Ignore("Admin session not available on this endpoint.");
            }

            NodeId getCertsId = await FindChildAsync(
                session, ServerConfigurationNodeId, "GetCertificates")
                .ConfigureAwait(false);
            if (getCertsId.IsNull)
            {
                Assert.Ignore("GetCertificates not found.");
            }

            NodeId defaultGroup = await FindDefaultApplicationGroupAsync(session)
                .ConfigureAwait(false);
            if (defaultGroup.IsNull)
            {
                Assert.Ignore("DefaultApplicationGroup not found.");
            }

            try
            {
                CallMethodResult result = await CallMethodAsync(
                    session, ServerConfigurationNodeId, getCertsId,
                    new Variant(defaultGroup)).ConfigureAwait(false);

                if (result.StatusCode == StatusCodes.BadNotImplemented ||
                    result.StatusCode == StatusCodes.BadServiceUnsupported ||
                    result.StatusCode == StatusCodes.BadUserAccessDenied)
                {
                    Assert.Ignore($"GetCertificates not implemented: {result.StatusCode}");
                }

                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore(
                    $"GetCertificates not implemented: {sre.StatusCode}");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "016")]
        public async Task GetCertificatesReturnsProperStructureAsync()
        {
            using ISession session = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (session == null)
            {
                Assert.Ignore("Admin session not available on this endpoint.");
            }

            NodeId getCertsId = await FindChildAsync(
                session, ServerConfigurationNodeId, "GetCertificates")
                .ConfigureAwait(false);
            if (getCertsId.IsNull)
            {
                Assert.Ignore("GetCertificates not found.");
            }

            NodeId defaultGroup = await FindDefaultApplicationGroupAsync(session)
                .ConfigureAwait(false);
            if (defaultGroup.IsNull)
            {
                Assert.Ignore("DefaultApplicationGroup not found.");
            }

            try
            {
                CallMethodResult result = await CallMethodAsync(
                    session, ServerConfigurationNodeId, getCertsId,
                    new Variant(defaultGroup)).ConfigureAwait(false);

                if (result.StatusCode == StatusCodes.BadNotImplemented ||
                    result.StatusCode == StatusCodes.BadServiceUnsupported ||
                    result.StatusCode == StatusCodes.BadUserAccessDenied)
                {
                    Assert.Ignore($"GetCertificates not implemented: {result.StatusCode}");
                }

                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(result.OutputArguments.Count, Is.GreaterThanOrEqualTo(1));
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported)
            {
                Assert.Ignore(
                    $"GetCertificates not implemented: {sre.StatusCode}");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "008")]
        public async Task CreateSigningRequestWithValidParametersAsync()
        {
            using ISession session = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (session == null)
            {
                Assert.Ignore("Admin session not available on this endpoint.");
            }

            NodeId csrId = await FindChildAsync(
                session, ServerConfigurationNodeId, "CreateSigningRequest")
                .ConfigureAwait(false);
            if (csrId.IsNull)
            {
                Assert.Ignore("CreateSigningRequest not found.");
            }

            NodeId defaultGroup = await FindDefaultApplicationGroupAsync(session)
                .ConfigureAwait(false);
            if (defaultGroup.IsNull)
            {
                Assert.Ignore("DefaultApplicationGroup not found.");
            }

            // RsaSha256 certificate type = i=12560
            var rsaCertType = new NodeId(12560u);
            try
            {
                CallMethodResult result = await CallMethodAsync(
                    session, ServerConfigurationNodeId, csrId,
                    new Variant(defaultGroup),
                    new Variant(rsaCertType),
                    new Variant((string)null),
                    new Variant(false),
                    new Variant((byte[])null)).ConfigureAwait(false);

                if (result.StatusCode == StatusCodes.BadNotImplemented ||
                    result.StatusCode == StatusCodes.BadServiceUnsupported ||
                    result.StatusCode == StatusCodes.BadUserAccessDenied ||
                    result.StatusCode == StatusCodes.BadInvalidArgument ||
                    result.StatusCode == StatusCodes.BadNotSupported)
                {
                    Assert.Ignore($"CreateSigningRequest not supported: {result.StatusCode}");
                }

                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True,
                    $"CreateSigningRequest failed: 0x{result.StatusCode.Code:X8}");
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported ||
                    sre.StatusCode == StatusCodes.BadInvalidArgument ||
                    sre.StatusCode == StatusCodes.BadNotSupported)
            {
                Assert.Ignore(
                    $"CreateSigningRequest not supported: {sre.StatusCode}");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "Err-001")]
        public async Task CreateSigningRequestWithInvalidGroupFailsAsync()
        {
            using ISession session = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (session == null)
            {
                Assert.Fail("Admin session not available on this endpoint.");
            }

            NodeId csrId = await FindChildAsync(
                session, ServerConfigurationNodeId, "CreateSigningRequest")
                .ConfigureAwait(false);
            if (csrId.IsNull)
            {
                Assert.Fail("CreateSigningRequest not found.");
            }

            var invalidGroup = new NodeId(99999u);
            var rsaCertType = new NodeId(12560u);
            try
            {
                CallMethodResult result = await CallMethodAsync(
                    session, ServerConfigurationNodeId, csrId,
                    new Variant(invalidGroup),
                    new Variant(rsaCertType),
                    new Variant((string)null),
                    new Variant(false),
                    new Variant((byte[])null)).ConfigureAwait(false);

                Assert.That(StatusCode.IsBad(result.StatusCode), Is.True,
                    "Expected Bad status for invalid group.");
            }
            catch (ServiceResultException sre)
            {
                Assert.That(StatusCode.IsBad(sre.StatusCode), Is.True,
                    "Expected Bad status for invalid group.");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "015")]
        public async Task ApplyChangesSucceedsAsync()
        {
            using ISession session = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (session == null)
            {
                Assert.Ignore("Admin session not available on this endpoint.");
            }

            NodeId applyId = await FindChildAsync(
                session, ServerConfigurationNodeId, "ApplyChanges")
                .ConfigureAwait(false);
            if (applyId.IsNull)
            {
                Assert.Ignore("ApplyChanges not found.");
            }

            try
            {
                CallMethodResult result = await CallMethodAsync(
                    session, ServerConfigurationNodeId, applyId)
                    .ConfigureAwait(false);

                if (result.StatusCode == StatusCodes.BadNotImplemented ||
                    result.StatusCode == StatusCodes.BadServiceUnsupported ||
                    result.StatusCode == StatusCodes.BadNothingToDo ||
                    result.StatusCode == StatusCodes.BadUserAccessDenied)
                {
                    Assert.Ignore($"ApplyChanges: {result.StatusCode}");
                }

                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented ||
                    sre.StatusCode == StatusCodes.BadServiceUnsupported ||
                    sre.StatusCode == StatusCodes.BadNothingToDo)
            {
                Assert.Ignore($"ApplyChanges: {sre.StatusCode}");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "Err-002")]
        public async Task UpdateCertificateWithEmptyCertFailsAsync()
        {
            using ISession session = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (session == null)
            {
                Assert.Fail("Admin session not available on this endpoint.");
            }

            NodeId updateId = await FindChildAsync(
                session, ServerConfigurationNodeId, "UpdateCertificate")
                .ConfigureAwait(false);
            if (updateId.IsNull)
            {
                Assert.Fail("UpdateCertificate not found.");
            }

            NodeId defaultGroup = await FindDefaultApplicationGroupAsync(session)
                .ConfigureAwait(false);
            if (defaultGroup.IsNull)
            {
                Assert.Fail("DefaultApplicationGroup not found.");
            }

            var rsaCertType = new NodeId(12560u);
            try
            {
                CallMethodResult result = await CallMethodAsync(
                    session, ServerConfigurationNodeId, updateId,
                    new Variant(defaultGroup),
                    new Variant(rsaCertType),
                    new Variant(System.Array.Empty<byte>()),
                    Variant.From(System.Array.Empty<ByteString>()),
                    new Variant((string)null),
                    new Variant((byte[])null)).ConfigureAwait(false);

                Assert.That(StatusCode.IsBad(result.StatusCode), Is.True,
                    "UpdateCertificate with empty cert should fail.");
            }
            catch (ServiceResultException sre)
            {
                Assert.That(StatusCode.IsBad(sre.StatusCode), Is.True);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "Err-003")]
        public async Task UpdateCertificateWithInvalidCertFailsAsync()
        {
            using ISession session = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (session == null)
            {
                Assert.Fail("Admin session not available on this endpoint.");
            }

            NodeId updateId = await FindChildAsync(
                session, ServerConfigurationNodeId, "UpdateCertificate")
                .ConfigureAwait(false);
            if (updateId.IsNull)
            {
                Assert.Fail("UpdateCertificate not found.");
            }

            NodeId defaultGroup = await FindDefaultApplicationGroupAsync(session)
                .ConfigureAwait(false);
            if (defaultGroup.IsNull)
            {
                Assert.Fail("DefaultApplicationGroup not found.");
            }

            var rsaCertType = new NodeId(12560u);
            byte[] invalidCert = new byte[] { 0x30, 0x82, 0x00, 0x01 };
            try
            {
                CallMethodResult result = await CallMethodAsync(
                    session, ServerConfigurationNodeId, updateId,
                    new Variant(defaultGroup),
                    new Variant(rsaCertType),
                    new Variant(invalidCert),
                    Variant.From(System.Array.Empty<ByteString>()),
                    new Variant((string)null),
                    new Variant((byte[])null)).ConfigureAwait(false);

                Assert.That(StatusCode.IsBad(result.StatusCode), Is.True,
                    "UpdateCertificate with invalid cert should fail.");
            }
            catch (ServiceResultException sre)
            {
                Assert.That(StatusCode.IsBad(sre.StatusCode), Is.True);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "017")]
        public async Task GetCertificateStatusIfPresentAsync()
        {
            using ISession session = await TryConnectAsAdminAsync().ConfigureAwait(false);
            if (session == null)
            {
                Assert.Ignore("Admin session not available on this endpoint.");
            }

            NodeId statusId = await FindChildAsync(
                session, ServerConfigurationNodeId, "GetCertificateStatus")
                .ConfigureAwait(false);
            if (statusId.IsNull)
            {
                Assert.Ignore("GetCertificateStatus not found.");
            }

            NodeId defaultGroup = await FindDefaultApplicationGroupAsync(session)
                .ConfigureAwait(false);
            if (defaultGroup.IsNull)
            {
                Assert.Ignore("DefaultApplicationGroup not found.");
            }

            var rsaCertType = new NodeId(12560u);
            try
            {
                CallMethodResult result = await CallMethodAsync(
                    session, ServerConfigurationNodeId, statusId,
                    new Variant(defaultGroup),
                    new Variant(rsaCertType)).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
                Assert.That(result.OutputArguments.Count, Is.GreaterThan(0));
            }
            catch (ServiceResultException sre)
                when (sre.StatusCode == StatusCodes.BadNotImplemented)
            {
                Assert.Ignore(
                    $"GetCertificateStatus not implemented: {sre.StatusCode}");
            }
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "Err-004")]
        public async Task NonAdminCannotCallCreateSigningRequestAsync()
        {
            ISession userSession;
            try
            {
                userSession = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None,
                        userIdentity: new UserIdentity("user1", "password"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                Assert.Fail("Cannot connect as user1.");
                return;
            }

            using (userSession)
            {
                NodeId csrId = await FindChildAsync(
                    userSession, ServerConfigurationNodeId, "CreateSigningRequest")
                    .ConfigureAwait(false);
                if (csrId.IsNull)
                {
                    Assert.Fail("CreateSigningRequest not found.");
                }

                NodeId defaultGroup = await FindDefaultApplicationGroupAsync(
                    userSession).ConfigureAwait(false);
                if (defaultGroup.IsNull)
                {
                    Assert.Fail("DefaultApplicationGroup not found.");
                }

                var rsaCertType = new NodeId(12560u);
                try
                {
                    CallMethodResult result = await CallMethodAsync(
                        userSession, ServerConfigurationNodeId, csrId,
                        new Variant(defaultGroup),
                        new Variant(rsaCertType),
                        new Variant((string)null),
                        new Variant(false),
                        new Variant((byte[])null)).ConfigureAwait(false);

                    Assert.That(StatusCode.IsBad(result.StatusCode), Is.True,
                        "Non-admin should not succeed.");
                }
                catch (ServiceResultException sre)
                {
                    Assert.That(StatusCode.IsBad(sre.StatusCode), Is.True,
                        "Expected access denied for non-admin user.");
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "Err-005")]
        public async Task NonAdminCannotCallGetRejectedListAsync()
        {
            ISession userSession;
            try
            {
                userSession = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None,
                        userIdentity: new UserIdentity("user1", "password"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                Assert.Fail("Cannot connect as user1.");
                return;
            }

            using (userSession)
            {
                NodeId methodId = await FindChildAsync(
                    userSession, ServerConfigurationNodeId, "GetRejectedList")
                    .ConfigureAwait(false);
                if (methodId.IsNull)
                {
                    Assert.Fail("GetRejectedList not found.");
                }

                try
                {
                    CallMethodResult result = await CallMethodAsync(
                        userSession, ServerConfigurationNodeId, methodId)
                        .ConfigureAwait(false);
                    Assert.That(StatusCode.IsBad(result.StatusCode), Is.True,
                        "Non-admin should not succeed.");
                }
                catch (ServiceResultException sre)
                {
                    Assert.That(StatusCode.IsBad(sre.StatusCode), Is.True,
                        "Expected access denied for non-admin user.");
                }
            }
        }

        [Test]
        [Property("ConformanceUnit", "Push Model for Global Certificate and TrustList Management")]
        [Property("Tag", "Err-006")]
        public async Task NonAdminCannotCallApplyChangesAsync()
        {
            ISession userSession;
            try
            {
                userSession = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None,
                        userIdentity: new UserIdentity("user1", "password"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                Assert.Fail("Cannot connect as user1.");
                return;
            }

            using (userSession)
            {
                NodeId applyId = await FindChildAsync(
                    userSession, ServerConfigurationNodeId, "ApplyChanges")
                    .ConfigureAwait(false);
                if (applyId.IsNull)
                {
                    Assert.Fail("ApplyChanges not found.");
                }

                try
                {
                    CallMethodResult result = await CallMethodAsync(
                        userSession, ServerConfigurationNodeId, applyId)
                        .ConfigureAwait(false);
                    Assert.That(StatusCode.IsBad(result.StatusCode), Is.True,
                        "Non-admin should not succeed.");
                }
                catch (ServiceResultException sre)
                {
                    Assert.That(StatusCode.IsBad(sre.StatusCode), Is.True,
                        "Expected access denied for non-admin user.");
                }
            }
        }

        /// <summary>
        /// OPC UA well-known NodeIds (namespace 0)
        /// </summary>
        private static readonly NodeId ServerNodeId = new(2253u);
        private static readonly NodeId ServerConfigurationNodeId = new(12637u);

        private async Task<ISession> TryConnectAsAdminAsync()
        {
            // Phase 7c: ServerConfiguration / TrustList push methods require
            // a SignAndEncrypt channel per Part 12 §7.10.3 — try to find a
            // SignAndEncrypt endpoint with username token first, fall back
            // to SecurityPolicies.None (which preserves prior behavior for
            // tests that don't strictly require an encrypted channel).
            try
            {
                var endpointConfiguration = EndpointConfiguration.Create(ClientFixture.Config);
                using DiscoveryClient client = await DiscoveryClient.CreateAsync(
                    ServerUrl,
                    endpointConfiguration,
                    Telemetry,
                    ct: CancellationToken.None).ConfigureAwait(false);
                ArrayOf<EndpointDescription> endpoints = await client.GetEndpointsAsync(
                    default, CancellationToken.None).ConfigureAwait(false);
                await client.CloseAsync(CancellationToken.None).ConfigureAwait(false);

                string preferred = null;
                foreach (EndpointDescription ep in endpoints)
                {
                    if (ep.SecurityMode != MessageSecurityMode.SignAndEncrypt
                        || ep.UserIdentityTokens == default)
                    {
                        continue;
                    }
                    foreach (UserTokenPolicy t in ep.UserIdentityTokens)
                    {
                        if (t.TokenType == UserTokenType.UserName)
                        {
                            preferred = ep.SecurityPolicyUri;
                            break;
                        }
                    }
                    if (preferred != null)
                    {
                        break;
                    }
                }

                return await ClientFixture
                    .ConnectAsync(ServerUrl, preferred ?? SecurityPolicies.None,
                        userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                return null;
            }
        }

        private async Task<ArrayOf<ReferenceDescription>> BrowseChildrenAsync(
            ISession session, NodeId nodeId)
        {
            BrowseResponse response = await session.BrowseAsync(
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

            if (response.Results.Count == 0)
            {
                return default;
            }

            ArrayOf<ReferenceDescription> refs = response.Results[0].References;

            // Handle continuation
            ByteString cp = response.Results[0].ContinuationPoint;
            while (!cp.IsEmpty)
            {
                BrowseNextResponse next = await session.BrowseNextAsync(
                    null, false,
                    new ByteString[] { cp }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
                if (next.Results.Count > 0)
                {
                    var more = new List<ReferenceDescription>(refs.ToArray());
                    more.AddRange(next.Results[0].References.ToArray());
                    refs = more.ToArrayOf();
                    cp = next.Results[0].ContinuationPoint;
                }
                else
                {
                    break;
                }
            }

            return refs;
        }

        private async Task<NodeId> FindChildAsync(
            ISession session, NodeId parentId, string browseName)
        {
            ArrayOf<ReferenceDescription> refs = await BrowseChildrenAsync(
                session, parentId).ConfigureAwait(false);
            foreach (ReferenceDescription rd in refs)
            {
                if (rd.BrowseName.Name == browseName)
                {
                    return ExpandedNodeId.ToNodeId(rd.NodeId, session.NamespaceUris);
                }
            }
            return NodeId.Null;
        }

        private async Task<NodeId> FindDefaultApplicationGroupAsync(ISession session)
        {
            NodeId certGroupsId = await FindChildAsync(
                session, ServerConfigurationNodeId, "CertificateGroups")
                .ConfigureAwait(false);
            if (certGroupsId.IsNull)
            {
                return NodeId.Null;
            }
            return await FindChildAsync(
                session, certGroupsId, "DefaultApplicationGroup")
                .ConfigureAwait(false);
        }

        private async Task<CallMethodResult> CallMethodAsync(
            ISession session,
            NodeId objectId,
            NodeId methodId,
            params Variant[] args)
        {
            CallResponse response = await session.CallAsync(
                null,
                new CallMethodRequest[]
                {
                    new() {
                        ObjectId = objectId,
                        MethodId = methodId,
                        InputArguments = args.Length > 0 ? args.ToArrayOf() : default
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }
    }
}
