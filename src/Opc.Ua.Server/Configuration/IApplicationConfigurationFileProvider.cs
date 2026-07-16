/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional, injectable provider that backs the OPC 10000-12 §7.10.20
    /// <c>ConfigurationFile</c> (an <c>ApplicationConfigurationFileType</c>)
    /// on the <c>ServerConfiguration</c> Object. It abstracts reading,
    /// validating and atomically applying the application configuration so the
    /// <see cref="ConfigurationNodeManager"/> can expose the standard
    /// <c>FileType</c> read/update flow (Open, Read, Write, Close,
    /// CloseAndUpdate, ConfirmUpdate) without knowing where or how the
    /// configuration is persisted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>ConfigurationFile</c> Object is Optional; its node (and the
    /// entire FileType sub-tree) is only exposed when an implementation of
    /// this interface is configured, otherwise the optional child is
    /// suppressed. The node manager owns the standard concerns: the FileType
    /// stream lifecycle, SecurityAdmin Role and authenticated-channel
    /// enforcement for update Methods, transaction exclusion against the
    /// PushManagement transaction coordinator (§7.10.2/§7.10.20), the
    /// <c>VersionToUpdate</c>/<c>CurrentVersion</c> check, and generating the
    /// <c>UpdateId</c> for confirmation. The provider only reads, validates
    /// and applies the opaque configuration content.
    /// </para>
    /// <para>
    /// The content exchanged through <see cref="ReadConfigurationAsync"/> and
    /// <see cref="ApplyConfigurationAsync"/> is the file stream body as defined
    /// by §7.8.5.1 (a serialized <c>UABinaryFileDataType</c>). It is treated
    /// opaquely by the node manager; the concrete encoding is the provider's
    /// responsibility.
    /// </para>
    /// <para>
    /// Implementations must be deterministic and safe to call off the calling
    /// Session's thread, must honour the supplied
    /// <see cref="CancellationToken"/>, and must never hold a lock across an
    /// <see langword="await"/>.
    /// </para>
    /// </remarks>
    public interface IApplicationConfigurationFileProvider
    {
        /// <summary>
        /// The version of the currently active configuration
        /// (<c>CurrentVersion</c>, a <c>VersionTime</c>). A
        /// <c>CloseAndUpdate</c> whose <c>VersionToUpdate</c> argument does not
        /// match this value is rejected with
        /// <see cref="StatusCodes.BadInvalidState"/>. Implementations must
        /// change this value whenever the configuration is updated so Clients
        /// can detect concurrent modifications.
        /// </summary>
        uint CurrentVersion { get; }

        /// <summary>
        /// The time at which the configuration was last updated
        /// (<c>LastUpdateTime</c>, a <c>UtcTime</c>).
        /// </summary>
        DateTime LastUpdateTime { get; }

        /// <summary>
        /// <see langword="true"/> when an applied update requires the Client
        /// to reconnect and call <c>ConfirmUpdate</c> (so a non-empty
        /// <c>UpdateId</c> is returned and, if confirmation does not arrive in
        /// time, the change is reverted). <see langword="false"/> when applied
        /// updates take effect immediately and never need confirmation, in
        /// which case <c>CloseAndUpdate</c> returns an empty <c>UpdateId</c>.
        /// </summary>
        bool RequiresConfirmation { get; }

        /// <summary>
        /// Reads the current configuration as the FileType stream body.
        /// </summary>
        /// <param name="cancellationToken">A token used to cancel the read.</param>
        /// <returns>The serialized current configuration.</returns>
        ValueTask<ByteString> ReadConfigurationAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates a proposed configuration without applying it. Called by
        /// <c>CloseAndUpdate</c> before any change is made so an invalid
        /// configuration is rejected with no partial update.
        /// </summary>
        /// <param name="configuration">The proposed configuration content.</param>
        /// <param name="cancellationToken">A token used to cancel the validation.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown when the proposed configuration is invalid; the carried
        /// <see cref="StatusCode"/> is surfaced to the Client.
        /// </exception>
        ValueTask ValidateConfigurationAsync(
            ByteString configuration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically applies a previously validated configuration. The apply
        /// must be all-or-nothing: on failure it must throw having left the
        /// active configuration unchanged (no partial update), so the node
        /// manager can surface the failure without a separate rollback step.
        /// On success the implementation updates <see cref="CurrentVersion"/>
        /// and <see cref="LastUpdateTime"/>.
        /// </summary>
        /// <param name="configuration">The validated configuration content.</param>
        /// <param name="cancellationToken">A token used to cancel the apply.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown when the configuration cannot be applied; the active
        /// configuration is left unchanged.
        /// </exception>
        ValueTask ApplyConfigurationAsync(
            ByteString configuration,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Confirms the most recently applied update (the Client reconnected
        /// successfully), finalizing it so it will not be reverted. Only
        /// meaningful when <see cref="RequiresConfirmation"/> is
        /// <see langword="true"/>; otherwise a no-op. Must be idempotent.
        /// </summary>
        /// <param name="cancellationToken">A token used to cancel the confirmation.</param>
        ValueTask ConfirmUpdateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reverts the most recently applied but not-yet-confirmed update,
        /// restoring the previous configuration. Invoked by the node manager
        /// when a required <c>ConfirmUpdate</c> does not arrive within the
        /// Client-supplied revert window. Must be idempotent and a no-op when
        /// there is nothing to revert.
        /// </summary>
        /// <param name="cancellationToken">A token used to cancel the revert.</param>
        ValueTask RevertUpdateAsync(CancellationToken cancellationToken = default);
    }
}
