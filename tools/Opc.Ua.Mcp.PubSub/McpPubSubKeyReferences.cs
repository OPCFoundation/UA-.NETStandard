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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Tracks only public SKS reference metadata and remote-group invalidation signals.
    /// An authorized remote invalidation closes affected PubSub runtimes across all local callers.
    /// </summary>
    public sealed class McpPubSubKeyReferences
    {
        /// <summary>
        /// Creates bounded key-reference metadata over the existing secret gateway.
        /// </summary>
        public McpPubSubKeyReferences(McpSecretReferences secrets, TimeProvider time)
        {
            m_secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            m_time = time ?? throw new ArgumentNullException(nameof(time));
        }

        /// <summary>
        /// Captures a group epoch before dispatching a key pull so an overlapping invalidation cannot revive it.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal long Epoch(string resource)
        {
            lock (m_state)
            {
                if (!m_epochs.ContainsKey(resource))
                {
                    if (m_epochs.Count >= 4096)
                    {
                        throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                            "The SKS group index is full.");
                    }
                    m_epochs.Add(resource, 0);
                }
                return m_epochs.GetValueOrDefault(resource);
            }
        }

        /// <summary>
        /// Associates a newly stored reference with its remote group and policy.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal void Track(
            McpSecretReference reference, string policyUri, long epoch, EndpointDescription keyService,
            DateTimeOffset? notBefore = null)
        {
            ArgumentNullException.ThrowIfNull(keyService);
            var endpoint = new Uri(PubSubInputValidation.Text(keyService.EndpointUrl), UriKind.Absolute);
            if (endpoint.UserInfo.Length != 0 ||
                endpoint.Fragment.Length != 0 ||
                keyService.SecurityMode != MessageSecurityMode.SignAndEncrypt)
            {
                throw new ServiceResultException(StatusCodes.BadSecurityModeInsufficient,
                    "SKS reference metadata must identify the actual encrypted issuing service.");
            }
            lock (m_state)
            {
                foreach (string expired in m_entries.Where(pair =>
                    pair.Value.Reference.ExpiresAt <= m_time.GetUtcNow() || pair.Value.Invalidated.Task.IsCompleted)
                    .Select(pair => pair.Key).ToArray())
                {
                    m_entries.Remove(expired);
                }
                if (m_entries.Count >= 4096)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations,
                        "The SKS reference index is full.");
                }
                var entry = new KeyReference(
                    reference, policyUri, notBefore ?? m_time.GetUtcNow(), CopyEndpoint(keyService));
                if (m_epochs.GetValueOrDefault(reference.Resource) != epoch)
                {
                    entry.Invalidated.TrySetResult();
                }
                m_entries.Add(reference.Reference, entry);
            }
        }

        /// <summary>
        /// Adds the remote-group invalidation signal to an owner-authorized secret dependency.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal McpSecretDependency Dependency(PubSubSecurityInput input)
        {
            McpSecretDependency secret = m_secrets.GetDependency(
                input.KeyReference, input.Resource, McpCapability.PubSubSecurity);
            lock (m_state)
            {
                if (!m_entries.TryGetValue(input.KeyReference, out KeyReference? entry) ||
                    entry.PolicyUri != input.PolicyUri ||
                    entry.Reference.Resource != input.Resource ||
                    entry.NotBefore > m_time.GetUtcNow() ||
                    entry.Invalidated.Task.IsCompleted)
                {
                    throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected,
                        "The SKS reference is unknown, invalidated, or bound to a different policy/group.");
                }
                entry.Combined ??= Task.WhenAny(secret.Invalidated, entry.Invalidated.Task);
                return new McpSecretDependency(secret.Reference, entry.Combined);
            }
        }

        /// <summary>
        /// Returns the actual encrypted SKS endpoint associated with an authorized key.
        /// This is descriptive standard configuration metadata, not permission for an automatic key pull.
        /// </summary>
        internal EndpointDescription KeyService(PubSubSecurityInput input)
        {
            _ = Dependency(input);
            lock (m_state)
            {
                return CopyEndpoint(m_entries[input.KeyReference].KeyService);
            }
        }

        /// <summary>
        /// Copies only public endpoint identity and security metadata, never user tokens or caller credentials.
        /// </summary>
        private static EndpointDescription CopyEndpoint(EndpointDescription source)
        {
            return new EndpointDescription
            {
                EndpointUrl = source.EndpointUrl,
                SecurityMode = source.SecurityMode,
                SecurityPolicyUri = source.SecurityPolicyUri,
                Server = new ApplicationDescription { ApplicationUri = source.Server.ApplicationUri }
            };
        }

        /// <summary>
        /// Invalidates a remotely changed group without using caller claims to choose another owner.
        /// The invoking tool already authorized the exact server operation.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal int Invalidate(string resource)
        {
            lock (m_state)
            {
                if (!m_epochs.ContainsKey(resource) && m_epochs.Count >= 4096)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations, "The SKS group index is full.");
                }
                m_epochs[resource] = checked(m_epochs.GetValueOrDefault(resource) + 1);
                int count = 0;
                foreach (KeyReference entry in m_entries.Values.Where(entry => entry.Reference.Resource == resource))
                {
                    if (entry.Invalidated.TrySetResult())
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        private readonly McpSecretReferences m_secrets;
        private readonly TimeProvider m_time;
        private readonly System.Threading.Lock m_state = new();
        private readonly Dictionary<string, KeyReference> m_entries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> m_epochs = new(StringComparer.Ordinal);

        /// <summary>
        /// Contains public metadata only, never materialized key bytes.
        /// </summary>
        private sealed class KeyReference(
            McpSecretReference reference, string policyUri, DateTimeOffset notBefore, EndpointDescription keyService)
        {
            /// <summary>Gets protected reference metadata.</summary>
            internal McpSecretReference Reference { get; } = reference;

            /// <summary>Gets the exact SKS policy identity.</summary>
            internal string PolicyUri { get; } = policyUri;

            /// <summary>Gets the earliest valid activation time of a prefetched key.</summary>
            internal DateTimeOffset NotBefore { get; } = notBefore;

            /// <summary>Gets actual public SKS endpoint metadata.</summary>
            internal EndpointDescription KeyService { get; } = keyService;

            /// <summary>Gets the remote-group invalidation signal.</summary>
            internal TaskCompletionSource Invalidated { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Gets or sets the cached combined revocation signal.</summary>
            internal Task? Combined { get; set; }
        }
    }
}
