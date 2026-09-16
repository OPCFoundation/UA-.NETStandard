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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens.Tests.Connection;

/// <summary>
/// Exercises the real Avalonia modal and picker caller without discovery,
/// sessions, or certificate stores. Select this explicitly on a desktop agent.
/// </summary>
[TestFixture]
[Explicit("Requires an interactive desktop; no network or PKI is accessed.")]
[Category("ConnectionDialogProbe")]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class EndpointCredentialsPickerDialogTests
{
    [Test]
    public async Task SecureEndpointAcceptReturnsAnonymousAndCancelReturnsNull()
    {
        if (Application.Current is null)
        {
            AppBuilder.Configure<Application>().UsePlatformDetect().SetupWithoutStarting();
            Application.Current!.Styles.Add(new FluentTheme());
        }
        var endpoint = new EndpointDescription("opc.tcp://localhost:4900/picker")
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            UserIdentityTokens = [new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "anonymous" }]
        };
        var owner = new Window { Width = 320, Height = 200, ShowInTaskbar = false };
        owner.Show();
        try
        {
            Task<EndpointCredentialsPicker.Result?> accepted =
                EndpointCredentialsPicker.PromptAsync(owner, [endpoint]);
            Assert.That(owner.OwnedWindows, Has.Count.EqualTo(1));
            Window picker = owner.OwnedWindows[0];
            picker.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntilCompleted(accepted);

            EndpointCredentialsPicker.Result? choice = await accepted.ConfigureAwait(true);
            Assert.That(choice, Is.Not.Null);
            Assert.That(choice!.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(choice.Endpoint.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(choice.Identity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
            Assert.That(choice.Policy!.PolicyId, Is.EqualTo("anonymous"));

            Task<EndpointCredentialsPicker.Result?> cancelled =
                EndpointCredentialsPicker.PromptAsync(owner, [endpoint]);
            Assert.That(owner.OwnedWindows, Has.Count.EqualTo(1));
            picker = owner.OwnedWindows[0];
            picker.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntilCompleted(cancelled);

            Assert.That(await cancelled.ConfigureAwait(true), Is.Null);
        }
        finally
        {
            owner.Close();
        }
    }

    private static void PumpUntilCompleted(Task task)
    {
        using var completion = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        bool waiting = true;
        _ = task.ContinueWith(
            _ => Dispatcher.UIThread.Post(() =>
            {
                if (waiting)
                {
                    completion.Cancel();
                }
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            Dispatcher.UIThread.MainLoop(completion.Token);
        }
        finally
        {
            waiting = false;
        }
        Assert.That(task.IsCompleted, Is.True, "The modal closed without completing the picker caller.");
    }
}
