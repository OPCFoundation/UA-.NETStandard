/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
