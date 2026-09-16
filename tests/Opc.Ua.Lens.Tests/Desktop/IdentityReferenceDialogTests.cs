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
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Identity;
using UaLens.Connection;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class IdentityReferenceDialogTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public Task AcceptedReferencePreservesTheExactPolicyAndNeverAcquiresCredentials(bool certificate, bool restored)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            using ConnectionIdentityConfiguration identities = Configuration();
            EndpointDescription endpoint = Endpoint(certificate);
            var provider = new Mock<IClientIdentityProvider>(MockBehavior.Strict);
            provider.SetupGet(value => value.SupportedTokenTypes).Returns(s_tokens);
            provider.SetupGet(value => value.SupportedIssuedTokenProfileUris).Returns(s_profiles);
            provider.Setup(value => value.CanSatisfyAsync(endpoint.UserIdentityTokens[0],
                It.IsAny<IdentitySelectionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CanSatisfyResult.Yes);
            ConnectionProfile expected = Profile(endpoint, certificate);
            ConnectionProfile? resolved = null;
            ApplicationConfiguration application = await context.Connection.GetConfigAsync().ConfigureAwait(true);
            await using var dialog = new IdentityReferenceDialog(
                endpoint, endpoint.UserIdentityTokens[0], SubscriptionEngineKind.ChannelV2,
                identities, application, restored ? expected : null, (profile, _) =>
                {
                    resolved = profile;
                    return ValueTask.FromResult(provider.Object);
                });
            Task<ConnectionSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner, CancellationToken.None);
            ComboBox sources = DesktopInteraction.Control<ComboBox>(dialog, "IdentitySourceBox");
            Assert.That(sources.Items, Has.Count.EqualTo(1));
            if (certificate)
            {
                Assert.That(sources.SelectedItem, Is.TypeOf<ConfiguredCertificateSource>());
                Assert.That(((ConfiguredCertificateSource)sources.SelectedItem!).Purpose,
                    Is.EqualTo(CryptoPurpose.UserIdentityKey));
                DesktopInteraction.Control<TextBox>(dialog, "CertificateSubjectBox").Text = "  CN=Operator  ";
            }
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "UseIdentityButton"));
            ConnectionSelection? result = await prompt.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
            Assert.That(result, Is.Not.Null);
            await using (result!.ConfigureAwait(true))
            {
                Assert.That(resolved, Is.EqualTo(expected));
                Assert.That(result.Profile, Is.EqualTo(expected));
                Assert.That(result.Endpoint.IsEqual(endpoint), Is.True);
            }
            provider.Verify(value => value.CanSatisfyAsync(endpoint.UserIdentityTokens[0],
                It.IsAny<IdentitySelectionContext>(), It.IsAny<CancellationToken>()), Times.Once);
            provider.Verify(value => value.GetIdentityAsync(
                It.IsAny<UserTokenPolicy>(), It.IsAny<IdentitySelectionContext>(), It.IsAny<CancellationToken>()),
                Times.Never);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task MissingProviderDisablesAcceptanceWithoutInventingAnAlternateIdentity(bool certificate)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            using var identities = new ConnectionIdentityConfiguration();
            EndpointDescription endpoint = Endpoint(certificate);
            await using var dialog = new IdentityReferenceDialog(
                endpoint, endpoint.UserIdentityTokens[0], SubscriptionEngineKind.ChannelV2, identities,
                await context.Connection.GetConfigAsync().ConfigureAwait(true));
            Task<ConnectionSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner, CancellationToken.None);
            Assert.That(DesktopInteraction.Control<Button>(dialog, "UseIdentityButton").IsEnabled, Is.False);
            Assert.That(DesktopInteraction.Control<ComboBox>(dialog, "IdentitySourceBox").Items, Is.Empty);
            Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "IdentityStatus").Text,
                Does.StartWith(certificate ? "No user-certificate provider" : "No access-token provider"));
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
            Assert.That(await prompt.ConfigureAwait(true), Is.Null);
        });
    }

    [TestCase("denied")]
    [TestCase("invalid")]
    [TestCase("missing-selection")]
    [TestCase("changed-reference")]
    public Task InvalidOrRejectedCertificateSelectionStaysOpenAndReturnsNoCredentials(string failure)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            using ConnectionIdentityConfiguration identities = Configuration();
            EndpointDescription endpoint = Endpoint(true);
            int calls = 0;
            await using var dialog = new IdentityReferenceDialog(
                endpoint, endpoint.UserIdentityTokens[0], SubscriptionEngineKind.ChannelV2,
                identities, await context.Connection.GetConfigAsync().ConfigureAwait(true),
                failure == "changed-reference" ? Profile(endpoint, true) : null, (_, _) =>
                {
                    calls++;
                    throw new UnauthorizedAccessException("provider implementation detail");
                });
            Task<ConnectionSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner, CancellationToken.None);
            DesktopInteraction.Control<TextBox>(dialog, "CertificateSubjectBox").Text =
                failure == "changed-reference" ? "CN=Other" : "CN=Operator";
            if (failure == "invalid")
            {
                DesktopInteraction.Control<TextBox>(dialog, "CertificateThumbprintBox").Text = "not-hex";
            }
            if (failure == "missing-selection")
            {
                DesktopInteraction.Control<ComboBox>(dialog, "PasswordSourceBox").SelectedIndex = -1;
            }
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "UseIdentityButton"));
            Assert.That(dialog.IsVisible, Is.True);
            Assert.That(prompt.IsCompleted, Is.False);
            Assert.That(DesktopInteraction.Control<Button>(dialog, "UseIdentityButton").IsEnabled, Is.True);
            string? status = DesktopInteraction.Control<TextBlock>(dialog, "IdentityStatus").Text;
            Assert.That(status, Does.Contain(failure switch
            {
                "denied" => "denied access",
                "invalid" => "hexadecimal",
                "missing-selection" => "Select a configured certificate",
                _ => "saved reference must be reacquired exactly"
            }));
            Assert.That(status, Does.Not.Contain("provider implementation detail"));
            Assert.That(calls, Is.EqualTo(failure == "denied" ? 1 : 0));
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
            Assert.That(await prompt.ConfigureAwait(true), Is.Null);
        });
    }

    [Test]
    public Task CancellationDrainsAnInFlightIdentityCheckAndDoesNotReturnASelection()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            using ConnectionIdentityConfiguration identities = Configuration();
            EndpointDescription endpoint = Endpoint(false);
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new TaskCompletionSource<IClientIdentityProvider>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await using var dialog = new IdentityReferenceDialog(
                endpoint, endpoint.UserIdentityTokens[0], SubscriptionEngineKind.ChannelV2, identities,
                await context.Connection.GetConfigAsync().ConfigureAwait(true), resolver: (_, token) =>
                {
                    entered.TrySetResult();
                    return new ValueTask<IClientIdentityProvider>(pending.Task.WaitAsync(token));
                });
            Task<ConnectionSelection?> prompt = dialog.PromptAsync(DesktopInteraction.Owner, cancellation.Token);
            DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "UseIdentityButton"));
            await entered.Task.ConfigureAwait(true);
            Assert.That(DesktopInteraction.Control<Button>(dialog, "UseIdentityButton").IsEnabled, Is.False);
            await cancellation.CancelAsync().ConfigureAwait(true);
            await Assert.ThatAsync(() => prompt.WaitAsync(TimeSpan.FromSeconds(10)),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(true);
            Assert.That(dialog.IsVisible, Is.False);
            Assert.That(pending.Task.IsCompleted, Is.False);
        });
    }

    private static ConnectionIdentityConfiguration Configuration()
    {
        var certificates = new Mock<ICertificateProvider>(MockBehavior.Strict);
        var password = new ConfiguredCertificatePasswordSource(
            "pin", "Configured password", new Mock<ICertificatePasswordProvider>().Object);
        var access = new Mock<IAccessTokenProvider>(MockBehavior.Strict);
        access.SetupGet(provider => provider.AuthorityUri).Returns(kAuthority);
        return new ConnectionIdentityConfiguration(
        [
            new("user", "User key", new CertificateIdentifier(), certificates.Object, [password]),
            new("application", "Application key", new CertificateIdentifier(), certificates.Object,
                [password], CryptoPurpose.ApplicationInstanceKey)
        ], [new ConfiguredAccessTokenSource("authority", "Authority", access.Object)]);
    }

    private static EndpointDescription Endpoint(bool certificate)
    {
        var policy = new UserTokenPolicy(certificate ? UserTokenType.Certificate : UserTokenType.IssuedToken)
        {
            PolicyId = certificate ? "certificate" : "jwt",
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            IssuedTokenType = certificate ? null : Profiles.JwtUserToken,
            IssuerEndpointUrl = certificate ? null :
                "{\"authorityUri\":\"" + kAuthority + "\",\"ua:resourceUri\":\"urn:fixture:identity\"}"
        };
        return new EndpointDescription("opc.tcp://identity.example.test:4840")
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            TransportProfileUri = Profiles.UaTcpTransport,
            Server = new ApplicationDescription { ApplicationUri = "urn:fixture:identity" },
            UserIdentityTokens = [policy]
        };
    }

    private static ConnectionProfile Profile(EndpointDescription endpoint, bool certificate)
    {
        return ConnectionProfile.Create(endpoint, endpoint.UserIdentityTokens[0], SubscriptionEngineKind.ChannelV2,
            certificateIdentity: certificate ? new CertificateIdentityReference
            {
                SourceId = "user",
                PasswordSourceId = "pin",
                SubjectName = "CN=Operator"
            } : null,
            issuedIdentity: certificate ? null : new IssuedIdentityReference
            {
                ProviderId = "authority",
                AuthorityUri = kAuthority
            });
    }

    private const string kAuthority = "https://authority.example.test";
    private static readonly UserTokenType[] s_tokens = [UserTokenType.Certificate, UserTokenType.IssuedToken];
    private static readonly string[] s_profiles = [Profiles.JwtUserToken];
}
