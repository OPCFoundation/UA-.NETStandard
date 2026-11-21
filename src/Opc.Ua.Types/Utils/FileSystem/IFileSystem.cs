// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;

namespace Opc.Ua
{
    /// <summary>
    /// File system abstraction for testing and in memory source management
    /// </summary>
    public interface IFileSystem
    {
        /// <summary>
        /// Tests path exists
        /// </summary>
        /// <param name="path"></param>
        /// <param name="isDirectory"></param>
        /// <returns></returns>
        bool Exists(string path, bool isDirectory = false);

        /// <summary>
        /// Delete file or directory
        /// </summary>
        /// <param name="path"></param>
        /// <param name="isDirectory"></param>
        /// <returns></returns>
        void Delete(string path, bool isDirectory = false);

        /// <summary>
        /// Create readable stream
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        Stream OpenRead(string path);

        /// <summary>
        /// Create writable stream
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        Stream OpenWrite(string path);

        /// <summary>
        /// Publishes <paramref name="sourcePath"/> as <paramref name="destinationPath"/>
        /// in a single indivisible step, overwriting any existing destination.
        /// <para>
        /// Durable writers stage content under a temporary name and then publish it, so
        /// that an interrupted write can never leave a partially written file visible at
        /// the destination. The source is consumed by the operation. Observers of
        /// <paramref name="destinationPath"/> only ever see the complete previous
        /// content or the complete new content, never an intermediate state.
        /// </para>
        /// </summary>
        /// <param name="sourcePath">The staged file to publish.</param>
        /// <param name="destinationPath">The final name to publish it under.</param>
        void Replace(string sourcePath, string destinationPath);

        /// <summary>
        /// Get last write time of file
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        DateTime GetLastWriteTime(string path);

        /// <summary>
        /// Get length of file
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        long GetLength(string path);
    }
}
