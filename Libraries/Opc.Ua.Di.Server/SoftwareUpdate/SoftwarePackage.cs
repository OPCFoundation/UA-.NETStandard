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

namespace Opc.Ua.Di.Server.SoftwareUpdate
{
    /// <summary>
    /// Metadata describing a single software package available for
    /// upload, download, or installation through the OPC 10000-100 §10.3
    /// software update facet.
    /// </summary>
    /// <param name="Id">
    /// Application-defined identifier — must be unique within the
    /// owning <see cref="ISoftwarePackageStore"/>. Often a string of
    /// the form <c>"firmware-1.2.3"</c>.
    /// </param>
    /// <param name="Version">
    /// Human-readable version string (e.g. <c>"1.2.3"</c>).
    /// </param>
    /// <param name="Vendor">
    /// Free-text vendor / origin information. Empty if unknown.
    /// </param>
    /// <param name="Description">
    /// Human-readable description; empty if not provided.
    /// </param>
    /// <param name="SizeBytes">
    /// Total size of the package payload in bytes.
    /// </param>
    /// <param name="CreatedAt">
    /// Timestamp when the package was added to the store.
    /// </param>
    /// <param name="Hash">
    /// Optional content hash (e.g. SHA-256, hex-encoded). Empty when
    /// not available.
    /// </param>
    public sealed record SoftwarePackage(
        string Id,
        string Version,
        string Vendor,
        string Description,
        long SizeBytes,
        DateTimeOffset CreatedAt,
        string Hash);
}
