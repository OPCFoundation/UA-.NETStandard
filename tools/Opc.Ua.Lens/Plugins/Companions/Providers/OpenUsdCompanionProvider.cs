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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// Inspects draft OpenUSD bindings and exports only bounded, digest-verified served assets from the primary session.
/// </summary>
internal sealed class OpenUsdCompanionProvider : ICompanionProvider
{
    public OpenUsdCompanionProvider()
        : this(
            static (context, options) => new OpenUsdCompanionReader(context, options),
            new OpenUsdCompanionExporter())
    {
    }

    public OpenUsdCompanionProvider(
        Func<CompanionContext, OpenUsdConnectorOptions, IOpenUsdCompanionReader> createReader,
        IOpenUsdCompanionExporter exporter)
    {
        m_createReader = createReader ?? throw new ArgumentNullException(nameof(createReader));
        m_exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
    }

    public CompanionDescriptor Descriptor { get; } = new(
        "openusd",
        "OpenUSD representations",
        "http://opcfoundation.org/UA/OpenUSD/",
        "Draft OpenUSD bindings; experimental, not ratified");

    public async ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
        CompanionContext context,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetime =
            CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
        IOpenUsdCompanionReader reader = m_createReader(context, CreateOptions());
        await using (reader.ConfigureAwait(false))
        {
            ArrayOf<OpenUsdConnector.RepresentationInfo> representations = await reader.DiscoverAsync(lifetime.Token)
                .ConfigureAwait(false);
            CellCompanionSupport.CheckCount(representations.Count, context.MaxTargets, "OpenUSD representations");
            var targets = new List<CompanionTarget>();
            var visited = new HashSet<NodeId>();
            for (int index = 0; index < representations.Count; index++)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                OpenUsdConnector.RepresentationInfo representation = representations[index];
                ValidateRepresentation(representation, context);
                string name = await reader.ReadNameAsync(representation.NodeId, lifetime.Token).ConfigureAwait(false);
                CellCompanionSupport.AddTarget(
                    targets,
                    visited,
                    new CompanionTarget(
                        Descriptor.Id, representation.NodeId,
                        string.IsNullOrEmpty(name) ? representation.NodeId.ToString() : name,
                        "OpenUsdRepresentation"),
                    context.MaxTargets);
            }
            lifetime.Token.ThrowIfCancellationRequested();
            return [.. targets];
        }
    }

    public async ValueTask<CompanionInspection> InspectAsync(
        CompanionContext context,
        CompanionTarget target,
        CancellationToken cancellationToken)
    {
        ValidateTarget(context, target);
        using CancellationTokenSource lifetime =
            CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
        IOpenUsdCompanionReader reader = m_createReader(context, CreateOptions());
        await using (reader.ConfigureAwait(false))
        {
            OpenUsdConnector.RepresentationInfo representation =
                await FindAsync(reader, context, target.NodeId, lifetime.Token).ConfigureAwait(false);
            ArrayOf<OpenUsdCompanionAsset> assets = await reader.ReadAssetsAsync(
                representation.StageNodeId, AssetLimit(context), lifetime.Token).ConfigureAwait(false);
            CellCompanionSupport.CheckCount(assets.Count, AssetLimit(context), "Served assets");
            var fields = new CellCompanionFields(context.MaxFields);
            fields.Add("Representation", Variant.From(representation.NodeId));
            fields.Add("Stage", Variant.From(representation.StageNodeId));
            fields.AddText("Prim path", representation.PrimPath);
            fields.AddText("Root layer", representation.RootLayerIdentifier);
            fields.Add("Advertised root digest", Variant.From(representation.RootLayerDigest));
            fields.AddText("Digest algorithm", representation.DigestAlgorithm.ToString());
            fields.AddText("Asset verification", "Not performed during inspection");
            fields.Add("Binding count", Variant.From(representation.Bindings.Count));
            for (int index = 0; index < representation.Bindings.Count; index++)
            {
                lifetime.Token.ThrowIfCancellationRequested();
                OpenUsdConnector.BindingInfo binding = representation.Bindings[index];
                string prefix = $"Binding {index + 1}";
                fields.Add($"{prefix} source", Variant.From(binding.SourceNodeId));
                fields.AddText($"{prefix} prim", binding.PrimPath);
                fields.AddText($"{prefix} property", binding.PropertyName);
                fields.AddText($"{prefix} intent", binding.Intent.ToString());
                fields.AddText($"{prefix} conversion", binding.Kind.ToString());
                fields.Add($"{prefix} enabled", Variant.From(binding.Enabled));
            }
            fields.Add("Component count", Variant.From(representation.Components.Count));
            for (int index = 0; index < representation.Components.Count; index++)
            {
                OpenUsdConnector.ComponentInfo component = representation.Components[index];
                string prefix = $"Component {index + 1}";
                fields.Add($"{prefix} node", Variant.From(component.NodeId));
                fields.AddText($"{prefix} prim", component.TargetPrimPath);
                fields.AddText($"{prefix} arc", component.Arc.ToString());
                fields.Add($"{prefix} remote", Variant.From(IsRemote(component)));
            }
            fields.Add("Served asset count", Variant.From(assets.Count));
            for (int index = 0; index < assets.Count; index++)
            {
                OpenUsdCompanionAsset asset = assets[index];
                string prefix = $"Asset {index + 1}";
                fields.AddText($"{prefix} identifier", asset.Identifier);
                fields.AddText($"{prefix} kind", asset.Kind.ToString());
                fields.Add($"{prefix} advertised bytes", Variant.From(asset.Size));
                fields.AddText($"{prefix} digest algorithm", asset.DigestAlgorithm.ToString());
            }
            lifetime.Token.ThrowIfCancellationRequested();
            ArrayOf<CompanionOperation> operations = assets.Count == 0
                ? [new("snapshot", "Read bound telemetry snapshot", CompanionOperationSafety.ReadOnly)]
                : [
                    new("snapshot", "Read bound telemetry snapshot", CompanionOperationSafety.ReadOnly),
                    new("verify-assets", "Verify served asset digests", CompanionOperationSafety.ReadOnly),
                    new("export", "Export verified assets and snapshot", CompanionOperationSafety.LocalFile,
                        "Absolute path of a new local directory under an existing, non-linked parent")
                ];
            return new CompanionInspection(
                fields.ToArray(),
                operations,
                "Metadata only; no served file was opened. Commands, federation, dependency resolution and rendering " +
                "are disabled. Export uses only this stage's advertised assets, not other servers or representations.");
        }
    }

    public async ValueTask<CompanionOperationResult> ExecuteAsync(
        CompanionContext context,
        CompanionTarget target,
        string operationId,
        string? input,
        CancellationToken cancellationToken)
    {
        ValidateTarget(context, target);
        cancellationToken.ThrowIfCancellationRequested();
        if (operationId is not ("snapshot" or "verify-assets" or "export"))
        {
            throw new ServiceResultException(StatusCodes.BadNotSupported, "This OpenUSD operation is not supported.");
        }
        string? exportPath = operationId == "export" ? OpenUsdCompanionExporter.ValidateDestination(input) : null;
        if (operationId != "export")
        {
            CellCompanionSupport.RequireNoInput(input);
        }
        using CancellationTokenSource lifetime =
            CellCompanionSupport.BeginOperation(context, Descriptor.ModelUri, cancellationToken);
        IOpenUsdCompanionReader reader = m_createReader(context, CreateOptions());
        await using (reader.ConfigureAwait(false))
        {
            CancellationToken token = lifetime.Token;
            OpenUsdConnector.RepresentationInfo representation =
                await FindAsync(reader, context, target.NodeId, token).ConfigureAwait(false);
            if (operationId == "snapshot")
            {
                ArrayOf<OpenUsdCompanionBindingValue> snapshot =
                    await ReadSnapshotAsync(reader, representation, context, token).ConfigureAwait(false);
                var sink = new MockUsdSink();
                var fields = new CellCompanionFields(context.MaxFields);
                for (int index = 0; index < snapshot.Count; index++)
                {
                    OpenUsdCompanionBindingValue value = snapshot[index];
                    sink.SetAttribute(value.PrimPath, value.PropertyName, value.Value);
                    fields.Add($"{value.PrimPath}.{value.PropertyName}", value.Value);
                }
                token.ThrowIfCancellationRequested();
                return new CompanionOperationResult(
                    $"Read and converted {sink.TotalWrites} telemetry binding(s) into an in-memory sink. " +
                    "Disabled, command, alarm and history bindings were not driven; " +
                    "no stage or subscription was opened.",
                    fields.ToArray());
            }

            ArrayOf<OpenUsdVerifiedAsset> verified = await ReadVerifiedAssetsAsync(
                reader, representation, context, token).ConfigureAwait(false);
            long totalBytes = 0;
            var resultFields = new CellCompanionFields(context.MaxFields);
            for (int index = 0; index < verified.Count; index++)
            {
                totalBytes += verified[index].Content.Length;
                resultFields.AddText($"Verified asset {index + 1}", verified[index].Identifier);
            }
            resultFields.Add("Verified bytes", Variant.From(totalBytes));
            if (exportPath is not null)
            {
                ArrayOf<OpenUsdCompanionBindingValue> snapshot =
                    await ReadSnapshotAsync(reader, representation, context, token).ConfigureAwait(false);
                resultFields.AddText("Export directory", exportPath);
                token.ThrowIfCancellationRequested();
                await m_exporter.WriteAsync(
                    exportPath,
                    representation.RootLayerIdentifier!,
                    representation.PrimPath!,
                    verified,
                    snapshot,
                    token).ConfigureAwait(false);
                return new CompanionOperationResult(
                    $"Exported {verified.Count} digest-verified served asset(s) " +
                    $"and {snapshot.Count} telemetry value(s). " +
                    "No renderer, external asset resolver, cross-server session or command was used.",
                    resultFields.ToArray());
            }
            token.ThrowIfCancellationRequested();
            return new CompanionOperationResult(
                $"Verified {verified.Count} advertised served asset(s) " +
                "and the selected stage's root digest in memory. " +
                "No files were written and no external dependencies were resolved.",
                resultFields.ToArray());
        }
    }

    internal static OpenUsdConnectorOptions CreateOptions()
    {
        return new OpenUsdConnectorOptions
        {
            EnableCommands = false,
            RemoteSessionFactory = null,
            RequireAssetDigests = true,
            MaxAssetBytes = MaxAssetBytes,
            MaxTotalAssetBytes = MaxTotalAssetBytes
        };
    }

    private void ValidateTarget(CompanionContext context, CompanionTarget target)
    {
        CellCompanionSupport.ValidateTarget(context, target, Descriptor.Id);
        if (target.TypeName != "OpenUsdRepresentation")
        {
            throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Select an OpenUSD representation.");
        }
    }

    private static async Task<OpenUsdConnector.RepresentationInfo> FindAsync(
        IOpenUsdCompanionReader reader,
        CompanionContext context,
        NodeId nodeId,
        CancellationToken cancellationToken)
    {
        ArrayOf<OpenUsdConnector.RepresentationInfo> representations = await reader.DiscoverAsync(cancellationToken)
            .ConfigureAwait(false);
        CellCompanionSupport.CheckCount(representations.Count, context.MaxTargets, "OpenUSD representations");
        OpenUsdConnector.RepresentationInfo? selected = null;
        for (int index = 0; index < representations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (representations[index].NodeId == nodeId)
            {
                if (selected is not null)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTooManyMatches, "Duplicate representation identity.");
                }
                selected = representations[index];
            }
        }
        if (selected is null)
        {
            throw new ServiceResultException(
                StatusCodes.BadNotFound, "The selected representation is no longer published.");
        }
        ValidateRepresentation(selected, context);
        return selected;
    }

    private static void ValidateRepresentation(
        OpenUsdConnector.RepresentationInfo representation,
        CompanionContext context)
    {
        if (representation.NodeId.IsNull)
        {
            throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "The representation has no NodeId.");
        }
        CellCompanionSupport.CheckCount(
            representation.Bindings.Count + representation.Components.Count,
            context.MaxFields,
            "Representation bindings and components");
        CellCompanionSupport.CheckCount(representation.RootLayerDigest.Length, 64, "Root digest bytes");
    }

    private static async Task<ArrayOf<OpenUsdCompanionBindingValue>> ReadSnapshotAsync(
        IOpenUsdCompanionReader reader,
        OpenUsdConnector.RepresentationInfo representation,
        CompanionContext context,
        CancellationToken cancellationToken)
    {
        var values = new List<OpenUsdCompanionBindingValue>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < representation.Bindings.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenUsdConnector.BindingInfo binding = representation.Bindings[index];
            if (!binding.Enabled || binding.Intent != OpenUsdIntentProfile.UaToUsdTelemetry)
            {
                continue;
            }
            CellCompanionSupport.CheckCount(values.Count + 1, context.MaxFields, "Telemetry snapshot values");
            OpenUsdCompanionExporter.ValidatePrimPath(binding.PrimPath);
            OpenUsdCompanionExporter.ValidatePropertyName(binding.PropertyName);
            if (binding.SourceNodeId.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid, "A telemetry binding source is unresolved.");
            }
            if (!names.Add($"{binding.PrimPath}.{binding.PropertyName}"))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTooManyMatches, "Multiple bindings target one USD property.");
            }
            DataValue source = await reader.ReadValueAsync(binding.SourceNodeId, cancellationToken)
                .ConfigureAwait(false);
            if (source.IsNull || !StatusCode.IsGood(source.StatusCode))
            {
                throw new ServiceResultException(
                    source.IsNull ? StatusCodes.BadNoData : source.StatusCode,
                    "The bound source has no good current value; no converted value was exported.");
            }
            Variant converted = OpenUsdConnector.Convert(binding, source.WrappedValue);
            if (converted.IsNull)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "A binding cannot be converted faithfully.");
            }
            values.Add(new OpenUsdCompanionBindingValue(binding.PrimPath!, binding.PropertyName!, converted));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return [.. values];
    }

    private static async Task<ArrayOf<OpenUsdVerifiedAsset>> ReadVerifiedAssetsAsync(
        IOpenUsdCompanionReader reader,
        OpenUsdConnector.RepresentationInfo representation,
        CompanionContext context,
        CancellationToken cancellationToken)
    {
        OpenUsdCompanionExporter.ValidateAssetIdentifier(representation.RootLayerIdentifier);
        OpenUsdCompanionExporter.ValidatePrimPath(representation.PrimPath);
        ValidateDigest(representation.RootLayerDigest, representation.DigestAlgorithm);
        for (int index = 0; index < representation.Components.Count; index++)
        {
            if (representation.Components[index].Enabled && IsRemote(representation.Components[index]))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotSupported, "Cross-server components cannot be included in a local asset export.");
            }
        }
        ArrayOf<OpenUsdCompanionAsset> assets = await reader.ReadAssetsAsync(
            representation.StageNodeId, AssetLimit(context), cancellationToken).ConfigureAwait(false);
        CellCompanionSupport.CheckCount(assets.Count, AssetLimit(context), "Served assets");
        if (assets.Count == 0)
        {
            throw new ServiceResultException(StatusCodes.BadNotFound, "The selected stage serves no assets.");
        }
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ulong advertisedBytes = 0;
        for (int index = 0; index < assets.Count; index++)
        {
            OpenUsdCompanionAsset asset = assets[index];
            OpenUsdCompanionExporter.ValidateAssetIdentifier(asset.Identifier);
            ValidateDigest(asset.Digest, asset.DigestAlgorithm);
            if (!identifiers.Add(asset.Identifier))
            {
                throw new ServiceResultException(
                    StatusCodes.BadBrowseNameDuplicated, "Asset identifiers collide locally.");
            }
            if (asset.Size > MaxAssetBytes)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "A served asset exceeds 8 MiB.");
            }
            advertisedBytes += asset.Size;
        }
        if (advertisedBytes > MaxTotalAssetBytes)
        {
            throw new ServiceResultException(
                StatusCodes.BadEncodingLimitsExceeded, "The asset closure exceeds 32 MiB.");
        }
        if (!identifiers.Contains(representation.RootLayerIdentifier!))
        {
            throw new ServiceResultException(
                StatusCodes.BadNotFound, "The asset closure does not contain its root layer.");
        }
        for (int index = 0; index < representation.Components.Count; index++)
        {
            OpenUsdConnector.ComponentInfo component = representation.Components[index];
            if (component.Enabled && !string.IsNullOrEmpty(component.ComponentAssetReference))
            {
                OpenUsdCompanionExporter.ValidateAssetIdentifier(component.ComponentAssetReference);
                if (!identifiers.Contains(component.ComponentAssetReference))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotFound,
                        "An advertised component asset is not in this stage's served asset set.");
                }
            }
        }
        var verified = new List<OpenUsdVerifiedAsset>();
        long totalBytes = 0;
        bool rootVerified = false;
        for (int index = 0; index < assets.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenUsdCompanionAsset asset = assets[index];
            ByteString content = await reader.ReadAssetAsync(asset.NodeId, cancellationToken).ConfigureAwait(false);
            totalBytes += content.Length;
            if (content.Length > MaxAssetBytes || totalBytes > MaxTotalAssetBytes)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingLimitsExceeded, "Served asset byte limits exceeded.");
            }
            if ((ulong)content.Length != asset.Size || !VerifyDigest(content, asset.Digest, asset.DigestAlgorithm))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "Served asset size or digest differs from its advertised metadata.");
            }
            if (string.Equals(asset.Identifier, representation.RootLayerIdentifier, StringComparison.Ordinal))
            {
                rootVerified = OpenUsdConnector.VerifyStageDigest(representation, content.ToArray());
            }
            verified.Add(new OpenUsdVerifiedAsset(asset.Identifier, content));
        }
        if (!rootVerified)
        {
            throw new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed, "The selected root-layer digest failed.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return [.. verified];
    }

    private static void ValidateDigest(ByteString digest, OpenUsdDigestAlgorithm algorithm)
    {
        int expected = algorithm switch
        {
            OpenUsdDigestAlgorithm.Sha256 => 32,
            OpenUsdDigestAlgorithm.Sha384 => 48,
            OpenUsdDigestAlgorithm.Sha512 => 64,
            _ => 0
        };
        if (expected == 0 || digest.IsNull || digest.Length != expected)
        {
            throw new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed, "A valid SHA-2 asset digest is required.");
        }
    }

    private static bool VerifyDigest(ByteString content, ByteString digest, OpenUsdDigestAlgorithm algorithm)
    {
        byte[] actual = algorithm switch
        {
            OpenUsdDigestAlgorithm.Sha256 => SHA256.HashData(content.Span),
            OpenUsdDigestAlgorithm.Sha384 => SHA384.HashData(content.Span),
            OpenUsdDigestAlgorithm.Sha512 => SHA512.HashData(content.Span),
            _ => throw new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed, "Unsupported asset digest algorithm.")
        };
        return CryptographicOperations.FixedTimeEquals(actual, digest.Span);
    }

    private static bool IsRemote(OpenUsdConnector.ComponentInfo component)
    {
        return !string.IsNullOrWhiteSpace(component.ComponentEndpointUrl) ||
            !string.IsNullOrWhiteSpace(component.ComponentServerUri);
    }

    private static int AssetLimit(CompanionContext context)
    {
        return Math.Min(context.MaxTargets, 32);
    }

    internal const int MaxAssetBytes = 8 * 1024 * 1024;
    internal const long MaxTotalAssetBytes = 32L * 1024 * 1024;

    private readonly Func<CompanionContext, OpenUsdConnectorOptions, IOpenUsdCompanionReader> m_createReader;
    private readonly IOpenUsdCompanionExporter m_exporter;
}
