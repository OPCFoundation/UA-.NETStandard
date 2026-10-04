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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Views;

namespace UaLens.Tests.Desktop
{
    [TestFixture]
    [Platform("Win,Linux")]
    [NonParallelizable]
    public sealed class IdentityEnrollmentDialogTests
    {
        [Test]
        public Task PreparationRequestAndAdoptionRequireSeparateVisibleConsent()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var fixture = new DialogFixture();
                IdentityEnrollmentDialog dialog = fixture.Create();
                await using (dialog.ConfigureAwait(true))
                {
                    fixture.Enrollment.VerifyNoOtherCalls();
                    Task<CertificateIdentityReference?> prompt = dialog.PromptAsync(
                        DesktopInteraction.Owner, CancellationToken.None);
                    Assert.That(Button(dialog, "PrepareEnrollmentButton").IsEnabled, Is.True);
                    Assert.That(Button(dialog, "RequestEnrollmentButton").IsEnabled, Is.False);
                    Assert.That(Button(dialog, "AdoptEnrollmentButton").IsEnabled, Is.False);
                    DesktopInteraction.Click(Button(dialog, "PrepareEnrollmentButton"));
                    await PumpAsync().ConfigureAwait(true);
                    Assert.That(Status(dialog), Does.Contain("urn:fixture:application"));
                    Assert.That(Status(dialog), Does.Contain("group").And.Contain("type"));
                    fixture.Enrollment.Verify(enrollment => enrollment.PrepareAsync(
                        fixture.Reference, fixture.Policy, It.IsAny<CancellationToken>()), Times.Once);
                    fixture.VerifyNoRequestOrAdoption();

                    CheckBox consent = DesktopInteraction.Control<CheckBox>(dialog, "EnrollmentConsent");
                    Assert.That(Button(dialog, "RequestEnrollmentButton").IsEnabled, Is.False);
                    consent.IsChecked = true;
                    DesktopInteraction.Click(Button(dialog, "RequestEnrollmentButton"));
                    await PumpAsync().ConfigureAwait(true);
                    Assert.That(consent.IsChecked, Is.False);
                    Assert.That(Button(dialog, "RequestEnrollmentButton").IsEnabled, Is.False);
                    Assert.That(Button(dialog, "AdoptEnrollmentButton").IsEnabled, Is.False);
                    fixture.Enrollment.Verify(enrollment => enrollment.RequestAsync(
                        fixture.Review, It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()), Times.Once);
                    fixture.Enrollment.Verify(enrollment => enrollment.AdoptAsync(
                        It.IsAny<IdentityEnrollmentResult>(), It.IsAny<CancellationToken>()), Times.Never);

                    consent.IsChecked = true;
                    DesktopInteraction.Click(Button(dialog, "AdoptEnrollmentButton"));
                    CertificateIdentityReference? result = await prompt.WaitAsync(sWait).ConfigureAwait(true);

                    Assert.That(result, Is.EqualTo(fixture.Adopted));
                    Assert.That(dialog.IsVisible, Is.False);
                    fixture.Enrollment.Verify(enrollment => enrollment.AdoptAsync(
                        fixture.Result, It.IsAny<CancellationToken>()), Times.Once);
                }
                fixture.Enrollment.Verify(enrollment => enrollment.DisposeAsync(), Times.Once);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public Task ClosingDrainsLateProviderWorkWithoutReturningAnIdentity(bool adoption)
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var fixture = new DialogFixture();
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var request = new TaskCompletionSource<IdentityEnrollmentResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var adopt = new TaskCompletionSource<CertificateIdentityReference>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationToken operationToken = default;
                if (adoption)
                {
                    fixture.Enrollment.Setup(enrollment => enrollment.AdoptAsync(
                        fixture.Result, It.IsAny<CancellationToken>())).Returns((IdentityEnrollmentResult _,
                            CancellationToken token) =>
                        {
                            operationToken = token;
                            entered.SetResult();
                            return adopt.Task;
                        });
                }
                else
                {
                    fixture.Enrollment.Setup(enrollment => enrollment.RequestAsync(
                        fixture.Review, It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
                        .Returns((IdentityEnrollmentReview _, IProgress<string>? _, CancellationToken token) =>
                        {
                            operationToken = token;
                            entered.SetResult();
                            return request.Task;
                        });
                }
                IdentityEnrollmentDialog dialog = fixture.Create();
                await using (dialog.ConfigureAwait(true))
                {
                    Task<CertificateIdentityReference?> prompt = dialog.PromptAsync(
                        DesktopInteraction.Owner, CancellationToken.None);
                    DesktopInteraction.Click(Button(dialog, "PrepareEnrollmentButton"));
                    await PumpAsync().ConfigureAwait(true);
                    DesktopInteraction.Control<CheckBox>(dialog, "EnrollmentConsent").IsChecked = true;
                    DesktopInteraction.Click(Button(dialog, "RequestEnrollmentButton"));
                    if (adoption)
                    {
                        await PumpAsync().ConfigureAwait(true);
                        DesktopInteraction.Control<CheckBox>(dialog, "EnrollmentConsent").IsChecked = true;
                        DesktopInteraction.Click(Button(dialog, "AdoptEnrollmentButton"));
                    }
                    await entered.Task.WaitAsync(sWait).ConfigureAwait(true);
                    DesktopInteraction.Click(Button(dialog, "CancelEnrollmentButton"));
                    await PumpAsync().ConfigureAwait(true);
                    Assert.That(operationToken.IsCancellationRequested, Is.True);
                    Assert.That(prompt.IsCompleted, Is.False);
                    fixture.Enrollment.Verify(enrollment => enrollment.DisposeAsync(), Times.Never);
                    if (adoption)
                    {
                        adopt.SetResult(fixture.Adopted);
                    }
                    else
                    {
                        request.SetResult(fixture.Result);
                    }

                    CertificateIdentityReference? result = await prompt.WaitAsync(sWait).ConfigureAwait(true);

                    Assert.That(result, Is.Null);
                    Assert.That(dialog.IsVisible, Is.False);
                    if (!adoption)
                    {
                        fixture.Enrollment.Verify(enrollment => enrollment.AdoptAsync(
                            It.IsAny<IdentityEnrollmentResult>(), It.IsAny<CancellationToken>()), Times.Never);
                    }
                }
                fixture.Enrollment.Verify(enrollment => enrollment.DisposeAsync(), Times.Once);
            });
        }

        [Test]
        public Task ProviderFailureDoesNotRevealItsTextOrLeaveAdoptionArmed()
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var fixture = new DialogFixture();
                string privateDetail = Guid.NewGuid().ToString("N");
                fixture.Enrollment.Setup(enrollment => enrollment.RequestAsync(
                    fixture.Review, It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.FromException<IdentityEnrollmentResult>(new CryptographicException(privateDetail)));
                IdentityEnrollmentDialog dialog = fixture.Create();
                await using (dialog.ConfigureAwait(true))
                {
                    Task<CertificateIdentityReference?> prompt = dialog.PromptAsync(
                        DesktopInteraction.Owner, CancellationToken.None);
                    DesktopInteraction.Click(Button(dialog, "PrepareEnrollmentButton"));
                    await PumpAsync().ConfigureAwait(true);
                    DesktopInteraction.Control<CheckBox>(dialog, "EnrollmentConsent").IsChecked = true;
                    DesktopInteraction.Click(Button(dialog, "RequestEnrollmentButton"));
                    await PumpAsync().ConfigureAwait(true);

                    Assert.That(Status(dialog), Does.Contain("CryptographicException").And.Not.Contain(privateDetail));
                    Assert.That(Button(dialog, "AdoptEnrollmentButton").IsEnabled, Is.False);
                    Assert.That(Button(dialog, "RequestEnrollmentButton").IsEnabled, Is.False);
                    Assert.That(Button(dialog, "PrepareEnrollmentButton").IsEnabled, Is.True);
                    fixture.Enrollment.Verify(enrollment => enrollment.AdoptAsync(
                        It.IsAny<IdentityEnrollmentResult>(), It.IsAny<CancellationToken>()), Times.Never);
                    DesktopInteraction.Click(Button(dialog, "CancelEnrollmentButton"));
                    Assert.That(await prompt.WaitAsync(sWait).ConfigureAwait(true), Is.Null);
                }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public Task ParentSetupIsFreshOnlyAndOwnsANewEnrollmentForEveryDialog(bool restored)
        {
            return AvaloniaDesktopTestHost.RunAsync(async () =>
            {
                var desktop = new DesktopConnectionContext();
                await using (desktop.ConfigureAwait(true))
                {
                    var certificates = new Mock<ICertificateProvider>(MockBehavior.Strict);
                    var created = new List<Mock<IIdentityEnrollment>>();
                    var source = new ConfiguredCertificateSource("user", "User identity",
                        new CertificateIdentifier(), certificates.Object,
                        [new ConfiguredCertificatePasswordSource("password", "Configured password",
                            new Mock<ICertificatePasswordProvider>(MockBehavior.Strict).Object)],
                        createEnrollment: _ =>
                        {
                            var enrollment = new Mock<IIdentityEnrollment>(MockBehavior.Strict);
                            enrollment.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
                            created.Add(enrollment);
                            return enrollment.Object;
                        });
                    using var identities = new ConnectionIdentityConfiguration([source]);
                    var policy = new UserTokenPolicy(UserTokenType.Certificate)
                    {
                        PolicyId = "certificate",
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    };
                    var endpoint = new EndpointDescription("opc.tcp://identity.example.test:4840")
                    {
                        SecurityMode = MessageSecurityMode.SignAndEncrypt,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                        Server = new ApplicationDescription { ApplicationUri = "urn:fixture:identity" },
                        UserIdentityTokens = [policy]
                    };
                    var profile = ConnectionProfile.Create(
                        endpoint, policy, SubscriptionEngineKind.ChannelV2, certificateIdentity: new()
                        {
                            SourceId = "user",
                            PasswordSourceId = "password",
                            SubjectName = "CN=Fixture user"
                        });
                    var parent = new IdentityReferenceDialog(
                        endpoint, policy, SubscriptionEngineKind.ChannelV2, identities,
                        await desktop.Connection.GetConfigAsync().ConfigureAwait(true), restored ? profile : null);
                    await using (parent.ConfigureAwait(true))
                    {
                        Button setup = DesktopInteraction.Control<Button>(parent, "ConfigureIdentityButton");
                        Assert.That(setup.IsEnabled, Is.False);
                        Assert.That(created, Is.Empty);
                        Task<ConnectionSelection?> prompt = parent.PromptAsync(
                            DesktopInteraction.Owner, CancellationToken.None);
                        Assert.That(setup.IsEnabled, Is.EqualTo(!restored));
                        if (!restored)
                        {
                            DesktopInteraction.Control<TextBox>(parent, "CertificateSubjectBox").Text =
                                "CN=Fixture user";
                            for (int index = 0; index < 2; index++)
                            {
                                IdentityEnrollmentDialog child =
                                    await DesktopInteraction.OpenedAsync<IdentityEnrollmentDialog>(
                                        () => DesktopInteraction.Click(setup)).ConfigureAwait(true);
                                Assert.That(child.Owner, Is.SameAs(parent));
                                Assert.That(created, Has.Count.EqualTo(index + 1));
                                Assert.That(DesktopInteraction.Control<Button>(parent, "UseIdentityButton").IsEnabled,
                                    Is.False);
                                Assert.That(DesktopInteraction.Control<ComboBox>(parent, "IdentitySourceBox").IsEnabled,
                                    Is.False);
                                DesktopInteraction.Click(Button(child, "CancelEnrollmentButton"));
                                await PumpAsync().ConfigureAwait(true);
                                Assert.That(setup.IsEnabled, Is.True);
                                created[index].Verify(value => value.DisposeAsync(), Times.Once);
                                created[index].VerifyNoOtherCalls();
                            }
                        }
                        parent.Close();
                        Assert.That(await prompt.WaitAsync(sWait).ConfigureAwait(true), Is.Null);
                    }
                    Assert.That(created, Has.Count.EqualTo(restored ? 0 : 2));
                    certificates.VerifyNoOtherCalls();
                }
            });
        }

        private static Button Button(IdentityEnrollmentDialog dialog, string name)
        {
            return DesktopInteraction.Control<Button>(dialog, name);
        }

        private static string Status(IdentityEnrollmentDialog dialog)
        {
            return DesktopInteraction.Control<TextBlock>(dialog, "EnrollmentStatus").Text ?? string.Empty;
        }

        private static async Task PumpAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        }

        private static readonly TimeSpan sWait = TimeSpan.FromSeconds(10);

        private sealed class DialogFixture
        {
            public DialogFixture()
            {
                Enrollment.Setup(enrollment => enrollment.PrepareAsync(
                    Reference, Policy, It.IsAny<CancellationToken>())).ReturnsAsync(Review);
                Enrollment.Setup(enrollment => enrollment.RequestAsync(
                    Review, It.IsAny<IProgress<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result);
                Enrollment.Setup(enrollment => enrollment.AdoptAsync(
                    Result, It.IsAny<CancellationToken>())).ReturnsAsync(Adopted);
                Enrollment.Setup(enrollment => enrollment.DisposeAsync()).Returns(ValueTask.CompletedTask);
            }

            public Mock<IIdentityEnrollment> Enrollment { get; } = new(MockBehavior.Strict);

            public CertificateIdentityReference Reference { get; } = new()
            {
                SourceId = "user",
                PasswordSourceId = "password",
                SubjectName = "CN=Fixture user"
            };

            public CertificateIdentityReference Adopted => Reference with { Thumbprint = "AABBCCDD" };

            public UserTokenPolicy Policy { get; } = new(UserTokenType.Certificate)
            {
                PolicyId = "certificate",
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };

            public IdentityEnrollmentReview Review { get; } = new("user", "CN=Fixture user",
                new DateTimeOffset(2030, 1, 1, 0, 5, 0, TimeSpan.Zero))
            {
                ApplicationUri = "urn:fixture:application",
                GdsEndpoint = "opc.tcp://localhost:4840/gds",
                ApplicationId = new NodeId("application", 2),
                CertificateGroupId = new NodeId("group", 2),
                CertificateTypeId = new NodeId("type", 2)
            };

            public IdentityEnrollmentResult Result { get; } = new("CN=Fixture user", "AABBCCDD",
                new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            public IdentityEnrollmentDialog Create()
            {
                return new IdentityEnrollmentDialog(Enrollment.Object, Reference, Policy);
            }

            public void VerifyNoRequestOrAdoption()
            {
                Enrollment.Verify(enrollment => enrollment.RequestAsync(
                    It.IsAny<IdentityEnrollmentReview>(), It.IsAny<IProgress<string>>(),
                    It.IsAny<CancellationToken>()), Times.Never);
                Enrollment.Verify(enrollment => enrollment.AdoptAsync(
                    It.IsAny<IdentityEnrollmentResult>(), It.IsAny<CancellationToken>()), Times.Never);
            }
        }
    }
}
