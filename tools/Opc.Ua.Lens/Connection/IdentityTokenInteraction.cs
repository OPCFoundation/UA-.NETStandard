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
