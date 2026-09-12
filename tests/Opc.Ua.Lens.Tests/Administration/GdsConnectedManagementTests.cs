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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Client;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using UaLens.Connection;
using UaLens.Plugins.Gds;
using UaLens.Plugins.GdsManagement;
using UaLens.Subscriptions;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class GdsConnectedManagementTests
{
    [TestCase(false)]
    [TestCase(true)]
    public Task RefreshResolvesRecordsAndRetainsUnresolvedApplicationsWithoutReplacingThePrimary(bool findFails)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new Scenario();
            await scenario.ConnectAsync().ConfigureAwait(true);
            var originalIdentity = new UserIdentity(new AnonymousIdentityToken());
            scenario.Primary.SetupGet(session => session.Identity).Returns(originalIdentity);
            scenario.Applications =
            [
                Description("urn:factory:assembly", "Initial assembly"),
                Description("urn:factory:pending", "Pending registration"),
                Description(string.Empty, "Local placeholder")
            ];
            var findFailure = new IOException("registration lookup unavailable");
            scenario.Client.Setup(client => client.FindApplicationAsync(
                "urn:factory:assembly", CancellationToken.None))
                .Returns(() => findFails
                    ? ValueTask.FromException<ArrayOf<ApplicationRecordDataType>>(findFailure)
                    : ValueTask.FromResult<ArrayOf<ApplicationRecordDataType>>([Record()]));
            scenario.Client.Setup(client => client.FindApplicationAsync(
                "urn:factory:pending", CancellationToken.None))
                .ReturnsAsync(ArrayOf<ApplicationRecordDataType>.Empty);
            await using var plugin = scenario.CreatePlugin();

            await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);

            Assert.That(plugin.AllApps, Has.Count.EqualTo(3));
            Assert.That(plugin.FilteredApps, Is.EqualTo(plugin.AllApps));
            Assert.That(plugin.AllApps[0].ApplicationId.IsNull, Is.EqualTo(findFails));
            Assert.That(plugin.AllApps[0].ApplicationName, Is.EqualTo(findFails ? "Initial assembly" : "Assembly"));
            Assert.That(plugin.AllApps[1].ApplicationId.IsNull, Is.True);
            Assert.That(plugin.AllApps[2].ApplicationName, Is.EqualTo("Local placeholder"));
            Assert.That(plugin.LastOperationResult, Is.EqualTo("Refreshed: 3 applications."));
            Assert.That(plugin.HasSecondarySession, Is.True);
            Assert.That(plugin.IsBusy, Is.False);
            Assert.That(scenario.FactoryCalls, Is.EqualTo(1));
            Assert.That(scenario.Administrators.Single(), Is.Not.SameAs(originalIdentity));
            Assert.That(scenario.Administrators[0].TokenType, Is.EqualTo(UserTokenType.Anonymous));
            Assert.That(
                scenario.Context.ConnectionContext.Connection.CurrentSession,
                Is.SameAs(scenario.Primary.Object));
            scenario.Client.Verify(client => client.FindApplicationAsync(string.Empty, It.IsAny<CancellationToken>()),
                Times.Never);

            scenario.Applications = [];
            await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.That(plugin.AllApps, Is.Empty);
            Assert.That(plugin.FilteredApps, Is.Empty);
            Assert.That(plugin.LastOperationResult, Is.EqualTo("Refreshed: 0 applications."));
            Assert.That(scenario.FactoryCalls, Is.EqualTo(1));
            Assert.That(scenario.Connects, Is.EqualTo(1));
            if (findFails)
            {
                Assert.That(scenario.Context.Log.Entries.Single(entry => entry.Exception is not null).Exception,
                    Is.SameAs(findFailure));
            }
        });
    }

    [Test]
    public Task PendingRefreshRejectsDuplicateExecutionAndPublishesOnlyItsCompletedResult()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new Scenario();
            await scenario.ConnectAsync().ConfigureAwait(true);
            var completion = new TaskCompletionSource<ArrayOf<ApplicationDescription>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            scenario.Query = () => completion.Task;
            await using var plugin = scenario.CreatePlugin();
            Task first = plugin.RefreshCommand.ExecuteAsync(null);
            try
            {
                Assert.That(plugin.IsBusy, Is.True);
                await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
                Assert.That(scenario.Queries, Is.EqualTo(1));
                Assert.That(plugin.AllApps, Is.Empty);
                completion.SetResult([Description(string.Empty, "Completed snapshot")]);
                await first.ConfigureAwait(true);
                Assert.That(plugin.IsBusy, Is.False);
                Assert.That(plugin.AllApps.Single().ApplicationName, Is.EqualTo("Completed snapshot"));
            }
            finally
            {
                completion.TrySetResult([]);
                await first.ConfigureAwait(true);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task RefreshFailurePreservesThePreviousSnapshotAndReleasesTheBusyState(bool cancel)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new Scenario();
            await scenario.ConnectAsync().ConfigureAwait(true);
            scenario.Applications = [Description(string.Empty, "Known snapshot")];
            await using var plugin = scenario.CreatePlugin();
            await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            RegisteredApp previous = plugin.AllApps.Single();
            Exception failure = cancel
                ? new OperationCanceledException("query canceled")
                : new IOException("query unavailable");
            scenario.Query = () => Task.FromException<ArrayOf<ApplicationDescription>>(failure);

            await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);

            Assert.That(plugin.AllApps.Single(), Is.SameAs(previous));
            Assert.That(plugin.FilteredApps.Single(), Is.SameAs(previous));
            Assert.That(plugin.IsBusy, Is.False);
            Assert.That(plugin.LastOperationResult, Is.EqualTo($"Refresh failed: {failure.Message}"));
            Assert.That(scenario.Context.Log.Entries.Single(entry => entry.Exception is not null).Exception,
                Is.SameAs(failure));
            Assert.That(scenario.Context.ConnectionContext.Connection.IsConnected, Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task CertificateGroupsKeepIndependentFailuresAndProjectOnlyValidCertificates(bool denied)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new Scenario();
            await scenario.ConnectAsync().ConfigureAwait(true);
            using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Group peer");
            NodeId application = new("registered", 2);
            NodeId group = new("ApplicationGroup", 2);
            NodeId missing = new("NoTrustList", 2);
            NodeId faulted = new("DeniedGroup", 2);
            NodeId trust = new("ApplicationTrust", 2);
            scenario.Client.Setup(client => client.GetCertificateGroupsAsync(application, CancellationToken.None))
                .ReturnsAsync((ArrayOf<NodeId>)[group, missing, faulted]);
            scenario.Client.Setup(client => client.GetTrustListAsync(application, group, CancellationToken.None))
                .ReturnsAsync(trust);
            scenario.Client.Setup(client => client.GetTrustListAsync(application, missing, CancellationToken.None))
                .ReturnsAsync(NodeId.Null);
            var failure = new ServiceResultException(StatusCodes.BadUserAccessDenied);
            scenario.Client.Setup(client => client.GetTrustListAsync(application, faulted, CancellationToken.None))
                .Returns(() => ValueTask.FromException<NodeId>(failure));
            scenario.Client.Setup(client => client.ReadTrustListAsync(trust, CancellationToken.None))
                .Returns(() => denied
                    ? ValueTask.FromException<TrustListDataType>(failure)
                    : ValueTask.FromResult(new TrustListDataType
                    {
                        TrustedCertificates = [new ByteString(certificate.RawData), ByteString.Empty],
                        IssuerCertificates = [new ByteString(certificate.RawData), s_invalidCertificate]
                    }));
            await using var plugin = scenario.CreatePlugin();
            ApplicationRecordDataType selected = Record();
            selected.ApplicationId = application;
            plugin.SelectedApp = RegisteredApp.FromRecord(selected);

            await plugin.ViewCertGroupsCommand.ExecuteAsync(null).ConfigureAwait(true);

            Assert.That(
                plugin.CertGroups.Select(value => value.GroupId),
                Is.EqualTo(new[] { group, missing, faulted }));
            Assert.That(plugin.CertGroups[1].Trusted, Is.Empty);
            Assert.That(plugin.CertGroups[2].Issuers, Is.Empty);
            Assert.That(plugin.CertGroups[0].Trusted, Has.Count.EqualTo(denied ? 0 : 1));
            Assert.That(plugin.CertGroups[0].Issuers, Has.Count.EqualTo(denied ? 0 : 1));
            if (!denied)
            {
                Assert.That(plugin.CertGroups[0].Trusted[0].Thumbprint, Is.EqualTo(certificate.Thumbprint));
                Assert.That(plugin.CertGroups[0].Trusted[0].Subject, Is.EqualTo("Group peer"));
                Assert.That(plugin.CertGroups[0].Trusted[0].Bucket, Is.EqualTo("Trusted"));
                Assert.That(plugin.CertGroups[0].Issuers[0].Bucket, Is.EqualTo("Issuer"));
            }
            Assert.That(plugin.IsBusy, Is.False);
            Assert.That(plugin.LastOperationResult, Is.EqualTo("Cert groups loaded for Assembly: 3 group(s)."));
            Assert.That(scenario.Context.Log.Entries.Count(entry => entry.Exception is not null),
                Is.EqualTo(denied ? 2 : 1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task DisconnectAndDisposeReleaseOnlyTheSecondaryClientEvenWhenDisconnectFails(bool fail)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new Scenario();
            await scenario.ConnectAsync().ConfigureAwait(true);
            scenario.Applications = [Description(string.Empty, "Previous entry")];
            var plugin = scenario.CreatePlugin();
            try
            {
                await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
                scenario.DisconnectFailure = fail ? new IOException("secondary already gone") : null;
                await plugin.DisconnectCommand.ExecuteAsync(null).ConfigureAwait(true);
                Assert.That(plugin.HasSecondarySession, Is.False);
                Assert.That(plugin.AllApps, Is.Empty);
                Assert.That(plugin.FilteredApps, Is.Empty);
                Assert.That(plugin.CertGroups, Is.Empty);
                Assert.That(plugin.SelectedAppDetail, Is.EqualTo("No application selected."));
                Assert.That(scenario.Disconnects, Is.EqualTo(1));
                Assert.That(scenario.Disposals, Is.EqualTo(1));
                Assert.That(scenario.Context.ConnectionContext.Connection.CurrentSession,
                    Is.SameAs(scenario.Primary.Object));
                Assert.That(scenario.Context.ConnectionContext.Connection.IsConnected, Is.True);
            }
            finally
            {
                await plugin.DisposeAsync().ConfigureAwait(true);
            }
            Assert.That(scenario.Disconnects, Is.EqualTo(1));
            Assert.That(scenario.Disposals, Is.EqualTo(1));
        });
    }

    [TestCase(false, 0)]
    [TestCase(true, 0)]
    [TestCase(false, 1)]
    [TestCase(true, 1)]
    [TestCase(false, 2)]
    [TestCase(true, 2)]
    public Task IssuingUsesTheSelectedGroupTypeAndDomainsWithoutInventingADeliveryDestination(
        bool https,
        int contextKind)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new Scenario();
            await scenario.ConnectAsync().ConfigureAwait(true);
            using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Issued assembly");
            ApplicationRecordDataType application = Record();
            NodeId group = contextKind == 0 ? NodeId.Null : new NodeId("DefaultGroup", 2);
            NodeId request = new("issue-1", 2);
            NodeId certificateType = https
                ? Opc.Ua.ObjectTypeIds.HttpsCertificateType : Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType;
            string subject = contextKind == 0 ? "CN=Assembly" : "CN=Requested assembly";
            if (contextKind != 0)
            {
                scenario.Context.Workspace.Object.CurrentRegisteredApp = Registration(application,
                    contextKind == 1 ? GdsRegistrationType.ClientPull : GdsRegistrationType.ServerPull) with
                {
                    Domains = " assembly.example.test, , backup.example.test ",
                    CertificateSubjectName = subject
                };
            }
            scenario.Client.Setup(client => client.GetCertificateGroupsAsync(
                application.ApplicationId, CancellationToken.None))
                .ReturnsAsync(group.IsNull ? ArrayOf<NodeId>.Null : (ArrayOf<NodeId>)[group, new NodeId(9020u)]);
            ArrayOf<string> sentDomains = default;
            scenario.Client.Setup(client => client.StartNewKeyPairRequestAsync(
                application.ApplicationId, group, certificateType, subject,
                It.IsAny<ArrayOf<string>>(), "PFX", It.Is<char[]>(value => value.Length == 0),
                CancellationToken.None))
                .Callback((NodeId _, NodeId _, NodeId _, string _, ArrayOf<string> domains,
                    string _, char[] _, CancellationToken _) => sentDomains = domains)
                .ReturnsAsync(request);
            scenario.Client.Setup(client => client.FinishRequestAsync(
                application.ApplicationId, request, CancellationToken.None))
                .ReturnsAsync((new ByteString(certificate.RawData), ByteString.Empty, ArrayOf<ByteString>.Empty));
            await using var plugin = scenario.CreatePlugin();
            plugin.SelectedApp = RegisteredApp.FromRecord(application);

            await (https ? plugin.IssueNewHttpsCertificateCommand : plugin.IssueNewCertificateCommand)
                .ExecuteAsync(null).ConfigureAwait(true);

            Assert.That(sentDomains.ToArray(), Is.EqualTo(contextKind == 0 ? [] : s_domains));
            Assert.That(plugin.IsBusy, Is.False);
            Assert.That(plugin.LastOperationResult, Does.Contain("Issued assembly"));
            Assert.That(plugin.LastOperationResult, Does.Contain(https ? "HTTPS cert" : "cert"));
            if (contextKind == 0)
            {
                Assert.That(plugin.LastOperationResult,
                    Does.StartWith("No registered application context").And.Contain("cannot deliver"));
            }
            else
            {
                Assert.That(plugin.LastOperationResult, Does.Contain(
                    $"{(contextKind == 1 ? "ClientPull" : "ServerPull")} delivery: no destination paths configured"));
            }
            scenario.Client.Verify(client => client.FinishRequestAsync(
                application.ApplicationId, request, CancellationToken.None), Times.Once);
        });
    }

    [TestCase(TrustListMasks.None)]
    [TestCase(TrustListMasks.TrustedCertificates)]
    [TestCase(TrustListMasks.IssuerCertificates)]
    [TestCase(TrustListMasks.All)]
    public Task PullingTrustListsHonorsEachSpecifiedMaskAndPreservesUnrelatedLocalCertificates(TrustListMasks mask)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var stores = new TemporaryCertificateStores();
            using Certificate retained = TemporaryCertificateStores.CreateCertificate("CN=Retained local peer");
            using Certificate peer = TemporaryCertificateStores.CreateCertificate("CN=Downloaded peer");
            using Certificate issuer = TemporaryCertificateStores.CreateCertificate("CN=Downloaded issuer");
            await stores.AddAsync(stores.Peer, retained).ConfigureAwait(true);
            await stores.AddAsync(stores.Issuer, retained).ConfigureAwait(true);
            await using var scenario = new Scenario();
            await scenario.ConnectAsync().ConfigureAwait(true);
            ApplicationRecordDataType application = Record();
            scenario.Context.Workspace.Object.CurrentRegisteredApp = Registration(application) with
            {
                TrustListStorePath = stores.Peer.Identifier.StorePath,
                IssuerListStorePath = stores.Issuer.Identifier.StorePath
            };
            NodeId trust = new("DownloadedTrust", 2);
            scenario.Client.Setup(client => client.GetTrustListAsync(
                application.ApplicationId, NodeId.Null, CancellationToken.None)).ReturnsAsync(trust);
            scenario.Client.Setup(client => client.ReadTrustListAsync(trust, 0, CancellationToken.None))
                .ReturnsAsync(new TrustListDataType
                {
                    SpecifiedLists = (uint)mask,
                    TrustedCertificates = [new ByteString(peer.RawData), ByteString.Empty, s_invalidCertificate],
                    IssuerCertificates = [new ByteString(issuer.RawData), ByteString.Empty, s_invalidCertificate],
                    TrustedCrls = [ByteString.Empty, s_invalidCertificate],
                    IssuerCrls = [ByteString.Empty, s_invalidCertificate]
                });
            await using var plugin = scenario.CreatePlugin();

            await plugin.PullTrustListSaveLocallyCommand.ExecuteAsync(null).ConfigureAwait(true);

            Assert.That(plugin.LastOperationResult, Does.StartWith("Trust list pulled for Assembly:"));
            IReadOnlyList<byte[]> peers = await stores.ReadAsync(stores.Peer).ConfigureAwait(true);
            IReadOnlyList<byte[]> issuers = await stores.ReadAsync(stores.Issuer).ConfigureAwait(true);
            Assert.That(peers, Is.EquivalentTo((mask & TrustListMasks.TrustedCertificates) != 0
                ? new[] { retained.RawData, peer.RawData } : new[] { retained.RawData }));
            Assert.That(issuers, Is.EquivalentTo((mask & TrustListMasks.IssuerCertificates) != 0
                ? new[] { retained.RawData, issuer.RawData } : new[] { retained.RawData }));
            Assert.That(plugin.IsBusy, Is.False);
            if (mask == TrustListMasks.None)
            {
                Assert.That(plugin.LastOperationResult, Does.Contain("no categories written"));
                Assert.That(scenario.Context.Log.Entries.Where(entry => entry.Exception is not null), Is.Empty);
            }
            else
            {
                Assert.That(scenario.Context.Log.Entries.Where(entry => entry.Exception is not null), Is.Not.Empty);
            }
            Assert.That(await stores.ReadAsync(stores.Application).ConfigureAwait(true), Is.Empty);
        });
    }

    private static RegisteredApplicationContext Registration(
        ApplicationRecordDataType application, GdsRegistrationType type = GdsRegistrationType.ServerPull)
    {
        return new RegisteredApplicationContext(
            application.ApplicationId,
            application.ApplicationUri ?? throw new AssertionException("The fixture requires an application URI."),
            "Assembly",
            application.ProductUri ?? throw new AssertionException("The fixture requires a product URI."),
            type, [], []);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public Task IssuedPublicCertificatesUseOnlyTheRegisteredDestinationsAndSurfaceIndividualWriteFailures(
        bool https, bool failPublicFile)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var files = new TemporaryCertificateStores();
            using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Delivered certificate");
            using Certificate issuer = TemporaryCertificateStores.CreateCertificate("CN=Delivered issuer");
            await using var scenario = new Scenario();
            await scenario.ConnectAsync().ConfigureAwait(true);
            ApplicationRecordDataType application = Record();
            string publicPath = failPublicFile ? files.Root : Path.Combine(files.Root, "issued", "public.cer");
            string otherPublicPath = Path.Combine(files.Root, "other", "public.cer");
            string privatePath = Path.Combine(files.Root, "private-not-issued.pfx");
            scenario.Context.Workspace.Object.CurrentRegisteredApp = Registration(application) with
            {
                CertificateStorePath = files.Application.Identifier.StorePath,
                CertificatePublicKeyPath = https ? otherPublicPath : publicPath,
                HttpsCertificatePublicKeyPath = https ? publicPath : otherPublicPath,
                CertificatePrivateKeyPath = privatePath,
                HttpsCertificatePrivateKeyPath = privatePath,
                IssuerListStorePath = files.Issuer.Identifier.StorePath,
                HttpsIssuerListStorePath = files.Issuer.Identifier.StorePath
            };
            scenario.Client.Setup(client => client.GetCertificateGroupsAsync(
                application.ApplicationId, CancellationToken.None)).ReturnsAsync(ArrayOf<NodeId>.Empty);
            scenario.Client.Setup(client => client.StartNewKeyPairRequestAsync(
                application.ApplicationId, NodeId.Null,
                https
                    ? Opc.Ua.ObjectTypeIds.HttpsCertificateType
                    : Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType,
                "CN=Assembly", It.IsAny<ArrayOf<string>>(), "PFX", It.IsAny<char[]>(), CancellationToken.None))
                .ReturnsAsync(new NodeId(9001u));
            scenario.Client.Setup(client => client.FinishRequestAsync(
                application.ApplicationId, new NodeId(9001u), CancellationToken.None))
                .ReturnsAsync((new ByteString(certificate.RawData), ByteString.Empty,
                    (ArrayOf<ByteString>)[new ByteString(issuer.RawData)]));
            await using var plugin = scenario.CreatePlugin();
            plugin.SelectedApp = RegisteredApp.FromRecord(application);
            await (https ? plugin.IssueNewHttpsCertificateCommand : plugin.IssueNewCertificateCommand)
                .ExecuteAsync(null).ConfigureAwait(true);
            Assert.That(File.Exists(otherPublicPath), Is.False);
            Assert.That(File.Exists(privatePath), Is.False);
            if (failPublicFile)
            {
                Assert.That(plugin.LastOperationResult, Does.Contain("pub!"));
                Assert.That(scenario.Context.Log.Entries.Any(entry => entry.Exception is not null), Is.True);
            }
            else
            {
                Assert.That(
                    await File.ReadAllBytesAsync(publicPath).ConfigureAwait(true),
                    Is.EqualTo(certificate.RawData));
                Assert.That(plugin.LastOperationResult, Does.Contain("pub=" + publicPath));
            }
            IReadOnlyList<byte[]> applications = await files.ReadAsync(files.Application).ConfigureAwait(true);
            Assert.That(applications, Has.Count.EqualTo(https ? 0 : 1));
            if (!https)
            {
                Assert.That(applications[0], Is.EqualTo(certificate.RawData));
            }
            IReadOnlyList<byte[]> issuers = await files.ReadAsync(files.Issuer).ConfigureAwait(true);
            Assert.That(issuers.Single(), Is.EqualTo(issuer.RawData));
            Assert.That(plugin.IsBusy, Is.False);
        });
    }

    private static ApplicationDescription Description(string uri, string name)
    {
        return new ApplicationDescription
        {
            ApplicationUri = uri,
            ApplicationName = new LocalizedText(name),
            ProductUri = "urn:factory:product",
            ApplicationType = ApplicationType.Server,
            DiscoveryUrls = ["opc.tcp://gds.example.test:4840/Assembly"]
        };
    }

    private static ApplicationRecordDataType Record()
    {
        return new ApplicationRecordDataType
        {
            ApplicationId = new NodeId("registered", 2),
            ApplicationUri = "urn:factory:assembly",
            ApplicationNames = [new LocalizedText("Assembly")],
            ProductUri = "urn:factory:product",
            ApplicationType = ApplicationType.Server,
            DiscoveryUrls = ["opc.tcp://gds.example.test:4840/Assembly"],
            ServerCapabilities = ["DA", "HA"]
        };
    }

    private sealed class Scenario : IAsyncDisposable
    {
        public Scenario()
        {
            Endpoint = new EndpointDescription("opc.tcp://gds.example.test:4840/Directory")
            {
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                TransportProfileUri = Profiles.UaTcpTransport,
                Server = Description("urn:factory:gds", "Factory GDS"),
                UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "anonymous" }]
            };
            Primary.SetupGet(session => session.Connected).Returns(true);
            Primary.SetupGet(session => session.SessionId).Returns(new NodeId(7001u));
            Primary.SetupGet(session => session.ConfiguredEndpoint)
                .Returns(new ConfiguredEndpoint(null, Endpoint, new EndpointConfiguration()));
            Primary.SetupGet(session => session.Identity).Returns(m_identity);
            var connection = new Mock<IConnectionSession>();
            connection.SetupGet(value => value.Session).Returns(Primary.Object);
            connection.SetupGet(value => value.State).Returns(new ConnectionSessionState(ConnectionPhase.Connected));
            connection.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
            Context.ConnectionContext.DiscoverAsync = (_, _) =>
                Task.FromResult<ArrayOf<EndpointDescription>>([Endpoint]);
            Context.ConnectionContext.Backend.Setup(backend => backend.ConnectAsync(
                It.IsAny<ApplicationConfiguration>(), It.IsAny<EndpointDescription>(),
                It.IsAny<ConnectionProfile>(), It.IsAny<IClientIdentityProvider>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(connection.Object);
            Query = () => Task.FromResult(Applications);
            Client.SetupGet(client => client.Session).Returns(() => m_connected ? Primary.Object : null);
            Client.Setup(client => client.ConnectAsync(It.IsAny<ConfiguredEndpoint>(), It.IsAny<CancellationToken>()))
                .Returns((ConfiguredEndpoint endpoint, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Assert.That(endpoint.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    Assert.That(endpoint.Description.EndpointUrl, Is.EqualTo(Endpoint.EndpointUrl));
                    Connects++;
                    m_connected = true;
                    return ValueTask.CompletedTask;
                });
            Client.Setup(client => client.QueryApplicationsAsync(
                0, 0, string.Empty, string.Empty, 0, string.Empty,
                It.Is<ArrayOf<string>>(values => values.Count == 0), CancellationToken.None))
                .Returns(async () =>
                {
                    Queries++;
                    ArrayOf<ApplicationDescription> applications = await Query().ConfigureAwait(false);
                    return (applications, DateTimeUtc.MinValue, 0u);
                });
            Client.Setup(client => client.DisconnectAsync(It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    Disconnects++;
                    m_connected = false;
                    return DisconnectFailure is null
                        ? ValueTask.CompletedTask : ValueTask.FromException(DisconnectFailure);
                });
            Client.Setup(client => client.DisposeAsync()).Returns(() =>
            {
                Disposals++;
                m_connected = false;
                return ValueTask.CompletedTask;
            });
        }

        public AdministrationWorkflowContext Context { get; } = new();
        public Mock<ISession> Primary { get; } = new();
        public Mock<IGlobalDiscoveryServerClient> Client { get; } = new(MockBehavior.Strict);
        public EndpointDescription Endpoint { get; }
        public List<IUserIdentity> Administrators { get; } = [];
        public ArrayOf<ApplicationDescription> Applications { get; set; } = [];
        public Func<Task<ArrayOf<ApplicationDescription>>> Query { get; set; }
        public int FactoryCalls { get; private set; }
        public int Connects { get; private set; }
        public int Queries { get; private set; }
        public int Disconnects { get; private set; }
        public int Disposals { get; private set; }
        public Exception? DisconnectFailure { get; set; }

        public Task ConnectAsync()
        {
            ConnectionProfile profile = ConnectionProfile.Create(
                Endpoint, Endpoint.UserIdentityTokens[0], SubscriptionEngineKind.ChannelV2);
            return Context.ConnectionContext.Connection.ConnectAsync(profile);
        }

        public GdsManagementPlugin CreatePlugin()
        {
            return new GdsManagementPlugin(Context.Host, (_, identity) =>
            {
                FactoryCalls++;
                Administrators.Add(identity);
                return Client.Object;
            });
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync().ConfigureAwait(false);
            foreach (IUserIdentity identity in Administrators)
            {
                if (identity is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private bool m_connected;
        private readonly UserIdentity m_identity = new(new AnonymousIdentityToken());
    }

    private static readonly ByteString s_invalidCertificate = new(new byte[] { 1, 2, 3 });
    private static readonly string[] s_domains = ["assembly.example.test", "backup.example.test"];
}
