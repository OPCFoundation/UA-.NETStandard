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

namespace Opc.Ua.Aas.Client.Registry
{
    /// <summary>
    /// Result of the AAS registry <c>GetSubmodel</c> document fast path.
    /// </summary>
    public sealed class AasGetSubmodelDocumentResult
    {
        /// <summary>
        /// Creates a result.
        /// </summary>
        public AasGetSubmodelDocumentResult(
            StatusCode statusCode,
            ByteString document,
            string format,
            string contentType)
        {
            StatusCode = statusCode;
            Document = document;
            Format = format ?? string.Empty;
            ContentType = contentType ?? string.Empty;
        }

        /// <summary>
        /// OPC UA Method result status.
        /// </summary>
        public StatusCode StatusCode { get; }

        /// <summary>
        /// Submodel document bytes, present only when <see cref="StatusCode"/> is good.
        /// </summary>
        public ByteString Document { get; }

        /// <summary>
        /// xRegistry format string for the document.
        /// </summary>
        public string Format { get; }

        /// <summary>
        /// Media type of the document.
        /// </summary>
        public string ContentType { get; }
    }

    /// <summary>
    /// Metadata read from one resource version file.
    /// </summary>
    public sealed class AasRegistryResourceVersionInfo
    {
        /// <summary>
        /// Creates version metadata.
        /// </summary>
        public AasRegistryResourceVersionInfo(
            NodeId resourceNodeId,
            string resourceId,
            string versionId,
            DateTime createdAt,
            DateTime modifiedAt)
        {
            if (resourceNodeId.IsNull)
            {
                throw new ArgumentException("A resource NodeId is required.", nameof(resourceNodeId));
            }

            ResourceNodeId = resourceNodeId;
            ResourceId = resourceId ?? string.Empty;
            VersionId = versionId ?? string.Empty;
            CreatedAt = createdAt;
            ModifiedAt = modifiedAt;
        }

        /// <summary>
        /// NodeId of this version file.
        /// </summary>
        public NodeId ResourceNodeId { get; }

        /// <summary>
        /// Stable resource identifier shared by all versions of the resource.
        /// </summary>
        public string ResourceId { get; }

        /// <summary>
        /// xRegistry version identifier.
        /// </summary>
        public string VersionId { get; }

        /// <summary>
        /// UTC time when this version was created.
        /// </summary>
        public DateTime CreatedAt { get; }

        /// <summary>
        /// UTC time when this version was last modified.
        /// </summary>
        public DateTime ModifiedAt { get; }
    }

    /// <summary>
    /// Bytes of a retrieved package after mandatory digest verification.
    /// </summary>
    public sealed class AasVerifiedPackage
    {
        /// <summary>
        /// Creates a verified package value.
        /// </summary>
        public AasVerifiedPackage(ByteString content, string digestAlg, string digest)
        {
            Content = content;
            DigestAlg = digestAlg ?? string.Empty;
            Digest = digest ?? string.Empty;
        }

        /// <summary>
        /// Exact package bytes whose digest was verified.
        /// </summary>
        public ByteString Content { get; }

        /// <summary>
        /// Case-sensitive digest algorithm published by the resource.
        /// </summary>
        public string DigestAlg { get; }

        /// <summary>
        /// Lowercase hexadecimal digest published by the resource.
        /// </summary>
        public string Digest { get; }
    }
}
