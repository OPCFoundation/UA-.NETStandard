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
using Opc.Ua.AMB.Server.Assets;
using ConformanceUnitNames = Opc.Ua.AMB.Server.ConformanceUnits;

namespace Opc.Ua.AMB.Server.Structure
{
    /// <summary>
    /// Evaluates the conformance units of OPC 10000-110 §10, §11, §13 and §14
    /// by looking at the address space of the registered assets, whoever
    /// added what they publish.
    /// </summary>
    internal static class AssetInspection
    {
        /// <summary>
        /// Adds the units the registered assets meet.
        /// </summary>
        /// <param name="assets">The registered assets; at least one.</param>
        /// <param name="typeTree">The type tree of the server.</param>
        /// <param name="isDictionaryEntry">Whether a node is a dictionary entry the AMB manager defined.</param>
        /// <param name="units">Receives the units.</param>
        public static void EvaluateStructure(
            ArrayOf<AssetHandle> assets,
            ITypeTable typeTree,
            Func<NodeId, bool> isDictionaryEntry,
            List<QualifiedName> units)
        {
            var registered = new Dictionary<NodeId, AssetHandle>();
            foreach (AssetHandle asset in assets)
            {
                registered[asset.NodeId] = asset;
            }

            bool versionInformation = true;
            bool operationCounters = false;
            bool requirements = false;
            bool capabilities = false;
            bool classification = false;
            bool localTime = false;
            bool hierarchicalProperty = false;
            bool operationalProperty = false;
            bool digitalLocation = false;
            bool hierarchicalObjects = false;
            bool operationalObjects = false;
            bool subAssets = false;
            bool relations = false;
            foreach (AssetHandle asset in assets)
            {
                ISystemContext context = asset.Context;
                ushort amb = (ushort)context.NamespaceUris.GetIndex(Namespaces.AMB);
                versionInformation &= HasVersionInformation(asset);
                operationCounters |= HasOperationCounter(asset);
                requirements |= HasEntries(asset, new QualifiedName(AmbBrowseNames.Requirements, amb));
                capabilities |= HasEntries(asset, new QualifiedName(AmbBrowseNames.Capabilities, amb));
                classification |= IsClassified(asset, isDictionaryEntry);
                localTime |= HasChild(asset, new QualifiedName(Ua.BrowseNames.LocalTime));
                hierarchicalProperty |= HasChild(asset, new QualifiedName(AmbBrowseNames.HierarchicalLocation, amb));
                operationalProperty |= HasChild(asset, new QualifiedName(AmbBrowseNames.OperationalLocation, amb));
                digitalLocation |= HasChild(asset, new QualifiedName(AmbBrowseNames.DigitalLocation, amb));
                hierarchicalObjects |= IsLocated(asset, AssetLocationKind.Hierarchical);
                operationalObjects |= IsLocated(asset, AssetLocationKind.Operational);
                subAssets |= HasSubAsset(asset, registered, typeTree);
                relations |= HasRelation(asset, registered, typeTree);
            }

            Add(units, versionInformation, ConformanceUnitNames.VersionInformation);
            Add(units, operationCounters, ConformanceUnitNames.OperationCounters);
            Add(units, requirements, ConformanceUnitNames.Requirements);
            Add(units, capabilities, ConformanceUnitNames.Capabilities);
            Add(units, classification, ConformanceUnitNames.Classification);
            Add(units, localTime, ConformanceUnitNames.LocalTime);
            Add(units, hierarchicalProperty, ConformanceUnitNames.HierarchicalLocationProperty);
            Add(units, hierarchicalObjects, ConformanceUnitNames.HierarchicalLocationObjects);
            Add(units, operationalProperty, ConformanceUnitNames.OperationalLocationProperty);
            Add(units, operationalObjects, ConformanceUnitNames.OperationalLocationObjects);
            Add(units, digitalLocation, ConformanceUnitNames.DigitalLocation);
            Add(units, subAssets, ConformanceUnitNames.SubAssets);
            Add(units, relations, ConformanceUnitNames.AssetRelations);
        }

        /// <summary>
        /// The hardware and/or software revision and the revision counter
        /// (§10.2), on the asset or in its identification group, with
        /// values: the defaults of OPC 10000-100 - an empty revision and a
        /// RevisionCounter of -1 - say that the asset does not provide them.
        /// </summary>
        internal static bool HasVersionInformation(AssetHandle asset)
        {
            return (HasRevision(asset, Opc.Ua.Di.BrowseNames.HardwareRevision) ||
                HasRevision(asset, Opc.Ua.Di.BrowseNames.SoftwareRevision)) &&
                AssetIdentification.FindProperty(
                    asset.Context,
                    asset.Asset,
                    asset.DiNamespaceIndex,
                    Opc.Ua.Di.BrowseNames.RevisionCounter) is BaseVariableState counter &&
                counter.WrappedValue.TryGetValue(out int revisions) &&
                revisions >= 0;
        }

        private static bool HasRevision(AssetHandle asset, string name)
        {
            return !string.IsNullOrEmpty(AssetIdentification.ReadString(AssetIdentification.FindProperty(
                asset.Context,
                asset.Asset,
                asset.DiNamespaceIndex,
                name)));
        }

        /// <summary>
        /// A <c>2:OperationCounters</c> group with at least one counter,
        /// possibly in nested groups (§10.3).
        /// </summary>
        internal static bool HasOperationCounter(AssetHandle asset)
        {
            var name = new QualifiedName(Opc.Ua.Di.BrowseNames.OperationCounters, asset.DiNamespaceIndex);
            return asset.Asset.FindChildWithQualifiedName(asset.Context, name) is BaseObjectState counters &&
                HasVariable(asset.Context, counters);
        }

        private static bool HasVariable(ISystemContext context, NodeState node)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is BaseVariableState || (child is BaseObjectState && HasVariable(context, child)))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasEntries(AssetHandle asset, QualifiedName folderName)
        {
            if (asset.Asset.FindChildWithQualifiedName(asset.Context, folderName) is not BaseObjectState folder)
            {
                return false;
            }
            var children = new List<BaseInstanceState>();
            folder.GetChildren(asset.Context, children);
            return children.Count > 0;
        }

        private static bool HasChild(AssetHandle asset, QualifiedName name)
        {
            return asset.Asset.FindChildWithQualifiedName(asset.Context, name) != null;
        }

        /// <summary>
        /// A <c>HasDictionaryEntry</c> reference to a dictionary entry object
        /// (§11, OPC 10000-19): one the AMB manager defined, or one that was a
        /// <c>DictionaryEntryType</c> object when the asset was registered.
        /// </summary>
        private static bool IsClassified(AssetHandle asset, Func<NodeId, bool> isDictionaryEntry)
        {
            var references = new List<IReference>();
            asset.Asset.GetReferences(asset.Context, references, Ua.ReferenceTypeIds.HasDictionaryEntry, false);
            ArrayOf<NodeId> checkedEntries = asset.Structure?.CheckedClassifications ?? default;
            foreach (IReference reference in references)
            {
                if (reference.TargetId.IsAbsolute)
                {
                    continue;
                }
                var target = (NodeId)reference.TargetId;
                if (isDictionaryEntry(target) || Contains(checkedEntries, target))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Contains(ArrayOf<NodeId> nodes, NodeId nodeId)
        {
            foreach (NodeId node in nodes)
            {
                if (node == nodeId)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsLocated(AssetHandle asset, AssetLocationKind kind)
        {
            if (asset.Structure == null)
            {
                return false;
            }
            foreach ((AssetLocationKind Kind, NodeId Location) location in asset.Structure.Locations)
            {
                if (location.Kind == kind)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Another registered asset below this one: in its parent chain, or
        /// the target of a hierarchical reference from it (§14.1).
        /// </summary>
        private static bool HasSubAsset(
            AssetHandle asset,
            Dictionary<NodeId, AssetHandle> registered,
            ITypeTable typeTree)
        {
            var references = new List<IReference>();
            asset.Asset.GetReferences(asset.Context, references);
            foreach (IReference reference in references)
            {
                if (!reference.IsInverse &&
                    !reference.TargetId.IsAbsolute &&
                    registered.ContainsKey((NodeId)reference.TargetId) &&
                    typeTree.IsTypeOf(reference.ReferenceTypeId, Ua.ReferenceTypeIds.HierarchicalReferences))
                {
                    return true;
                }
            }
            // A sub-asset created as a child of the asset, directly or
            // through grouping objects, has it in its parent chain.
            foreach (AssetHandle other in registered.Values)
            {
                if (!ReferenceEquals(other, asset) && IsAncestor(asset.Asset, other.Asset))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsAncestor(NodeState ancestor, BaseInstanceState node)
        {
            for (NodeState? parent = node.Parent; parent != null; parent = (parent as BaseInstanceState)?.Parent)
            {
                if (ReferenceEquals(parent, ancestor))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A non-hierarchical reference, in either direction, to another
        /// registered asset (§14.2).
        /// </summary>
        private static bool HasRelation(
            AssetHandle asset,
            Dictionary<NodeId, AssetHandle> registered,
            ITypeTable typeTree)
        {
            var references = new List<IReference>();
            asset.Asset.GetReferences(asset.Context, references);
            foreach (IReference reference in references)
            {
                if (!reference.TargetId.IsAbsolute &&
                    registered.ContainsKey((NodeId)reference.TargetId) &&
                    (NodeId)reference.TargetId != asset.NodeId &&
                    typeTree.IsTypeOf(reference.ReferenceTypeId, Ua.ReferenceTypeIds.NonHierarchicalReferences))
                {
                    return true;
                }
            }
            return false;
        }

        private static void Add(List<QualifiedName> units, bool met, string unit)
        {
            if (met)
            {
                units.Add(new QualifiedName(unit));
            }
        }
    }
}
