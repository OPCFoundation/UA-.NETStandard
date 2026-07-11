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
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Security.Policies;

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Default <see cref="IPubSubSecurityWrapperResolver"/>. Inspects a
    /// <see cref="PubSubConnectionDataType"/>, determines the effective
    /// <see cref="MessageSecurityMode"/> and <c>SecurityGroupId</c> from
    /// its WriterGroups and ReaderGroups, and materialises a configured
    /// <see cref="UadpSecurityWrapper"/> bound to the matching
    /// <see cref="IPubSubSecurityKeyProvider"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resolves the per-connection security context per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.7">
    /// Part 14 §6.2.7</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// §8.3 Security Key Service</see>. Key material is sourced from the
    /// supplied <see cref="IPubSubSecurityKeyProvider"/> instances keyed
    /// by their <see cref="IPubSubSecurityKeyProvider.SecurityGroupId"/>.
    /// </para>
    /// <para>
    /// The resolver is fail-closed: it returns <see langword="null"/>
    /// for <see cref="MessageSecurityMode.None"/> connections, and also
    /// returns <see langword="null"/> when a secured connection cannot
    /// be matched to a key provider or policy. The caller treats a
    /// <see langword="null"/> result for a secured connection as a hard
    /// configuration error and refuses to publish or receive in the
    /// clear.
    /// </para>
    /// </remarks>
    public sealed class PubSubSecurityWrapperResolver : IPubSubSecurityWrapperResolver
    {
        private readonly Dictionary<string, IPubSubSecurityKeyProvider> m_keyProviders;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly TimeProvider m_timeProvider;
        private readonly INonceProvider? m_nonceProvider;

        private readonly Func<PubSubConnectionDataType, string, IPubSubSecurityPolicy?>
            m_policySelector;

        private readonly int m_replayWindowSize;

        /// <summary>
        /// Initializes a new <see cref="PubSubSecurityWrapperResolver"/>.
        /// </summary>
        /// <param name="keyProviders">
        /// Key providers keyed by <c>SecurityGroupId</c>. May be empty,
        /// in which case every secured connection fails to resolve.
        /// </param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="timeProvider">
        /// Optional clock for the per-connection replay window. Defaults
        /// to <see cref="TimeProvider.System"/>.
        /// </param>
        /// <param name="nonceProvider">
        /// Optional shared nonce provider. When <see langword="null"/> a
        /// per-connection <see cref="RandomNonceProvider"/> seeded from
        /// the connection's PublisherId is created.
        /// </param>
        /// <param name="policySelector">
        /// Optional callback mapping a connection plus SecurityGroupId to
        /// the <see cref="IPubSubSecurityPolicy"/> bundle to use. Defaults
        /// to <see cref="PubSubAes256CtrPolicy"/>.
        /// </param>
        /// <param name="replayWindowSize">
        /// Receive-side replay history size. Defaults to 1024.
        /// </param>
        public PubSubSecurityWrapperResolver(
            IEnumerable<IPubSubSecurityKeyProvider> keyProviders,
            ITelemetryContext telemetry,
            TimeProvider? timeProvider = null,
            INonceProvider? nonceProvider = null,
            Func<PubSubConnectionDataType, string, IPubSubSecurityPolicy?>? policySelector = null,
            int replayWindowSize = 1024)
        {
            if (keyProviders is null)
            {
                throw new ArgumentNullException(nameof(keyProviders));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (replayWindowSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(replayWindowSize),
                    "Replay window size must be positive.");
            }
            m_keyProviders = new Dictionary<string, IPubSubSecurityKeyProvider>(
                StringComparer.Ordinal);
            foreach (IPubSubSecurityKeyProvider provider in keyProviders)
            {
                if (provider is null)
                {
                    continue;
                }
                m_keyProviders[provider.SecurityGroupId] = provider;
            }
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<PubSubSecurityWrapperResolver>();
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_nonceProvider = nonceProvider;
            m_policySelector = policySelector
                ?? ((_, _) => PubSubAes256CtrPolicy.Instance);
            m_replayWindowSize = replayWindowSize;
        }

        /// <inheritdoc/>
        public PubSubSecurityContext? Resolve(PubSubConnectionDataType connection)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (!TryResolveConnectionSecurity(
                connection,
                out MessageSecurityMode mode,
                out string securityGroupId))
            {
                // SecurityMode == None for every group: no wrapping.
                return null;
            }

            if (!m_keyProviders.TryGetValue(
                securityGroupId,
                out IPubSubSecurityKeyProvider? keyProvider))
            {
                m_logger.LogWarning(
                    "No key provider registered for SecurityGroupId '{SecurityGroupId}' " +
                    "required by secured connection '{Connection}'.",
                    securityGroupId,
                    connection.Name);
                return null;
            }

            IPubSubSecurityPolicy? policy = m_policySelector(connection, securityGroupId);
            if (policy is null ||
                string.Equals(
                    policy.PolicyUri,
                    PubSubSecurityPolicyUri.None,
                    StringComparison.Ordinal))
            {
                m_logger.LogWarning(
                    "No usable security policy for SecurityGroupId '{SecurityGroupId}' " +
                    "required by secured connection '{Connection}'.",
                    securityGroupId,
                    connection.Name);
                return null;
            }

            PublisherId publisherId = connection.PublisherId.IsNull
                ? PublisherId.Null
                : PublisherId.From(connection.PublisherId);
            // Ownership of the per-connection nonce provider transfers to
            // the returned UadpSecurityWrapper, which lives for the
            // connection lifetime; it is therefore not disposed here.
            // TODO: plumb wrapper disposal so the RNG is released on
            // connection teardown.
#pragma warning disable CA2000
            INonceProvider nonceProvider = m_nonceProvider
                ?? new RandomNonceProvider(publisherId, m_timeProvider);
#pragma warning restore CA2000
            var window = new SecurityTokenWindow(m_replayWindowSize, m_timeProvider);
            PrimeReplayWindow(keyProvider, window);

            var wrapper = new UadpSecurityWrapper(
                policy,
                keyProvider,
                nonceProvider,
                window,
                m_telemetry);

            UadpSecurityWrapOptions options = mode == MessageSecurityMode.SignAndEncrypt
                ? UadpSecurityWrapOptions.SignAndEncrypt
                : UadpSecurityWrapOptions.SignOnly;

            return new PubSubSecurityContext(wrapper, options);
        }

        /// <summary>
        /// Computes the strictest <see cref="MessageSecurityMode"/>
        /// requested across the connection's WriterGroups and
        /// ReaderGroups and the SecurityGroupId backing it.
        /// </summary>
        /// <param name="connection">Connection configuration.</param>
        /// <param name="mode">
        /// Resolved strictest <see cref="MessageSecurityMode"/>.
        /// </param>
        /// <param name="securityGroupId">
        /// SecurityGroupId of the secured group.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when at least one group requests
        /// <see cref="MessageSecurityMode.Sign"/> or
        /// <see cref="MessageSecurityMode.SignAndEncrypt"/>; otherwise
        /// <see langword="false"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static bool TryResolveConnectionSecurity(
            PubSubConnectionDataType connection,
            out MessageSecurityMode mode,
            out string securityGroupId)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            mode = MessageSecurityMode.None;
            securityGroupId = string.Empty;
            int bestRank = 0;

            if (!connection.WriterGroups.IsNull)
            {
                foreach (WriterGroupDataType group in connection.WriterGroups)
                {
                    Consider(group.SecurityMode, group.SecurityGroupId,
                        ref mode, ref securityGroupId, ref bestRank);
                }
            }
            if (!connection.ReaderGroups.IsNull)
            {
                foreach (ReaderGroupDataType group in connection.ReaderGroups)
                {
                    Consider(group.SecurityMode, group.SecurityGroupId,
                        ref mode, ref securityGroupId, ref bestRank);
                }
            }

            return bestRank > 0;
        }

        private static void Consider(
            MessageSecurityMode groupMode,
            string? groupSecurityGroupId,
            ref MessageSecurityMode mode,
            ref string securityGroupId,
            ref int bestRank)
        {
            int rank = SecurityRank(groupMode);
            if (rank <= bestRank)
            {
                return;
            }
            bestRank = rank;
            mode = groupMode;
            securityGroupId = groupSecurityGroupId ?? string.Empty;
        }

        private static int SecurityRank(MessageSecurityMode mode)
        {
            return mode switch
            {
                MessageSecurityMode.Sign => 1,
                MessageSecurityMode.SignAndEncrypt => 2,
                _ => 0
            };
        }

        private void PrimeReplayWindow(
            IPubSubSecurityKeyProvider keyProvider,
            SecurityTokenWindow window)
        {
            // Register the currently active token so the receive side
            // accepts the first secured frame; subsequent tokens are
            // registered as the provider rotates.
            try
            {
                System.Threading.Tasks.ValueTask<PubSubSecurityKey> currentTask =
                    keyProvider.GetCurrentKeyAsync();
                if (currentTask.IsCompletedSuccessfully)
                {
                    // Reading the result of an already-completed ValueTask
                    // is not a blocking sync-over-async wait.
                    window.RegisterToken(currentTask.Result.TokenId);
                }
            }
            catch (InvalidOperationException)
            {
                // No current token yet; it will be registered on the
                // first KeyRotated notification.
            }
            keyProvider.KeyRotated += (_, e) => window.RegisterToken(e.NewTokenId);
        }
    }
}
