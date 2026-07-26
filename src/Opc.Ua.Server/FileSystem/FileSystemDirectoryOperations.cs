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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Shared implementation of FileDirectoryType method bodies.
    /// </summary>
    internal static class FileSystemDirectoryOperations
    {
        public static async ValueTask<CreateDirectoryMethodStateResult> CreateDirectoryAsync(
            IFileSystemHost host,
            string providerPath,
            string directoryName,
            CancellationToken cancellationToken)
        {
            if (!CanCreate(host, out ServiceResult accessResult))
            {
                return new CreateDirectoryMethodStateResult { ServiceResult = accessResult };
            }
            if (string.IsNullOrEmpty(directoryName))
            {
                return new CreateDirectoryMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidArgument, "Directory name required.")
                };
            }

            string newPath = host.CombineProviderPath(providerPath, directoryName);
            try
            {
                await host.Provider.CreateDirectoryAsync(newPath, cancellationToken).ConfigureAwait(false);
                await host.OnProviderChangedAsync(cancellationToken).ConfigureAwait(false);
                return new CreateDirectoryMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    DirectoryNodeId = host.BuildDirectoryNodeId(newPath)
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
            catch (NotSupportedException ex)
            {
                return new CreateDirectoryMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadNotSupported,
                        "Directory creation is not supported.")
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

        public static async ValueTask<CreateFileMethodStateResult> CreateFileAsync(
            IFileSystemHost host,
            ISystemContext context,
            string providerPath,
            string fileName,
            bool requestFileOpen,
            CancellationToken cancellationToken)
        {
            if (!CanCreate(host, out ServiceResult accessResult))
            {
                return new CreateFileMethodStateResult { ServiceResult = accessResult };
            }
            if (string.IsNullOrEmpty(fileName))
            {
                return new CreateFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidArgument, "File name required.")
                };
            }

            NodeId sessionId = NodeId.Null;
            if (requestFileOpen &&
                !FileSystemNodeManager.TryGetSessionId(context, out sessionId, out ServiceResult sessionResult))
            {
                return new CreateFileMethodStateResult { ServiceResult = sessionResult };
            }

            string newPath = host.CombineProviderPath(providerPath, fileName);
            NodeId fileNodeId = host.BuildFileNodeId(newPath);
            try
            {
                await host.Provider.CreateFileAsync(newPath, cancellationToken).ConfigureAwait(false);
                await host.OnProviderChangedAsync(cancellationToken).ConfigureAwait(false);
                if (!requestFileOpen)
                {
                    return new CreateFileMethodStateResult
                    {
                        ServiceResult = ServiceResult.Good,
                        FileNodeId = fileNodeId,
                        FileHandle = 0u
                    };
                }

                FileHandle? handle = host.GetOrCreateHandle(fileNodeId, newPath);
                if (handle == null)
                {
                    return new CreateFileMethodStateResult
                    {
                        ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidState,
                            "Failed to obtain file handle.")
                    };
                }

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
            catch (NotSupportedException ex)
            {
                return new CreateFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadNotSupported,
                        "File creation is not supported.")
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

        public static async ValueTask<DeleteFileMethodStateResult> DeleteAsync(
            IFileSystemHost host,
            NodeId objectToDelete,
            CancellationToken cancellationToken)
        {
            if (!host.Provider.IsWritable || !host.AllowDelete)
            {
                return new DeleteFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadUserAccessDenied,
                        "Deleting file-system objects is not allowed.")
                };
            }
            if (!host.TryGetProviderPath(objectToDelete, out string providerPath, out _, out bool isRoot))
            {
                return new DeleteFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidState,
                        "Not a file-system object.")
                };
            }
            if (isRoot)
            {
                return new DeleteFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadUserAccessDenied,
                        "Cannot delete the file-system root.")
                };
            }

            try
            {
                await host.Provider.DeleteAsync(providerPath, cancellationToken).ConfigureAwait(false);
                host.ForgetHandle(objectToDelete);
                await host.OnProviderChangedAsync(cancellationToken).ConfigureAwait(false);
                return new DeleteFileMethodStateResult { ServiceResult = ServiceResult.Good };
            }
            catch (FileNotFoundException ex)
            {
                return CreateNotFoundDeleteResult(ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                return CreateNotFoundDeleteResult(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return new DeleteFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied,
                        "Failed to delete file-system object.")
                };
            }
            catch (NotSupportedException ex)
            {
                return new DeleteFileMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadNotSupported,
                        "Deleting file-system objects is not supported.")
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

        public static async ValueTask<MoveOrCopyMethodStateResult> MoveOrCopyAsync(
            IFileSystemHost host,
            NodeId objectToMoveOrCopy,
            NodeId targetDirectory,
            bool createCopy,
            string newName,
            CancellationToken cancellationToken)
        {
            if (!host.Provider.IsWritable || !host.AllowMoveOrCopy)
            {
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadUserAccessDenied,
                        "Moving or copying file-system objects is not allowed.")
                };
            }
            if (!host.TryGetProviderPath(objectToMoveOrCopy, out string sourcePath, out bool sourceIsDirectory,
                    out bool sourceIsRoot) ||
                sourceIsRoot)
            {
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidArgument,
                        "Source is not a directory or file.")
                };
            }
            if (!host.TryGetProviderPath(targetDirectory, out string targetDirectoryPath, out bool targetIsDirectory,
                    out _) ||
                !targetIsDirectory)
            {
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(StatusCodes.BadInvalidArgument,
                        "Target is not a directory.")
                };
            }

            string sourceName = ProviderPathName(sourcePath);
            string finalName = !string.IsNullOrEmpty(newName) ? newName : sourceName;
            string targetPath = host.CombineProviderPath(targetDirectoryPath, finalName);

            try
            {
                if (createCopy)
                {
                    await host.Provider.CopyAsync(sourcePath, targetPath, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await host.Provider.MoveAsync(sourcePath, targetPath, cancellationToken).ConfigureAwait(false);
                    host.ForgetHandle(objectToMoveOrCopy);
                }

                await host.OnProviderChangedAsync(cancellationToken).ConfigureAwait(false);
                NodeId newNodeId = sourceIsDirectory
                    ? host.BuildDirectoryNodeId(targetPath)
                    : host.BuildFileNodeId(targetPath);
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Good,
                    NewNodeId = newNodeId
                };
            }
            catch (FileNotFoundException ex)
            {
                return CreateNotFoundMoveOrCopyResult(ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                return CreateNotFoundMoveOrCopyResult(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied,
                        "Failed to move or copy.")
                };
            }
            catch (NotSupportedException ex)
            {
                return new MoveOrCopyMethodStateResult
                {
                    ServiceResult = ServiceResult.Create(ex, StatusCodes.BadNotSupported,
                        "Moving or copying is not supported.")
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

        private static bool CanCreate(IFileSystemHost host, out ServiceResult result)
        {
            if (!host.Provider.IsWritable || !host.AllowCreate)
            {
                result = ServiceResult.Create(StatusCodes.BadUserAccessDenied,
                    "Creating file-system objects is not allowed.");
                return false;
            }

            result = ServiceResult.Good;
            return true;
        }

        private static DeleteFileMethodStateResult CreateNotFoundDeleteResult(Exception ex)
        {
            return new DeleteFileMethodStateResult
            {
                ServiceResult = ServiceResult.Create(ex, StatusCodes.BadNotFound,
                    "File-system object not found.")
            };
        }

        private static MoveOrCopyMethodStateResult CreateNotFoundMoveOrCopyResult(Exception ex)
        {
            return new MoveOrCopyMethodStateResult
            {
                ServiceResult = ServiceResult.Create(ex, StatusCodes.BadNotFound,
                    "Source not found.")
            };
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
