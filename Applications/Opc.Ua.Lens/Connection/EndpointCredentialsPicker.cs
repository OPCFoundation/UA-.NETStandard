/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Opc.Ua;
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

        var picker = new EndpointPickerDialog(endpoints);
        var pick = await picker.ShowDialog<(EndpointDescription, UserTokenPolicy?)?>(owner).ConfigureAwait(true);
        if (pick is null || pick.Value.Item1 is null)
        {
            return null;
        }
        EndpointDescription endpoint = pick.Value.Item1;
        UserTokenPolicy? policy = pick.Value.Item2;

        // Build identity:
        //   - UserName policy → CredentialsDialog → UserIdentity(user, password).
        //   - Anything else (endpoint root, explicit Anonymous, etc.) → AnonymousIdentityToken.
        // CA2000: ownership of the IUserIdentity transfers to the caller.
#pragma warning disable CA2000
        IUserIdentity identity;
        if (policy is { TokenType: UserTokenType.UserName })
        {
            var creds = new CredentialsDialog();
            var pair = await creds.ShowDialog<(string, string)?>(owner).ConfigureAwait(true);
            if (pair is null)
            {
                return null;
            }
            (string user, string pass) = pair.Value;
            identity = new UserIdentity(user, Encoding.UTF8.GetBytes(pass));
        }
        else
        {
            identity = new UserIdentity(new AnonymousIdentityToken());
        }
#pragma warning restore CA2000

        return new Result(endpoint, identity, policy);
    }

    /// <summary>
    /// Endpoint-equality used by the GDS plugins to decide whether to
    /// reuse the existing secondary session via <c>UpdateSessionAsync</c>
    /// or tear it down and reconnect.  Compares URL (case-insensitive,
    /// trimmed), SecurityMode and SecurityPolicyUri.
    /// </summary>
    public static bool EndpointsMatch(EndpointDescription? a, EndpointDescription? b)
    {
        if (a is null || b is null)
        {
            return false;
        }
        return string.Equals(
                   (a.EndpointUrl ?? string.Empty).Trim(),
                   (b.EndpointUrl ?? string.Empty).Trim(),
                   StringComparison.OrdinalIgnoreCase)
               && a.SecurityMode == b.SecurityMode
               && string.Equals(
                   a.SecurityPolicyUri ?? string.Empty,
                   b.SecurityPolicyUri ?? string.Empty,
                   StringComparison.Ordinal);
    }
}
