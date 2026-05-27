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

#nullable enable

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
