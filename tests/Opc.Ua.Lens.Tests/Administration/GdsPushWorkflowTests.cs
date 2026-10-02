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
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Security.Certificates;
using UaLens.Plugins.GdsPush;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

[TestFixture]
[NonParallelizable]
public sealed class GdsPushWorkflowTests
{
    [Test]
    public async Task EndpointNotificationsFollowWorkspaceChangesOnlyUntilAsyncDisposal()
    {
        await using var context = new AdministrationWorkflowContext();
        context.Workspace.Object.EndpointUrl = "opc.tcp://initial.test:4840";
        var plugin = new GdsPushPlugin(context.Host);
        var endpoints = new List<string>();
        plugin.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(plugin.EndpointUrl))
            {
                endpoints.Add(plugin.EndpointUrl);
            }
        };
        try
        {
            context.Workspace.Raise(w => w.PropertyChanged += null,
                new PropertyChangedEventArgs("CurrentRegisteredApp"));
            Assert.That(endpoints, Is.Empty);
            context.Workspace.Object.EndpointUrl = "opc.tcp://new.test:4841";
            context.Workspace.Raise(w => w.PropertyChanged += null, new PropertyChangedEventArgs("EndpointUrl"));
            Assert.That(endpoints, Is.EqualTo(s_endpointNotificationsFollowWorkspaceChangesOnlyUntilAsyncDispExpected));
            Assert.That(plugin.EndpointUrl, Is.EqualTo("opc.tcp://new.test:4841"));

            await plugin.DisposeAsync().ConfigureAwait(false);

            context.Workspace.Object.EndpointUrl = "opc.tcp://after-dispose.test:4842";
            context.Workspace.Raise(w => w.PropertyChanged += null, new PropertyChangedEventArgs("EndpointUrl"));
            Assert.That(endpoints, Is.EqualTo(s_endpointNotificationsFollowWorkspaceChangesOnlyUntilAsyncDispExpected));
            Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
            Assert.That(context.Host.Session, Is.Null);
            Assert.That(context.Log.Entries.Any(e => e.Exception is not null), Is.False);
        }
        finally
        {
            await plugin.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    [Platform("Win,Linux")]
    public Task DisconnectClearsCachedTrustAndServerStatusButPreservesBucketAndMaskIntent()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            context.Workspace.Object.EndpointUrl = "opc.tcp://primary-intent.test:4840";
            await using var plugin = new GdsPushPlugin(context.Host);
            using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Cached certificate");
            using var x509 = certificate.AsX509Certificate2();
            var row = new GdsCertItem
            {
                Certificate = x509, Thumbprint = certificate.Thumbprint, Subject = "Cached certificate"
            };
            plugin.Trusted.Add(row);
            plugin.Issuers.Add(row);
            plugin.Rejected.Add(row);
            plugin.ActiveBucket = TrustListBucket.Issuer;
            plugin.TrustListMasks = TrustListMasks.IssuerCertificates;
            plugin.ServerApplicationName = "Previous server";
            plugin.ServerApplicationUri = "urn:previous";
            plugin.ServerCertSubject = "Previous certificate";
            plugin.ServerCertIssuer = "Previous issuer";
            plugin.ServerCertExpiry = "2030-01-01";
            plugin.StatusProductName = "Previous product";
            plugin.StatusProductUri = "urn:previous:product";
            plugin.StatusManufacturerName = "Previous manufacturer";
            plugin.StatusSoftwareVersion = "1.2";
            plugin.StatusBuildNumber = "123";
            plugin.StatusBuildDate = "2025-01-01";
            plugin.StatusStartTime = "2025-01-02";
            plugin.StatusCurrentTime = "2025-01-03";
            plugin.StatusState = "Running";
            plugin.StatusSecondsTillShutdown = "42";
            plugin.StatusShutdownReason = "Maintenance";

            await DesktopInteraction.ModelChangedAsync(plugin,
                () => plugin.StatusState == "—" && plugin.StatusShutdownReason.Length == 0,
                () => plugin.DisconnectCommand.ExecuteAsync(null)).ConfigureAwait(true);

            Assert.That(plugin.Trusted, Is.Empty);
            Assert.That(plugin.Issuers, Is.Empty);
            Assert.That(plugin.Rejected, Is.Empty);
            Assert.That(new[]
            {
                plugin.ServerApplicationName, plugin.ServerApplicationUri, plugin.ServerCertSubject,
                plugin.ServerCertIssuer, plugin.ServerCertExpiry, plugin.StatusProductName, plugin.StatusProductUri,
                plugin.StatusManufacturerName, plugin.StatusSoftwareVersion, plugin.StatusBuildNumber,
                plugin.StatusBuildDate, plugin.StatusStartTime, plugin.StatusCurrentTime, plugin.StatusState
            }, Is.All.EqualTo("—"));
            Assert.That(plugin.StatusSecondsTillShutdown, Is.Empty);
            Assert.That(plugin.StatusShutdownReason, Is.Empty);
            Assert.That(plugin.ActiveBucket, Is.EqualTo(TrustListBucket.Issuer));
            Assert.That(plugin.TrustListMasks, Is.EqualTo(TrustListMasks.IssuerCertificates));
            Assert.That(plugin.EndpointUrl, Is.EqualTo("opc.tcp://primary-intent.test:4840"));
            Assert.That(plugin.Status, Is.EqualTo("● Disconnected · Disconnected."));
            Assert.That(plugin.IsBusy, Is.False);
            Assert.That(x509.RawData, Is.EqualTo(certificate.RawData));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    [Platform("Win,Linux")]
    public Task ApplyChangesRequiresConsentAndCancelOrCloseNeverStartsASecondaryConnection(bool close)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            await using var plugin = new GdsPushPlugin(context.Host);
            Task operation = Task.CompletedTask;
            Window dialog = await DesktopInteraction.OpenedAsync<Window>(
                () => operation = plugin.ApplyChangesCommand.ExecuteAsync(null)).ConfigureAwait(true);
            try
            {
                Assert.That(dialog.Title, Is.EqualTo("Apply certificate changes"));
                Assert.That(((DockPanel)dialog.Content!).Children.OfType<TextBlock>().Single().Text,
                    Does.Contain("trust-list changes").And.Contain("closes this session").And.Contain("Apply"));
                Button cancel = dialog.GetLogicalDescendants().OfType<Button>()
                    .Single(b => Equals(b.Content, "Cancel"));
                Assert.That(cancel.IsDefault, Is.True);
                Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
                Assert.That(plugin.IsBusy, Is.False);
                if (close)
                {
                    dialog.Close();
                }
                else
                {
                    DesktopInteraction.Click(cancel);
                }
                await operation.ConfigureAwait(true);
                Assert.That(plugin.LastOperationResult, Is.EqualTo("ApplyChanges cancelled."));
                Assert.That(plugin.Status, Is.EqualTo("● Disconnected · ApplyChanges cancelled."));
                Assert.That(plugin.IsBusy, Is.False);
                Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
                Assert.That(context.ConnectionContext.Discoveries, Is.Empty);
            }
            finally
            {
                dialog.Close();
                await operation.ConfigureAwait(true);
            }
        });
    }

    [Test]
    [Platform("Win,Linux")]
    public Task AddCertificateCancellationKeepsTheActiveBucketAndDoesNotResolveAClient()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new AdministrationWorkflowContext();
            await using var plugin = new GdsPushPlugin(context.Host) { ActiveBucket = TrustListBucket.Issuer };
            Task operation = Task.CompletedTask;
            AddCertificateDialog dialog = await DesktopInteraction.OpenedAsync<AddCertificateDialog>(
                () => operation = plugin.AddCertificateCommand.ExecuteAsync(null)).ConfigureAwait(true);
            try
            {
                Assert.That(DesktopInteraction.Control<RadioButton>(dialog, "DestIssuer").IsChecked, Is.True);
                DesktopInteraction.Control<RadioButton>(dialog, "DestTrusted").IsChecked = true;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                await operation.ConfigureAwait(true);
                Assert.That(plugin.ActiveBucket, Is.EqualTo(TrustListBucket.Issuer));
                Assert.That(plugin.Trusted, Is.Empty);
                Assert.That(plugin.Issuers, Is.Empty);
                Assert.That(plugin.LastOperationResult, Is.Empty);
                Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
                Assert.That(plugin.IsBusy, Is.False);
            }
            finally
            {
                dialog.Close();
                await operation.ConfigureAwait(true);
            }
        });
    }

    private static readonly string[] s_endpointNotificationsFollowWorkspaceChangesOnlyUntilAsyncDispExpected =
    [
        "opc.tcp://new.test:4841",
    ];
}
