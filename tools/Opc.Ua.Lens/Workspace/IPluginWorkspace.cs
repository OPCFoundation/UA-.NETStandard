// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Diagnostics;
using UaLens.Plugins.Gds;
using UaLens.ViewModels;

namespace UaLens.Workspace;

/// <summary>
/// Shared working context used by cooperating tools. It deliberately grants no
/// authority to mutate document membership, disconnect a session or dispose the shell.
/// </summary>
internal interface IPluginWorkspace : INotifyPropertyChanged
{
    IPlugin? ActiveDocument { get; }

    string EndpointUrl { get; set; }

    RegisteredApplicationContext? CurrentRegisteredApp { get; set; }

    NodeViewModel? SelectedNode { get; }

    bool IsAddressSpaceVisible { get; }

    ResourceMonitorHost? ResourceMonitor { get; }

    Task<IPlugin> OpenToolAsync(
        PluginKind kind,
        EndpointDescription? discoveryEndpoint = null,
        CancellationToken cancellationToken = default);
}
