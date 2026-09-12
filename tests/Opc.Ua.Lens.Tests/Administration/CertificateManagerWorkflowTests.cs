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
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Security.Certificates;
using UaLens.Plugins.CertificateManager;
using UaLens.Tests.Desktop;
using ReferenceEqualityComparer = System.Collections.Generic.ReferenceEqualityComparer;

namespace UaLens.Tests.Administration;

[TestFixture]
[NonParallelizable]
public sealed class CertificateManagerWorkflowTests
{
    [Test]
    public async Task ConfigurationLoadsStoresOnceAndPreservesCustomStoreSelectionAcrossConnectionNotifications()
    {
        using var stores = new TemporaryCertificateStores();
        using Certificate application = TemporaryCertificateStores.CreateCertificate("CN=Application fixture");
        await stores.AddAsync(stores.Application, application).ConfigureAwait(false);
        await using var context = new AdministrationWorkflowContext(stores.Configuration);
        await using var plugin = new CertificateManagerPlugin(context.Host);
        using var ownership = new CertificateRowsScope(plugin);

        await DesktopInteraction.ModelChangedAsync(plugin,
            () => plugin.Status == "● Application: 1 certificate(s).",
            () => plugin.OnConnectionStateChangedAsync(CancellationToken.None)).ConfigureAwait(false);

        Assert.That(plugin.Stores.Select(s => s.Role), Is.EqualTo(new[]
        {
            CertStoreRole.Application, CertStoreRole.TrustedPeer, CertStoreRole.TrustedIssuer, CertStoreRole.Rejected
        }));
        Assert.That(plugin.Stores.Select(s => s.Identifier.StorePath), Is.EqualTo(new[]
        {
            stores.Application.Identifier.StorePath, stores.Peer.Identifier.StorePath,
            stores.Issuer.Identifier.StorePath, stores.Rejected.Identifier.StorePath
        }));
        Assert.That(plugin.Certificates.Single().Certificate.RawData, Is.EqualTo(application.RawData));
        var custom = new CertStoreNode(CertStoreRole.Custom, "Commissioning", stores.Identifier("custom"));
        plugin.Stores.Add(custom);
        await SelectAsync(plugin, custom, 0).ConfigureAwait(false);
        await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.That(plugin.SelectedStore, Is.SameAs(custom));
        Assert.That(plugin.Stores.Last(), Is.SameAs(custom));
        Assert.That(plugin.Certificates, Is.Empty);
        Assert.That(plugin.Status, Is.EqualTo("● Commissioning: 0 certificate(s)."));
        context.ConnectionContext.Backend.Verify(
            b => b.CreateConfigurationAsync(It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(context.Host.Session, Is.Null);
    }

    [Test]
    public async Task StoreSelectionReplacesCertificateRowsAndResetsAllPerCertificateCommands()
    {
        using var stores = new TemporaryCertificateStores();
        using Certificate first = TemporaryCertificateStores.CreateCertificate("CN=Peer one");
        using Certificate second = TemporaryCertificateStores.CreateCertificate("CN=Peer two");
        using Certificate issuer = TemporaryCertificateStores.CreateCertificate("CN=Issuer one");
        await stores.AddAsync(stores.Peer, first).ConfigureAwait(false);
        await stores.AddAsync(stores.Peer, second).ConfigureAwait(false);
        await stores.AddAsync(stores.Issuer, issuer).ConfigureAwait(false);
        await using var context = new AdministrationWorkflowContext();
        await using var plugin = new CertificateManagerPlugin(context.Host);
        using var ownership = new CertificateRowsScope(plugin);
        plugin.Stores.Add(stores.Peer);
        plugin.Stores.Add(stores.Issuer);
        await SelectAsync(plugin, stores.Peer, 2).ConfigureAwait(false);
        Assert.That(plugin.Certificates.Select(r => r.Thumbprint),
            Is.EquivalentTo(new[] { first.Thumbprint, second.Thumbprint }));
        plugin.SelectedCertificate = plugin.Certificates.Single(r => r.Thumbprint == first.Thumbprint);
        Assert.That(plugin.ViewDetailsCommand.CanExecute(null), Is.True);
        Assert.That(plugin.DeleteCommand.CanExecute(null), Is.True);
        Assert.That(plugin.TrustToPeerCommand.CanExecute(null), Is.True);

        await SelectAsync(plugin, stores.Issuer, 1).ConfigureAwait(false);

        Assert.That(plugin.Certificates.Single().Certificate.RawData, Is.EqualTo(issuer.RawData));
        Assert.That(plugin.SelectedCertificate, Is.Null);
        Assert.That(plugin.HasSelectedCertificate, Is.False);
        Assert.That(plugin.ViewDetailsCommand.CanExecute(null), Is.False);
        Assert.That(plugin.DeleteCommand.CanExecute(null), Is.False);
        Assert.That(plugin.TrustToPeerCommand.CanExecute(null), Is.False);
        await DesktopInteraction.ModelChangedAsync(plugin, () => plugin.Status == "● No store selected.", () =>
        {
            plugin.SelectedStore = null;
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        Assert.That(plugin.Certificates, Is.Empty);
        Assert.That(await stores.ReadAsync(stores.Peer).ConfigureAwait(false),
            Is.EquivalentTo(new[] { first.RawData, second.RawData }));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task TrustPeerTrustIssuerAndRejectMoveOnlyTheSelectedGeneratedCertificate(int destination)
    {
        using var stores = new TemporaryCertificateStores();
        using Certificate moved = TemporaryCertificateStores.CreateCertificate("CN=Move candidate");
        using Certificate retained = TemporaryCertificateStores.CreateCertificate("CN=Keep in source");
        using Certificate neighbor = TemporaryCertificateStores.CreateCertificate("CN=Keep in destination");
        var source = new CertStoreNode(CertStoreRole.Custom, "Source", stores.Identifier("source"));
        CertStoreNode target = destination switch { 0 => stores.Peer, 1 => stores.Issuer, _ => stores.Rejected };
        await stores.AddAsync(source, moved).ConfigureAwait(false);
        await stores.AddAsync(source, retained).ConfigureAwait(false);
        await stores.AddAsync(target, neighbor).ConfigureAwait(false);
        await using var context = new AdministrationWorkflowContext();
        await using var plugin = new CertificateManagerPlugin(context.Host);
        using var ownership = new CertificateRowsScope(plugin);
        plugin.Stores.Add(source);
        plugin.Stores.Add(target);
        await SelectAsync(plugin, source, 2).ConfigureAwait(false);
        CertItemRow selected = plugin.Certificates.Single(r => r.Thumbprint == moved.Thumbprint);
        plugin.SelectedCertificate = selected;
        var statuses = new List<string>();
        plugin.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(plugin.Status))
            {
                statuses.Add(plugin.Status);
            }
        };

        await MoveAsync(plugin, destination).ConfigureAwait(false);

        Assert.That(await stores.ReadAsync(source).ConfigureAwait(false), Is.EqualTo(new[] { retained.RawData }));
        Assert.That(await stores.ReadAsync(target).ConfigureAwait(false),
            Is.EquivalentTo(new[] { neighbor.RawData, moved.RawData }));
        Assert.That(selected.Certificate.RawData, Is.EqualTo(moved.RawData),
            "Disposing the add wrapper must not invalidate the certificate still owned by the row.");
        Assert.That(plugin.SelectedStore, Is.SameAs(source));
        Assert.That(plugin.SelectedCertificate, Is.Null);
        Assert.That(plugin.Certificates.Single().Thumbprint, Is.EqualTo(retained.Thumbprint));
        Assert.That(statuses, Does.Contain($"● Moved Move candidate → {target.DisplayName}."));
        Assert.That(plugin.Status, Is.EqualTo("● Source: 1 certificate(s)."));
        AdministrationLogCapture.Entry added = context.Log.Entries.Single(e => e.Event.Id == 3002);
        Assert.That(added.State.Single(p => p.Key == "Thumbprint").Value, Is.EqualTo(moved.Thumbprint));
        Assert.That(added.State.Single(p => p.Key == "Store").Value, Is.EqualTo(target.DisplayName));
    }

    [Test]
    public async Task DuplicateDestinationAddPreservesTheSourceAndSelectedCertificate()
    {
        using var stores = new TemporaryCertificateStores();
        using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Already trusted");
        await stores.AddAsync(stores.Rejected, certificate).ConfigureAwait(false);
        await stores.AddAsync(stores.Peer, certificate).ConfigureAwait(false);
        await using var context = new AdministrationWorkflowContext();
        await using var plugin = new CertificateManagerPlugin(context.Host);
        using var ownership = new CertificateRowsScope(plugin);
        plugin.Stores.Add(stores.Rejected);
        plugin.Stores.Add(stores.Peer);
        await SelectAsync(plugin, stores.Rejected, 1).ConfigureAwait(false);
        plugin.SelectedCertificate = plugin.Certificates.Single();

        await plugin.TrustToPeerAsync().ConfigureAwait(false);

        Assert.That(await stores.ReadAsync(stores.Rejected).ConfigureAwait(false),
            Is.EqualTo(new[] { certificate.RawData }));
        Assert.That(await stores.ReadAsync(stores.Peer).ConfigureAwait(false),
            Is.EqualTo(new[] { certificate.RawData }));
        Assert.That(plugin.SelectedCertificate!.Certificate.RawData, Is.EqualTo(certificate.RawData));
        Assert.That(plugin.Status, Is.EqualTo("● Add to Trusted Peers failed."));
        Assert.That(context.Log.Entries.Single(e => e.Event.Id == 3003).Exception, Is.TypeOf<ArgumentException>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingOrSamePathDestinationDoesNotDeleteTheSource(bool samePath)
    {
        using var stores = new TemporaryCertificateStores();
        using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Guarded move");
        await stores.AddAsync(stores.Rejected, certificate).ConfigureAwait(false);
        await using var context = new AdministrationWorkflowContext();
        await using var plugin = new CertificateManagerPlugin(context.Host);
        using var ownership = new CertificateRowsScope(plugin);
        plugin.Stores.Add(stores.Rejected);
        if (samePath)
        {
            var target = new CertStoreNode(CertStoreRole.TrustedPeer, "Same store",
                new CertificateStoreIdentifier(stores.Rejected.Identifier.StorePath!.ToUpperInvariant(),
                    CertificateStoreType.Directory));
            plugin.Stores.Add(target);
        }
        await SelectAsync(plugin, stores.Rejected, 1).ConfigureAwait(false);
        plugin.SelectedCertificate = plugin.Certificates.Single();

        await plugin.TrustToPeerAsync().ConfigureAwait(false);

        Assert.That(plugin.Status,
            Is.EqualTo(samePath ? "● Already in Same store." : "● TrustedPeer store not configured."));
        Assert.That(await stores.ReadAsync(stores.Rejected).ConfigureAwait(false),
            Is.EqualTo(new[] { certificate.RawData }));
        Assert.That(plugin.SelectedCertificate!.Thumbprint, Is.EqualTo(certificate.Thumbprint));
        Assert.That(context.Log.Entries, Is.Empty);
    }

    [Test]
    public async Task MoveFailureLogsTheOriginalExceptionAndTitleAndRetainsDestinationInStatus()
    {
        using var stores = new TemporaryCertificateStores();
        using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Open failure");
        await stores.AddAsync(stores.Rejected, certificate).ConfigureAwait(false);
        await using var context = new AdministrationWorkflowContext();
        await using var plugin = new CertificateManagerPlugin(context.Host) { Title = "Commissioning certificates" };
        using var ownership = new CertificateRowsScope(plugin);
        var failure = new IOException("destination unavailable");
        var targetId = new Mock<CertificateStoreIdentifier>(
            MockBehavior.Strict, Path.Combine(stores.Root, "offline"), CertificateStoreType.Directory, true);
        targetId.Setup(id => id.OpenStore(context.Telemetry)).Throws(failure);
        var target = new CertStoreNode(CertStoreRole.TrustedPeer, "Offline peer store", targetId.Object);
        plugin.Stores.Add(stores.Rejected);
        plugin.Stores.Add(target);
        await SelectAsync(plugin, stores.Rejected, 1).ConfigureAwait(false);
        plugin.SelectedCertificate = plugin.Certificates.Single();

        await plugin.TrustToPeerAsync().ConfigureAwait(false);

        Assert.That(plugin.Status, Is.EqualTo("● Move to Offline peer store failed: destination unavailable"));
        Assert.That(await stores.ReadAsync(stores.Rejected).ConfigureAwait(false),
            Is.EqualTo(new[] { certificate.RawData }));
        Assert.That(plugin.SelectedCertificate!.Certificate.RawData, Is.EqualTo(certificate.RawData));
        AdministrationLogCapture.Entry entry = context.Log.Entries.Single(e => e.Event.Id == 3007);
        Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
        Assert.That(entry.Exception, Is.SameAs(failure));
        Assert.That(entry.State.Single(p => p.Key == "Title").Value, Is.EqualTo("Commissioning certificates"));
        Assert.That(entry.Message, Is.EqualTo("Certificate Manager tab Commissioning certificates Move failed."));
        targetId.Verify(id => id.OpenStore(context.Telemetry), Times.Once);
        targetId.VerifyNoOtherCalls();
    }

    [Test]
    public async Task FailedAddClosesAndDisposesTheDestinationBeforeReturningWithoutSourceDeletion()
    {
        using var stores = new TemporaryCertificateStores();
        using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Failed add");
        byte[] der = certificate.RawData;
        await stores.AddAsync(stores.Rejected, certificate).ConfigureAwait(false);
        await using var context = new AdministrationWorkflowContext();
        await using var plugin = new CertificateManagerPlugin(context.Host);
        using var ownership = new CertificateRowsScope(plugin);
        var order = new List<string>();
        var failure = new IOException("store full");
        Mock<ICertificateStore> destination = ReleasedStore(order);
        destination.Setup(s => s.AddAsync(It.Is<Certificate>(c => c.RawData.SequenceEqual(der)),
            null, CancellationToken.None)).Returns(() =>
            {
                order.Add("add");
                return Task.FromException(failure);
            });
        Mock<CertificateStoreIdentifier> target = StoreIdentifier(stores, "full", context, destination.Object);
        plugin.Stores.Add(stores.Rejected);
        plugin.Stores.Add(new CertStoreNode(CertStoreRole.TrustedPeer, "Full peer store", target.Object));
        await SelectAsync(plugin, stores.Rejected, 1).ConfigureAwait(false);
        plugin.SelectedCertificate = plugin.Certificates.Single();

        await plugin.TrustToPeerAsync().ConfigureAwait(false);

        Assert.That(order, Is.EqualTo(s_failedAddClosesAndDisposesTheDestinationBeforeReturningWithouExpected));
        Assert.That(plugin.Status, Is.EqualTo("● Add to Full peer store failed."));
        Assert.That(await stores.ReadAsync(stores.Rejected).ConfigureAwait(false), Is.EqualTo(new[] { der }));
        Assert.That(context.Log.Entries.Single(e => e.Event.Id == 3003).Exception, Is.SameAs(failure));
        destination.VerifyAll();
        destination.VerifyNoOtherCalls();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedSourceDeleteLeavesTheSuccessfulDestinationCopyAndReleasesTheSource(bool throws)
    {
        using var stores = new TemporaryCertificateStores();
        using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Copy survives");
        await stores.AddAsync(stores.Rejected, certificate).ConfigureAwait(false);
        await using var context = new AdministrationWorkflowContext();
        await using var plugin = new CertificateManagerPlugin(context.Host);
        using var ownership = new CertificateRowsScope(plugin);
        var order = new List<string>();
        var failure = new IOException("source is read only");
        Mock<ICertificateStore> source = ReleasedStore(order);
        source.Setup(s => s.DeleteAsync(certificate.Thumbprint, CancellationToken.None)).Returns(() =>
        {
            order.Add("delete");
            return throws ? Task.FromException<bool>(failure) : Task.FromResult(false);
        });
        var sourceId = new Mock<CertificateStoreIdentifier>(
            MockBehavior.Strict, stores.Rejected.Identifier.StorePath!, CertificateStoreType.Directory, true);
        int opens = 0;
        sourceId.Setup(id => id.OpenStore(context.Telemetry)).Returns(() =>
        {
            opens++;
            return opens == 2 ? source.Object : stores.Rejected.Identifier.OpenStore(context.Telemetry);
        });
        var sourceNode = new CertStoreNode(CertStoreRole.Rejected, "Rejected", sourceId.Object);
        plugin.Stores.Add(sourceNode);
        plugin.Stores.Add(stores.Peer);
        await SelectAsync(plugin, sourceNode, 1).ConfigureAwait(false);
        plugin.SelectedCertificate = plugin.Certificates.Single();
        var statuses = new List<string>();
        plugin.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(plugin.Status))
            {
                statuses.Add(plugin.Status);
            }
        };

        await plugin.TrustToPeerAsync().ConfigureAwait(false);

        Assert.That(order, Is.EqualTo(s_failedSourceDeleteLeavesTheSuccessfulDestinationCopyAndReleasExpected));
        Assert.That(await stores.ReadAsync(stores.Peer).ConfigureAwait(false),
            Is.EqualTo(new[] { certificate.RawData }));
        Assert.That(await stores.ReadAsync(stores.Rejected).ConfigureAwait(false),
            Is.EqualTo(new[] { certificate.RawData }));
        Assert.That(opens, Is.EqualTo(throws ? 2 : 3));
        if (throws)
        {
            Assert.That(plugin.Status, Is.EqualTo("● Move to Trusted Peers failed: source is read only"));
            Assert.That(context.Log.Entries.Single(e => e.Event.Id == 3007).Exception, Is.SameAs(failure));
            Assert.That(plugin.SelectedCertificate!.Thumbprint, Is.EqualTo(certificate.Thumbprint));
        }
        else
        {
            Assert.That(statuses, Does.Contain("● Copied Copy survives → Trusted Peers (source remove failed)."));
            Assert.That(plugin.Status, Is.EqualTo("● Rejected: 1 certificate(s)."));
            Assert.That(plugin.SelectedCertificate, Is.Null);
        }
        source.VerifyAll();
        source.VerifyNoOtherCalls();
    }

    [Test]
    public async Task EnumerationFailureReleasesTheOwnedStoreAndClearsPreviouslySelectedRows()
    {
        using var stores = new TemporaryCertificateStores();
        using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Old row");
        await stores.AddAsync(stores.Peer, certificate).ConfigureAwait(false);
        await using var context = new AdministrationWorkflowContext();
        await using var plugin = new CertificateManagerPlugin(context.Host);
        using var ownership = new CertificateRowsScope(plugin);
        await SelectAsync(plugin, stores.Peer, 1).ConfigureAwait(false);
        plugin.SelectedCertificate = plugin.Certificates.Single();
        var order = new List<string>();
        var failure = new IOException("enumeration denied");
        Mock<ICertificateStore> unreadable = ReleasedStore(order);
        unreadable.Setup(s => s.EnumerateAsync(CancellationToken.None)).Returns(() =>
        {
            order.Add("enumerate");
            return Task.FromException<CertificateCollection>(failure);
        });
        var node = new CertStoreNode(CertStoreRole.Custom, "Unreadable",
            StoreIdentifier(stores, "unreadable", context, unreadable.Object).Object);

        await DesktopInteraction.ModelChangedAsync(plugin,
            () => plugin.Status == "● Enumerate Unreadable failed: enumeration denied", () =>
            {
                plugin.SelectedStore = node;
                return Task.CompletedTask;
            }).ConfigureAwait(false);

        Assert.That(plugin.Certificates, Is.Empty);
        Assert.That(plugin.SelectedCertificate, Is.Null);
        Assert.That(order, Is.EqualTo(s_enumerationFailureReleasesTheOwnedStoreAndClearsPreviouslySelExpected));
        Assert.That(context.Log.Entries.Single(e => e.Event.Id == 3001).Exception, Is.SameAs(failure));
        Assert.That(await stores.ReadAsync(stores.Peer).ConfigureAwait(false),
            Is.EqualTo(new[] { certificate.RawData }));
        unreadable.VerifyAll();
    }

    [Test]
    public async Task ConfigurationFailureCanBeRecoveredByAnExplicitRefreshWithoutOpeningAConnection()
    {
        using var stores = new TemporaryCertificateStores();
        await using var context = new AdministrationWorkflowContext(stores.Configuration);
        await using var plugin = new CertificateManagerPlugin(context.Host);
        var failure = new IOException("configuration temporarily unavailable");
        context.ConnectionContext.Backend.SetupSequence(b => b.CreateConfigurationAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure).ReturnsAsync(stores.Configuration);
        await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(false);
        Assert.That(plugin.Status, Is.EqualTo("● Load stores failed: configuration temporarily unavailable"));
        Assert.That(plugin.Stores, Is.Empty);
        Assert.That(context.Log.Entries.Single(e => e.Event.Id == 3000).Exception, Is.SameAs(failure));

        await DesktopInteraction.ModelChangedAsync(plugin,
            () => plugin.Status == "● Application: 0 certificate(s).", plugin.RefreshAsync).ConfigureAwait(false);

        Assert.That(plugin.Stores.Select(s => s.DisplayName),
            Is.EqualTo(s_configurationFailureCanBeRecoveredByAnExplicitRefreshWithoutOExpected));
        Assert.That(plugin.SelectedStore!.Role, Is.EqualTo(CertStoreRole.Application));
        Assert.That(context.Host.Session, Is.Null);
        context.ConnectionContext.Backend.Verify(b => b.CreateConfigurationAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task DeleteRemovesOnlyTheSelectedCertificateAndRefreshesTheCommandState()
    {
        using var stores = new TemporaryCertificateStores();
        using Certificate selected = TemporaryCertificateStores.CreateCertificate("CN=Delete candidate");
        using Certificate keep = TemporaryCertificateStores.CreateCertificate("CN=Retain candidate");
        await stores.AddAsync(stores.Rejected, selected).ConfigureAwait(false);
        await stores.AddAsync(stores.Rejected, keep).ConfigureAwait(false);
        await using var context = new AdministrationWorkflowContext();
        await using var plugin = new CertificateManagerPlugin(context.Host);
        using var ownership = new CertificateRowsScope(plugin);
        await SelectAsync(plugin, stores.Rejected, 2).ConfigureAwait(false);
        plugin.SelectedCertificate = plugin.Certificates.Single(r => r.Thumbprint == selected.Thumbprint);

        await plugin.DeleteAsync().ConfigureAwait(false);

        Assert.That(await stores.ReadAsync(stores.Rejected).ConfigureAwait(false), Is.EqualTo(new[] { keep.RawData }));
        Assert.That(plugin.Certificates.Single().Thumbprint, Is.EqualTo(keep.Thumbprint));
        Assert.That(plugin.SelectedCertificate, Is.Null);
        Assert.That(plugin.DeleteCommand.CanExecute(null), Is.False);
        Assert.That(plugin.Status, Is.EqualTo("● Rejected: 1 certificate(s)."));
    }

    [TestCase(-1, "EXPIRED")]
    [TestCase(0, "")]
    [TestCase(1, "NOT YET VALID")]
    [Platform("Win,Linux")]
    public Task DetailsShowsTheSelectedCertificateAndClosingDoesNotInvalidateItsBorrowedHandle(
        int validity, string warning)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var stores = new TemporaryCertificateStores();
            using Certificate certificate =
                TemporaryCertificateStores.CreateCertificate("CN=Details candidate", validity);
            await stores.AddAsync(stores.Peer, certificate).ConfigureAwait(true);
            await using var context = new AdministrationWorkflowContext();
            await using var plugin = new CertificateManagerPlugin(context.Host);
            using var ownership = new CertificateRowsScope(plugin);
            await SelectAsync(plugin, stores.Peer, 1).ConfigureAwait(true);
            plugin.SelectedCertificate = plugin.Certificates.Single();
            Task operation = Task.CompletedTask;
            Window dialog = await DesktopInteraction.OpenedAsync<Window>(() => operation = plugin.ViewDetailsAsync())
                .ConfigureAwait(true);
            try
            {
                Assert.That(dialog.Title, Is.EqualTo("Certificate details"));
                Assert.That(dialog.Owner, Is.SameAs(DesktopInteraction.Owner));
                var grid = (Grid)dialog.Content!;
                string Row(int row) => grid.Children.OfType<TextBlock>()
                    .Single(c => Grid.GetRow(c) == row && Grid.GetColumn(c) == 1).Text!;
                Assert.That(Row(0), Is.EqualTo("CN=Details candidate"));
                Assert.That(Row(1), Is.EqualTo("CN=Details candidate"));
                Assert.That(Row(5), Is.EqualTo(certificate.Thumbprint));
                Assert.That(Row(7), Is.EqualTo("no"));
                Assert.That(Row(3).Contains('⚠', StringComparison.Ordinal), Is.EqualTo(validity != 0));
                if (validity != 0)
                {
                    Assert.That(Row(3), Does.Contain(warning));
                }
                TextBox pem = grid.Children.OfType<TextBox>().Single();
                Assert.That(pem.IsReadOnly, Is.True);
                string body = string.Concat(pem.Text!.Split('\n')
                    .Where(line => !line.StartsWith("-----", StringComparison.Ordinal)));
                Assert.That(Convert.FromBase64String(body), Is.EqualTo(certificate.RawData));
                DesktopInteraction.Click(grid.Children.OfType<Button>().Single());
                await operation.ConfigureAwait(true);
                Assert.That(plugin.SelectedCertificate!.Certificate.RawData, Is.EqualTo(certificate.RawData));
            }
            finally
            {
                dialog.Close();
                await operation.ConfigureAwait(true);
            }
        });
    }

    [Test]
    public async Task DestinationWithoutAStoreHandleCannotRemoveTheSelectedSourceCertificate()
    {
        using var stores = new TemporaryCertificateStores();
        using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=No destination handle");
        await stores.AddAsync(stores.Rejected, certificate).ConfigureAwait(false);
        await using var context = new AdministrationWorkflowContext();
        await using var plugin = new CertificateManagerPlugin(context.Host);
        using var ownership = new CertificateRowsScope(plugin);
        plugin.Stores.Add(stores.Rejected);
        plugin.Stores.Add(new CertStoreNode(CertStoreRole.TrustedPeer, "Unconfigured peers",
            new CertificateStoreIdentifier(string.Empty, CertificateStoreType.Directory)));
        await SelectAsync(plugin, stores.Rejected, 1).ConfigureAwait(false);
        plugin.SelectedCertificate = plugin.Certificates.Single();

        await plugin.TrustToPeerAsync().ConfigureAwait(false);

        Assert.That(plugin.Status, Is.EqualTo("● Add to Unconfigured peers failed."));
        Assert.That(plugin.SelectedCertificate!.Thumbprint, Is.EqualTo(certificate.Thumbprint));
        Assert.That(await stores.ReadAsync(stores.Rejected).ConfigureAwait(false),
            Is.EqualTo(new[] { certificate.RawData }));
        Assert.That(context.Log.Entries.Any(e => e.Event.Id == 3002), Is.False);
    }

    internal static Task SelectAsync(CertificateManagerPlugin plugin, CertStoreNode node, int count)
    {
        return DesktopInteraction.ModelChangedAsync(plugin,
            () => plugin.Status == $"● {node.DisplayName}: {count} certificate(s).", () =>
            {
                plugin.SelectedStore = node;
                return Task.CompletedTask;
            });
    }

    private static Task MoveAsync(CertificateManagerPlugin plugin, int destination)
    {
        return destination switch
        {
            0 => plugin.TrustToPeerAsync(),
            1 => plugin.TrustToIssuerAsync(),
            _ => plugin.RejectAsync()
        };
    }

    private static Mock<ICertificateStore> ReleasedStore(List<string> order)
    {
        var store = new Mock<ICertificateStore>(MockBehavior.Strict);
        store.Setup(s => s.Close()).Callback(() => order.Add("close"));
        store.Setup(s => s.Dispose()).Callback(() => order.Add("dispose"));
        return store;
    }

    private static Mock<CertificateStoreIdentifier> StoreIdentifier(
        TemporaryCertificateStores stores, string name, AdministrationWorkflowContext context, ICertificateStore store)
    {
        var identifier = new Mock<CertificateStoreIdentifier>(
            MockBehavior.Strict, Path.Combine(stores.Root, name), CertificateStoreType.Directory, true);
        identifier.Setup(id => id.OpenStore(context.Telemetry)).Returns(store);
        return identifier;
    }

    private static readonly string[] s_failedAddClosesAndDisposesTheDestinationBeforeReturningWithouExpected =
    [
        "add",
        "close",
        "dispose",
    ];
    private static readonly string[] s_failedSourceDeleteLeavesTheSuccessfulDestinationCopyAndReleasExpected =
    [
        "delete",
        "close",
        "dispose",
    ];
    private static readonly string[] s_enumerationFailureReleasesTheOwnedStoreAndClearsPreviouslySelExpected =
    [
        "enumerate",
        "close",
        "dispose",
    ];
    private static readonly string[] s_configurationFailureCanBeRecoveredByAnExplicitRefreshWithoutOExpected =
    [
        "Application",
        "Trusted Peers",
        "Trusted Issuers",
        "Rejected",
    ];
}

internal sealed class CertificateRowsScope : IDisposable
{
    public CertificateRowsScope(CertificateManagerPlugin plugin)
    {
        m_plugin = plugin;
        m_plugin.Certificates.CollectionChanged += Changed;
        foreach (CertItemRow row in m_plugin.Certificates)
        {
            m_certificates.Add(row.Certificate);
        }
    }

    public void Dispose()
    {
        m_plugin.Certificates.CollectionChanged -= Changed;
        foreach (X509Certificate2 certificate in m_certificates)
        {
            certificate.Dispose();
        }
    }

    private void Changed(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null)
        {
            foreach (CertItemRow row in args.NewItems)
            {
                m_certificates.Add(row.Certificate);
            }
        }
    }

    private readonly CertificateManagerPlugin m_plugin;
    private readonly HashSet<X509Certificate2> m_certificates = new(ReferenceEqualityComparer.Instance);
}
