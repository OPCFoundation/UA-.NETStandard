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
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Immutable registration provenance shared by captured service views, with explicit lifetime revocation.
    /// </summary>
    internal sealed class McpSessionAuthority
    {
        /// <summary>
        /// Binds a new registration to its establishing caller and actual authenticated endpoint.
        /// Only a strict host-established registration is eligible for protected secrets.
        /// </summary>
        internal McpSessionAuthority(
            McpCallerIdentity? owner, bool autoAccepted, EndpointDescription endpoint)
        {
            Owner = owner;
            AutoAccepted = autoAccepted;
            TargetIdentity = GetTargetIdentity(endpoint);
        }

        /// <summary>
        /// Gets the non-reusable registration identity.
        /// </summary>
        internal string RegistrationId { get; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Gets the establishing caller, or null for explicit trusted host provisioning.
        /// </summary>
        internal McpCallerIdentity? Owner { get; }

        /// <summary>
        /// Gets whether this connection bypassed the host's strict peer-trust policy.
        /// </summary>
        internal bool AutoAccepted { get; }

        /// <summary>
        /// Gets the endpoint address, application, certificate and transport-security binding.
        /// </summary>
        internal string TargetIdentity { get; }

        /// <summary>
        /// Gets whether this exact registration is still live.
        /// </summary>
        internal bool Active => Volatile.Read(ref m_retired) == 0;

        /// <summary>
        /// Invalidates all retained captured views when the registration is removed or replaced.
        /// </summary>
        internal void Retire()
        {
            Interlocked.Exchange(ref m_retired, 1);
        }

        /// <summary>
        /// Hashes length-delimited public endpoint identity rather than trusting an advertised application URI.
        /// </summary>
        internal static string GetTargetIdentity(EndpointDescription endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            string[] fields =
            [
                endpoint.EndpointUrl ?? string.Empty,
                endpoint.Server?.ApplicationUri ?? string.Empty,
                endpoint.SecurityMode.ToString(),
                endpoint.SecurityPolicyUri ?? string.Empty,
                endpoint.TransportProfileUri ?? string.Empty,
                Convert.ToHexString(endpoint.ServerCertificate.Span)
            ];
            foreach (string field in fields)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(field);
                hash.AppendData(BitConverter.GetBytes(bytes.Length));
                hash.AppendData(bytes);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        /// <summary>Whether the manager retired this registration.</summary>
        private int m_retired;
    }
}
