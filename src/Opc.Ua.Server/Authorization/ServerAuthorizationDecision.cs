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
        AliasManagement,

        /// <summary>
        /// Opening and reading a TrustList (Part 12 §7.8.2): the server's own push TrustLists and
        /// the GDS certificate group TrustLists. Default: as
        /// <see cref="CertificatePushEncrypted"/>; the GDS supplies its own standard rule.
        /// </summary>
        TrustListRead,

        /// <summary>
        /// Writing a TrustList (Write, CloseAndUpdate, AddCertificate, RemoveCertificate).
        /// Default: as <see cref="CertificatePushEncrypted"/>; the GDS supplies its own standard rule.
        /// </summary>
        TrustListWrite,

        /// <summary>
        /// Part 14 §9.1.3.3 SetSecurityKeys: a Security Key Server pushes keys to this server.
        /// Default: SecurityAdmin or SecurityKeyServerPush, authenticated, SignAndEncrypt;
        /// server-internal calls are allowed.
        /// </summary>
        SecurityKeyPush,

        /// <summary>
        /// GDS Part 12 §6.5 directory changes (RegisterApplication, UpdateApplication,
        /// UnregisterApplication). The GDS supplies the standard rule.
        /// </summary>
        ApplicationRegistration,

        /// <summary>
        /// GDS Part 12 §6.5 directory reads restricted to a caller (GetApplication).
        /// The GDS supplies the standard rule.
        /// </summary>
        ApplicationQuery,

        /// <summary>
        /// GDS Part 12 §7.6 certificate management (StartSigningRequest, StartNewKeyPairRequest,
        /// FinishRequest, GetCertificateGroups, GetTrustList, GetCertificateStatus).
        /// The GDS supplies the standard rule.
        /// </summary>
        CertificateManagement,

        /// <summary>
        /// GDS Part 12 §7.6 RevokeCertificate. The GDS supplies the standard rule.
        /// </summary>
        CertificateRevocation,

        /// <summary>
        /// GDS Part 12 §8.5 KeyCredential service (StartRequest, FinishRequest, Revoke).
        /// The GDS supplies the standard rule.
        /// </summary>
        KeyCredentialManagement,

        /// <summary>
        /// Part 21 device onboarding administration (registrar acceptance, provisioning).
        /// The GDS supplies the standard rule.
        /// </summary>
        DeviceOnboarding
    }
}
