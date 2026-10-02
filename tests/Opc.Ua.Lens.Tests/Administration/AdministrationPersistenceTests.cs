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

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.CertificateManager;
using UaLens.Plugins.FileSystem;
using UaLens.Plugins.GdsDiscovery;
using UaLens.Plugins.GdsManagement;
using UaLens.Plugins.GdsPush;
using UaLens.Storage;
using UaLens.ViewModels;

namespace UaLens.Tests.Administration;

/// <summary>
/// Verifies that every owned administration tool implements versioned, typed,
/// non-secret workspace persistence, restores offline configuration only, and
/// never starts a connection while restoring.
/// </summary>
[TestFixture]
public sealed class AdministrationPersistenceTests
{
    private static readonly PluginKind[] s_ownedKinds =
    [
        PluginKind.CertificateManager,
        PluginKind.FileSystem,
        PluginKind.GdsDiscovery,
        PluginKind.GdsManagement,
        PluginKind.GdsPush,
        PluginKind.RoleManagement,
        PluginKind.UserManagement
    ];

    [Test]
    public async Task EveryOwnedToolCapturesAJsonObjectOffline()
    {
        foreach (PluginKind kind in s_ownedKinds)
        {
            await using var context = new AdministrationTestContext();
            IPlugin plugin = context.Create(kind);
            await using (plugin.ConfigureAwait(false))
            {
                Assert.That(plugin, Is.InstanceOf<IWorkspaceState>(), $"{kind} must persist its configuration.");
                JsonElement captured = ((IWorkspaceState)plugin).CaptureState();
                Assert.That(captured.ValueKind, Is.EqualTo(JsonValueKind.Object),
                    $"{kind} capture must be a JSON object.");
            }
        }
    }

    [Test]
    public async Task EveryOwnedToolRejectsAnUnsupportedStateVersion()
    {
        using JsonDocument document = JsonDocument.Parse("{\"version\":99}");
        JsonElement unsupported = document.RootElement;
        foreach (PluginKind kind in s_ownedKinds)
        {
            await using var context = new AdministrationTestContext();
            IPlugin plugin = context.Create(kind);
            await using (plugin.ConfigureAwait(false))
            {
                var state = (IWorkspaceState)plugin;
                await Assert.ThatAsync(
                    async () => await state.RestoreStateAsync(unsupported, CancellationToken.None).ConfigureAwait(false),
                    Throws.InstanceOf<JsonException>()).ConfigureAwait(false);
            }
        }
    }

    [Test]
    public async Task RestoringOwnedToolStateNeverStartsAConnection()
    {
        foreach (PluginKind kind in s_ownedKinds)
        {
            await using var context = new AdministrationTestContext();
            IPlugin plugin = context.Create(kind);
            await using (plugin.ConfigureAwait(false))
            {
                var state = (IWorkspaceState)plugin;
                JsonElement captured = state.CaptureState();
                await state.RestoreStateAsync(captured, CancellationToken.None).ConfigureAwait(false);
                Assert.That(context.Connection.IsConnected, Is.False, $"{kind} restore must stay offline.");
                Assert.That(context.Host.Session, Is.Null, $"{kind} restore must not open a session.");
            }
        }
    }

    [Test]
    public async Task GdsManagementRestoresTargetEndpointAndFilterOffline()
    {
        await using var context = new AdministrationTestContext();
        var source = (GdsManagementPlugin)context.Create(PluginKind.GdsManagement);
        JsonElement captured;
        await using (source.ConfigureAwait(false))
        {
            source.EndpointUrl = "opc.tcp://gds.example.invalid:4840/GDS";
            source.FilterText = "server-name";
            captured = ((IWorkspaceState)source).CaptureState();
        }

        var target = (GdsManagementPlugin)context.Create(PluginKind.GdsManagement);
        await using (target.ConfigureAwait(false))
        {
            await ((IWorkspaceState)target).RestoreStateAsync(captured, CancellationToken.None).ConfigureAwait(false);
            Assert.That(target.EndpointUrl, Is.EqualTo("opc.tcp://gds.example.invalid:4840/GDS"));
            Assert.That(target.FilterText, Is.EqualTo("server-name"));
            Assert.That(context.Connection.IsConnected, Is.False);
        }
    }

    [Test]
    public async Task GdsPushRestoresActiveBucketAndTrustListMaskOffline()
    {
        await using var context = new AdministrationTestContext();
        var source = (GdsPushPlugin)context.Create(PluginKind.GdsPush);
        JsonElement captured;
        await using (source.ConfigureAwait(false))
        {
            source.ActiveBucket = TrustListBucket.Issuer;
            source.TrustListMasks = TrustListMasks.TrustedCertificates;
            captured = ((IWorkspaceState)source).CaptureState();
        }

        var target = (GdsPushPlugin)context.Create(PluginKind.GdsPush);
        await using (target.ConfigureAwait(false))
        {
            await ((IWorkspaceState)target).RestoreStateAsync(captured, CancellationToken.None).ConfigureAwait(false);
            Assert.That(target.ActiveBucket, Is.EqualTo(TrustListBucket.Issuer));
            Assert.That(target.TrustListMasks, Is.EqualTo(TrustListMasks.TrustedCertificates));
            Assert.That(context.Connection.IsConnected, Is.False);
        }
    }

    [Test]
    public async Task GdsDiscoveryRestoresQueryFilterOffline()
    {
        await using var context = new AdministrationTestContext();
        var plugin = (GdsDiscoveryPlugin)context.Create(PluginKind.GdsDiscovery);
        await using (plugin.ConfigureAwait(false))
        {
            var seed = new GdsDiscoveryState
            {
                LocalMachineUrl = "opc.tcp://lds.example.invalid:4840/",
                Filter = new GdsDiscoveryState.QueryFilter { ApplicationName = "Widget", ProductUri = "urn:widget" }
            };
            seed.Filter.ServerCapabilities.Add("DA");
            JsonElement seeded = JsonSerializer.SerializeToElement(
                seed, GdsDiscoveryStateJsonContext.Default.GdsDiscoveryState);

            await ((IWorkspaceState)plugin).RestoreStateAsync(seeded, CancellationToken.None).ConfigureAwait(false);
            Assert.That(plugin.LocalMachineUrl, Is.EqualTo("opc.tcp://lds.example.invalid:4840/"));

            GdsDiscoveryState roundTripped = ((IWorkspaceState)plugin).CaptureState()
                .Deserialize(GdsDiscoveryStateJsonContext.Default.GdsDiscoveryState)!;
            Assert.That(roundTripped.Filter.ApplicationName, Is.EqualTo("Widget"));
            Assert.That(roundTripped.Filter.ServerCapabilities, Does.Contain("DA"));
            Assert.That(context.Connection.IsConnected, Is.False);
        }
    }

    [Test]
    public async Task FileSystemRestoresRootFilterOffline()
    {
        await using var context = new AdministrationTestContext();
        var plugin = (FileSystemPlugin)context.Create(PluginKind.FileSystem);
        await using (plugin.ConfigureAwait(false))
        {
            var seed = new FileSystemState { AllowFileSystem = false, AllowDirectory = false, AllowFile = true };
            seed.Roots.Add(new FileSystemState.RootSpec { NodeId = "ns=2;s=Root", DisplayName = "Docs" });
            JsonElement seeded = JsonSerializer.SerializeToElement(
                seed, FileSystemStateJsonContext.Default.FileSystemState);

            await ((IWorkspaceState)plugin).RestoreStateAsync(seeded, CancellationToken.None).ConfigureAwait(false);

            FileSystemState roundTripped = ((IWorkspaceState)plugin).CaptureState()
                .Deserialize(FileSystemStateJsonContext.Default.FileSystemState)!;
            Assert.That(roundTripped.AllowFile, Is.True);
            Assert.That(roundTripped.AllowFileSystem, Is.False);
            Assert.That(roundTripped.AllowDirectory, Is.False);
            // Roots re-attach against a live session, so an offline capture has none yet.
            Assert.That(roundTripped.Roots, Is.Empty);
            Assert.That(context.Connection.IsConnected, Is.False);
        }
    }

    [Test]
    public async Task CertificateManagerCapturesVersionedEmptyStoresOffline()
    {
        await using var context = new AdministrationTestContext();
        var plugin = (CertificateManagerPlugin)context.Create(PluginKind.CertificateManager);
        await using (plugin.ConfigureAwait(false))
        {
            CertificateManagerState captured = ((IWorkspaceState)plugin).CaptureState()
                .Deserialize(CertificateManagerStateJsonContext.Default.CertificateManagerState)!;
            Assert.That(captured.Version, Is.EqualTo(1));
            Assert.That(captured.CustomStores, Is.Empty);

            var seed = new CertificateManagerState { SelectedStorePath = "pki/trusted" };
            seed.CustomStores.Add(new CertificateManagerState.CustomStore { Path = "pki/extra", DisplayName = "Extra" });
            JsonElement seeded = JsonSerializer.SerializeToElement(
                seed, CertificateManagerStateJsonContext.Default.CertificateManagerState);
            await ((IWorkspaceState)plugin).RestoreStateAsync(seeded, CancellationToken.None).ConfigureAwait(false);
            Assert.That(context.Connection.IsConnected, Is.False);
        }
    }
}
