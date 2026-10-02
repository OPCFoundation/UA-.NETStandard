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
using Opc.Ua.Security.Certificates;
using UaLens.Plugins.CertificateManager;
using UaLens.Tests.Desktop;
using UaLens.Views;

namespace UaLens.Tests.Administration;

[TestFixture]
[NonParallelizable]
public sealed class CertificateStoreWorkflowTests
{
    [TestCase(-1, "EXPIRED", "2001-01-01", "2002-01-01")]
    [TestCase(0, "OK", "2020-01-01", "2080-01-01")]
    [TestCase(1, "NOT YET VALID", "2090-01-01", "2091-01-01")]
    public void CertificateRowsProjectCommonNameUtcValidityAndDoNotConsumeTheInput(
        int validity, string status, string before, string after)
    {
        using Certificate certificate =
            TemporaryCertificateStores.CreateCertificate("CN=Row candidate,O=Fixture", validity);
        using var x509 = certificate.AsX509Certificate2();

        CertItemRow workbench = CertItemRow.From(x509);
        CertRow modal = CertRow.From(x509);

        Assert.That(workbench.Subject, Is.EqualTo("Row candidate"));
        Assert.That(workbench.Issuer, Is.EqualTo("Row candidate"));
        Assert.That(workbench.NotBefore, Is.EqualTo(before));
        Assert.That(workbench.NotAfter, Is.EqualTo(after));
        Assert.That(workbench.Status, Is.EqualTo(status));
        Assert.That(workbench.Thumbprint, Is.EqualTo(certificate.Thumbprint));
        Assert.That(workbench.Certificate, Is.SameAs(x509));
        Assert.That(modal.Subject, Is.EqualTo("Row candidate"));
        Assert.That(modal.Issuer, Is.EqualTo("Row candidate"));
        Assert.That(modal.NotBefore, Is.EqualTo(before));
        Assert.That(modal.NotAfter, Is.EqualTo(after));
        Assert.That(modal.Status, Is.EqualTo(status));
        Assert.That(modal.Thumbprint, Is.EqualTo(certificate.Thumbprint));
        Assert.That(x509.RawData, Is.EqualTo(certificate.RawData));
    }

    [Test]
    public void StoreRoleDescriptionAndCertificateWithoutCommonNameRetainTheirExactDisplayMeaning()
    {
        using var temporary = new TemporaryCertificateStores();
        string[] glyphs = ["app", "trust", "ca", "rej", "dir"];
        for (int i = 0; i < glyphs.Length; i++)
        {
            var node = new CertStoreNode((CertStoreRole)i, "Commissioning", temporary.Identifier("role-" + i));
            Assert.That(node.Glyph, Is.EqualTo(glyphs[i]));
            Assert.That(node.Description, Is.EqualTo("[Directory]" + Path.Combine(temporary.Root, "role-" + i)));
            Assert.That(node.DisplayName, Is.EqualTo("Commissioning"));
        }
        var unspecified = new CertStoreNode((CertStoreRole)999, "Unspecified",
            new CertificateStoreIdentifier("relative-store", string.Empty));
        Assert.That(unspecified.Description, Is.EqualTo("relative-store"));
        Assert.That(unspecified.Glyph, Is.EqualTo("dir"));
        using Certificate certificate = TemporaryCertificateStores.CreateCertificate("O=No common name");
        using var x509 = certificate.AsX509Certificate2();
        Assert.That(CertItemRow.From(x509).Subject, Is.EqualTo("O=No common name"));
        Assert.That(CertRow.From(x509).Issuer, Is.EqualTo("O=No common name"));
    }

    [Test]
    [Platform("Win,Linux")]
    public Task DialogTrustMovesOnlyTheRejectedSelectionAndDeletePreservesTheOtherBuckets()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var stores = new TemporaryCertificateStores();
            using Certificate peer = TemporaryCertificateStores.CreateCertificate("CN=Existing peer");
            using Certificate issuer = TemporaryCertificateStores.CreateCertificate("CN=Existing issuer");
            using Certificate rejected = TemporaryCertificateStores.CreateCertificate("CN=Accepted candidate");
            await stores.AddAsync(stores.Peer, peer).ConfigureAwait(true);
            await stores.AddAsync(stores.Issuer, issuer).ConfigureAwait(true);
            await stores.AddAsync(stores.Rejected, rejected).ConfigureAwait(true);
            var dialog = new CertificateStoreDialog(stores.Configuration, stores.Telemetry);
            Task prompt = dialog.ShowDialog(DesktopInteraction.Owner).WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                await ReadyAsync(dialog, 1, 1, 1).ConfigureAwait(true);
                Assert.That(dialog.Trusted.Single().Thumbprint, Is.EqualTo(peer.Thumbprint));
                Assert.That(dialog.Issuer.Single().Thumbprint, Is.EqualTo(issuer.Thumbprint));
                Assert.That(dialog.Rejected.Single().Thumbprint, Is.EqualTo(rejected.Thumbprint));
                Click(dialog, "RejectedTrustBtn");
                Assert.That(dialog.Trusted.Single().Thumbprint, Is.EqualTo(peer.Thumbprint),
                    "No selection must not move the first rejected row implicitly.");
                DesktopInteraction.Control<ListBox>(dialog, "RejectedList").SelectedItem = dialog.Rejected.Single();

                await ActionAsync(dialog, "Trusted: 2 certificate(s).",
                    () => dialog.Trusted.Count == 2 && dialog.Rejected.Count == 0,
                    () => Click(dialog, "RejectedTrustBtn")).ConfigureAwait(true);

                Assert.That(await stores.ReadAsync(stores.Peer).ConfigureAwait(true),
                    Is.EquivalentTo(new[] { peer.RawData, rejected.RawData }));
                Assert.That(await stores.ReadAsync(stores.Rejected).ConfigureAwait(true), Is.Empty);
                Assert.That(dialog.Trusted.Select(r => r.Subject),
                    Is.EquivalentTo(s_dialogTrustMovesOnlyTheRejectedSelectionAndDeletePreservesTheExpected));
                DesktopInteraction.Control<ListBox>(dialog, "TrustedList").SelectedItem =
                    dialog.Trusted.Single(r => r.Thumbprint == rejected.Thumbprint);
                await ActionAsync(dialog, "Trusted: 1 certificate(s).", () => dialog.Trusted.Count == 1,
                    () => Click(dialog, "TrustedUntrustBtn")).ConfigureAwait(true);
                Assert.That(dialog.Trusted.Single().Thumbprint, Is.EqualTo(peer.Thumbprint));
                Assert.That(await stores.ReadAsync(stores.Peer).ConfigureAwait(true),
                    Is.EqualTo(new[] { peer.RawData }));
                Assert.That(await stores.ReadAsync(stores.Issuer).ConfigureAwait(true),
                    Is.EqualTo(new[] { issuer.RawData }));
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    public Task DeleteExpiredRemovesOnlyPastCertificatesAndClearRejectedDoesNotTouchTrust(bool issuerBucket)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var stores = new TemporaryCertificateStores();
            using Certificate valid = TemporaryCertificateStores.CreateCertificate("CN=Valid retained");
            using Certificate expired = TemporaryCertificateStores.CreateCertificate("CN=Expired removed", -1);
            using Certificate future = TemporaryCertificateStores.CreateCertificate("CN=Future retained", 1);
            using Certificate rejected = TemporaryCertificateStores.CreateCertificate("CN=Rejected one");
            using Certificate otherRejected = TemporaryCertificateStores.CreateCertificate("CN=Rejected two");
            CertStoreNode target = issuerBucket ? stores.Issuer : stores.Peer;
            CertStoreNode other = issuerBucket ? stores.Peer : stores.Issuer;
            await stores.AddAsync(target, valid).ConfigureAwait(true);
            await stores.AddAsync(target, expired).ConfigureAwait(true);
            await stores.AddAsync(target, future).ConfigureAwait(true);
            await stores.AddAsync(other, valid).ConfigureAwait(true);
            await stores.AddAsync(stores.Rejected, rejected).ConfigureAwait(true);
            await stores.AddAsync(stores.Rejected, otherRejected).ConfigureAwait(true);
            var dialog = new CertificateStoreDialog(stores.Configuration, stores.Telemetry);
            Task prompt = dialog.ShowDialog(DesktopInteraction.Owner).WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                await ReadyAsync(dialog, issuerBucket ? 1 : 3, issuerBucket ? 3 : 1, 2).ConfigureAwait(true);
                var rows = issuerBucket ? dialog.Issuer : dialog.Trusted;
                Assert.That(rows.Single(r => r.Thumbprint == expired.Thumbprint).Status, Is.EqualTo("EXPIRED"));
                await ActionAsync(dialog, (issuerBucket ? "Issuer" : "Trusted") + ": 2 certificate(s).",
                    () => rows.Count == 2,
                    () => Click(dialog, issuerBucket ? "IssuerExpireBtn" : "TrustedExpireBtn")).ConfigureAwait(true);
                Assert.That(rows.Select(r => r.Thumbprint),
                    Is.EquivalentTo(new[] { valid.Thumbprint, future.Thumbprint }));
                Assert.That(rows.Single(r => r.Thumbprint == future.Thumbprint).Status, Is.EqualTo("NOT YET VALID"));
                Assert.That(await stores.ReadAsync(target).ConfigureAwait(true),
                    Is.EquivalentTo(new[] { valid.RawData, future.RawData }));

                await ActionAsync(dialog, "Rejected: 0 certificate(s).", () => dialog.Rejected.Count == 0,
                    () => Click(dialog, "RejectedClearBtn")).ConfigureAwait(true);

                Assert.That(await stores.ReadAsync(stores.Rejected).ConfigureAwait(true), Is.Empty);
                Assert.That(await stores.ReadAsync(other).ConfigureAwait(true), Is.EqualTo(new[] { valid.RawData }));
                Assert.That(await stores.ReadAsync(target).ConfigureAwait(true),
                    Is.EquivalentTo(new[] { valid.RawData, future.RawData }));
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    [Platform("Win,Linux")]
    public Task DialogRefreshFailureKeepsTheLastCompletedSnapshotAndReleasesTheFaultedStore()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var stores = new TemporaryCertificateStores();
            using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Snapshot candidate");
            await stores.AddAsync(stores.Peer, certificate).ConfigureAwait(true);
            await stores.AddAsync(stores.Issuer, certificate).ConfigureAwait(true);
            await stores.AddAsync(stores.Rejected, certificate).ConfigureAwait(true);
            var order = new List<string>();
            var failure = new IOException("controlled store failure");
            var faulted = new Mock<ICertificateStore>(MockBehavior.Strict);
            faulted.Setup(s => s.EnumerateAsync(CancellationToken.None)).Returns(() =>
            {
                order.Add("enumerate");
                return Task.FromException<CertificateCollection>(failure);
            });
            faulted.Setup(s => s.Close()).Callback(() => order.Add("close"));
            faulted.Setup(s => s.Dispose()).Callback(() => order.Add("dispose"));
            var identifier = new Mock<CertificateTrustList>(MockBehavior.Strict);
            identifier.Object.StorePath = stores.Peer.Identifier.StorePath;
            identifier.Object.StoreType = CertificateStoreType.Directory;
            int opens = 0;
            identifier.Setup(id => id.OpenStore(stores.Telemetry)).Returns(() =>
            {
                opens++;
                return opens == 2 ? faulted.Object : stores.Peer.Identifier.OpenStore(stores.Telemetry);
            });
            stores.Configuration.SecurityConfiguration.TrustedPeerCertificates = identifier.Object;
            var dialog = new CertificateStoreDialog(stores.Configuration, stores.Telemetry);
            Task prompt = dialog.ShowDialog(DesktopInteraction.Owner).WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                await ReadyAsync(dialog, 1, 1, 1).ConfigureAwait(true);
                CertRow old = dialog.Trusted.Single();
                await ActionAsync(dialog, "Load Trusted failed: controlled store failure",
                    () => dialog.Trusted.Count == 1, () => Click(dialog, "TrustedRefreshBtn")).ConfigureAwait(true);
                Assert.That(dialog.Trusted.Single(), Is.SameAs(old));
                Assert.That(old.Thumbprint, Is.EqualTo(certificate.Thumbprint));
                Assert.That(order, Is.EqualTo(s_dialogRefreshFailureKeepsTheLastCompletedSnapshotAndReleasesTExpected));
                await ActionAsync(dialog, "Trusted: 1 certificate(s).",
                    () => !ReferenceEquals(dialog.Trusted.Single(), old),
                    () => Click(dialog, "TrustedRefreshBtn")).ConfigureAwait(true);
                Assert.That(dialog.Trusted.Single().Thumbprint, Is.EqualTo(certificate.Thumbprint));
                Assert.That(dialog.Rejected.Single().Thumbprint, Is.EqualTo(certificate.Thumbprint));
                Assert.That(opens, Is.EqualTo(3));
                faulted.VerifyAll();
                faulted.VerifyNoOtherCalls();
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    [Platform("Win,Linux")]
    public Task LegacyTrustDialogReturnsToTheWorkbenchAndRefreshesItsSelectedTemporaryStore()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using var stores = new TemporaryCertificateStores();
            using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Modal removal");
            await stores.AddAsync(stores.Peer, certificate).ConfigureAwait(true);
            await stores.AddAsync(stores.Issuer, certificate).ConfigureAwait(true);
            await stores.AddAsync(stores.Rejected, certificate).ConfigureAwait(true);
            await using var context = new AdministrationWorkflowContext(stores.Configuration);
            await using var plugin = new CertificateManagerPlugin(context.Host);
            using var ownership = new CertificateRowsScope(plugin);
            await DesktopInteraction.ModelChangedAsync(plugin,
                () => plugin.Status == "● Application: 0 certificate(s).",
                () => plugin.OnConnectionStateChangedAsync(CancellationToken.None)).ConfigureAwait(true);
            CertStoreNode peer = plugin.Stores.Single(s => s.Role == CertStoreRole.TrustedPeer);
            await CertificateManagerWorkflowTests.SelectAsync(plugin, peer, 1).ConfigureAwait(true);
            plugin.SelectedCertificate = plugin.Certificates.Single();
            Task operation = Task.CompletedTask;
            CertificateStoreDialog dialog = await DesktopInteraction.OpenedAsync<CertificateStoreDialog>(
                () => operation = plugin.OpenTrustDialogAsync()).ConfigureAwait(true);
            try
            {
                await ReadyAsync(dialog, 1, 1, 1).ConfigureAwait(true);
                DesktopInteraction.Control<ListBox>(dialog, "TrustedList").SelectedItem = dialog.Trusted.Single();
                await ActionAsync(dialog, "Trusted: 0 certificate(s).", () => dialog.Trusted.Count == 0,
                    () => Click(dialog, "TrustedUntrustBtn")).ConfigureAwait(true);
                dialog.Close();
                await operation.ConfigureAwait(true);
                Assert.That(plugin.SelectedStore, Is.SameAs(peer));
                Assert.That(plugin.Certificates, Is.Empty);
                Assert.That(plugin.SelectedCertificate, Is.Null);
                Assert.That(plugin.Status, Is.EqualTo("● Trusted Peers: 0 certificate(s)."));
                Assert.That(await stores.ReadAsync(stores.Peer).ConfigureAwait(true), Is.Empty);
                Assert.That(context.Host.Session, Is.Null);
            }
            finally
            {
                dialog.Close();
                await operation.ConfigureAwait(true);
            }
        });
    }

    private static Task ReadyAsync(CertificateStoreDialog dialog, int trusted, int issuer, int rejected)
    {
        TextBlock label = DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel");
        return DesktopInteraction.ChangedAsync(label,
            () => dialog.Trusted.Count == trusted && dialog.Issuer.Count == issuer && dialog.Rejected.Count == rejected,
            () => Task.CompletedTask);
    }

    private static Task ActionAsync(CertificateStoreDialog dialog, string status, Func<bool> complete, Action action)
    {
        TextBlock label = DesktopInteraction.Control<TextBlock>(dialog, "StatusLabel");
        return DesktopInteraction.ChangedAsync(label, () => label.Text == status && complete(), () =>
        {
            action();
            return Task.CompletedTask;
        });
    }

    private static void Click(CertificateStoreDialog dialog, string name)
    {
        DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, name));
    }

    private static readonly string[] s_dialogTrustMovesOnlyTheRejectedSelectionAndDeletePreservesTheExpected =
    [
        "Existing peer",
        "Accepted candidate",
    ];
    private static readonly string[] s_dialogRefreshFailureKeepsTheLastCompletedSnapshotAndReleasesTExpected =
    [
        "enumerate",
        "close",
        "dispose",
    ];
}
