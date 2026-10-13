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
using System.ComponentModel;
using System.Formats.Asn1;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Mcp
{
    public sealed partial class GdsCertificateTools
    {
        /// <summary>
        /// Reads public file and update metadata from a real TrustListType.
        /// </summary>
        [McpServerTool(Name = "certificate_inspect_trust_list", ReadOnly = true, Destructive = false)]
        [Description("Inspect a typed TrustList's size, masks/update metadata and writable flags. Returns original " +
            "DataValue statuses/timestamps; does not open a file handle or alter trust.")]
        public Task<CallToolResult> InspectTrustListAsync(
            [Description(GdsToolDescriptions.TrustListNodeId)]
            string trustListNodeId,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                GdsEndpoint endpoint = await m_access.EndpointAsync(
                    trustListNodeId, GdsTarget.TrustList, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                return await McpModelAccess.PropertiesAsync(endpoint.Session, endpoint.Proxy.ObjectId,
                    [new QualifiedName("Size"), new QualifiedName("Writable"), new QualifiedName("UserWritable"),
                        new QualifiedName("OpenCount"), new QualifiedName("LastUpdateTime"),
                        new QualifiedName("UpdateFrequency"), new QualifiedName("ActivityTimeout")], token)
                    .ConfigureAwait(false);
            }, ct);
        }

        /// <summary>
        /// Reads and validates a bounded trust list before publishing public binary bytes.
        /// </summary>
        [McpServerTool(Name = "certificate_download_trust_list", ReadOnly = true, Destructive = false)]
        [Description("Download a public TrustListDataType binary artifact beneath TransferRoot using masks 1..15. " +
            "Returns actual open/read/close outcomes and counts. Handles are always closed and never exposed.")]
        public Task<CallToolResult> DownloadTrustListAsync(
            [Description(GdsToolDescriptions.TrustListNodeId)]
            string trustListNodeId,
            [Description("Output path beneath TransferRoot for the public UA-binary TrustListDataType artifact, " +
                "e.g. 'trust-list.bin'; contains certificates and CRLs, never private keys.")]
            string artifactPath,
            [Description("Trust-list bitmask (1..15): 1 trusted certificates, 2 trusted CRLs, " +
                "4 issuer certificates, 8 issuer CRLs. Combine bits; default 15 downloads all lists.")]
            uint masks = 15,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(masks, 1u);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(masks, 15u);
                m_access.Transfers.ValidateDownloadPath(artifactPath);
                GdsEndpoint endpoint = await m_access.EndpointAsync(
                    trustListNodeId, GdsTarget.TrustList, sessionName, token).ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                CallMethodResult opened = await endpoint.CallAsync(MethodIds.TrustListType_OpenWithMasks,
                    [new Variant(masks)], token).ConfigureAwait(false);
                JsonObject result = endpoint.Result(opened);
                if (StatusCode.IsBad(opened.StatusCode))
                {
                    return result;
                }
                uint handle = GdsJson.UInt32(opened, 0);
                using var buffer = new McpBoundedMemoryStream(m_access.Options.MaxArtifactBytes, token);
                bool complete = false;
                try
                {
                    for (int reads = 0; reads < 4096; reads++)
                    {
                        CallMethodResult read = await endpoint.CallAsync(MethodIds.FileType_Read,
                            [new Variant(handle), new Variant(16 * 1024)], token).ConfigureAwait(false);
                        result["readStatusCodeValue"] = read.StatusCode.Code;
                        if (StatusCode.IsBad(read.StatusCode))
                        {
                            result["error"] = true;
                            break;
                        }
                        ByteString chunk = GdsJson.Bytes(read, 0);
                        if (chunk.IsEmpty)
                        {
                            complete = true;
                            break;
                        }
                        await buffer.WriteAsync(chunk.Memory, token).ConfigureAwait(false);
                    }
                    if (!complete)
                    {
                        result["error"] = true;
                        result["downloadState"] = "incomplete";
                    }
                }
                catch (Exception failure) when (failure is ServiceResultException or IOException)
                {
                    result["error"] = true;
                    result["downloadState"] = "readFailedOrLimitExceeded";
                    result["readStatusCodeValue"] = failure is ServiceResultException service
                        ? service.StatusCode.Code : StatusCodes.BadEncodingLimitsExceeded.Code;
                }
                finally
                {
                    await CloseTrustListAsync(endpoint, handle, result).ConfigureAwait(false);
                }
                if (complete && !McpModelAccess.HasError(result))
                {
                    buffer.Position = 0;
                    TrustListDataType list = DecodeTrustList(buffer, endpoint.Context);
                    using McpBoundedMemoryStream encoded = EncodeTrustList(list, endpoint.Context);
                    result["artifact"] = await m_access.Transfers.DownloadAsync(artifactPath,
                        async (output, cancellation) =>
                            await encoded.CopyToAsync(output, cancellation).ConfigureAwait(false), token)
                        .ConfigureAwait(false);
                    result["specifiedLists"] = list.SpecifiedLists;
                    result["trustedCertificateCount"] = list.TrustedCertificates.Count;
                    result["issuerCertificateCount"] = list.IssuerCertificates.Count;
                    result["trustedCrlCount"] = list.TrustedCrls.Count;
                    result["issuerCrlCount"] = list.IssuerCrls.Count;
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// Stages exactly one validated public trust list, preserving CloseAndUpdate separately from ApplyChanges.
        /// </summary>
        [McpServerTool(Name = "certificate_stage_trust_list", ReadOnly = false, Destructive = true)]
        [Description("Stage a bounded public TrustListDataType binary file for a verified configuration child. " +
            "Returns CloseAndUpdate status/applyChangesRequired. Never applies changes or exports a file handle.")]
        public Task<CallToolResult> StageTrustListAsync(
            [Description(GdsToolDescriptions.ConfigurationNodeId)]
            string configurationNodeId,
            [Description(GdsToolDescriptions.TrustListNodeId +
                " It must be a verified descendant of configurationNodeId.")]
            string trustListNodeId,
            [Description("Existing public UA-binary TrustListDataType file beneath TransferRoot, " +
                "e.g. 'trust-list.bin'; its SpecifiedLists mask selects the certificate/CRL lists to update.")]
            string artifactPath,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                GdsEndpoint configuration = await PushAsync(configurationNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable configurationScope = configuration.ConfigureAwait(false);
                GdsEndpoint endpoint = await OwnedTrustListAsync(configuration, trustListNodeId, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                ByteString bytes = await m_access.PublicInputAsync(artifactPath, token).ConfigureAwait(false);
                using var input = new MemoryStream(bytes.ToArray()!, writable: false);
                TrustListDataType list = DecodeTrustList(input, endpoint.Context);
                using McpBoundedMemoryStream encoded = EncodeTrustList(list, endpoint.Context);
                using GdsOperationState.PushOperation owner = EnterPush(configuration);
                CallMethodResult opened = await endpoint.CallAsync(MethodIds.FileType_Open,
                    [new Variant((byte)((int)OpenFileMode.Write | (int)OpenFileMode.EraseExisting))], token)
                    .ConfigureAwait(false);
                JsonObject result = endpoint.Result(opened);
                result["applyInvoked"] = false;
                if (StatusCode.IsBad(opened.StatusCode))
                {
                    owner.Refused();
                    return result;
                }
                uint handle = GdsJson.UInt32(opened, 0);
                bool closed = false;
                try
                {
                    byte[] chunk = new byte[16 * 1024];
                    while (true)
                    {
                        int count = await encoded.ReadAsync(chunk, token).ConfigureAwait(false);
                        if (count == 0)
                        {
                            break;
                        }
                        CallMethodResult write = await endpoint.CallAsync(MethodIds.FileType_Write,
                            [new Variant(handle), new Variant(ByteString.From(chunk.AsSpan(0, count).ToArray()))],
                            token)
                            .ConfigureAwait(false);
                        result["writeStatusCodeValue"] = write.StatusCode.Code;
                        if (StatusCode.IsBad(write.StatusCode))
                        {
                            result["error"] = true;
                            return result;
                        }
                    }
                    CallMethodResult update = await endpoint.CallAsync(
                        MethodIds.TrustListType_CloseAndUpdate, [new Variant(handle)], token).ConfigureAwait(false);
                    result["closeAndUpdate"] = GdsJson.Method(update);
                    if (StatusCode.IsBad(update.StatusCode))
                    {
                        result["error"] = true;
                    }
                    else
                    {
                        closed = true;
                        result["applyChangesRequired"] = GdsJson.Boolean(update, 0);
                        result["remoteOperation"] = "closeAndUpdateCompleted";
                    }
                    return result;
                }
                finally
                {
                    if (!closed)
                    {
                        await CloseTrustListAsync(endpoint, handle, result).ConfigureAwait(false);
                    }
                }
            }, ct);
        }

        /// <summary>
        /// Adds a single public certificate to the selected trusted/issuer list.
        /// </summary>
        [McpServerTool(Name = "certificate_add_to_trust_list", ReadOnly = false, Destructive = true)]
        [Description("Add one public DER certificate to a verified TrustList child. isTrusted selects trusted vs " +
            "issuer certificates. Returns actual status; any ApplyChanges is a separate explicit operation.")]
        public Task<CallToolResult> AddToTrustListAsync(
            [Description(GdsToolDescriptions.ConfigurationNodeId)]
            string configurationNodeId,
            [Description(GdsToolDescriptions.TrustListNodeId +
                " It must be a verified descendant of configurationNodeId.")]
            string trustListNodeId,
            [Description(GdsToolDescriptions.PublicCertificatePath)]
            string certificatePath,
            [Description("True adds the certificate to the trusted list; false adds it to the issuer list. " +
                "This selects the remote list, not host trust.")]
            bool isTrusted,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                GdsEndpoint configuration = await PushAsync(configurationNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable configurationScope = configuration.ConfigureAwait(false);
                GdsEndpoint endpoint = await OwnedTrustListAsync(configuration, trustListNodeId, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                ByteString certificate = await PublicCertificateAsync(certificatePath, token).ConfigureAwait(false);
                using GdsOperationState.PushOperation owner = EnterPush(configuration);
                CallMethodResult call = await endpoint.CallAsync(MethodIds.TrustListType_AddCertificate,
                    [new Variant(certificate), new Variant(isTrusted)], token).ConfigureAwait(false);
                if (StatusCode.IsBad(call.StatusCode))
                {
                    owner.Refused();
                }
                JsonObject result = endpoint.Result(call);
                result["applyInvoked"] = false;
                return result;
            }, ct);
        }

        /// <summary>
        /// Removes one certificate by the server's thumbprint convention.
        /// </summary>
        [McpServerTool(Name = "certificate_remove_from_trust_list", ReadOnly = false, Destructive = true)]
        [Description("Remove one trusted/issuer certificate by its server thumbprint from a verified TrustList. " +
            "Returns actual status; does not remove unrelated entries, auto-apply changes or alter host trust.")]
        public Task<CallToolResult> RemoveFromTrustListAsync(
            [Description(GdsToolDescriptions.ConfigurationNodeId)]
            string configurationNodeId,
            [Description(GdsToolDescriptions.TrustListNodeId +
                " It must be a verified descendant of configurationNodeId.")]
            string trustListNodeId,
            [Description("Exact hexadecimal certificate thumbprint in the target server's RemoveCertificate " +
                "convention; not a NodeId, file path or an assumed interchangeable local fingerprint.")]
            string thumbprint,
            [Description("True removes from the trusted certificate list; false removes from the issuer list. " +
                "The other list and host trust are not selected.")]
            bool isTrusted,
            [Description(GdsToolDescriptions.SessionName)]
            string? sessionName = null,
            CancellationToken ct = default)
        {
            return m_access.ExecuteAsync(async token =>
            {
                GdsAccess.Text(thumbprint);
                GdsEndpoint configuration = await PushAsync(configurationNodeId, sessionName, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable configurationScope = configuration.ConfigureAwait(false);
                GdsEndpoint endpoint = await OwnedTrustListAsync(configuration, trustListNodeId, token)
                    .ConfigureAwait(false);
                await using ConfiguredAsyncDisposable endpointScope = endpoint.ConfigureAwait(false);
                using GdsOperationState.PushOperation owner = EnterPush(configuration);
                CallMethodResult call = await endpoint.CallAsync(MethodIds.TrustListType_RemoveCertificate,
                    [new Variant(thumbprint), new Variant(isTrusted)], token).ConfigureAwait(false);
                if (StatusCode.IsBad(call.StatusCode))
                {
                    owner.Refused();
                }
                JsonObject result = endpoint.Result(call);
                result["applyInvoked"] = false;
                return result;
            }, ct);
        }

        /// <summary>
        /// Proves trust-list ancestry before sharing the configuration's push-transaction ownership.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private async ValueTask<GdsEndpoint> OwnedTrustListAsync(
            GdsEndpoint configuration, string trustListNodeId, CancellationToken ct)
        {
            NodeId requested = GdsAccess.Node(trustListNodeId);
            await foreach (ReferenceDescription reference in McpModelAccess.DiscoverAsync(
                configuration.Session, configuration.Proxy.ObjectId,
                ObjectTypeIds.TrustListType, maxDepth: 4, ct: ct).ConfigureAwait(false))
            {
                if (McpModelAccess.ModelNode(configuration.Session, reference.NodeId) == requested)
                {
                    return new GdsEndpoint(configuration.Info, configuration.RequestContext, new TrustListTypeClient(
                        configuration.Session, requested, m_access.Sessions.Telemetry), ownsContext: false);
                }
            }
            throw new ServiceResultException(StatusCodes.BadUserAccessDenied,
                "The selected trust list is not a verified child of this server configuration.");
        }

        /// <summary>
        /// Closes only the handle opened by this call using independent bounded cleanup.
        /// A cleanup refusal is attached rather than replacing an earlier read/write failure.
        /// </summary>
        private async ValueTask CloseTrustListAsync(GdsEndpoint endpoint, uint handle, JsonObject result)
        {
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30), m_access.Time);
                CallMethodResult close = await endpoint.CallAsync(
                    MethodIds.FileType_Close, [new Variant(handle)], cleanup.Token).ConfigureAwait(false);
                result["closeStatusCodeValue"] = close.StatusCode.Code;
                if (StatusCode.IsBad(close.StatusCode))
                {
                    result["error"] = true;
                    result["remoteHandleCleanup"] = "failed";
                }
            }
            catch (Exception failure) when (
                failure is ServiceResultException or OperationCanceledException or IOException)
            {
                result["error"] = true;
                result["remoteHandleCleanup"] = "failed";
                result["closeStatusCodeValue"] = failure is ServiceResultException service
                    ? service.StatusCode.Code : StatusCodes.BadTimeout.Code;
            }
        }

        /// <summary>
        /// Decodes the standard binary structure with no trailing payload and validates every public element.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private TrustListDataType DecodeTrustList(Stream source, IServiceMessageContext context)
        {
            var result = new TrustListDataType();
            var boundedContext = new ServiceMessageContext(context, context.Telemetry)
            {
                MaxArrayLength = 500,
                MaxByteStringLength = m_access.Options.MaxArtifactBytes,
                MaxStringLength = 2048,
                MaxMessageSize = m_access.Options.MaxArtifactBytes,
                MaxEncodingNestingLevels = 16
            };
            using (var decoder = new BinaryDecoder(source, boundedContext, leaveOpen: true))
            {
                result.Decode(decoder);
            }
            if (source.Position != source.Length || result.SpecifiedLists > 15)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError);
            }
            foreach (ByteString certificate in result.TrustedCertificates)
            {
                _ = GdsJson.Certificate(certificate);
            }
            foreach (ByteString certificate in result.IssuerCertificates)
            {
                _ = GdsJson.Certificate(certificate);
            }
            ValidateCrls(result.TrustedCrls);
            ValidateCrls(result.IssuerCrls);
            return result;
        }

        /// <summary>
        /// Serializes already validated public lists through the shared allocation ceiling.
        /// </summary>
        private McpBoundedMemoryStream EncodeTrustList(TrustListDataType list, IServiceMessageContext context)
        {
            var buffer = new McpBoundedMemoryStream(m_access.Options.MaxArtifactBytes);
            try
            {
                using (var encoder = new BinaryEncoder(buffer, context, leaveOpen: true))
                {
                    list.Encode(encoder);
                }
                buffer.Position = 0;
                return buffer;
            }
            catch (Exception failure) when (failure is IOException or ServiceResultException or ArgumentException)
            {
                buffer.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Rejects private-key containers and trailing material in a purported public CRL.
        /// </summary>
        private static void ValidateCrls(ArrayOf<ByteString> crls)
        {
            foreach (ByteString bytes in crls)
            {
                var reader = new AsnReader(bytes.Memory, AsnEncodingRules.DER);
                _ = reader.ReadEncodedValue();
                reader.ThrowIfNotEmpty();
                _ = new X509CRL(bytes.ToArray()!);
            }
        }
    }
}
