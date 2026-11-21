// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Identity;

namespace UaLens.Connection
{
    internal enum IdentityInteractionStage
    {
        ContactingAuthority,
        WaitingForUser,
        Completing
    }

    /// <summary>
    /// Optional interaction owned by the already registered token provider.
    /// It configures authorization, not a token result. Credentials remain with
    /// the provider; the normal identity flow acquires a fresh token for Connect.
    /// </summary>
    internal interface IIdentityTokenInteraction
    {
        Task AuthorizeAsync(
            AuthorizationServerMetadata metadata,
            IProgress<IdentityInteractionStage>? progress,
            CancellationToken cancellationToken);
    }

    internal static class IdentityTokenInteraction
    {
        public static AuthorizationServerMetadata PrepareMetadata(
            ConfiguredAccessTokenSource source, AuthorizationServerMetadata metadata)
        {
            if (!string.Equals(metadata.AuthorityUri, source.AuthorityUri, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(metadata.ResourceUri))
            {
                throw new ConnectionIdentityException(
                    ConnectionIdentityFailure.Incompatible, "The advertised authority/resource is not configured.");
            }
            return metadata with
            {
                TokenEndpoint = null,
                AuthorizationEndpoint = null,
                JwksUri = null,
                AdditionalFields = new Dictionary<string, System.Text.Json.JsonElement>()
            };
        }

        public static async Task AuthorizeAsync(
            ConfiguredAccessTokenSource source, UserTokenPolicy policy,
            IProgress<IdentityInteractionStage>? progress, CancellationToken cancellationToken,
            TimeProvider? timeProvider = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(policy);
            cancellationToken.ThrowIfCancellationRequested();
            IIdentityTokenInteraction interaction = source.Interaction ??
                throw new InvalidOperationException("This provider requires external authorization setup.");
            if (policy.TokenType != UserTokenType.IssuedToken ||
                policy.IssuedTokenType != source.ProfileUri ||
                !AuthorizationServerMetadata.TryFromPolicy(policy, out AuthorizationServerMetadata metadata))
            {
                throw new ConnectionIdentityException(
                    ConnectionIdentityFailure.Incompatible, "The selected issued-token policy is incompatible.");
            }
            AuthorizationServerMetadata request = PrepareMetadata(source, metadata);
            using var deadline = new CancellationTokenSource(
                TimeSpan.FromMinutes(5), timeProvider ?? TimeProvider.System);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            try
            {
                await interaction.AuthorizeAsync(request, progress, budget.Token).WaitAsync(budget.Token)
                    .ConfigureAwait(false);
                budget.Token.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                throw new OperationCanceledException("Configured provider authorization canceled or expired.",
                    budget.Token);
            }
            catch (Exception error) when (error is UnauthorizedAccessException or ServiceResultException or
                InvalidOperationException or System.IO.IOException or System.Net.Http.HttpRequestException or
                TimeoutException or System.Security.Cryptography.CryptographicException or
                System.Security.Authentication.AuthenticationException or ArgumentException or
                FormatException or NotSupportedException)
            {
                throw new ConnectionIdentityException(ConnectionIdentityFailure.Denied,
                    "The configured provider could not complete authorization. No other authority was contacted.");
            }
        }
    }
}
