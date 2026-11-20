/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// How <see cref="IFileSystemProvider.OpenWriteAsync"/> should open
    /// the target file. Matches the OPC UA Part 5 §C
    /// <c>FileType.Open</c> mode bits but without the read flag.
    /// </summary>
    public enum FileWriteMode
    {
        /// <summary>
        /// Opens the file for writing; creates it if it doesn't
        /// exist; preserves existing content (the caller then issues
        /// <c>SetPosition</c> / <c>Write</c> to put data wherever it
        /// wants).
        /// </summary>
        OpenOrCreate = 0,

        /// <summary>
        /// Creates the file (truncating any existing content) and
        /// opens it for writing. Equivalent to the
        /// <c>FileType.Open</c> "Erase" bit.
        /// </summary>
        Truncate = 1,

        /// <summary>
        /// Opens the file for writing with the position pre-set to
        /// the end of the file. Equivalent to the
        /// <c>FileType.Open</c> "Append" bit.
        /// </summary>
        Append = 2
    }
}
