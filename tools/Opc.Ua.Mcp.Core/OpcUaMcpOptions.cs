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

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Strongly-typed options for the OPC UA MCP server.
    /// </summary>
    /// <remarks>
    /// Bound from the <c>McpServer</c> configuration section at host
    /// startup and consumed by the MCP host and individual tool helpers.
    /// </remarks>
    public sealed class OpcUaMcpOptions
    {
        /// <summary>
        /// Gets or sets the tool catalog exposed by the MCP server.
        /// </summary>
        /// <remarks>
        /// When <see cref="ToolProfiles"/> is empty this single value drives
        /// the tool set, so a host that only wants one profile does not have to
        /// think about composition. Setting <see cref="ToolProfiles"/> takes
        /// precedence over this value, so an agent that needs several profiles
        /// - vision plus robotics, for instance - configures the set and leaves
        /// this property alone.
        /// </remarks>
        public McpToolProfile ToolProfile { get; set; } = McpToolProfile.Full;

        /// <summary>
        /// Gets or sets the composed tool catalog exposed by the MCP server.
        /// </summary>
        /// <remarks>
        /// When non-empty this set takes precedence over <see cref="ToolProfile"/>
        /// and lists every profile the server should carry. This is how a host
        /// composes several bounded profiles into one meaningful catalog - for
        /// example, <c>Vision</c> plus <c>Robotics</c> for a vision-guided
        /// pick-and-place agent - without pulling in every other profile
        /// through <see cref="McpToolProfile.Full"/>.
        /// </remarks>
        public McpToolProfileSet ToolProfiles { get; set; }

        /// <summary>
        /// The effective set of tool profiles the host should register, so a
        /// caller does not have to know whether the single-profile or composed
        /// selection is in force.
        /// </summary>
        public McpToolProfileSet EffectiveToolProfiles => ToolProfiles.IsEmpty
            ? new McpToolProfileSet(ToolProfile)
            : ToolProfiles;

        /// <summary>
        /// Base directory under which the
        /// <see cref="Tools.NodeSetExportTools"/> is
        /// allowed to write exported NodeSet2 XML files. When
        /// <c>null</c> or whitespace the tool falls back to the
        /// <c>OPCUA_MCP_EXPORT_ROOT</c> environment variable and
        /// finally to a default under the system temp folder.
        /// </summary>
        public string? NodeSetExportRoot { get; set; }

        /// <summary>
        /// Base directory under which the packet decode tools of
        /// <c>Opc.Ua.Mcp.Diagnostics</c> are allowed to read pcap and keylog
        /// files. When <c>null</c> or whitespace the tool falls back to
        /// <c>PcapOptions.BaseFolder</c> resolved from DI, and
        /// finally to a default under the per-user
        /// <c>LocalApplicationData</c> directory.
        /// </summary>
        public string? PcapBaseFolder { get; set; }

        /// <summary>
        /// Creates options from the well-known environment variables the MCP
        /// tools consume.
        /// </summary>
        /// <remarks>
        /// This is the shape a host gets when it does not configure anything,
        /// so an embedded server behaves like the shipped tool by default.
        /// </remarks>
        public static OpcUaMcpOptions FromEnvironment()
        {
            return new OpcUaMcpOptions
            {
                NodeSetExportRoot = Environment.GetEnvironmentVariable(
                    "OPCUA_MCP_NODESET_EXPORT_ROOT"),
                PcapBaseFolder = Environment.GetEnvironmentVariable(
                    "OPCUA_MCP_PCAP_BASE_FOLDER")
            };
        }
    }
}
