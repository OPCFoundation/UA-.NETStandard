/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Opc.Ua;
using Opc.Ua.OpenUsd;
using Opc.Ua.XRegistry;

namespace Opc.Ua.OpenUsd.Server
{
    /// <summary>
    /// One artist-authored USD layer the server delivers through the address space
    /// (spec §5.15, conformance unit OU-AssetDelivery).
    /// </summary>
    public sealed class ServedAsset
    {
        public ServedAsset(string identifier, OpenUsdAssetKindEnum kind, byte[] bytes, string mediaType = "model/vnd.usda")
        {
            Identifier = identifier;
            Kind = kind;
            Bytes = bytes;
            MediaType = mediaType;
        }

        public string Identifier { get; }
        public OpenUsdAssetKindEnum Kind { get; }
        public byte[] Bytes { get; }
        public string MediaType { get; }
    }

    /// <summary>
    /// Bootstraps read-only USD resources and their first exact Versions in the
    /// server's artifact registry. Stage Assets folders are non-owning views.
    /// </summary>
    public static class UsdAssetDelivery
    {
        /// <summary>
        /// Allocates a distinct asset container from the stage's name and reuses it for that stage.
        /// Call before the facility's NodeId assignment and registration.
        /// </summary>
        public static ArrayOf<OpenUsdAssetState> AttachStageAssets(
            ISystemContext context, OpenUsdStageState stage, ushort openUsdNs, ArrayOf<ServedAsset> assets)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (stage is null)
            {
                throw new ArgumentNullException(nameof(stage));
            }
            return AttachStageAssets(
                context, stage, openUsdNs, assets,
                s_defaultContainerIds.GetValue(stage, key => AllocateStageContainerId(context, key)));
        }

        /// <summary>
        /// Registers immutable layers in an asset container and organizes those logical
        /// Resources through the stage's Assets view. Repeated definitions must have the
        /// same bytes, kind and content type. Different stages can share one container.
        /// Call before the facility's NodeId assignment and registration.
        /// </summary>
        /// <exception cref="ArgumentException">The container identifier is not symbolic.</exception>
        /// <exception cref="InvalidOperationException">
        /// The stage is not under an OpenUSD root, or an existing resource has a conflicting definition.
        /// </exception>
        public static ArrayOf<OpenUsdAssetState> AttachStageAssets(
            ISystemContext context,
            OpenUsdStageState stage,
            ushort openUsdNs,
            ArrayOf<ServedAsset> assets,
            string assetContainerId)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (stage is null)
            {
                throw new ArgumentNullException(nameof(stage));
            }
            if (string.IsNullOrEmpty(assetContainerId) ||
                XRegistryIdentifier.FromSourceIdentity(assetContainerId) != assetContainerId)
            {
                throw new ArgumentException("The asset container identifier must be symbolic.", nameof(assetContainerId));
            }
            if (stage.Parent is not FolderState { Parent: OpenUsdRootState root })
            {
                throw new InvalidOperationException("The stage must belong to the OpenUSD facility's Stages folder.");
            }

            OpenUsdArtifactRegistryState registry = root.Artifacts ?? root.CreateOrReplaceArtifacts(
                context, context.CreateOpenUsdRootType_Artifacts(root, forInstance: true));
            registry.RegistryId!.Value = "openusd-artifacts";
            registry.AddName(context).Name!.Value = "OpenUSD artifacts";
            registry.DisplayName = new LocalizedText(registry.Name!.Value);
            registry.AddXid(context).Xid!.Value = "/";
            registry.AddEpoch(context).Epoch!.Value = 1;
            if (registry.Capabilities is null)
            {
                registry.AddSpecVersion(context).SpecVersion!.Value = "0.1.0";
                registry.AddCapabilities(context);
                WireReadOnlyFile(
                    registry.Capabilities!,
                    Encoding.UTF8.GetBytes(
                        "{\"available\":{\"entities\":{\"mutable\":false}},\"specversions\":[\"0.1.0\"],\"mutable\":[]}"),
                    "application/json");
            }

            OpenUsdAssetGroupState? group = registry.FindChild(
                context, new QualifiedName(assetContainerId, openUsdNs)) as OpenUsdAssetGroupState;
            if (group is null)
            {
                group = context.CreateInstanceOfOpenUsdAssetGroupType(
                    registry, new QualifiedName(assetContainerId, openUsdNs));
                group.ReferenceTypeId = ReferenceTypeIds.Organizes;
                group.GroupId!.Value = assetContainerId;
                group.AssetContainerId!.Value = assetContainerId;
                group.Name!.Value = assetContainerId;
                group.DisplayName = new LocalizedText(assetContainerId);
                group.AddXid(context).Xid!.Value = "/usdassetgroups/" + assetContainerId;
                group.AddEpoch(context).Epoch!.Value = 1;
                registry.AddChild(group);
            }

            var created = new List<OpenUsdAssetState>();
            FolderState folder = stage.Assets ?? stage.CreateOrReplaceAssets(
                context, context.CreateOpenUsdStageType_Assets(stage, forInstance: true));
            foreach (ServedAsset a in assets)
            {
                string identifier = a.Identifier;
                while (identifier.StartsWith("./", StringComparison.Ordinal))
                {
                    identifier = identifier[2..];
                }
                if (string.IsNullOrEmpty(identifier))
                {
                    throw new ArgumentException("A served asset must have an authored identifier.", nameof(assets));
                }
                byte[] bytes = (byte[])a.Bytes.Clone();
                byte[] digest;
#pragma warning disable CA1850 // Prefer static HashData (net48/netstandard2.0 compatibility)
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    digest = sha.ComputeHash(bytes);
                }
#pragma warning restore CA1850

                var siblings = new List<BaseInstanceState>();
                group.GetChildren(context, siblings);
                var resourceIds = new List<string>();
                OpenUsdAssetState? asset = null;
                foreach (BaseInstanceState child in siblings)
                {
                    if (child is OpenUsdAssetState resource)
                    {
                        resourceIds.Add(resource.ResourceId!.Value);
                        if (resource.AssetIdentifier!.Value == identifier)
                        {
                            asset = resource;
                        }
                    }
                }
                if (asset is null)
                {
                    string resourceId = XRegistryIdentifier.FromSourceIdentity(identifier, resourceIds);
                    string xid = group.Xid!.Value + "/usdassets/" + resourceId;
                    asset = CreateAsset(context, group, openUsdNs, resourceId, identifier, xid, a, bytes, digest);
                    OpenUsdAssetVersionsState versions = asset.CreateOrReplaceVersions(
                        context, context.CreateOpenUsdAssetType_Versions(asset, forInstance: true));
                    OpenUsdAssetState version = CreateAsset(
                        context, versions, openUsdNs, "1", identifier, xid + "/versions/1", a, bytes, digest);
                    version.ResourceId!.Value = resourceId;
                    versions.AddChild(version);
                    group.AddChild(asset);
                }
                else if (asset.AssetKind!.Value != a.Kind ||
                    asset.ContentType!.Value != a.MediaType ||
                    !asset.Digest!.Value.Span.SequenceEqual(digest))
                {
                    throw new InvalidOperationException(
                        $"The immutable artifact '{assetContainerId}/{identifier}' is already defined differently.");
                }

                if (!folder.ReferenceExists(ReferenceTypeIds.Organizes, false, asset.NodeId))
                {
                    folder.AddReference(ReferenceTypeIds.Organizes, false, asset.NodeId);
                }
                if (!asset.ReferenceExists(ReferenceTypeIds.Organizes, true, folder.NodeId))
                {
                    asset.AddReference(ReferenceTypeIds.Organizes, true, folder.NodeId);
                }
                created.Add(asset);
            }
            return created;
        }

        /// <summary>
        /// Allocates a symbolic stage container without taking an existing registry group's identifier.
        /// </summary>
        private static string AllocateStageContainerId(ISystemContext context, OpenUsdStageState stage)
        {
            if (stage.Parent is not FolderState { Parent: OpenUsdRootState root })
            {
                throw new InvalidOperationException("The stage must belong to the OpenUSD facility's Stages folder.");
            }

            var children = new List<BaseInstanceState>();
            root.Artifacts?.GetChildren(context, children);
            var containerIds = new List<string>();
            foreach (BaseInstanceState child in children)
            {
                if (child is OpenUsdAssetGroupState group)
                {
                    containerIds.Add(group.GroupId!.Value);
                }
            }
            return XRegistryIdentifier.FromSourceIdentity(
                stage.BrowseName.Name ?? throw new ArgumentException("The stage must be named.", nameof(stage)),
                containerIds);
        }

        private static OpenUsdAssetState CreateAsset(
            ISystemContext context,
            NodeState parent,
            ushort openUsdNs,
            string browseName,
            string identifier,
            string xid,
            ServedAsset definition,
            byte[] bytes,
            byte[] digest)
        {
            OpenUsdAssetState asset = context.CreateInstanceOfOpenUsdAssetType(
                parent, new QualifiedName(browseName, openUsdNs));
            asset.ReferenceTypeId = ReferenceTypeIds.Organizes;
            asset.ResourceId!.Value = browseName;
            asset.Name!.Value = identifier;
            asset.DisplayName = new LocalizedText(identifier);
            asset.AssetIdentifier!.Value = identifier;
            asset.AssetKind!.Value = definition.Kind;
            asset.AddVersionId(context).VersionId!.Value = "1";
            asset.AddXid(context).Xid!.Value = xid;
            asset.AddEpoch(context).Epoch!.Value = 1;
            asset.AddContentType(context).ContentType!.Value = definition.MediaType;
            asset.CreateOrReplaceMediaType(
                context, context.CreateOpenUsdAssetType_MediaType(asset, forInstance: true)).Value = definition.MediaType;
            asset.CreateOrReplaceDigest(
                context, context.CreateOpenUsdAssetType_Digest(asset, forInstance: true)).Value = (ByteString)digest;
            asset.CreateOrReplaceDigestAlgorithm(
                context, context.CreateOpenUsdAssetType_DigestAlgorithm(asset, forInstance: true))
                .Value = OpenUsdDigestAlgorithmEnum.Sha256;
            WireReadOnlyFile(asset, bytes, definition.MediaType);
            return asset;
        }

        // Attaches read-only Open/Read/Close/GetPosition/SetPosition handlers over an
        // in-memory byte[] to an existing (generator-materialised) FileState.
        private static void WireReadOnlyFile(FileState file, byte[] bytes, string mediaType)
        {
            var backing = new ReadOnlyFileBacking(bytes);

            if (file.Size != null)
            {
                file.Size.Value = (ulong)bytes.Length;
            }
            if (file.Writable != null)
            {
                file.Writable.Value = false;
            }
            if (file.UserWritable != null)
            {
                file.UserWritable.Value = false;
            }
            if (file.MimeType != null)
            {
                file.MimeType.Value = mediaType;
            }
            if (file.Open != null)
            {
                file.Open.OnCall = backing.OnOpen;
            }
            if (file.Read != null)
            {
                file.Read.OnCall = backing.OnRead;
            }
            if (file.Close != null)
            {
                file.Close.OnCall = backing.OnClose;
            }
            if (file.GetPosition != null)
            {
                file.GetPosition.OnCall = backing.OnGetPosition;
            }
            if (file.SetPosition != null)
            {
                file.SetPosition.OnCall = backing.OnSetPosition;
            }
            // Read-only: any Write method stays unwired (server rejects the call).
        }

        /// <summary>
        /// Remembers default container allocations by stage instance without retaining discarded stages.
        /// </summary>
        private static readonly ConditionalWeakTable<OpenUsdStageState, string> s_defaultContainerIds = new();

        /// <summary>
        /// Read-only in-memory backing for a Part 5 <see cref="FileState"/>: hands out
        /// per-Open file handles over an independent position cursor, bounded to a small
        /// number of concurrent handles.
        /// </summary>
        private sealed class ReadOnlyFileBacking
        {
            private const int MaxConcurrentHandles = 8;
            private readonly byte[] m_bytes;
            private readonly Lock m_gate = new();
            private readonly Dictionary<uint, long> m_positions = new();
            private uint m_nextHandle;

            public ReadOnlyFileBacking(byte[] bytes)
            {
                m_bytes = bytes;
            }

            public ServiceResult OnOpen(ISystemContext context, MethodState method,
                NodeId objectId, byte mode, ref uint fileHandle)
            {
                // Part 5 Open mode: bit 0 = Read. Reject write/append/erase (this is read-only).
                if ((mode & 0x01) == 0 || (mode & 0x06) != 0)
                {
                    fileHandle = 0;
                    return ServiceResult.Create(StatusCodes.BadInvalidArgument,
                        "This file is read-only; open with Read mode only.");
                }
                lock (m_gate)
                {
                    if (m_positions.Count >= MaxConcurrentHandles)
                    {
                        fileHandle = 0;
                        return ServiceResult.Create(StatusCodes.BadTooManyOperations,
                            "Too many concurrent file handles.");
                    }
                    fileHandle = ++m_nextHandle;
                    m_positions[fileHandle] = 0;
                }
                return ServiceResult.Good;
            }

            public ServiceResult OnRead(ISystemContext context, MethodState method,
                NodeId objectId, uint fileHandle, int length, ref ByteString data)
            {
                if (length < 0)
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidArgument, "Negative length.");
                }
                lock (m_gate)
                {
                    if (!m_positions.TryGetValue(fileHandle, out long pos))
                    {
                        return ServiceResult.Create(StatusCodes.BadInvalidState, "File handle not open.");
                    }
                    long remaining = m_bytes.Length - pos;
                    int n = (int)Math.Max(0, Math.Min(length, remaining));
                    var buffer = new byte[n];
                    Array.Copy(m_bytes, pos, buffer, 0, n);
                    m_positions[fileHandle] = pos + n;
                    data = (ByteString)buffer;
                }
                return ServiceResult.Good;
            }

            public ServiceResult OnClose(ISystemContext context, MethodState method,
                NodeId objectId, uint fileHandle)
            {
                lock (m_gate)
                {
                    m_positions.Remove(fileHandle);
                }
                return ServiceResult.Good;
            }

            public ServiceResult OnGetPosition(ISystemContext context, MethodState method,
                NodeId objectId, uint fileHandle, ref ulong position)
            {
                lock (m_gate)
                {
                    if (!m_positions.TryGetValue(fileHandle, out long pos))
                    {
                        return ServiceResult.Create(StatusCodes.BadInvalidState, "File handle not open.");
                    }
                    position = (ulong)pos;
                }
                return ServiceResult.Good;
            }

            public ServiceResult OnSetPosition(ISystemContext context, MethodState method,
                NodeId objectId, uint fileHandle, ulong position)
            {
                lock (m_gate)
                {
                    if (!m_positions.ContainsKey(fileHandle))
                    {
                        return ServiceResult.Create(StatusCodes.BadInvalidState, "File handle not open.");
                    }
                    m_positions[fileHandle] = (long)Math.Min(position, (ulong)m_bytes.Length);
                }
                return ServiceResult.Good;
            }
        }
    }
}
