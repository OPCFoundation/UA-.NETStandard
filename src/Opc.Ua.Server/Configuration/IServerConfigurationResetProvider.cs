// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional, injectable provider that performs the vendor-specific work
    /// of resetting the application's security configuration to its default
    /// settings for the OPC 10000-12 §7.10.13 <c>ResetToServerDefaults</c>
    /// Method.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>ResetToServerDefaults</c> Method is Optional on the
    /// <c>ServerConfigurationType</c>. The Method (and its address-space node)
    /// is only exposed when an implementation of this interface is configured;
    /// otherwise the optional child is suppressed. The
    /// <see cref="ConfigurationNodeManager"/> owns the standard concerns
    /// (SecurityAdmin Role and authenticated-channel enforcement, transaction
    /// conflict rejection, auditing, and advertising the pending shutdown via
    /// <c>ServerState</c>/<c>SecondsTillShutdown</c>/<c>ShutdownReason</c>) and
    /// only delegates the actual reset to this provider, after the Method
    /// response has been returned to the caller.
    /// </para>
    /// <para>
    /// The default configuration is vendor-specific and is not necessarily the
    /// factory default; a machine builder may, for example, keep a configuration
    /// that still allows the application to communicate within a machine after
    /// the reset. Implementations must be safe to invoke off the calling
    /// Session's thread and must honour the supplied
    /// <see cref="CancellationToken"/> so a server shutdown that races the
    /// reset can abandon it cleanly.
    /// </para>
    /// </remarks>
    public interface IServerConfigurationResetProvider
    {
        /// <summary>
        /// Resets the application's security configuration to its default
        /// settings. Invoked by <see cref="ConfigurationNodeManager"/> after
        /// the <c>ResetToServerDefaults</c> Method response has been returned
        /// and the advertised shutdown grace period has elapsed.
        /// </summary>
        /// <param name="cancellationToken">
        /// Signalled when the server is shutting down; a cooperating
        /// implementation abandons the reset when cancellation is requested.
        /// </param>
        ValueTask ResetToServerDefaultsAsync(CancellationToken cancellationToken = default);
    }
}
