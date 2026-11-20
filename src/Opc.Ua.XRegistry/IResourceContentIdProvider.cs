/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// Maps a registry resource document (and its format) to an independent content key — the
    /// fingerprint that makes document bytes addressable by an Opaque NodeId and de-duplicable.
    /// This key never replaces structural xRegistry identity. A concrete registry provides an implementation (for example the PubSub Schema
    /// Registry fingerprints schema documents via the pluggable SchemaId providers).
    /// </summary>
    public interface IResourceContentIdProvider
    {
        /// <summary>
        /// Computes the raw content-key bytes for a resource document of a given format.
        /// </summary>
        /// <param name="format">The resource format (for example <c>avro</c>).</param>
        /// <param name="document">The resource document bytes.</param>
        /// <returns>The raw content-id bytes.</returns>
        ByteString ComputeContentId(string format, ReadOnlySpan<byte> document);

        /// <summary>
        /// Gets the algorithm name that identifies the (canonicalization, hash) used for a format,
        /// or <c>null</c> when the format is not supported.
        /// </summary>
        /// <param name="format">The resource format.</param>
        /// <returns>The algorithm name, or <c>null</c>.</returns>
        string? GetAlgorithm(string format);
    }
}
