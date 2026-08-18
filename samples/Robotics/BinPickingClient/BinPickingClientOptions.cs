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
using System.Globalization;
using System.Linq;

namespace BinPickingClient
{
    /// <summary>
    /// Command-line options for the bin-picking client. Mirrors the switches accepted
    /// by <c>IntentViewerClient</c> where the intent is the same, and adds the two
    /// bin-picking specific switches (<c>--demo</c> and <c>--part</c>) that drive the
    /// scripted end-to-end demonstration.
    /// </summary>
    internal sealed record BinPickingClientOptions
    {
        public string ServerUrl { get; init; } = "opc.tcp://localhost:62855/BinPickingCell";

        public bool Insecure { get; init; }

        public bool View { get; init; }

        public string? Renderer { get; init; }

        /// <summary>
        /// Prim path of the camera the viewport opens on. Defaults to the fixed observer
        /// camera authored in the stage, which shows the cell working. Pass
        /// <c>--camera auto</c> to let the viewer frame the scene itself, or another prim
        /// path to pin a different view; note that the eye-in-hand sensor on the flange is
        /// also a camera prim, so pointing this at it shows what the tool sees rather than
        /// the cell.
        /// </summary>
        public string? CameraPath { get; init; } = DefaultObserverCameraPath;

        /// <summary>
        /// Raises the log level to Debug, which surfaces every live OpenUSD binding update
        /// and every target the connector had to leave unresolved.
        /// </summary>
        public bool Verbose { get; init; }

        /// <summary>
        /// Picks every part the detector reports and places them all on the destination,
        /// so they end up stacked, rather than running a single pick-and-place cycle.
        /// </summary>
        public bool StackAll { get; init; }

        /// <summary>
        /// The stage's fixed observer camera.
        /// </summary>
        public const string DefaultObserverCameraPath = "/World/ObserverCamera";

        public string? FetchAssetsDirectory { get; init; }

        public int Seconds { get; init; }

        public bool Mcp { get; init; }

        public bool Demo { get; init; }

        public string PartClassLabel { get; init; } = "RedCube";

        public string SourceLocationName { get; init; } = "Bin";

        public string DestinationLocationName { get; init; } = "Fixture";

        public string ToolBrowseName { get; init; } = "ParallelGripper";

        public string? Transport { get; init; }

        public int Port { get; init; } = 5170;

        public static BinPickingClientOptions Parse(string[] args)
        {
            return new BinPickingClientOptions
            {
                ServerUrl = GetOption(args, "--server") ?? "opc.tcp://localhost:62855/BinPickingCell",
                Insecure = HasFlag(args, "--insecure"),
                View = HasFlag(args, "--view"),
                Renderer = GetOption(args, "--renderer"),
                CameraPath = ResolveCameraPath(GetOption(args, "--camera")),
                Verbose = HasFlag(args, "--verbose"),
                StackAll = HasFlag(args, "--stack-all"),
                FetchAssetsDirectory = GetOption(args, "--fetch-assets"),
                Seconds = int.TryParse(
                    GetOption(args, "--seconds"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                    ? seconds
                    : 0,
                Mcp = HasFlag(args, "--mcp"),
                Demo = HasFlag(args, "--demo"),
                PartClassLabel = GetOption(args, "--part") ?? "RedCube",
                SourceLocationName = GetOption(args, "--source") ?? "Bin",
                DestinationLocationName = GetOption(args, "--destination") ?? "Fixture",
                ToolBrowseName = GetOption(args, "--tool") ?? "ParallelGripper",
                Transport = GetOption(args, "--transport"),
                Port = int.TryParse(
                    GetOption(args, "--port"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
                    ? port
                    : 5170
            };
        }

        private static string? GetOption(string[] args, string name)
        {
            for (int ii = 0; ii < args.Length - 1; ii++)
            {
                if (string.Equals(args[ii], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[ii + 1];
                }
            }
            return null;
        }

        private static bool HasFlag(string[] args, string name)
        {
            return args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Maps the <c>--camera</c> argument onto a prim path, treating <c>auto</c> as
        /// "let the viewer frame the scene" and no argument at all as the stage's observer
        /// camera.
        /// </summary>
        private static string? ResolveCameraPath(string? requested)
        {
            if (requested is null)
            {
                return DefaultObserverCameraPath;
            }
            return string.Equals(requested, "auto", StringComparison.OrdinalIgnoreCase)
                ? null
                : requested;
        }

        internal BinPickingClientMcpTransportSelection SelectMcpTransport()
        {
            if (Transport is not null &&
                TryParseMcpTransport(Transport, out BinPickingClientMcpTransport requestedTransport))
            {
                if (View && requestedTransport == BinPickingClientMcpTransport.Stdio)
                {
                    return new BinPickingClientMcpTransportSelection(
                        requestedTransport,
                        true,
                        "WARNING: --transport stdio was explicitly requested with --view. " +
                        "MCP stdio uses stdout for protocol frames and the in-process viewer may share that stream; " +
                        "protocol corruption is possible.");
                }

                return new BinPickingClientMcpTransportSelection(
                    requestedTransport,
                    true,
                    $"Using explicitly requested MCP transport '{requestedTransport.ToOptionValue()}'.");
            }

            if (View)
            {
                return new BinPickingClientMcpTransportSelection(
                    BinPickingClientMcpTransport.Http,
                    false,
                    "Using MCP transport 'http' because --view is enabled. MCP stdio frames use stdout, " +
                    "which cannot safely coexist with the in-process OpenUSD viewer.");
            }

            return new BinPickingClientMcpTransportSelection(
                BinPickingClientMcpTransport.Stdio,
                false,
                "Using default MCP transport 'stdio'.");
        }

        internal static bool TryParseMcpTransport(string? value, out BinPickingClientMcpTransport transport)
        {
            if (string.Equals(value, "stdio", StringComparison.OrdinalIgnoreCase))
            {
                transport = BinPickingClientMcpTransport.Stdio;
                return true;
            }

            if (string.Equals(value, "http", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "sse", StringComparison.OrdinalIgnoreCase))
            {
                transport = BinPickingClientMcpTransport.Http;
                return true;
            }

            transport = BinPickingClientMcpTransport.Stdio;
            return false;
        }
    }

    internal enum BinPickingClientMcpTransport
    {
        Stdio,

        Http
    }

    internal sealed record BinPickingClientMcpTransportSelection(
        BinPickingClientMcpTransport Transport,
        bool Explicit,
        string Message);

    internal static class BinPickingClientMcpTransportExtensions
    {
        public static string ToOptionValue(this BinPickingClientMcpTransport transport)
        {
            return transport switch
            {
                BinPickingClientMcpTransport.Stdio => "stdio",
                BinPickingClientMcpTransport.Http => "http",
                _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown MCP transport.")
            };
        }
    }
}
