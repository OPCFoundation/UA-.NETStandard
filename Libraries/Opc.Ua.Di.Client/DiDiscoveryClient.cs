/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Di.Client
{
    /// <summary>
    /// Helper for discovering OPC UA DI (OPC 10000-100) devices on
    /// a connected server. Browses the <c>Objects</c> folder
    /// recursively to locate instances whose type definition is
    /// <c>DeviceType</c> (or a subtype).
    /// </summary>
    public static class DiDiscoveryClient
    {
        /// <summary>
        /// Enumerates all <c>DeviceType</c> instances below the
        /// <c>Objects</c> folder on the connected server.
        /// </summary>
        /// <param name="session">An open OPC UA session.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A list of discovered device entries.</returns>
        public static async ValueTask<IReadOnlyList<DeviceEntry>> EnumerateDevicesAsync(
            ISession session,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            if (session is null) { throw new ArgumentNullException(nameof(session)); }
            if (telemetry is null) { throw new ArgumentNullException(nameof(telemetry)); }

            ExpandedNodeId deviceTypeId = global::Opc.Ua.Di.ObjectTypeIds.DeviceType;
            List<DeviceEntry> devices = new();

            // Browse the Objects folder for all child objects.
            await BrowseForDevicesAsync(
                session,
                Opc.Ua.ObjectIds.ObjectsFolder,
                deviceTypeId,
                devices,
                depth: 0,
                maxDepth: 3,
                ct).ConfigureAwait(false);

            return devices;
        }

        private static async ValueTask BrowseForDevicesAsync(
            ISession session,
            NodeId parentId,
            ExpandedNodeId deviceTypeId,
            List<DeviceEntry> devices,
            int depth,
            int maxDepth,
            CancellationToken ct)
        {
            if (depth > maxDepth)
            {
                return;
            }

            (_, _, ArrayOf<ReferenceDescription> references) = await session.BrowseAsync(
                requestHeader: null,
                view: null,
                parentId,
                maxResultsToReturn: 0,
                BrowseDirection.Forward,
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                includeSubtypes: true,
                (uint)NodeClass.Object,
                ct).ConfigureAwait(false);

            ReferenceDescription[] snapshot =
                new ReferenceDescription[references.Count];
            for (int i = 0; i < references.Count; i++)
            {
                snapshot[i] = references[i];
            }

            foreach (ReferenceDescription reference in snapshot)
            {
                ct.ThrowIfCancellationRequested();

                NodeId targetId = ExpandedNodeId.ToNodeId(
                    reference.NodeId, session.NamespaceUris);

                if (reference.TypeDefinition == deviceTypeId)
                {
                    string displayName =
                        reference.DisplayName.Text ?? string.Empty;
                    string deviceClass = await ReadDeviceClassAsync(
                        session, targetId, ct).ConfigureAwait(false);
                    devices.Add(new DeviceEntry(
                        targetId, displayName, deviceClass));
                }
                else
                {
                    // Recurse into non-device objects to find nested
                    // devices (e.g. under organizational folders).
                    await BrowseForDevicesAsync(
                        session,
                        targetId,
                        deviceTypeId,
                        devices,
                        depth + 1,
                        maxDepth,
                        ct).ConfigureAwait(false);
                }
            }
        }

        private static async ValueTask<string> ReadDeviceClassAsync(
            ISession session,
            NodeId deviceNodeId,
            CancellationToken ct)
        {
            ushort diNs = session.NamespaceUris
                .GetIndexOrAppend(global::Opc.Ua.Di.Namespaces.OpcUaDi);

            BrowsePath path = new()
            {
                StartingNode = deviceNodeId,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName("DeviceClass", diNs)
                        }
                    ]
                }
            };

            TranslateBrowsePathsToNodeIdsResponse translateResponse = await session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null, new[] { path }.ToArrayOf(), ct)
                .ConfigureAwait(false);

            if (translateResponse.Results.Count == 0 ||
                StatusCode.IsBad(translateResponse.Results[0].StatusCode) ||
                translateResponse.Results[0].Targets.Count == 0)
            {
                return string.Empty;
            }

            NodeId targetId = ExpandedNodeId.ToNodeId(
                translateResponse.Results[0].Targets[0].TargetId,
                session.NamespaceUris);

            ArrayOf<ReadValueId> nodesToRead = new[]
            {
                new ReadValueId
                {
                    NodeId = targetId,
                    AttributeId = Attributes.Value
                }
            }.ToArrayOf();

            ReadResponse readResponse = await session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: nodesToRead,
                ct: ct).ConfigureAwait(false);

            if (readResponse.Results.Count == 0 ||
                StatusCode.IsBad(readResponse.Results[0].StatusCode))
            {
                return string.Empty;
            }

            Variant wrapped = readResponse.Results[0].WrappedValue;
            object? raw = wrapped.AsBoxedObject();
            if (raw is string s)
            {
                return s;
            }

            return raw?.ToString() ?? string.Empty;
        }
    }

    /// <summary>
    /// A single device entry discovered by
    /// <see cref="DiDiscoveryClient.EnumerateDevicesAsync"/>.
    /// </summary>
    /// <param name="DeviceId">The device NodeId.</param>
    /// <param name="DisplayName">The device display name.</param>
    /// <param name="DeviceClass">
    /// The device class (e.g. "Sensor"), or an empty string if not set.
    /// </param>
    public sealed record DeviceEntry(
        NodeId DeviceId,
        string DisplayName,
        string DeviceClass);
}
