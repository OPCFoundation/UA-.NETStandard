// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The Server Configuration Node Manager.
    /// </summary>
    public interface IConfigurationNodeManager : IAsyncNodeManager
    {
        /// <summary>
        /// Raised when the <c>DefaultRolePermissions</c> or <c>DefaultUserRolePermissions</c>
        /// property of any <see cref="NamespaceMetadataState"/> node changes.
        /// Subscribers should invalidate any cached role-permission validation results.
        /// </summary>
        event EventHandler DefaultPermissionsChanged;

        /// <summary>
        /// Gets or creates the <see cref="NamespaceMetadataState"/> node for the specified NamespaceUri.
        /// </summary>
        /// <remarks>
        /// An existing <c>NamespaceMetadataType</c> Object of the namespace is never duplicated. When
        /// that Object is not a <see cref="NamespaceMetadataState"/>, e.g. one created by an untyped
        /// NodeSet import, it is kept and <c>null</c> is returned. While the <c>NamespaceUri</c> of such
        /// an Object cannot be resolved yet, the Object may describe the namespace, so nothing is
        /// created and <c>null</c> is returned as well.
        /// </remarks>
        /// <returns>
        /// The metadata node, or <c>null</c> when the namespace is (or may be) described by an Object
        /// that is not a <see cref="NamespaceMetadataState"/> or when the node cannot be created.
        /// </returns>
        ValueTask<NamespaceMetadataState?> CreateNamespaceMetadataStateAsync(string namespaceUri, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates the configuration node for the server.
        /// </summary>
        void CreateServerConfiguration(ServerSystemContext systemContext, ApplicationConfiguration configuration);

        /// <summary>
        /// Gets and returns the <see cref="NamespaceMetadataState"/> node associated with the specified NamespaceUri
        /// </summary>
        ValueTask<NamespaceMetadataState?> GetNamespaceMetadataStateAsync(string namespaceUri, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets and returns the <see cref="NamespaceMetadataState"/> node associated with the specified namespace index
        /// </summary>
        ValueTask<NamespaceMetadataState?> GetNamespaceMetadataStateAsync(ushort namespaceIndex, CancellationToken cancellationToken = default);

        /// <summary>
        /// Binds the standard KeyCredentialConfiguration folder to the supplied push subject.
        /// </summary>
        ValueTask BindKeyCredentialPushAsync(
            KeyCredentialPushSubject subject,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Determine if the impersonated user has admin access.
        /// </summary>
        /// <exception cref="ServiceResultException"/>
        /// <seealso cref="StatusCodes.BadUserAccessDenied"/>
        void HasApplicationSecureAdminAccess(ISystemContext context);

        /// <summary>
        /// Determine if the impersonated user has admin access.
        /// </summary>
        /// <exception cref="ServiceResultException"/>
        /// <seealso cref="StatusCodes.BadUserAccessDenied"/>
        void HasApplicationSecureAdminAccess(ISystemContext context, CertificateStoreIdentifier trustedStore);

        /// <summary>
        /// Starts periodic evaluation of <c>CertificateExpired</c> and
        /// <c>TrustListOutOfDate</c> alarms per OPC 10000-12 §7.8.3.
        /// Call this after the server is fully started so that
        /// <c>SetActiveState</c> can safely emit event notifications.
        /// </summary>
        /// <param name="interval">
        /// Evaluation interval. A typical value is 60 seconds.
        /// </param>
        void StartAlarmMonitoring(TimeSpan interval);

        /// <summary>
        /// Stops the periodic alarm evaluation.
        /// </summary>
        void StopAlarmMonitoring();

        /// <summary>
        /// The grace period that elapses between the
        /// <c>ApplyChanges</c> method response being returned and the
        /// per-channel force-renegotiate fan-out specified by OPC UA
        /// Part 12 §7.10.9. Configurable because the right value depends
        /// on the transport latency between the server and its
        /// administrative client.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Defaults to 250&#160;ms which is adequate for in-process and
        /// LAN deployments. Hosts that run the server behind a higher-
        /// latency link (e.g. WAN-fronted Push Configuration) should
        /// raise this so the <c>ApplyChanges</c> response is flushed
        /// before its SecureChannel is torn down.
        /// </para>
        /// <para>
        /// Setting a non-positive value treats the rotation as
        /// "immediate" and may cause the calling client to receive a
        /// transport error instead of the method response. Tests can
        /// instead await
        /// <see cref="DrainPendingApplyChangesAsync(CancellationToken)"/>
        /// for deterministic completion without relying on the grace
        /// period.
        /// </para>
        /// <para>
        /// TODO: replace the grace period with a transport-level
        /// "response flushed" hook so the channel cut can run exactly
        /// when the response has left the wire — tracked as a follow-up
        /// feature on top of this PR.
        /// </para>
        /// </remarks>
        TimeSpan ApplyChangesGracePeriod { get; set; }

        /// <summary>
        /// Awaits completion of any pending deferred work scheduled by a
        /// recent <c>ApplyChanges</c> call. The deferred work includes
        /// reloading certificates from disk via
        /// <see cref="CertificateManager.UpdateAsync"/> and forcing
        /// affected SecureChannels to renegotiate per OPC UA Part 12
        /// §7.10.9.
        /// </summary>
        /// <remarks>
        /// Returns immediately when no deferred work is in flight. Used
        /// by tests and tightly-coupled hosts to deterministically wait
        /// for cert rotation to take effect after a push update.
        /// </remarks>
        /// <param name="cancellationToken">A token to cancel the wait.</param>
        Task DrainPendingApplyChangesAsync(CancellationToken cancellationToken = default);
    }
}
