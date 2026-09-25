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
using Opc.Ua;

namespace UaLens.Connection;

internal enum ConnectionTransportCheckState
{
    Ready,
    Pending,
    Blocked
}

internal sealed record ConnectionTransportCheck(string Name, ConnectionTransportCheckState State, string Detail)
{
    public override string ToString()
    {
        return $"{Name}: {State} - {Detail}";
    }
}

/// <summary>
/// Side-effect-free guidance for the selected transport intent. Configured
/// registrations are not evidence of private-key access, peer trust or reachability.
/// </summary>
internal sealed record ConnectionTransportReadiness(
    bool CanUseSetup,
    bool CanStartListener,
    bool CanDiscover,
    ArrayOf<ConnectionTransportCheck> Checks)
{
    public static ConnectionTransportReadiness Assess(
        ConnectionTransportCatalog transports,
        ConnectionConfigurationCatalog configurations,
        ConnectionSetupSelection setup,
        ReverseConnectionSnapshot listener,
        ConnectionProfile? selectedProfile = null,
        ArrayOf<EndpointDescription> discoveredEndpoints = default,
        bool discoveryCompleted = false)
    {
        ArgumentNullException.ThrowIfNull(transports);
        ArgumentNullException.ThrowIfNull(configurations);
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(listener);
        var checks = new List<ConnectionTransportCheck>();
        bool valid = Check(checks, "Transport", () => transports.RequireForward(setup.EndpointUrl),
            Uri.TryCreate(setup.EndpointUrl, UriKind.Absolute, out Uri? endpoint) && endpoint.Scheme.Length <= 128
                ? transports.GetCapability(endpoint.Scheme).ToString()
                : "Enter an absolute endpoint URL.");

        valid &= Check(checks, "Application identity", () =>
        {
            if (setup.ApplicationIdentityId is { } id)
            {
                ConnectionReference.Validate(id);
                configurations.ResolveApplication(id).ValidateConfiguration(selectedProfile?.SecurityPolicyUri);
            }
        }, setup.ApplicationIdentityId is { } application
            ? $"Named application key: {application}. Provider/key access is checked only on explicit use."
            : "Default UaLens application key selected explicitly; this is separate from the user identity.");

        ReverseConnectionProfile? reverse = setup.ReverseConnection;
        if (reverse is not null)
        {
            valid &= Check(checks, "Expected reverse peer", () =>
            {
                transports.RequireReverse(reverse);
                reverse.Validate(setup.EndpointUrl, selectedProfile?.ServerApplicationUri);
            }, $"ServerUri AND complete endpoint must match: {reverse.ServerUri} / {reverse.EndpointUrl}");
            valid &= Check(checks, "Listener TLS", () =>
            {
                if (reverse.TlsConfigurationId is { } tls)
                {
                    configurations.ResolveTls(tls);
                }
                else if (Uri.TryCreate(reverse.ListenerUrl, UriKind.Absolute, out Uri? address) &&
                    transports.GetCapability(address.Scheme).RequiresListenerTls)
                {
                    throw new InvalidOperationException(
                        "Reverse WSS requires a named listener certificate and fail-closed peer validator.");
                }
            }, reverse.TlsConfigurationId is { } tlsId
                ? $"Named TLS configuration: {tlsId}. Certificate/key/chain eligibility is checked at explicit Start."
                : "TCP does not use listener TLS. UA secure-channel certificate validation remains required.");
        }
        else
        {
            checks.Add(new ConnectionTransportCheck("Listener TLS", ConnectionTransportCheckState.Ready,
                "Not a local listener. HTTPS/WSS server TLS is validated by the registered forward binding."));
            checks.Add(new ConnectionTransportCheck("Expected server", ConnectionTransportCheckState.Pending,
                selectedProfile?.ServerApplicationUri is { } server
                    ? $"Pinned application URI: {server}. Peer certificates are still validated at connection time."
                    : "Discover, then select the exact endpoint/server application URI, " +
                        "security and user-token policy."));
        }

        valid &= Check(checks, "Selected profile",
            () => transports.RequireSetup(setup, configurations, selectedProfile),
            selectedProfile is null
                ? "Setup intent only. Connect still requires an explicit endpoint/security/user-identity selection."
                : $"{selectedProfile.SecurityMode}; {selectedProfile.SecurityPolicyUri}; " +
                    $"user policy {selectedProfile.UserTokenPolicyId}. No alternate profile is selected on reconnect.");

        if (selectedProfile is not null && discoveryCompleted)
        {
            valid &= Check(checks, "Advertised endpoint", () =>
                RequireAdvertisedProfile(selectedProfile, discoveredEndpoints),
                "The exact endpoint, server application URI, security and user-token policy are advertised. " +
                "Discovery is not certificate-trust validation.");
        }
        else
        {
            checks.Add(new ConnectionTransportCheck("Advertised endpoint", ConnectionTransportCheckState.Pending,
                discoveryCompleted
                    ? $"{discoveredEndpoints.Count} endpoint descriptions discovered. Select one explicitly at Connect."
                    : "Not discovered in this setup. Discovery and certificate validation are explicit operations."));
        }

        bool matchingListener = reverse is not null && listener.Profile == reverse &&
            listener.Phase == ReverseConnectionPhase.Listening;
        if (reverse is not null)
        {
            checks.Add(new ConnectionTransportCheck("Listener",
                matchingListener ? ConnectionTransportCheckState.Ready : ConnectionTransportCheckState.Pending,
                matchingListener
                    ? $"Listening at {reverse.ListenerUrl} for the exact configured peer."
                    : $"Owner state: {listener.Phase}. Start this exact setup explicitly before reverse discovery. " +
                        "Stop a different running listener before changing it."));
        }
        checks.Add(new ConnectionTransportCheck("External prerequisites", ConnectionTransportCheckState.Pending,
            "Check DNS/routing, platform binding support and peer trust. Reverse mode also needs the server's " +
            "configured reverse target, a reachable local bind address and inbound firewall permission. " +
            "UaLens does not provision certificates, install firewall rules or auto-trust a peer."));
        return new ConnectionTransportReadiness(valid,
            valid && reverse is not null && listener.Phase == ReverseConnectionPhase.Stopped,
            valid && (reverse is null || matchingListener), [.. checks]);
    }

    private static void RequireAdvertisedProfile(
        ConnectionProfile profile,
        ArrayOf<EndpointDescription> endpoints)
    {
        foreach (EndpointDescription endpoint in endpoints)
        {
            if (profile.MatchesEndpoint(endpoint))
            {
                foreach (UserTokenPolicy policy in endpoint.UserIdentityTokens)
                {
                    if (profile.MatchesPolicy(policy))
                    {
                        return;
                    }
                }
            }
        }
        throw new ServiceResultException(StatusCodes.BadSecurityPolicyRejected,
            "The server does not advertise the explicitly selected endpoint, application identity and security. " +
            "Choose a different profile explicitly; no fallback is permitted.");
    }

    private static bool Check(
        List<ConnectionTransportCheck> checks,
        string name,
        Action validate,
        string ready)
    {
        try
        {
            validate();
            checks.Add(new ConnectionTransportCheck(name, ConnectionTransportCheckState.Ready, ready));
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or NotSupportedException or ServiceResultException)
        {
            checks.Add(new ConnectionTransportCheck(name, ConnectionTransportCheckState.Blocked, error.Message));
            return false;
        }
    }
}
