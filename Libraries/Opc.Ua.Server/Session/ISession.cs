/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

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
        /// The application instance certificate associated with the client.
        /// </summary>
        X509Certificate2 ClientCertificate { get; }

        /// <summary>
        /// The last time the session was contacted by the client.
        /// </summary>
        DateTime ClientLastContactTime { get; }

        /// <summary>
        /// The client Nonce associated with the session.
        /// </summary>
        byte[] ClientNonce { get; }

        /// <summary>
        /// A lock which must be acquired before accessing the diagnostics.
        /// </summary>
        object DiagnosticsLock { get; }

        /// <summary>
        /// The application defined mapping for user identity provided by the client.
        /// </summary>
        IUserIdentity EffectiveIdentity { get; }

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
        /// The user identity token provided by the client.
        /// </summary>
        UserIdentityToken IdentityToken { get; }

        /// <summary>
        /// The locales requested when the session was created.
        /// </summary>
        string[] PreferredLocales { get; }

        /// <summary>
        /// Returns the session's SecureChannelId
        /// </summary>
        string SecureChannelId { get; }

        /// <summary>
        /// The diagnostics associated with the session.
        /// </summary>
        SessionDiagnosticsDataType SessionDiagnostics { get; }

        /// <summary>
        /// Activates the session and binds it to the current secure channel.
        /// </summary>
        bool Activate(
            OperationContext context,
            List<SoftwareCertificate> clientSoftwareCertificates,
            UserIdentityToken identityToken,
            IUserIdentity identity,
            IUserIdentity effectiveIdentity,
            StringCollection localeIds,
            Nonce serverNonce);

        /// <summary>
        /// Closes a session and removes itself from the address space.
        /// </summary>
        void Close();

        /// <summary>
        /// Create new ECC ephemeral key
        /// </summary>
        /// <returns>A new ephemeral key</returns>
        EphemeralKeyType GetNewEccKey();

        /// <summary>
        /// Checks if the secure channel is currently valid.
        /// </summary>
        bool IsSecureChannelValid(string secureChannelId);

        /// <summary>
        /// Restores a continuation point for a session.
        /// </summary>
        /// <remarks>
        /// The caller is responsible for disposing the continuation point returned.
        /// </remarks>
        ContinuationPoint RestoreContinuationPoint(byte[] continuationPoint);

        /// <summary>
        /// Restores a previously saves history continuation point.
        /// </summary>
        /// <param name="continuationPoint">The identifier for the continuation point.</param>
        /// <returns>The save continuation point. null if not found.</returns>
        object RestoreHistoryContinuationPoint(byte[] continuationPoint);

        /// <summary>
        /// Saves a continuation point for a session.
        /// </summary>
        /// <remarks>
        /// If the session has too many continuation points the oldest one is dropped.
        /// </remarks>
        void SaveContinuationPoint(ContinuationPoint continuationPoint);

        /// <summary>
        /// Saves a continuation point used for historical reads.
        /// </summary>
        /// <param name="id">The identifier for the continuation point.</param>
        /// <param name="continuationPoint">The continuation point.</param>
        /// <remarks>
        /// If the continuationPoint implements IDisposable it will be disposed when
        /// the Session is closed or discarded.
        /// </remarks>
        void SaveHistoryContinuationPoint(Guid id, object continuationPoint);

        /// <summary>
        /// Set the ECC security policy URI
        /// </summary>
        void SetEccUserTokenSecurityPolicy(string securityPolicyUri);

        /// <summary>
        /// Updates the requested locale ids.
        /// </summary>
        /// <returns>true if the new locale ids are different from the old locale ids.</returns>
        bool UpdateLocaleIds(StringCollection localeIds);

        /// <summary>
        /// Activates the session and binds it to the current secure channel.
        /// </summary>
        void ValidateBeforeActivate(
            OperationContext context,
            SignatureData clientSignature,
            List<SoftwareCertificate> clientSoftwareCertificates,
            ExtensionObject userIdentityToken,
            SignatureData userTokenSignature,
            out UserIdentityToken identityToken,
            out UserTokenPolicy userTokenPolicy);

        /// <summary>
        /// Validate the diagnostic info.
        /// </summary>
        void ValidateDiagnosticInfo(RequestHeader requestHeader);

        /// <summary>
        /// Validates the request.
        /// </summary>
        void ValidateRequest(RequestHeader requestHeader, RequestType requestType);
    }
}
