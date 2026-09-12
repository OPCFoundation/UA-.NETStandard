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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;
using UaLens.Plugins.GdsPush;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class ConnectedGdsPushTests
{
    [TestCase(TrustListMasks.None)]
    [TestCase(TrustListMasks.TrustedCertificates)]
    [TestCase(TrustListMasks.IssuerCertificates)]
    [TestCase(TrustListMasks.All)]
    public Task RefreshHonorsSelectedTrustBucketsAndKeepsThePrimarySessionOwnedElsewhere(TrustListMasks masks)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new PushScenario();
            await scenario.Context.ConnectAsync().ConfigureAwait(true);
            await using var plugin = scenario.CreatePlugin();
            plugin.TrustListMasks = masks;
            await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.That(scenario.Masks, Is.EqualTo(masks));
            Assert.That(plugin.Trusted, Has.Count.EqualTo((masks & TrustListMasks.TrustedCertificates) == 0 ? 0 : 1));
            Assert.That(plugin.Issuers, Has.Count.EqualTo((masks & TrustListMasks.IssuerCertificates) == 0 ? 0 : 1));
            Assert.That(plugin.Rejected, Has.Count.EqualTo(1));
            Assert.That(plugin.Rejected[0].Subject, Is.EqualTo("Rejected peer"));
            Assert.That(plugin.HasSecondarySession, Is.True);
            Assert.That(plugin.ServerApplicationName, Is.EqualTo("Push fixture"));
            Assert.That(plugin.ServerApplicationUri, Is.EqualTo("urn:fixture:push"));
            Assert.That(scenario.Connects, Is.EqualTo(1));
            await plugin.RefreshRejectedListCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.That(scenario.TrustReads, Is.EqualTo(1));
            Assert.That(scenario.RejectedReads, Is.EqualTo(2));
            Assert.That(plugin.LastOperationResult, Is.EqualTo("Rejected list refreshed: 1 cert(s)."));
            await plugin.DisconnectCommand.ExecuteAsync(null).ConfigureAwait(true);
            Assert.That(plugin.Trusted, Is.Empty);
            Assert.That(plugin.Issuers, Is.Empty);
            Assert.That(plugin.Rejected, Is.Empty);
            Assert.That(plugin.HasSecondarySession, Is.False);
            Assert.That(scenario.Context.Desktop.Connection.CurrentSession, Is.SameAs(scenario.Context.Session.Object));
            Assert.That(scenario.Disposals, Is.EqualTo(1));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task PolledStatusDistinguishesRunningFromAnAnnouncedShutdown(bool shutdown)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new PushScenario();
            scenario.Shutdown = shutdown;
            await scenario.Context.ConnectAsync().ConfigureAwait(true);
            await using var plugin = scenario.CreatePlugin();
            await DesktopInteraction.ModelChangedAsync(plugin,
                () => plugin.StatusProductName == "Fixture product", async () =>
                {
                    await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
                    await plugin.PollOnceCommand.ExecuteAsync(null).ConfigureAwait(true);
                }).ConfigureAwait(true);
            Assert.That(plugin.StatusProductUri, Is.EqualTo("urn:fixture:product"));
            Assert.That(plugin.StatusManufacturerName, Is.EqualTo("Fixture manufacturer"));
            Assert.That(plugin.StatusSoftwareVersion, Is.EqualTo("2.0"));
            Assert.That(plugin.StatusBuildNumber, Is.EqualTo("42"));
            Assert.That(plugin.StatusState, Is.EqualTo(shutdown ? "Shutdown" : "Running"));
            Assert.That(plugin.StatusSecondsTillShutdown, Is.EqualTo(shutdown ? "20" : string.Empty));
            Assert.That(plugin.StatusShutdownReason, Is.EqualTo(shutdown ? "maintenance" : string.Empty));
            Assert.That(plugin.StatusCurrentTime, Does.Contain("2026"));
        });
    }

    [TestCase((int)TrustListBucket.Trusted, false)]
    [TestCase((int)TrustListBucket.Issuer, false)]
    [TestCase((int)TrustListBucket.Trusted, true)]
    [TestCase((int)TrustListBucket.Issuer, true)]
    [TestCase((int)TrustListBucket.Rejected, false)]
    public Task CertificateRemovalTargetsTheSelectedBucketAndReportsRejections(int bucket, bool fail)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new PushScenario();
            await scenario.Context.ConnectAsync().ConfigureAwait(true);
            await using var plugin = scenario.CreatePlugin();
            await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            plugin.ActiveBucket = (TrustListBucket)bucket;
            plugin.SelectedCertificate = bucket == (int)TrustListBucket.Trusted ? plugin.Trusted[0]
                : bucket == (int)TrustListBucket.Issuer ? plugin.Issuers[0] : plugin.Rejected[0];
            string thumbprint = plugin.SelectedCertificate.Thumbprint;
            scenario.Client.Setup(client => client.RemoveCertificateAsync(
                thumbprint, bucket == (int)TrustListBucket.Trusted, CancellationToken.None))
                .Returns(() => fail
                    ? ValueTask.FromException(new ServiceResultException(StatusCodes.BadUserAccessDenied))
                    : ValueTask.CompletedTask);
            await plugin.RemoveCertificateCommand.ExecuteAsync(null).ConfigureAwait(true);
            if (bucket == (int)TrustListBucket.Rejected)
            {
                scenario.Client.Verify(client => client.RemoveCertificateAsync(
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
                Assert.That(plugin.LastOperationResult, Does.Contain("server"));
            }
            else
            {
                scenario.Client.Verify(client => client.RemoveCertificateAsync(
                    thumbprint, bucket == (int)TrustListBucket.Trusted, CancellationToken.None), Times.Once);
                Assert.That(plugin.LastOperationResult, Does.Contain(fail ? "Remove Cert failed:" : "Removed"));
            }
            Assert.That(plugin.IsBusy, Is.False);
        });
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public Task ApplyingPendingCertificateChangesRequiresConsentAndPreservesFailure(bool accept, bool fail)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new PushScenario();
            await scenario.Context.ConnectAsync().ConfigureAwait(true);
            await using var plugin = scenario.CreatePlugin();
            await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            scenario.Client.Setup(client => client.ApplyChangesAsync(CancellationToken.None))
                .Returns(() => fail
                    ? ValueTask.FromException(new ServiceResultException(StatusCodes.BadUserAccessDenied))
                    : ValueTask.CompletedTask);
            Task applying = Task.CompletedTask;
            Window confirm = await DesktopInteraction.OpenedAsync<Window>(
                () => applying = plugin.ApplyChangesCommand.ExecuteAsync(null)).ConfigureAwait(true);
            scenario.Client.Verify(client => client.ApplyChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            Button action = confirm.GetLogicalDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, accept ? "Apply changes" : "Cancel"));
            DesktopInteraction.Click(action);
            await applying.ConfigureAwait(true);
            scenario.Client.Verify(client => client.ApplyChangesAsync(CancellationToken.None),
                accept ? Times.Once() : Times.Never());
            Assert.That(plugin.LastOperationResult, Does.Contain(!accept ? "cancelled" : fail ? "failed:" : "invoked"));
            Assert.That(plugin.IsBusy, Is.False);
        });
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public Task AddingCertificatesUsesOnlyTheChosenBucketAndRetainsTheOriginalFailure(bool issuer, bool fail)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new PushScenario();
            await scenario.Context.ConnectAsync().ConfigureAwait(true);
            await using var plugin = scenario.CreatePlugin();
            await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=New trusted peer");
            byte[]? received = null;
            scenario.Client.Setup(client => client.AddCertificateAsync(
                It.IsAny<Certificate>(), !issuer, CancellationToken.None))
                .Returns((Certificate supplied, bool _, CancellationToken _) =>
                {
                    received = supplied.RawData;
                    return fail
                        ? ValueTask.FromException(new ServiceResultException(StatusCodes.BadUserAccessDenied))
                        : ValueTask.CompletedTask;
                });
            plugin.ActiveBucket = issuer ? TrustListBucket.Issuer : TrustListBucket.Trusted;
            Task adding = Task.CompletedTask;
            AddCertificateDialog dialog = await DesktopInteraction.OpenedAsync<AddCertificateDialog>(
                () => adding = plugin.AddCertificateCommand.ExecuteAsync(null)).ConfigureAwait(true);
            DesktopInteraction.Control<TextBox>(dialog, "PemBox").Text =
                "-----BEGIN CERTIFICATE-----\n" + Convert.ToBase64String(certificate.RawData) +
                "\n-----END CERTIFICATE-----";
            Assert.That(received, Is.Null);
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
            await adding.ConfigureAwait(true);
            Assert.That(received, Is.EqualTo(certificate.RawData));
            Assert.That(plugin.LastOperationResult, Does.Contain(fail ? "Add Cert failed:" : "Added New trusted peer"));
            Assert.That(plugin.IsBusy, Is.False);
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public Task CertificateRequestUsesTheSelectedOfferedGroupWithoutImplicitlyApplyingChanges(int selected)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var scenario = new PushScenario();
            await scenario.Context.ConnectAsync().ConfigureAwait(true);
            NodeId[] groups = [new(801u), new(802u), new(803u)];
            scenario.Client.SetupGet(client => client.DefaultApplicationGroup).Returns(groups[0]);
            scenario.Client.SetupGet(client => client.DefaultHttpsGroup).Returns(groups[1]);
            scenario.Client.SetupGet(client => client.DefaultUserTokenGroup).Returns(groups[2]);
            scenario.Client.SetupGet(client => client.ApplicationCertificateType)
                .Returns(ObjectTypeIds.RsaSha256ApplicationCertificateType);
            byte[] csr = TemporaryCertificateStores.CreateSigningRequest();
            scenario.Client.Setup(client => client.CreateSigningRequestAsync(groups[selected],
                ObjectTypeIds.RsaSha256ApplicationCertificateType, "CN=Requested", true,
                It.IsAny<ByteString>(), CancellationToken.None)).ReturnsAsync(new ByteString(csr));
            await using var plugin = scenario.CreatePlugin();
            await plugin.RefreshCommand.ExecuteAsync(null).ConfigureAwait(true);
            Task requesting = Task.CompletedTask;
            CertificateRequestDialog dialog = await DesktopInteraction.OpenedAsync<CertificateRequestDialog>(
                () => requesting = plugin.RequestNewCertificateCommand.ExecuteAsync(null)).ConfigureAwait(true);
            Assert.That(DesktopInteraction.Control<ComboBox>(dialog, "GroupBox").Items, Has.Count.EqualTo(3));
            DesktopInteraction.Control<ComboBox>(dialog, "GroupBox").SelectedIndex = selected;
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "NextButton"));
            DesktopInteraction.Control<TextBox>(dialog, "SubjectBox").Text = "CN=Requested";
            DesktopInteraction.Control<CheckBox>(dialog, "RegenKeyBox").IsChecked = true;
            await DesktopInteraction.ChangedAsync(DesktopInteraction.Control<TextBox>(dialog, "CsrBox"),
                () => !string.IsNullOrEmpty(DesktopInteraction.Control<TextBox>(dialog, "CsrBox").Text), () =>
                {
                    DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "NextButton"));
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
            Assert.That(DesktopInteraction.Control<TextBox>(dialog, "CsrBox").Text,
                Does.Contain("BEGIN CERTIFICATE REQUEST"));
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
            await requesting.ConfigureAwait(true);
            Assert.That(plugin.LastOperationResult, Does.Contain("CSR generated").And.Contain("not applied"));
            scenario.Client.Verify(client => client.CreateSigningRequestAsync(groups[selected],
                ObjectTypeIds.RsaSha256ApplicationCertificateType, "CN=Requested", true,
                It.IsAny<ByteString>(), CancellationToken.None), Times.Once);
            scenario.Client.Verify(client => client.ApplyChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        });
    }

    private sealed class PushScenario : IAsyncDisposable
    {
        public PushScenario()
        {
            Context.Endpoint.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            Context.Endpoint.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            Context.Endpoint.Server.ApplicationName = new LocalizedText("Push fixture");
            Context.Endpoint.Server.ApplicationUri = "urn:fixture:push";
            Context.Session.SetupGet(session => session.Identity)
                .Returns(new UserIdentity(new AnonymousIdentityToken()));
            Peer = TemporaryCertificateStores.CreateCertificate("CN=Trusted peer");
            Issuer = TemporaryCertificateStores.CreateCertificate("CN=Issuer peer");
            Rejected = new CertificateCollection
            {
                TemporaryCertificateStores.CreateCertificate("CN=Rejected peer")
            };
            Client.SetupProperty(client => client.AdminCredentials);
            Client.SetupGet(client => client.Session).Returns(() => m_connected ? Context.Session.Object : null);
            Client.Setup(client => client.ConnectAsync(It.IsAny<ConfiguredEndpoint>(), It.IsAny<CancellationToken>()))
                .Returns((ConfiguredEndpoint endpoint, CancellationToken _) =>
                {
                    Assert.That(endpoint.Description.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    Connects++;
                    m_connected = true;
                    return ValueTask.CompletedTask;
                });
            Client.Setup(client => client.DisconnectAsync(It.IsAny<CancellationToken>())).Returns(() =>
            {
                m_connected = false;
                return ValueTask.CompletedTask;
            });
            Client.Setup(client => client.DisposeAsync()).Returns(() =>
            {
                Disposals++;
                m_connected = false;
                return ValueTask.CompletedTask;
            });
            Client.Setup(client => client.ReadTrustListAsync(
                It.IsAny<TrustListMasks>(), 0, CancellationToken.None))
                .Returns((TrustListMasks mask, long _, CancellationToken _) =>
                {
                    Masks = mask;
                    TrustReads++;
                    return ValueTask.FromResult(new TrustListDataType
                    {
                        TrustedCertificates = [new ByteString(Peer.RawData), ByteString.Empty, s_invalidDer],
                        IssuerCertificates = [new ByteString(Issuer.RawData), ByteString.Empty]
                    });
                });
            Client.Setup(client => client.GetRejectedListAsync(CancellationToken.None)).Returns(() =>
            {
                RejectedReads++;
                return ValueTask.FromResult(Rejected);
            });
            Context.Read = (_, _) => ValueTask.FromResult(new ReadResponse
            {
                Results = [new DataValue(Variant.FromStructure(new ServerStatusDataType
                {
                    CurrentTime = s_time, StartTime = s_time.AddHours(-1),
                    State = Shutdown ? ServerState.Shutdown : ServerState.Running,
                    SecondsTillShutdown = Shutdown ? 20u : 0u,
                    ShutdownReason = Shutdown ? new LocalizedText("maintenance") : LocalizedText.Null,
                    BuildInfo = new BuildInfo
                    {
                        ProductName = "Fixture product", ProductUri = "urn:fixture:product",
                        ManufacturerName = "Fixture manufacturer", SoftwareVersion = "2.0",
                        BuildNumber = "42", BuildDate = s_time
                    }
                }))]
            });
        }

        public ConnectedProtocolContext Context { get; } = new();
        public Mock<IServerPushConfigurationClient> Client { get; } = new();
        public Certificate Peer { get; }
        public Certificate Issuer { get; }
        public CertificateCollection Rejected { get; }
        public int Connects { get; private set; }
        public int Disposals { get; private set; }
        public int TrustReads { get; private set; }
        public int RejectedReads { get; private set; }
        public TrustListMasks Masks { get; private set; }
        public bool Shutdown { get; set; }

        public GdsPushPlugin CreatePlugin() => new(Context.Host, _ => Client.Object);

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync().ConfigureAwait(true);
            Peer.Dispose();
            Issuer.Dispose();
            Rejected.Dispose();
        }

        private bool m_connected;
    }

    private static readonly DateTime s_time = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private static readonly ByteString s_invalidDer = new(new byte[] { 1, 2, 3 });
}
