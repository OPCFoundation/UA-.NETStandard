// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Gds.Client
{
    /// <summary>
    /// Configuration options for <see cref="GlobalDiscoveryServerClient"/> and
    /// <see cref="ServerPushConfigurationClient"/>. Bound by
    /// <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> when the
    /// clients are resolved from the dependency injection container via
    /// <c>builder.AddGdsClient(...)</c>.
    /// </summary>
    public sealed class GdsClientOptions
    {
        /// <summary>
        /// Session lifetime requested when establishing a new session with the
        /// server. Defaults to one minute.
        /// </summary>
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Maximum number of connect attempts before connecting
        /// gives up and rethrows the last <see cref="ServiceResultException"/>.
        /// Must be greater than zero. Defaults to <c>5</c>.
        /// </summary>
        public int MaxConnectAttempts { get; set; } = 5;

        /// <summary>
        /// Delay between connect retries. Defaults to one second.
        /// </summary>
        public TimeSpan ConnectBackoff { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum trust list size accepted on read or sent on update, in
        /// bytes. Defaults to one megabyte.
        /// </summary>
        public long MaxTrustListSize { get; set; } = 1 * 1024 * 1024;

        /// <summary>
        /// Chunk size used for the trust-list file transfer. Must be greater
        /// than zero. Defaults to <c>256</c> bytes.
        /// </summary>
        public int FileTransferChunkSize { get; set; } = 256;
    }
}
