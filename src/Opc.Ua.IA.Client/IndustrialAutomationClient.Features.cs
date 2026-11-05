/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.IA.Client
{
    /// <summary>
    /// The published category of a calibration target.
    /// </summary>
    public enum IaTargetCategory
    {
        /// <summary>
        /// A category not defined by the published IA model.
        /// </summary>
        Unknown,

        /// <summary>
        /// A reusable target.
        /// </summary>
        Reusable,

        /// <summary>
        /// A reusable device target.
        /// </summary>
        ReusableDevice,

        /// <summary>
        /// A target consumed by its use.
        /// </summary>
        OneTime,

        /// <summary>
        /// A target generated at runtime.
        /// </summary>
        Dynamic
    }

    /// <summary>
    /// A target feature with its native value and metadata, including custom feature objects.
    /// </summary>
    /// <param name="NodeId">The feature identity in the current session.</param>
    /// <param name="BrowseName">The feature's actual qualified name.</param>
    /// <param name="DisplayName">The description to display, including for custom features.</param>
    /// <param name="TypeDefinition">The actual type, including vendor subtypes.</param>
    /// <param name="Value">The native value, or DataValue.Null for an object feature.</param>
    /// <param name="EngineeringUnits">The units and quality, or DataValue.Null when absent.</param>
    /// <param name="Resolution">The range resolution and quality, or DataValue.Null when absent.</param>
    public sealed record IaCalibrationFeature(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        ExpandedNodeId TypeDefinition,
        DataValue Value,
        DataValue EngineeringUnits,
        DataValue Resolution);

    public sealed partial class IndustrialAutomationClient
    {
        /// <summary>
        /// Distinguishes all published target categories, checking device before its reusable supertype.
        /// </summary>
        public async ValueTask<IaTargetCategory> ReadCalibrationCategoryAsync(
            NodeId target,
            CancellationToken cancellationToken = default)
        {
            NodeId category = await ClientModelAccess.ResolveAsync(Session, Require(target), Namespaces.IA,
                BrowseNames.CalibrationTargetCategory, cancellationToken).ConfigureAwait(false);
            if (category.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound);
            }
            (ArrayOf<ArrayOf<ReferenceDescription>> types, ArrayOf<ServiceResult> errors) =
                await Session.ManagedBrowseAsync(null, null, [category], 0, BrowseDirection.Forward,
                    Opc.Ua.ReferenceTypeIds.HasTypeDefinition, false, (uint)NodeClass.ObjectType,
                    cancellationToken).ConfigureAwait(false);
            if (errors.Count != 0 && ServiceResult.IsBad(errors[0]))
            {
                throw new ServiceResultException(errors[0]);
            }
            if (types.Count != 1 || types[0].Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid);
            }
            NodeId type = ExpandedNodeId.ToNodeId(types[0][0].NodeId, Session.NamespaceUris);
            ArrayOf<(ExpandedNodeId Type, IaTargetCategory Category)> candidates =
            [
                (ObjectTypeIds.ReusableDeviceCalibrationTargetCategoryType, IaTargetCategory.ReusableDevice),
                (ObjectTypeIds.ReusableCalibrationTargetCategoryType, IaTargetCategory.Reusable),
                (ObjectTypeIds.OneTimeCalibrationTargetCategoryType, IaTargetCategory.OneTime),
                (ObjectTypeIds.DynamicCalibrationTargetCategoryType, IaTargetCategory.Dynamic)
            ];
            for (int i = 0; i < candidates.Count; i++)
            {
                NodeId expected = ExpandedNodeId.ToNodeId(candidates[i].Type, Session.NamespaceUris);
                if (!expected.IsNull &&
                    await Session.NodeCache.IsTypeOfAsync(type, expected, cancellationToken).ConfigureAwait(false))
                {
                    return candidates[i].Category;
                }
            }
            return IaTargetCategory.Unknown;
        }

        /// <summary>
        /// Reads all configured standard and custom target features without flattening native value types.
        /// </summary>
        public async ValueTask<ArrayOf<IaCalibrationFeature>> ReadCalibrationFeaturesAsync(
            NodeId target,
            int maximumFeatures = 256,
            CancellationToken cancellationToken = default)
        {
            if (maximumFeatures < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumFeatures));
            }
            NodeId features = await ClientModelAccess.ResolveAsync(Session, Require(target), Namespaces.IA,
                BrowseNames.CalibrationTargetFeatures, cancellationToken).ConfigureAwait(false);
            if (features.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotFound);
            }
            ArrayOf<ReferenceDescription> children = await ClientModelAccess.BrowseAsync(
                Session, features, (uint)(NodeClass.Variable | NodeClass.Object), cancellationToken)
                .ConfigureAwait(false);
            var featuresToRead = new List<ReferenceDescription>();
            var nodes = new List<NodeId>();
            var seen = new HashSet<NodeId>();
            for (int i = 0; i < children.Count; i++)
            {
                ReferenceDescription child = children[i];
                NodeId node = ExpandedNodeId.ToNodeId(child.NodeId, Session.NamespaceUris);
                if (node.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdInvalid);
                }
                if (!seen.Add(node))
                {
                    continue;
                }
                if (seen.Count > maximumFeatures)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
                featuresToRead.Add(child);
                nodes.Add(node);
            }
            if (nodes.Count == 0)
            {
                return [];
            }
            (ArrayOf<ArrayOf<ReferenceDescription>> properties, ArrayOf<ServiceResult> errors) =
                await Session.ManagedBrowseAsync(null, null, nodes.ToArray(), 0, BrowseDirection.Forward,
                    Opc.Ua.ReferenceTypeIds.HierarchicalReferences, true, 0, cancellationToken)
                    .ConfigureAwait(false);
            for (int i = 0; i < errors.Count; i++)
            {
                if (ServiceResult.IsBad(errors[i]))
                {
                    throw new ServiceResultException(errors[i]);
                }
            }
            if (properties.Count != nodes.Count)
            {
                throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                    "A calibration feature browse returned an invalid result.");
            }
            var readNodes = new List<NodeId>();
            var indexes = new (int Value, int Units, int Resolution)[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                indexes[i] = (
                    AddRead(featuresToRead[i].NodeClass == NodeClass.Variable ? nodes[i] : NodeId.Null),
                    AddRead(ResolveFeatureProperty(properties[i], Opc.Ua.Namespaces.OpcUa,
                        Opc.Ua.BrowseNames.EngineeringUnits)),
                    AddRead(ResolveFeatureProperty(properties[i], Namespaces.IA, BrowseNames.Resolution)));
            }
            ArrayOf<DataValue> values = [];
            if (readNodes.Count != 0)
            {
                (values, _) = await Session.ReadValuesAsync(readNodes.ToArray(), cancellationToken)
                    .ConfigureAwait(false);
                if (values.Count != readNodes.Count)
                {
                    throw ServiceResultException.Create(StatusCodes.BadUnexpectedError,
                        "A calibration feature read returned an invalid result.");
                }
            }
            var result = new List<IaCalibrationFeature>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                ReferenceDescription child = featuresToRead[i];
                (int value, int units, int resolution) = indexes[i];
                result.Add(new IaCalibrationFeature(nodes[i], child.BrowseName, child.DisplayName,
                    child.TypeDefinition,
                    value < 0 ? DataValue.Null : values[value],
                    units < 0 ? DataValue.Null : values[units],
                    resolution < 0 ? DataValue.Null : values[resolution]));
            }
            return result.ToArray();

            int AddRead(NodeId node)
            {
                if (node.IsNull)
                {
                    return -1;
                }
                int index = readNodes.Count;
                readNodes.Add(node);
                return index;
            }
        }

        /// <summary>
        /// Resolves an optional property from the shared browse while rejecting ambiguous standard names.
        /// </summary>
        private NodeId ResolveFeatureProperty(
            ArrayOf<ReferenceDescription> references,
            string namespaceUri,
            string name)
        {
            int namespaceIndex = Session.NamespaceUris.GetIndex(namespaceUri);
            if (namespaceIndex < 0)
            {
                return NodeId.Null;
            }
            var browseName = new QualifiedName(name, (ushort)namespaceIndex);
            NodeId found = NodeId.Null;
            for (int i = 0; i < references.Count; i++)
            {
                if (references[i].BrowseName != browseName)
                {
                    continue;
                }
                if (!found.IsNull)
                {
                    throw ServiceResultException.Create(StatusCodes.BadBrowseNameDuplicated,
                        "The server exposes an ambiguous standard child.");
                }
                found = ExpandedNodeId.ToNodeId(references[i].NodeId, Session.NamespaceUris);
            }
            return found;
        }
    }
}
