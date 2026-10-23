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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.WotCon.Client;
using Wot = Opc.Ua.WotCon;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed class WotAssetDeleteTask : CompanionTaskInput
    {
        public WotAssetDeleteTask(RegistryLifecycleTask request, NodeId asset, NodeId file, ByteString digest)
        {
            Request = request;
            Asset = asset;
            File = file;
            m_digest = digest.Copy();
        }

        public RegistryLifecycleTask Request { get; }
        public NodeId Asset { get; }
        public NodeId File { get; }
        public ByteString Digest => m_digest.Copy();

        public override string Review =>
            $"Delete only asset {Asset} and its subtree on manager {Request.Target.NodeId}.\n" +
            $"WoT file: {File}; SHA-256: {Convert.ToHexString(m_digest.Span)}.\n" +
            "The server may stop this asset's connections. No other asset, physical device or trust store is deleted.";

        private readonly ByteString m_digest;
    }

    internal sealed partial class WotCompanionProvider
    {
        private static async ValueTask<WotAssetDeleteTask> PrepareAssetDeleteAsync(
            CompanionContext context, RegistryLifecycleTask request, NodeId asset, CancellationToken cancellationToken)
        {
            IndustrialCompanionAccess.CheckFields(context, 2);
            (NodeId file, ByteString digest) = await ReadAssetEvidenceAsync(
                context, request.Target.NodeId, asset, cancellationToken).ConfigureAwait(false);
            return new WotAssetDeleteTask(request, asset, file, digest);
        }

        private static async ValueTask<CompanionOperationResult> ExecuteAssetDeleteAsync(
            CompanionContext context, WotAssetDeleteTask task, CancellationToken cancellationToken)
        {
            (NodeId file, ByteString digest) = await ReadAssetEvidenceAsync(
                context, task.Request.Target.NodeId, task.Asset, cancellationToken).ConfigureAwait(false);
            if (file != task.File || digest != task.Digest)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The selected asset changed. Prepare again.");
            }
            var manager = new WotConnectivityClient(context.Session, task.Request.Target.NodeId, context.Telemetry);
            await manager.DeleteAssetAsync(task.Asset, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new CompanionOperationResult(
                "The server accepted deletion of the reviewed asset subtree. " +
                "Refresh the manager to observe its absence.",
                [new("Requested asset deletion", Variant.From(task.Asset)),
                    new("Reviewed WoT file", Variant.From(task.File))]);
        }

        private static async ValueTask<(NodeId File, ByteString Digest)> ReadAssetEvidenceAsync(
            CompanionContext context, NodeId managerId, NodeId assetId, CancellationToken cancellationToken)
        {
            if (assetId.IsNull)
            {
                throw new ArgumentException("Select an existing asset NodeId.", nameof(assetId));
            }
            bool found = false;
            await foreach (ReferenceDescription child in IndustrialCompanionAccess.BrowseAsync(
                context, managerId, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
                NodeClass.Object, new IndustrialBrowseBudget(context), cancellationToken).ConfigureAwait(false))
            {
                if (IndustrialCompanionAccess.LocalId(context, child.NodeId) == assetId)
                {
                    if (found)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTooManyMatches, "The asset association is ambiguous.");
                    }
                    found = true;
                }
            }
            if (!found)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound, "The asset is not owned by the selected manager.");
            }
            var manager = new WotConnectivityClient(context.Session, managerId, context.Telemetry);
            WotAssetClient asset = await manager.OpenAssetAsync(assetId, cancellationToken).ConfigureAwait(false);
            await IndustrialCompanionAccess.RequireTargetAsync(context,
                new CompanionTarget("wot", asset.File.ObjectId, "Asset document", kAssetFileKind),
                "wot", [new IndustrialCompanionType(Wot.ObjectTypeIds.WoTAssetFileType, kAssetFileKind)],
                cancellationToken).ConfigureAwait(false);
            ByteString document = await IndustrialCompanionAccess.ReadDocumentAsync(
                asset.File, cancellationToken).ConfigureAwait(false);
            return (asset.File.ObjectId, ByteString.From(SHA256.HashData(document.Span)));
        }
    }
}
