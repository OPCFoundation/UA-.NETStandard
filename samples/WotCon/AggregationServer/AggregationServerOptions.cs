/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using Opc.Ua.XRegistry.Server;

namespace AggregationServer
{
    /// <summary>
    /// Configures the generic WoT aggregation server host.
    /// </summary>
    public sealed class AggregationServerOptions
    {
        /// <summary>
        /// Gets or sets the endpoint URL. When empty, host and port are used.
        /// </summary>
        public string? EndpointUrl { get; set; }

        /// <summary>
        /// Gets or sets the endpoint host.
        /// </summary>
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>
        /// Gets or sets the endpoint port.
        /// </summary>
        public int Port { get; set; } = 62550;

        /// <summary>
        /// Gets or sets the application name.
        /// </summary>
        public string ApplicationName { get; set; } = "AggregationServer";

        /// <summary>
        /// Gets or sets the isolated PKI root shared by the server and upstream client.
        /// </summary>
        public string? PkiRoot { get; set; }

        /// <summary>
        /// Gets or sets the durable registry directory used by prepared publication.
        /// </summary>
        public string StorageFolder { get; set; } = Path.Combine(AppContext.BaseDirectory, "registry");

        /// <summary>
        /// Gets or sets the caller-owned document store.
        /// Prepared publication requires genuine immutable content leases.
        /// The provider owns content durability; the registry manifest remains file-backed.
        /// </summary>
        public IXRegistryResourceStore? ResourceStore { get; set; }

        /// <summary>
        /// Gets or sets the maximum accepted document size.
        /// </summary>
        public int MaximumDocumentBytes { get; set; } = 32 * 1024 * 1024;
    }
}
