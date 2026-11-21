// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
public sealed partial class GdsPushWorkflowTests
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

    private static readonly string[] s_endpointNotificationsFollowWorkspaceChangesOnlyUntilAsyncDispExpected =
    [
        "opc.tcp://new.test:4841",
    ];
}
