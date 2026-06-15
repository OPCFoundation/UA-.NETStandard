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

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Strongly-typed options for the OPC UA MCP server.
    /// </summary>
    /// <remarks>
    /// Bound from the <c>McpServer</c> configuration section at host
    /// startup and consumed by individual tool helpers via the
    /// <c>IServiceProvider</c> that the MCP framework injects into
    /// each tool invocation. Both properties are optional; when a
    /// value is not configured the consuming tool falls back to the
    /// per-tool environment variable (if any) and then to a per-user
    /// default directory.
    /// </remarks>
    public sealed class McpServerOptions
    {
        /// <summary>
        /// Base directory under which the
        /// <see cref="Opc.Ua.Mcp.Tools.NodeSetExportTools"/> is
        /// allowed to write exported NodeSet2 XML files. When
        /// <c>null</c> or whitespace the tool falls back to the
        /// <c>OPCUA_MCP_EXPORT_ROOT</c> environment variable and
        /// finally to a default under the system temp folder.
        /// </summary>
        public string? NodeSetExportRoot { get; set; }

        /// <summary>
        /// Base directory under which
        /// <see cref="Opc.Ua.Mcp.Tools.PacketDecodeTools"/> is
        /// allowed to read pcap and keylog files. When <c>null</c>
        /// or whitespace the tool falls back to
        /// <c>PcapOptions.BaseFolder</c> resolved from DI, and
        /// finally to a default under the per-user
        /// <c>LocalApplicationData</c> directory.
        /// </summary>
        public string? PcapBaseFolder { get; set; }
    }
}
