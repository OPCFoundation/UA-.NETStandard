/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
