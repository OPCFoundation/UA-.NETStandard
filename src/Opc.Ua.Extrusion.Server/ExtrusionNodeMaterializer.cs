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
using System.Linq;
using Opc.Ua.Di.Server;
using Opc.Ua.Server;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// Materializes compiled declarations without reflection or a duplicate information model.
    /// </summary>
    internal sealed class ExtrusionNodeMaterializer
    {
        /// <summary>
        /// Creates a bounded, instance-local materializer.
        /// </summary>
        internal ExtrusionNodeMaterializer(
            DiNodeManager manager,
            ExtrusionFeatures features,
            ushort instanceNamespace,
            int maximumNodes)
        {
            m_manager = manager;
            m_context = manager.SystemContext;
            m_features = features;
            m_instanceNamespace = instanceNamespace;
            m_maximumNodes = maximumNodes;
        }

        /// <summary>
        /// Creates optional children, replaces placeholders with real instances and assigns stable identities.
        /// </summary>
        internal void Materialize(ExtrusionDeviceState root, string identity)
        {
            NodeState type = m_manager.FindPredefinedNode(root.TypeDefinitionId)
                ?? throw Error("The concrete extrusion type is not loaded.");
            m_line = root is Line.ExtrusionLine_InterfaceState;
            Dictionary<string, Declaration> inventory = Inventory([type], string.Empty, 0, []);
            m_features.ValidateSelections(inventory.Keys.ToHashSet(StringComparer.Ordinal),
                inventory.Where(pair => pair.Value.Mandatory).Select(pair => pair.Key)
                    .ToHashSet(StringComparer.Ordinal));
            Build(root, [type], string.Empty, 0);
            AssignStableIds(root, "Extrusion/" + Uri.EscapeDataString(identity));
        }

        /// <summary>
        /// Applies the same selection to a newly added job or other dynamic instance.
        /// </summary>
        internal void MaterializeChild(BaseInstanceState node, string declarationPath, string stableId)
        {
            NodeState type = m_manager.FindPredefinedNode(node.TypeDefinitionId)
                ?? throw Error("The child type is not loaded.");
            Build(node, [type], declarationPath, 0);
            AssignStableIds(node, stableId);
        }

        /// <summary>
        /// Assigns identities using namespace URIs, never the current namespace-table ordering.
        /// </summary>
        internal void AssignStableIds(NodeState root, string stableId)
        {
            var replacements = new Dictionary<NodeId, NodeId>();
            Assign(root, stableId);
            root.UpdateReferenceTargets(m_context, replacements);

            void Assign(NodeState node, string identifier)
            {
                NodeId previous = node.NodeId;
                node.NodeId = new NodeId(identifier, m_instanceNamespace);
                if (!previous.IsNull && previous != node.NodeId)
                {
                    replacements[previous] = node.NodeId;
                }
                foreach (BaseInstanceState child in Children(node))
                {
                    string uri = m_context.NamespaceUris.GetString(child.BrowseName.NamespaceIndex)
                        ?? throw Error($"No namespace URI is registered for '{child.BrowseName}'.");
                    Assign(child, identifier + "/" + Uri.EscapeDataString(uri) + ":" +
                        Uri.EscapeDataString(child.BrowseName.Name!));
                }
            }
        }

        /// <summary>
        /// Inventories all declarations, even beneath an unselected optional container.
        /// </summary>
        private Dictionary<string, Declaration> Inventory(
            IList<NodeState> scopes,
            string parentPath,
            int depth,
            HashSet<NodeId> ancestors)
        {
            CheckDepth(depth, parentPath);
            var result = new Dictionary<string, Declaration>(StringComparer.Ordinal);
            foreach (KeyValuePair<QualifiedName, List<NodeState>> pair in Declarations(scopes))
            {
                var definition = (BaseInstanceState)pair.Value[pair.Value.Count - 1];
                string path = Join(parentPath, definition.BrowseName.Name!);
                bool mandatory = IsMandatory(definition.ModellingRuleId);
                result[path] = new Declaration(mandatory);
                bool recursive = !ancestors.Add(definition.NodeId);
                if (recursive && !m_features.HasDescendantSelections(path))
                {
                    continue;
                }
                try
                {
                    foreach (KeyValuePair<string, Declaration> descendant in
                        Inventory(pair.Value, path, depth + 1, ancestors))
                    {
                        result[descendant.Key] = descendant.Value;
                    }
                }
                finally
                {
                    if (!recursive)
                    {
                        ancestors.Remove(definition.NodeId);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Builds the exact selected graph from generated child factories and compiled type declarations.
        /// </summary>
        private void Build(NodeState parent, IList<NodeState> scopes, string parentPath, int depth)
        {
            CheckDepth(depth, parentPath);
            if (++m_materialized > m_maximumNodes)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded, "The extrusion device node limit was exceeded.");
            }
            foreach (KeyValuePair<QualifiedName, List<NodeState>> pair in Declarations(scopes))
            {
                var definition = (BaseInstanceState)pair.Value[pair.Value.Count - 1];
                string name = definition.BrowseName.Name!;
                string path = Join(parentPath, name);
                bool mandatory = IsMandatory(definition.ModellingRuleId);
                bool placeholder = IsPlaceholder(definition.ModellingRuleId);
                BaseInstanceState? existing = parent.FindChildWithQualifiedName(m_context, pair.Key);

                // OPC 40084-2 §6.2 explicitly prohibits these inherited component-only properties.
                bool prohibited = m_line && parentPath.Length == 0 && name is "Strand" or "Target";
                if (prohibited || (!mandatory && !m_features.Includes(path)))
                {
                    if (existing != null)
                    {
                        parent.RemoveChild(existing);
                    }
                    continue;
                }

                if (placeholder)
                {
                    if (existing != null)
                    {
                        parent.RemoveChild(existing);
                    }
                    int defaultCount = name == "CuttingProduct_<Nr>" ? 5 : 1;
                    int count = m_features.Count(path, mandatory, defaultCount);
                    if (count > 0 && name is "JobGroup_<Nr>" or "Job_<Nr>")
                    {
                        throw new ArgumentException(
                            $"Create '{path}' through the typed job controller with complete committed metadata.");
                    }
                    if (mandatory && count == 0)
                    {
                        throw new ArgumentException($"Mandatory placeholder '{path}' needs at least one instance.");
                    }
                    for (int index = 1; index <= count; index++)
                    {
                        string instanceName = PlaceholderName(parent, definition, index);
                        var browseName = new QualifiedName(instanceName, pair.Key.NamespaceIndex);
                        BaseInstanceState child = CreateFromDeclaration(parent, definition, browseName);
                        parent.AddChild(child);
                        Build(child, pair.Value, path, depth + 1);
                    }
                }
                else
                {
                    BaseInstanceState? child = existing;
                    if (child == null)
                    {
                        BaseInstanceState? generated = parent.CreateChild(m_context, pair.Key);
                        // Generated slots may match only the name. Reuse a slot only
                        // for its namespace, and apply the occurrence's full metadata.
                        child = CreateFromDeclaration(parent, definition, pair.Key,
                            generated?.BrowseName == pair.Key ? generated : null);
                    }
                    if (parent.FindChildWithQualifiedName(m_context, pair.Key) == null)
                    {
                        parent.AddChild(child);
                    }
                    Build(child, pair.Value, path, depth + 1);
                }
            }
            if (parent is BaseInstanceState instance)
            {
                instance.ModellingRuleId = NodeId.Null;
            }
        }

        /// <summary>
        /// Merges inherited and occurrence-specific declarations, preserving namespace-qualified names.
        /// </summary>
        private Dictionary<QualifiedName, List<NodeState>> Declarations(IList<NodeState> scopes)
        {
            var result = new Dictionary<QualifiedName, List<NodeState>>();
            var visited = new HashSet<NodeId>();
            foreach (NodeState scope in scopes)
            {
                AddScope(scope);
            }
            return result;

            void AddScope(NodeState scope)
            {
                NodeId baseId = scope switch
                {
                    BaseObjectTypeState objectType => objectType.SuperTypeId,
                    BaseVariableTypeState variableType => variableType.SuperTypeId,
                    BaseInstanceState instance => instance.TypeDefinitionId,
                    _ => NodeId.Null
                };
                if (!baseId.IsNull && visited.Add(baseId) &&
                    m_manager.FindPredefinedNode(baseId) is NodeState baseType)
                {
                    AddScope(baseType);
                }
                foreach (BaseInstanceState child in Children(scope))
                {
                    if (!IsModelled(child.ModellingRuleId))
                    {
                        continue;
                    }
                    if (!result.TryGetValue(child.BrowseName, out List<NodeState>? definitions))
                    {
                        definitions = [];
                        result.Add(child.BrowseName, definitions);
                    }
                    if (!definitions.Contains(child))
                    {
                        definitions.Add(child);
                    }
                }
            }
        }

        /// <summary>
        /// Uses registered generated activators to preserve the concrete CLR and UA type.
        /// </summary>
        private BaseInstanceState CreateFromDeclaration(
            NodeState parent,
            BaseInstanceState definition,
            QualifiedName browseName,
            BaseInstanceState? generated = null)
        {
            if (m_manager.FindPredefinedNode(definition.TypeDefinitionId) is BaseObjectTypeState { IsAbstract: true })
            {
                throw Error($"Placeholder '{browseName}' requires an application-selected concrete subtype.");
            }
            BaseInstanceState child = generated ?? m_context.NodeStateFactory.CreateInstance(
                m_context, parent, definition.NodeClass, browseName,
                definition.ReferenceTypeId, definition.TypeDefinitionId) as BaseInstanceState
                ?? throw Error($"No generated instance factory exists for '{browseName}'.");
            child.Create(m_context, definition);
            child.BrowseName = browseName;
            child.SymbolicName = browseName.Name
                ?? throw Error("An extrusion instance must have a non-null BrowseName.");
            child.DisplayName = new LocalizedText(browseName.Name);
            child.ModellingRuleId = NodeId.Null;
            NodeId previous = m_context.AssignInstanceNodeId(child);
            m_context.AssignInstanceChildNodeIds(child, previous);
            return child;
        }

        /// <summary>
        /// Uses the part-specific names required when the common MeasuringDevices placeholder is refined.
        /// </summary>
        private string PlaceholderName(NodeState parent, BaseInstanceState definition, int index)
        {
            string template = definition.BrowseName.Name
                ?? throw Error("An extrusion placeholder must have a non-null BrowseName.");
            string suffix = index.ToString("D3", CultureInfo.InvariantCulture);
            string stem = parent.BrowseName.Name switch
            {
                "MeltPressures" => "MeltPressure",
                "MeltTemperatureZones" => "MeltTemperatureZone",
                "MeltPressureZones" => "MeltPressureZone",
                "PressureZones" => "PressureZone",
                "VacuumZones" => "VacuumZone",
                "ScrewTemperatures" => "ScrewTemperature",
                "DimensionAdjustment" => "DimensionAdjustment",
                "PositionsX" => "PositionX",
                "PositionsY" => "PositionY",
                "PositionsPipeSupport" => "PositionPipeSupport",
                "VacuumPumps" => "VacuumPump",
                "WaterPumps" => "WaterPump",
                _ => string.Empty
            };
            if (stem.Length == 0 && template == "<Name>_<Nr>")
            {
                string typeName = m_manager.FindPredefinedNode(definition.TypeDefinitionId)?.BrowseName.Name
                    ?? throw Error($"The type of placeholder '{template}' is not loaded.");
                stem = typeName.EndsWith("Type", StringComparison.Ordinal) ? typeName[..^4] : typeName;
            }
            string name = stem.Length != 0
                ? stem + "_" + suffix
                : template.Replace("<Nr>", suffix, StringComparison.Ordinal);
            if (name.Contains('<', StringComparison.Ordinal) || name.Contains('>', StringComparison.Ordinal))
            {
                throw Error($"Placeholder '{template}' has no concrete instance naming convention.");
            }
            return name;
        }

        /// <summary>
        /// Reads explicit and generated children without accessing CLR members by reflection.
        /// </summary>
        private List<BaseInstanceState> Children(NodeState node)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(m_context, children);
            return children;
        }

        /// <summary>
        /// Recognizes all instantiable modelling rules.
        /// </summary>
        private static bool IsModelled(NodeId rule)
        {
            return IsMandatory(rule) || rule == Opc.Ua.ObjectIds.ModellingRule_Optional ||
                rule == Opc.Ua.ObjectIds.ModellingRule_OptionalPlaceholder;
        }

        /// <summary>
        /// Recognizes mandatory members and mandatory placeholders.
        /// </summary>
        private static bool IsMandatory(NodeId rule)
        {
            return rule == Opc.Ua.ObjectIds.ModellingRule_Mandatory ||
                rule == Opc.Ua.ObjectIds.ModellingRule_MandatoryPlaceholder;
        }

        /// <summary>
        /// Recognizes repeated declarations rather than literal instance BrowseNames.
        /// </summary>
        private static bool IsPlaceholder(NodeId rule)
        {
            return rule == Opc.Ua.ObjectIds.ModellingRule_OptionalPlaceholder ||
                rule == Opc.Ua.ObjectIds.ModellingRule_MandatoryPlaceholder;
        }

        /// <summary>
        /// Appends one complete BrowseName to a relative selection path.
        /// </summary>
        private static string Join(string parent, string name)
        {
            return parent.Length == 0 ? name : parent + "/" + name;
        }

        /// <summary>
        /// Bounds recursive model traversal independently of the instance cardinalities.
        /// </summary>
        private static void CheckDepth(int depth, string path)
        {
            if (depth > 48)
            {
                throw Error($"Recursive extrusion declarations at '{path}' exceed the supported model depth.");
            }
        }

        /// <summary>
        /// Reports a configuration failure rather than silently materializing a different type.
        /// </summary>
        private static ServiceResultException Error(string message)
        {
            return ServiceResultException.Create(StatusCodes.BadConfigurationError, message);
        }

        /// <summary>Selection validation facts for one declaration.</summary>
        private sealed record Declaration(bool Mandatory);

        /// <summary>The single owner of the compiled type model.</summary>
        private readonly DiNodeManager m_manager;

        /// <summary>The owner's factory and namespace context.</summary>
        private readonly ServerSystemContext m_context;

        /// <summary>The frozen caller selections.</summary>
        private readonly ExtrusionFeatures m_features;

        /// <summary>The application-owned instance namespace.</summary>
        private readonly ushort m_instanceNamespace;

        /// <summary>The per-device allocation limit.</summary>
        private readonly int m_maximumNodes;

        /// <summary>The materialized node count.</summary>
        private int m_materialized;

        /// <summary>Whether component-only inherited properties must be omitted.</summary>
        private bool m_line;
    }
}
