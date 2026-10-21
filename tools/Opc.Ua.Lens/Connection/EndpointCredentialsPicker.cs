/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Opc.Ua;
using Opc.Ua.Identity;
using UaLens.Views;

namespace UaLens.Connection;

/// <summary>
/// Unifies the "pick an endpoint and supply credentials" flow used by the
/// main <see cref="Views.MainWindow"/> Connect button and by the GDS Push /
/// GDS Management plugins.
///
/// The flow is: <see cref="DiscoveryService"/> → <see cref="EndpointPickerDialog"/>
/// → (if the picked policy is UserName) <see cref="CredentialsDialog"/>.
/// </summary>
internal static class EndpointCredentialsPicker
{
    /// <summary>
    /// Result of a successful pick.  The <see cref="IUserIdentity"/> is
    /// freshly allocated; ownership transfers to the caller, who is
    /// responsible for handing it to the session (which then disposes
    /// it when the session is disposed).
    /// </summary>
    internal sealed record Result(
        EndpointDescription Endpoint,
        IUserIdentity Identity,
        UserTokenPolicy? Policy);

    /// <summary>
    /// Primary connection flow. The returned owner retains a provider, not an
    /// eager token; canceling/abandoning the selection releases interactive
    /// username material without affecting host-owned certificate/token providers.
    /// </summary>
    public static async Task<ConnectionSelection?> PromptProviderAsync(
        Window owner,
        ArrayOf<EndpointDescription> endpoints,
        ConnectionIdentityConfiguration identities,
        ApplicationConfiguration application,
        SubscriptionEngineKind engine,
        Func<ConnectionProfile, CancellationToken, ValueTask<IClientIdentityProvider>>? resolver = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var picker = new EndpointPickerDialog(endpoints, allowConfiguredIdentities: true);
        (EndpointDescription, UserTokenPolicy?)? pick = await ShowCancelableAsync<
            (EndpointDescription, UserTokenPolicy?)?>(picker, owner, ct).ConfigureAwait(true);
        if (pick is not { } selected || selected.Item2 is not { } policy)
        {
            return null;
        }
        return await PromptIdentityAsync(
            owner, selected.Item1, policy, identities, application, engine, null, resolver, ct).ConfigureAwait(true);
    }

    /// <summary>
    /// Reacquires the saved reference without substituting another authority,
    /// user, key or policy. Legacy profiles without a certificate/token reference
    /// may explicitly select one, still within the saved endpoint/token policy.
    /// </summary>
    public static async Task<ConnectionSelection?> PromptProfileAsync(
        Window owner,
        EndpointDescription endpoint,
        ConnectionProfile profile,
        ConnectionIdentityConfiguration identities,
        ApplicationConfiguration application,
        Func<ConnectionProfile, CancellationToken, ValueTask<IClientIdentityProvider>> resolver,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        UserTokenPolicy policy = profile.RequireMatch(endpoint);
        if ((profile.IdentityType == UserTokenType.UserName && profile.CredentialReference is null) ||
            (profile.IdentityType == UserTokenType.Certificate && profile.CertificateIdentity is null) ||
            (profile.IdentityType == UserTokenType.IssuedToken && profile.IssuedIdentity is null))
        {
            return await PromptIdentityAsync(owner, endpoint, policy, identities, application,
                profile.Engine, profile, resolver, ct).ConfigureAwait(true);
        }
        IClientIdentityProvider provider = await resolver(profile, ct).ConfigureAwait(true);
        var pinned = new ProfileIdentityProvider(profile, provider);
        await pinned.SelectUserTokenPolicyAsync(new IdentitySelectionContext(
            endpoint,
            endpoint.UserIdentityTokens,
            application.CreateMessageContext(),
            application.SecurityConfiguration.SupportedSecurityPolicies), ct).ConfigureAwait(true);
        ct.ThrowIfCancellationRequested();
        return new ConnectionSelection(endpoint, profile, provider);
    }

    private static async Task<ConnectionSelection?> PromptIdentityAsync(
        Window owner,
        EndpointDescription endpoint,
        UserTokenPolicy policy,
        ConnectionIdentityConfiguration identities,
        ApplicationConfiguration application,
        SubscriptionEngineKind engine,
        ConnectionProfile? restored,
        Func<ConnectionProfile, CancellationToken, ValueTask<IClientIdentityProvider>>? resolver,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (policy.TokenType is UserTokenType.Certificate or UserTokenType.IssuedToken)
        {
            var referenceDialog = new IdentityReferenceDialog(
                endpoint, policy, engine, identities, application,
                restored?.CertificateIdentity is not null || restored?.IssuedIdentity is not null ? restored : null,
                resolver);
            await using (referenceDialog.ConfigureAwait(false))
            {
                return await referenceDialog.PromptAsync(owner, ct).ConfigureAwait(true);
            }
        }
        if (policy.TokenType == UserTokenType.Anonymous)
        {
            ConnectionProfile profile = restored ?? ConnectionProfile.Create(endpoint, policy, engine);
            IClientIdentityProvider provider = resolver is null
                ? new AnonymousIdentityProvider()
                : await resolver(profile, ct).ConfigureAwait(true);
            return new ConnectionSelection(endpoint, profile, provider);
        }
        if (policy.TokenType != UserTokenType.UserName)
        {
            throw new NotSupportedException("The server's selected user-token type has no configured identity flow.");
        }
        var credentials = new CredentialsDialog(restored?.IdentityName);
        (string, string)? pair = await ShowCancelableAsync<(string, string)?>(credentials, owner, ct)
            .ConfigureAwait(true);
        if (pair is null)
        {
            return null;
        }
        (string user, string passwordText) = pair.Value;
        if (restored is not null && !string.Equals(user, restored.IdentityName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Restoring this profile requires the saved username. Use a new connection to change identity.");
        }
        ConnectionProfile selectedProfile = restored ?? ConnectionProfile.Create(endpoint, policy, engine, user);
        byte[] password = Encoding.UTF8.GetBytes(passwordText);
        ConnectionCredentials ownerCredentials;
        try
        {
            var identity = new UserIdentity(user, password.AsSpan()) { PolicyId = policy.PolicyId! };
            ownerCredentials = await ConnectionCredentials.FromIdentityAsync(selectedProfile, identity, ct)
                .ConfigureAwait(true);
        }
        finally
        {
            Array.Clear(password);
        }
        return new ConnectionSelection(endpoint, selectedProfile, ownerCredentials);
    }

    internal static async Task<T> ShowCancelableAsync<T>(Window dialog, Window owner, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CancellationTokenRegistration registration = ct.Register(() =>
            Dispatcher.UIThread.Post(() =>
            {
                if (dialog.IsVisible)
                {
                    dialog.Close();
                }
            }));
        await using (registration.ConfigureAwait(true))
        {
            T result = await dialog.ShowDialog<T>(owner).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            return result;
        }
    }

    /// <summary>
    /// Runs the full pick + credentials flow.  Returns null if the user
    /// cancelled at any step.
    /// </summary>
    /// <param name="owner">Modal owner — usually the main window.</param>
    /// <param name="telemetry">Telemetry context for discovery logging.</param>
    /// <param name="defaultEndpointUrl">URL used as the discovery seed.</param>
    /// <param name="ct">Cancellation token for the discovery call.</param>
    public static async Task<Result?> PromptAsync(
        Window owner,
        ITelemetryContext telemetry,
        string defaultEndpointUrl,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(telemetry);

        if (string.IsNullOrWhiteSpace(defaultEndpointUrl))
        {
            return null;
        }

        var discovery = new DiscoveryService(telemetry);
        ArrayOf<EndpointDescription> endpoints =
            await discovery.DiscoverAsync(defaultEndpointUrl, ct).ConfigureAwait(true);
        return await PromptAsync(owner, endpoints, ct).ConfigureAwait(true);
    }

    /// <summary>
    /// Prompts over already discovered endpoints. Hosts can discover with their
    /// own configuration/PKI, and dialog-result handling can be tested without
    /// network access or default user certificate stores.
    /// </summary>
    public static async Task<Result?> PromptAsync(
        Window owner,
        ArrayOf<EndpointDescription> endpoints,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ct.ThrowIfCancellationRequested();
        var picker = new EndpointPickerDialog(endpoints);
        var pick = await ShowCancelableAsync<(EndpointDescription, UserTokenPolicy?)?>(picker, owner, ct)
            .ConfigureAwait(true);
        if (pick is null || pick.Value.Item1 is null)
        {
            return null;
        }
        EndpointDescription endpoint = pick.Value.Item1;
        UserTokenPolicy? policy = pick.Value.Item2;

        if (policy is null)
        {
            foreach (UserTokenPolicy offered in endpoint.UserIdentityTokens)
            {
                if (offered.TokenType == UserTokenType.Anonymous)
                {
                    policy = offered;
                    break;
                }
            }
            if (policy is null)
            {
                throw new InvalidOperationException(
                    "The endpoint does not offer Anonymous access. Select an identity policy explicitly.");
            }
        }

        IUserIdentity identity;
        if (policy is { TokenType: UserTokenType.UserName })
        {
            var creds = new CredentialsDialog();
            var pair = await ShowCancelableAsync<(string, string)?>(creds, owner, ct).ConfigureAwait(true);
            if (pair is null)
            {
                return null;
            }
            (string user, string pass) = pair.Value;
            byte[] password = Encoding.UTF8.GetBytes(pass);
            try
            {
                identity = new UserIdentity(user, password.AsSpan()) { PolicyId = policy.PolicyId! };
            }
            finally
            {
                Array.Clear(password);
            }
        }
        else if (policy.TokenType == UserTokenType.Anonymous)
        {
            identity = new UserIdentity(new AnonymousIdentityToken()) { PolicyId = policy.PolicyId! };
        }
        else
        {
            throw new NotSupportedException(
                "This picker supports Anonymous and UserName identities. " +
                "Use an identity provider for this token type.");
        }

        return new Result(endpoint, identity, policy);
    }

    /// <summary>
    /// Endpoint-equality used by the GDS plugins to decide whether to
    /// reuse the existing secondary session via <c>UpdateSessionAsync</c>
    /// or tear it down and reconnect. URL paths remain case-sensitive.
    /// </summary>
    public static bool EndpointsMatch(EndpointDescription? a, EndpointDescription? b)
    {
        if (a is null || b is null)
        {
            return false;
        }
        return ConnectionProfile.EndpointUrlsMatch(a.EndpointUrl, b.EndpointUrl)
               && a.SecurityMode == b.SecurityMode
               && string.Equals(
                   a.SecurityPolicyUri ?? string.Empty,
                   b.SecurityPolicyUri ?? string.Empty,
                   StringComparison.Ordinal);
    }
}
