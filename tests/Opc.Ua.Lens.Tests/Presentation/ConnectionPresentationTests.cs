// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Views;

namespace UaLens.Tests.Presentation;

[TestFixture]
public sealed class ConnectionPresentationTests
{
    [Test]
    public void DisconnectedProfileIsSummarizedAsPlaceholder()
    {
        Assert.That(ConnectionPresentation.Describe(null), Is.EqualTo("—"));
    }

    [Test]
    public void AnonymousNoneProfileHidesTheEmptyPolicy()
    {
        var profile = new ConnectionProfile
        {
            EndpointUrl = "opc.tcp://host:1/p",
            SecurityMode = MessageSecurityMode.None,
            SecurityPolicyUri = SecurityPolicies.None,
            IdentityType = UserTokenType.Anonymous,
            UserTokenPolicyId = "anon"
        };

        Assert.That(ConnectionPresentation.Describe(profile), Is.EqualTo("None · Anonymous"));
    }

    [Test]
    public void SecureUserNameProfileShowsPolicyModeAndUser()
    {
        var profile = new ConnectionProfile
        {
            EndpointUrl = "opc.tcp://host:1/p",
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            IdentityType = UserTokenType.UserName,
            UserTokenPolicyId = "user",
            IdentityName = "alice"
        };

        Assert.That(
            ConnectionPresentation.Describe(profile),
            Is.EqualTo("Basic256Sha256 SignAndEncrypt · alice"));
    }

    [Test]
    public void UserNameProfileWithoutNameFallsBackToGenericLabel()
    {
        var profile = new ConnectionProfile
        {
            EndpointUrl = "opc.tcp://host:1/p",
            SecurityMode = MessageSecurityMode.Sign,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            IdentityType = UserTokenType.UserName,
            UserTokenPolicyId = "user"
        };

        Assert.That(ConnectionPresentation.Describe(profile), Is.EqualTo("Basic256Sha256 Sign · user"));
    }

    [TestCase("http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256", "Basic256Sha256")]
    [TestCase("http://example.com/policies/Aes256", "Aes256")]
    [TestCase("", "None")]
    public void ShortPolicyReturnsTheTrailingToken(string uri, string expected)
    {
        Assert.That(ConnectionPresentation.ShortPolicy(uri), Is.EqualTo(expected));
    }

    [TestCase((int)ConnectionPhase.Disconnected, "Connect")]
    [TestCase((int)ConnectionPhase.Connecting, "Cancel")]
    [TestCase((int)ConnectionPhase.Connected, "Disconnect")]
    [TestCase((int)ConnectionPhase.Reconnecting, "Disconnect")]
    [TestCase((int)ConnectionPhase.Failed, "Connect")]
    public void ConnectButtonLabelReflectsThePhase(int phase, string expected)
    {
        Assert.That(ConnectionPresentation.ConnectButtonLabel((ConnectionPhase)phase), Is.EqualTo(expected));
    }

    [Test]
    public void IndicatorReportsConnectingBeforeConnected()
    {
        (string key, string text) = ConnectionPresentation.Indicator(
            ConnectionPhase.Connecting, connected: false, keepAliveLost: false, active: false);

        Assert.That(key, Is.EqualTo("AccentYellowLight"));
        Assert.That(text, Is.EqualTo("connecting"));
    }

    [Test]
    public void ReconnectingIsNotPresentedAsDisconnected()
    {
        (string key, string text) = ConnectionPresentation.Indicator(
            ConnectionPhase.Reconnecting, connected: false, keepAliveLost: true, active: false);

        Assert.That(key, Is.EqualTo("AccentYellowLight"));
        Assert.That(text, Is.EqualTo("reconnecting"));
    }

    [Test]
    public void IndicatorReportsFailure()
    {
        (string key, string text) = ConnectionPresentation.Indicator(
            ConnectionPhase.Failed, connected: false, keepAliveLost: false, active: false);

        Assert.That(key, Is.EqualTo("AccentRed"));
        Assert.That(text, Is.EqualTo("failed"));
    }

    [Test]
    public void KeepAliveLossIsHiddenWhileNotificationsStillFlow()
    {
        (string key, string text) = ConnectionPresentation.Indicator(
            ConnectionPhase.Connected, connected: true, keepAliveLost: true, active: true);

        Assert.That(key, Is.EqualTo("AccentGreen"));
        Assert.That(text, Is.EqualTo("connected"));
    }

    [Test]
    public void KeepAliveLossIsSurfacedWhenActivityStops()
    {
        (string key, string text) = ConnectionPresentation.Indicator(
            ConnectionPhase.Connected, connected: true, keepAliveLost: true, active: false);

        Assert.That(key, Is.EqualTo("AccentYellowLight"));
        Assert.That(text, Is.EqualTo("keep-alive lost"));
    }

    [Test]
    public void DisconnectedIndicatorIsNeutral()
    {
        (string key, string text) = ConnectionPresentation.Indicator(
            ConnectionPhase.Disconnected, connected: false, keepAliveLost: false, active: false);

        Assert.That(key, Is.EqualTo("TextDim"));
        Assert.That(text, Is.EqualTo("disconnected"));
    }
}
