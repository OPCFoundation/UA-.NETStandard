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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Aas.Client.Registry
{
    /// <summary>
    /// Shared browse and property-read helpers for the AAS registry client wrappers.
    /// </summary>
    internal static class AasRegistryNodeReader
    {
        /// <summary>
        /// Browses organized child objects.
        /// </summary>
        public static async ValueTask<ArrayOf<NodeId>> BrowseOrganizedObjectsAsync(
            ISession session,
            NodeId parentNodeId,
            CancellationToken ct)
        {
            var nodes = new List<NodeId>();
            ByteString continuationPoint = default;
            do
            {
                ArrayOf<ReferenceDescription> references;
                if (continuationPoint.IsNull)
                {
                    (_, continuationPoint, references) = await session.BrowseAsync(
                        null,
                        null,
                        parentNodeId,
                        0,
                        BrowseDirection.Forward,
                        ReferenceTypeIds.Organizes,
                        includeSubtypes: true,
                        nodeClassMask: (uint)NodeClass.Object,
                        ct).ConfigureAwait(false);
                }
                else
                {
                    (_, continuationPoint, references) = await session.BrowseNextAsync(
                        null,
                        releaseContinuationPoint: false,
                        continuationPoint,
                        ct).ConfigureAwait(false);
                }

                for (int i = 0; i < references.Count; i++)
                {
                    NodeId nodeId = ExpandedNodeId.ToNodeId(references[i].NodeId, session.NamespaceUris);
                    if (!nodeId.IsNull)
                    {
                        nodes.Add(nodeId);
                    }
                }
            }
            while (!continuationPoint.IsNull);

            return nodes.ToArrayOf();
        }

        /// <summary>
        /// Reads a mandatory string Property.
        /// </summary>
        public static async ValueTask<string> ReadRequiredStringPropertyAsync(
            ISession session,
            NodeId ownerNodeId,
            ushort propertyNamespaceIndex,
            string propertyName,
            CancellationToken ct)
        {
            DataValue value = await ReadPropertyValueAsync(
                session,
                ownerNodeId,
                propertyNamespaceIndex,
                propertyName,
                ct).ConfigureAwait(false);
            if (StatusCode.IsBad(value.StatusCode) || !value.WrappedValue.TryGetValue(out string? text))
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    $"Property '{propertyName}' did not return a string value.");
            }
            return text ?? string.Empty;
        }

        /// <summary>
        /// Reads an optional string Property.
        /// </summary>
        public static async ValueTask<string> ReadOptionalStringPropertyAsync(
            ISession session,
            NodeId ownerNodeId,
            ushort propertyNamespaceIndex,
            string propertyName,
            CancellationToken ct)
        {
            try
            {
                DataValue value = await ReadPropertyValueAsync(
                    session,
                    ownerNodeId,
                    propertyNamespaceIndex,
                    propertyName,
                    ct).ConfigureAwait(false);
                if (StatusCode.IsBad(value.StatusCode) || !value.WrappedValue.TryGetValue(out string? text))
                {
                    return string.Empty;
                }
                return text ?? string.Empty;
            }
            catch (ServiceResultException ex) when (
                ex.StatusCode == StatusCodes.BadNoMatch ||
                ex.StatusCode == StatusCodes.BadNodeIdUnknown)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Reads an optional DateTime Property.
        /// </summary>
        public static async ValueTask<DateTime> ReadOptionalDateTimePropertyAsync(
            ISession session,
            NodeId ownerNodeId,
            ushort propertyNamespaceIndex,
            string propertyName,
            CancellationToken ct)
        {
            try
            {
                DataValue value = await ReadPropertyValueAsync(
                    session,
                    ownerNodeId,
                    propertyNamespaceIndex,
                    propertyName,
                    ct).ConfigureAwait(false);
                if (StatusCode.IsBad(value.StatusCode))
                {
                    return DateTime.MinValue;
                }
                if (value.WrappedValue.TryGetValue(out DateTimeUtc dateTimeUtc))
                {
                    return (DateTime)dateTimeUtc;
                }
                return DateTime.MinValue;
            }
            catch (ServiceResultException ex) when (
                ex.StatusCode == StatusCodes.BadNoMatch ||
                ex.StatusCode == StatusCodes.BadNodeIdUnknown)
            {
                return DateTime.MinValue;
            }
        }

        private static async ValueTask<DataValue> ReadPropertyValueAsync(
            ISession session,
            NodeId ownerNodeId,
            ushort propertyNamespaceIndex,
            string propertyName,
            CancellationToken ct)
        {
            NodeId propertyNodeId = await AasRegistryBrowsePathResolver.ResolveChildAsync(
                session,
                ownerNodeId,
                ReferenceTypeIds.HasProperty,
                propertyNamespaceIndex,
                propertyName,
                StatusCodes.BadNoMatch,
                $"Property '{propertyName}' was not found.",
                ct).ConfigureAwait(false);
            ReadResponse response = await session.ReadAsync(
                null,
                0,
                TimestampsToReturn.Neither,
                new[] { new ReadValueId { NodeId = propertyNodeId, AttributeId = Attributes.Value } }.ToArrayOf(),
                ct).ConfigureAwait(false);
            if (response.Results.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    $"Property '{propertyName}' returned no value.");
            }
            return response.Results[0];
        }
    }
}
