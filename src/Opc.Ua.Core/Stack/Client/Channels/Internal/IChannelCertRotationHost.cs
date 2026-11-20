// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua
{
    /// <summary>
    /// Supplies application certificates and entry recovery services to the certificate-change processor.
    /// </summary>
    internal interface IChannelCertRotationHost
    {
        /// <summary>
        /// Gets the application configuration and its certificate manager.
        /// </summary>
        ApplicationConfiguration Configuration { get; }

        /// <summary>
        /// Gets the logger used to report certificate loading and rotation failures.
        /// </summary>
        ILogger? Logger { get; }

        /// <summary>
        /// Gets whether the channel manager has begun disposal.
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Captures the entries currently registered with the manager.
        /// </summary>
        /// <returns>A snapshot of the registered channel entry references.</returns>
        ChannelEntry[] SnapshotEntries();

        /// <summary>
        /// Takes ownership of the certificate and chain and updates the matching certificate-type snapshot.
        /// </summary>
        /// <param name="clientCertificate">
        /// The replacement application certificate whose ownership is transferred.
        /// </param>
        /// <param name="clientCertificateChain">
        /// The replacement chain whose ownership is transferred, if supplied.
        /// </param>
        void ReplaceClientCertificate(
            Certificate? clientCertificate,
            CertificateCollection? clientCertificateChain);

        /// <summary>
        /// Retains the active rotation task for diagnostics and lifetime tracking.
        /// </summary>
        /// <param name="task">The rotation task, or null to clear the tracked task.</param>
        void SetCertificateRotationTask(Task? task);
    }
}
