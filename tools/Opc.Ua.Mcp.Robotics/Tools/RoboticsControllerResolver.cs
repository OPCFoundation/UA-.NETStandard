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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Robotics.Client.Intent;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Resolves a controller selector (unique display name, BrowseName, or NodeId string) to a
    /// <see cref="RobotIntentControllerClient"/>, and resolves scoped resource selectors against
    /// a controller's published lookup tables. Name resolution is unambiguous: when two entries
    /// share a name the caller must use the full NodeId to disambiguate.
    /// </summary>
    internal static class RoboticsControllerResolver
    {
        /// <summary>
        /// Resolves <paramref name="controllerIdOrName"/> to a unique controller on
        /// <paramref name="client"/>. Accepts either an OPC UA NodeId string (for example
        /// <c>ns=2;s=RobotIntent/Controllers/C1</c>) or a controller display name or BrowseName
        /// (for example <c>Controller1</c>). Names are trimmed and compared with exact ordinal
        /// comparison; the discovery browse happens only when the selector is not a NodeId.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="controllerIdOrName"/> is empty or whitespace, names zero
        /// controllers, or names more than one controller.
        /// </exception>
        public static async ValueTask<RobotIntentControllerClient> ResolveAsync(
            RobotIntentClient client,
            string controllerIdOrName,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentException.ThrowIfNullOrWhiteSpace(controllerIdOrName);

            string trimmed = controllerIdOrName.Trim();
            if (NodeId.TryParse(trimmed, out NodeId nodeId) && !nodeId.IsNull)
            {
                return client.Controller(nodeId);
            }

            ArrayOf<RobotIntentNodeLookupEntry> controllers =
                await client.DiscoverControllersAsync(ct).ConfigureAwait(false);

            List<RobotIntentNodeLookupEntry> matches = MatchByName(controllers, trimmed);
            if (matches.Count == 0)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"No controller named '{trimmed}' was found. " +
                        $"Available: [{FormatNamesAndNodeIds(controllers)}]."),
                    nameof(controllerIdOrName));
            }

            if (matches.Count > 1)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"Ambiguous controller name '{trimmed}' matches {matches.Count} controllers. " +
                        $"Use the full NodeId to disambiguate: [{FormatNodeIds(matches)}]."),
                    nameof(controllerIdOrName));
            }

            return client.Controller(matches[0].NodeId);
        }

        /// <summary>
        /// Resolves a scoped resource (tool, frame, location, output, program, axis) within the
        /// controller's published lookup tables. Accepts a NodeId string, a unique display name,
        /// or a unique BrowseName from the corresponding lookup list.
        /// </summary>
        /// <param name="nameOrNodeId">Display name, BrowseName, or NodeId string.</param>
        /// <param name="entries">The lookup entries for the resource category.</param>
        /// <param name="category">Human-readable category name used in error messages.</param>
        /// <returns>The resolved NodeId, or a null NodeId when the selector is empty.</returns>
        public static NodeId ResolveScopedResource(
            string? nameOrNodeId,
            ArrayOf<RobotIntentNodeLookupEntry> entries,
            string category)
        {
            if (string.IsNullOrWhiteSpace(nameOrNodeId))
            {
                return NodeId.Null;
            }

            string trimmed = nameOrNodeId.Trim();
            if (NodeId.TryParse(trimmed, out NodeId nodeId) && !nodeId.IsNull)
            {
                return nodeId;
            }

            List<RobotIntentNodeLookupEntry> matches = MatchByName(entries, trimmed);
            if (matches.Count == 0)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"No {category} named '{trimmed}' found. " +
                        $"Available: [{FormatNamesAndNodeIds(entries)}]."),
                    nameof(nameOrNodeId));
            }

            if (matches.Count > 1)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"Ambiguous {category} name '{trimmed}' matches {matches.Count} entries. " +
                        $"Use the full NodeId to disambiguate: [{FormatNodeIds(matches)}]."),
                    nameof(nameOrNodeId));
            }

            return matches[0].NodeId;
        }

        private static List<RobotIntentNodeLookupEntry> MatchByName(
            ArrayOf<RobotIntentNodeLookupEntry> entries,
            string trimmed)
        {
            var matches = new List<RobotIntentNodeLookupEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                RobotIntentNodeLookupEntry entry = entries[i];
                if (string.Equals(entry.Name, trimmed, StringComparison.Ordinal) ||
                    string.Equals(entry.BrowseName.Name, trimmed, StringComparison.Ordinal))
                {
                    matches.Add(entry);
                }
            }

            return matches;
        }

        private static string FormatNamesAndNodeIds(ArrayOf<RobotIntentNodeLookupEntry> entries)
        {
            if (entries.Count == 0)
            {
                return "(none)";
            }

            var items = new List<string>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                items.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{entries[i].Name} ({entries[i].NodeId})"));
            }
            return string.Join(", ", items);
        }

        private static string FormatNodeIds(List<RobotIntentNodeLookupEntry> entries)
        {
            var ids = new List<string>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                ids.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{entries[i].Name} ({entries[i].NodeId})"));
            }
            return string.Join(", ", ids);
        }
    }
}
