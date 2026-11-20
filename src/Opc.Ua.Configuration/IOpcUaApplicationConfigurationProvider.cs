/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Builds and validates the shared dependency-injection application
    /// configuration for registered client and server features.
    /// </summary>
    public interface IOpcUaApplicationConfigurationProvider : IAsyncDisposable
    {
        /// <summary>
        /// The application instance that owns the configuration.
        /// </summary>
        IApplicationInstance Application { get; }

        /// <summary>
        /// The configuration assembled from all registered features.
        /// Validation completes when <see cref="GetAsync"/> is awaited.
        /// </summary>
        ApplicationConfiguration Configuration { get; }

        /// <summary>
        /// Creates and validates the configuration once. Client-only
        /// configurations also ensure the application instance certificate
        /// exists; hosted server features complete certificate initialization
        /// during server startup.
        /// </summary>
        Task<ApplicationConfiguration> GetAsync(CancellationToken ct = default);
    }
}
