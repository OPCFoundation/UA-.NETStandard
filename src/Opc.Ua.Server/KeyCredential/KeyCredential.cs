// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Resource-server copy of a pushed OPC 10000-12 §8 KeyCredential.
    /// </summary>
    public sealed record KeyCredential
    {
        /// <summary>
        /// Creates a credential record.
        /// </summary>
        public KeyCredential(
            byte[] secret,
            DateTime expiration,
            IReadOnlyDictionary<string, object?>? subject = null,
            IReadOnlyList<string>? scopes = null)
        {
            if (secret == null)
            {
                throw new ArgumentNullException(nameof(secret));
            }

            Secret = (byte[])secret.Clone();
            Expiration = expiration;
            var subjectCopy = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (subject != null)
            {
                foreach (KeyValuePair<string, object?> item in subject)
                {
                    subjectCopy[item.Key] = item.Value;
                }
            }
            Subject = subjectCopy;
            Scopes = scopes == null ? Array.Empty<string>() : new List<string>(scopes).AsReadOnly();
        }

        /// <summary>
        /// Shared secret associated with the credential.
        /// </summary>
        public byte[] Secret { get; init; }

        /// <summary>
        /// UTC expiration time for the credential.
        /// </summary>
        public DateTime Expiration { get; init; }

        /// <summary>
        /// Claims describing the principal associated with the credential.
        /// </summary>
        public IReadOnlyDictionary<string, object?> Subject { get; init; }

        /// <summary>
        /// Granted scopes associated with the credential.
        /// </summary>
        public IReadOnlyList<string> Scopes { get; init; }
    }
}
