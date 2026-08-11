/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Server
{
    /// <summary>
    /// A generic session manager object for a server.
    /// </summary>
    public interface ISession : IDisposable
    {
        /// <summary>
        /// Whether the session has been activated.
        /// </summary>
        bool Activated { get; }

        /// <summary>
        /// Whether the session is being closed. Requests that would create new server state for
        /// the session are rejected once this is set, because that state would be torn down again
        /// immediately. Closing is entered once and never left.
        /// </summary>
        bool IsClosing { get; }

        /// <summary>
        /// The continuation points this session is holding for its client, for browses and
        /// for historical reads.
        /// </summary>
        ISessionContinuationPoints ContinuationPoints { get; }

        /// <summary>
        /// The server application instance certificate used by this session.
        /// </summary>
        Certificate ServerCertificate { get; }

        /// <summary>
        /// The application instance certificate associated with the client.
        /// </summary>
        Certificate ClientCertificate { get; }

        /// <summary>
        /// The last time the session was contacted by the client.
        /// </summary>
        DateTime ClientLastContactTime { get; }

        /// <summary>
        /// The monotonic tick count (milliseconds) at the last client contact.
        /// Used for timeout calculations that are immune to system time changes.
        /// </summary>
        long LastContactTickCount { get; }

        /// <summary>
        /// The client Nonce associated with the session.
        /// </summary>
        ByteString ClientNonce { get; }

        /// <summary>
        /// Applies an update to the session diagnostics while holding the session's
        /// diagnostics lock.
        /// </summary>
        /// <remarks>
        /// The session owns its lock and never exposes it, so callers cannot participate in
        /// the server's locking order. Keep the update short and free of I/O or callbacks.
        /// </remarks>
        /// <param name="update">The mutation to apply to the diagnostics.</param>
        void UpdateDiagnostics(Action<SessionDiagnosticsDataType> update);

        /// <summary>
        /// Reads a value derived from the session diagnostics while holding the session's
        /// diagnostics lock.
        /// </summary>
        /// <remarks>
        /// Do not let the diagnostics object escape the callback: once the lock is released,
        /// any field read from it is unsynchronized.
        /// </remarks>
        /// <typeparam name="TResult">The type of the value produced.</typeparam>
        /// <param name="read">The projection applied to the diagnostics.</param>
        TResult ReadDiagnostics<TResult>(Func<SessionDiagnosticsDataType, TResult> read);

        /// <summary>
        /// The application defined mapping for user identity provided by the client.
        /// </summary>
        IUserIdentity EffectiveIdentity { get; }

        /// <summary>
        /// Whether the session's <see cref="EffectiveIdentity"/> has been
        /// invalidated by a configuration change (e.g. Role identity-mapping
        /// rules) and should be recomputed on the next request.
        /// </summary>
        /// <remarks>
        /// The flag is set by <see cref="MarkIdentityStale"/> and cleared by
        /// <see cref="RefreshEffectiveIdentity"/>. Per OPC UA Part 18 §4.4.1
        /// role grants must reflect the live RoleSet without forcing the
        /// client to re-activate.
        /// </remarks>
        bool IsIdentityStale { get; }

        /// <summary>
        /// Marks the session's <see cref="EffectiveIdentity"/> as stale so
        /// the next request triggers a re-evaluation of the role mapping.
        /// </summary>
        /// <remarks>
        /// Safe to call from any thread. Multiple concurrent calls are
        /// idempotent — the flag is sticky until a refresh clears it.
        /// </remarks>
        void MarkIdentityStale();

        /// <summary>
        /// Atomically replaces the session's <see cref="EffectiveIdentity"/>
        /// with the supplied value and clears the
        /// <see cref="IsIdentityStale"/> flag.
        /// </summary>
        /// <param name="effectiveIdentity">
        /// The newly resolved effective identity (including any roles layered
        /// on by mandatory-role assignment and the live RoleSet).
        /// </param>
        void RefreshEffectiveIdentity(IUserIdentity effectiveIdentity);

        /// <summary>
        /// Returns the session's endpoint
        /// </summary>
        EndpointDescription EndpointDescription { get; }

        /// <summary>
        /// Whether the session timeout has elapsed since the last communication from the client.
        /// </summary>
        bool HasExpired { get; }

        /// <summary>
        /// Gets the identifier assigned to the session when it was created.
        /// </summary>
        NodeId Id { get; }

        /// <summary>
        /// The user identity provided by the client.
        /// </summary>
        IUserIdentity Identity { get; }

        /// <summary>
        /// The user identity token provided by the client wrapped into a handler.
        /// </summary>
        IUserIdentityTokenHandler IdentityToken { get; }

        /// <summary>
        /// The locales requested when the session was created.
        /// </summary>
        string[] PreferredLocales { get; }

        /// <summary>
        /// Returns the session's SecureChannelId
        /// </summary>
        string SecureChannelId { get; }

        /// <summary>
        /// The name the client gave this session when it created it.
        /// </summary>
        string SessionName { get; }

        /// <summary>
        /// The application URI of the client that owns this session, or <c>null</c> when the
        /// client did not supply an application description.
        /// </summary>
        string? ClientApplicationUri { get; }

        /// <summary>
        /// Completes the asynchronous part of session creation by registering
        /// the session diagnostics node in the address space and setting
        /// <see cref="Id"/>. Called once by the session manager after the
        /// session is constructed, before it is activated.
        /// </summary>
        /// <param name="context">The operation context of the create request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask InitializeAsync(
            OperationContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Activates the session and binds it to the current secure channel.
        /// </summary>
        bool Activate(
            OperationContext context,
            IUserIdentityTokenHandler identityToken,
            IUserIdentity identity,
            IUserIdentity effectiveIdentity,
            ArrayOf<string> localeIds,
            Nonce serverNonce);

        /// <summary>
        /// Closes a session and removes itself from the address space.
        /// </summary>
        ValueTask CloseAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Create new ECC ephemeral key
        /// </summary>
        /// <returns>A new ephemeral key</returns>
        EphemeralKeyType? GetNewEphemeralKey();

        /// <summary>
        /// Checks if the secure channel is currently valid.
        /// </summary>
        bool IsSecureChannelValid(string secureChannelId);

        /// <summary>
        /// Set the ECC security policy URI
        /// </summary>
        void SetUserTokenSecurityPolicy(string securityPolicyUri);

        /// <summary>
        /// Updates the requested locale ids.
        /// </summary>
        /// <returns>true if the new locale ids are different from the old locale ids.</returns>
        bool UpdateLocaleIds(ArrayOf<string> localeIds);

        /// <summary>
        /// Validates the application signature and user identity token before activation.
        /// </summary>
        /// <param name="context">The operation context for the activation request.</param>
        /// <param name="clientSignature">The client application signature.</param>
        /// <param name="userIdentityToken">The encoded user identity token.</param>
        /// <param name="userTokenSignature">The user token signature.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The validated identity token handler and matching user token policy.</returns>
        ValueTask<(
            IUserIdentityTokenHandler IdentityToken,
            UserTokenPolicy? UserTokenPolicy)> ValidateBeforeActivateAsync(
            OperationContext context,
            SignatureData clientSignature,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validate the diagnostic info.
        /// </summary>
        void ValidateDiagnosticInfo(RequestHeader requestHeader);

        /// <summary>
        /// Validates the request.
        /// </summary>
        void ValidateRequest(RequestHeader requestHeader, SecureChannelContext secureChannelContext, RequestType requestType);
    }
}
