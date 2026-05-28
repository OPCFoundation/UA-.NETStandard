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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database;
using Opc.Ua.Gds.Server.Identity;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Identity
{
    [TestFixture]
    [Category("Identity")]
    [Category("Gds")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class GdsApplicationSelfAdminProviderTests
    {
        private const string ApplicationUri = "urn:localhost:self-admin-provider-test";

        [Test]
        public async Task CertMatchesRegisteredAppGrantsApplicationSelfAdminAsync()
        {
            using Certificate channelCertificate = CreateCertificate("CN=SelfAdminMatch");
            NodeId applicationId = new NodeId("app-match", 2);
            var database = new StubApplicationsDatabase(ApplicationUri, applicationId, channelCertificate.RawData);
            var provider = new GdsApplicationSelfAdminProvider(
                database,
                NullLogger<GdsApplicationSelfAdminProvider>.Instance);
            IUserIdentity identity = new UserIdentity();

            AuthenticationResult result = await provider.AugmentAsync(
                identity,
                CreateContext(channelCertificate, ApplicationUri)).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.InstanceOf<GdsRoleBasedIdentity>());
            var gdsIdentity = (GdsRoleBasedIdentity)result.Identity;
            Assert.That(gdsIdentity.ApplicationId, Is.EqualTo(applicationId));
            Assert.That(gdsIdentity.Roles, Does.Contain(GdsRole.ApplicationSelfAdmin));
        }

        [Test]
        public async Task ChannelCertThumbprintMismatchLeavesIdentityUnchangedAsync()
        {
            using Certificate registeredCertificate = CreateCertificate("CN=Registered");
            using Certificate channelCertificate = CreateCertificate("CN=Channel");
            var database = new StubApplicationsDatabase(
                ApplicationUri,
                new NodeId("app-mismatch", 2),
                registeredCertificate.RawData);
            var provider = new GdsApplicationSelfAdminProvider(
                database,
                NullLogger<GdsApplicationSelfAdminProvider>.Instance);
            IUserIdentity identity = new UserIdentity();

            AuthenticationResult result = await provider.AugmentAsync(
                identity,
                CreateContext(channelCertificate, ApplicationUri)).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.SameAs(identity));
        }

        [Test]
        public async Task MissingChannelCertificateReturnsNotHandledAsync()
        {
            using Certificate registeredCertificate = CreateCertificate("CN=RegisteredNoChannelCert");
            var database = new StubApplicationsDatabase(
                ApplicationUri,
                new NodeId("app-no-channel-cert", 2),
                registeredCertificate.RawData);
            var provider = new GdsApplicationSelfAdminProvider(
                database,
                NullLogger<GdsApplicationSelfAdminProvider>.Instance);
            IUserIdentity identity = new UserIdentity();

            AuthenticationResult result = await provider.AugmentAsync(
                identity,
                CreateContext(null, ApplicationUri)).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.NotHandled));
            Assert.That(result.Identity, Is.Null);
        }

        [Test]
        public async Task MissingChannelApplicationUriLeavesIdentityUnchangedAsync()
        {
            using Certificate channelCertificate = CreateCertificate("CN=RegisteredNoUri");
            var database = new StubApplicationsDatabase(
                ApplicationUri,
                new NodeId("app-no-channel-uri", 2),
                channelCertificate.RawData);
            var provider = new GdsApplicationSelfAdminProvider(
                database,
                NullLogger<GdsApplicationSelfAdminProvider>.Instance);
            IUserIdentity identity = new UserIdentity();

            AuthenticationResult result = await provider.AugmentAsync(
                identity,
                CreateContext(channelCertificate, null)).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.SameAs(identity));
        }

        [Test]
        public async Task ExistingGdsIdentityPreservesAdministeredApplicationsAndAddsSelfAdminAsync()
        {
            using Certificate channelCertificate = CreateCertificate("CN=ExistingGdsIdentity");
            NodeId applicationId = new NodeId("app-self-admin", 2);
            NodeId administeredId = new NodeId("app-administered", 2);
            NamespaceTable namespaces = CreateNamespaceTable();
            var database = new StubApplicationsDatabase(ApplicationUri, applicationId, channelCertificate.RawData);
            var provider = new GdsApplicationSelfAdminProvider(
                database,
                NullLogger<GdsApplicationSelfAdminProvider>.Instance,
                namespaces);
            IUserIdentity identity = new GdsRoleBasedIdentity(
                new UserIdentity(),
                [GdsRole.ApplicationAdmin],
                default(NodeId),
                new[] { administeredId },
                namespaces);

            AuthenticationResult result = await provider.AugmentAsync(
                identity,
                CreateContext(channelCertificate, ApplicationUri)).ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.InstanceOf<GdsRoleBasedIdentity>());
            var gdsIdentity = (GdsRoleBasedIdentity)result.Identity;
            Assert.That(gdsIdentity.ApplicationId, Is.EqualTo(applicationId));
            Assert.That(gdsIdentity.AdministeredApplicationIds, Is.EquivalentTo(new[] { administeredId }));
            Assert.That(gdsIdentity.Roles, Does.Contain(GdsRole.ApplicationAdmin));
            Assert.That(gdsIdentity.Roles, Does.Contain(GdsRole.ApplicationSelfAdmin));
        }

        private static Certificate CreateCertificate(string subjectName)
        {
            return CertificateBuilder.Create(subjectName).CreateForRSA();
        }

        private static AuthenticationContext CreateContext(
            Certificate channelCertificate,
            string channelApplicationUri)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            return new AuthenticationContext(
                new AnonymousIdentityTokenHandler(),
                new UserTokenPolicy(UserTokenType.Anonymous),
                new EndpointDescription(),
                ServiceMessageContext.CreateEmpty(telemetry),
                channelCertificate,
                channelApplicationUri);
        }

        private static NamespaceTable CreateNamespaceTable()
        {
            var namespaces = new NamespaceTable();
            namespaces.Append(Namespaces.OpcUa);
            namespaces.Append(Namespaces.OpcUaGds);
            return namespaces;
        }

        private sealed class StubApplicationsDatabase : IApplicationsDatabase
        {
            private readonly string m_applicationUri;
            private readonly ApplicationRecordDataType m_record;
            private readonly byte[] m_certificateRawData;

            public StubApplicationsDatabase(
                string applicationUri,
                NodeId applicationId,
                byte[] certificateRawData)
            {
                m_applicationUri = applicationUri;
                m_record = new ApplicationRecordDataType
                {
                    ApplicationId = applicationId,
                    ApplicationUri = applicationUri
                };
                m_certificateRawData = certificateRawData;
            }

            public ushort NamespaceIndex { get; set; }

            public void Initialize()
            {
            }

            public NodeId RegisterApplication(ApplicationRecordDataType application)
            {
                throw new NotSupportedException();
            }

            public NodeId UpdateApplication(ApplicationRecordDataType application)
            {
                throw new NotSupportedException();
            }

            public void UnregisterApplication(NodeId applicationId)
            {
                throw new NotSupportedException();
            }

            public ApplicationRecordDataType GetApplication(NodeId applicationId)
            {
                return m_record;
            }

            public ApplicationRecordDataType[] FindApplications(string applicationUri)
            {
                return string.Equals(applicationUri, m_applicationUri, StringComparison.Ordinal)
                    ? new[] { m_record }
                    : [];
            }

            public ServerOnNetwork[] QueryServers(
                uint startingRecordId,
                uint maxRecordsToReturn,
                string applicationName,
                string applicationUri,
                string productUri,
                ArrayOf<string> serverCapabilities,
                out DateTimeUtc lastCounterResetTime)
            {
                lastCounterResetTime = DateTimeUtc.MinValue;
                return [];
            }

            public bool SetApplicationCertificate(
                NodeId applicationId,
                string certificateTypeId,
                ByteString certificate)
            {
                throw new NotSupportedException();
            }

            public bool GetApplicationCertificate(
                NodeId applicationId,
                string certificateTypeId,
                out ByteString certificate)
            {
                if (object.Equals(applicationId, m_record.ApplicationId) &&
                    string.Equals(
                        certificateTypeId,
                        nameof(Ua.ObjectTypeIds.ApplicationCertificateType),
                        StringComparison.Ordinal))
                {
                    certificate = (ByteString)m_certificateRawData;
                    return true;
                }

                certificate = ByteString.Empty;
                return false;
            }

            public bool SetApplicationTrustLists(
                NodeId applicationId,
                string certificateTypeId,
                string trustListId)
            {
                throw new NotSupportedException();
            }

            public bool GetApplicationTrustLists(
                NodeId applicationId,
                string certificateTypeId,
                out string trustListId)
            {
                trustListId = null;
                return false;
            }

            public ApplicationDescription[] QueryApplications(
                uint startingRecordId,
                uint maxRecordsToReturn,
                string applicationName,
                string applicationUri,
                uint applicationType,
                string productUri,
                ArrayOf<string> serverCapabilities,
                out DateTimeUtc lastCounterResetTime,
                out uint nextRecordId)
            {
                lastCounterResetTime = DateTimeUtc.MinValue;
                nextRecordId = 0;
                return [];
            }
        }
    }
}
