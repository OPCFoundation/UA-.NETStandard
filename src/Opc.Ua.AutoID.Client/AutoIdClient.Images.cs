// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Opc.Ua.Client.FileSystem;

namespace Opc.Ua.AutoID.Client
{
    public sealed partial class AutoIdClient
    {
        /// <summary>
        /// Discovers the FileType images of a reader, with MIME type and other metadata populated.
        /// </summary>
        /// <remarks>
        /// Returns detached file handles because Images is a FolderType, not a FileDirectoryType.
        /// Use the standard file client's asynchronous read operations; no image encoding is assumed.
        /// An absent optional Images folder yields no files; rejected browses and reads propagate.
        /// </remarks>
        public IAsyncEnumerable<UaFileInfo> EnumerateImagesAsync(
            AutoIdReaderEntry reader,
            CancellationToken cancellationToken = default)
        {
            if (reader is null)
            {
                throw new ArgumentNullException(nameof(reader));
            }
            return EnumerateImagesCoreAsync(reader.NodeId, cancellationToken);
        }

        /// <summary>
        /// Browses the optional image folder and resolves its files through the standard FileType client.
        /// </summary>
        private async IAsyncEnumerable<UaFileInfo> EnumerateImagesCoreAsync(
            NodeId readerId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArrayOf<ReferenceDescription> children = await BrowseChildrenAsync(
                readerId, NodeClass.Object, cancellationToken).ConfigureAwait(false);
            int ns = Session.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri);
            for (int i = 0; i < children.Count; i++)
            {
                ReferenceDescription folder = children[i];
                if (ns < 0 || folder.BrowseName != new QualifiedName("Images", (ushort)ns))
                {
                    continue;
                }
                NodeId folderId = ExpandedNodeId.ToNodeId(folder.NodeId, Session.NamespaceUris);
                ArrayOf<ReferenceDescription> files = await BrowseReferencesAsync(
                    folderId, BrowseDirection.Forward, Types.ReferenceTypeIds.Organizes,
                    NodeClass.Object, cancellationToken).ConfigureAwait(false);
                FileSystemClient owner = FileSystemClient.OpenServerFileSystem(Session);
                for (int j = 0; j < files.Count; j++)
                {
                    ReferenceDescription file = files[j];
                    NodeId fileId = ExpandedNodeId.ToNodeId(file.NodeId, Session.NamespaceUris);
                    NodeId typeId = ExpandedNodeId.ToNodeId(file.TypeDefinition, Session.NamespaceUris);
                    if (fileId.IsNull || typeId.IsNull ||
                        !await Session.NodeCache.IsTypeOfAsync(
                            typeId, global::Opc.Ua.ObjectTypeIds.FileType, cancellationToken).ConfigureAwait(false))
                    {
                        continue;
                    }
                    var image = new UaFileInfo(owner, fileId, file.BrowseName);
                    await image.RefreshAsync(cancellationToken).ConfigureAwait(false);
                    yield return image;
                }
            }
        }
    }
}
