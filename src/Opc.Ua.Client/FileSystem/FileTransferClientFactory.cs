/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
        /// <exception cref="ArgumentNullException"></exception>
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
        /// <exception cref="ArgumentNullException"></exception>
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
        /// <exception cref="ArgumentNullException"></exception>
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
