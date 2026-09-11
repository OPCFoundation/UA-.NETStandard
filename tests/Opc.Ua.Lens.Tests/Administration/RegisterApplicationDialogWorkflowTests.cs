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
using System.Threading.Tasks;
using Avalonia.Controls;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Gds;
using UaLens.Plugins.GdsManagement;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class RegisterApplicationDialogWorkflowTests
{
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public Task EachRegistrationModeReturnsAcceptedIntentAndKeepsBothCertificatePathFamilies(int mode)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            RegisteredApplicationContext original = RegistrationTestData.Create();
            var dialog = new RegisterApplicationDialog(null, original);
            Task<RegisteredApplicationContext?> prompt =
                dialog.ShowDialog<RegisteredApplicationContext?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<ComboBox>(dialog, "RegistrationTypeBox").SelectedIndex = mode;
                DesktopInteraction.Control<TextBox>(dialog, "NameBox").Text = "  Assembly revision 2  ";
                Assert.That(DesktopInteraction.Control<StackPanel>(dialog, "PullPanel").IsVisible,
                    Is.EqualTo(mode != 2));
                Assert.That(DesktopInteraction.Control<StackPanel>(dialog, "PushPanel").IsVisible,
                    Is.EqualTo(mode == 2));
                Assert.That(DesktopInteraction.Control<Button>(dialog, "PickEndpointButton").IsEnabled, Is.False);
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                RegisteredApplicationContext result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.ApplicationName, Is.EqualTo("Assembly revision 2"));
                Assert.That(result.ApplicationUri, Is.EqualTo("urn:fixture:assembly"));
                Assert.That(result.ProductUri, Is.EqualTo("urn:fixture:product"));
                Assert.That(result.RegistrationType, Is.EqualTo((GdsRegistrationType)mode));
                Assert.That(result.ApplicationId.IsNull, Is.True, "A new registration must not reuse the old GDS id.");
                Assert.That(result.DiscoveryUrls,
                    Is.EqualTo(s_eachRegistrationModeReturnsAcceptedIntentAndKeepsBothCertificExpected));
                Assert.That(result.ServerCapabilities, Is.EqualTo(s_eachRegistrationModeReturnsAcceptedIntentAndKeepsBothCertificExpected2));
                Assert.That(result.HasPushEndpoint, Is.EqualTo(mode == 2));
                RegistrationTestData.AssertPaths(result);
                Assert.That(original.ApplicationId, Is.EqualTo(new NodeId("assembly", 2)));
                Assert.That(original.ApplicationName, Is.EqualTo("Assembly line"));
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase("NameBox", "Application Name is required.")]
    [TestCase("UriBox", "Application URI is required.")]
    [TestCase("ProductUriBox", "Product URI is required.")]
    [TestCase("PushEndpointUrlBox", "Push endpoint URL is required for ServerPush mode.")]
    public Task RequiredFieldFailureKeepsTheDialogOpenUntilCorrected(string field, string error)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new RegisterApplicationDialog(null, RegistrationTestData.Create());
            Task<RegisteredApplicationContext?> prompt =
                dialog.ShowDialog<RegisteredApplicationContext?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                TextBox input = DesktopInteraction.Control<TextBox>(dialog, field);
                string original = input.Text!;
                input.Text = " \t ";
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                Assert.That(prompt.IsCompleted, Is.False);
                TextBlock label = DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel");
                Assert.That(label.Text, Is.EqualTo(error));
                Assert.That(label.IsVisible, Is.True);
                input.Text = original;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                RegisteredApplicationContext result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.ApplicationName, Is.EqualTo("Assembly line"));
                Assert.That(result.ApplicationUri, Is.EqualTo("urn:fixture:assembly"));
                Assert.That(result.PushEndpoint!.EndpointUrl, Is.EqualTo("opc.tcp://assembly.example.test:4840"));
                Assert.That(label.IsVisible, Is.False);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task MatchingPrefillPreservesSecurityAndIdentityButANewPushUrlDoesNotInventCredentials(bool changed)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            RegisteredApplicationContext original = RegistrationTestData.Create();
            var dialog = new RegisterApplicationDialog(null, original);
            Task<RegisteredApplicationContext?> prompt =
                dialog.ShowDialog<RegisteredApplicationContext?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "PushEndpointUrlBox").Text = changed
                    ? "  opc.tcp://replacement.example.test:4841  " : "OPC.TCP://ASSEMBLY.EXAMPLE.TEST:4840";
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "PushSecurityLabel").Text,
                    Is.EqualTo("Sign / " + SecurityPolicies.Basic256Sha256));
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                RegisteredApplicationContext result = (await prompt.ConfigureAwait(true))!;
                EndpointDescription endpoint = result.PushEndpoint!;
                if (changed)
                {
                    Assert.That(endpoint.EndpointUrl, Is.EqualTo("opc.tcp://replacement.example.test:4841"));
                    Assert.That(endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    Assert.That(endpoint.SecurityPolicyUri, Is.Empty);
                    Assert.That(endpoint.UserIdentityTokens.Count, Is.Zero);
                    Assert.That(endpoint, Is.Not.SameAs(original.PushEndpoint));
                }
                else
                {
                    Assert.That(endpoint, Is.SameAs(original.PushEndpoint));
                    Assert.That(endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.Sign));
                    Assert.That(endpoint.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
                    Assert.That(endpoint.UserIdentityTokens[0].PolicyId, Is.EqualTo("x509-operator"));
                    Assert.That(endpoint.UserIdentityTokens[0].TokenType, Is.EqualTo(UserTokenType.Certificate));
                    Assert.That(endpoint.Server.ApplicationUri, Is.EqualTo("urn:fixture:assembly-server"));
                }
                Assert.That(original.PushEndpoint!.EndpointUrl, Is.EqualTo("opc.tcp://assembly.example.test:4840"));
                Assert.That(result.ApplicationId.IsNull, Is.True);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task ListInputUsesDocumentedDelimitersAndOptionalWhitespaceBecomesNull(bool populated)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new RegisterApplicationDialog(null, null);
            Task<RegisteredApplicationContext?> prompt =
                dialog.ShowDialog<RegisteredApplicationContext?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "NameBox").Text = " Collector ";
                DesktopInteraction.Control<TextBox>(dialog, "UriBox").Text = " urn:fixture:collector ";
                DesktopInteraction.Control<TextBox>(dialog, "ProductUriBox").Text = " urn:fixture:product ";
                DesktopInteraction.Control<TextBox>(dialog, "DiscoveryBox").Text = populated
                    ? "  opc.tcp://first.test \r\n\n opc.tcp://second.test \n opc.tcp://first.test " : null;
                DesktopInteraction.Control<TextBox>(dialog, "CapabilitiesBox").Text =
                    populated ? " DA, \r\nHA,, DA " : string.Empty;
                DesktopInteraction.Control<TextBox>(dialog, "CertStorePathBox").Text = " \t ";
                DesktopInteraction.Control<TextBox>(dialog, "SubjectNameBox").Text = " CN=Collector ";
                DesktopInteraction.Control<ComboBox>(dialog, "RegistrationTypeBox").SelectedIndex = -1;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "OkButton"));
                RegisteredApplicationContext result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.ApplicationName, Is.EqualTo("Collector"));
                Assert.That(result.RegistrationType, Is.EqualTo(GdsRegistrationType.ClientPull));
                Assert.That(result.DiscoveryUrls, Is.EqualTo(populated
                    ? s_listInputUsesDocumentedDelimitersAndOptionalWhitespaceBecomesExpected : []));
                Assert.That(result.ServerCapabilities, Is.EqualTo(populated ? s_listInputUsesDocumentedDelimitersAndOptionalWhitespaceBecomesExpected2 : []));
                Assert.That(result.CertificateStorePath, Is.Null);
                Assert.That(result.CertificateSubjectName, Is.EqualTo("CN=Collector"));
                Assert.That(result.HttpsCertificatePrivateKeyPath, Is.Null);
                Assert.That(result.PushEndpoint, Is.Null);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task CancelLeavesTheOriginalRegistrationAndEndpointUntouched()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            RegisteredApplicationContext original = RegistrationTestData.Create();
            var dialog = new RegisterApplicationDialog(null, original);
            Task<RegisteredApplicationContext?> prompt =
                dialog.ShowDialog<RegisteredApplicationContext?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "NameBox").Text = "Discarded";
                DesktopInteraction.Control<TextBox>(dialog, "DiscoveryBox").Text = "opc.tcp://discarded.test";
                DesktopInteraction.Control<ComboBox>(dialog, "RegistrationTypeBox").SelectedIndex = 0;
                DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, "CancelButton"));
                Assert.That(await prompt.ConfigureAwait(true), Is.Null);
                Assert.That(original.ApplicationName, Is.EqualTo("Assembly line"));
                Assert.That(original.RegistrationType, Is.EqualTo(GdsRegistrationType.ServerPush));
                Assert.That(original.DiscoveryUrls,
                    Is.EqualTo(s_eachRegistrationModeReturnsAcceptedIntentAndKeepsBothCertificExpected));
                Assert.That(original.PushEndpoint!.UserIdentityTokens[0].PolicyId, Is.EqualTo("x509-operator"));
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    private static readonly string[] s_eachRegistrationModeReturnsAcceptedIntentAndKeepsBothCertificExpected =
    [
        "opc.tcp://assembly.example.test:4840",
        "https://assembly.test",
    ];
    private static readonly string[] s_eachRegistrationModeReturnsAcceptedIntentAndKeepsBothCertificExpected2 =
    [
        "DA",
        "HA",
    ];
    private static readonly string[] s_listInputUsesDocumentedDelimitersAndOptionalWhitespaceBecomesExpected =
    [
        "opc.tcp://first.test",
        "opc.tcp://second.test",
        "opc.tcp://first.test",
    ];
    private static readonly string[] s_listInputUsesDocumentedDelimitersAndOptionalWhitespaceBecomesExpected2 =
    [
        "DA",
        "HA",
        "DA",
    ];
}
