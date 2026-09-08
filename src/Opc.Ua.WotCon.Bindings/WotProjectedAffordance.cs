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

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// Immutable local identity of a projected interaction. Unlike a form's
    /// addressing, these identities belong to the materialized generation.
    /// The containing plan's resource xid and <see cref="JsonPointer"/> identify
    /// the declaration, including when different documents reuse a name.
    /// </summary>
    public sealed class WotProjectedAffordance
    {
        /// <summary>
        /// Initializes a local method or event declaration.
        /// </summary>
        public WotProjectedAffordance(
            WotAffordanceKind kind,
            string name,
            string jsonPointer,
            string nodeId,
            string ownerNodeId,
            string? conditionAction = null,
            string? actsOn = null,
            string? conditionTypeId = null)
        {
            Kind = kind;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            JsonPointer = jsonPointer ?? throw new ArgumentNullException(nameof(jsonPointer));
            NodeId = nodeId ?? throw new ArgumentNullException(nameof(nodeId));
            OwnerNodeId = ownerNodeId ?? throw new ArgumentNullException(nameof(ownerNodeId));
            ConditionAction = conditionAction;
            ActsOn = actsOn;
            ConditionTypeId = conditionTypeId;
        }

        /// <summary>
        /// Gets the interaction kind.
        /// </summary>
        public WotAffordanceKind Kind { get; }

        /// <summary>
        /// Gets the authored affordance name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the RFC 6901 pointer to the local declaration, not its form.
        /// </summary>
        public string JsonPointer { get; }

        /// <summary>
        /// Gets the local Method or EventType identity. Empty means unresolved,
        /// never an instruction to use the upstream form's target instead.
        /// </summary>
        public string NodeId { get; }

        /// <summary>
        /// Gets the local owning Object, also the default event notifier.
        /// </summary>
        public string OwnerNodeId { get; }

        /// <summary>
        /// Gets the Section 13 Condition Method name, when declared.
        /// </summary>
        public string? ConditionAction { get; }

        /// <summary>
        /// Gets the same-document event affordance a Condition action addresses.
        /// </summary>
        public string? ActsOn { get; }

        /// <summary>
        /// Gets the pinned ConditionType identity, when the event projects a
        /// Condition. An empty string resolves through the converted EventType's
        /// supertype; null denotes a non-Condition event.
        /// </summary>
        public string? ConditionTypeId { get; }

        internal WotProjectedAffordance WithDefaultOwner(string nodeId)
        {
            return OwnerNodeId.Length != 0 ? this : new WotProjectedAffordance(
                Kind, Name, JsonPointer, NodeId, nodeId, ConditionAction, ActsOn, ConditionTypeId);
        }

        internal static ArrayOf<WotProjectedAffordance> Extract(JsonElement root)
        {
            var declarations = new List<WotProjectedAffordance>();
            string owner = ReadString(root, "uav:id") ?? string.Empty;
            foreach (WotAffordanceKind kind in s_kinds)
            {
                string collection = kind == WotAffordanceKind.Action ? "actions" : "events";
                if (!root.TryGetProperty(collection, out JsonElement affordances) ||
                    affordances.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                foreach (JsonProperty member in affordances.EnumerateObject())
                {
                    JsonElement affordance = member.Value;
                    if (affordance.ValueKind != JsonValueKind.Object ||
                        (kind == WotAffordanceKind.Event && affordance.TryGetProperty("@id", out _)))
                    {
                        continue;
                    }
                    declarations.Add(new WotProjectedAffordance(
                        kind,
                        member.Name,
                        "/" + collection + "/" + member.Name
                            .Replace("~", "~0", StringComparison.Ordinal)
                            .Replace("/", "~1", StringComparison.Ordinal),
                        ReadString(affordance, "uav:id") ?? string.Empty,
                        ReadString(affordance, "uav:componentOf") ?? owner,
                        ReadString(affordance, "uav:conditionAction"),
                        ReadString(affordance, "uav:actsOn"),
                        ReadString(affordance, "uav:conditionTypeId") ??
                            (ReadString(affordance, "uav:conditionType") is null ? null : string.Empty)));
                }
            }
            return declarations.ToArrayOf();
        }

        private static string? ReadString(JsonElement element, string name)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(name, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
        }

        private static readonly ArrayOf<WotAffordanceKind> s_kinds =
            [WotAffordanceKind.Action, WotAffordanceKind.Event];
    }
}
