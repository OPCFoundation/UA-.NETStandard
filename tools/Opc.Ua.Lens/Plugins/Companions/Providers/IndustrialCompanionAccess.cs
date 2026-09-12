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
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// Bounded discovery and strict property reads for models whose generated proxies
/// expose Methods and Object children, but not variable getters. No session is retained.
/// </summary>
internal static class IndustrialCompanionAccess
{
    public static async ValueTask<ArrayOf<CompanionTarget>> DiscoverAsync(
        CompanionContext context,
        string providerId,
        ArrayOf<IndustrialCompanionType> types,
        ArrayOf<NodeId> roots,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<NodeId, string> resolvedTypes = ResolveTypes(context, types);
        if (resolvedTypes.Count == 0)
        {
            throw Unsupported("The server does not expose this companion model.");
        }

        var budget = new IndustrialBrowseBudget(context);
        var pending = new Queue<NodeId>();
        var visited = new HashSet<NodeId>();
        var kinds = new Dictionary<NodeId, string?>();
        var targets = new List<CompanionTarget>();
        foreach (NodeId root in roots)
        {
            if (!root.IsNull && visited.Add(root))
            {
                pending.Enqueue(root);
            }
        }

        while (pending.TryDequeue(out NodeId parent))
        {
            await foreach (ReferenceDescription reference in BrowseAsync(
                context, parent, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
                NodeClass.Object, budget, cancellationToken).ConfigureAwait(false))
            {
                NodeId nodeId = LocalId(context, reference.NodeId);
                if (nodeId.IsNull || reference.NodeClass != NodeClass.Object || !visited.Add(nodeId))
                {
                    continue;
                }
                if (visited.Count > budget.MaxNodes)
                {
                    throw Limit("The bounded companion object scan was exhausted.");
                }

                NodeId typeId = LocalId(context, reference.TypeDefinition);
                string? kind = await FindKindAsync(
                    context, typeId, resolvedTypes, kinds, budget, cancellationToken).ConfigureAwait(false);
                if (kind is not null)
                {
                    targets.Add(new CompanionTarget(
                        providerId, nodeId, reference.DisplayName.Text ?? reference.BrowseName.Name ?? kind, kind));
                    if (targets.Count == context.MaxTargets)
                    {
                        return [.. targets];
                    }
                }
                pending.Enqueue(nodeId);
            }
        }

        if (targets.Count == 0)
        {
            throw Unsupported("No supported typed instances were found beneath the discovery root.");
        }
        return [.. targets];
    }

    public static async ValueTask RequireTargetAsync(
        CompanionContext context,
        CompanionTarget target,
        string providerId,
        ArrayOf<IndustrialCompanionType> types,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        if (target.ProviderId != providerId || target.NodeId.IsNull)
        {
            throw new ArgumentException("The target does not belong to this provider.", nameof(target));
        }

        ReadResponse response = await context.Session.ReadAsync(
            null, 0, TimestampsToReturn.Neither,
            [new ReadValueId { NodeId = target.NodeId, AttributeId = Attributes.NodeClass }],
            cancellationToken).ConfigureAwait(false);
        ValidateRead(response, 1);
        if (!response.Results[0].WrappedValue.TryGetValue(out int nodeClass) || nodeClass != (int)NodeClass.Object)
        {
            throw Unsupported("The target is not an Object instance.");
        }

        var budget = new IndustrialBrowseBudget(context);
        Dictionary<NodeId, string> resolvedTypes = ResolveTypes(context, types);
        var kinds = new Dictionary<NodeId, string?>();
        bool found = false;
        await foreach (ReferenceDescription reference in BrowseAsync(
            context, target.NodeId, BrowseDirection.Forward, ReferenceTypeIds.HasTypeDefinition,
            NodeClass.ObjectType, budget, cancellationToken).ConfigureAwait(false))
        {
            if (found || reference.NodeClass != NodeClass.ObjectType)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "An invalid type definition was returned.");
            }
            string? kind = await FindKindAsync(
                context, LocalId(context, reference.NodeId), resolvedTypes, kinds, budget, cancellationToken)
                .ConfigureAwait(false);
            if (kind != target.TypeName)
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "The target's companion type has changed.");
            }
            found = true;
        }
        if (!found)
        {
            throw Unsupported("The target has no supported type definition.");
        }
    }

    public static async IAsyncEnumerable<ReferenceDescription> BrowseAsync(
        CompanionContext context,
        NodeId parent,
        BrowseDirection direction,
        NodeId referenceType,
        NodeClass nodeClass,
        IndustrialBrowseBudget budget,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var outstanding = new List<ByteString>();
        try
        {
            budget.Request();
            cancellationToken.ThrowIfCancellationRequested();
            BrowseResponse response = await context.Session.BrowseAsync(
                null, null, 64,
                [
                    new BrowseDescription
                    {
                        NodeId = parent,
                        BrowseDirection = direction,
                        ReferenceTypeId = referenceType,
                        IncludeSubtypes = true,
                        NodeClassMask = (uint)nodeClass,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                ],
                cancellationToken).ConfigureAwait(false);
            RememberContinuations(response.Results, outstanding);
            ValidateHeader(response.ResponseHeader);
            BrowseResult result = SingleResult(response.Results);
            while (true)
            {
                ThrowIfBad(result.StatusCode);
                for (int index = 0; index < result.References.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    budget.Reference();
                    yield return result.References[index];
                }
                if (result.ContinuationPoint.IsNull || result.ContinuationPoint.Length == 0)
                {
                    break;
                }
                cancellationToken.ThrowIfCancellationRequested();
                budget.Request();
                BrowseNextResponse next = await context.Session.BrowseNextAsync(
                    null, false, [result.ContinuationPoint], cancellationToken).ConfigureAwait(false);
                if (next.ResponseHeader is not null && StatusCode.IsGood(next.ResponseHeader.ServiceResult) &&
                    next.Results.Count == 1 && StatusCode.IsGood(next.Results[0].StatusCode))
                {
                    outstanding.Clear();
                }
                RememberContinuations(next.Results, outstanding);
                ValidateHeader(next.ResponseHeader);
                result = SingleResult(next.Results);
            }
        }
        finally
        {
            if (outstanding.Count != 0)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    BrowseNextResponse released = await context.Session.BrowseNextAsync(
                        null, true, [.. outstanding], cleanup.Token).ConfigureAwait(false);
                    ValidateHeader(released.ResponseHeader);
                }
                catch (ServiceResultException failure)
                {
                    IndustrialCompanionAccessLog.ContinuationReleaseFailed(
                        context.Telemetry.CreateLogger(nameof(IndustrialCompanionAccess)), failure.StatusCode);
                }
                catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
                {
                    IndustrialCompanionAccessLog.ContinuationReleaseFailed(
                        context.Telemetry.CreateLogger(nameof(IndustrialCompanionAccess)), StatusCodes.BadTimeout);
                }
                catch (Exception failure) when (failure is IOException or InvalidOperationException or
                    ObjectDisposedException or TimeoutException)
                {
                    IndustrialCompanionAccessLog.ContinuationReleaseFailed(
                        context.Telemetry.CreateLogger(nameof(IndustrialCompanionAccess)), StatusCodes.BadCommunicationError);
                }
            }
        }
    }

    public static async ValueTask<ArrayOf<CompanionValue>> ReadPropertiesAsync(
        CompanionContext context,
        NodeId parent,
        string namespaceUri,
        ArrayOf<string> names,
        CancellationToken cancellationToken)
    {
        CheckFields(context, names.Count);
        var paths = new List<BrowsePath>();
        foreach (string name in names)
        {
            paths.Add(Path(context, parent, namespaceUri, name));
        }
        cancellationToken.ThrowIfCancellationRequested();
        TranslateBrowsePathsToNodeIdsResponse translated = await context.Session.TranslateBrowsePathsToNodeIdsAsync(
            null, [.. paths], cancellationToken).ConfigureAwait(false);
        ValidateHeader(translated.ResponseHeader);
        if (translated.Results.Count != names.Count)
        {
            throw new ServiceResultException(
                StatusCodes.BadUnexpectedError, "An incomplete property lookup was returned.");
        }

        var reads = new List<ReadValueId>();
        var positions = new List<int>();
        var values = new CompanionValue[names.Count];
        for (int index = 0; index < names.Count; index++)
        {
            NodeId nodeId = ResolveResult(context, translated.Results[index], optional: true);
            if (nodeId.IsNull)
            {
                values[index] = new CompanionValue(names[index], Variant.From(StatusCodes.BadNotFound));
                continue;
            }
            reads.Add(new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value });
            positions.Add(index);
        }
        if (reads.Count != 0)
        {
            ReadResponse response = await context.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither, [.. reads], cancellationToken).ConfigureAwait(false);
            ValidateRead(response, reads.Count);
            for (int index = 0; index < reads.Count; index++)
            {
                int position = positions[index];
                values[position] = new CompanionValue(names[position], response.Results[index].WrappedValue);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return values;
    }

    public static async ValueTask<NodeId> ResolveChildAsync(
        CompanionContext context,
        NodeId parent,
        string namespaceUri,
        string name,
        bool optional,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TranslateBrowsePathsToNodeIdsResponse response = await context.Session.TranslateBrowsePathsToNodeIdsAsync(
            null, [Path(context, parent, namespaceUri, name)], cancellationToken).ConfigureAwait(false);
        ValidateHeader(response.ResponseHeader);
        if (response.Results.Count != 1)
        {
            throw new ServiceResultException(
                StatusCodes.BadUnexpectedError, "An incomplete child lookup was returned.");
        }
        return ResolveResult(context, response.Results[0], optional);
    }

    public static ValueTask<string?> ClassifyAsync(
        CompanionContext context,
        ExpandedNodeId typeId,
        ArrayOf<IndustrialCompanionType> types,
        IndustrialBrowseBudget budget,
        CancellationToken cancellationToken)
    {
        return FindKindAsync(
            context, LocalId(context, typeId), ResolveTypes(context, types),
            new Dictionary<NodeId, string?>(), budget, cancellationToken);
    }

    public static NodeId LocalId(CompanionContext context, ExpandedNodeId expanded)
    {
        if (expanded.IsNull || expanded.ServerIndex != 0)
        {
            return NodeId.Null;
        }
        return ExpandedNodeId.ToNodeId(expanded, context.Session.NamespaceUris);
    }

    public static void RequireNamespace(CompanionContext context, string namespaceUri)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Session.NamespaceUris.GetIndex(namespaceUri) < 0)
        {
            throw Unsupported("The server does not expose the required companion namespace.");
        }
    }

    public static void CheckFields(CompanionContext context, int count)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (count > context.MaxFields)
        {
            throw Limit("The inspection exceeds the configured field limit.");
        }
    }

    public static string SampleId(string? input)
    {
        if (string.IsNullOrEmpty(input) || input.Length > 32)
        {
            throw new ArgumentException("Enter a 1–32 character sample suffix (lowercase ASCII, digits or hyphens).",
                nameof(input));
        }
        foreach (char character in input)
        {
            if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-')
            {
                throw new ArgumentException("The sample suffix may contain only lowercase ASCII, digits or hyphens.",
                    nameof(input));
            }
        }
        return "ualens-sample-" + input;
    }

    public static void RequireNoInput(string? input)
    {
        if (!string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("This operation does not accept input.", nameof(input));
        }
    }

    public static ArrayOf<CompanionValue> DescribeDocument(ByteString content)
    {
        if (content.Length > MaxDocumentBytes)
        {
            throw Limit("The document exceeds the 64 KiB inspection limit.");
        }
        using JsonDocument document = JsonDocument.Parse(
            content.Span.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ServiceResultException(StatusCodes.BadDecodingError, "A JSON Object document is required.");
        }
        int members = 0;
        JsonElement.ObjectEnumerator properties = document.RootElement.EnumerateObject();
        while (properties.MoveNext())
        {
            members++;
        }
        return
        [
            new("Document bytes", Variant.From(content.Length)),
            new("Document SHA-256", Variant.From(ByteString.From(SHA256.HashData(content.Span)))),
            new("JSON member count", Variant.From(members))
        ];
    }

    public static async ValueTask<ByteString> ReadDocumentAsync(
        FileTypeClient file,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();
        uint handle = await file.OpenAsync(1, cancellationToken).ConfigureAwait(false);
        Exception? failure = null;
        try
        {
            using var buffer = new IndustrialDocumentBuffer();
            for (int request = 0; request < 128; request++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(4096, MaxDocumentBytes - (int)buffer.Length + 1);
                ByteString chunk = await file.ReadAsync(handle, count, cancellationToken).ConfigureAwait(false);
                if (chunk.IsNull || chunk.Length == 0)
                {
                    return ByteString.From(buffer.ToArray());
                }
                if (chunk.Length > count)
                {
                    throw Limit("The file server returned more bytes than requested.");
                }
                await buffer.WriteAsync(chunk.Span.ToArray(), cancellationToken).ConfigureAwait(false);
            }
            throw Limit("The document inspection exceeded 128 file reads.");
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await file.CloseAsync(handle, cleanup.Token).ConfigureAwait(false);
            }
            catch when (failure is not null)
            {
                // Preserve the primary read failure; cleanup has its own uncancelled, bounded token.
            }
        }
    }

    public static ServiceResultException Unsupported(string message)
    {
        return new ServiceResultException(StatusCodes.BadNotSupported, message);
    }

    public static ServiceResultException Limit(string message)
    {
        return new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded, message);
    }

    public static void ThrowIfBad(StatusCode status)
    {
        if (StatusCode.IsBad(status))
        {
            throw new ServiceResultException(status);
        }
    }

    private static Dictionary<NodeId, string> ResolveTypes(
        CompanionContext context,
        ArrayOf<IndustrialCompanionType> types)
    {
        var resolved = new Dictionary<NodeId, string>();
        foreach (IndustrialCompanionType type in types)
        {
            NodeId id = LocalId(context, type.TypeId);
            if (!id.IsNull)
            {
                resolved.Add(id, type.Name);
            }
        }
        return resolved;
    }

    private static async ValueTask<string?> FindKindAsync(
        CompanionContext context,
        NodeId typeId,
        Dictionary<NodeId, string> expected,
        Dictionary<NodeId, string?> cache,
        IndustrialBrowseBudget budget,
        CancellationToken cancellationToken)
    {
        if (typeId.IsNull)
        {
            return null;
        }
        if (expected.TryGetValue(typeId, out string? direct))
        {
            return direct;
        }
        if (cache.TryGetValue(typeId, out string? cached))
        {
            return cached;
        }

        var visited = new HashSet<NodeId>();
        var pending = new Queue<NodeId>();
        pending.Enqueue(typeId);
        while (pending.TryDequeue(out NodeId current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(current))
            {
                continue;
            }
            if (visited.Count > 32)
            {
                throw Limit("The type ancestry exceeds 32 nodes.");
            }
            if (expected.TryGetValue(current, out string? kind))
            {
                cache[typeId] = kind;
                return kind;
            }
            if (current.NamespaceIndex == 0)
            {
                continue;
            }
            await foreach (ReferenceDescription parent in BrowseAsync(
                context, current, BrowseDirection.Inverse, ReferenceTypeIds.HasSubtype,
                NodeClass.ObjectType, budget, cancellationToken).ConfigureAwait(false))
            {
                NodeId parentId = LocalId(context, parent.NodeId);
                if (!parentId.IsNull)
                {
                    pending.Enqueue(parentId);
                }
            }
        }
        cache[typeId] = null;
        return null;
    }

    private static BrowsePath Path(CompanionContext context, NodeId parent, string namespaceUri, string name)
    {
        RequireNamespace(context, namespaceUri);
        return new BrowsePath
        {
            StartingNode = parent,
            RelativePath = new RelativePath
            {
                Elements =
                [
                    new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                        IncludeSubtypes = true,
                        TargetName = new QualifiedName(
                            name, (ushort)context.Session.NamespaceUris.GetIndex(namespaceUri))
                    }
                ]
            }
        };
    }

    private static NodeId ResolveResult(CompanionContext context, BrowsePathResult result, bool optional)
    {
        if (result.StatusCode == StatusCodes.BadNoMatch && optional)
        {
            return NodeId.Null;
        }
        ThrowIfBad(result.StatusCode);
        if (result.Targets.Count != 1 || result.Targets[0].RemainingPathIndex != uint.MaxValue)
        {
            throw new ServiceResultException(StatusCodes.BadNoMatch, "The child lookup is absent or ambiguous.");
        }
        NodeId id = LocalId(context, result.Targets[0].TargetId);
        if (id.IsNull)
        {
            throw Unsupported("Cross-server or unresolved companion child references are not supported.");
        }
        return id;
    }

    private static BrowseResult SingleResult(ArrayOf<BrowseResult> results)
    {
        if (results.Count != 1)
        {
            throw new ServiceResultException(
                StatusCodes.BadUnexpectedError, "An incomplete browse response was returned.");
        }
        return results[0];
    }

    private static void RememberContinuations(ArrayOf<BrowseResult> results, List<ByteString> outstanding)
    {
        foreach (BrowseResult result in results)
        {
            if (!result.ContinuationPoint.IsNull && result.ContinuationPoint.Length != 0 &&
                !outstanding.Contains(result.ContinuationPoint))
            {
                outstanding.Add(result.ContinuationPoint);
            }
        }
    }

    private static void ValidateHeader(ResponseHeader? header)
    {
        if (header is null)
        {
            throw new ServiceResultException(StatusCodes.BadUnexpectedError, "The response header is missing.");
        }
        ThrowIfBad(header.ServiceResult);
    }

    private static void ValidateRead(ReadResponse response, int count)
    {
        ValidateHeader(response.ResponseHeader);
        if (response.Results.Count != count)
        {
            throw new ServiceResultException(
                StatusCodes.BadUnexpectedError, "An incomplete read response was returned.");
        }
        foreach (DataValue value in response.Results)
        {
            ThrowIfBad(value.StatusCode);
        }
    }

    public const int MaxDocumentBytes = 64 * 1024;
}

internal static partial class IndustrialCompanionAccessLog
{
    [LoggerMessage(EventId = UaLensEventIds.CompanionPluginBase + 2, Level = LogLevel.Warning,
        Message = "Companion discovery continuation cleanup failed with {StatusCode}.")]
    public static partial void ContinuationReleaseFailed(ILogger logger, StatusCode statusCode);
}

internal sealed record IndustrialCompanionType(ExpandedNodeId TypeId, string Name);

/// <summary>
/// A per-call budget, shared by instance traversal and type ancestry lookups.
/// </summary>
internal sealed class IndustrialBrowseBudget
{
    public IndustrialBrowseBudget(CompanionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        MaxNodes = Math.Clamp(context.MaxTargets * 16, 128, 4096);
    }

    public int MaxNodes { get; }

    public void Request()
    {
        if (++m_requests > MaxNodes * 2)
        {
            throw IndustrialCompanionAccess.Limit("The companion browse request budget was exhausted.");
        }
    }

    public void Reference()
    {
        if (++m_references > MaxNodes * 4)
        {
            throw IndustrialCompanionAccess.Limit("The companion browse reference budget was exhausted.");
        }
    }

    private int m_requests;
    private int m_references;
}

/// <summary>
/// Non-expandable destination used with existing typed file-transfer helpers.
/// It cannot allocate past the document bound even when a server misreports Size.
/// </summary>
internal sealed class IndustrialDocumentBuffer : MemoryStream
{
    public IndustrialDocumentBuffer()
        : base(new byte[IndustrialCompanionAccess.MaxDocumentBytes], 0, IndustrialCompanionAccess.MaxDocumentBytes,
            writable: true, publiclyVisible: false)
    {
        SetLength(0);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        CheckCount(count);
        base.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        CheckCount(buffer.Length);
        base.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckCount(count);
        return base.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckCount(buffer.Length);
        return base.WriteAsync(buffer, cancellationToken);
    }

    private void CheckCount(int count)
    {
        if (count > IndustrialCompanionAccess.MaxDocumentBytes - Position)
        {
            throw IndustrialCompanionAccess.Limit("The document exceeds the 64 KiB inspection limit.");
        }
    }
}
