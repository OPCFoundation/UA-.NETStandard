// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// A file of a result's <c>FileSet</c>.
    /// </summary>
    /// <param name="NodeId">The NodeId of the <c>ResultFileType</c> object.</param>
    /// <param name="Name">The browse name of the result file.</param>
    public sealed record LadsResultFile(NodeId NodeId, string Name)
    {
        /// <summary>
        /// Gets the file name.
        /// </summary>
        public string? FileName { get; init; }

        /// <summary>
        /// Gets the MIME type.
        /// </summary>
        public string? MimeType { get; init; }

        /// <summary>
        /// Gets the URL of a file stored outside the server, when present.
        /// </summary>
        public string? Url { get; init; }

        /// <summary>
        /// Gets the NodeId of the <c>File</c> object, or <see cref="NodeId.Null"/>
        /// when the file can not be downloaded through OPC UA.
        /// </summary>
        public NodeId FileId { get; init; }

        /// <summary>
        /// Gets whether the file can be downloaded through OPC UA.
        /// </summary>
        public bool CanDownload => !FileId.IsNull;
    }
}
