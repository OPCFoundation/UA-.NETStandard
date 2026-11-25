// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Server
{
    /// <summary>
    /// The named administrative decisions an <see cref="IServerAuthorizationPolicy"/> makes.
    /// </summary>
    /// <remarks>
    /// The default rules of <see cref="ServerAuthorizationPolicy"/> are listed on each value.
    /// "Authenticated" means the session has a non-anonymous identity token. A "server-internal"
    /// call is one whose context carries no <see cref="OperationContext"/>.
    /// </remarks>
    public enum ServerAuthorizationDecision
    {
        /// <summary>
        /// Generic administrator access, used by <see cref="RoleAuthorizationGate.CheckAdmin"/>.
        /// Default: SecurityAdmin, authenticated, SignAndEncrypt; server-internal calls are denied.
        /// </summary>
        SecurityAdmin,

        /// <summary>
        /// Generic non-security configuration access.
        /// Default: SecurityAdmin or ConfigureAdmin, authenticated, SignAndEncrypt;
        /// server-internal calls are denied.
        /// </summary>
        ConfigureAdmin,

        /// <summary>
        /// Part 18 §4 role management (AddRole, RemoveRole, identity and application rules).
        /// Default: as <see cref="SecurityAdmin"/>.
        /// </summary>
        RoleManagement,

        /// <summary>
        /// Part 18 §5 user management (AddUser, ModifyUser, RemoveUser).
        /// Default: as <see cref="SecurityAdmin"/>.
        /// </summary>
        UserManagement,

        /// <summary>
        /// Part 12 §7.10 certificate push methods that accept an authenticated channel
        /// (CreateSelfSignedCertificate, DeleteCertificate, GetCertificates, GetRejectedList,
        /// ApplyChanges, CancelChanges, ResetToServerDefaults, the ConfigurationFile read).
        /// Default: SecurityAdmin, authenticated, Sign or SignAndEncrypt; server-internal calls
        /// are allowed.
        /// </summary>
        CertificatePush,

        /// <summary>
        /// Part 12 §7.8 / §7.10 certificate push access that requires an encrypted channel
        /// (UpdateCertificate, CreateSigningRequest, TrustList, the ConfigurationFile update).
        /// Default: SecurityAdmin, authenticated, SignAndEncrypt; server-internal calls are allowed.
        /// </summary>
        CertificatePushEncrypted,

        /// <summary>
        /// Part 12 §8 KeyCredential push (CreateCredential, UpdateCredential, DeleteCredential,
        /// GetEncryptingKey). Default: SecurityAdmin, authenticated, SignAndEncrypt;
        /// server-internal calls are denied.
        /// </summary>
        KeyCredentialPush,

        /// <summary>
        /// Part 5 §6.3 administrator view of server, session, subscription and program diagnostics.
        /// Default: SecurityAdmin, authenticated, SignAndEncrypt; server-internal calls are denied.
        /// </summary>
        DiagnosticsRead,

        /// <summary>
        /// Changing the diagnostics settings (Server.ServerDiagnostics.EnabledFlag).
        /// Default: SecurityAdmin or ConfigureAdmin, authenticated, SignAndEncrypt;
        /// server-internal calls are denied.
        /// </summary>
        DiagnosticsConfiguration,

        /// <summary>
        /// Part 17 alias mutation (AddAliasesToCategory, DeleteAliasesFromCategory).
        /// Default: as <see cref="SecurityAdmin"/>.
        /// </summary>
        AliasManagement
    }
}
