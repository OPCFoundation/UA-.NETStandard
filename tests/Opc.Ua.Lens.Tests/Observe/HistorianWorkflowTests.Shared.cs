// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Historian;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;
using UaLens.Views;
using UaLens.Workspace;

namespace UaLens.Tests.Observe;

public sealed partial class HistorianWorkflowTests
{
    private static readonly DateTime s_time = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    private static HistoryRow Row(int seconds, Variant value)
    {
        return new HistoryRow(s_time.AddSeconds(seconds), s_time.AddSeconds(seconds + 1), value, StatusCodes.Good);
    }

    internal static PluginHost Host(DesktopConnectionContext context, NodeViewModel? selection = null)
    {
        var workspace = new Mock<IPluginWorkspace>();
        workspace.SetupGet(w => w.SelectedNode).Returns(selection);
        return new PluginHost(workspace.Object, context.Connection, context.Browser, context.Telemetry);
    }
}
