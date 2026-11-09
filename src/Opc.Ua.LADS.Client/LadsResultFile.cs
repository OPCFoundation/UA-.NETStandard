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
