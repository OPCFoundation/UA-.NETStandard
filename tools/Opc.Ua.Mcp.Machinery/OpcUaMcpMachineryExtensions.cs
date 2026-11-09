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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;
using Opc.Ua.Mcp.Tools;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Registers OPC 40001 Machinery tools in an embedding MCP host.
    /// </summary>
    public static class OpcUaMcpMachineryExtensions
    {
        /// <summary>
        /// Registers Machinery tools; each invocation resolves its named session anew.
        /// </summary>
        public static IServiceCollection AddOpcUaMcpMachinery(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.TryAddTransient<MachineryTools>();
            return services;
        }

        /// <summary>
        /// Adds Machinery and connection tools for the Machinery or Full profile.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaMachineryTools(
            this IMcpServerBuilder builder,
            McpToolProfile toolProfile = McpToolProfile.Full)
        {
            ArgumentNullException.ThrowIfNull(builder);
            return builder.WithOpcUaMachineryTools(new McpToolProfileSet(toolProfile));
        }

        /// <summary>
        /// Adds Machinery tools to a composed catalog, with idempotent connection tools.
        /// </summary>
        public static IMcpServerBuilder WithOpcUaMachineryTools(
            this IMcpServerBuilder builder,
            McpToolProfileSet toolProfiles)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (toolProfiles.Contains(McpToolProfile.Machinery) || toolProfiles.Contains(McpToolProfile.Full))
            {
                builder.WithOpcUaConnectionTools().WithTools<MachineryTools>();
            }
            return builder;
        }
    }
}
