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
using System.Collections.Generic;
using System.Text.Json;
using Opc.Ua.MachineTool;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Validates bounded native MCP inputs before any session access or device operation.
    /// </summary>
    internal static partial class MachineToolParameters
    {
        /// <summary>
        /// Requires a bounded, non-null OPC UA identifier using the repository's common parser.
        /// </summary>
        internal static NodeId Node(string value)
        {
            Text(value, 1024);
            NodeId node = OpcUaJsonHelper.ParseNodeId(value);
            if (node.IsNull)
            {
                throw new ArgumentException("A non-null OPC UA NodeId is required.", nameof(value));
            }
            return node;
        }

        /// <summary>
        /// Rejects blank and oversized input before resolving a session.
        /// </summary>
        internal static void Text(string value, int maximumLength)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maximumLength);
        }

        /// <summary>
        /// Validates inclusive integer limits shared by metadata and runtime enforcement.
        /// </summary>
        internal static void Bound(int value, int minimum, int maximum)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, minimum);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, maximum);
        }

        /// <summary>
        /// Limits every whole MCP response independently from any underlying decoded-payload bound.
        /// </summary>
        internal static void ResponseLimit(int value)
        {
            Bound(value, 1024, MaximumResponseBytes);
        }

        /// <summary>
        /// Requires explicit authorization of an attempted destructive or device-changing workflow.
        /// </summary>
        internal static void Consent(bool confirmed)
        {
            if (!confirmed)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                    "Explicit confirm=true is required. Consent never bypasses server authorization.");
            }
        }

        /// <summary>
        /// Converts a native JSON array to distinct NodeIds without exposing ArrayOf backing memory.
        /// </summary>
        internal static ArrayOf<NodeId> Nodes(JsonElement values)
        {
            if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is < 1 or > 256)
            {
                throw new ArgumentException("nodeIds must contain 1-256 distinct NodeId strings.", nameof(values));
            }
            var result = new List<NodeId>(values.GetArrayLength());
            var seen = new HashSet<NodeId>();
            foreach (JsonElement element in values.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    throw new ArgumentException("Every nodeIds entry must be a string.", nameof(values));
                }
                NodeId node = Node(element.GetString()!);
                if (!seen.Add(node))
                {
                    throw new ArgumentException("Monitored NodeIds must be distinct.", nameof(values));
                }
                result.Add(node);
            }
            return result.ToArrayOf();
        }

        /// <summary>
        /// Reuses the typed client's count and total-window contract.
        /// </summary>
        internal static MachineToolObservationOptions Observation(int seconds, int notifications, int responseBytes)
        {
            Bound(seconds, 1, 60);
            ResponseLimit(responseBytes);
            var options = new MachineToolObservationOptions
            {
                Window = TimeSpan.FromSeconds(seconds),
                MaximumNotifications = notifications
            };
            options.Validate();
            return options;
        }

        /// <summary>The hard whole-response limit; ordinary queries default to 128 KiB.</summary>
        internal const int MaximumResponseBytes = 1048576;
    }
}
