// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using OpenUsd;

namespace Opc.Ua.OpenUsd.Connector.Viewer
{
    /// <summary>
    /// Registers only the application-bundled runtime manifests before the first asset resolution.
    /// RID-less hosts may load the DLL below runtimes/RID/native while the manifests stay at the output root.
    /// </summary>
    internal static class UsdViewportRuntime
    {
        /// <summary>
        /// Initializes the bundled resolver/schema and imaging trees after explicit viewport activation.
        /// The native registry is idempotent; no environment variables or server-supplied plugin paths are used.
        /// </summary>
        public static void Initialize()
        {
            string core = Path.Combine(AppContext.BaseDirectory, "usd", "plugInfo.json");
            string imaging = Path.Combine(AppContext.BaseDirectory, "plugin", "usd", "plugInfo.json");
            if (!File.Exists(core) || !File.Exists(imaging))
            {
                throw new DirectoryNotFoundException(
                    "The bundled OpenUSD core/imaging plugin manifests are missing. Publish the matching RID payload.");
            }
            _ = UsdPluginRegistry.Register(core);
            _ = UsdPluginRegistry.Register(imaging);
        }
    }
}
