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
