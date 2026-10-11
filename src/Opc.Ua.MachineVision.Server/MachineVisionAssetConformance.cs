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
using Opc.Ua.Vision.MachineVision.AMCM;
using Amcm = Opc.Ua.Vision.MachineVision.AMCM;

namespace Opc.Ua.Vision.Server
{
    /// <summary>
    /// Reports OPC 40100-2 conformance units from actual asset nodes and relationships.
    /// </summary>
    internal static class MachineVisionAssetConformance
    {
        /// <summary>
        /// Returns units backed by live asset instances and their reference graph.
        /// </summary>
        public static ArrayOf<QualifiedName> Asset(IVisionBuildContext context, VisionSystemAssetState asset)
        {
            if (!ReferenceEquals(context.Manager.FindPredefinedNode<NodeState>(asset.NodeId), asset))
            {
                return [];
            }
            ushort ns = (ushort)context.Context.NamespaceUris.GetIndex(MachineVisionModels.AssetNamespaceUri);
            var names = new HashSet<string>(StringComparer.Ordinal) { "VSA Basic" };
            AddInformation(asset, "VSA", Amcm.ObjectTypeIds.VisionMachineIdentificationType);
            var members = new List<BaseInstanceState>();
            asset.GetChildren(context.Context, members);
            var pathNodes = new HashSet<NodeState> { asset };
            for (MachineVisionItemKind kind = MachineVisionItemKind.ComputingDevice;
                kind <= MachineVisionItemKind.Cable; kind++)
            {
                string category = CategoryName(kind);
                foreach (BaseInstanceState member in members)
                {
                    if (member is not VisionItemFolderState folder || member.BrowseName.Name != category)
                    {
                        continue;
                    }
                    var children = new List<BaseInstanceState>();
                    folder.GetChildren(context.Context, children);
                    var items = new HashSet<BaseObjectState>();
                    foreach (BaseInstanceState child in children)
                    {
                        if (child is BaseObjectState item)
                        {
                            items.Add(item);
                        }
                    }
                    var links = new List<IReference>();
                    folder.GetReferences(context.Context, links, ReferenceTypeIds.Organizes, false);
                    foreach (IReference link in links)
                    {
                        var id = ExpandedNodeId.ToNodeId(link.TargetId, context.Context.NamespaceUris);
                        if (context.Manager.FindPredefinedNode<BaseObjectState>(id) is { } item)
                        {
                            items.Add(item);
                        }
                    }
                    foreach (BaseObjectState item in items)
                    {
                        pathNodes.Add(item);
                        // Table 74 defines no cable-specific units, although cables are a model category.
                        if (kind != MachineVisionItemKind.Cable)
                        {
                            names.Add("VSA Find " + category);
                            string label = kind == MachineVisionItemKind.SoftwareComponent
                                ? "SoftwareComponents" : kind.ToString();
                            AddInformation(item, "VSA " + label, Amcm.ObjectTypeIds.VisionComponentIdentificationType);
                        }
                    }
                }
            }
            foreach (NodeState node in pathNodes)
            {
                AddPath(node, Amcm.ReferenceTypeIds.HasOpticalPathTo, "VSA Optical Paths");
                AddPath(node, Amcm.ReferenceTypeIds.TransmitsDataTo, "VSA Data Paths");
            }
            var result = new List<QualifiedName>();
            foreach (string name in names)
            {
                result.Add(new QualifiedName(name, ns));
            }
            return result.ToArrayOf();

            void AddInformation(NodeState node, string prefix, ExpandedNodeId identification)
            {
                var children = new List<BaseInstanceState>();
                node.GetChildren(context.Context, children);
                foreach (BaseInstanceState child in children)
                {
                    if (!context.Context.TypeTable.IsTypeOf(
                        child.ReferenceTypeId, ReferenceTypeIds.HasAddIn))
                    {
                        continue;
                    }
                    if (IsType(child, identification))
                    {
                        names.Add(prefix + " Identification");
                    }
                    if (IsType(child, Amcm.ObjectTypeIds.VisionMaintenanceInfoType))
                    {
                        names.Add(prefix + " Maintenance Information");
                    }
                    if (IsType(child, Amcm.ObjectTypeIds.VisionHealthInfoType))
                    {
                        names.Add(prefix + " Health Information");
                    }
                }
            }

            bool IsType(BaseInstanceState instance, ExpandedNodeId type)
            {
                return context.Context.TypeTable.IsTypeOf(
                    instance.TypeDefinitionId, ExpandedNodeId.ToNodeId(type, context.Context.NamespaceUris));
            }

            void AddPath(NodeState node, ExpandedNodeId type, string name)
            {
                var links = new List<IReference>();
                node.GetReferences(
                    context.Context, links, ExpandedNodeId.ToNodeId(type, context.Context.NamespaceUris), false);
                if (links.Count > 0)
                {
                    names.Add(name);
                }
            }
        }

        /// <summary>
        /// Resolves the exact category BrowseName shared by model construction and conformance reporting.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static string CategoryName(MachineVisionItemKind kind)
        {
            return kind switch
            {
                MachineVisionItemKind.SurroundingEnvironment => "SurroundingEnvironment",
                MachineVisionItemKind.Lens => "Lenses",
                MachineVisionItemKind.PowerSupply => "PowerSupplies",
                _ when kind is >= MachineVisionItemKind.ComputingDevice and <= MachineVisionItemKind.Cable =>
                    kind + "s",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }
    }
}
