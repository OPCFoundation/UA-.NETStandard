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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.OpenUsd.Client;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// Asset metadata is separate from content so inspection cannot accidentally acquire files.
/// </summary>
internal sealed record OpenUsdCompanionAsset(
    NodeId NodeId,
    string Identifier,
    OpenUsdAssetKind Kind,
    ulong Size,
    ByteString Digest,
    OpenUsdDigestAlgorithm DigestAlgorithm);

internal sealed record OpenUsdVerifiedAsset(string Identifier, ByteString Content);

internal sealed record OpenUsdCompanionBindingValue(string PrimPath, string PropertyName, Variant Value);

/// <summary>
/// Owns connector-local resources, but never the primary session, subscriptions or a remote session.
/// </summary>
internal interface IOpenUsdCompanionReader : IAsyncDisposable
{
    Task<ArrayOf<OpenUsdConnector.RepresentationInfo>> DiscoverAsync(CancellationToken cancellationToken);

    Task<string> ReadNameAsync(NodeId nodeId, CancellationToken cancellationToken);

    Task<ArrayOf<OpenUsdCompanionAsset>> ReadAssetsAsync(
        NodeId stageNodeId, int maximum, CancellationToken cancellationToken);

    Task<DataValue> ReadValueAsync(NodeId nodeId, CancellationToken cancellationToken);

    ValueTask<ByteString> ReadAssetAsync(NodeId assetNodeId, CancellationToken cancellationToken);
}

/// <summary>
/// Uses the existing connector for binding discovery and the Part 5 file client for a selected stage's assets.
/// The connector's registry-wide FetchServedAssets API is deliberately not used for a selected-instance export.
/// </summary>
internal sealed class OpenUsdCompanionReader : IOpenUsdCompanionReader
{
    public OpenUsdCompanionReader(CompanionContext context, OpenUsdConnectorOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        m_context = context;
        m_options = options;
        m_connector = new OpenUsdConnectorFactory(context.Telemetry)
            .Create(context.Session, new MockUsdSink(), options);
    }

    public async Task<ArrayOf<OpenUsdConnector.RepresentationInfo>> DiscoverAsync(CancellationToken cancellationToken)
    {
        List<OpenUsdConnector.RepresentationInfo> representations =
            await m_connector.DiscoverAllRepresentationsAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        CellCompanionSupport.CheckCount(representations.Count, m_context.MaxTargets, "OpenUSD representations");
        return [.. representations];
    }

    public Task<string> ReadNameAsync(NodeId nodeId, CancellationToken cancellationToken)
    {
        return m_connector.ReadBrowseNameAsync(nodeId, cancellationToken);
    }

    public async Task<ArrayOf<OpenUsdCompanionAsset>> ReadAssetsAsync(
        NodeId stageNodeId,
        int maximum,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (stageNodeId.IsNull)
        {
            return [];
        }
        int namespaceIndex = m_context.Session.NamespaceUris.GetIndex("http://opcfoundation.org/UA/OpenUSD/");
        (ArrayOf<NodeId> folders, ArrayOf<ServiceResult> errors) =
            await m_context.Session.FindComponentIdsAsync(
                stageNodeId, [$"/{namespaceIndex}:Assets"], cancellationToken).ConfigureAwait(false);
        if (errors.Count == 1 &&
            (errors[0].StatusCode == StatusCodes.BadNoMatch ||
             errors[0].StatusCode == StatusCodes.BadNotFound ||
             errors[0].StatusCode == StatusCodes.BadNodeIdUnknown))
        {
            return [];
        }
        RequireGood(errors);
        if (folders.Count != 1 || folders[0].IsNull)
        {
            throw new ServiceResultException(StatusCodes.BadUnexpectedError, "The stage Assets folder is unresolved.");
        }
        ArrayOf<ReferenceDescription> references = await BrowseAssetsAsync(folders[0], maximum, cancellationToken)
            .ConfigureAwait(false);
        var files = new FileSystemClient(m_context.Session, folders[0], new FileSystemClientOptions
        {
            ChunkSize = 8192,
            MaxBufferedReadSize = m_options.MaxAssetBytes,
            PathCacheSize = maximum
        });
        var assets = new List<OpenUsdCompanionAsset>();
        for (int index = 0; index < references.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReferenceDescription reference = references[index];
            NodeId nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, m_context.Session.NamespaceUris);
            NodeId typeId = ExpandedNodeId.ToNodeId(reference.TypeDefinition, m_context.Session.NamespaceUris);
            if (reference.NodeId.ServerIndex != 0 || nodeId.IsNull || typeId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "Asset references must resolve locally.");
            }
            if (!await m_context.Session.NodeCache.IsTypeOfAsync(
                typeId, Opc.Ua.ObjectTypeIds.FileType, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }
            UaFileInfo file = await files.GetFileAsync(UaPath.FormatSegment(reference.BrowseName), cancellationToken)
                .ConfigureAwait(false);
            if (file.NodeId != nodeId)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The served asset changed during discovery.");
            }
            await file.RefreshAsync(cancellationToken).ConfigureAwait(false);
            (ArrayOf<NodeId> properties, ArrayOf<ServiceResult> propertyErrors) =
                await m_context.Session.FindComponentIdsAsync(nodeId,
                    [
                        $"/{namespaceIndex}:AssetIdentifier",
                        $"/{namespaceIndex}:AssetKind",
                        $"/{namespaceIndex}:Digest",
                        $"/{namespaceIndex}:DigestAlgorithm"
                    ], cancellationToken).ConfigureAwait(false);
            RequireGood(propertyErrors);
            if (properties.Count != 4)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError, "Incomplete served asset metadata.");
            }
            (ArrayOf<DataValue> values, ArrayOf<ServiceResult> readErrors) =
                await m_context.Session.ReadValuesAsync(properties, cancellationToken).ConfigureAwait(false);
            RequireGood(readErrors);
            if (values.Count != 4)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError, "Incomplete served asset values.");
            }
            for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
            {
                if (!StatusCode.IsGood(values[valueIndex].StatusCode))
                {
                    throw new ServiceResultException(values[valueIndex].StatusCode);
                }
            }
            if (!values[0].WrappedValue.TryGetValue(out string? identifier) || string.IsNullOrEmpty(identifier) ||
                !values[1].WrappedValue.TryGetValue(out OpenUsdAssetKind kind) ||
                !values[2].WrappedValue.TryGetValue(out ByteString digest) ||
                !values[3].WrappedValue.TryGetValue(out OpenUsdDigestAlgorithm algorithm))
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Invalid served asset metadata types.");
            }
            assets.Add(new OpenUsdCompanionAsset(
                nodeId, identifier, kind, file.Size, digest, algorithm));
            m_files[nodeId] = file;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return [.. assets];
    }

    public Task<DataValue> ReadValueAsync(NodeId nodeId, CancellationToken cancellationToken)
    {
        return m_context.Session.ReadValueAsync(nodeId, cancellationToken);
    }

    public async ValueTask<ByteString> ReadAssetAsync(NodeId assetNodeId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!m_files.TryGetValue(assetNodeId, out UaFileInfo? file))
        {
            throw new ServiceResultException(StatusCodes.BadNotFound, "Read asset metadata on this operation first.");
        }
        // ReadAllBytes enforces MaxBufferedReadSize during the stream and closes its Part 5
        // handle with an uncancelled token, including cancellation and oversized-stream failures.
        byte[] bytes = await file.ReadAllBytesAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new ByteString(bytes);
    }

    public async ValueTask DisposeAsync()
    {
        m_files.Clear();
        await m_connector.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<ArrayOf<ReferenceDescription>> BrowseAssetsAsync(
        NodeId folder,
        int maximum,
        CancellationToken cancellationToken)
    {
        var browser = new Browser(m_context.Session)
        {
            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            NodeClassMask = (uint)NodeClass.Object,
            MaxReferencesReturned = (uint)Math.Min(maximum + 1, 128),
            ContinueUntilDone = false
        };
        bool exceeded = false;
        browser.MoreReferences += (_, args) =>
        {
            if (args.References.Count >= maximum)
            {
                exceeded = true;
                args.Cancel = true;
            }
        };
        ArrayOf<ReferenceDescription> references = await browser.BrowseAsync(folder, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (exceeded)
        {
            throw new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded, "The served asset registry exceeds the selected asset limit.");
        }
        CellCompanionSupport.CheckCount(references.Count, maximum, "Served asset references");
        return references;
    }

    private static void RequireGood(ArrayOf<ServiceResult> errors)
    {
        for (int index = 0; index < errors.Count; index++)
        {
            if (ServiceResult.IsBad(errors[index]))
            {
                throw new ServiceResultException(errors[index]);
            }
        }
    }

    private readonly CompanionContext m_context;
    private readonly OpenUsdConnectorOptions m_options;
    private readonly OpenUsdConnector m_connector;
    private readonly Dictionary<NodeId, UaFileInfo> m_files = [];
}
