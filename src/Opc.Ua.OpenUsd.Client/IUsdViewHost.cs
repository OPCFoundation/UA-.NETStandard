/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.OpenUsd.Client
{
    /// <summary>
    /// Describes the viewport an <see cref="IUsdViewHost"/> should open.
    /// </summary>
    public sealed class UsdViewOptions
    {
        /// <summary>
        /// Absolute path of the composed stage to render. This is normally the
        /// <c>stage.usda</c> the connector writes after fetching the server's asset
        /// closure, which sublayers the live override over the served base layers.
        /// </summary>
        public string StagePath { get; set; } = string.Empty;

        /// <summary>
        /// Directory holding the staged USD plugin tree (the parent of <c>plugin/usd</c>).
        /// When <c>null</c> the host falls back to its own discovery.
        /// </summary>
        public string? PluginPath { get; set; }

        /// <summary>
        /// Renderer preference (<c>Auto</c>, <c>Storm</c>, <c>D3D12</c>, <c>Vulkan</c>, or
        /// <c>Metal</c>). When <c>null</c> the host's default selection applies.
        /// </summary>
        public string? Renderer { get; set; }

        /// <summary>
        /// Window title. When <c>null</c> the host's default title is kept.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Prim path of a camera in the stage to open on, for example an overhead camera
        /// framing the whole scene. When <c>null</c> the host frames the stage itself.
        /// </summary>
        public string? CameraPath { get; set; }

        /// <summary>
        /// Telemetry used by the optional viewport host to report degraded renderer
        /// picking and other best-effort viewport failures.
        /// </summary>
        public ITelemetryContext? Telemetry { get; set; }

        /// <summary>
        /// Optional callback invoked when the viewport host resolves an operator pick to a
        /// USD prim path.
        /// </summary>
        /// <remarks>
        /// A host resolves the operator pick with whatever mechanism it supports. The
        /// optional OpenUSD viewer host uses the viewer's renderer-backed pick callback
        /// when available and otherwise watches <see cref="CommandPrimPath"/> for a
        /// <c>targetPrim</c> relationship or attribute. <see cref="PickMode"/> selects
        /// between those mechanisms. Hosts that can do neither simply never invoke this
        /// callback. The callback runs from a background worker, not from the UI thread.
        /// </remarks>
        public Func<string, CancellationToken, Task>? PrimPicked { get; set; }

        /// <summary>
        /// Fallback command prim watched by hosts that cannot resolve real viewport
        /// picks. The prim should expose a <c>targetPrim</c> relationship, string
        /// attribute, or token attribute containing the picked target prim path.
        /// </summary>
        /// <remarks>
        /// This is the command-channel fallback used when <see cref="PickMode"/> selects
        /// it explicitly or when automatic renderer-backed picking is unavailable.
        /// </remarks>
        public string CommandPrimPath { get; set; } = UsdViewPickCommand.DefaultCommandPrimPath;

        /// <summary>
        /// Selects how the host turns viewport picks into <see cref="PrimPicked"/>
        /// callbacks.
        /// </summary>
        /// <remarks>
        /// The default uses renderer-backed pointer picking first and falls back to
        /// watching <see cref="CommandPrimPath"/> only if renderer picking is not
        /// available or reports an unsupported pick.
        /// </remarks>
        public UsdViewPickMode PickMode { get; set; } = UsdViewPickMode.Auto;
    }

    /// <summary>
    /// Describes how an <see cref="IUsdViewHost"/> should resolve operator picks.
    /// </summary>
    public enum UsdViewPickMode
    {
        /// <summary>
        /// Use renderer-backed picks when the host can reach them, otherwise fall back to
        /// watching <see cref="UsdViewOptions.CommandPrimPath"/>.
        /// </summary>
        Auto,

        /// <summary>
        /// Use only renderer-backed pointer picks. Hosts that cannot reach renderer
        /// picking do not invoke <see cref="UsdViewOptions.PrimPicked"/>.
        /// </summary>
        Renderer,

        /// <summary>
        /// Use only the command prim fallback that watches
        /// <see cref="UsdViewOptions.CommandPrimPath"/>.
        /// </summary>
        CommandPrim
    }

    /// <summary>
    /// A renderer that opens a window on a USD stage and exposes that stage as an
    /// <see cref="IUsdSink"/>, so the connector can stream live OPC UA values into the
    /// picture it is showing.
    /// </summary>
    /// <remarks>
    /// The implementation lives in a separate, optional assembly so the connector package
    /// stays free of a renderer and its native payload. It is deliberately expressed
    /// without any USD type: the connector never references the rendering stack.
    /// </remarks>
    public interface IUsdViewHost
    {
        /// <summary>
        /// Opens the viewport and runs its event loop on the calling thread until the
        /// window closes or <paramref name="cancellationToken"/> is cancelled. On Windows
        /// the calling thread must be the process main thread and marked
        /// <c>[STAThread]</c>.
        /// </summary>
        /// <param name="options">The stage, plugin tree, renderer, and title to use.</param>
        /// <param name="sessionAsync">
        /// Invoked on a background task once the stage is composed and rendering, with a
        /// sink that authors into the rendered stage. The viewport keeps running while it
        /// executes; returning or throwing does not close the window.
        /// </param>
        /// <param name="cancellationToken">Closes the viewport when cancelled.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="options"/> or <paramref name="sessionAsync"/> is <c>null</c>.
        /// </exception>
        void RunViewport(
            UsdViewOptions options,
            Func<IUsdSink, CancellationToken, Task> sessionAsync,
            CancellationToken cancellationToken);
    }

    internal static class UsdViewPickCommand
    {
        public static bool TryUpdatePickedPrim(
            string? targetPrimPath,
            ref string? lastTargetPrimPath,
            bool emitInitialTarget,
            out string pickedPrimPath)
        {
            pickedPrimPath = string.Empty;
            string normalized = NormalizePrimPath(targetPrimPath);
            if (normalized.Length == 0)
            {
                lastTargetPrimPath = normalized;
                return false;
            }

            if (lastTargetPrimPath is null)
            {
                lastTargetPrimPath = normalized;
                if (!emitInitialTarget)
                {
                    return false;
                }
            }
            else if (string.Equals(lastTargetPrimPath, normalized, StringComparison.Ordinal))
            {
                return false;
            }
            else
            {
                lastTargetPrimPath = normalized;
            }

            pickedPrimPath = normalized;
            return true;
        }

        private static string NormalizePrimPath(string? primPath)
        {
            if (string.IsNullOrWhiteSpace(primPath))
            {
                return string.Empty;
            }

            string trimmed = primPath.Trim();
            return trimmed[0] == '/' ? trimmed : string.Empty;
        }

        public const string DefaultCommandPrimPath = "/World/IntentCommand";
    }
}
