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
            var result = new List<IaCalibrationFeature>();
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
                DataValue value = child.NodeClass == NodeClass.Variable
                    ? await Session.ReadValueAsync(node, cancellationToken).ConfigureAwait(false)
                    : DataValue.Null;
                DataValue units = await ReadOptionalPropertyAsync(node, Opc.Ua.Namespaces.OpcUa,
                    Opc.Ua.BrowseNames.EngineeringUnits, cancellationToken).ConfigureAwait(false);
                DataValue resolution = await ReadOptionalPropertyAsync(node, Namespaces.IA,
                    BrowseNames.Resolution, cancellationToken).ConfigureAwait(false);
                result.Add(new IaCalibrationFeature(node, child.BrowseName, child.DisplayName,
                    child.TypeDefinition, value, units, resolution));
            }
            return result.ToArray();
        }

        /// <summary>
        /// Preserves metadata quality and represents an omitted optional property explicitly.
        /// </summary>
        private async ValueTask<DataValue> ReadOptionalPropertyAsync(
            NodeId parent,
            string namespaceUri,
            string name,
            CancellationToken cancellationToken)
        {
            NodeId node = await ClientModelAccess.ResolveAsync(Session, parent, namespaceUri, name,
                cancellationToken).ConfigureAwait(false);
            return node.IsNull
                ? DataValue.Null
                : await Session.ReadValueAsync(node, cancellationToken).ConfigureAwait(false);
        }
    }
}
