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
