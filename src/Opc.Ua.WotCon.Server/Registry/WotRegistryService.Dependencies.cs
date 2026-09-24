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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Server.Materialization;

namespace Opc.Ua.WotCon.Server.Registry
{
    public sealed partial class WotRegistryService
    {
        private async ValueTask<WotRegistrySnapshot> HydrateDependencyMetadataAsync(
            WotRegistrySnapshot snapshot, CancellationToken cancellationToken)
        {
            var metadataByDigest = new Dictionary<string, WotResourceDependencies>(StringComparer.Ordinal);
            foreach (WotResourceGroup group in snapshot.Groups.Values)
            {
                ImmutableDictionary<string, WotResource> resources = group.Resources;
                foreach (WotResource resource in group.Resources.Values)
                {
                    WotResource hydrated = await HydrateVersionsAsync(resource).ConfigureAwait(false);
                    if (resource.CommittedVersion is { } committed)
                    {
                        WotResourceVersion current = await HydrateVersionAsync(committed).ConfigureAwait(false);
                        if (!ReferenceEquals(current, committed))
                        {
                            hydrated = hydrated.WithCommittedVersion(current);
                        }
                    }
                    WotResource[]? inputs = null;
                    for (int i = 0; i < resource.CommittedInputs.Count; i++)
                    {
                        WotResource input = resource.CommittedInputs[i];
                        WotResource current = await HydrateVersionsAsync(input).ConfigureAwait(false);
                        if (!ReferenceEquals(current, input))
                        {
                            inputs ??= resource.CommittedInputs.Span.ToArray();
                            inputs[i] = current;
                        }
                    }
                    if (inputs is not null)
                    {
                        hydrated = hydrated.WithCommittedInputs(inputs.ToArrayOf());
                    }
                    if (!ReferenceEquals(hydrated, resource))
                    {
                        resources = resources.SetItem(resource.ResourceId, hydrated);
                    }
                }
                if (!ReferenceEquals(resources, group.Resources))
                {
                    snapshot = snapshot.WithGroup(group.WithResources(resources, group.Epoch), snapshot.Generation);
                }
            }
            return snapshot;

            async ValueTask<WotResource> HydrateVersionsAsync(WotResource resource)
            {
                ImmutableArray<WotResourceVersion>.Builder? versions = null;
                for (int i = 0; i < resource.Versions.Length; i++)
                {
                    WotResourceVersion previous = resource.Versions[i];
                    WotResourceVersion current = await HydrateVersionAsync(previous).ConfigureAwait(false);
                    if (!ReferenceEquals(current, previous))
                    {
                        versions ??= resource.Versions.ToBuilder();
                        versions[i] = current;
                    }
                }
                return versions is null ? resource : resource.With(versions: versions.ToImmutable());
            }

            async ValueTask<WotResourceVersion> HydrateVersionAsync(WotResourceVersion version)
            {
                if (!version.HasContent || version.Dependencies is not null)
                {
                    return version;
                }
                if (!metadataByDigest.TryGetValue(version.DigestHex, out WotResourceDependencies? metadata))
                {
                    try
                    {
                        ByteString content = await ReadContentAsync(version, cancellationToken).ConfigureAwait(false);
                        metadata = WotDependencyGraph.ReadMetadata(content, Bounds.MaxJsonDepth);
                    }
                    catch (Exception exception) when (exception is IOException or ServiceResultException or
                        UnauthorizedAccessException)
                    {
                        metadata = new WotResourceDependencies(version.Digest, [], [], [], [], exception.Message);
                    }
                    metadataByDigest.Add(version.DigestHex, metadata);
                }
                return version.WithDocumentMetadata(
                    version.DocumentId, version.Title, version.BaseUri, version.ModelVersion, metadata);
            }
        }
    }
}
