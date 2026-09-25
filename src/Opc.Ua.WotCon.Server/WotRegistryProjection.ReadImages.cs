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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server
{
    internal sealed partial class WotRegistryProjection
    {
        internal INodeManagerReadImage PrepareReadImage(
            WotRegistrySnapshot previous, WotRegistrySnapshot intended)
        {
            if (m_manager.Server.NodeManager is not INodeManagerReadImageSource)
            {
                throw new NotSupportedException("The native host cannot retain captured registry read images.");
            }
            // Requests captured before the first image must retain its predecessor after publication.
            if (Volatile.Read(ref m_initialReadImage) is null)
            {
                Interlocked.CompareExchange(ref m_initialReadImage, new RegistryReadImage(m_manager, previous), null);
            }
            return new RegistryReadImage(m_manager, intended);
        }

        internal void BindPublishedRegistryProperties(WoTRegistryState registry)
        {
            registry.RefreshGeneration!.AccessLevel = AccessLevels.CurrentRead;
            registry.RefreshGeneration.UserAccessLevel = AccessLevels.CurrentRead;
            registry.RefreshGeneration.OnSimpleReadValueAsync = (context, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                uint generation = GetCapturedReadImage(context)?.RefreshGeneration ??
                    m_registry.Current.RefreshGeneration;
                return new ValueTask<AttributeSimpleReadResult>(
                    new AttributeSimpleReadResult(StatusCodes.Good, Variant.From(generation)));
            };
            registry.LastRefreshTime!.AccessLevel = AccessLevels.CurrentRead;
            registry.LastRefreshTime.UserAccessLevel = AccessLevels.CurrentRead;
            registry.LastRefreshTime.StatusCode = StatusCodes.BadWaitingForInitialData;
            registry.LastRefreshTime.OnSimpleReadValueAsync = (_, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                WoTRefreshSummaryDataType? summary = m_manager.Coordinator.LastRefreshSummary;
                return new ValueTask<AttributeSimpleReadResult>(summary is null
                    ? new AttributeSimpleReadResult(StatusCodes.BadWaitingForInitialData, Variant.Null)
                    : new AttributeSimpleReadResult(StatusCodes.Good, Variant.From(summary.EndTime)));
            };
            registry.LastRefreshSummary!.AccessLevel = AccessLevels.CurrentRead;
            registry.LastRefreshSummary.UserAccessLevel = AccessLevels.CurrentRead;
            registry.LastRefreshSummary.StatusCode = StatusCodes.BadWaitingForInitialData;
            registry.LastRefreshSummary.OnSimpleReadValueAsync = (_, _, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                WoTRefreshSummaryDataType? summary = m_manager.Coordinator.LastRefreshSummary;
                return new ValueTask<AttributeSimpleReadResult>(summary is null
                    ? new AttributeSimpleReadResult(StatusCodes.BadWaitingForInitialData, Variant.Null)
                    : new AttributeSimpleReadResult(StatusCodes.Good, Variant.FromStructure(summary)));
            };
        }

        private void BindPublishedResourceProperties(
            WoTDocumentState document, WotResource resource, string versionId)
        {
            (string, string) identity = (resource.GroupId, resource.ResourceId);
            ResourceReadImage initial = ResourceReadImage.Capture(resource);
            document.ActiveVersionId!.OnSimpleReadValueAsync = (context, _, ct) =>
                ReadPublishedResourcePropertyAsync(context, identity, initial,
                    static image => Variant.From(image.ActiveVersionId ?? string.Empty), ct);
            document.MaterializedNodeCount!.OnSimpleReadValueAsync = (context, _, ct) =>
                ReadPublishedResourcePropertyAsync(context, identity, initial,
                    static image => Variant.From(image.MaterializedNodeCount), ct);
            document.RootNodeId!.OnSimpleReadValueAsync = (context, _, ct) =>
                ReadPublishedResourcePropertyAsync(context, identity, initial,
                    static image => Variant.From(image.RootNodeId), ct);
            document.RefreshGeneration!.OnSimpleReadValueAsync = (context, _, ct) =>
                ReadPublishedResourcePropertyAsync(context, identity, initial,
                    static image => Variant.From(image.RefreshGeneration), ct);
            document.LastRefreshTime!.OnSimpleReadValueAsync = (context, _, ct) =>
                ReadPublishedResourcePropertyAsync(context, identity, initial,
                    static image => Variant.From(image.LastRefreshTime), ct);
            if (document.LoadState is { } loadState)
            {
                loadState.OnSimpleReadValueAsync = (context, _, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    if (!string.IsNullOrEmpty(versionId) &&
                        m_registry.Current.FindResource(resource.GroupId, resource.ResourceId)?
                            .FindVersion(versionId)?.HasValidationFailure == true)
                    {
                        return new ValueTask<AttributeSimpleReadResult>(new AttributeSimpleReadResult(
                            StatusCodes.Good, Variant.From((int)WoTLoadStateEnum.Failed)));
                    }
                    return ReadPublishedResourcePropertyAsync(context, identity, initial,
                        static image => Variant.From((int)image.LoadState), ct);
                };
            }
        }

        private ValueTask<AttributeSimpleReadResult> ReadPublishedResourcePropertyAsync(
            ISystemContext context,
            (string GroupId, string ResourceId) identity,
            ResourceReadImage initial,
            Func<ResourceReadImage, Variant> select,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RegistryReadImage? captured = GetCapturedReadImage(context);
            ResourceReadImage resource;
            bool found;
            if (captured is not null)
            {
                found = captured.TryGetResource(identity, out resource);
            }
            else
            {
                WotResource? current = m_registry.Current.FindResource(identity.GroupId, identity.ResourceId);
                found = current is not null;
                resource = current is null ? default : ResourceReadImage.Capture(current);
            }
            if (!found)
            {
                if (initial.ActiveVersionId is not null)
                {
                    return new ValueTask<AttributeSimpleReadResult>(new AttributeSimpleReadResult(
                        StatusCodes.BadWaitingForInitialData, Variant.Null));
                }
                resource = initial;
            }
            return new ValueTask<AttributeSimpleReadResult>(
                new AttributeSimpleReadResult(ServiceResult.Good, select(resource)));
        }

        private RegistryReadImage? GetCapturedReadImage(ISystemContext context)
        {
            if (context is not ServerSystemContext { OperationContext: not null } ||
                m_manager.Server.NodeManager is not INodeManagerReadImageSource images)
            {
                return null;
            }
            return images.GetReadImage(m_manager) switch
            {
                RegistryReadImage current => current,
                null => Volatile.Read(ref m_initialReadImage),
                _ => throw new InvalidOperationException("The registry has an incompatible native read image.")
            };
        }

        private RegistryReadImage? m_initialReadImage;

        private sealed class RegistryReadImage(IAsyncNodeManager owner, WotRegistrySnapshot snapshot)
            : INodeManagerReadImage
        {
            public IAsyncNodeManager Owner { get; } = owner;
            public uint RefreshGeneration { get; } = snapshot.RefreshGeneration;

            public bool TryGetResource((string, string) identity, out ResourceReadImage resource)
            {
                return m_resources.TryGetValue(identity, out resource);
            }

            private readonly Dictionary<(string, string), ResourceReadImage> m_resources = snapshot.AllResources()
                .ToDictionary(resource => (resource.GroupId, resource.ResourceId), ResourceReadImage.Capture);
        }

        private readonly record struct ResourceReadImage(
            string? ActiveVersionId,
            uint RefreshGeneration,
            uint MaterializedNodeCount,
            NodeId RootNodeId,
            WoTLoadStateEnum LoadState,
            DateTimeUtc LastRefreshTime)
        {
            public static ResourceReadImage Capture(WotResource resource)
            {
                return new ResourceReadImage(
                    resource.ActiveVersionId, resource.RefreshGeneration, (uint)resource.MaterializedNodeCount,
                    resource.RootNodeId, resource.LoadState, (DateTimeUtc)resource.LastRefreshTime);
            }
        }
    }
}
