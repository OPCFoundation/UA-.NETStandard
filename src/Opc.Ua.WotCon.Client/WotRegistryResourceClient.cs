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

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Encoders;
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
    /// Closing a write handle commits the buffered content as a new
    /// resource version.
    /// </summary>
    public sealed class WotRegistryResourceClient
    {
        internal WotRegistryResourceClient(
            ISession session,
            NodeId resourceNodeId,
            string groupId,
            string resourceId,
            WoTDocumentKindEnum kind,
            WoTDocumentTypeClient proxy,
            ITelemetryContext telemetry)
        {
            Session = session;
            ResourceNodeId = resourceNodeId;
            GroupId = groupId;
            ResourceId = resourceId;
            Kind = kind;
            Proxy = proxy;
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
            CallMethodResult result = response.Results[0];
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
        /// Calls <c>Delete</c> on the resource.
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
        public ValueTask UploadNewVersionAsync(
            ByteString content,
            int chunkSize = FileTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            return Proxy.UploadAsync(content, chunkSize: chunkSize, ct: ct);
        }

        /// <summary>
        /// Uploads the contents of <paramref name="content"/> through the
        /// inherited <c>FileType</c> primitives. The stream is read
        /// sequentially until end-of-stream; the caller retains ownership
        /// of <paramref name="content"/> and is responsible for disposing
        /// it. Closing the write handle commits the buffer as a new
        /// resource version.
        /// </summary>
        public ValueTask UploadNewVersionAsync(
            Stream content,
            int chunkSize = FileTypeClientExtensions.DefaultChunkSize,
            CancellationToken ct = default)
        {
            return Proxy.UploadAsync(content, chunkSize: chunkSize, ct: ct);
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
    }
}
