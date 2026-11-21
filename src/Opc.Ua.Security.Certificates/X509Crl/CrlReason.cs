// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// CRL Reason codes.
    /// </summary>
    /// <remarks>
    /// id-ce-cRLReasons OBJECT IDENTIFIER ::= { id-ce 21 }
    ///   -- reasonCode::= { CRLReason }
    /// CRLReason::= ENUMERATED {
    ///      unspecified(0),
    ///      keyCompromise(1),
    ///      cACompromise(2),
    ///      affiliationChanged(3),
    ///      superseded(4),
    ///      cessationOfOperation(5),
    ///      certificateHold(6),
    ///           --value 7 is not used
    ///      removeFromCRL(8),
    ///      privilegeWithdrawn(9),
    ///      aACompromise(10) }
    /// </remarks>
#pragma warning disable CA1027 // Flags does not work with asn.1 enumerated types
    public enum CRLReason
#pragma warning restore CA1027
    {
        /// <summary>
        /// Unspecified
        /// </summary>
        Unspecified = 0,

        /// <summary>
        /// KeyCompromise
        /// </summary>
        KeyCompromise = 1,

        /// <summary>
        /// CACompromise
        /// </summary>
        CACompromise = 2,

        /// <summary>
        /// AffiliationChanged
        /// </summary>
        AffiliationChanged = 3,

        /// <summary>
        /// Superseded
        /// </summary>
        Superseded = 4,

        /// <summary>
        /// CessationOfOperation
        /// </summary>
        CessationOfOperation = 5,

        /// <summary>
        /// CertificateHold
        /// </summary>
        CertificateHold = 6,

        /// <summary>
        /// RemoveFromCRL
        /// </summary>
        RemoveFromCRL = 8,

        /// <summary>
        /// PrivilegeWithdrawn
        /// </summary>
        PrivilegeWithdrawn = 9,

        /// <summary>
        /// AACompromise
        /// </summary>
        AACompromise = 10
    }
}
