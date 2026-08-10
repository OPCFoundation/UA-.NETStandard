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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// Focused client over a single <c>VisionMediaManagementType</c> instance —
    /// typically the <c>Media</c> object of a sensor. Wraps <c>GetClip</c>,
    /// <c>GetStreamEndpoint</c>, <c>ReleaseStreamEndpoint</c>,
    /// <c>ConfigureStreamEndpoint</c>, <c>SelectEndpoint</c>, and read access to
    /// <c>LatestClip</c> and <c>LatestClipMetadata</c>, following the §6 media rules
    /// (by-reference default, inline gating, §6.4 status-code classification).
    /// </summary>
    public sealed class VisionMediaClient
    {
        private readonly VisionClientOperations m_operations;
        private readonly VisionMediaManagementTypeClient m_proxy;

        internal VisionMediaClient(VisionClientOperations operations, NodeId mediaNodeId)
        {
            m_operations = operations
                ?? throw new ArgumentNullException(nameof(operations));
            if (mediaNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Media NodeId must not be null.", nameof(mediaNodeId));
            }
            MediaNodeId = mediaNodeId;
            m_proxy = new VisionMediaManagementTypeClient(
                m_operations.Session, mediaNodeId, m_operations.Telemetry);
        }

        /// <summary>
        /// Gets the media-management object NodeId.
        /// </summary>
        public NodeId MediaNodeId { get; }

        /// <summary>
        /// Enumerates the clip endpoints attached to this media manager.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async IAsyncEnumerable<VisionNodeEntry> EnumerateClipEndpointsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            FolderTypeClient? folder = await m_proxy.GetClipEndpointsAsync(
                m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            if (folder is null || folder.ObjectId.IsNull)
            {
                yield break;
            }
            ArrayOf<ReferenceDescription> refs = await m_operations
                .BrowseHierarchicalObjectsAsync(folder.ObjectId, cancellationToken)
                .ConfigureAwait(false);
            for (int ii = 0; ii < refs.Count; ii++)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(
                    refs[ii].NodeId, m_operations.Session.NamespaceUris);
                NodeId typeDef = ExpandedNodeId.ToNodeId(
                    refs[ii].TypeDefinition, m_operations.Session.NamespaceUris);
                if (!nodeId.IsNull && !typeDef.IsNull)
                {
                    yield return new VisionNodeEntry(
                        nodeId, refs[ii].BrowseName, refs[ii].DisplayName, typeDef);
                }
            }
        }

        /// <summary>
        /// Enumerates the stream endpoints attached to this media manager.
        /// </summary>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async IAsyncEnumerable<VisionNodeEntry> EnumerateStreamEndpointsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            FolderTypeClient? folder = await m_proxy.GetStreamEndpointsAsync(
                m_operations.Telemetry, cancellationToken).ConfigureAwait(false);
            if (folder is null || folder.ObjectId.IsNull)
            {
                yield break;
            }
            ArrayOf<ReferenceDescription> refs = await m_operations
                .BrowseHierarchicalObjectsAsync(folder.ObjectId, cancellationToken)
                .ConfigureAwait(false);
            for (int ii = 0; ii < refs.Count; ii++)
            {
                NodeId nodeId = ExpandedNodeId.ToNodeId(
                    refs[ii].NodeId, m_operations.Session.NamespaceUris);
                NodeId typeDef = ExpandedNodeId.ToNodeId(
                    refs[ii].TypeDefinition, m_operations.Session.NamespaceUris);
                if (!nodeId.IsNull && !typeDef.IsNull)
                {
                    yield return new VisionNodeEntry(
                        nodeId, refs[ii].BrowseName, refs[ii].DisplayName, typeDef);
                }
            }
        }

        /// <summary>
        /// Calls <c>GetClip</c> and returns the by-reference image descriptor. When
        /// <paramref name="requestInline"/> is <c>true</c> and the still fits the
        /// Server's effective inline limit, <see cref="VisionClipResult.InlineImage"/>
        /// carries the encoded bytes.
        /// </summary>
        /// <param name="endpointNodeId">
        /// The <c>ClipEndpointType</c> NodeId, or a null NodeId to let the Server
        /// apply the §6.3 selection rule (<c>PreferredClipEndpoint</c> first, then
        /// the first endpoint in BrowseName order).
        /// </param>
        /// <param name="resultId">
        /// The <c>ResultId</c> to correlate this clip against, or a null string.
        /// </param>
        /// <param name="timestamp">
        /// The requested acquisition time.
        /// </param>
        /// <param name="format">
        /// The desired encoded format.
        /// </param>
        /// <param name="requestInline">
        /// Request inline delivery on top of the by-reference descriptor.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        /// <exception cref="ServiceResultException">
        /// The Server refused the call.
        /// </exception>
        public async Task<VisionClipResult> GetClipAsync(
            NodeId endpointNodeId,
            string? resultId,
            DateTimeUtc timestamp,
            VisionClipFormatEnum format,
            bool requestInline,
            CancellationToken cancellationToken = default)
        {
            (VisionImageReferenceDataType image, NodeId endpointOut, ByteString inlineImage) =
                await m_proxy.GetClipAsync(
                    endpointNodeId,
                    resultId ?? string.Empty,
                    timestamp,
                    format,
                    requestInline,
                    cancellationToken).ConfigureAwait(false);
            return new VisionClipResult
            {
                Image = image,
                EndpointNodeId = endpointOut,
                InlineImage = inlineImage
            };
        }

        /// <summary>
        /// Reads the <c>LatestClip</c> variable on the clip endpoint and its
        /// concurrent <c>LatestClipMetadata</c>. Classifies the resulting
        /// <see cref="StatusCode"/> per §6.4 rules 3 and 5 so a client can branch
        /// on state rather than raw codes.
        /// </summary>
        /// <param name="clipEndpointNodeId">
        /// The <c>ClipEndpointType</c> instance NodeId.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionInlineClipReading> ReadLatestClipAsync(
            NodeId clipEndpointNodeId,
            CancellationToken cancellationToken = default)
        {
            if (clipEndpointNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Clip endpoint NodeId must not be null.", nameof(clipEndpointNodeId));
            }
            string[] members =
            [
                BrowseNames.LatestClip,
                BrowseNames.LatestClipMetadata
            ];
            ArrayOf<NodeId> nodes = await m_operations.ResolveChildrenAsync(
                clipEndpointNodeId, members, cancellationToken).ConfigureAwait(false);
            var toRead = new List<NodeId>();
            for (int ii = 0; ii < nodes.Count; ii++)
            {
                if (!nodes[ii].IsNull)
                {
                    toRead.Add(nodes[ii]);
                }
            }
            ArrayOf<DataValue> values = await m_operations.ReadValuesAsync(
                toRead, cancellationToken).ConfigureAwait(false);
            int cursor = 0;
            DataValue latestClip = DataValue.Null;
            if (!nodes[0].IsNull)
            {
                latestClip = values[cursor++];
            }
            VisionImageReferenceDataType? metadata = null;
            if (!nodes[1].IsNull)
            {
                DataValue metadataValue = values[cursor++];
#pragma warning disable CS8600 // TryGetValue uses [MaybeNullWhen(false)] on encodeable overloads.
                if (StatusCode.IsGood(metadataValue.StatusCode) &&
                    metadataValue.WrappedValue.TryGetValue(
                        out VisionImageReferenceDataType meta,
                        m_operations.Session.MessageContext))
                {
                    metadata = meta;
                }
#pragma warning restore CS8600
            }
            StatusCode statusCode = latestClip.StatusCode;
            ByteString bytes = ByteString.Empty;
            VisionInlineClipState state;
            if (StatusCode.IsGood(statusCode))
            {
                if (latestClip.WrappedValue.TryGetValue(out ByteString candidate))
                {
                    bytes = candidate;
                }
                state = VisionInlineClipState.Available;
            }
            else
            {
                state = ClassifyInlineState(statusCode);
            }
            return new VisionInlineClipReading(bytes, metadata, statusCode, state);
        }

        /// <summary>
        /// Reads the <c>LatestClipMetadata</c> variable on the clip endpoint. Returns
        /// <c>null</c> when the Server has not published one yet.
        /// </summary>
        /// <param name="clipEndpointNodeId">
        /// The <c>ClipEndpointType</c> instance NodeId.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionImageReferenceDataType?> ReadLatestClipMetadataAsync(
            NodeId clipEndpointNodeId,
            CancellationToken cancellationToken = default)
        {
            if (clipEndpointNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Clip endpoint NodeId must not be null.", nameof(clipEndpointNodeId));
            }
            NodeId node = await m_operations.ResolveChildAsync(
                clipEndpointNodeId,
                BrowseNames.LatestClipMetadata,
                cancellationToken).ConfigureAwait(false);
            return await m_operations
                .TryReadStructureAsync<VisionImageReferenceDataType>(node, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Configures the video codec, resolution, frame rate and bitrate of a
        /// stream endpoint (§6.3 <c>ConfigureStreamEndpoint</c>).
        /// </summary>
        /// <param name="streamEndpointNodeId">
        /// The <c>StreamEndpointType</c> instance NodeId.
        /// </param>
        /// <param name="codec">
        /// The desired codec.
        /// </param>
        /// <param name="width">
        /// The desired horizontal resolution in pixels.
        /// </param>
        /// <param name="height">
        /// The desired vertical resolution in pixels.
        /// </param>
        /// <param name="frameRate">
        /// The desired frame rate in Hz.
        /// </param>
        /// <param name="bitrate">
        /// The desired target bitrate in bits per second.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task ConfigureStreamEndpointAsync(
            NodeId streamEndpointNodeId,
            VisionVideoCodecEnum codec,
            uint width,
            uint height,
            double frameRate,
            uint bitrate,
            CancellationToken cancellationToken = default)
        {
            if (streamEndpointNodeId.IsNull)
            {
                throw new ArgumentException(
                    "Stream endpoint NodeId must not be null.", nameof(streamEndpointNodeId));
            }
            return m_proxy.ConfigureStreamEndpointAsync(
                streamEndpointNodeId, codec, width, height, frameRate, bitrate,
                cancellationToken).AsTask();
        }

        /// <summary>
        /// Opens a stream session against the stream endpoint, returning the
        /// session token, URI, protocol and expiry as reported by the Server.
        /// </summary>
        /// <param name="streamEndpointNodeId">
        /// The <c>StreamEndpointType</c> instance NodeId, or a null NodeId to apply
        /// the §6.3 selection rule.
        /// </param>
        /// <param name="profileName">
        /// The desired profile name, or an empty string to accept the Server's
        /// default.
        /// </param>
        /// <param name="preferredProtocol">
        /// The preferred stream protocol.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public async Task<VisionStreamSessionDataType> GetStreamEndpointAsync(
            NodeId streamEndpointNodeId,
            string profileName,
            VisionStreamProtocolEnum preferredProtocol,
            CancellationToken cancellationToken = default)
        {
            if (profileName is null)
            {
                throw new ArgumentNullException(nameof(profileName));
            }
            (VisionStreamSessionDataType session, NodeId _) = await m_proxy
                .GetStreamEndpointAsync(
                    streamEndpointNodeId,
                    profileName,
                    preferredProtocol,
                    cancellationToken).ConfigureAwait(false);
            return session;
        }

        /// <summary>
        /// Releases a previously opened stream session identified by
        /// <paramref name="sessionToken"/>.
        /// </summary>
        /// <param name="sessionToken">
        /// The session token returned by <see cref="GetStreamEndpointAsync"/>.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task ReleaseStreamEndpointAsync(
            ByteString sessionToken,
            CancellationToken cancellationToken = default)
        {
            return m_proxy.ReleaseStreamEndpointAsync(
                sessionToken, cancellationToken).AsTask();
        }

        /// <summary>
        /// Sets the Server's preferred clip and stream endpoints (§6.3
        /// <c>SelectEndpoint</c>). A null argument leaves the corresponding preference
        /// unchanged.
        /// </summary>
        /// <param name="streamEndpointNodeId">
        /// The stream endpoint to prefer.
        /// </param>
        /// <param name="clipEndpointNodeId">
        /// The clip endpoint to prefer.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancels the operation.
        /// </param>
        public Task SelectEndpointAsync(
            NodeId streamEndpointNodeId,
            NodeId clipEndpointNodeId,
            CancellationToken cancellationToken = default)
        {
            return m_proxy.SelectEndpointAsync(
                streamEndpointNodeId,
                clipEndpointNodeId,
                cancellationToken).AsTask();
        }

        private static VisionInlineClipState ClassifyInlineState(StatusCode statusCode)
        {
            uint code = statusCode.Code;
            if (code == StatusCodes.BadNoDataAvailable)
            {
                return VisionInlineClipState.NotYetAvailable;
            }
            if (code == StatusCodes.BadNotSupported)
            {
                return VisionInlineClipState.InlineDisabled;
            }
            if (code == StatusCodes.BadEncodingLimitsExceeded)
            {
                return VisionInlineClipState.Overflow;
            }
            return VisionInlineClipState.Faulted;
        }
    }
}
