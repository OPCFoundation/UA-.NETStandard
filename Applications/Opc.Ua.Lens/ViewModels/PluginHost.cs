/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using UaLens.Connection;

namespace UaLens.ViewModels;

/// <summary>
/// Lightweight bundle of shared services passed to each
/// <see cref="IPlugin"/> factory.  Keeps the per-kind
/// view-model surface clean by avoiding a transitive dependency on the
/// full <see cref="MainViewModel"/>.  The values reflect the host
/// state AT FACTORY TIME; for properties that may change over a
/// session's lifetime (e.g. <see cref="ConnectionService.Session"/>
/// when the user reconnects), each tab should re-read via the
/// <see cref="Main"/> view model.
/// </summary>
internal sealed record PluginHost(
    MainViewModel Main,
    ISession? Session,
    ConnectionService Connection,
    BrowserViewModel Browser,
    ILogger Log);
