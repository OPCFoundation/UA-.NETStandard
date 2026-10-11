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
    /// Maps configured runtimes and published assets to the names in OPC 40100-1
    /// tables 161-172 and OPC 40100-2 table 74, without manufacturing profile URIs.
    /// </summary>
    internal static class MachineVisionConformance
    {
        /// <summary>
        /// Returns the units whose protocol handlers the control runtime binds.
        /// </summary>
        public static ArrayOf<QualifiedName> Control(ISystemContext context, MachineVisionFeatures features)
        {
            ushort ns = (ushort)context.NamespaceUris.GetIndex(MachineVisionModels.ControlNamespaceUri);
            var units = new List<QualifiedName>();
            Add("Basic Vision System");
            Add("Automatic Mode");
            Add("Easy Confirmation");
            Add("Error Events");
            Add("Error Conditions");
            Add("Information Events");
            Add("Measurement ID");
            Add("Part ID");
            Add("Simulation", Has(MachineVisionFeatures.Simulation));
            Add("Safety State Management", Has(MachineVisionFeatures.Safety));
            Add("System State Information", Has(MachineVisionFeatures.Diagnostics));
            Add("Diagnostic Events", Has(MachineVisionFeatures.Diagnostics));
            if (Has(MachineVisionFeatures.Steps))
            {
                foreach (string state in s_stepStates)
                {
                    Add("StepModel " + state);
                }
            }
            if (Has(MachineVisionFeatures.Recipes))
            {
                Add("Basic Recipe Management");
                Add("Recipe ID External");
                Add("Product ID");
                Add("Recipe Versioning");
                Add("Recipe File", Has(MachineVisionFeatures.FileTransfer));
                Add("Recipe Folder", Has(MachineVisionFeatures.Folders));
                Add("Product Folder", Has(MachineVisionFeatures.Folders));
            }
            if (Has(MachineVisionFeatures.Configurations))
            {
                Add("Basic Configuration Management");
                Add("Configuration ID External");
                Add("Configuration Versioning");
                Add("Configuration File", Has(MachineVisionFeatures.FileTransfer));
                Add("Configuration Folder", Has(MachineVisionFeatures.Folders));
            }
            if (Has(MachineVisionFeatures.Results))
            {
                Add("Basic Result Management");
                Add("Event Based Content");
                Add("Method Based Content");
                Add("Result File", Has(MachineVisionFeatures.FileTransfer));
                Add("Result Folder", Has(MachineVisionFeatures.Folders));
            }
            return units.ToArrayOf();

            void Add(string name, bool present = true)
            {
                if (present)
                {
                    units.Add(new QualifiedName(name, ns));
                }
            }

            bool Has(MachineVisionFeatures feature) => (features & feature) != 0;
        }

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
                    folder.GetReferences(context.Context, links, global::Opc.Ua.ReferenceTypeIds.Organizes, false);
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
                        child.ReferenceTypeId, global::Opc.Ua.ReferenceTypeIds.HasAddIn))
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
                _ when kind >= MachineVisionItemKind.ComputingDevice && kind <= MachineVisionItemKind.Cable =>
                    kind + "s",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }

        private static readonly string[] s_stepStates =
        [
            "Preoperational", "Halted", "Error", "Initialized", "Ready", "SingleExecution", "ContinuousExecution"
        ];
    }
}
