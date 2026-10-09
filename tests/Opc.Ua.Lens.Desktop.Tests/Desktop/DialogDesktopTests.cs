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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Connection;
using UaLens.StructuredValues;
using UaLens.Subscriptions;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.Desktop;

/// <summary>
/// Real modal-window regressions with isolated operation backends.
/// No certificate store or network operation is performed.
/// </summary>
[TestFixture]
[Category("LensDesktop")]
[NonParallelizable]
public sealed class DialogDesktopTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public Task WriteOutcomeRemainsVisibleUntilAcknowledgedAndPendingCloseDrains(bool cancel, bool shutdown)
    {
        return DesktopApplication.RunAsync(async () =>
        {
            await using DesktopWindowScope scope = await DesktopWindowScope.OpenAsync().ConfigureAwait(true);
            var response = new TaskCompletionSource<WriteResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(backend => backend.MessageContext)
                .Returns(new ServiceMessageContext(scope.ViewModel.Telemetry, EncodeableFactory.Create()));
            session.Setup(backend => backend.ReadAsync(
                null, 0, TimestampsToReturn.Neither, It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReadResponse
                {
                    Results =
                    [
                        new DataValue(Variant.From(1)),
                        new DataValue(Variant.From(DataTypeIds.Int32)),
                        new DataValue(Variant.From(ValueRanks.Scalar)),
                        new DataValue(Variant.From(ArrayOf<uint>.Empty))
                    ]
                });
            CancellationToken writeToken = default;
            ArrayOf<WriteValue> writes = [];
            session.Setup(backend => backend.WriteAsync(
                null, It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()))
                .Callback<RequestHeader?, ArrayOf<WriteValue>, CancellationToken>((_, values, token) =>
                {
                    writeToken = token;
                    writes = values;
                })
                .Returns(new ValueTask<WriteResponse>(response.Task));
            var values = new Mock<IStructuredValueService>(MockBehavior.Strict);
            values.Setup(service => service.ResolveAsync(DataTypeIds.Int32, It.IsAny<CancellationToken>()))
                .ReturnsAsync((DataTypeDefinition?)null);
            var node = new NodeViewModel(scope.ViewModel.Browser, ObjectIds.ObjectsFolder,
                new NodeId("desktop-write", 0), "Desktop write", NodeClass.Variable);
            var operation = new WriteValueOperation(node.NodeId, session.Object);
            var dialog = new WriteValueDialog(node, session.Object, operation, values.Object);
            var write = dialog.FindControl<Button>("OkButton")!;
            var acknowledge = dialog.FindControl<Button>("CancelButton")!;
            var result = dialog.FindControl<TextBlock>("ResultLabel")!;
            Task modal = Task.CompletedTask;
            await DesktopWindowScope.ControlChangeAsync(write, () => write.IsEnabled,
                () => modal = dialog.ShowDialog(scope.Window)).ConfigureAwait(true);
            await DesktopWindowScope.FrameAsync(dialog).ConfigureAwait(true);
            dialog.FindControl<TextBox>("ValueText")!.Text = "42";
            try
            {
                DesktopWindowScope.Click(write);
                Assert.That(write.IsEnabled, Is.False);
                Assert.That(dialog.FindControl<TextBox>("ValueText")!.IsEnabled, Is.False);
                Assert.That(writes.Count, Is.EqualTo(1));
                Assert.That(writes[0].Value.WrappedValue.TryGetValue(out int sent), Is.True);
                Assert.That(sent, Is.EqualTo(42));
                write.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                session.Verify(backend => backend.WriteAsync(
                    null, It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()), Times.Once);
                if (shutdown)
                {
                    Assert.That(scope.Window.OwnedWindows.Single(), Is.SameAs(dialog));
                    scope.Window.Close();
                    await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
                    Assert.That(scope.Window.IsClosingRequested, Is.True);
                    Assert.That(writeToken.IsCancellationRequested, Is.True);
                    Assert.That(operation.State, Is.EqualTo(WriteValueState.Closing));
                    Assert.That(modal.IsCompleted, Is.False,
                        "The owned write dialog must remain alive while its request drains.");
                    Assert.That(scope.Closed.IsCompleted, Is.False,
                        "The native shell must await its owned write operation.");
                    Assert.That(dialog.IsVisible, Is.True);
                    Assert.That(scope.Window.TryGetPlatformHandle()?.Handle,
                        Is.Not.Null.And.Not.EqualTo(IntPtr.Zero));
                    Assert.That(scope.ViewModel.Workspace.IsClosing, Is.False,
                        "Workspace and connection disposal must follow owned operation cleanup.");
                    response.SetResult(new WriteResponse { Results = [StatusCodes.Good] });
                    await Task.WhenAll(modal, scope.Closed).WaitAsync(DesktopApplication.Timeout)
                        .ConfigureAwait(true);
                    Assert.That(operation.State, Is.EqualTo(WriteValueState.Succeeded),
                        "Shutdown must observe the server result even after requesting local cancellation.");
                    Assert.That(dialog.IsVisible, Is.False);
                    Assert.That(scope.Window.OwnedWindows, Is.Empty);
                    Assert.That(scope.Window.TryGetPlatformHandle(), Is.Null);
                    Assert.That(scope.ViewModel.Workspace.IsClosing, Is.True);
                    session.Verify(backend => backend.WriteAsync(
                        null, It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()), Times.Once);
                    return;
                }
                if (cancel)
                {
                    DesktopWindowScope.Click(acknowledge);
                    Assert.That(writeToken.IsCancellationRequested, Is.True);
                    Assert.That(acknowledge.IsEnabled, Is.False);
                    Assert.That(result.Text, Does.Contain("cancellation is not rollback"));
                    Assert.That(modal.IsCompleted, Is.False, "A pending write must drain before its modal can close.");
                }
                await DesktopWindowScope.ControlChangeAsync(acknowledge,
                    () => acknowledge.IsEnabled && Equals(acknowledge.Content, "Acknowledge and close"),
                    () =>
                    {
                        if (cancel)
                        {
                            response.SetCanceled(writeToken);
                        }
                        else
                        {
                            response.SetResult(new WriteResponse { Results = [StatusCodes.Good] });
                        }
                    }).ConfigureAwait(true);
                Assert.That(operation.State,
                    Is.EqualTo(cancel ? WriteValueState.Uncertain : WriteValueState.Succeeded));
                Assert.That(result.Text, cancel ? Does.Contain("may have applied") : Does.Contain("succeeded"));
                Assert.That(AutomationProperties.GetName(result), Is.EqualTo(result.Text));
                Assert.That(ControlAutomationPeer.CreatePeerForElement(result).GetLiveSetting(),
                    Is.EqualTo(AutomationLiveSetting.Assertive));
                Assert.That(dialog.IsVisible, Is.True);
                Assert.That(modal.IsCompleted, Is.False,
                    "The result must remain visible until the user acknowledges it.");
                Assert.That(dialog.FocusManager.GetFocusedElement(), Is.SameAs(acknowledge));
                DesktopWindowScope.Click(acknowledge);
                await modal.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
                await dialog.DisposeAsync().ConfigureAwait(true);
                Assert.That(scope.Window.OwnedWindows, Is.Empty);
                session.Verify(backend => backend.WriteAsync(
                    null, It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                response.TrySetCanceled();
            }
        });
    }

    [Test]
    public Task AddItemValidationNamesAndFocusIdentifySamplingAndPercentBounds()
    {
        return DesktopApplication.RunAsync(async () =>
        {
            await using DesktopWindowScope scope = await DesktopWindowScope.OpenAsync().ConfigureAwait(true);
            var node = new NodeViewModel(scope.ViewModel.Browser, ObjectIds.ObjectsFolder,
                new NodeId("desktop-monitor", 0), "Desktop monitor", NodeClass.Variable);
            var dialog = new AddItemDialog(node, isEvent: false);
            Task<MonitoredItemConfig?> modal = dialog.ShowDialog<MonitoredItemConfig?>(scope.Window);
            await DesktopWindowScope.FrameAsync(dialog).ConfigureAwait(true);
            var sampling = dialog.FindControl<TextBox>("SamplingMs")!;
            var deadband = dialog.FindControl<TextBox>("DeadbandValueBox")!;
            var error = dialog.FindControl<TextBlock>("ValidationError")!;
            var accept = dialog.FindControl<Button>("OkButton")!;
            sampling.Text = "-2";
            await DesktopWindowScope.FrameAsync(dialog).ConfigureAwait(true);
            DesktopWindowScope.Click(accept);
            Assert.That(modal.IsCompleted, Is.False);
            Assert.That(dialog.FocusManager.GetFocusedElement(), Is.SameAs(sampling));
            Assert.That(error.Text, Does.Contain("Sampling interval (ms)").And.Contain("3600000"));
            Assert.That(ControlAutomationPeer.CreatePeerForElement(sampling).GetHelpText(), Is.EqualTo(error.Text));
            Assert.That(AutomationProperties.GetName(error), Is.EqualTo(error.Text));
            Assert.That(ControlAutomationPeer.CreatePeerForElement(error).GetLiveSetting(),
                Is.EqualTo(AutomationLiveSetting.Assertive));
            string? samplingError = error.Text;
            dialog.FindControl<ComboBox>("DeadbandTypeCombo")!.SelectedIndex = (int)DeadbandType.Percent;
            Assert.That(deadband.Focus(), Is.True);
            deadband.Text = "5";
            await DesktopWindowScope.FrameAsync(dialog).ConfigureAwait(true);
            Assert.That(error.Text, Is.EqualTo(samplingError),
                "Editing a different valid field must not hide the sampling error.");
            Assert.That(ControlAutomationPeer.CreatePeerForElement(sampling).GetHelpText(),
                Is.EqualTo(samplingError));
            sampling.Text = "250";
            await DesktopWindowScope.FrameAsync(dialog).ConfigureAwait(true);
            Assert.That(error.Text, Is.Empty);
            Assert.That(ControlAutomationPeer.CreatePeerForElement(sampling).GetHelpText(), Is.Null.Or.Empty);
            deadband.Text = "101";
            await DesktopWindowScope.FrameAsync(dialog).ConfigureAwait(true);
            DesktopWindowScope.Click(accept);
            Assert.That(dialog.FocusManager.GetFocusedElement(), Is.SameAs(deadband));
            Assert.That(error.Text, Does.Contain("0 to 100"));
            Assert.That(ControlAutomationPeer.CreatePeerForElement(deadband).GetName(),
                Is.EqualTo("Deadband value (%)"));
            Assert.That(ControlAutomationPeer.CreatePeerForElement(deadband).GetHelpText(), Is.EqualTo(error.Text));
            Assert.That(modal.IsCompleted, Is.False);
            await DesktopPaletteCapture.CaptureAsync(scope, dialog, error).ConfigureAwait(true);
            deadband.Text = "100";
            await DesktopWindowScope.FrameAsync(dialog).ConfigureAwait(true);
            DesktopWindowScope.Click(accept);
            MonitoredItemConfig? item = await modal.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
            Assert.That(item, Is.Not.Null);
            Assert.That(item!.SamplingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(250)));
            Assert.That(item.DataChangeFilter?.DeadbandValue, Is.EqualTo(100));
            Assert.That(item.NodeId, Is.EqualTo(node.NodeId));
            Assert.That(scope.Window.OwnedWindows, Is.Empty);
        });
    }

    [TestCase("Click", false)]
    [TestCase("Space", false)]
    [TestCase("Enter", false)]
    [TestCase("Escape", false)]
    [TestCase("Click", true)]
    public Task CertificateCloseRoutesDismissModalAndRestoreOwnerFocus(string route, bool designerConstructor)
    {
        return DesktopApplication.RunAsync(async () =>
        {
            await using DesktopWindowScope scope = await DesktopWindowScope.OpenAsync().ConfigureAwait(true);
            ArrayOf<X509Certificate2> emptyStore = [];
            var store = new Mock<ICertificateStoreAccess>(MockBehavior.Strict);
            store.Setup(backend => backend.ListAsync(It.IsAny<CertStoreKind>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(emptyStore);
            var dialog = designerConstructor
                ? new CertificateStoreDialog()
                : new CertificateStoreDialog(new CertificateStoreOperations(store.Object));
            await dialog.InitialLoad.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
            var returnTarget = scope.Window.FindControl<Button>("AddToolButton")!;
            Assert.That(returnTarget.Focus(NavigationMethod.Tab), Is.True);
            Task modal = dialog.ShowAsync(scope.Window);
            await DesktopWindowScope.FrameAsync(dialog).ConfigureAwait(true);
            Assert.That(scope.Window.OwnedWindows.Single(), Is.SameAs(dialog));
            var close = dialog.FindControl<Button>("CloseBtn")!;
            Assert.That(close.Focus(NavigationMethod.Tab), Is.True);
            Assert.That(dialog.FocusManager.GetFocusedElement(), Is.SameAs(close));
            if (route == "Click")
            {
                DesktopWindowScope.Click(close);
            }
            else
            {
                Key key = Enum.Parse<Key>(route);
                DesktopWindowScope.Key(close, key);
                if (dialog.IsVisible)
                {
                    DesktopWindowScope.Key(close, key, keyUp: true);
                }
            }
            await modal.WaitAsync(DesktopApplication.Timeout).ConfigureAwait(true);
            await DesktopWindowScope.FrameAsync(scope.Window).ConfigureAwait(true);
            Assert.That(dialog.IsVisible, Is.False);
            Assert.That(scope.Window.OwnedWindows, Is.Empty);
            Assert.That(scope.Window.FocusManager.GetFocusedElement(), Is.SameAs(returnTarget),
                "Dismissing a modal must restore the owner's previously focused control.");
            if (!designerConstructor)
            {
                store.Verify(backend => backend.ListAsync(
                    CertStoreKind.Trusted, It.IsAny<CancellationToken>()), Times.Once);
                store.Verify(backend => backend.ListAsync(
                    CertStoreKind.Issuer, It.IsAny<CancellationToken>()), Times.Once);
                store.Verify(backend => backend.ListAsync(
                    CertStoreKind.Rejected, It.IsAny<CancellationToken>()), Times.Once);
            }
            store.VerifyNoOtherCalls();
        });
    }
}
