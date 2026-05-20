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
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.WotCon;

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

        /// <summary>The OPC UA session.</summary>
        public ISession Session { get; }

        /// <summary>Asset NodeId.</summary>
        public NodeId AssetId { get; }

        /// <summary>Asset display name (BrowseName minus namespace prefix).</summary>
        public string Name { get; }

        /// <summary>The generated <c>WoTAssetFileType</c> proxy.</summary>
        public WoTAssetFileTypeClient File { get; }

        /// <summary>Telemetry context.</summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Uploads a WoT Thing Description and triggers
        /// <c>CloseAndUpdate</c>. After this call the server has
        /// materialised the asset's variables and methods.
        /// </summary>
        public ValueTask UploadThingDescriptionAsync(
            ReadOnlyMemory<byte> thingDescriptionJson,
            CancellationToken ct = default)
            => File.UploadAndUpdateAsync(thingDescriptionJson, ct: ct);

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
            => File.UploadAndUpdateAsync(thingDescriptionJson, ct: ct);

        /// <summary>
        /// Downloads the currently persisted WoT Thing Description.
        /// </summary>
        public ValueTask<byte[]> DownloadThingDescriptionAsync(
            CancellationToken ct = default)
            => File.DownloadAllAsync(ct: ct);

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
            => File.DownloadToAsync(destination, ct: ct);

        /// <summary>
        /// Enumerates the asset's property variables (children attached
        /// via <c>HasWoTComponent</c>).
        /// </summary>
        public IAsyncEnumerable<WotAssetVariableEntry> EnumeratePropertiesAsync(
            CancellationToken ct = default)
            => BrowseChildrenAsync(
                Opc.Ua.WotCon.ReferenceTypeIds.HasWoTComponent,
                includeSubtypes: false,
                nodeClasses: NodeClass.Variable,
                ct);

        /// <summary>
        /// Enumerates the asset's action methods (children attached via
        /// <c>HasComponent</c>).
        /// </summary>
        public IAsyncEnumerable<WotAssetVariableEntry> EnumerateActionsAsync(
            CancellationToken ct = default)
            => BrowseChildrenAsync(
                Opc.Ua.ReferenceTypeIds.HasComponent,
                includeSubtypes: false,
                nodeClasses: NodeClass.Method,
                ct);

        private async IAsyncEnumerable<WotAssetVariableEntry> BrowseChildrenAsync(
            ExpandedNodeId referenceType,
            bool includeSubtypes,
            NodeClass nodeClasses,
            [EnumeratorCancellation] CancellationToken ct)
        {
            NodeId refTypeId = ExpandedNodeId.ToNodeId(referenceType, Session.NamespaceUris);
            (_, _, ArrayOf<ReferenceDescription> references) = await Session.BrowseAsync(
                requestHeader: null,
                view: null,
                AssetId,
                maxResultsToReturn: 0,
                BrowseDirection.Forward,
                refTypeId,
                includeSubtypes: includeSubtypes,
                (uint)nodeClasses,
                ct).ConfigureAwait(false);
            ReferenceDescription[] snapshot = new ReferenceDescription[references.Count];
            for (int i = 0; i < references.Count; i++)
            {
                snapshot[i] = references[i];
            }
            foreach (ReferenceDescription reference in snapshot)
            {
                ct.ThrowIfCancellationRequested();
                NodeId targetId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
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
