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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Encoders;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Client;

namespace Opc.Ua.WotCon.Client
{
    /// <summary>
    /// Wrapper around a single registry document resource (a
    /// <c>ThingDescriptionFileType</c> or <c>ThingModelFileType</c>
    /// instance below a registry group). Composes the generated
    /// <see cref="WoTDocumentTypeClient"/> proxy shared by both document
    /// subtypes, which in turn inherits the <c>FileType</c>
    /// <c>Open</c>/<c>Read</c>/<c>Write</c>/<c>Close</c> primitives used
    /// here through the existing <see cref="FileTypeClientExtensions"/>.
    /// <see cref="UploadNewVersionAsync(ByteString, int, CancellationToken)"/>
    /// allocates a structural Version before streaming content. Direct writes
    /// through <see cref="Proxy"/> replace the concrete Version represented by
    /// that proxy.
    /// </summary>
    public sealed class WotRegistryResourceClient
    {
        internal WotRegistryResourceClient(
            ISession session,
            NodeId resourceNodeId,
            string groupId,
            string resourceId,
            string versionId,
            WoTDocumentKindEnum kind,
            GroupTypeClient groupProxy,
            WoTDocumentTypeClient proxy,
            bool pendingStructuralVersion,
            ITelemetryContext telemetry)
        {
            Session = session;
            ResourceNodeId = resourceNodeId;
            GroupId = groupId;
            ResourceId = resourceId;
            VersionId = versionId;
            Kind = kind;
            m_groupProxy = groupProxy;
            Proxy = proxy;
            m_pendingStructuralVersion = pendingStructuralVersion;
            HasContent = pendingStructuralVersion ? false : null;
            Telemetry = telemetry;
        }

        /// <summary>
        /// The OPC UA session.
        /// </summary>
        public ISession Session { get; }

        /// <summary>
        /// Resource object NodeId.
        /// </summary>
        public NodeId ResourceNodeId { get; }

        /// <summary>
        /// Id of the owning group.
        /// </summary>
        public string GroupId { get; }

        /// <summary>
        /// Resource id (BrowseName minus namespace prefix).
        /// </summary>
        public string ResourceId { get; }

        /// <summary>
        /// Version id represented by this client when it was returned from a
        /// create/get-or-create operation. It is empty for a logical default-resource lookup.
        /// </summary>
        public string VersionId { get; }

        /// <summary>
        /// Whether this resource is a Thing Description or a Thing Model.
        /// </summary>
        public WoTDocumentKindEnum Kind { get; }

        /// <summary>
        /// The underlying generated proxy, shared by
        /// <c>ThingDescriptionFileType</c> and <c>ThingModelFileType</c>.
        /// </summary>
        public WoTDocumentTypeClient Proxy { get; }

        /// <summary>
        /// Telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets the last content-state value observed for this Version, or
        /// <c>null</c> when the connected server does not expose that capability.
        /// </summary>
        public bool? HasContent { get; private set; }

        /// <summary>
        /// Reads the existing <c>ContentDigest</c> field to determine whether
        /// this Version has committed document bytes. Older servers that do not
        /// expose the field return <c>null</c>. The value is advisory; uploads
        /// use an atomic server operation before filling a content-less Version.
        /// </summary>
        public async ValueTask<bool?> HasContentAsync(CancellationToken ct = default)
        {
            NodeId contentDigestNodeId;
            try
            {
                ushort ns = Session.NamespaceUris.GetIndexOrAppend(Namespaces.WotCon);
                contentDigestNodeId = await WotConBrowsePathResolver.ResolveChildAsync(
                        Session,
                        ResourceNodeId,
                        Ua.ReferenceTypeIds.HierarchicalReferences,
                        ns,
                        BrowseNames.ContentDigest,
                        StatusCodes.BadNoMatch,
                        "The server does not expose Version content state.",
                        ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (IsUnavailableContentState(ex.StatusCode))
            {
                HasContent = null;
                return null;
            }

            ReadResponse response = await Session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    new[]
                    {
                        new ReadValueId
                        {
                            NodeId = contentDigestNodeId,
                            AttributeId = Attributes.Value
                        }
                    }.ToArrayOf(),
                    ct)
                .ConfigureAwait(false);
            if (response.Results.Count == 0)
            {
                HasContent = null;
                return null;
            }
            DataValue value = response.Results[0];
            if (StatusCode.IsBad(value.StatusCode))
            {
                if (IsUnavailableContentState(value.StatusCode))
                {
                    HasContent = null;
                    return null;
                }
                throw new ServiceResultException(value.StatusCode);
            }
            if (!value.WrappedValue.TryGetValue(out ByteString digest))
            {
                HasContent = null;
                return null;
            }
            if (digest.IsNull)
            {
                HasContent = null;
                return null;
            }
            HasContent = digest.Length > 0;
            return HasContent;
        }

        /// <summary>
        /// Calls <c>Validate</c> on the resource.
        /// </summary>
        public async ValueTask<WoTValidationOutcomeDataType> ValidateAsync(CancellationToken ct = default)
        {
            var request = new CallMethodRequest
            {
                ObjectId = ResourceNodeId,
                MethodId = ExpandedNodeId.ToNodeId(
                    MethodIds.WoTDocumentType_Validate,
                    Session.NamespaceUris),
                InputArguments = []
            };
            CallResponse response = await Session
                .CallAsync(new RequestHeader(), [request], ct)
                .ConfigureAwait(false);
            CallMethodResult result = GetFirstCallResult(response, "Validate");
            if (StatusCode.IsBad(result.StatusCode))
            {
                throw new ServiceResultException(result.StatusCode);
            }
            ArrayOf<Variant> output = result.OutputArguments.IsNull ? [] : result.OutputArguments;
            if (output.Count == 0 ||
                !TryDecodeStructure(output[0], out WoTValidationOutcomeDataType? outcome) ||
                outcome is null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "Validate returned unexpected output arguments.");
            }
            return outcome;
        }

        private static CallMethodResult GetFirstCallResult(CallResponse response, string methodName)
        {
            if (response.Results.IsNull || response.Results.Count == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    $"{methodName} returned no method result.");
            }
            return response.Results[0];
        }

        private bool TryDecodeStructure<T>(Variant value, out T? result)
            where T : class, IEncodeable, new()
        {
            result = null;
#pragma warning disable CS8600 // TryGetStructure annotates failed output as maybe-null.
            if (value.TryGetStructure(Session.MessageContext, out T decoded))
#pragma warning restore CS8600
            {
                result = decoded;
                return true;
            }
#pragma warning disable CS8600 // TryGetValue annotates failed output as maybe-null.
            if (value.TryGetValue(out ExtensionObject extension) &&
                extension.TryGetValue(out decoded, Session.MessageContext))
#pragma warning restore CS8600
            {
                result = decoded;
                return true;
            }
            if (value.TryGetValue(out extension) &&
                extension.TryGetAsBinary(out ByteString body, Session.MessageContext) &&
                !body.IsNull)
            {
                using var decoder = new BinaryDecoder(body.Span.ToArray(), Session.MessageContext);
                result = new T();
                result.Decode(decoder);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Calls <c>SetEnabled</c> on the resource.
        /// </summary>
        public ValueTask SetEnabledAsync(bool enabled, uint expectedEpoch, CancellationToken ct = default)
        {
            return Proxy.SetEnabledAsync(enabled, expectedEpoch, ct);
        }

        /// <summary>
        /// Calls <c>SetDefaultVersion</c> on the resource.
        /// </summary>
        public ValueTask SetDefaultVersionAsync(
            string versionId, uint expectedEpoch, CancellationToken ct = default)
        {
            return Proxy.SetDefaultVersionAsync(versionId, expectedEpoch, ct);
        }

        /// <summary>
        /// Calls <c>Delete</c> on the resource node. When the node is the current
        /// default Version mapped from the logical Resource, this deletes the
        /// Resource and all Versions using the Resource Meta epoch. Otherwise,
        /// it deletes only this Version using the Version epoch.
        /// </summary>
        public ValueTask DeleteAsync(uint expectedEpoch, CancellationToken ct = default)
        {
            return Proxy.DeleteAsync(expectedEpoch, ct);
        }

        /// <summary>
        /// Uploads <paramref name="content"/> through the inherited
        /// <c>FileType</c> primitives (<c>Open(Write|EraseExisting)</c> →
        /// <c>Write</c> → <c>Close</c>). Closing the write handle commits
        /// the buffer as a new resource version.
        /// </summary>
        public async ValueTask UploadNewVersionAsync(
            ByteString content,
            int chunkSize = FileTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            _ = await UploadNewVersionAndGetResultAsync(content, chunkSize, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Uploads a new Version and returns the Version id that received the bytes.
        /// A pending structural Version is filled in place only when the server
        /// supports the atomic content-less write capability.
        /// </summary>
        public async ValueTask<string> UploadNewVersionAndGetIdAsync(
            ByteString content,
            int chunkSize = FileTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            WotRegistryUploadResult result = await UploadNewVersionAndGetResultAsync(
                    content,
                    chunkSize,
                    ct)
                .ConfigureAwait(false);
            return result.VersionId;
        }

        /// <summary>
        /// Uploads a new Version and returns the exact Version node and id that
        /// received the bytes. A pending structural Version is claimed through
        /// an atomic server-side content-less write when available. Servers
        /// without that optional capability cause a new Version to be allocated.
        /// </summary>
        public async ValueTask<WotRegistryUploadResult> UploadNewVersionAndGetResultAsync(
            ByteString content,
            int chunkSize = FileTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkSize),
                    "Chunk size must be positive.");
            }
            if (m_pendingStructuralVersion)
            {
                WotRegistryUploadResult? filled = await TryFillPendingVersionAsync(
                        content,
                        chunkSize,
                        ct)
                    .ConfigureAwait(false);
                if (filled is not null)
                {
                    return filled;
                }
            }

            WotRegistryUploadResult allocated = await AllocateAndUploadAsync(
                    content,
                    chunkSize,
                    ct)
                .ConfigureAwait(false);
            if (m_pendingStructuralVersion)
            {
                m_pendingStructuralVersion = false;
                HasContent = null;
            }
            return allocated;
        }

        private async ValueTask<WotRegistryUploadResult?> TryFillPendingVersionAsync(
            ByteString content,
            int chunkSize,
            CancellationToken ct)
        {
            NodeId nodeId;
            string assignedVersionId;
            uint fileHandle;
            try
            {
                (nodeId, assignedVersionId, fileHandle) = await m_groupProxy
                    .CreateResourceAsync(
                        ResourceId,
                        VersionId,
                        requestFileOpen: true,
                        ct)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadNodeIdExists)
            {
                return null;
            }

            if (fileHandle == 0 ||
                nodeId != ResourceNodeId ||
                !string.Equals(assignedVersionId, VersionId, StringComparison.Ordinal))
            {
                if (fileHandle != 0)
                {
                    await CreateProxy(nodeId)
                        .CloseAsync(fileHandle, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                return null;
            }

            await CreateProxy(nodeId)
                .WriteDocumentAsync(fileHandle, content, chunkSize, ct)
                .ConfigureAwait(false);
            if (content.Length > 0)
            {
                m_pendingStructuralVersion = false;
                HasContent = true;
            }
            return new WotRegistryUploadResult(nodeId, assignedVersionId);
        }

        private async ValueTask<WotRegistryUploadResult> AllocateAndUploadAsync(
            ByteString content,
            int chunkSize,
            CancellationToken ct)
        {
            (NodeId nodeId, string assignedVersionId, uint fileHandle) = await m_groupProxy
                .CreateResourceAsync(
                    ResourceId,
                    versionId: string.Empty,
                    requestFileOpen: true,
                    ct)
                .ConfigureAwait(false);
            if (fileHandle == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "CreateResource did not return the requested write handle.");
            }
            await CreateProxy(nodeId)
                .WriteDocumentAsync(fileHandle, content, chunkSize, ct)
                .ConfigureAwait(false);
            return new WotRegistryUploadResult(nodeId, assignedVersionId);
        }

        /// <summary>
        /// Uploads the contents of <paramref name="content"/> through the
        /// inherited <c>FileType</c> primitives. The stream is read
        /// sequentially until end-of-stream; the caller retains ownership
        /// of <paramref name="content"/> and is responsible for disposing
        /// it. Closing the write handle commits the buffer as a new
        /// resource version.
        /// </summary>
        public async ValueTask UploadNewVersionAsync(
            Stream content,
            int chunkSize = FileTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            _ = await UploadNewVersionAndGetResultAsync(content, chunkSize, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Uploads a stream as a new Version and returns the Version id that
        /// received the bytes. A pending structural Version is filled in place
        /// only when the server supports the atomic content-less write capability.
        /// </summary>
        public async ValueTask<string> UploadNewVersionAndGetIdAsync(
            Stream content,
            int chunkSize = FileTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            WotRegistryUploadResult result = await UploadNewVersionAndGetResultAsync(
                    content,
                    chunkSize,
                    ct)
                .ConfigureAwait(false);
            return result.VersionId;
        }

        /// <summary>
        /// Uploads a stream as a new Version and returns the exact Version node
        /// and id that received the bytes.
        /// </summary>
        public async ValueTask<WotRegistryUploadResult> UploadNewVersionAndGetResultAsync(
            Stream content,
            int chunkSize = FileTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            if (content is null)
            {
                throw new ArgumentNullException(nameof(content));
            }
            if (!content.CanRead)
            {
                throw new ArgumentException("Stream must be readable.", nameof(content));
            }
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chunkSize),
                    "Chunk size must be positive.");
            }
            if (m_pendingStructuralVersion)
            {
                using var replay = new MemoryStream();
                await content.CopyToAsync(replay, chunkSize, ct).ConfigureAwait(false);
                return await UploadNewVersionAndGetResultAsync(
                        ByteString.From(replay.ToArray()),
                        chunkSize,
                        ct)
                    .ConfigureAwait(false);
            }

            (NodeId nodeId, string assignedVersionId, uint fileHandle) = await m_groupProxy
                .CreateResourceAsync(
                    ResourceId,
                    versionId: string.Empty,
                    requestFileOpen: true,
                    ct)
                .ConfigureAwait(false);
            if (fileHandle == 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "CreateResource did not return the requested write handle.");
            }
            WoTDocumentTypeClient proxy = CreateProxy(nodeId);
            try
            {
                await content.CopyStreamInChunksAsync(
                        chunkSize,
                        (chunk, token) => proxy.WriteAsync(
                            fileHandle,
                            ByteString.From(chunk),
                            token),
                        ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                await proxy.CloseAsync(fileHandle, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            return new WotRegistryUploadResult(nodeId, assignedVersionId);
        }

        /// <summary>
        /// Downloads the currently persisted resource content (the active
        /// / default version) via chunked <c>Read</c> calls.
        /// <para>
        /// A WoT document resource is an xRegistry <c>ResourceType</c>, so this reuses the shared
        /// <see cref="ResourceTypeClientExtensions.ReadDocumentAsync"/> helper directly on the
        /// generated proxy rather than round-tripping the document through a <c>byte[]</c>.
        /// </para>
        /// </summary>
        public ValueTask<ByteString> DownloadAsync(
            int chunkSize = FileTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            return Proxy.ReadDocumentAsync(chunkSize, ct);
        }

        /// <summary>
        /// Downloads the currently persisted resource content and writes
        /// it sequentially into <paramref name="destination"/>. The
        /// caller retains ownership of <paramref name="destination"/> and
        /// is responsible for disposing it.
        /// </summary>
        public ValueTask DownloadToAsync(
            Stream destination,
            int chunkSize = FileTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            return Proxy.DownloadToAsync(destination, chunkSize, ct);
        }

        private WoTDocumentTypeClient CreateProxy(NodeId nodeId)
        {
            return Kind == WoTDocumentKindEnum.ThingModel
                ? new ThingModelFileTypeClient(Session, nodeId, Telemetry)
                : new ThingDescriptionFileTypeClient(Session, nodeId, Telemetry);
        }

        internal void MarkPendingStructuralVersion()
        {
            m_pendingStructuralVersion = true;
            HasContent = false;
        }

        private static bool IsUnavailableContentState(StatusCode statusCode)
        {
            return statusCode == StatusCodes.BadNoMatch ||
                statusCode == StatusCodes.BadNodeIdUnknown ||
                statusCode == StatusCodes.BadAttributeIdInvalid ||
                statusCode == StatusCodes.BadNotReadable;
        }

        private readonly GroupTypeClient m_groupProxy;
        private bool m_pendingStructuralVersion;
    }

    /// <summary>
    /// Identifies the exact Version node that received a registry upload.
    /// </summary>
    public sealed class WotRegistryUploadResult
    {
        internal WotRegistryUploadResult(NodeId resourceNodeId, string versionId)
        {
            ResourceNodeId = resourceNodeId;
            VersionId = versionId;
        }

        /// <summary>
        /// Gets the NodeId of the concrete Version that received the bytes.
        /// </summary>
        public NodeId ResourceNodeId { get; }

        /// <summary>
        /// Gets the server-assigned Version id.
        /// </summary>
        public string VersionId { get; }
    }
}
