// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    /// <summary>
    /// Certificate store names
    /// </summary>
    public enum CertificateStoreName
    {
        /// <summary>
        /// Own store
        /// </summary>
        Application,

        /// <summary>
        /// Rejected store
        /// </summary>
        Rejected,

        /// <summary>
        /// Trusted store
        /// </summary>
        Trusted,

        /// <summary>
        /// Https certificates
        /// </summary>
        Https,

        /// <summary>
        /// User store
        /// </summary>
        User,

        /// <summary>
        /// Opc Ua certificate issuer store
        /// </summary>
        Issuer,

        /// <summary>
        /// Https certificate issuer store
        /// </summary>
        HttpsIssuer,

        /// <summary>
        /// User issuer store
        /// </summary>
        UserIssuer,
    }
}
