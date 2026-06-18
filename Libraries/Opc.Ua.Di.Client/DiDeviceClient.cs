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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.Di.Client
{
    /// <summary>
    /// High-level client for reading OPC UA DI (OPC 10000-100) device
    /// information. Composes (does <strong>not</strong> inherit) the
    /// source-generated <see cref="Opc.Ua.Di.DeviceTypeClient"/> proxy
    /// and provides ergonomic helpers for reading identification
    /// properties and browsing functional groups.
    /// </summary>
    public sealed class DiDeviceClient
    {
        private static readonly string[] IdentificationBrowseNames =
        [
            "Manufacturer",
            "Model",
            "SerialNumber",
            "HardwareRevision",
            "SoftwareRevision",
            "DeviceRevision",
            "DeviceClass",
            "ProductInstanceUri"
        ];

        /// <summary>
        /// Creates a new client rooted at <paramref name="deviceNodeId"/>.
        /// </summary>
        /// <param name="session">An open OPC UA session.</param>
        /// <param name="deviceNodeId">NodeId of a
        /// <c>DeviceType</c> instance on the server.</param>
        /// <param name="telemetry">Telemetry context used for diagnostics.</param>
        public DiDeviceClient(
            ISession session,
            NodeId deviceNodeId,
            ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (deviceNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Device NodeId is required.", nameof(deviceNodeId));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            Session = session;
            DeviceNodeId = deviceNodeId;
            Telemetry = telemetry;
            Proxy = new Opc.Ua.Di.DeviceTypeClient(session, deviceNodeId, telemetry);
        }

        /// <summary>
        /// The OPC UA session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// The device object NodeId.
        /// </summary>
        public NodeId DeviceNodeId { get; }

        /// <summary>
        /// Telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// The underlying generated proxy.
        /// </summary>
        public Opc.Ua.Di.DeviceTypeClient Proxy { get; }

        /// <summary>
        /// Creates a <see cref="DiDeviceClient"/> for the device at
        /// <paramref name="deviceNodeId"/> by verifying the node exists
        /// and reading its display name.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<DiDeviceClient> ForDeviceAsync(
            ISession session,
            NodeId deviceNodeId,
            ITelemetryContext telemetry,
            CancellationToken ct = default)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (deviceNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Device NodeId is required.", nameof(deviceNodeId));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            // Verify the node exists by reading the NodeClass attribute.
            ArrayOf<ReadValueId> nodesToRead = new[]
            {
                new ReadValueId
                {
                    NodeId = deviceNodeId,
                    AttributeId = Attributes.NodeClass
                }
            }.ToArrayOf();

            ReadResponse response = await session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: nodesToRead,
                ct: ct).ConfigureAwait(false);

            if (response.Results.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown,
                    "The specified device NodeId was not found on the server.");
            }

            return new DiDeviceClient(session, deviceNodeId, telemetry);
        }

        /// <summary>
        /// Reads the standard DI device identification properties
        /// (Manufacturer, Model, SerialNumber, etc.) from the device
        /// node using <c>TranslateBrowsePathsToNodeIds</c> and
        /// <c>Read</c>.
        /// </summary>
        public async ValueTask<DeviceIdentification> ReadIdentificationAsync(
            CancellationToken ct = default)
        {
            ushort diNs = Session.NamespaceUris
                .GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);

            // Build browse paths for each identification property.
            BrowsePath[] paths = new BrowsePath[IdentificationBrowseNames.Length];
            for (int i = 0; i < IdentificationBrowseNames.Length; i++)
            {
                paths[i] = new BrowsePath
                {
                    StartingNode = DeviceNodeId,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                                IsInverse = false,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(
                                    IdentificationBrowseNames[i], diNs)
                            }
                        ]
                    }
                };
            }

            TranslateBrowsePathsToNodeIdsResponse translateResponse = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null, paths.ToArrayOf(), ct)
                .ConfigureAwait(false);

            // Collect resolved NodeIds for a batch read.
            List<int> resolvedIndices = new();
            List<ReadValueId> readItems = new();
            for (int i = 0; i < translateResponse.Results.Count; i++)
            {
                BrowsePathResult result = translateResponse.Results[i];
                if (StatusCode.IsGood(result.StatusCode) &&
                    result.Targets.Count > 0)
                {
                    NodeId targetId = ExpandedNodeId.ToNodeId(
                        result.Targets[0].TargetId,
                        Session.NamespaceUris);
                    readItems.Add(new ReadValueId
                    {
                        NodeId = targetId,
                        AttributeId = Attributes.Value
                    });
                    resolvedIndices.Add(i);
                }
            }

            string?[] values = new string?[IdentificationBrowseNames.Length];

            if (readItems.Count > 0)
            {
                ReadResponse readResponse = await Session.ReadAsync(
                    requestHeader: null,
                    maxAge: 0,
                    timestampsToReturn: TimestampsToReturn.Neither,
                    nodesToRead: readItems.ToArray().ToArrayOf(),
                    ct: ct).ConfigureAwait(false);

                for (int i = 0; i < readResponse.Results.Count; i++)
                {
                    DataValue dv = readResponse.Results[i];
                    if (StatusCode.IsGood(dv.StatusCode))
                    {
                        int originalIndex = resolvedIndices[i];
                        values[originalIndex] =
                            ExtractStringFromWrappedValue(dv.WrappedValue);
                    }
                }
            }

            return new DeviceIdentification(
                Manufacturer: values[0],
                Model: values[1],
                SerialNumber: values[2],
                HardwareRevision: values[3],
                SoftwareRevision: values[4],
                DeviceRevision: values[5],
                DeviceClass: values[6],
                ProductInstanceUri: values[7]);
        }

        /// <summary>
        /// Browses the child <c>FunctionalGroupType</c> objects below
        /// the device node.
        /// </summary>
        public async IAsyncEnumerable<FunctionalGroupEntry> BrowseFunctionalGroupsAsync(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            ExpandedNodeId functionalGroupTypeId =
                Opc.Ua.Di.ObjectTypeIds.FunctionalGroupType;
            NodeId refTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;

            (_, _, ArrayOf<ReferenceDescription> references) = await Session.BrowseAsync(
                requestHeader: null,
                view: null,
                DeviceNodeId,
                maxResultsToReturn: 0,
                BrowseDirection.Forward,
                refTypeId,
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

                if (!IsSubtypeOf(reference.TypeDefinition, functionalGroupTypeId))
                {
                    continue;
                }

                NodeId targetId = ExpandedNodeId.ToNodeId(
                    reference.NodeId, Session.NamespaceUris);
                yield return new FunctionalGroupEntry(
                    targetId,
                    reference.DisplayName.Text ?? string.Empty);
            }
        }

        /// <summary>
        /// Reads a single property by browse name from the device node.
        /// Returns <c>default</c> if the property is not found or the
        /// value cannot be converted to <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Expected property value type.</typeparam>
        /// <exception cref="ArgumentNullException"></exception>
        public async ValueTask<T?> ReadPropertyAsync<T>(
            string browseName,
            CancellationToken ct = default)
        {
            if (browseName is null)
            {
                throw new ArgumentNullException(nameof(browseName));
            }
            ushort diNs = Session.NamespaceUris
                .GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);

            BrowsePath path = new()
            {
                StartingNode = DeviceNodeId,
                RelativePath = new RelativePath
                {
                    Elements =
                    [
                        new RelativePathElement
                        {
                            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                            IsInverse = false,
                            IncludeSubtypes = true,
                            TargetName = new QualifiedName(browseName, diNs)
                        }
                    ]
                }
            };

            TranslateBrowsePathsToNodeIdsResponse translateResponse = await Session
                .TranslateBrowsePathsToNodeIdsAsync(
                    null, new[] { path }.ToArrayOf(), ct)
                .ConfigureAwait(false);

            if (translateResponse.Results.Count == 0 ||
                StatusCode.IsBad(translateResponse.Results[0].StatusCode) ||
                translateResponse.Results[0].Targets.Count == 0)
            {
                return default;
            }

            NodeId targetId = ExpandedNodeId.ToNodeId(
                translateResponse.Results[0].Targets[0].TargetId,
                Session.NamespaceUris);

            ArrayOf<ReadValueId> nodesToRead = new[]
            {
                new ReadValueId
                {
                    NodeId = targetId,
                    AttributeId = Attributes.Value
                }
            }.ToArrayOf();

            ReadResponse readResponse = await Session.ReadAsync(
                requestHeader: null,
                maxAge: 0,
                timestampsToReturn: TimestampsToReturn.Neither,
                nodesToRead: nodesToRead,
                ct: ct).ConfigureAwait(false);

            if (readResponse.Results.Count == 0 ||
                StatusCode.IsBad(readResponse.Results[0].StatusCode))
            {
                return default;
            }

            Variant wrapped = readResponse.Results[0].WrappedValue;
            object? raw = wrapped.AsBoxedObject();
            if (raw is T typed)
            {
                return typed;
            }
            if (typeof(T) == typeof(string) &&
                wrapped.TryGetValue(out LocalizedText lt))
            {
                return (T?)(object?)(lt.Text ?? string.Empty);
            }

            return default;
        }

        private static bool IsSubtypeOf(
            ExpandedNodeId typeDefinition,
            ExpandedNodeId expectedType)
        {
            return typeDefinition == expectedType;
        }

        private static string? ExtractStringFromWrappedValue(Variant wrapped)
        {
            if (wrapped.TryGetValue(out LocalizedText lt))
            {
                return lt.Text;
            }

            object? raw = wrapped.AsBoxedObject();
            if (raw is string s)
            {
                return s;
            }
            return raw?.ToString();
        }
    }

    /// <summary>
    /// A single functional group entry exposed by
    /// <see cref="DiDeviceClient.BrowseFunctionalGroupsAsync"/>.
    /// </summary>
    /// <param name="NodeId">The functional group NodeId.</param>
    /// <param name="DisplayName">The functional group display name.</param>
    public sealed record FunctionalGroupEntry(
        NodeId NodeId,
        string DisplayName);
}
