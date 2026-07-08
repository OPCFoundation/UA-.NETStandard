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

namespace Opc.Ua.Client.FileSystem
{
    /// <summary>
    /// Dependency-injected factory for per-session file-transfer clients.
    /// </summary>
    public sealed class FileTransferClientFactory
    {
        /// <summary>
        /// Initializes a new instance.
        /// </summary>
        public FileTransferClientFactory(ITelemetryContext telemetry)
        {
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        /// <summary>
        /// Creates a file-system client rooted at <paramref name="rootDirectoryId"/>.
        /// </summary>
        public FileSystemClient CreateFileSystem(
            ISession session,
            NodeId rootDirectoryId,
            FileSystemClientOptions? options = null)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            return new FileSystemClient(session, rootDirectoryId, options);
        }

        /// <summary>
        /// Creates a file-system client rooted at the standard server file-system object.
        /// </summary>
        public FileSystemClient OpenServerFileSystem(
            ISession session,
            FileSystemClientOptions? options = null)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            return FileSystemClient.OpenServerFileSystem(session, options);
        }

        /// <summary>
        /// Creates a temporary-file-transfer client rooted at
        /// <paramref name="temporaryFileTransferObjectId"/>.
        /// </summary>
        public TemporaryFileTransferClient CreateTemporaryFileTransfer(
            ISession session,
            NodeId temporaryFileTransferObjectId,
            FileSystemClientOptions? options = null)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            return new TemporaryFileTransferClient(session, temporaryFileTransferObjectId, options);
        }

        /// <summary>
        /// The shared telemetry context.
        /// </summary>
        public ITelemetryContext Telemetry { get; }
    }
}
