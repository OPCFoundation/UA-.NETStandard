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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Address-space representation of a single directory in an
    /// <see cref="IFileSystemProvider"/>. Hangs the FileDirectoryType
    /// methods (CreateFile / CreateDirectory / DeleteFileSystemObject
    /// / MoveOrCopy) on top of the provider.
    /// </summary>
    internal sealed class DirectoryObjectState : FileDirectoryState
    {
        public string ProviderPath { get; }

        public bool IsRoot { get; }

        public DirectoryObjectState(
            ISystemContext context,
            NodeId nodeId,
            string providerPath,
            string displayName,
            bool isRoot)
            : this(context, nodeId, providerPath, displayName, isRoot, host: null)
        {
        }

        public DirectoryObjectState(
            ISystemContext context,
            NodeId nodeId,
            string providerPath,
            string displayName,
            bool isRoot,
            IFileSystemHost? host)
            : base(null)
        {
            m_host = host;
            ProviderPath = providerPath ?? string.Empty;
            IsRoot = isRoot;

            TypeDefinitionId = ObjectTypeIds.FileDirectoryType;
            SymbolicName = ProviderPath;
            NodeId = nodeId;
            BrowseName = new QualifiedName(displayName, nodeId.NamespaceIndex);
            DisplayName = new LocalizedText(displayName);
            Description = LocalizedText.Null;
            WriteMask = 0;
            UserWriteMask = 0;
            EventNotifier = EventNotifiers.None;

            DeleteFileSystemObject = new DeleteFileMethodState(this)
            {
                OnCallAsync = OnDeleteFileSystemObjectAsync,
                Executable = true,
                UserExecutable = true
            };
            DeleteFileSystemObject.Create(context, MethodIds.FileDirectoryType_DeleteFileSystemObject,
                new QualifiedName(BrowseNames.DeleteFileSystemObject),
                new LocalizedText(BrowseNames.DeleteFileSystemObject), false);

            CreateFile = new CreateFileMethodState(this)
            {
                OnCallAsync = OnCreateFileAsync,
                Executable = true,
                UserExecutable = true
            };
            CreateFile.Create(context, MethodIds.FileDirectoryType_CreateFile,
                new QualifiedName(BrowseNames.CreateFile),
                new LocalizedText(BrowseNames.CreateFile), false);

            CreateDirectory = new CreateDirectoryMethodState(this)
            {
                OnCallAsync = OnCreateDirectoryAsync,
                Executable = true,
                UserExecutable = true
            };
            CreateDirectory.Create(context, MethodIds.FileDirectoryType_CreateDirectory,
                new QualifiedName(BrowseNames.CreateDirectory),
                new LocalizedText(BrowseNames.CreateDirectory), false);

            MoveOrCopy = new MoveOrCopyMethodState(this)
            {
                OnCallAsync = OnMoveOrCopyAsync,
                Executable = true,
                UserExecutable = true
            };
            MoveOrCopy.Create(context, MethodIds.FileDirectoryType_MoveOrCopy,
                new QualifiedName(BrowseNames.MoveOrCopy),
                new LocalizedText(BrowseNames.MoveOrCopy), false);
        }

        public override INodeBrowser CreateBrowser(
            ISystemContext context, ViewDescription? view, NodeId referenceType,
            bool includeSubtypes, BrowseDirection browseDirection,
            QualifiedName browseName, IEnumerable<IReference>? additionalReferences,
            bool internalOnly)
        {
            IFileSystemHost? host = ResolveHost(context);
            if (host?.UsesVirtualDirectoryBrowsing != true)
            {
                return base.CreateBrowser(context, view, referenceType, includeSubtypes,
                    browseDirection, browseName, additionalReferences, internalOnly);
            }

            var browser = new DirectoryBrowser(
                context!, view, referenceType, includeSubtypes,
                browseDirection, browseName, additionalReferences,
                internalOnly, host, this);
            PopulateBrowserSynchronized(context!, browser);
            return browser;
        }

        protected override void PopulateBrowser(ISystemContext context, NodeBrowser browser)
        {
            base.PopulateBrowser(context, browser);

            IFileSystemHost? host = ResolveHost(context);
            if (host == null)
            {
                return;
            }

            // Inverse reference to the parent: for the mount root we
            // expose a HasComponent inverse to Server.FileSystem
            // (i=16314). For nested directories the parent is another
            // directory inside this provider.
            if (browser.IsRequired(ReferenceTypeIds.HasComponent, true))
            {
                if (IsRoot)
                {
                    browser.Add(ReferenceTypeIds.HasComponent, true, ObjectIds.FileSystem);
                }
                else
                {
                    NodeId parentId = host.GetParentNodeId(ProviderPath);
                    if (!parentId.IsNull)
                    {
                        browser.Add(ReferenceTypeIds.HasComponent, true, parentId);
                    }
                }
            }
        }

        private async ValueTask<CreateDirectoryMethodStateResult> OnCreateDirectoryAsync(
            ISystemContext context, MethodState method, NodeId objectId,
            string directoryName, CancellationToken cancellationToken)
        {
            IFileSystemHost? host = ResolveHost(context);
            if (host == null)
            {
                return new CreateDirectoryMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidState,
                        "Node manager unavailable.")
                };
            }

            return await FileSystemDirectoryOperations.CreateDirectoryAsync(
                host, ProviderPath, directoryName, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<CreateFileMethodStateResult> OnCreateFileAsync(
            ISystemContext context, MethodState method, NodeId objectId,
            string fileName, bool requestFileOpen, CancellationToken cancellationToken)
        {
            IFileSystemHost? host = ResolveHost(context);
            if (host == null)
            {
                return new CreateFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidState,
                        "Node manager unavailable.")
                };
            }

            return await FileSystemDirectoryOperations.CreateFileAsync(
                host, context, ProviderPath, fileName, requestFileOpen, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<DeleteFileMethodStateResult> OnDeleteFileSystemObjectAsync(
            ISystemContext context, MethodState method, NodeId objectId,
            NodeId objectToDelete, CancellationToken cancellationToken)
        {
            IFileSystemHost? host = ResolveHost(context);
            if (host == null)
            {
                return new DeleteFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidState,
                        "Node manager unavailable.")
                };
            }

            return await FileSystemDirectoryOperations.DeleteAsync(
                host, objectToDelete, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask<MoveOrCopyMethodStateResult> OnMoveOrCopyAsync(
            ISystemContext context, MethodState method, NodeId objectId,
            NodeId objectToMoveOrCopy, NodeId targetDirectory,
            bool createCopy, string newName, CancellationToken cancellationToken)
        {
            IFileSystemHost? host = ResolveHost(context);
            if (host == null)
            {
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidState,
                        "Node manager unavailable.")
                };
            }

            return await FileSystemDirectoryOperations.MoveOrCopyAsync(
                host, objectToMoveOrCopy, targetDirectory, createCopy, newName, cancellationToken)
                .ConfigureAwait(false);
        }

        internal void DetachCallbacks()
        {
            if (DeleteFileSystemObject != null)
            {
                DeleteFileSystemObject.OnCallAsync = null;
            }
            if (CreateFile != null)
            {
                CreateFile.OnCallAsync = null;
            }
            if (CreateDirectory != null)
            {
                CreateDirectory.OnCallAsync = null;
            }
            if (MoveOrCopy != null)
            {
                MoveOrCopy.OnCallAsync = null;
            }
        }

        private IFileSystemHost? ResolveHost(ISystemContext context)
        {
            return m_host ?? context?.SystemHandle as IFileSystemHost;
        }

        private readonly IFileSystemHost? m_host;
    }
}
