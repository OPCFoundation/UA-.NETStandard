/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;
using Opc.Ua.Tests;
using CoreObjectIds = Opc.Ua.ObjectIds;
using CoreObjectTypeIds = Opc.Ua.ObjectTypeIds;
using Ua = global::Opc.Ua;

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Exercises the composed application push engine against independent certificate stores and live GDS context.
    /// </summary>
    [TestFixture]
    [Category("ManagedApplications")]
    [NonParallelizable]
    public sealed class ManagedApplicationPushTests : GdsTestFixture
    {
        /// <summary>
        /// Fluent hosting isolates certificate rotation and trust changes over a real encrypted management channel.
        /// The management channel survives target changes and an observer cannot read application certificates.
        /// </summary>
        [Test]
        public async Task EncryptedChannelRoutesIndependentApplicationsAndEnforcesSecurityAdmin()
        {
            using var first = await TestTarget.CreateAsync("urn:managed:channel:first").ConfigureAwait(false);
            using var second = await TestTarget.CreateAsync("urn:managed:channel:second").ConfigureAwait(false);
            string root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory, "managed-channel", Guid.NewGuid().ToString("N"));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            int port;
            try
            {
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
            var url = new Uri($"opc.tcp://localhost:{port}/managed-applications");
            string password = Guid.NewGuid().ToString("N");
            var users = new LinqUserDatabase();
            Assert.That(users.CreateUser("administrator", Encoding.UTF8.GetBytes(password),
                [Role.AuthenticatedUser, Role.SecurityAdmin]), Is.True);
            Assert.That(users.CreateUser("observer", Encoding.UTF8.GetBytes(password),
                [Role.AuthenticatedUser]), Is.True);
            Mock<IRecoverableApplicationConfigurationFileProvider> configurationFile =
                CreateConfigurationFileProvider();
            var applications = new Mock<IManagedApplicationPushProvider>();
            applications.Setup(value => value.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>(
                [
                    new(first, Mock.Of<IPushConfigurationAuthorization>(),
                        configurationFileProvider: configurationFile.Object),
                    new(second, Mock.Of<IPushConfigurationAuthorization>())
                ]));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IUserDatabase>(users);
            services.AddSingleton<IUserManagement>(provider =>
                new UserManagement(provider.GetRequiredService<IUserDatabase>()));
            services.AddOpcUa().AddServer(options =>
            {
                options.ApplicationName = "ManagedApplicationChannelTest";
                options.ApplicationUri = "urn:managed:channel:host";
                options.ProductUri = "urn:opcfoundation:test:managed-applications";
                options.PkiRoot = Path.Combine(root, "server");
                options.AutoAcceptUntrustedCertificates = true;
                options.IncludeSignAndEncryptPolicies = true;
                options.IncludeUnsecurePolicyNone = false;
                options.IncludeEccPolicies = false;
                options.EndpointUrls.Add(url.AbsoluteUri);
                options.UserTokenPolicies.Add(new OpcUaUserTokenPolicy { TokenType = UserTokenType.UserName });
            })
                .AddDefaultIdentityAuthenticators(options =>
                {
                    options.EnableAnonymous = false;
                    options.EnableUserNamePassword = true;
                    options.EnableX509 = false;
                    options.EnableJwt = false;
                })
                .ConfigureRoles(options => options.Roles.Add(new RoleDefinitionOptions
                {
                    Name = "SecurityAdmin",
                    Identities =
                    {
                        new RoleIdentityMappingOptions
                        {
                            CriteriaType = IdentityCriteriaType.UserName,
                            Criteria = "administrator"
                        }
                    }
                }))
                .AddManagedApplicationPush(_ => applications.Object);
            ServiceProvider container = services.BuildServiceProvider();
            IHostedService? hosted = null;
            try
            {
                hosted = container.GetServices<IHostedService>()
                    .Single(static service => service.GetType().Name == "OpcUaServerHostedService");
                await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);
                using var client = new ClientFixture(telemetry: s_telemetry);
                await client.LoadClientConfigurationAsync(Path.Combine(root, "client"), "ManagedApplicationsClient")
                    .ConfigureAwait(false);
                using Opc.Ua.Client.ISession session = await client.ConnectAsync(
                    url, SecurityPolicies.Basic256Sha256, default,
                    new UserIdentity("administrator", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false);
                int namespaceIndex = session.NamespaceUris.GetIndex(
                    DefaultManagedApplicationsNodeManager.ManagedApplicationsNamespaceUri);
                Assert.That(namespaceIndex, Is.GreaterThan(0));
                var firstNode = new NodeId(first.Configuration.ApplicationUri!, checked((ushort)namespaceIndex));
                var secondNode = new NodeId(second.Configuration.ApplicationUri!, checked((ushort)namespaceIndex));
                var firstClient = new ServerConfigurationTypeClient(session, firstNode, s_telemetry);
                var secondClient = new ServerConfigurationTypeClient(session, secondNode, s_telemetry);

                var (_, firstCertificates) = await firstClient.GetCertificatesAsync(NodeId.Null)
                    .ConfigureAwait(false);
                var (_, secondCertificates) = await secondClient.GetCertificatesAsync(NodeId.Null)
                    .ConfigureAwait(false);
                Assert.That(firstCertificates, Has.Count.EqualTo(1));
                Assert.That(secondCertificates, Has.Count.EqualTo(1));
                using var firstCertificate = Certificate.FromRawData(firstCertificates[0]);
                using var secondCertificate = Certificate.FromRawData(secondCertificates[0]);
                Assert.That(firstCertificate.Thumbprint, Is.EqualTo(first.CurrentThumbprint()));
                Assert.That(secondCertificate.Thumbprint, Is.EqualTo(second.CurrentThumbprint()));
                Assert.That(firstCertificate.Thumbprint, Is.Not.EqualTo(secondCertificate.Thumbprint));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using Certificate replacement = CertificateBuilder.Create("CN=Managed Push Test")
                    .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                    .SetLifeTime(TimeSpan.FromDays(365))
                    .AddExtension(new X509SubjectAltNameExtension(first.Configuration.ApplicationUri!, ["localhost"]))
                    .SetRSAKeySize(2048)
                    .CreateForRSA();
                ByteString managementCertificate = session.ConfiguredEndpoint.Description.ServerCertificate;
                bool applyRequired = await firstClient.UpdateCertificateAsync(
                    NodeId.Null, CoreObjectTypeIds.RsaSha256ApplicationCertificateType,
                    replacement.RawData.ToByteString(), [], "PFX",
                    replacement.Export(X509ContentType.Pfx).ToByteString(), timeout.Token).ConfigureAwait(false);
                Assert.That(applyRequired, Is.True);
                Assert.That(first.CurrentThumbprint(), Is.EqualTo(firstCertificate.Thumbprint));
                await firstClient.ApplyChangesAsync(timeout.Token).ConfigureAwait(false);
                await first.Activation.WaitAsync(timeout.Token).ConfigureAwait(false);
                var (_, rotatedCertificates) = await firstClient.GetCertificatesAsync(NodeId.Null, timeout.Token)
                    .ConfigureAwait(false);
                using var rotated = Certificate.FromRawData(rotatedCertificates[0]);
                Assert.That(rotated.Thumbprint, Is.EqualTo(replacement.Thumbprint));
                Assert.That(second.CurrentThumbprint(), Is.EqualTo(secondCertificate.Thumbprint));
                Assert.That(second.ActivatedGroups, Is.Empty);
                Assert.That(first.PreviousCertificates, Has.Count.EqualTo(1));
                using var previous = Certificate.FromRawData(first.PreviousCertificates[0].PreviousCertificate);
                Assert.That(previous.Thumbprint, Is.EqualTo(firstCertificate.Thumbprint));
                Assert.That(session.ConfiguredEndpoint.Description.ServerCertificate,
                    Is.EqualTo(managementCertificate));

                NodeId trustListId = await ResolveChildAsync(session, firstNode,
                    [BrowseNames.CertificateGroups, Ua.BrowseNames.DefaultApplicationGroup, Ua.BrowseNames.TrustList],
                    timeout.Token).ConfigureAwait(false);
                var trust = new TrustListTypeClient(session, trustListId, s_telemetry);
                await trust.AddCertificateAsync(secondCertificate.RawData.ToByteString(), true, timeout.Token)
                    .ConfigureAwait(false);
                await firstClient.ApplyChangesAsync(timeout.Token).ConfigureAwait(false);
                await first.TrustActivation.WaitAsync(timeout.Token).ConfigureAwait(false);
                using TrustListData? firstTrust = await first.Configuration.CertificateManager!
                    .ReadTrustListAsync(TrustListIdentifier.Peers, TrustListMasks.All, timeout.Token)
                    .ConfigureAwait(false);
                using TrustListData? secondTrust = await second.Configuration.CertificateManager!
                    .ReadTrustListAsync(TrustListIdentifier.Peers, TrustListMasks.All, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(firstTrust, Is.Not.Null);
                Assert.That(firstTrust!.TrustedCertificates, Has.Count.EqualTo(1));
                Assert.That(firstTrust.TrustedCertificates[0].Thumbprint, Is.EqualTo(secondCertificate.Thumbprint));
                Assert.That(secondTrust, Is.Not.Null);
                Assert.That(secondTrust!.TrustedCertificates, Is.Empty);
                Assert.That(first.TrustEffects.ToList().Select(static effect => effect.TrustListId),
                    Has.All.EqualTo(trustListId));
                Assert.That(second.TrustEffects, Is.Empty);
                var (_, stillAccessible) = await secondClient.GetCertificatesAsync(NodeId.Null, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(stillAccessible[0], Is.EqualTo(secondCertificate.RawData.ToByteString()));

                ApplicationConfigurationFileTypeClient? firstFile =
                    await firstClient.GetConfigurationFileAsync(s_telemetry, timeout.Token).ConfigureAwait(false);
                Assert.That(firstFile, Is.Not.Null);
                Assert.That(await secondClient.GetConfigurationFileAsync(s_telemetry, timeout.Token)
                    .ConfigureAwait(false), Is.Null);
                uint fileHandle = await firstFile!.OpenAsync(
                    (byte)(OpenFileMode.Read | OpenFileMode.Write), timeout.Token).ConfigureAwait(false);
                ByteString content = ByteString.From(new byte[] { 1, 2, 3 });
                await firstFile.WriteAsync(fileHandle, content, timeout.Token).ConfigureAwait(false);
                var update = await firstFile.CloseAndUpdateAsync(
                    fileHandle, 1,
                    [new ConfigurationUpdateTargetType
                    {
                        Path = "ApplicationIdentity",
                        UpdateType = ConfigurationUpdateType.InsertOrReplace
                    }],
                    0, 0, timeout.Token).ConfigureAwait(false);
                Assert.That(update.newVersion, Is.EqualTo(2));
                Assert.That(update.updateId.Guid, Is.Not.EqualTo(Guid.Empty));
                await firstFile.ConfirmUpdateAsync(update.updateId, timeout.Token).ConfigureAwait(false);
                configurationFile.Verify(provider => provider.ValidateConfigurationAsync(
                    content, It.IsAny<CancellationToken>()), Times.Once);
                configurationFile.Verify(provider => provider.ApplyPendingUpdateAsync(
                    update.updateId, It.IsAny<CancellationToken>()), Times.Once);
                configurationFile.Verify(provider => provider.ConfirmPendingUpdateAsync(
                    update.updateId, It.IsAny<CancellationToken>()), Times.Once);
                await session.CloseAsync(5000, true).ConfigureAwait(false);

                using Opc.Ua.Client.ISession observer = await client.ConnectAsync(
                    url, SecurityPolicies.Basic256Sha256, default,
                    new UserIdentity("observer", Encoding.UTF8.GetBytes(password))).ConfigureAwait(false);
                var observerClient = new ServerConfigurationTypeClient(observer, firstNode, s_telemetry);
                ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await observerClient.GetCertificatesAsync(NodeId.Null).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                var observerFile = new ApplicationConfigurationFileTypeClient(
                    observer, firstFile.ObjectId, s_telemetry);
                ServiceResultException? fileDenied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await observerFile.OpenAsync((byte)OpenFileMode.Read).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(fileDenied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                configurationFile.Verify(provider => provider.ReadConfigurationAsync(
                    It.IsAny<CancellationToken>()), Times.Never);
                await observer.CloseAsync(5000, true).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (hosted != null)
                    {
                        await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await container.DisposeAsync().ConfigureAwait(false);
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, recursive: true);
                    }
                }
            }
        }

        /// <summary>
        /// Creation in an unoccupied slot still schedules target activation without an old certificate to retire.
        /// </summary>
        [Test]
        public async Task CreationIntoEmptySlotStillActivatesTargetApplication()
        {
            using var target = TestTarget.CreateEmpty("urn:managed:empty-slot");
            await using var manager = CreateManager(target);
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);
            ApplicationConfigurationState node = manager.ApplicationNodes[target.Configuration.ApplicationUri!];
            NodeId group = node.CertificateGroups!.DefaultApplicationGroup!.NodeId;
            CreateSelfSignedCertificateMethodStateResult created = await node.CreateSelfSignedCertificate!.OnCallAsync!(
                manager.SystemContext, node.CreateSelfSignedCertificate, node.NodeId,
                group, CoreObjectTypeIds.RsaSha256ApplicationCertificateType, "CN=Managed Push Test",
                ["localhost"], [], 365, 2048, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(created.ServiceResult), Is.True);

            ServiceResult applied = await node.ApplyChanges!.OnCallMethod2Async!(
                manager.SystemContext, node.ApplyChanges, node.NodeId, [], [], CancellationToken.None)
                .ConfigureAwait(false);
            await manager.DrainPendingApplicationChangesAsync().ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(applied), Is.True);
            Assert.That(target.ActivatedGroups, Is.EqualTo(new[] { group }));
            Assert.That(target.PreviousCertificates, Is.Empty);
            global::Opc.Ua.GetCertificatesMethodStateResult result = await node.GetCertificates!.OnCallAsync!(
                manager.SystemContext, node.GetCertificates, node.NodeId, group, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(result.Certificates, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// A staged rotation uses its own group and coordinator and activates only the selected application.
        /// </summary>
        [Test]
        public async Task RotationAndTransactionStateAreIsolatedBetweenApplications()
        {
            using var first = await TestTarget.CreateAsync("urn:managed:first").ConfigureAwait(false);
            using var second = await TestTarget.CreateAsync("urn:managed:second").ConfigureAwait(false);
            await using var manager = CreateManager(first, second);
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            ApplicationConfigurationState firstNode = manager.ApplicationNodes[first.Configuration.ApplicationUri!];
            ApplicationConfigurationState secondNode = manager.ApplicationNodes[second.Configuration.ApplicationUri!];
            NodeId group = firstNode.CertificateGroups!.DefaultApplicationGroup!.NodeId;
            string previous = first.CurrentThumbprint();
            string untouched = second.CurrentThumbprint();
            using Certificate replacement = CertificateBuilder.Create("CN=Managed Push Test")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(TimeSpan.FromDays(365))
                .AddExtension(new X509SubjectAltNameExtension(first.Configuration.ApplicationUri!, ["localhost"]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
            UpdateCertificateMethodStateResult staged =
                await firstNode.UpdateCertificate!.OnCallAsync!(
                    manager.SystemContext, firstNode.UpdateCertificate, firstNode.NodeId,
                    group, CoreObjectTypeIds.RsaSha256ApplicationCertificateType,
                    replacement.RawData.ToByteString(), [], "PFX",
                    replacement.Export(X509ContentType.Pfx).ToByteString(),
                    CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(staged.ServiceResult), Is.True);
            Assert.That(first.CurrentThumbprint(), Is.EqualTo(previous));
            Assert.That(group.NamespaceIndex, Is.Not.Zero);
            Assert.That(group, Is.Not.EqualTo(secondNode.CertificateGroups!.DefaultApplicationGroup!.NodeId));
            ServiceResult otherCancel = await secondNode.CancelChanges!.OnCallMethod2Async!(
                manager.SystemContext, secondNode.CancelChanges, secondNode.NodeId, [], [],
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(otherCancel.StatusCode, Is.EqualTo(StatusCodes.BadNothingToDo));

            ServiceResult applied = await firstNode.ApplyChanges!.OnCallMethod2Async!(
                manager.SystemContext, firstNode.ApplyChanges, firstNode.NodeId, [], [],
                CancellationToken.None).ConfigureAwait(false);
            await manager.DrainPendingApplicationChangesAsync().ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(applied), Is.True);
            Assert.That(first.CurrentThumbprint(), Is.EqualTo(replacement.Thumbprint));
            Assert.That(second.CurrentThumbprint(), Is.EqualTo(untouched));
            Assert.That(first.ActivatedGroups, Is.EqualTo(new[] { group }));
            Assert.That(first.PreviousCertificates, Has.Count.EqualTo(1));
            using var retired = Certificate.FromRawData(first.PreviousCertificates[0].PreviousCertificate);
            Assert.That(retired.Thumbprint, Is.EqualTo(previous));
            Assert.That(second.ActivatedGroups, Is.Empty);
            global::Opc.Ua.GetCertificatesMethodStateResult certificates = await firstNode.GetCertificates!.OnCallAsync!(
                manager.SystemContext, firstNode.GetCertificates, firstNode.NodeId, NodeId.Null,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(certificates.CertificateTypeIds, Has.Count.EqualTo(1));
            Assert.That(certificates.CertificateTypeIds[0],
                Is.EqualTo(CoreObjectTypeIds.RsaSha256ApplicationCertificateType));
            using var returned = Certificate.FromRawData(certificates.Certificates[0]);
            Assert.That(returned.Thumbprint, Is.EqualTo(first.CurrentThumbprint()));
        }

        /// <summary>
        /// Trust changes are applied to the selected application, never to the DCA or sibling runtime.
        /// </summary>
        [Test]
        public async Task TrustListCommitUsesOnlyTargetApplicationEffects()
        {
            using var first = await TestTarget.CreateAsync("urn:managed:trust:first").ConfigureAwait(false);
            using var second = await TestTarget.CreateAsync("urn:managed:trust:second").ConfigureAwait(false);
            await using var manager = CreateManager(first, second);
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            ApplicationConfigurationState node = manager.ApplicationNodes[first.Configuration.ApplicationUri!];
            TrustListState trustList = node.CertificateGroups!.DefaultApplicationGroup!.TrustList!;
            using CertificateEntry? peer = second.Configuration.CertificateManager!
                .AcquireApplicationCertificateByType(CoreObjectTypeIds.RsaSha256ApplicationCertificateType);
            AddCertificateMethodStateResult staged = await trustList.AddCertificate!.OnCallAsync!(
                manager.SystemContext, trustList.AddCertificate, trustList.NodeId,
                peer!.Certificate.RawData.ToByteString(), true, CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(staged.ServiceResult), Is.True);
            Assert.That(first.TrustEffects, Is.Empty);

            ServiceResult applied = await node.ApplyChanges!.OnCallMethod2Async!(
                manager.SystemContext, node.ApplyChanges, node.NodeId, [], [], CancellationToken.None)
                .ConfigureAwait(false);
            await manager.DrainPendingApplicationChangesAsync().ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(applied), Is.True);
            Assert.That(first.TrustEffects, Has.Count.EqualTo(1));
            Assert.That(first.TrustEffects[0].TrustListId, Is.EqualTo(trustList.NodeId));
            Assert.That(first.TrustEffects[0].CertificateGroupId,
                Is.EqualTo(node.CertificateGroups.DefaultApplicationGroup.NodeId));
            Assert.That(second.TrustEffects, Is.Empty);
            using TrustListData? material = await first.Configuration.CertificateManager!
                .ReadTrustListAsync(TrustListIdentifier.Peers, TrustListMasks.All).ConfigureAwait(false);
            Assert.That(material, Is.Not.Null);
            Assert.That(material!.TrustedCertificates, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Foreign application certificates and management-server group ids fail before upload can stage work.
        /// </summary>
        [Test]
        public async Task UploadRejectsForeignApplicationUriAndManagementGroupId()
        {
            using var first = await TestTarget.CreateAsync("urn:managed:uri:first").ConfigureAwait(false);
            using var second = await TestTarget.CreateAsync("urn:managed:uri:second").ConfigureAwait(false);
            await using var manager = CreateManager(first);
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            ApplicationConfigurationState node = manager.ApplicationNodes[first.Configuration.ApplicationUri!];
            using CertificateEntry? foreign = second.Configuration.CertificateManager!
                .AcquireApplicationCertificateByType(CoreObjectTypeIds.RsaSha256ApplicationCertificateType);
            ServiceResultException? uriFailure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await node.UpdateCertificate!.OnCallAsync!(
                    manager.SystemContext, node.UpdateCertificate, node.NodeId,
                    node.CertificateGroups!.DefaultApplicationGroup!.NodeId,
                    CoreObjectTypeIds.RsaSha256ApplicationCertificateType,
                    foreign!.Certificate.RawData.ToByteString(), [], string.Empty, ByteString.Empty, CancellationToken.None)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(uriFailure!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUriInvalid));
            ServiceResultException? groupFailure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await node.GetCertificates!.OnCallAsync!(
                    manager.SystemContext, node.GetCertificates, node.NodeId,
                    CoreObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                    CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(groupFailure!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(first.ActivatedGroups, Is.Empty);
        }

        /// <summary>
        /// One active sibling binding cannot reuse another application's registry or trust store.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task BindingRejectsSiblingCertificateManagerOrTrustStoreAliases(bool shareManager)
        {
            using var first = await TestTarget.CreateAsync("urn:managed:alias:first").ConfigureAwait(false);
            using var second = await TestTarget.CreateAsync("urn:managed:alias:second").ConfigureAwait(false);
            await using var manager = CreateManager(first);
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            ICertificateManager? originalManager = second.Configuration.CertificateManager;
            CertificateTrustList originalTrust = second.Configuration.SecurityConfiguration.TrustedPeerCertificates;
            if (shareManager)
            {
                second.Configuration.CertificateManager = first.Configuration.CertificateManager;
            }
            else
            {
                second.Configuration.SecurityConfiguration.TrustedPeerCertificates =
                    first.Configuration.SecurityConfiguration.TrustedPeerCertificates;
            }
            try
            {
                var node = new ApplicationConfigurationState(null);
                node.Create(manager.SystemContext, new NodeId("alias", 2),
                    new QualifiedName("alias", 2), new LocalizedText("alias"), true);
                node.ApplicationUri!.Value = second.Configuration.ApplicationUri!;
                Assert.Throws<ArgumentException>(() => new ApplicationConfigurationPushBinding(
                    ReferenceServer.CurrentInstance, ServerFixture.Config, manager.SystemContext,
                    node, second, Mock.Of<IPushConfigurationAuthorization>()));
            }
            finally
            {
                second.Configuration.CertificateManager = originalManager;
                second.Configuration.SecurityConfiguration.TrustedPeerCertificates = originalTrust;
            }
        }

        /// <summary>
        /// An injected pending-key store cannot become shared mutable signing state across applications.
        /// </summary>
        [Test]
        public async Task BindingRejectsSiblingPendingKeyStore()
        {
            using var first = await TestTarget.CreateAsync("urn:managed:keys:first").ConfigureAwait(false);
            using var second = await TestTarget.CreateAsync("urn:managed:keys:second").ConfigureAwait(false);
            var pendingKeys = Mock.Of<IPendingCertificateKeyStore>();
            var provider = new Mock<IManagedApplicationPushProvider>();
            provider.Setup(p => p.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>(
                [
                    new(first, Mock.Of<IPushConfigurationAuthorization>(), pendingKeys),
                    new(second, Mock.Of<IPushConfigurationAuthorization>(), pendingKeys)
                ]));
            await using var manager = new DefaultManagedApplicationsNodeManager(
                ReferenceServer.CurrentInstance, ServerFixture.Config, provider.Object);

            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false)).ConfigureAwait(false);
        }

        /// <summary>
        /// The composed surface retains the standard channel and SecurityAdmin checks before invoking host policy.
        /// </summary>
        [TestCase(MessageSecurityMode.None, true)]
        [TestCase(MessageSecurityMode.SignAndEncrypt, false)]
        public async Task StandardAccessChecksPrecedeApplicationPolicy(
            MessageSecurityMode mode,
            bool administrator)
        {
            using var target = await TestTarget.CreateAsync("urn:managed:standard-access").ConfigureAwait(false);
            var policy = new Mock<IPushConfigurationAuthorization>(MockBehavior.Strict);
            var provider = new Mock<IManagedApplicationPushProvider>();
            provider.Setup(p => p.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>([new(target, policy.Object)]));
            await using var manager = new DefaultManagedApplicationsNodeManager(
                ReferenceServer.CurrentInstance, ServerFixture.Config, provider.Object);
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            ApplicationConfigurationState node = manager.ApplicationNodes[target.Configuration.ApplicationUri!];
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.GrantedRoleIds).Returns(
                administrator ? ArrayOf.Wrapped(CoreObjectIds.WellKnownRole_SecurityAdmin) : []);
            var channel = new SecureChannelContext(
                "managed-application-test", new EndpointDescription { SecurityMode = mode }, RequestEncoding.Binary);
            var operation = new OperationContext(
                new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, identity.Object);
            var context = new SessionSystemContext(operation, s_telemetry);

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await node.GetCertificates!.OnCallAsync!(
                    context, node.GetCertificates, node.NodeId, NodeId.Null, CancellationToken.None)
                    .ConfigureAwait(false)).ConfigureAwait(false);

            StatusCode expectedStatus = mode == MessageSecurityMode.None
                ? StatusCodes.BadSecurityModeInsufficient
                : StatusCodes.BadUserAccessDenied;
            Assert.That(failure!.StatusCode, Is.EqualTo(expectedStatus));
            policy.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Device attachment uses actual application components and refuses singleton layout.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task DeviceApplicationComponentsRespectSingletonLayout(bool singleton)
        {
            using var target = await TestTarget.CreateAsync("urn:managed:device:application").ConfigureAwait(false);
            await using var manager = new DefaultManagedApplicationsNodeManager(
                ReferenceServer.CurrentInstance, ServerFixture.Config, new InMemoryConfigurationDataStore());
            var references = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(references).ConfigureAwait(false);
            var device = new ProvisionableDeviceState(null);
            device.Create(manager.SystemContext, new NodeId("device", 2),
                new QualifiedName("device", 2), new LocalizedText("device"), true);
            device.IsSingleton!.Value = singleton;
            var runtime = new Mock<IProvisionableDeviceRuntime>();
            runtime.SetupGet(r => r.IsSingleton).Returns(singleton);
            var security = new Mock<IProvisionableDeviceSecurity>();
            var operations = new Mock<IApplicationConfigurationPushOperations>(MockBehavior.Strict);
            operations.Setup(value => value.ExecuteAsync(
                    It.IsAny<Func<ValueTask<global::Opc.Ua.GetCertificatesMethodStateResult>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<ValueTask<global::Opc.Ua.GetCertificatesMethodStateResult>> operation, CancellationToken _) =>
                    operation());
            security.Setup(s => s.CreateApplicationOperations()).Returns(operations.Object);
            security.Setup(s => s.RequireInstalledRegistrarAsync(
                It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>())).Returns(default(ValueTask));
            var software = new Mock<IProvisionableDeviceSoftwareUpdatePolicy>();
            ArrayOf<ManagedApplicationPushRegistration> registrations =
                [new(target, Mock.Of<IPushConfigurationAuthorization>())];
            if (singleton)
            {
                ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await manager.BindProvisionableDeviceApplicationsAsync(
                        device, runtime.Object, security.Object, registrations, references, software.Object)
                        .ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(manager.ApplicationNodes, Is.Empty);
            }
            else
            {
                await manager.BindProvisionableDeviceApplicationsAsync(
                    device, runtime.Object, security.Object, registrations, references, software.Object)
                    .ConfigureAwait(false);
                ApplicationConfigurationState node = manager.ApplicationNodes[target.Configuration.ApplicationUri!];
                Assert.That(node.Parent, Is.SameAs(device));
                Assert.That(node.UpdateCertificate!.OnCallAsync, Is.Not.Null);
                Assert.That(node.CertificateGroups!.DefaultApplicationGroup!.TrustList!.Open!.OnCallAsync, Is.Not.Null);
                Assert.That(references[device.NodeId], Has.Count.EqualTo(1));
                global::Opc.Ua.GetCertificatesMethodStateResult result = await node.GetCertificates!.OnCallAsync!(
                    manager.SystemContext, node.GetCertificates, node.NodeId, NodeId.Null, CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(result.Certificates, Has.Count.EqualTo(1));
                security.Verify(s => s.RequireInstalledRegistrarAsync(
                    manager.SystemContext, It.IsAny<CancellationToken>()), Times.Once);
                software.Verify(s => s.RequireReadyAsync(
                    target.Configuration.ApplicationUri!, It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        /// <summary>
        /// Public-certificate reads, rejected-list reads and trust-file reads all await the application gate.
        /// </summary>
        [Test]
        public async Task ReadSurfacesRequireApplicationAuthorizationBeforeOpeningTrustFiles()
        {
            using var target = await TestTarget.CreateAsync("urn:managed:read-gate").ConfigureAwait(false);
            var policy = new Mock<IPushConfigurationAuthorization>();
            policy.Setup(p => p.AuthorizeAsync(It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadUserAccessDenied));
            var provider = new Mock<IManagedApplicationPushProvider>();
            provider.Setup(p => p.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>(
                    [new(target, policy.Object)]));
            await using var manager = new DefaultManagedApplicationsNodeManager(
                ReferenceServer.CurrentInstance, ServerFixture.Config, provider.Object);
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            ApplicationConfigurationState node = manager.ApplicationNodes[target.Configuration.ApplicationUri!];
            TrustListState trust = node.CertificateGroups!.DefaultApplicationGroup!.TrustList!;

            ServiceResultException? certificates = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await node.GetCertificates!.OnCallAsync!(
                    manager.SystemContext, node.GetCertificates, node.NodeId, NodeId.Null, CancellationToken.None)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            ServiceResultException? rejected = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await node.GetRejectedList!.OnCallAsync!(
                    manager.SystemContext, node.GetRejectedList, node.NodeId, CancellationToken.None)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            ServiceResultException? opened = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await trust.Open!.OnCallAsync!(
                    manager.SystemContext, trust.Open, trust.NodeId, (byte)OpenFileMode.Read, CancellationToken.None)
                    .ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(certificates!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(opened!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(trust.OpenCount!.Value, Is.Zero);
            policy.Verify(p => p.AuthorizeAsync(
                It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        /// <summary>
        /// Disposal waits for an admitted asynchronous read and rejects subsequent access.
        /// </summary>
        [Test]
        public async Task DisposalDrainsActiveCallsBeforeReleasingApplicationSecurity()
        {
            using var target = await TestTarget.CreateAsync("urn:managed:drain").ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var policy = new Mock<IPushConfigurationAuthorization>();
            policy.Setup(p => p.AuthorizeAsync(It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask(WaitForReleaseAsync()));
            var provider = new Mock<IManagedApplicationPushProvider>();
            provider.Setup(p => p.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>(
                    [new(target, policy.Object)]));
            var manager = new DefaultManagedApplicationsNodeManager(
                ReferenceServer.CurrentInstance, ServerFixture.Config, provider.Object);
            await using (manager.ConfigureAwait(false))
            {
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                ApplicationConfigurationState node = manager.ApplicationNodes[target.Configuration.ApplicationUri!];
                Task<global::Opc.Ua.GetCertificatesMethodStateResult> read = node.GetCertificates!.OnCallAsync!(
                    manager.SystemContext, node.GetCertificates, node.NodeId, NodeId.Null, CancellationToken.None)
                    .AsTask();
                await entered.Task.ConfigureAwait(false);
                Task disposal = manager.DisposeAsync().AsTask();
                Assert.That(disposal.IsCompleted, Is.False);
                release.SetResult(true);
                await read.ConfigureAwait(false);
                await disposal.ConfigureAwait(false);
                ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await node.GetCertificates.OnCallAsync!(
                        manager.SystemContext, node.GetCertificates, node.NodeId, NodeId.Null, CancellationToken.None)
                        .ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadShutdown));
            }

            async Task WaitForReleaseAsync()
            {
                entered.SetResult(true);
                await release.Task.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Disposal after a successful commit cannot cancel target activation that has not run yet.
        /// </summary>
        [Test]
        public async Task DisposalCompletesCommittedApplicationActivation()
        {
            using var target = TestTarget.CreateEmpty("urn:managed:dispose-activation");
            var authorization = new Mock<IPushConfigurationAuthorization>();
            Mock<IApplicationConfigurationPushOperations> operations =
                authorization.As<IApplicationConfigurationPushOperations>();
            operations.Setup(value => value.ExecuteAsync(
                    It.IsAny<Func<ValueTask<CreateSelfSignedCertificateMethodStateResult>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<ValueTask<CreateSelfSignedCertificateMethodStateResult>> operation,
                    CancellationToken _) => operation());
            operations.Setup(value => value.ExecuteAsync(
                    It.IsAny<Func<ValueTask<ServiceResult>>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<ValueTask<ServiceResult>> operation, CancellationToken _) => operation());
            operations.Setup(value => value.ExecuteAsync(
                    It.IsAny<Func<ValueTask>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<ValueTask> operation, CancellationToken _) => operation());
            var provider = new Mock<IManagedApplicationPushProvider>();
            provider.Setup(value => value.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>(
                    [new(target, authorization.Object)]));
            var manager = new DefaultManagedApplicationsNodeManager(
                ReferenceServer.CurrentInstance, ServerFixture.Config, provider.Object);
            await using (manager.ConfigureAwait(false))
            {
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                ApplicationConfigurationState node = manager.ApplicationNodes[target.Configuration.ApplicationUri!];
                NodeId group = node.CertificateGroups!.DefaultApplicationGroup!.NodeId;
                CreateSelfSignedCertificateMethodStateResult created =
                    await node.CreateSelfSignedCertificate!.OnCallAsync!(
                        manager.SystemContext, node.CreateSelfSignedCertificate, node.NodeId,
                        group, CoreObjectTypeIds.RsaSha256ApplicationCertificateType, "CN=Managed Push Test",
                        ["localhost"], [], 365, 2048, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(created.ServiceResult), Is.True);

                ServiceResult applied = await node.ApplyChanges!.OnCallMethod2Async!(
                    manager.SystemContext, node.ApplyChanges, node.NodeId, [], [], CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(applied), Is.True);
                await manager.DisposeAsync().ConfigureAwait(false);

                Assert.That(target.ActivatedGroups, Is.EqualTo(new[] { group }));
                Assert.That(target.PreviousCertificates, Is.Empty);
                operations.Verify(value => value.ExecuteAsync(
                    It.IsAny<Func<ValueTask<CreateSelfSignedCertificateMethodStateResult>>>(),
                    It.IsAny<CancellationToken>()), Times.Once);
                operations.Verify(value => value.ExecuteAsync(
                    It.IsAny<Func<ValueTask<ServiceResult>>>(), It.IsAny<CancellationToken>()), Times.Once);
                operations.Verify(value => value.ExecuteAsync(
                    It.IsAny<Func<ValueTask>>(), It.IsAny<CancellationToken>()), Times.Once);
            }
        }

        /// <summary>
        /// A completed activation failure remains observable to later drain and disposal callers.
        /// </summary>
        [Test]
        public async Task CompletedApplicationActivationFailureRemainsObservableDuringDisposal()
        {
            using var target = TestTarget.CreateEmpty("urn:managed:failed-activation");
            await using var manager = CreateManager();
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            var expected = new InvalidOperationException("The target certificate activation failed.");
            var runtime = new Mock<IApplicationConfigurationPushTarget>(MockBehavior.Strict);
            runtime.SetupGet(value => value.Configuration).Returns(target.Configuration);
            runtime.SetupGet(value => value.Endpoints).Returns(ArrayOf<EndpointDescription>.Empty);
            runtime.SetupGet(value => value.SecurityPolicies).Returns((ISecurityPolicyRegistry?)null);
            runtime.Setup(value => value.ApplyCertificateChangesAsync(
                    It.IsAny<ArrayOf<NodeId>>(), It.IsAny<ArrayOf<ApplicationCertificateRotation>>(),
                    It.IsAny<CancellationToken>()))
                .Throws(expected);
            var node = new ApplicationConfigurationState(null);
            node.Create(manager.SystemContext, new NodeId("failed-activation", 2),
                new QualifiedName("failed-activation", 2), new LocalizedText("failed-activation"), true);
            node.ApplicationUri!.Value = target.Configuration.ApplicationUri!;
            var binding = new ApplicationConfigurationPushBinding(
                ReferenceServer.CurrentInstance, ServerFixture.Config, manager.SystemContext,
                node, runtime.Object, Mock.Of<IPushConfigurationAuthorization>());
            try
            {
                NodeId group = node.CertificateGroups!.DefaultApplicationGroup!.NodeId;
                CreateSelfSignedCertificateMethodStateResult created =
                    await node.CreateSelfSignedCertificate!.OnCallAsync!(
                        manager.SystemContext, node.CreateSelfSignedCertificate, node.NodeId,
                        group, CoreObjectTypeIds.RsaSha256ApplicationCertificateType, "CN=Managed Push Test",
                        ["localhost"], [], 365, 2048, CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(created.ServiceResult), Is.True);
                ServiceResult applied = await node.ApplyChanges!.OnCallMethod2Async!(
                    manager.SystemContext, node.ApplyChanges, node.NodeId, [], [], CancellationToken.None)
                    .ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(applied), Is.True);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                Task drain = binding.DrainPendingApplyChangesAsync();
                InvalidOperationException? first = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await drain.WaitAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);

                Task completedDrain = binding.DrainPendingApplyChangesAsync();
                InvalidOperationException? repeated = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await completedDrain.ConfigureAwait(false)).ConfigureAwait(false);

                Assert.That(first, Is.SameAs(expected));
                Assert.That(repeated, Is.SameAs(expected));
                Assert.That(completedDrain, Is.SameAs(drain));
            }
            finally
            {
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await binding.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure, Is.SameAs(expected));
            }
        }

        /// <summary>
        /// Shutdown publishes one completion before invoking external cancellation callbacks and retains isolation
        /// until the trust callback returns, including when its cancellation registration throws.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposalPublishesSharedCompletionAndDrainsReentrantTrustCallback(bool throwOnCancellation)
        {
            using var target = await TestTarget.CreateAsync("urn:managed:reentrant-disposal").ConfigureAwait(false);
            await using var manager = CreateManager();
            await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var expected = new InvalidOperationException("The target cancellation registration failed.");
            ApplicationConfigurationPushBinding? binding = null;
            Task? reentrantDisposal = null;
            CancellationTokenRegistration registration = default;
            int cancellationCalls = 0;
            var runtime = new Mock<IApplicationConfigurationPushTarget>(MockBehavior.Strict);
            runtime.SetupGet(value => value.Configuration).Returns(target.Configuration);
            runtime.SetupGet(value => value.Endpoints).Returns(ArrayOf<EndpointDescription>.Empty);
            runtime.SetupGet(value => value.SecurityPolicies).Returns((ISecurityPolicyRegistry?)null);
            runtime.Setup(value => value.ApplyTrustListChangesAsync(
                    It.IsAny<ArrayOf<TrustListChangeEffect>>(), It.IsAny<CancellationToken>()))
                .Returns((ArrayOf<TrustListChangeEffect> _, CancellationToken token) =>
                {
                    registration = token.Register(() =>
                    {
                        Interlocked.Increment(ref cancellationCalls);
                        reentrantDisposal = binding!.DisposeAsync().AsTask();
                        cancelled.TrySetResult(true);
                        if (throwOnCancellation)
                        {
                            throw expected;
                        }
                    });
                    entered.TrySetResult(true);
                    return new ValueTask(release.Task);
                });
            var node = new ApplicationConfigurationState(null);
            node.Create(manager.SystemContext, new NodeId("shutdown", 2),
                new QualifiedName("shutdown", 2), new LocalizedText("shutdown"), true);
            node.ApplicationUri!.Value = target.Configuration.ApplicationUri!;
            binding = new ApplicationConfigurationPushBinding(
                ReferenceServer.CurrentInstance, ServerFixture.Config, manager.SystemContext,
                node, runtime.Object, Mock.Of<IPushConfigurationAuthorization>());
            Task? disposal = null;
            try
            {
                target.Configuration.CertificateManager!.NotifyTrustListChanged(
                    TrustListIdentifier.Peers, trustChanged: true, crlChanged: false);
                await entered.Task.WaitAsync(timeout.Token).ConfigureAwait(false);

                disposal = binding.DisposeAsync().AsTask();
                await cancelled.Task.WaitAsync(timeout.Token).ConfigureAwait(false);

                Assert.That(reentrantDisposal, Is.SameAs(disposal));
                Assert.That(binding.DisposeAsync().AsTask(), Is.SameAs(disposal));
                Assert.That(disposal.IsCompleted, Is.False);
                Assert.Throws<ArgumentException>(() => new ApplicationConfigurationPushBinding(
                    ReferenceServer.CurrentInstance, ServerFixture.Config, manager.SystemContext,
                    node, runtime.Object, Mock.Of<IPushConfigurationAuthorization>()));
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    Task completion = disposal ?? binding.DisposeAsync().AsTask();
                    if (throwOnCancellation)
                    {
                        AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                            await completion.WaitAsync(timeout.Token).ConfigureAwait(false)).ConfigureAwait(false);
                        Assert.That(failure!.Flatten().InnerExceptions, Does.Contain(expected));
                    }
                    else
                    {
                        await completion.WaitAsync(timeout.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    registration.Dispose();
                }
            }
            Assert.That(cancellationCalls, Is.EqualTo(1));
            runtime.Verify(value => value.ApplyTrustListChangesAsync(
                It.IsAny<ArrayOf<TrustListChangeEffect>>(), It.IsAny<CancellationToken>()), Times.Once);
            await using var replacement = new ApplicationConfigurationPushBinding(
                ReferenceServer.CurrentInstance, ServerFixture.Config, manager.SystemContext,
                node, runtime.Object, Mock.Of<IPushConfigurationAuthorization>());
        }

        /// <summary>
        /// A provider cannot publish application components on a singleton device.
        /// </summary>
        [Test]
        public async Task ProvisionableDeviceNodeManagerRejectsApplicationsOnSingletonDevice()
        {
            var applications = new Mock<IProvisionableDeviceApplicationsProvider>(MockBehavior.Strict);
            applications.Setup(provider => provider.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>(
                [
                    new(Mock.Of<IApplicationConfigurationPushTarget>(),
                        Mock.Of<IPushConfigurationAuthorization>())
                ]));
            IAsyncNodeManager manager = await CreateProvisionableDeviceNodeManagerAsync(
                isSingleton: true, applications.Object).ConfigureAwait(false);

            try
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false)).ConfigureAwait(false);

                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(error.Message, Does.Contain("singleton"));
                applications.Verify(provider =>
                    provider.GetApplicationsAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                await ((IAsyncDisposable)manager).DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A non-empty provider requires the device security gate before any application binding can be installed.
        /// </summary>
        [Test]
        public async Task ProvisionableDeviceNodeManagerRejectsApplicationsWithoutSecurityProvider()
        {
            var applications = new Mock<IProvisionableDeviceApplicationsProvider>(MockBehavior.Strict);
            applications.Setup(provider => provider.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>(
                [
                    new(Mock.Of<IApplicationConfigurationPushTarget>(),
                        Mock.Of<IPushConfigurationAuthorization>())
                ]));
            IAsyncNodeManager manager = await CreateProvisionableDeviceNodeManagerAsync(
                isSingleton: false, applications.Object).ConfigureAwait(false);

            try
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false)).ConfigureAwait(false);

                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(error.Message, Does.Contain("security provider"));
                applications.Verify(provider =>
                    provider.GetApplicationsAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                await ((IAsyncDisposable)manager).DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Duplicate application URIs fail address-space creation, dispose earlier bindings, and permit a clean retry.
        /// </summary>
        [Test]
        public async Task ProvisionableDeviceNodeManagerRejectsDuplicateApplicationUrisAndReleasesEarlierBinding()
        {
            const string applicationUri = "urn:opcfoundation.org:tests:provisionable-device:managed-app";
            using TestTarget target = await TestTarget.CreateAsync(applicationUri).ConfigureAwait(false);
            var registration = new ManagedApplicationPushRegistration(
                target, Mock.Of<IPushConfigurationAuthorization>());
            var duplicateApplications = new Mock<IProvisionableDeviceApplicationsProvider>(MockBehavior.Strict);
            duplicateApplications.Setup(provider => provider.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>(
                    [registration, registration]));
            IProvisionableDeviceSecurity security = Mock.Of<IProvisionableDeviceSecurity>();
            IAsyncNodeManager firstManager = await CreateProvisionableDeviceNodeManagerAsync(
                isSingleton: false, duplicateApplications.Object, security).ConfigureAwait(false);

            try
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await firstManager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false)).ConfigureAwait(false);

                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
                Assert.That(error.Message, Does.Contain("unique"));
            }
            finally
            {
                await ((IAsyncDisposable)firstManager).DisposeAsync().ConfigureAwait(false);
            }

            var retryApplications = new Mock<IProvisionableDeviceApplicationsProvider>(MockBehavior.Strict);
            retryApplications.Setup(provider => provider.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>([registration]));
            IAsyncNodeManager retryManager = await CreateProvisionableDeviceNodeManagerAsync(
                isSingleton: false, retryApplications.Object, security).ConfigureAwait(false);

            try
            {
                await retryManager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);

                ushort namespaceIndex = checked((ushort)ReferenceServer.CurrentInstance.NamespaceUris.GetIndex(
                    ProvisionableDeviceNodeManagerFactory.NamespaceUri));
                var applicationNodeId = new NodeId($"Application:{applicationUri}", namespaceIndex);
                object handle = await retryManager.GetManagerHandleAsync(applicationNodeId).ConfigureAwait(false);

                Assert.That(handle, Is.TypeOf<NodeHandle>());
                var node = ((NodeHandle)handle).Node;
                Assert.That(node, Is.TypeOf<ApplicationConfigurationState>());
                Assert.That(((ApplicationConfigurationState)node).ApplicationUri!.Value, Is.EqualTo(applicationUri));
                retryApplications.Verify(provider =>
                    provider.GetApplicationsAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                await ((IAsyncDisposable)retryManager).DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A non-singleton device publishes each configured application as a separate component.
        /// </summary>
        [Test]
        public async Task ProvisionableDeviceNodeManagerPublishesMultipleApplicationComponents()
        {
            const string firstApplicationUri = "urn:opcfoundation.org:tests:provisionable-device:application:first";
            const string secondApplicationUri = "urn:opcfoundation.org:tests:provisionable-device:application:second";
            using TestTarget firstTarget = await TestTarget.CreateAsync(firstApplicationUri).ConfigureAwait(false);
            using TestTarget secondTarget = await TestTarget.CreateAsync(secondApplicationUri).ConfigureAwait(false);
            var applications = new Mock<IProvisionableDeviceApplicationsProvider>(MockBehavior.Strict);
            applications.Setup(provider => provider.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>(
                [
                    new(firstTarget, Mock.Of<IPushConfigurationAuthorization>()),
                    new(secondTarget, Mock.Of<IPushConfigurationAuthorization>())
                ]));
            IAsyncNodeManager manager = await CreateProvisionableDeviceNodeManagerAsync(
                isSingleton: false,
                applications.Object,
                Mock.Of<IProvisionableDeviceSecurity>()).ConfigureAwait(false);

            try
            {
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();
                await manager.CreateAddressSpaceAsync(externalReferences).ConfigureAwait(false);

                ushort namespaceIndex = checked((ushort)ReferenceServer.CurrentInstance.NamespaceUris.GetIndex(
                    ProvisionableDeviceNodeManagerFactory.NamespaceUri));
                var deviceNodeId = new NodeId("ProvisionableDevice", namespaceIndex);
                var firstNodeId = new NodeId($"Application:{firstApplicationUri}", namespaceIndex);
                var secondNodeId = new NodeId($"Application:{secondApplicationUri}", namespaceIndex);
                object firstHandle = await manager.GetManagerHandleAsync(firstNodeId).ConfigureAwait(false);
                object secondHandle = await manager.GetManagerHandleAsync(secondNodeId).ConfigureAwait(false);
                Assert.That(firstHandle, Is.TypeOf<NodeHandle>());
                Assert.That(secondHandle, Is.TypeOf<NodeHandle>());
                Assert.That(((NodeHandle)firstHandle).Node, Is.TypeOf<ApplicationConfigurationState>());
                Assert.That(((NodeHandle)secondHandle).Node, Is.TypeOf<ApplicationConfigurationState>());
                Assert.That(((ApplicationConfigurationState)((NodeHandle)firstHandle).Node)
                    .ApplicationUri!.Value, Is.EqualTo(firstApplicationUri));
                Assert.That(((ApplicationConfigurationState)((NodeHandle)secondHandle).Node)
                    .ApplicationUri!.Value, Is.EqualTo(secondApplicationUri));
                Assert.That(externalReferences[deviceNodeId], Has.Count.EqualTo(2));
                Assert.That(externalReferences[deviceNodeId].Select(reference => reference.TargetId)
                    .ToArray(), Is.EquivalentTo(new[] { firstNodeId, secondNodeId }));
                applications.Verify(provider =>
                    provider.GetApplicationsAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                await ((IAsyncDisposable)manager).DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A configuration provider cannot be shared by sibling bindings, and shutdown releases its reservation.
        /// </summary>
        [Test]
        public async Task ManagedConfigurationFileProviderCannotBeSharedAndIsReleasedAfterDisposal()
        {
            using var first = await TestTarget.CreateAsync("urn:managed:file-isolation:first").ConfigureAwait(false);
            using var second = await TestTarget.CreateAsync("urn:managed:file-isolation:second").ConfigureAwait(false);
            Mock<IRecoverableApplicationConfigurationFileProvider> file = CreateConfigurationFileProvider();
            var firstRegistration = new ManagedApplicationPushRegistration(
                first, Mock.Of<IPushConfigurationAuthorization>(), configurationFileProvider: file.Object);
            var secondRegistration = new ManagedApplicationPushRegistration(
                second, Mock.Of<IPushConfigurationAuthorization>(), configurationFileProvider: file.Object);
            await using (DefaultManagedApplicationsNodeManager rejected =
                CreateRegisteredManager(firstRegistration, secondRegistration))
            {
                ArgumentException? error = await Assert.ThrowsAsync<ArgumentException>(async () =>
                    await rejected.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(error!.ParamName, Is.EqualTo("configurationFileProvider"));
                Assert.That(rejected.ApplicationNodes.Keys,
                    Is.EquivalentTo(new[] { first.Configuration.ApplicationUri }));
            }

            await using DefaultManagedApplicationsNodeManager retry = CreateRegisteredManager(secondRegistration);
            await retry.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            Assert.That(retry.ApplicationNodes[second.Configuration.ApplicationUri!].ConfigurationFile, Is.Not.Null);
        }

        /// <summary>
        /// Recovery failure does not publish an application or retain a reservation that prevents a corrected retry.
        /// </summary>
        [Test]
        public async Task ManagedConfigurationRecoveryFailureDoesNotPublishApplicationOrRetainBinding()
        {
            using var target = await TestTarget.CreateAsync("urn:managed:file-recovery-failure").ConfigureAwait(false);
            Mock<IRecoverableApplicationConfigurationFileProvider> file = CreateConfigurationFileProvider();
            file.Setup(provider => provider.GetPendingUpdateAsync(It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadResourceUnavailable));
            var registration = new ManagedApplicationPushRegistration(
                target, Mock.Of<IPushConfigurationAuthorization>(), configurationFileProvider: file.Object);
            await using (DefaultManagedApplicationsNodeManager rejected = CreateRegisteredManager(registration))
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await rejected.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                        .ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                Assert.That(rejected.ApplicationNodes, Is.Empty);
            }

            file.Setup(provider => provider.GetPendingUpdateAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask<PendingApplicationConfigurationUpdate?>));
            await using DefaultManagedApplicationsNodeManager retry = CreateRegisteredManager(registration);
            await retry.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>()).ConfigureAwait(false);
            Assert.That(retry.ApplicationNodes[target.Configuration.ApplicationUri!].ConfigurationFile, Is.Not.Null);
        }

        /// <summary>
        /// The real factory recovers the selected application's update before publishing stable file nodes.
        /// </summary>
        [TestCase("applied")]
        [TestCase("due")]
        [TestCase("expired")]
        public async Task ProvisionableDeviceFactoryRecoversApplicationConfigurationBeforePublishing(string state)
        {
            using var target = await TestTarget.CreateAsync("urn:managed:device:file-recovery").ConfigureAwait(false);
            Mock<IRecoverableApplicationConfigurationFileProvider> fileProvider = CreateConfigurationFileProvider();
            Uuid updateId = Uuid.NewUuid();
            var pending = new PendingApplicationConfigurationUpdate(
                updateId, DateTime.UtcNow.AddMinutes(-10),
                state == "expired" ? DateTime.UtcNow.AddMinutes(-5) : null,
                state != "due");
            fileProvider.Setup(provider => provider.GetPendingUpdateAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<PendingApplicationConfigurationUpdate?>(pending));
            var applications = new Mock<IProvisionableDeviceApplicationsProvider>();
            applications.Setup(provider => provider.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>(
                [
                    new(target, Mock.Of<IPushConfigurationAuthorization>(),
                        configurationFileProvider: fileProvider.Object)
                ]));
            var security = new Mock<IProvisionableDeviceSecurity>();
            security.Setup(value => value.CreateApplicationOperations()).Returns(new TestApplicationOperations());
            IAsyncNodeManager manager = await CreateProvisionableDeviceNodeManagerAsync(
                false, applications.Object, security.Object).ConfigureAwait(false);
            try
            {
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                ushort namespaceIndex = checked((ushort)ReferenceServer.CurrentInstance.NamespaceUris.GetIndex(
                    ProvisionableDeviceNodeManagerFactory.NamespaceUri));
                object handle = await manager.GetManagerHandleAsync(
                    new NodeId($"Application:{target.Configuration.ApplicationUri}", namespaceIndex))
                    .ConfigureAwait(false);
                var application = (ApplicationConfigurationState)((NodeHandle)handle).Node;
                ApplicationConfigurationFileState file = application.ConfigurationFile!;
                Assert.That(file, Is.Not.Null);
                var children = new List<BaseInstanceState>();
                file.GetChildren(((AsyncCustomNodeManager)manager).SystemContext, children);
                Assert.That(children, Is.Not.Empty);
                Assert.That(children.Select(child => child.NodeId.NamespaceIndex),
                    Has.All.EqualTo(namespaceIndex));
                Assert.That(children.Select(child => child.NodeId).Distinct().Count(), Is.EqualTo(children.Count));
                fileProvider.Verify(provider => provider.GetPendingUpdateAsync(
                    It.IsAny<CancellationToken>()), Times.Once);
                fileProvider.Verify(provider => provider.ApplyPendingUpdateAsync(
                    updateId, It.IsAny<CancellationToken>()), state == "due" ? Times.Once() : Times.Never());
                fileProvider.Verify(provider => provider.RevertPendingUpdateAsync(
                    updateId, It.IsAny<CancellationToken>()), state == "expired" ? Times.Once() : Times.Never());

                ConfigurationFileConfirmUpdateMethodStateResult confirmation = await file.ConfirmUpdate!.OnCallAsync!(
                    ((AsyncCustomNodeManager)manager).SystemContext, file.ConfirmUpdate, file.NodeId,
                    updateId, CancellationToken.None).ConfigureAwait(false);
                Assert.That(confirmation.ServiceResult.StatusCode,
                    Is.EqualTo(state == "expired" ? StatusCodes.BadInvalidArgument : StatusCodes.Good));
                fileProvider.Verify(provider => provider.ConfirmPendingUpdateAsync(
                    updateId, It.IsAny<CancellationToken>()), state == "expired" ? Times.Never() : Times.Once());
                security.Verify(value => value.RequireInstalledRegistrarAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                await ((IAsyncDisposable)manager).DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Configuration-file reads and writes cannot bypass registrar policy or survive deployment revocation.
        /// </summary>
        [Test]
        public async Task ProvisionableDeviceFactoryGatesApplicationConfigurationFileAccess()
        {
            using var target = await TestTarget.CreateAsync("urn:managed:device:file-gate").ConfigureAwait(false);
            Mock<IRecoverableApplicationConfigurationFileProvider> fileProvider = CreateConfigurationFileProvider();
            var applications = new Mock<IProvisionableDeviceApplicationsProvider>();
            applications.Setup(provider => provider.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>(
                [
                    new(target, Mock.Of<IPushConfigurationAuthorization>(),
                        configurationFileProvider: fileProvider.Object)
                ]));
            var operations = new TestApplicationOperations();
            var security = new Mock<IProvisionableDeviceSecurity>();
            security.Setup(value => value.CreateApplicationOperations()).Returns(operations);
            security.Setup(value => value.RequireInstalledRegistrarAsync(
                It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadUserAccessDenied));
            IAsyncNodeManager manager = await CreateProvisionableDeviceNodeManagerAsync(
                false, applications.Object, security.Object).ConfigureAwait(false);
            try
            {
                await manager.CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                    .ConfigureAwait(false);
                ushort namespaceIndex = checked((ushort)ReferenceServer.CurrentInstance.NamespaceUris.GetIndex(
                    ProvisionableDeviceNodeManagerFactory.NamespaceUri));
                object handle = await manager.GetManagerHandleAsync(
                    new NodeId($"Application:{target.Configuration.ApplicationUri}", namespaceIndex))
                    .ConfigureAwait(false);
                var application = (ApplicationConfigurationState)((NodeHandle)handle).Node;
                ApplicationConfigurationFileState file = application.ConfigurationFile!;
                ServerSystemContext context = ((AsyncCustomNodeManager)manager).SystemContext;
                ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await file.Open!.OnCallAsync!(context, file.Open, file.NodeId, (byte)OpenFileMode.Read,
                        CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                fileProvider.Verify(provider => provider.ReadConfigurationAsync(
                    It.IsAny<CancellationToken>()), Times.Never);
                security.Setup(value => value.RequireInstalledRegistrarAsync(
                    It.IsAny<ISystemContext>(), It.IsAny<CancellationToken>())).Returns(default(ValueTask));
                OpenMethodStateResult opened = await file.Open!.OnCallAsync!(
                    context, file.Open, file.NodeId, (byte)(OpenFileMode.Read | OpenFileMode.Write),
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(opened.ServiceResult), Is.True);
                operations.Revoked = true;

                ServiceResultException? revoked = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await file.Write!.OnCallAsync!(
                        context, file.Write, file.NodeId, opened.FileHandle,
                        ByteString.From(new byte[] { 1 }), CancellationToken.None).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(revoked!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                fileProvider.Verify(provider => provider.BeginUpdateAsync(
                    It.IsAny<Uuid>(), It.IsAny<ByteString>(), It.IsAny<ArrayOf<ConfigurationUpdateTargetType>>(),
                    It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                await ((IAsyncDisposable)manager).DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates an application-scoped provider whose applied version is observable through the file node.
        /// </summary>
        private static Mock<IRecoverableApplicationConfigurationFileProvider> CreateConfigurationFileProvider()
        {
            uint version = 1;
            var provider = new Mock<IRecoverableApplicationConfigurationFileProvider>();
            provider.SetupGet(value => value.CurrentVersion).Returns(() => version);
            provider.SetupGet(value => value.LastUpdateTime).Returns(DateTime.UtcNow);
            provider.SetupGet(value => value.RequiresConfirmation).Returns(true);
            provider.Setup(value => value.ApplyPendingUpdateAsync(It.IsAny<Uuid>(), It.IsAny<CancellationToken>()))
                .Callback(() => version++)
                .Returns(default(ValueTask));
            return provider;
        }

        /// <summary>
        /// Creates the provider-backed device node manager over the fixture's already-hosted in-process server.
        /// </summary>
        private Task<IAsyncNodeManager> CreateProvisionableDeviceNodeManagerAsync(
            bool isSingleton,
            IProvisionableDeviceApplicationsProvider applications,
            IProvisionableDeviceSecurity? security = null)
        {
            var runtime = new Mock<IProvisionableDeviceRuntime>();
            runtime.SetupGet(value => value.IsSingleton).Returns(isSingleton);
            var factory = new ProvisionableDeviceNodeManagerFactory(runtime.Object, applications, security);
            return factory.CreateAsync(ReferenceServer.CurrentInstance, ServerFixture.Config).AsTask();
        }

        /// <summary>
        /// Resolves standard browse names relative to the application instance rather than namespace-zero ids.
        /// </summary>
        private static async ValueTask<NodeId> ResolveChildAsync(
            Opc.Ua.Client.ISession session,
            NodeId parent,
            ArrayOf<string> names,
            CancellationToken cancellationToken)
        {
            var elements = new RelativePathElement[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                elements[i] = new RelativePathElement
                {
                    ReferenceTypeId = ReferenceTypeIds.HasChild,
                    IncludeSubtypes = true,
                    IsInverse = false,
                    TargetName = new QualifiedName(names[i])
                };
            }
            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                [new BrowsePath
                {
                    StartingNode = parent,
                    RelativePath = new RelativePath { Elements = elements }
                }],
                cancellationToken).ConfigureAwait(false);
            Assert.That(response.Results, Has.Count.EqualTo(1));
            BrowsePathResult result = response.Results[0];
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(result.Targets, Has.Count.EqualTo(1));
            Assert.That(result.Targets[0].RemainingPathIndex, Is.EqualTo(uint.MaxValue));
            return ExpandedNodeId.ToNodeId(result.Targets[0].TargetId, session.NamespaceUris);
        }

        /// <summary>
        /// Creates a node manager whose provider returns exactly the independent applications under test.
        /// </summary>
        private DefaultManagedApplicationsNodeManager CreateManager(params TestTarget[] targets)
        {
            var registrations = new List<ManagedApplicationPushRegistration>();
            foreach (TestTarget target in targets)
            {
                registrations.Add(new ManagedApplicationPushRegistration(
                    target, Mock.Of<IPushConfigurationAuthorization>()));
            }
            return CreateRegisteredManager(registrations.ToArray());
        }

        /// <summary>
        /// Creates a runtime-backed manager with explicit application-scoped optional providers.
        /// </summary>
        private DefaultManagedApplicationsNodeManager CreateRegisteredManager(
            params ManagedApplicationPushRegistration[] registrations)
        {
            var provider = new Mock<IManagedApplicationPushProvider>();
            provider.Setup(p => p.GetApplicationsAsync(It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<ManagedApplicationPushRegistration>>(ArrayOf.Wrapped(registrations)));
            return new DefaultManagedApplicationsNodeManager(
                ReferenceServer.CurrentInstance, ServerFixture.Config, provider.Object);
        }

        /// <summary>
        /// Executes admitted application operations and allows tests to revoke the deployment.
        /// </summary>
        private sealed class TestApplicationOperations : IApplicationConfigurationPushOperations
        {
            /// <summary>
            /// Gets or sets whether the deployment has been reset.
            /// </summary>
            public bool Revoked { get; set; }

            /// <inheritdoc/>
            public ValueTask<T> ExecuteAsync<T>(
                Func<ValueTask<T>> operation,
                CancellationToken cancellationToken = default)
            {
                RequireActive(cancellationToken);
                return operation();
            }

            /// <inheritdoc/>
            public ValueTask ExecuteAsync(Func<ValueTask> operation, CancellationToken cancellationToken = default)
            {
                RequireActive(cancellationToken);
                return operation();
            }

            /// <summary>
            /// Rejects calls after the test revokes the deployment.
            /// </summary>
            private void RequireActive(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Revoked)
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState);
                }
            }
        }

        /// <summary>
        /// Owns an independent real certificate manager and directory stores while recording target-only effects.
        /// </summary>
        private sealed class TestTarget : IApplicationConfigurationPushTarget, IDisposable
        {
            /// <summary>
            /// Allocates independent store paths under the test output directory, not the operating-system temp root.
            /// </summary>
            private TestTarget(string applicationUri)
            {
                m_root = Path.Combine(
                    TestContext.CurrentContext.WorkDirectory, "managed-push", Guid.NewGuid().ToString("N"));
                Configuration = new ApplicationConfiguration
                {
                    ApplicationUri = applicationUri,
                    ApplicationName = "Managed Push Test",
                    ApplicationType = ApplicationType.Server,
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        ApplicationCertificates =
                        [
                            new CertificateIdentifier
                            {
                                StorePath = Path.Combine(m_root, "own"),
                                StoreType = CertificateStoreType.Directory,
                                SubjectName = "CN=Managed Push Test",
                                CertificateType = CoreObjectTypeIds.RsaSha256ApplicationCertificateType
                            }
                        ],
                        TrustedPeerCertificates = new CertificateTrustList
                        {
                            StorePath = Path.Combine(m_root, "trusted"),
                            StoreType = CertificateStoreType.Directory
                        },
                        TrustedIssuerCertificates = new CertificateTrustList
                        {
                            StorePath = Path.Combine(m_root, "issuers"),
                            StoreType = CertificateStoreType.Directory
                        },
                        RejectedCertificateStore = new CertificateStoreIdentifier(
                            Path.Combine(m_root, "rejected"), CertificateStoreType.Directory)
                    },
                    ServerConfiguration = new ServerConfiguration()
                };
                m_certificates = CertificateManagerFactory.Create(Configuration.SecurityConfiguration, s_telemetry);
                Configuration.CertificateManager = m_certificates;
            }

            /// <inheritdoc/>
            public ApplicationConfiguration Configuration { get; }

            /// <inheritdoc/>
            public ArrayOf<EndpointDescription> Endpoints => [];

            /// <inheritdoc/>
            public ISecurityPolicyRegistry? SecurityPolicies => null;

            /// <summary>
            /// Gets the certificate groups activated by successful commits.
            /// </summary>
            public ArrayOf<NodeId> ActivatedGroups
            {
                get
                {
                    lock (m_effects)
                    {
                        return m_activatedGroups.ToArrayOf();
                    }
                }
            }

            /// <summary>
            /// Gets previous certificates passed to target-specific channel retirement.
            /// </summary>
            public ArrayOf<ApplicationCertificateRotation> PreviousCertificates
            {
                get
                {
                    lock (m_effects)
                    {
                        return m_previousCertificates.ToArrayOf();
                    }
                }
            }

            /// <summary>
            /// Gets committed target trust effects.
            /// </summary>
            public ArrayOf<TrustListChangeEffect> TrustEffects
            {
                get
                {
                    lock (m_effects)
                    {
                        return m_trustEffects.ToArrayOf();
                    }
                }
            }

            /// <summary>
            /// Gets completion after the first target certificate activation.
            /// </summary>
            public Task Activation => m_activation.Task;

            /// <summary>
            /// Gets completion after the first target trust revalidation.
            /// </summary>
            public Task TrustActivation => m_trustActivation.Task;

            /// <summary>
            /// Creates and loads the initial application instance certificate through its real private store.
            /// </summary>
            public static async Task<TestTarget> CreateAsync(string applicationUri)
            {
                var target = new TestTarget(applicationUri);
                try
                {
                    using Certificate certificate = CertificateBuilder.Create("CN=Managed Push Test")
                        .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                        .SetLifeTime(TimeSpan.FromDays(365))
                        .AddExtension(new X509SubjectAltNameExtension(applicationUri, ["localhost"]))
                        .SetRSAKeySize(2048)
                        .CreateForRSA();
                    CertificateIdentifier identifier =
                        target.Configuration.SecurityConfiguration.ApplicationCertificates[0];
                    using ICertificateStore store = new CertificateStoreIdentifier(
                        identifier.StorePath!, identifier.StoreType!, noPrivateKeys: false).OpenStore(s_telemetry)!;
                    await store.AddAsync(certificate).ConfigureAwait(false);
                    identifier.Thumbprint = certificate.Thumbprint;
                    await target.m_certificates.UpdateAsync(target.Configuration.SecurityConfiguration, applicationUri)
                        .ConfigureAwait(false);
                    Assert.That(target.CurrentThumbprint(), Is.EqualTo(certificate.Thumbprint));
                    return target;
                }
                catch
                {
                    target.Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Creates a configured but unoccupied application certificate slot.
            /// </summary>
            public static TestTarget CreateEmpty(string applicationUri)
            {
                return new TestTarget(applicationUri);
            }

            /// <summary>
            /// Gets the currently registered certificate rather than cached endpoint metadata.
            /// </summary>
            public string CurrentThumbprint()
            {
                using CertificateEntry? current = m_certificates.AcquireApplicationCertificateByType(
                    CoreObjectTypeIds.RsaSha256ApplicationCertificateType);
                return current!.Certificate.Thumbprint;
            }

            /// <inheritdoc/>
            public ValueTask ApplyCertificateChangesAsync(
                ArrayOf<NodeId> certificateGroups,
                ArrayOf<ApplicationCertificateRotation> previousCertificates,
                CancellationToken cancellationToken = default)
            {
                lock (m_effects)
                {
                    m_activatedGroups.AddRange(certificateGroups.ToList());
                    m_previousCertificates.AddRange(previousCertificates.ToList());
                }
                m_activation.TrySetResult(true);
                return default;
            }

            /// <inheritdoc/>
            public ValueTask ApplyTrustListChangesAsync(
                ArrayOf<TrustListChangeEffect> effects,
                CancellationToken cancellationToken = default)
            {
                lock (m_effects)
                {
                    m_trustEffects.AddRange(effects.ToList());
                }
                m_trustActivation.TrySetResult(true);
                return default;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_certificates.Dispose();
                if (Directory.Exists(m_root))
                {
                    Directory.Delete(m_root, true);
                }
            }

            /// <summary>The isolated test store root.</summary>
            private readonly string m_root;

            /// <summary>The target-owned certificate manager.</summary>
            private readonly CertificateManager m_certificates;

            /// <summary>Protects effect snapshots while asynchronous enforcement updates them.</summary>
            private readonly Lock m_effects = new();

            /// <summary>The activated certificate groups.</summary>
            private readonly List<NodeId> m_activatedGroups = [];

            /// <summary>The public certificates supplied for channel retirement.</summary>
            private readonly List<ApplicationCertificateRotation> m_previousCertificates = [];

            /// <summary>The observed target trust effects.</summary>
            private readonly List<TrustListChangeEffect> m_trustEffects = [];

            /// <summary>Signals target certificate activation without polling the live registry.</summary>
            private readonly TaskCompletionSource<bool> m_activation =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Signals target trust revalidation without polling effect state.</summary>
            private readonly TaskCompletionSource<bool> m_trustActivation =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>The test telemetry context.</summary>
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();
    }
}
