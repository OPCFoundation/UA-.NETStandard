// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace Opc.Ua.WotCon.Client
{
    /// <summary>
    /// Thin wrapper around an individual WoT asset object plus its
    /// associated <c>WoTAssetFileType</c> child. Composes the generated
    /// <see cref="WoTAssetFileTypeClient"/> proxy and exposes the
    /// upload / download / browse helpers needed to drive the
    /// OPC 10100-1 §6.2 workflow from the client side.
    /// </summary>
    public sealed class WotAssetClient
    {
        internal WotAssetClient(
            ISession session,
            NodeId assetId,
            string name,
            WoTAssetFileTypeClient file,
            ITelemetryContext telemetry)
        {
            Session = session;
            AssetId = assetId;
            Name = name;
            File = file;
            Telemetry = telemetry;
        }

        /// <summary>
        /// The OPC UA session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Asset NodeId.
        /// </summary>
        public NodeId AssetId { get; }

        /// <summary>
        /// Asset display name (BrowseName minus namespace prefix).
        /// </summary>
        public string Name { get; }

        /// <summary>The generated <c>WoTAssetFileType</c> proxy.</summary>
        public WoTAssetFileTypeClient File { get; }

        /// <summary>
        /// Telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Uploads a WoT Thing Description and triggers
        /// <c>CloseAndUpdate</c>. After this call the server has
        /// materialised the asset's variables and methods.
        /// </summary>
        public ValueTask UploadThingDescriptionAsync(
            ReadOnlyMemory<byte> thingDescriptionJson,
            CancellationToken ct = default)
        {
            return File.UploadAndUpdateAsync(thingDescriptionJson, ct: ct);
        }

        /// <summary>
        /// Uploads a WoT Thing Description streamed from
        /// <paramref name="thingDescriptionJson"/> and triggers
        /// <c>CloseAndUpdate</c>. Streams are read sequentially until
        /// end-of-stream; non-seekable streams (e.g. network or
        /// compressed streams) are supported. The caller retains
        /// ownership of <paramref name="thingDescriptionJson"/> and is
        /// responsible for disposing it.
        /// </summary>
        public ValueTask UploadThingDescriptionAsync(
            Stream thingDescriptionJson,
            CancellationToken ct = default)
        {
            return File.UploadAndUpdateAsync(thingDescriptionJson, ct: ct);
        }

        /// <summary>
        /// Downloads the currently persisted WoT Thing Description.
        /// </summary>
        public ValueTask<byte[]> DownloadThingDescriptionAsync(
            CancellationToken ct = default)
        {
            return File.DownloadAllAsync(ct: ct);
        }

        /// <summary>
        /// Downloads the currently persisted WoT Thing Description and
        /// writes it sequentially into <paramref name="destination"/>.
        /// The caller retains ownership of
        /// <paramref name="destination"/> and is responsible for
        /// disposing it.
        /// </summary>
        public ValueTask DownloadThingDescriptionAsync(
            Stream destination,
            CancellationToken ct = default)
        {
            return File.DownloadToAsync(destination, ct: ct);
        }

        /// <summary>
        /// Enumerates the asset's property variables (children attached
        /// via <c>HasWoTComponent</c>).
        /// </summary>
        public IAsyncEnumerable<WotAssetVariableEntry> EnumeratePropertiesAsync(
            CancellationToken ct = default)
        {
            return BrowseChildrenAsync(
                        ReferenceTypeIds.HasWoTComponent,
                        includeSubtypes: false,
                        nodeClasses: NodeClass.Variable,
                        ct);
        }

        /// <summary>
        /// Enumerates the asset's action methods (children attached via
        /// <c>HasComponent</c>).
        /// </summary>
        public IAsyncEnumerable<WotAssetVariableEntry> EnumerateActionsAsync(
            CancellationToken ct = default)
        {
            return BrowseChildrenAsync(
                        Ua.ReferenceTypeIds.HasComponent,
                        includeSubtypes: false,
                        nodeClasses: NodeClass.Method,
                        ct);
        }

        private async IAsyncEnumerable<WotAssetVariableEntry> BrowseChildrenAsync(
            ExpandedNodeId referenceType,
            bool includeSubtypes,
            NodeClass nodeClasses,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var refTypeId = ExpandedNodeId.ToNodeId(referenceType, Session.NamespaceUris);
            await foreach (ReferenceDescription reference in Session.EnumerateReferencesAsync(
                AssetId, refTypeId, includeSubtypes: includeSubtypes, nodeClassMask: (uint)nodeClasses, ct: ct)
                .ConfigureAwait(false))
            {
                var targetId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                if (reference.NodeId.ServerIndex != 0 || targetId.IsNull)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "The affordance reference does not identify a node in the current session.");
                }
                yield return new WotAssetVariableEntry(
                    targetId,
                    reference.BrowseName.Name ?? string.Empty);
            }
        }
    }

    /// <summary>
    /// A property / action child of an asset surfaced by
    /// <see cref="WotAssetClient.EnumeratePropertiesAsync"/> /
    /// <see cref="WotAssetClient.EnumerateActionsAsync"/>.
    /// </summary>
    public sealed record WotAssetVariableEntry(NodeId NodeId, string BrowseName);
}
