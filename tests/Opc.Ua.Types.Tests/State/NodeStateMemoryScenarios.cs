/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Stable semantic inputs shared by allocation, live-heap and BDN measurements.
    /// </summary>
    internal sealed record NodeStateMemoryScenario(
        string Name,
        bool Variable,
        bool Initialized = true,
        bool Description = false,
        bool Unique = false,
        bool StringId = false,
        bool Metadata = false,
        bool Security = false,
        bool Reset = false,
        bool Payload = false,
        bool Telemetry = false,
        bool CreateLifecycle = false,
        bool Callbacks = false,
        int References = 0,
        int ReferenceMode = 0);

    internal static class NodeStateMemoryScenarios
    {
        internal static IEnumerable<NodeStateMemoryScenario> All => s_cases;

        internal static NodeStateMemoryScenario Select(string name)
        {
            return s_byName.TryGetValue(name, out NodeStateMemoryScenario? scenario)
                ? scenario
                : throw new InvalidOperationException("Unknown memory scenario: " + name);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static NodeState Construct(
            NodeStateMemoryScenario scenario,
            int index,
            ITelemetryContext? telemetry = null)
        {
            BaseInstanceState node = scenario.Telemetry
                ? new TelemetryVariable(telemetry ?? s_telemetry)
                : scenario.Variable ? new BaseDataVariableState(null) : new BaseObjectState(null);
            if (scenario.Initialized)
            {
                string text = scenario.Unique ? Text(index) : k_text;
                node.NodeId = scenario.StringId ? new NodeId(text, 2) : new NodeId((uint)(index + 1000), 2);
                node.BrowseName = new QualifiedName(text, 2);
                node.DisplayName = new LocalizedText(text);
                node.SymbolicName = text;
                node.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                node.TypeDefinitionId = scenario.Variable
                    ? VariableTypeIds.BaseDataVariableType : ObjectTypeIds.BaseObjectType;
                if (scenario.Description)
                {
                    node.Description = new LocalizedText(text);
                }
                if (node is BaseDataVariableState variable)
                {
                    variable.DataType = scenario.Payload ? DataTypeIds.ByteString : DataTypeIds.Double;
                    variable.ValueRank = ValueRanks.Scalar;
                    variable.AccessLevel = AccessLevels.CurrentRead;
                    variable.UserAccessLevel = AccessLevels.CurrentRead;
                    variable.Value = scenario.Payload
                        ? Variant.From(scenario.Unique ? ByteString.From(new byte[64]) : s_payload)
                        : Variant.From(42.0);
                }
            }
            if (scenario.CreateLifecycle)
            {
                node.Create(s_context, node.NodeId, node.BrowseName, node.DisplayName, false);
            }
            if (scenario.Metadata)
            {
                node.Extensions = s_extensions;
                node.Categories = s_categories;
                node.Specification = "urn:memory:spec";
                node.NodeSetDocumentation = "urn:memory:docs";
                node.ReleaseStatus = Export.ReleaseStatus.Draft;
                node.DesignToolOnly = true;
            }
            if (scenario.Security)
            {
                ArrayOf<RolePermissionType> permissions = scenario.Unique
                    ? ArrayOf.Wrapped(new RolePermissionType
                    {
                        RoleId = new NodeId(1u),
                        Permissions = (uint)PermissionType.Read
                    })
                    : s_permissions;
                node.RolePermissions = permissions;
                node.UserRolePermissions = permissions;
                node.AccessRestrictions = AccessRestrictionType.SigningRequired;
            }
            if (scenario.Reset)
            {
                node.Extensions = null;
                node.Categories = null;
                node.Specification = null;
                node.NodeSetDocumentation = null;
                node.ReleaseStatus = Export.ReleaseStatus.Released;
                node.DesignToolOnly = false;
                node.RolePermissions = default;
                node.UserRolePermissions = default;
                node.AccessRestrictions = null;
            }
            if (scenario.Callbacks)
            {
                node.OnStateChanged = s_changed;
                if (node is BaseDataVariableState variable)
                {
                    variable.OnReadValue = s_fullValue;
                    variable.OnWriteValue = s_fullValue;
                    variable.OnSimpleReadValue = s_simpleValue;
                    variable.OnSimpleWriteValue = s_simpleValue;
                }
            }
            for (int i = 0; i < scenario.References; i++)
            {
                ExpandedNodeId target = scenario.ReferenceMode switch
                {
                    0 => s_targets[i],
                    1 => new ExpandedNodeId(s_targets[i], "urn:memory:targets", 0),
                    2 => s_expandedTargets[i],
                    _ => throw new ArgumentOutOfRangeException(nameof(scenario))
                };
                node.AddReference(ReferenceTypeIds.HasComponent, false, target);
            }
            return node;
        }

        internal static string Text(int index)
        {
            return index.ToString("X16", CultureInfo.InvariantCulture);
        }

        private static NodeStateMemoryScenario[] CreateCases()
        {
            var cases = new List<NodeStateMemoryScenario>();
            foreach (bool variable in new[] { false, true })
            {
                string kind = variable ? "Variable" : "Object";
                cases.Add(new($"{kind}.Bare", variable, Initialized: false));
                cases.Add(new($"{kind}.Minimal", variable));
                cases.Add(new($"{kind}.CreateLifecycle", variable, CreateLifecycle: true));
                if (variable)
                {
                    cases.Add(new($"{kind}.Telemetry", true, Telemetry: true));
                }
                cases.Add(new($"{kind}.Synthetic.Callbacks", variable, Callbacks: true));
                foreach (bool unique in new[] { false, true })
                {
                    string sharing = unique ? "Unique" : "Cached";
                    cases.Add(new($"{kind}.Numeric.{sharing}", variable, Description: true, Unique: unique));
                    cases.Add(new($"{kind}.String.{sharing}", variable,
                        Description: true, Unique: unique, StringId: true));
                    foreach (string bag in new[] { "Neither", "Metadata", "Security", "Both" })
                    {
                        cases.Add(new($"{kind}.{bag}.{sharing}", variable, Unique: unique,
                            Metadata: bag is "Metadata" or "Both", Security: bag is "Security" or "Both"));
                    }
                    if (variable)
                    {
                        cases.Add(new($"{kind}.Payload.{sharing}", true, Unique: unique, Payload: true));
                    }
                }
                cases.Add(new($"{kind}.Metadata.Reset", variable, Metadata: true, Reset: true));
                cases.Add(new($"{kind}.Security.Reset", variable, Security: true, Reset: true));
                cases.Add(new($"{kind}.Both.Reset", variable, Metadata: true, Security: true, Reset: true));
            }
            foreach (int count in new[] { 0, 1, 2, 4, 8, 16, 128, 1024 })
            {
                for (int mode = 0; mode < 3; mode++)
                {
                    cases.Add(new($"Object.References.{mode}.{count}", false, References: count, ReferenceMode: mode));
                }
            }
            return cases.ToArray();
        }

        private const string k_text = "0123456789ABCDEF";
        private static readonly NodeStateChangedHandler s_changed = static (_, _, _) => { };
        private static readonly NodeValueSimpleEventHandler s_simpleValue =
            static (ISystemContext _, NodeState _, ref Variant _) => ServiceResult.Good;
        private static readonly NodeValueEventHandler s_fullValue =
            static (ISystemContext _, NodeState _, NumericRange _, QualifiedName _,
                ref Variant _, ref StatusCode _, ref DateTimeUtc _) => ServiceResult.Good;
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.CreateForBenchmarks();
        private static readonly SystemContext s_context = new(s_telemetry);
        private static readonly ByteString s_payload = ByteString.From(new byte[64]);
        private static readonly XmlElement[] s_extensions =
            [XmlElement.From(new System.Xml.XmlDocument().CreateElement("ext"))];
        private static readonly string[] s_categories = ["Category"];
        private static readonly ArrayOf<RolePermissionType> s_permissions =
            ArrayOf.Wrapped(new RolePermissionType
            {
                RoleId = new NodeId(1u),
                Permissions = (uint)PermissionType.Read
            });
        private static readonly NodeId[] s_targets =
            Enumerable.Range(0, 1024).Select(i => new NodeId((uint)(i + 50000), 2)).ToArray();
        private static readonly ExpandedNodeId[] s_expandedTargets =
            s_targets.Select(id => (ExpandedNodeId)id).ToArray();
        private static readonly NodeStateMemoryScenario[] s_cases = CreateCases();
        private static readonly Dictionary<string, NodeStateMemoryScenario> s_byName =
            s_cases.ToDictionary(c => c.Name, StringComparer.Ordinal);

        private sealed class TelemetryVariable : BaseDataVariableState
        {
            public TelemetryVariable(ITelemetryContext telemetry)
                : base(null)
            {
                Initialize(telemetry);
            }
        }
    }
}
