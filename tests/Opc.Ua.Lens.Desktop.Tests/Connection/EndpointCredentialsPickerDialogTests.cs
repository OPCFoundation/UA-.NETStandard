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
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Connection;
using UaLens.Tests.Desktop;
using UaLens.Views;

namespace UaLens.Tests.Connection;

/// <summary>
/// Exercises the real Avalonia modal and picker caller without discovery,
/// sessions, or certificate stores. Select this explicitly on a desktop agent.
/// </summary>
[TestFixture]
[Category("ConnectionDialogProbe")]
[Category("LensDesktop")]
[NonParallelizable]
public sealed class EndpointCredentialsPickerDialogTests
{
    [Test]
    public Task SecureEndpointAcceptReturnsAnonymousAndCancelReturnsNull()
    {
        return DesktopApplication.RunAsync(async () =>
        {
            await using DesktopWindowScope scope = await DesktopWindowScope.OpenAsync().ConfigureAwait(true);
            EndpointDescription endpoint = SecureEndpoint();
            var unsecured = new EndpointDescription(k_endpointUrl)
            {
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens = endpoint.UserIdentityTokens
            };
            Task<EndpointCredentialsPicker.Result?> accepted =
                EndpointCredentialsPicker.PromptAsync(scope.Window, [unsecured, endpoint]);
            Assert.That(scope.Window.OwnedWindows, Has.Count.EqualTo(1));
            Window picker = scope.Window.OwnedWindows[0];
            await DesktopWindowScope.FrameAsync(picker).ConfigureAwait(true);
            var tree = picker.FindControl<TreeView>("Tree")!;
            tree.SelectedItem = tree.Items.OfType<PickerNode>().Single(node => node.Endpoint == endpoint);
            Assert.That(picker.Owner, Is.SameAs(scope.Window));
            Assert.That(picker.TryGetPlatformHandle()?.Handle, Is.Not.EqualTo(System.IntPtr.Zero));
            DesktopWindowScope.Click(picker.FindControl<Button>("OkButton")!);

            EndpointCredentialsPicker.Result? choice =
                await accepted.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
            Assert.That(choice, Is.Not.Null);
            Assert.That(choice!.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(choice.Endpoint.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(choice.Identity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
            Assert.That(choice.Policy!.PolicyId, Is.EqualTo("anonymous"));
            Assert.That(scope.Window.OwnedWindows, Is.Empty);

            Task<EndpointCredentialsPicker.Result?> cancelled =
                EndpointCredentialsPicker.PromptAsync(scope.Window, [endpoint]);
            Assert.That(scope.Window.OwnedWindows, Has.Count.EqualTo(1));
            picker = scope.Window.OwnedWindows[0];
            await DesktopWindowScope.FrameAsync(picker).ConfigureAwait(true);
            DesktopWindowScope.Click(picker.FindControl<Button>("CancelButton")!);
            Assert.That(await cancelled.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true), Is.Null);
            Assert.That(scope.Window.OwnedWindows, Is.Empty);
        });
    }

    [Test]
    public Task CancellationClosesUnselectableEndpointWithoutChoosingAnonymous()
    {
        return DesktopApplication.RunAsync(async () =>
        {
            await using DesktopWindowScope scope = await DesktopWindowScope.OpenAsync().ConfigureAwait(true);
            EndpointDescription endpoint = SecureEndpoint();
            endpoint.UserIdentityTokens = [new UserTokenPolicy(UserTokenType.UserName) { PolicyId = "username" }];
            using var cancellation = new CancellationTokenSource();
            Task<EndpointCredentialsPicker.Result?> pending =
                EndpointCredentialsPicker.PromptAsync(scope.Window, [endpoint], cancellation.Token);
            Window picker = scope.Window.OwnedWindows.Single();
            await DesktopWindowScope.FrameAsync(picker).ConfigureAwait(true);
            Assert.That(picker.FindControl<Button>("OkButton")!.IsEnabled, Is.False);
            await cancellation.CancelAsync().ConfigureAwait(true);
            await Assert.ThatAsync(() => pending.WaitAsync(DesktopApplication.Timeout),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(true);
            Assert.That(scope.Window.OwnedWindows, Is.Empty);
            Assert.That(scope.Window.IsVisible, Is.True);
        });
    }

    private static EndpointDescription SecureEndpoint()
    {
        return new EndpointDescription(k_endpointUrl)
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "anonymous" }]
        };
    }

    private const string k_endpointUrl = "opc.tcp://localhost:4900/picker";
}
