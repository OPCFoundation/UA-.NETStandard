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

using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.FileSystem;
using UaLens.Plugins.GdsDiscovery;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.Administration;

/// <summary>
/// Verifies cross-cutting administration conventions: neutral (non-emoji) tree
/// markers, the reserved logging event-id block, offline availability of the local
/// tools, cancellation of the connection hook, and safe confirmation gating.
/// </summary>
[TestFixture]
public sealed class AdministrationConventionTests
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
    public void DiscoveryTreeMarkersAreNeutralAsciiWithoutEmoji()
    {
        FieldInfo[] fields = typeof(DiscoveryGlyphs).GetFields(BindingFlags.Public | BindingFlags.Static);
        Assert.That(fields, Is.Not.Empty);
        foreach (FieldInfo field in fields)
        {
            if (!field.IsLiteral || field.FieldType != typeof(string))
            {
                continue;
            }
            var value = (string)field.GetRawConstantValue()!;
            Assert.That(value, Is.Not.Empty, $"DiscoveryGlyphs.{field.Name} must not be blank.");
            foreach (char character in value)
            {
                Assert.That(char.IsHighSurrogate(character) || char.IsLowSurrogate(character), Is.False,
                    $"DiscoveryGlyphs.{field.Name} must not use emoji.");
                Assert.That((int)character, Is.LessThan(0x2000),
                    $"DiscoveryGlyphs.{field.Name} must be neutral ASCII, not a pictographic symbol.");
            }
        }
    }

    [Test]
    public void AdministrationEventIdsAreReservedInTheThreeThousandBlock()
    {
        int[] bases =
        [
            UaLensEventIds.CertificateManagerBase,
            UaLensEventIds.FileSystemBase,
            UaLensEventIds.FileSystemNodeBase,
            UaLensEventIds.GdsDiscoveryBase,
            UaLensEventIds.GdsManagementBase,
            UaLensEventIds.GdsPushBase,
            UaLensEventIds.GdsSessionBase,
            UaLensEventIds.RoleManagementBase,
            UaLensEventIds.UserManagementBase
        ];
        foreach (int reserved in bases)
        {
            Assert.That(reserved, Is.InRange(3000, 5999));
        }
        Assert.That(bases, Is.Unique);
    }

    [Test]
    public void LocalToolsAreAvailableWithoutAPrimaryConnection()
    {
        Assert.That(
            PluginRegistry.For(PluginKind.CertificateManager).ConnectionScope,
            Is.EqualTo(ToolConnectionScope.Local));
        Assert.That(
            PluginRegistry.For(PluginKind.GdsDiscovery).ConnectionScope,
            Is.EqualTo(ToolConnectionScope.Local));
    }

    [Test]
    public async Task EveryOwnedToolConstructsAndDisposesOffline()
    {
        foreach (PluginKind kind in s_ownedKinds)
        {
            await using var context = new AdministrationTestContext();
            IPlugin plugin = context.Create(kind);
            Assert.That(plugin.Kind, Is.EqualTo(kind));
            Assert.That(context.Connection.IsConnected, Is.False);
            await plugin.DisposeAsync().ConfigureAwait(false);
        }
    }

    [Test]
    public async Task OwnedToolConnectionHooksHonorCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);
        foreach (PluginKind kind in s_ownedKinds)
        {
            await using var context = new AdministrationTestContext();
            IPlugin plugin = context.Create(kind);
            await using (plugin.ConfigureAwait(false))
            {
                await Assert.ThatAsync(
                    async () => await ((IWorkspaceDocument)plugin)
                        .OnConnectionStateChangedAsync(cancellation.Token).ConfigureAwait(false),
                    Throws.InstanceOf<System.OperationCanceledException>()).ConfigureAwait(false);
            }
        }
    }

    [Test]
    public async Task FileSystemDeleteIsANoOpWithoutASelection()
    {
        await using var context = new AdministrationTestContext();
        var plugin = (FileSystemPlugin)context.Create(PluginKind.FileSystem);
        await using (plugin.ConfigureAwait(false))
        {
            Assert.That(plugin.SelectedNode, Is.Null);
            // No selection: the destructive path returns before any confirmation dialog.
            await plugin.DeleteAsync().ConfigureAwait(false);
            Assert.That(context.Connection.IsConnected, Is.False);
        }
    }
}
