// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Buffers;

namespace Opc.Ua.GMS.Contracts
{
    /// <summary>
    /// Immutable bounds used at provider and client boundaries; exceeding a bound never truncates a result.
    /// </summary>
    public sealed record GmsResultLimits
    {
        /// <summary>
        /// Gets the maximum retained results or identifiers in a query response.
        /// </summary>
        public int MaximumResults { get; init; } = 64;

        /// <summary>
        /// Gets the maximum binary-encoded inline response or measurement metadata size.
        /// </summary>
        public int MaximumMetadataBytes { get; init; } = 256 * 1024;

        /// <summary>
        /// Gets the maximum bytes in one transferred result file.
        /// </summary>
        public int MaximumFileBytes { get; init; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets the maximum sum of stored encoded metadata and transferable payload bytes.
        /// </summary>
        public long MaximumStoredBytes { get; init; } = 64 * 1024 * 1024;

        /// <summary>
        /// Gets the maximum characteristics in one intermediate result.
        /// </summary>
        public int MaximumCharacteristics { get; init; } = 64;

        /// <summary>
        /// Gets the maximum tool identifiers associated with one result, independently of result-store capacity.
        /// </summary>
        public int MaximumUsedTools { get; init; } = 64;

        /// <summary>
        /// Gets the maximum configured correction objects.
        /// </summary>
        public int MaximumCorrections { get; init; } = 64;

        /// <summary>
        /// Validates all bounds before a provider or client is made available.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A configured bound is outside its accepted range.</exception>
        public void Validate()
        {
            const string message = "GMS result bounds must be positive.";
            if (MaximumResults is < 1 or > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumResults), message);
            }
            if (MaximumCharacteristics is < 1 or > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumCharacteristics), message);
            }
            if (MaximumUsedTools is < 1 or > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumUsedTools), message);
            }
            if (MaximumCorrections is < 1 or > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumCorrections), message);
            }
            if (MaximumMetadataBytes is < 1 or > 16 * 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumMetadataBytes), message);
            }
            if (MaximumFileBytes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumFileBytes), message);
            }
            if (MaximumStoredBytes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumStoredBytes), message);
            }
        }

        /// <summary>
        /// Measures an inline value using a fixed-size encoding buffer, failing rather than allocating without limit.
        /// </summary>
        public int Measure(in Variant value, IServiceMessageContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            Validate();
            byte[] buffer = ArrayPool<byte>.Shared.Rent(MaximumMetadataBytes);
            try
            {
                using var encoder = new BinaryEncoder(buffer, 0, MaximumMetadataBytes, context);
                encoder.WriteVariant(null, value);
                return encoder.Position;
            }
            catch (NotSupportedException error)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "The GMS inline metadata exceeds the configured byte bound.", error);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
    }
}
