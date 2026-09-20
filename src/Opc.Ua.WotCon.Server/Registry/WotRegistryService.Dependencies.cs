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
            foreach (WotResourceGroup group in snapshot.Groups.Values)
            {
                ImmutableDictionary<string, WotResource> resources = group.Resources;
                foreach (WotResource resource in group.Resources.Values)
                {
                    ImmutableArray<WotResourceVersion>.Builder? versions = null;
                    for (int i = 0; i < resource.Versions.Length; i++)
                    {
                        WotResourceVersion version = resource.Versions[i];
                        if (!version.HasContent || version.Dependencies is not null)
                        {
                            continue;
                        }
                        WotResourceDependencies metadata;
                        try
                        {
                            ByteString content = await ReadContentAsync(version, cancellationToken).ConfigureAwait(false);
                            metadata = WotDependencyGraph.ReadMetadata(content, Bounds.MaxJsonDepth);
                        }
                        catch (Exception exception) when (exception is IOException or ServiceResultException or
                            UnauthorizedAccessException)
                        {
                            metadata = new WotResourceDependencies(
                                version.Digest, [], [], [], [], exception.Message);
                        }
                        versions ??= resource.Versions.ToBuilder();
                        versions[i] = version.WithDocumentMetadata(
                            version.DocumentId, version.Title, version.BaseUri, version.ModelVersion, metadata);
                    }
                    if (versions is not null)
                    {
                        resources = resources.SetItem(
                            resource.ResourceId, resource.With(versions: versions.ToImmutable()));
                    }
                }
                if (!ReferenceEquals(resources, group.Resources))
                {
                    snapshot = snapshot.WithGroup(group.WithResources(resources, group.Epoch), snapshot.Generation);
                }
            }
            return snapshot;
        }
    }
}
