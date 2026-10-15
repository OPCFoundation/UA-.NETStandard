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
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Alarms;

namespace UaLens.Tests.Observe;

[TestFixture]
public sealed class AlarmCommandAdapterTests
{
    [Test]
    public async Task TypedAcknowledgeUsesActualConditionAndExactEventId()
    {
        var context = new CommandContext();
        await using var lifetime = context.ConfigureAwait(false);
        AlarmCondition condition = AlarmTestData.Condition(7, branch: 42);

        await context.Adapter.ExecuteAsync(
            new AlarmCommand(condition, AlarmOperationKind.Acknowledge, "Checked"), CancellationToken.None)
            .ConfigureAwait(false);

        CallMethodRequest request = context.LastCall!;
        Assert.That(request.ObjectId, Is.EqualTo(condition.Key.ConditionId));
        Assert.That(request.ObjectId, Is.Not.EqualTo(condition.SourceNode));
        Assert.That(request.MethodId, Is.EqualTo(MethodIds.AcknowledgeableConditionType_Acknowledge));
        Assert.That(request.InputArguments[0].TryGetValue(out ByteString eventId), Is.True);
        Assert.That(eventId, Is.EqualTo(ByteString.From([7])));
        Assert.That(request.InputArguments[1].TryGetValue(out LocalizedText comment), Is.True);
        Assert.That(comment.Text, Is.EqualTo("Checked"));
    }

    [Test]
    public async Task ShelvingUsesResolvedShelvingStateObjectRatherThanCondition()
    {
        var context = new CommandContext();
        await using var lifetime = context.ConfigureAwait(false);

        await context.Adapter.ExecuteAsync(
            new AlarmCommand(AlarmTestData.Condition(), AlarmOperationKind.TimedShelve, ShelvingMilliseconds: 90000),
            CancellationToken.None).ConfigureAwait(false);

        Assert.That(context.LastCall!.ObjectId, Is.EqualTo(CommandContext.ShelvingId));
        Assert.That(context.LastCall.ObjectId, Is.Not.EqualTo(AlarmTestData.Condition().Key.ConditionId));
        Assert.That(context.LastCall.MethodId, Is.EqualTo(MethodIds.ShelvedStateMachineType_TimedShelve));
        Assert.That(context.LastCall.InputArguments[0].TryGetValue(out double duration), Is.True);
        Assert.That(duration, Is.EqualTo(90000));
    }

    [Test]
    public async Task DeniedUserExecutableNeverSendsAnOperatorCall()
    {
        var context = new CommandContext();
        await using var lifetime = context.ConfigureAwait(false);
        context.UserExecutable = false;

        ArrayOf<AlarmOperation> operations = await context.Adapter.InspectAsync(
            AlarmTestData.Condition(), CancellationToken.None).ConfigureAwait(false);
        AlarmOperation acknowledge = operations.ToList().Single(
            static operation => operation.Kind == AlarmOperationKind.Acknowledge);
        Assert.That(acknowledge.Availability, Is.EqualTo(AlarmAvailability.Denied));
        Assert.That(acknowledge.CanInvoke, Is.False);

        await Assert.ThatAsync(
            async () => await context.Adapter.ExecuteAsync(
                new AlarmCommand(AlarmTestData.Condition(), AlarmOperationKind.Acknowledge), CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadUserAccessDenied)).ConfigureAwait(false);
        Assert.That(context.LastCall, Is.Null);
    }

    [Test]
    public async Task UnexposedConditionAllowsExplicitMandatoryPartNineCallAsUnknown()
    {
        var context = new CommandContext();
        await using var lifetime = context.ConfigureAwait(false);
        context.ExposeMethods = false;

        ArrayOf<AlarmOperation> operations = await context.Adapter.InspectAsync(
            AlarmTestData.Condition(), CancellationToken.None).ConfigureAwait(false);
        AlarmOperation comment = operations.ToList().Single(
            static operation => operation.Kind == AlarmOperationKind.AddComment);
        Assert.That(comment.Availability, Is.EqualTo(AlarmAvailability.Unknown));
        Assert.That(comment.CanInvoke, Is.True);

        await context.Adapter.ExecuteAsync(
            new AlarmCommand(AlarmTestData.Condition(), AlarmOperationKind.AddComment, "Explicit note"),
            CancellationToken.None).ConfigureAwait(false);

        Assert.That(context.LastCall!.ObjectId, Is.EqualTo(AlarmTestData.Condition().Key.ConditionId));
        Assert.That(context.LastCall.MethodId, Is.EqualTo(MethodIds.ConditionType_AddComment));
    }

    [Test]
    public async Task MissingAdvancedMethodIsUnsupportedAndNeverCalled()
    {
        var context = new CommandContext();
        await using var lifetime = context.ConfigureAwait(false);
        context.ExposeMethods = false;

        ArrayOf<AlarmOperation> operations = await context.Adapter.InspectAsync(
            AlarmTestData.Condition(), CancellationToken.None).ConfigureAwait(false);
        AlarmOperation reset = operations.ToList().Single(
            static operation => operation.Kind == AlarmOperationKind.Reset);
        Assert.That(reset.Availability, Is.EqualTo(AlarmAvailability.Unsupported));
        Assert.That(reset.CanInvoke, Is.False);

        await Assert.ThatAsync(
            async () => await context.Adapter.ExecuteAsync(
                new AlarmCommand(AlarmTestData.Condition(), AlarmOperationKind.Reset), CancellationToken.None)
                .ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>().With.Property(nameof(ServiceResultException.StatusCode))
                .EqualTo(StatusCodes.BadNotSupported)).ConfigureAwait(false);
        Assert.That(context.LastCall, Is.Null);
    }

    [Test]
    public async Task WholeConditionMutationRequiresCurrentBranch()
    {
        var context = new CommandContext();
        await using var lifetime = context.ConfigureAwait(false);
        AlarmCondition retainedBranch = AlarmTestData.Condition(branch: 42);

        ArrayOf<AlarmOperation> operations = await context.Adapter.InspectAsync(
            retainedBranch, CancellationToken.None).ConfigureAwait(false);

        AlarmOperation suppress = operations.ToList().Single(
            static operation => operation.Kind == AlarmOperationKind.Suppress);
        Assert.That(suppress.Availability, Is.EqualTo(AlarmAvailability.Unsupported));
        Assert.That(suppress.Reason, Does.Contain("current branch"));
        Assert.That(context.LastCall, Is.Null);
    }

    [Test]
    public async Task DialogResponseUsesTypedProxyAndReportedOptionIndex()
    {
        var context = new CommandContext();
        await using var lifetime = context.ConfigureAwait(false);
        AlarmCondition dialog = AlarmTestData.Condition() with
        {
            Kind = AlarmConditionKind.Dialog,
            DialogActive = true,
            Responses = ["Cancel", "Proceed"]
        };

        await context.Adapter.ExecuteAsync(
            new AlarmCommand(dialog, AlarmOperationKind.Respond, ResponseIndex: 1), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(context.LastCall!.ObjectId, Is.EqualTo(dialog.Key.ConditionId));
        Assert.That(context.LastCall.MethodId, Is.EqualTo(MethodIds.DialogConditionType_Respond));
        Assert.That(context.LastCall.InputArguments[0].TryGetValue(out int index), Is.True);
        Assert.That(index, Is.EqualTo(1));
    }

    [Test]
    public async Task PermissionEvidenceIsNotCachedAcrossCommands()
    {
        var context = new CommandContext();
        await using var lifetime = context.ConfigureAwait(false);
        var command = new AlarmCommand(AlarmTestData.Condition(), AlarmOperationKind.AddComment, "Checked");
        await context.Adapter.ExecuteAsync(command, CancellationToken.None).ConfigureAwait(false);
        context.LastCall = null;
        context.UserExecutable = false;

        await Assert.ThatAsync(
            async () => await context.Adapter.ExecuteAsync(command, CancellationToken.None).ConfigureAwait(false),
            Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);

        Assert.That(context.LastCall, Is.Null);
        context.Session.Verify(session => session.TranslateBrowsePathsToNodeIdsAsync(
            It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public async Task CancellationBeforeDiscoveryDoesNotSendRequests()
    {
        var context = new CommandContext();
        await using var lifetime = context.ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);

        await Assert.ThatAsync(
            async () => await context.Adapter.ExecuteAsync(
                new AlarmCommand(AlarmTestData.Condition(), AlarmOperationKind.Acknowledge), cancellation.Token)
                .ConfigureAwait(false),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        context.Session.Verify(session => session.CallAsync(
            It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.Session.Verify(session => session.TranslateBrowsePathsToNodeIdsAsync(
            It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class CommandContext : IAsyncDisposable
    {
        public CommandContext()
        {
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:test:server");
            namespaces.Append("urn:test:model");
            Session.SetupGet(session => session.MessageContext).Returns(Host.MessageContext);
            Session.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<BrowsePath> paths, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var results = new BrowsePathResult[paths.Count];
                    for (int i = 0; i < paths.Count; i++)
                    {
                        results[i] = ExposeMethods
                            ? new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets =
                                [
                                    new BrowsePathTarget
                                    {
                                        TargetId = i == paths.Count - 1 ? ShelvingId : new NodeId((uint)(1000 + i), 2),
                                        RemainingPathIndex = uint.MaxValue
                                    }
                                ]
                            }
                            : new BrowsePathResult { StatusCode = StatusCodes.BadNodeIdUnknown };
                    }
                    return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                        new TranslateBrowsePathsToNodeIdsResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = results
                        });
                });
            Session.Setup(session => session.ReadAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> reads,
                    CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    var values = new DataValue[reads.Count];
                    for (int i = 0; i < reads.Count; i++)
                    {
                        values[i] = new DataValue(
                            Variant.From(reads[i].AttributeId != Attributes.UserExecutable || UserExecutable),
                            ExposeMethods ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown);
                    }
                    return new ValueTask<ReadResponse>(
                        new ReadResponse { ResponseHeader = new ResponseHeader(), Results = values });
                });
            Session.Setup(session => session.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<CallMethodRequest> requests, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Assert.That(requests, Has.Count.EqualTo(1));
                    LastCall = requests[0];
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }]
                    });
                });
            Adapter = new AlarmCommandAdapter(Session.Object, namespaces, Host.Telemetry);
        }

        public static NodeId ShelvingId => new(900u, 2);

        public ObserveTestHost Host { get; } = new();

        public Mock<ISessionClient> Session { get; } = new(MockBehavior.Strict);

        public AlarmCommandAdapter Adapter { get; }

        public bool UserExecutable { get; set; } = true;

        public bool ExposeMethods { get; set; } = true;

        public CallMethodRequest? LastCall { get; set; }

        public ValueTask DisposeAsync()
        {
            return Host.DisposeAsync();
        }
    }
}
