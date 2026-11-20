// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.OpenUsd.Connector;

// Standalone OPC UA -> OpenUSD connector client.
//
// Usage: Opc.Ua.OpenUsd.Connector [--server <opc.tcp url>] [--out <live.usda>]
//            [--seconds N] [--fetch-assets <dir>] [--insecure]
//            [--enable-commands] [--command-value <double>] [--federate]
//            [--view] [--renderer <Auto|Storm|D3D12|Vulkan>] [--stage <stage.usda>]
//            [--plugins <plugin/usd dir>] [--pick-command [<prim path>]]
//
// --federate honours component bindings that name another server, opening a session
// to each and composing it into the same stage. It is opt-in because the endpoint
// comes from the server rather than the operator; the same --insecure posture
// governs certificate acceptance for those sessions.
//
// --view renders the composed stage and streams the same live values into it, so the
// twin animates on screen. It needs the optional Opc.Ua.OpenUsd.Connector.Viewer
// assembly alongside the connector; the runner hosts the viewport on its own dedicated
// UI thread, so this entry point stays fully asynchronous.
return await OpenUsdConnectorRunner.RunAsync(args).ConfigureAwait(false);
