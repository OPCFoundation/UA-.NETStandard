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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Encoding.Uadp;
using UaLens.Plugins.PubSub;
using UaLens.Tests.Observe;
using UaLens.ViewModels;

namespace UaLens.Tests.PubSub;

[TestFixture]
public sealed class PubSubWorkspaceTests
{
    [Test]
    public async Task ConfigurationAndRestoreNeverAcquireAnApplicationOrStartTraffic()
    {
        var runtime = new PubSubTestRuntime();
        var workspace = runtime.CreateWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.ConfigureAsync(PubSubTestRuntime.Configuration).ConfigureAwait(false);
            PubSubConfiguration restored = PubSubStateCodec.Restore(PubSubStateCodec.Capture(workspace.Configuration));
            await workspace.ConfigureAsync(restored).ConfigureAwait(false);
            await workspace.BindPrimarySessionAsync(null).ConfigureAwait(false);

            Assert.That(workspace.Snapshot().Phase, Is.EqualTo(PubSubDocumentPhase.Offline));
            Assert.That(workspace.Snapshot().Observations.Values.Count, Is.Zero);
            runtime.Factory.Verify(factory => factory.CreateAsync(
                It.IsAny<PubSubConfiguration>(), It.IsAny<PubSubStartAuthorization>(), It.IsAny<ISession>(),
                It.IsAny<PubSubObservationStore>(), It.IsAny<CancellationToken>()), Times.Never);
            runtime.Application.Verify(app => app.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    [Test]
    public async Task PluginRoundTripIsTypedOfflineAndClearsTransientConsentAndInputs()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            var runtime = new PubSubTestRuntime();
            var plugin = new PubSubPlugin(host.Host, runtime.Factory.Object)
            {
                AllowUnsecured = true,
                AllowPublication = true,
                AllowWriteBack = true,
                AllowResponder = true,
                AllowAnonymousBroker = true,
                AllowAction = true,
                ActionInputs = "[{\"name\":\"input\",\"type\":\"String\",\"text\":\"ephemeral-marker\"}]"
            };
            await using (plugin.ConfigureAwait(false))
            {
                await plugin.RestoreStateAsync(PubSubStateCodec.Capture(PubSubTestRuntime.Configuration))
                    .ConfigureAwait(false);
                await plugin.OnConnectionStateChangedAsync(CancellationToken.None).ConfigureAwait(false);

                Assert.That(plugin.Kind, Is.EqualTo(PluginKind.PubSub));
                Assert.That(plugin.IsOffline, Is.True);
                Assert.That(plugin.AllowUnsecured, Is.False);
                Assert.That(plugin.AllowPublication, Is.False);
                Assert.That(plugin.AllowWriteBack, Is.False);
                Assert.That(plugin.AllowResponder, Is.False);
                Assert.That(plugin.AllowAnonymousBroker, Is.False);
                Assert.That(plugin.AllowAction, Is.False);
                Assert.That(plugin.ActionInputs, Is.EqualTo("[]"));
                Assert.That(plugin.CaptureState().GetRawText(), Does.Not.Contain("ephemeral-marker"));
                runtime.Application.Verify(app => app.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
            }
        }
    }

    [Test]
    public async Task ReceiveStartsWithoutAPrimarySessionAndStopDisposesTheOwnedRuntimeOnce()
    {
        var runtime = new PubSubTestRuntime();
        var workspace = runtime.CreateWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.ConfigureAsync(PubSubTestRuntime.Configuration).ConfigureAwait(false);
            await workspace.StartAsync(PubSubTestRuntime.ReceiveAuthorization).ConfigureAwait(false);
            Assert.That(workspace.Snapshot().Phase, Is.EqualTo(PubSubDocumentPhase.Running));
            Assert.That(runtime.Store, Is.Not.Null);
            await runtime.Store!.WriteAsync([PubSubTestRuntime.Field(42)]).ConfigureAwait(false);

            await workspace.BindPrimarySessionAsync(null).ConfigureAwait(false);
            Assert.That(workspace.Snapshot().Phase, Is.EqualTo(PubSubDocumentPhase.Running));
            runtime.Application.Verify(app => app.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(workspace.Snapshot().Observations.AcceptedDataSets, Is.EqualTo(1));

            await workspace.StopAsync().ConfigureAwait(false);
            await workspace.StopAsync().ConfigureAwait(false);
            Assert.That(workspace.Snapshot().Phase, Is.EqualTo(PubSubDocumentPhase.Offline));
            runtime.Application.Verify(app => app.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
            runtime.Application.Verify(app => app.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
            runtime.Application.Verify(app => app.DisposeAsync(), Times.Once);
        }
    }

    [Test]
    public async Task CallerCancellationAfterStartStillStopsTheWholeOwnedWorkload()
    {
        var runtime = new PubSubTestRuntime();
        var workspace = runtime.CreateWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            using var cancellation = new CancellationTokenSource();
            await workspace.ConfigureAsync(PubSubTestRuntime.Configuration).ConfigureAwait(false);
            await workspace.StartAsync(PubSubTestRuntime.ReceiveAuthorization, cancellation.Token)
                .ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            await runtime.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await workspace.StopAsync().ConfigureAwait(false);

            Assert.That(runtime.Disposed.Task.IsCompletedSuccessfully, Is.True);
            Assert.That(workspace.Snapshot().Phase, Is.EqualTo(PubSubDocumentPhase.Offline));
            Assert.That(workspace.Snapshot().Observations.Evidence.Contains(entry => entry.Area == "Lifecycle"),
                Is.True);
        }
    }

    [Test]
    public async Task ClosingDuringStartCancelsAndAwaitsPartialRuntimeDisposal()
    {
        var runtime = new PubSubTestRuntime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Application.Setup(app => app.StartAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken token) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
            });
        var workspace = runtime.CreateWorkspace();
        await workspace.ConfigureAsync(PubSubTestRuntime.Configuration).ConfigureAwait(false);
        Task start = workspace.StartAsync(PubSubTestRuntime.ReceiveAuthorization);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await workspace.DisposeAsync().ConfigureAwait(false);

        await Assert.ThatAsync(() => start, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        Assert.That(runtime.Disposed.Task.IsCompletedSuccessfully, Is.True);
        Assert.That(workspace.Snapshot().Phase, Is.EqualTo(PubSubDocumentPhase.Disposed));
    }

    [Test]
    public async Task StartFailureIsVisibleRedactedAndCanBeReconfiguredOffline()
    {
        var runtime = new PubSubTestRuntime();
        runtime.Application.Setup(app => app.StartAsync(It.IsAny<CancellationToken>()))
            .Throws(new IOException("credential-marker should not reach evidence"));
        var workspace = runtime.CreateWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.ConfigureAsync(PubSubTestRuntime.Configuration).ConfigureAwait(false);
            await Assert.ThatAsync(() => workspace.StartAsync(PubSubTestRuntime.ReceiveAuthorization),
                Throws.InstanceOf<IOException>()).ConfigureAwait(false);
            PubSubWorkspaceSnapshot failed = workspace.Snapshot();
            Assert.That(failed.Phase, Is.EqualTo(PubSubDocumentPhase.Faulted));
            Assert.That(failed.Status, Does.Not.Contain("credential-marker"));
            Assert.That(failed.Observations.Evidence.ToList().All(entry =>
                !entry.Detail.Contains("credential-marker", StringComparison.Ordinal)),
                Is.True);
            Assert.That(runtime.Disposed.Task.IsCompletedSuccessfully, Is.True);

            await workspace.ConfigureAsync(PubSubTestRuntime.Configuration with { DurationSeconds = 2 })
                .ConfigureAwait(false);
            Assert.That(workspace.Snapshot().Phase, Is.EqualTo(PubSubDocumentPhase.Offline));
        }
    }

    [Test]
    public async Task StopFailureCannotSkipDisposalAndIsNotReportedAsSuccessfulCleanup()
    {
        var runtime = new PubSubTestRuntime();
        runtime.Application.Setup(app => app.StopAsync(It.IsAny<CancellationToken>()))
            .Throws(new IOException("stop failed"));
        var workspace = runtime.CreateWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.ConfigureAsync(PubSubTestRuntime.Configuration).ConfigureAwait(false);
            await workspace.StartAsync(PubSubTestRuntime.ReceiveAuthorization).ConfigureAwait(false);
            await workspace.StopAsync().ConfigureAwait(false);

            Assert.That(runtime.Disposed.Task.IsCompletedSuccessfully, Is.True);
            Assert.That(workspace.Snapshot().Phase, Is.EqualTo(PubSubDocumentPhase.Faulted));
            Assert.That(workspace.Snapshot().Observations.Evidence.Contains(entry => entry.Area == "Cleanup"), Is.True);
        }
    }

    [Test]
    public async Task DurationExpiryAutomaticallyStopsPublicationWithoutTimingBasedAssertions()
    {
        var runtime = new PubSubTestRuntime();
        var expiry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayEntered = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workspace = runtime.CreateWorkspace((duration, token) =>
        {
            delayEntered.TrySetResult(duration);
            return expiry.Task.WaitAsync(token);
        });
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.ConfigureAsync(PubSubTestRuntime.Configuration with
            {
                Publication = PubSubPublication.Synthetic,
                DurationSeconds = 3
            }).ConfigureAwait(false);
            await workspace.StartAsync(new PubSubStartAuthorization(AllowUnsecured: true, AllowPublication: true))
                .ConfigureAwait(false);
            TimeSpan duration = await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(duration, Is.EqualTo(TimeSpan.FromSeconds(3)));
            expiry.SetResult();
            await runtime.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await workspace.StopAsync().ConfigureAwait(false);
            Assert.That(runtime.Disposed.Task.IsCompletedSuccessfully, Is.True);
            Assert.That(workspace.Snapshot().Observations.Evidence.Contains(entry =>
                entry.Detail.Contains("bound", StringComparison.Ordinal)),
                Is.True);
        }
    }

    [Test]
    public async Task PrimaryLossReleasesOnlyExplicitBindingsAndDoesNotStopIndependentReception()
    {
        var runtime = new PubSubTestRuntime();
        runtime.Factory.Setup(factory => factory.UsesPrimarySession(It.IsAny<PubSubConfiguration>())).Returns(true);
        var session = new Mock<ISession>();
        session.SetupGet(value => value.Connected).Returns(true);
        var workspace = runtime.CreateWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.ConfigureAsync(PubSubTestRuntime.Configuration).ConfigureAwait(false);
            await workspace.BindPrimarySessionAsync(session.Object).ConfigureAwait(false);
            await workspace.StartAsync(PubSubTestRuntime.ReceiveAuthorization).ConfigureAwait(false);
            await workspace.BindPrimarySessionAsync(null).ConfigureAwait(false);

            Assert.That(runtime.PrimaryReleased.Task.IsCompletedSuccessfully, Is.True);
            Assert.That(workspace.Snapshot().Phase, Is.EqualTo(PubSubDocumentPhase.Running));
            runtime.Application.Verify(app => app.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(runtime.Store, Is.Not.Null);
            await runtime.Store!.WriteAsync([PubSubTestRuntime.Field(5)]).ConfigureAwait(false);
            Assert.That(workspace.Snapshot().Observations.AcceptedDataSets, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task ActionInvocationRequiresConsentAndUsesRealStackCorrelationEvidence()
    {
        var runtime = new PubSubTestRuntime();
        PubSubActionRequest? sent = null;
        runtime.Application.Setup(app => app.InvokeActionAsync(
            It.IsAny<PubSubActionRequest>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns((PubSubActionRequest request, TimeSpan _, CancellationToken _) =>
            {
                sent = request;
                return ValueTask.FromResult(new PubSubActionResponse
                {
                    Target = request.Target,
                    RequestId = 77,
                    CorrelationData = new ByteString(new byte[] { 1, 2, 3, 4 }),
                    StatusCode = StatusCodes.Good,
                    ActionState = ActionState.Done,
                    OutputFields = [PubSubTestRuntime.Field(42, "Answer")]
                });
            });
        var workspace = runtime.CreateWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.ConfigureAsync(PubSubTestRuntime.Configuration).ConfigureAwait(false);
            await workspace.StartAsync(PubSubTestRuntime.ReceiveAuthorization).ConfigureAwait(false);
            var request = new PubSubActionRequest
            {
                Target = new PubSubActionTarget { DataSetWriterId = 2, ActionTargetId = 1 },
                InputFields = [PubSubTestRuntime.Field(21, "Input")]
            };
            Assert.That(() => workspace.InvokeActionAsync(request, false, TimeSpan.FromSeconds(2)),
                Throws.InvalidOperationException);
            Assert.That(sent, Is.Null);
            PubSubActionResult result = await workspace.InvokeActionAsync(request, true, TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);

            Assert.That(sent, Is.Not.Null);
            Assert.That(sent!.Target.ConnectionName, Is.EqualTo(PubSubStackConfiguration.ConnectionName));
            Assert.That(result.RequestId, Is.EqualTo(77));
            Assert.That(result.Correlation, Is.EqualTo("01020304"));
            Assert.That(result.Outputs[0].Value.TryGetValue(out int answer), Is.True);
            Assert.That(answer, Is.EqualTo(42));
            Assert.That(workspace.Snapshot().Action, Is.EqualTo(result));
        }
    }

    [Test]
    public async Task StopCancelsAnInFlightActionBeforeDisposingTheApplication()
    {
        var runtime = new PubSubTestRuntime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Application.Setup(app => app.InvokeActionAsync(
            It.IsAny<PubSubActionRequest>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(async (PubSubActionRequest _, TimeSpan _, CancellationToken token) =>
            {
                entered.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                }
                finally
                {
                    canceled.SetResult();
                }
                return new PubSubActionResponse();
            });
        var workspace = runtime.CreateWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.ConfigureAsync(PubSubTestRuntime.Configuration).ConfigureAwait(false);
            await workspace.StartAsync(PubSubTestRuntime.ReceiveAuthorization).ConfigureAwait(false);
            Task<PubSubActionResult> action = workspace.InvokeActionAsync(new PubSubActionRequest
            {
                Target = new PubSubActionTarget { DataSetWriterId = 2, ActionTargetId = 1 }
            }, true, TimeSpan.FromSeconds(2));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await workspace.StopAsync().ConfigureAwait(false);

            await Assert.ThatAsync(() => action, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(canceled.Task.IsCompletedSuccessfully, Is.True);
            Assert.That(runtime.Disposed.Task.IsCompletedSuccessfully, Is.True);
            Assert.That(workspace.Snapshot().Action, Is.Null);
        }
    }

    [Test]
    public async Task ALateActionResponseAfterStopCannotCommitASuccessfulResult()
    {
        var runtime = new PubSubTestRuntime();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<PubSubActionResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Application.Setup(app => app.InvokeActionAsync(
            It.IsAny<PubSubActionRequest>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns((PubSubActionRequest _, TimeSpan _, CancellationToken _) =>
            {
                entered.SetResult();
                return new ValueTask<PubSubActionResponse>(response.Task);
            });
        var workspace = runtime.CreateWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.ConfigureAsync(PubSubTestRuntime.Configuration).ConfigureAwait(false);
            await workspace.StartAsync(PubSubTestRuntime.ReceiveAuthorization).ConfigureAwait(false);
            Task<PubSubActionResult> action = workspace.InvokeActionAsync(new PubSubActionRequest
            {
                Target = new PubSubActionTarget { DataSetWriterId = 2, ActionTargetId = 1 }
            }, true, TimeSpan.FromSeconds(2));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Task stop = workspace.StopAsync();
            response.SetResult(new PubSubActionResponse
            {
                RequestId = 5,
                CorrelationData = new ByteString(new byte[] { 1, 2, 3, 4 }),
                OutputFields = [PubSubTestRuntime.Field(42)]
            });
            await stop.ConfigureAwait(false);
            await Assert.ThatAsync(() => action, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(workspace.Snapshot().Action, Is.Null);
            Assert.That(runtime.Disposed.Task.IsCompletedSuccessfully, Is.True);
        }
    }

    [Test]
    public async Task DiscoveryResultsAreBoundedAndAdvertisedEndpointsDoNotBecomeConnections()
    {
        var runtime = new PubSubTestRuntime();
        runtime.Application.Setup(app => app.RequestDiscoveryAsync(
            It.IsAny<PubSubDiscoveryRequest>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(new PubSubDiscoveryResult
            {
                DataSetMetaDataEntries =
                [
                    .. Enumerable.Range(1, 100).Select(id => new PubSubDataSetMetaDataDiscoveryResult
                    {
                        PublisherId = PublisherId.FromUInt16((ushort)id),
                        DataSetWriterId = 1,
                        StatusCode = StatusCodes.Good
                    })
                ],
                PublisherEndpoints = [new EndpointDescription { EndpointUrl = "opc.tcp://untrusted.example:4840" }]
            }));
        var workspace = runtime.CreateWorkspace();
        await using (workspace.ConfigureAwait(false))
        {
            await workspace.ConfigureAsync(PubSubTestRuntime.Configuration).ConfigureAwait(false);
            await workspace.StartAsync(PubSubTestRuntime.ReceiveAuthorization).ConfigureAwait(false);
            var rows = await workspace.DiscoverAsync(UadpDiscoveryType.DataSetMetaData, TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
            Assert.That(rows, Has.Count.EqualTo(64));
            Assert.That(workspace.Snapshot().Discovery, Has.Count.EqualTo(64));
            runtime.Application.Verify(app => app.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
