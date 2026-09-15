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
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using UaLens.Connection;

namespace UaLens.Tests.Connection;

[TestFixture]
public sealed class ConnectionTransportReadinessTests
{
    [Test]
    public async Task ChoosingConfiguredReverseSetupDoesNotAcquireAKeyOrConstructAListener()
    {
        var certificates = new Mock<ICertificateProvider>(MockBehavior.Strict);
        var passwords = new Mock<ICertificatePasswordProvider>(MockBehavior.Strict);
        ConfiguredApplicationIdentity application = ApplicationIdentity(certificates.Object, passwords.Object);
        var configurations = new ConnectionConfigurationCatalog([application]);
        var factory = new Mock<IReverseConnectionRuntimeFactory>(MockBehavior.Strict);
        var owner = new ReverseConnectionService(factory.Object);
        await using (owner.ConfigureAwait(false))
        {
            ReverseConnectionProfile reverse = Reverse();
            var setup = new ConnectionSetupSelection(reverse.EndpointUrl, reverse, application.Id);

            ConnectionTransportReadiness readiness = ConnectionTransportReadiness.Assess(
                new ConnectionTransportCatalog(), configurations, setup, owner.Snapshot);

            Assert.That(readiness.CanUseSetup, Is.True, "Safe intent can be selected without listening.");
            Assert.That(readiness.CanStartListener, Is.True);
            Assert.That(readiness.CanDiscover, Is.False);
            Assert.That(Check(readiness, "Application identity").State,
                Is.EqualTo(ConnectionTransportCheckState.Ready));
            Assert.That(Check(readiness, "Advertised endpoint").State,
                Is.EqualTo(ConnectionTransportCheckState.Pending));
            Assert.That(Check(readiness, "External prerequisites").State,
                Is.EqualTo(ConnectionTransportCheckState.Pending));
            Assert.That(owner.Snapshot.Phase, Is.EqualTo(ReverseConnectionPhase.Stopped));
            factory.VerifyNoOtherCalls();
            certificates.VerifyNoOtherCalls();
            passwords.VerifyNoOtherCalls();
        }
    }

    [TestCase("opc.tcp")]
    [TestCase("https")]
    [TestCase("wss")]
    [TestCase("opc.custom")]
    public void RegisteredForwardSetupDoesNotRequireALocalListenerOrInventEndpointReadiness(string scheme)
    {
        var bindings = new Mock<ITransportBindingRegistry>();
        bindings.Setup(value => value.HasChannelFactory(scheme)).Returns(true);
        var setup = new ConnectionSetupSelection($"{scheme}://server.example.test:4840/Factory");

        ConnectionTransportReadiness readiness = ConnectionTransportReadiness.Assess(
            new ConnectionTransportCatalog(bindings.Object), new ConnectionConfigurationCatalog(), setup, Stopped());

        Assert.That(readiness.CanUseSetup, Is.True);
        Assert.That(readiness.CanDiscover, Is.True);
        Assert.That(readiness.CanStartListener, Is.False);
        Assert.That(Check(readiness, "Transport").Detail, Does.StartWith($"{scheme}: forward registered"));
        Assert.That(Check(readiness, "Advertised endpoint").State, Is.EqualTo(ConnectionTransportCheckState.Pending));
        bindings.Verify(value => value.CreateChannel(It.IsAny<string>(), It.IsAny<ITelemetryContext>()), Times.Never);
        bindings.Verify(value => value.CreateListener(It.IsAny<string>(), It.IsAny<ITelemetryContext>()), Times.Never);
    }

    [TestCase("removed-application")]
    [TestCase("")]
    public void UnavailableSavedApplicationIdentityBlocksSetupInsteadOfFallingBackToDefault(string applicationId)
    {
        ConnectionTransportReadiness readiness = Assess(
            new ConnectionSetupSelection(Reverse().EndpointUrl, ApplicationIdentityId: applicationId));

        Assert.That(readiness.CanUseSetup, Is.False);
        Assert.That(readiness.CanDiscover, Is.False);
        Assert.That(Check(readiness, "Application identity").State, Is.EqualTo(ConnectionTransportCheckState.Blocked));
        Assert.That(Check(readiness, "Application identity").Detail, Is.Not.Empty);
    }

    [TestCase(null)]
    [TestCase("missing-listener-tls")]
    public void ReverseWssRequiresAnAvailableExplicitListenerTlsRegistration(string? tls)
    {
        ReverseConnectionProfile reverse = Reverse() with
        {
            EndpointUrl = "wss://server.example.test:4840/Factory",
            ListenerUrl = "wss://localhost:4841/client",
            TlsConfigurationId = tls
        };
        var bindings = new Mock<ITransportBindingRegistry>();
        bindings.Setup(value => value.HasChannelFactory("wss")).Returns(true);
        bindings.Setup(value => value.HasListenerFactory("wss")).Returns(true);

        ConnectionTransportReadiness readiness = ConnectionTransportReadiness.Assess(
            new ConnectionTransportCatalog(bindings.Object), new ConnectionConfigurationCatalog(),
            new ConnectionSetupSelection(reverse.EndpointUrl, reverse), Stopped());

        Assert.That(readiness.CanUseSetup, Is.False);
        Assert.That(readiness.CanStartListener, Is.False);
        Assert.That(Check(readiness, "Listener TLS").State, Is.EqualTo(ConnectionTransportCheckState.Blocked));
        bindings.Verify(value => value.CreateListener(It.IsAny<string>(), It.IsAny<ITelemetryContext>()), Times.Never);
    }

    [Test]
    public void ConfiguredReverseWssIsReadyForExplicitStartWithoutAcquiringItsTlsConfiguration()
    {
        ReverseConnectionProfile reverse = Reverse() with
        {
            EndpointUrl = "opc.wss://server.example.test:4840/Factory",
            ListenerUrl = "opc.wss://localhost:4841/client",
            TlsConfigurationId = "listener-tls"
        };
        var bindings = new Mock<ITransportBindingRegistry>();
        bindings.Setup(value => value.HasChannelFactory("opc.wss")).Returns(true);
        bindings.Setup(value => value.HasListenerFactory("opc.wss")).Returns(true);
        var configurations = new ConnectionConfigurationCatalog(reverseTlsSources:
        [
            new ConfiguredReverseTlsSource("listener-tls", "Listener TLS",
                _ => throw new InvalidOperationException("Readiness must not acquire listener certificates."))
        ]);

        ConnectionTransportReadiness readiness = ConnectionTransportReadiness.Assess(
            new ConnectionTransportCatalog(bindings.Object), configurations,
            new ConnectionSetupSelection(reverse.EndpointUrl, reverse), Stopped());

        Assert.That(readiness.CanUseSetup, Is.True);
        Assert.That(readiness.CanStartListener, Is.True);
        Assert.That(readiness.CanDiscover, Is.False);
        Assert.That(Check(readiness, "Listener TLS").Detail, Does.Contain("checked at explicit Start"));
        Assert.That(Check(readiness, "External prerequisites").Detail, Does.Contain("does not provision"));
    }

    [TestCase("Stopped", true, false)]
    [TestCase("Starting", false, false)]
    [TestCase("Listening", false, true)]
    [TestCase("Waiting", false, false)]
    [TestCase("Stopping", false, false)]
    [TestCase("Failed", false, false)]
    public void ReverseActionsReflectTheOwnersActualPhase(string phase, bool start, bool discover)
    {
        ReverseConnectionProfile reverse = Reverse();
        var snapshot = new ReverseConnectionSnapshot(Enum.Parse<ReverseConnectionPhase>(phase), reverse);

        ConnectionTransportReadiness readiness = Assess(
            new ConnectionSetupSelection(reverse.EndpointUrl, reverse), snapshot);

        Assert.That(readiness.CanUseSetup, Is.True);
        Assert.That(readiness.CanStartListener, Is.EqualTo(start));
        Assert.That(readiness.CanDiscover, Is.EqualTo(discover));
    }

    [TestCase("peer")]
    [TestCase("listener")]
    [TestCase("wait")]
    [TestCase("hold")]
    public void DifferentRunningReverseProfileCannotEnableDiscovery(string change)
    {
        ReverseConnectionProfile reverse = Reverse();
        ReverseConnectionProfile running = change switch
        {
            "peer" => reverse with { ServerUri = "urn:other-server" },
            "listener" => reverse with { ListenerUrl = "opc.tcp://localhost:4842/client" },
            "wait" => reverse with { WaitTimeoutSeconds = 21 },
            _ => reverse with { HoldTimeSeconds = 16 }
        };

        ConnectionTransportReadiness readiness = Assess(new ConnectionSetupSelection(reverse.EndpointUrl, reverse),
            new ReverseConnectionSnapshot(ReverseConnectionPhase.Listening, running));

        Assert.That(readiness.CanUseSetup, Is.True);
        Assert.That(readiness.CanStartListener, Is.False);
        Assert.That(readiness.CanDiscover, Is.False);
        Assert.That(Check(readiness, "Listener").Detail, Does.Contain("Stop a different running listener"));
    }

    [TestCase("endpoint")]
    [TestCase("server")]
    [TestCase("mode")]
    [TestCase("policy")]
    [TestCase("transport")]
    [TestCase("token")]
    [TestCase("anonymous")]
    public void ExplicitProfileRejectsAdvertisedPeerSecurityAndIdentityDowngrade(string change)
    {
        EndpointDescription selected = Endpoint();
        ConnectionProfile profile = Profile(selected);
        EndpointDescription advertised = Endpoint();
        switch (change)
        {
            case "endpoint":
                advertised.EndpointUrl += "/other";
                break;
            case "server":
                advertised.Server.ApplicationUri = "urn:other-server";
                break;
            case "mode":
                advertised.SecurityMode = MessageSecurityMode.None;
                advertised.SecurityPolicyUri = SecurityPolicies.None;
                break;
            case "policy":
                advertised.SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss;
                break;
            case "transport":
                advertised.TransportProfileUri = "urn:changed-transport-profile";
                break;
            case "token":
                advertised.UserIdentityTokens[0].PolicyId = "different-policy";
                break;
            case "anonymous":
                advertised.UserIdentityTokens =
                [
                    new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "operator" }
                ];
                break;
        }

        ConnectionTransportReadiness readiness = Assess(
            new ConnectionSetupSelection(profile.EndpointUrl), selectedProfile: profile,
            endpoints: [advertised], discovered: true);

        Assert.That(readiness.CanUseSetup, Is.False);
        Assert.That(readiness.CanDiscover, Is.False);
        Assert.That(Check(readiness, "Advertised endpoint").State, Is.EqualTo(ConnectionTransportCheckState.Blocked));
        Assert.That(Check(readiness, "Advertised endpoint").Detail, Does.Contain("no fallback"));
    }

    [Test]
    public void ExactAdvertisedProfileStillLeavesPeerTrustAndPrivateKeyAccessPending()
    {
        EndpointDescription endpoint = Endpoint();
        ConnectionProfile profile = Profile(endpoint);

        ConnectionTransportReadiness readiness = Assess(
            new ConnectionSetupSelection(profile.EndpointUrl), selectedProfile: profile,
            endpoints: [endpoint], discovered: true);

        Assert.That(readiness.CanUseSetup, Is.True);
        Assert.That(Check(readiness, "Advertised endpoint").State, Is.EqualTo(ConnectionTransportCheckState.Ready));
        Assert.That(Check(readiness, "Advertised endpoint").Detail, Does.Contain("not certificate-trust validation"));
        Assert.That(Check(readiness, "External prerequisites").State,
            Is.EqualTo(ConnectionTransportCheckState.Pending));
    }

    [Test]
    public void LaterMatchingDescriptionCanSupplyTheExactSelectedUserPolicy()
    {
        EndpointDescription endpoint = Endpoint();
        ConnectionProfile profile = Profile(endpoint);
        EndpointDescription withoutPolicy = Endpoint();
        withoutPolicy.UserIdentityTokens = [];

        ConnectionTransportReadiness readiness = Assess(
            new ConnectionSetupSelection(profile.EndpointUrl), selectedProfile: profile,
            endpoints: [withoutPolicy, endpoint], discovered: true);

        Assert.That(readiness.CanUseSetup, Is.True);
        Assert.That(Check(readiness, "Advertised endpoint").State, Is.EqualTo(ConnectionTransportCheckState.Ready));
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void MissingDiscoveryIsPendingButCompletedEmptyDiscoveryCannotConfirmAPinnedProfile(
        bool discovered,
        bool usable)
    {
        ConnectionProfile profile = Profile(Endpoint());

        ConnectionTransportReadiness readiness = Assess(
            new ConnectionSetupSelection(profile.EndpointUrl), selectedProfile: profile,
            endpoints: [], discovered: discovered);

        Assert.That(readiness.CanUseSetup, Is.EqualTo(usable));
        Assert.That(Check(readiness, "Advertised endpoint").State, Is.EqualTo(discovered
            ? ConnectionTransportCheckState.Blocked
            : ConnectionTransportCheckState.Pending));
    }

    [TestCase("endpoint")]
    [TestCase("direction")]
    [TestCase("application")]
    public void PinnedSetupCannotChangeEndpointDirectionOrApplicationIdentity(string change)
    {
        ConnectionProfile profile = Profile(Endpoint());
        var initial = new ConnectionSetupSelection(profile.EndpointUrl);
        ConnectionSetupSelection setup = change switch
        {
            "endpoint" => initial with { EndpointUrl = profile.EndpointUrl + "/other" },
            "direction" => initial with { ReverseConnection = Reverse() },
            _ => initial with { ApplicationIdentityId = "other-application" }
        };

        ConnectionTransportReadiness readiness = Assess(setup, selectedProfile: profile);

        Assert.That(readiness.CanUseSetup, Is.False);
        Assert.That(Check(readiness, "Selected profile").State, Is.EqualTo(ConnectionTransportCheckState.Blocked));
        Assert.That(Check(readiness, "Selected profile").Detail, Does.Contain("explicitly selected"));
    }

    [TestCase("password")]
    [TestCase("certificate-source")]
    [TestCase("crypto-registry")]
    public void ApplicationRegistrationReadinessChecksItsConfiguredReferencesWithoutKeyAccess(string missing)
    {
        var certificates = new Mock<ICertificateProvider>(MockBehavior.Strict);
        var passwords = new Mock<ICertificatePasswordProvider>(MockBehavior.Strict);
        ConfiguredApplicationIdentity application = ApplicationIdentity(certificates.Object, passwords.Object, missing);
        var configurations = new ConnectionConfigurationCatalog([application]);

        ConnectionTransportReadiness readiness = ConnectionTransportReadiness.Assess(
            new ConnectionTransportCatalog(), configurations,
            new ConnectionSetupSelection(Reverse().EndpointUrl, ApplicationIdentityId: application.Id), Stopped());

        Assert.That(readiness.CanUseSetup, Is.False);
        Assert.That(Check(readiness, "Application identity").State, Is.EqualTo(ConnectionTransportCheckState.Blocked));
        certificates.VerifyNoOtherCalls();
        passwords.VerifyNoOtherCalls();
    }

    private static ConnectionTransportReadiness Assess(
        ConnectionSetupSelection setup,
        ReverseConnectionSnapshot? listener = null,
        ConnectionProfile? selectedProfile = null,
        ArrayOf<EndpointDescription> endpoints = default,
        bool discovered = false)
    {
        return ConnectionTransportReadiness.Assess(new ConnectionTransportCatalog(),
            new ConnectionConfigurationCatalog(), setup, listener ?? Stopped(), selectedProfile, endpoints, discovered);
    }

    private static ConnectionTransportCheck Check(ConnectionTransportReadiness readiness, string name)
    {
        return readiness.Checks.ToList().Single(value => value.Name == name);
    }

    private static ConfiguredApplicationIdentity ApplicationIdentity(
        ICertificateProvider certificates,
        ICertificatePasswordProvider passwords,
        string? missing = null)
    {
        var source = new ConfiguredCertificateSource("application-source", "Application source",
            new CertificateIdentifier(), certificates,
            [new ConfiguredCertificatePasswordSource("application-access", "Configured access", passwords)],
            CryptoPurpose.ApplicationInstanceKey,
            cryptoProviderName: missing == "crypto-registry" ? "configured-device" : null);
        var reference = new CertificateIdentityReference
        {
            SourceId = missing == "certificate-source" ? "unavailable-source" : source.Id,
            PasswordSourceId = missing == "password" ? "unavailable-access" : "application-access",
            SubjectName = "CN=Configured application"
        };
        return new ConfiguredApplicationIdentity("application", "Selected application", source, reference,
            (_, _, _) => throw new InvalidOperationException("Readiness must not acquire application keys."));
    }

    private static ReverseConnectionSnapshot Stopped()
    {
        return new ReverseConnectionSnapshot(ReverseConnectionPhase.Stopped, null);
    }

    private static ReverseConnectionProfile Reverse()
    {
        return new ReverseConnectionProfile
        {
            ListenerUrl = "opc.tcp://localhost:4841/client",
            EndpointUrl = "opc.tcp://server.example.test:4840/Factory",
            ServerUri = "urn:expected-server"
        };
    }

    private static EndpointDescription Endpoint()
    {
        return new EndpointDescription(Reverse().EndpointUrl)
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            Server = new ApplicationDescription { ApplicationUri = "urn:expected-server" },
            UserIdentityTokens =
            [
                new UserTokenPolicy(UserTokenType.UserName)
                {
                    PolicyId = "operator", SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                }
            ]
        };
    }

    private static ConnectionProfile Profile(EndpointDescription endpoint)
    {
        return ConnectionProfile.Create(endpoint, endpoint.UserIdentityTokens[0],
            SubscriptionEngineKind.ChannelV2, "operator");
    }
}
