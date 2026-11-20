// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens.Views;

/// <summary>
/// Pure presentation mapping for the connection strip. Kept free of Avalonia so
/// the security summary, indicator and primary-button label are deterministic
/// and unit-testable without a desktop.
/// </summary>
internal static class ConnectionPresentation
{
    /// <summary>
    /// Human-readable, non-secret security and identity summary for a profile.
    /// </summary>
    public static string Describe(ConnectionProfile? profile)
    {
        if (profile is null)
        {
            return "—";
        }
        string identity = profile.IdentityType == UserTokenType.UserName
            ? (string.IsNullOrEmpty(profile.IdentityName) ? "user" : profile.IdentityName!)
            : profile.IdentityType.ToString();
        return profile.SecurityMode == MessageSecurityMode.None
            ? $"None · {identity}"
            : $"{ShortPolicy(profile.SecurityPolicyUri)} {profile.SecurityMode} · {identity}";
    }

    /// <summary>
    /// The trailing policy token of a security-policy URI (for example Basic256Sha256).
    /// </summary>
    public static string ShortPolicy(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return "None";
        }
        int cut = Math.Max(uri.LastIndexOf('#'), uri.LastIndexOf('/'));
        return cut >= 0 && cut < uri.Length - 1 ? uri[(cut + 1)..] : uri;
    }

    /// <summary>
    /// The primary connect-button label for the current phase.
    /// </summary>
    public static string ConnectButtonLabel(ConnectionPhase phase) => phase switch
    {
        ConnectionPhase.Connecting => "Cancel",
        ConnectionPhase.Connected or ConnectionPhase.Reconnecting => "Disconnect",
        _ => "Connect"
    };

    /// <summary>
    /// The indicator brush key and label. Keep-alive loss is only surfaced when
    /// the connection has no recent activity, so a latched SDK flag is not misread.
    /// </summary>
    public static (string ResourceKey, string Text) Indicator(
        ConnectionPhase phase, bool connected, bool keepAliveLost, bool active)
    {
        if (phase == ConnectionPhase.Connecting)
        {
            return ("AccentYellowLight", "connecting");
        }
        if (phase == ConnectionPhase.Failed)
        {
            return ("AccentRed", "failed");
        }
        if (phase == ConnectionPhase.Reconnecting)
        {
            return ("AccentYellowLight", "reconnecting");
        }
        if (!connected)
        {
            return ("TextDim", "disconnected");
        }
        return keepAliveLost && !active
            ? ("AccentYellowLight", "keep-alive lost")
            : ("AccentGreen", "connected");
    }
}
