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
using System.IO;
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
            : base(null)
        {
            ProviderPath = providerPath ?? string.Empty;
            IsRoot = isRoot;

            TypeDefinitionId = ObjectTypeIds.FileDirectoryType;
            SymbolicName = ProviderPath;
            NodeId = nodeId;
            BrowseName = new QualifiedName(displayName);
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
            var manager = context?.SystemHandle as FileSystemNodeManager;
            var browser = new DirectoryBrowser(
                context!, view, referenceType, includeSubtypes,
                browseDirection, browseName, additionalReferences,
                internalOnly, manager!, this);
            PopulateBrowser(context!, browser);
            return browser;
        }

        protected override void PopulateBrowser(ISystemContext context, NodeBrowser browser)
        {
            base.PopulateBrowser(context, browser);

            if (context?.SystemHandle is not FileSystemNodeManager manager)
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
                    NodeId parentId = manager.GetParentNodeId(ProviderPath);
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
            if (context?.SystemHandle is not FileSystemNodeManager manager)
            {
                return new CreateDirectoryMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidState,
                        "Node manager unavailable.")
                };
            }
            if (string.IsNullOrEmpty(directoryName))
            {
                return new CreateDirectoryMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidArgument,
                        "Directory name required.")
                };
            }
            string newPath = manager.CombineProviderPath(ProviderPath, directoryName);
            try
            {
                await manager.Provider.CreateDirectoryAsync(newPath, cancellationToken).ConfigureAwait(false);
                return new CreateDirectoryMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    DirectoryNodeId = FileSystemNodeId.BuildDirectory(newPath, manager.NamespaceIndex)
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                return new CreateDirectoryMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied,
                        "Failed to create directory.")
                };
            }
            catch (IOException ex)
            {
                return new CreateDirectoryMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadBrowseNameDuplicated,
                        "Directory or file with same name exists.")
                };
            }
        }

        private async ValueTask<CreateFileMethodStateResult> OnCreateFileAsync(
            ISystemContext context, MethodState method, NodeId objectId,
            string fileName, bool requestFileOpen, CancellationToken cancellationToken)
        {
            if (context?.SystemHandle is not FileSystemNodeManager manager)
            {
                return new CreateFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidState,
                        "Node manager unavailable.")
                };
            }
            if (string.IsNullOrEmpty(fileName))
            {
                return new CreateFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidArgument,
                        "File name required.")
                };
            }

            NodeId sessionId = NodeId.Null;
            if (requestFileOpen)
            {
                if (context is not ISessionSystemContext sessionContext ||
                    sessionContext.SessionId is not { IsNull: false } validSessionId)
                {
                    return new CreateFileMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(
                            StatusCodes.BadSessionIdInvalid,
                            "A valid Session is required to open a file.")
                    };
                }

                sessionId = validSessionId;
            }

            string newPath = manager.CombineProviderPath(ProviderPath, fileName);
            try
            {
                if (!requestFileOpen)
                {
                    await manager.Provider.CreateFileAsync(newPath, cancellationToken).ConfigureAwait(false);
                    return new CreateFileMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        FileNodeId = FileSystemNodeId.BuildFile(newPath, manager.NamespaceIndex),
                        FileHandle = 0u
                    };
                }

                NodeId fileNodeId = FileSystemNodeId.BuildFile(newPath, manager.NamespaceIndex);
                FileHandle? handle = manager.GetOrCreateHandle(fileNodeId, newPath);
                if (handle == null)
                {
                    return new CreateFileMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidState,
                            "Failed to obtain file handle.")
                    };
                }
                // Open with write + erase to mirror the spec's
                // "create + open for write" semantics.
                ServiceResult openResult = handle.Open(sessionId, 0x6, out uint fileHandle);
                return new CreateFileMethodStateResult
                {
                    ServiceResult = openResult,
                    FileNodeId = fileNodeId,
                    FileHandle = fileHandle
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                return new CreateFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied,
                        "Failed to create file.")
                };
            }
            catch (IOException ex)
            {
                return new CreateFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadBrowseNameDuplicated,
                        "Directory or file with same name exists.")
                };
            }
        }

        private async ValueTask<DeleteFileMethodStateResult> OnDeleteFileSystemObjectAsync(
            ISystemContext context, MethodState method, NodeId objectId,
            NodeId objectToDelete, CancellationToken cancellationToken)
        {
            if (context?.SystemHandle is not FileSystemNodeManager manager)
            {
                return new DeleteFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidState,
                        "Node manager unavailable.")
                };
            }
            if (!FileSystemNodeId.TryParse(objectToDelete, out FileSystemNodeId parsed))
            {
                return new DeleteFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidState,
                        "Not a file-system object.")
                };
            }
            if (parsed.RootType == FileSystemNodeId.Root)
            {
                return new DeleteFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadUserAccessDenied,
                        "Cannot delete the file-system root.")
                };
            }
            try
            {
                await manager.Provider.DeleteAsync(parsed.ProviderPath, cancellationToken).ConfigureAwait(false);
                manager.ForgetHandle(objectToDelete);
                return new DeleteFileMethodStateResult { ServiceResult = ServiceResult.Good };
            }
            catch (FileNotFoundException ex)
            {
                return new DeleteFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadNotFound,
                        "File-system object not found.")
                };
            }
            catch (DirectoryNotFoundException ex)
            {
                return new DeleteFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadNotFound,
                        "File-system object not found.")
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                return new DeleteFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied,
                        "Failed to delete file-system object.")
                };
            }
            catch (IOException ex)
            {
                return new DeleteFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied,
                        "Failed to delete file-system object.")
                };
            }
        }

        private async ValueTask<MoveOrCopyMethodStateResult> OnMoveOrCopyAsync(
            ISystemContext context, MethodState method, NodeId objectId,
            NodeId objectToMoveOrCopy, NodeId targetDirectory,
            bool createCopy, string newName, CancellationToken cancellationToken)
        {
            if (context?.SystemHandle is not FileSystemNodeManager manager)
            {
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidState,
                        "Node manager unavailable.")
                };
            }
            if (!FileSystemNodeId.TryParse(objectToMoveOrCopy, out FileSystemNodeId source) ||
                source.RootType == FileSystemNodeId.Root)
            {
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidArgument,
                        "Source is not a directory or file.")
                };
            }
            if (!FileSystemNodeId.TryParse(targetDirectory, out FileSystemNodeId target) ||
                target.RootType == FileSystemNodeId.File)
            {
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidArgument,
                        "Target is not a directory.")
                };
            }

            string sourceName = ProviderPathName(source.ProviderPath);
            string finalName = !string.IsNullOrEmpty(newName) ? newName : sourceName;
            string targetPath = manager.CombineProviderPath(target.ProviderPath, finalName);

            try
            {
                if (createCopy)
                {
                    await manager.Provider.CopyAsync(source.ProviderPath, targetPath, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await manager.Provider.MoveAsync(source.ProviderPath, targetPath, cancellationToken)
                        .ConfigureAwait(false);
                    manager.ForgetHandle(objectToMoveOrCopy);
                }

                NodeId newNodeId = source.RootType == FileSystemNodeId.File
                    ? FileSystemNodeId.BuildFile(targetPath, manager.NamespaceIndex)
                    : FileSystemNodeId.BuildDirectory(targetPath, manager.NamespaceIndex);
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    NewNodeId = newNodeId
                };
            }
            catch (FileNotFoundException ex)
            {
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadNotFound,
                        "Source not found.")
                };
            }
            catch (DirectoryNotFoundException ex)
            {
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadNotFound,
                        "Source not found.")
                };
            }
            catch (UnauthorizedAccessException ex)
            {
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied,
                        "Failed to move or copy.")
                };
            }
            catch (IOException ex)
            {
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadBrowseNameDuplicated,
                        "Failed to move or copy.")
                };
            }
        }

        private static string ProviderPathName(string providerPath)
        {
            if (string.IsNullOrEmpty(providerPath))
            {
                return string.Empty;
            }
            int slash = providerPath.LastIndexOf('/');
            return slash < 0 ? providerPath : providerPath[(slash + 1)..];
        }
    }
}
