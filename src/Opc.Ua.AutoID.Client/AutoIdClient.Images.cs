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
