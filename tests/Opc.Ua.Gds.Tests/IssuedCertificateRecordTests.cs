// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// A certificate the GDS signed is recorded in the application
    /// certificate store only while the application is registered.
    /// </summary>
    [TestFixture]
    [Category("GDS")]
    [Parallelizable]
    public sealed class IssuedCertificateRecordTests
    {
        private string m_path = null!;
        private ITelemetryContext m_telemetry = null!;
        private LinqApplicationsDatabase m_database = null!;
        private ApplicationsNodeManager m_nodeManager = null!;

        [SetUp]
        public void SetUp()
        {
            m_path = Path.Combine(Path.GetTempPath(), "OPC", "GDS", "IssuedRecord-" + Guid.NewGuid().ToString("N"));
            m_telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(m_telemetry);
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.Telemetry).Returns(m_telemetry);
            server.SetupGet(value => value.NamespaceUris).Returns(messageContext.NamespaceUris);
            server.SetupGet(value => value.ServerUris).Returns(messageContext.ServerUris);
            server.SetupGet(value => value.Factory).Returns(messageContext.Factory);
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(messageContext.NamespaceUris));
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            var configuration = new ApplicationConfiguration(m_telemetry)
            {
                ApplicationName = "IssuedCertificateRecordTests",
                SecurityConfiguration = new SecurityConfiguration()
            };
            configuration.UpdateExtension(null, new GlobalDiscoveryServerConfiguration
            {
                ApplicationCertificatesStorePath = Path.Combine(m_path, "applications")
            });
            m_database = new LinqApplicationsDatabase();
            m_nodeManager = new ApplicationsNodeManager(
                server.Object, configuration, m_database, m_database, Mock.Of<ICertificateGroup>());
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await m_nodeManager.DisposeAsync().ConfigureAwait(false);
            if (Directory.Exists(m_path))
            {
                Directory.Delete(m_path, true);
            }
        }

        [Test]
        public async Task SignedCertificateIsRecordedForRegisteredApplicationAsync()
        {
            NodeId applicationId = Register("urn:localhost:IssuedRecord:Registered");
            using Certificate certificate = CertificateBuilder.Create("CN=IssuedRecordRegistered").CreateForRSA();

            await m_nodeManager.RecordIssuedCertificateAsync(applicationId, certificate).ConfigureAwait(false);

            Assert.That(await IsRecordedAsync(certificate).ConfigureAwait(false), Is.True);
        }

        [Test]
        public async Task SignedCertificateIsNotRecordedAfterUnregistrationAsync()
        {
            // The application is removed while its certificate is issued.
            NodeId applicationId = Register("urn:localhost:IssuedRecord:Unregistered");
            m_database.UnregisterApplication(applicationId);
            using Certificate certificate = CertificateBuilder.Create("CN=IssuedRecordUnregistered").CreateForRSA();

            ServiceResultException? failure = null;
            try
            {
                await m_nodeManager.RecordIssuedCertificateAsync(applicationId, certificate).ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                failure = ex;
            }

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
            Assert.That(await IsRecordedAsync(certificate).ConfigureAwait(false), Is.False);
        }

        private NodeId Register(string applicationUri)
        {
            return m_database.RegisterApplication(new ApplicationRecordDataType
            {
                ApplicationUri = applicationUri,
                ApplicationType = ApplicationType.Client,
                ProductUri = "http://opcfoundation.org/UA/IssuedRecord",
                ApplicationNames = [new LocalizedText("en-US", applicationUri)]
            });
        }

        private async Task<bool> IsRecordedAsync(Certificate certificate)
        {
            using ICertificateStore store = new CertificateStoreIdentifier(Path.Combine(m_path, "applications"))
                .OpenStore(m_telemetry);
            using CertificateCollection matches = await store.FindByThumbprintAsync(certificate.Thumbprint)
                .ConfigureAwait(false);
            return matches.Count > 0;
        }
    }
}
