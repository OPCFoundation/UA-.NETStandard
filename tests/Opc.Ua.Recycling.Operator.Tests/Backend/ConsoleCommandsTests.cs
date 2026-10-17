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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Recycling.OperatorConsole;

namespace Opc.Ua.Recycling.Operator.Tests.Backend;

/// <summary>
/// Focused regression tests for the bounded workstation command ledger.
/// </summary>
[TestFixture]
public sealed class ConsoleCommandsTests
{
    /// <summary>
    /// Observer configuration cannot acquire server mutation authority through a browser command.
    /// </summary>
    [Test]
    public async Task ObserverRejectsMutationBeforeCallingCoordinator()
    {
        Mock<IOperatorPlantFeed> feed = Feed();
        var commands = new ConsoleCommands(feed.Object, new ConsoleOptions { Role = "Observer", LineId = "line" });
        var result = await commands.SubmitAsync(Request("Pause"), CancellationToken.None).ConfigureAwait(false);
        Assert.That(result.Status, Is.EqualTo(403));
        feed.Verify(item => item.ExecuteAsync(
            It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Membership verbs are never browser accessible, even under the maximum local ceiling.
    /// </summary>
    [TestCase("Register")]
    [TestCase("Initialize")]
    [TestCase("Heartbeat")]
    [TestCase("Disconnect")]
    public async Task AdministratorCannotInvokeMembership(string kind)
    {
        Mock<IOperatorPlantFeed> feed = Feed();
        var commands = Administrator(feed);
        var result = await commands.SubmitAsync(Request(kind), CancellationToken.None).ConfigureAwait(false);
        Assert.That(result.Status, Is.EqualTo(400));
        feed.Verify(item => item.ExecuteAsync(
            It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A future revision cannot be submitted against an older browser feed.
    /// </summary>
    [Test]
    public async Task FutureRevisionIsVisibleAndDoesNotCallCoordinator()
    {
        Mock<IOperatorPlantFeed> feed = Feed();
        var commands = Administrator(feed);
        var result = await commands.SubmitAsync(Request("Pause") with { ExpectedRevision = "8" },
            CancellationToken.None).ConfigureAwait(false);
        Assert.That(result.Status, Is.EqualTo(409));
        Assert.That(result.Error, Does.Contain("Stale"));
        feed.Verify(item => item.ExecuteAsync(
            It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Only the authoritative coordinator can distinguish operational changes from lease-only revisions.
    /// </summary>
    [Test]
    public async Task OlderRevisionIsForwardedExactlyOnceWithoutRewritingAsync()
    {
        Mock<IOperatorPlantFeed> feed = Feed();
        feed.Setup(item => item.ExecuteAsync(It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromException<RecyclingCommandOutcome>(
                new ServiceResultException(StatusCodes.BadInvalidState)));
        var commands = Administrator(feed);
        var result = await commands.SubmitAsync(Request("Pause") with { ExpectedRevision = "6" },
            CancellationToken.None).ConfigureAwait(false);
        Assert.That(result.Result!.State, Is.EqualTo("Rejected"));
        Assert.That(result.Result.Status, Is.EqualTo("BadInvalidState"));
        feed.Verify(item => item.ExecuteAsync(
            It.Is<RecyclingCommand>(command => command.ExpectedRevision == 6),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Destructive operations require explicit acknowledgement before any transport side effect.
    /// </summary>
    [TestCase("Stop", "", "", "0")]
    [TestCase("Reset", "", "", "0")]
    [TestCase("DisableProcess", "Sorting", "", "0")]
    [TestCase("DisposeHold", "", "Rejected", "12")]
    public async Task DestructiveCommandRequiresConfirmation(
        string kind, string process, string disposition, string holdId)
    {
        Mock<IOperatorPlantFeed> feed = Feed();
        var commands = Administrator(feed);
        CommandRequest request = Request(kind) with { Process = process, Disposition = disposition, HoldId = holdId };
        var result = await commands.SubmitAsync(request, CancellationToken.None).ConfigureAwait(false);
        Assert.That(result.Status, Is.EqualTo(400));
        Assert.That(result.Error, Does.Contain("confirmation"));
        feed.Verify(item => item.ExecuteAsync(
            It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A transport timeout remains unknown until an explicitly requested exact-envelope retry.
    /// </summary>
    [Test]
    public async Task UnknownOutcomeRetryUsesTheExactOriginalCoordinatorEnvelope()
    {
        Mock<IOperatorPlantFeed> feed = Feed();
        RecyclingCommand? first = null;
        RecyclingCommand? second = null;
        int calls = 0;
        feed.Setup(item => item.ExecuteAsync(It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()))
            .Returns((RecyclingCommand command, CancellationToken _) =>
            {
                if (++calls == 1)
                {
                    first = command;
                    return ValueTask.FromException<RecyclingCommandOutcome>(new IOException("Connection lost."));
                }
                second = command;
                return ValueTask.FromResult(new RecyclingCommandOutcome(command.RequestId, "run", 8, 4,
                    StatusCodes.Good.Code, "Paused."));
            });
        var commands = Administrator(feed);
        CommandRequest request = Request("Pause");
        var unknown = await commands.SubmitAsync(request, CancellationToken.None).ConfigureAwait(false);
        Assert.That(unknown.Result!.State, Is.EqualTo("Unknown"));
        Assert.That(calls, Is.EqualTo(1));
        var duplicate = await commands.SubmitAsync(request, CancellationToken.None).ConfigureAwait(false);
        Assert.That(duplicate.Result!.State, Is.EqualTo("Unknown"));
        Assert.That(calls, Is.EqualTo(1), "Repeated clicks must not silently reissue an ambiguous mutation.");
        var retried = await commands.SubmitAsync(request with { Retry = true }, CancellationToken.None)
            .ConfigureAwait(false);
        Assert.That(retried.Result!.State, Is.EqualTo("Accepted"));
        Assert.That(second, Is.SameAs(first), "The original transport object must be retained, not reconstructed.");
        Assert.That(second!.ExpectedRevision, Is.EqualTo(7));
        Assert.That(second.RequestId, Is.EqualTo(request.RequestId));
    }

    /// <summary>
    /// Generic bad responses can follow a durable commit and must preserve an exact-envelope recovery path.
    /// </summary>
    [TestCase("BadUnexpectedError")]
    [TestCase("BadInternalError")]
    [TestCase("BadResponseTooLarge")]
    public async Task AmbiguousServerFailureRemainsUnknownAndAllowsExactRetryAsync(string symbolicStatus)
    {
        StatusCode status = symbolicStatus switch
        {
            "BadUnexpectedError" => StatusCodes.BadUnexpectedError,
            "BadInternalError" => StatusCodes.BadInternalError,
            _ => StatusCodes.BadResponseTooLarge
        };
        Mock<IOperatorPlantFeed> feed = Feed();
        RecyclingCommand? committed = null;
        int calls = 0;
        feed.Setup(item => item.ExecuteAsync(It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()))
            .Returns((RecyclingCommand command, CancellationToken _) =>
            {
                if (++calls == 1)
                {
                    committed = command;
                    return ValueTask.FromException<RecyclingCommandOutcome>(new ServiceResultException(status));
                }
                Assert.That(command, Is.SameAs(committed));
                return ValueTask.FromResult(new RecyclingCommandOutcome(
                    command.RequestId, "run", 8, 4, StatusCodes.Good.Code, "Original durable outcome."));
            });
        var commands = Administrator(feed);
        CommandRequest request = Request("Checkpoint");
        var ambiguous = await commands.SubmitAsync(request, CancellationToken.None).ConfigureAwait(false);
        Assert.That(ambiguous.Result!.State, Is.EqualTo("Unknown"));
        Assert.That(ambiguous.Result.Status, Is.EqualTo(symbolicStatus));
        var duplicate = await commands.SubmitAsync(request, CancellationToken.None).ConfigureAwait(false);
        Assert.That(duplicate.Result!.State, Is.EqualTo("Unknown"));
        Assert.That(calls, Is.EqualTo(1));
        var recovered = await commands.SubmitAsync(request with { Retry = true }, CancellationToken.None)
            .ConfigureAwait(false);
        Assert.That(recovered.Result!.State, Is.EqualTo("Accepted"));
        Assert.That(recovered.Result.Revision, Is.EqualTo("8"));
        Assert.That(calls, Is.EqualTo(2));
    }

    /// <summary>
    /// Reusing an ID with a changed command cannot mutate the original pending action.
    /// </summary>
    [Test]
    public async Task PendingDuplicateIsCoalescedAndChangedEnvelopeIsRejected()
    {
        Mock<IOperatorPlantFeed> feed = Feed();
        var completion = new TaskCompletionSource<RecyclingCommandOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        feed.Setup(item => item.ExecuteAsync(It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask<RecyclingCommandOutcome>(completion.Task));
        var commands = Administrator(feed);
        CommandRequest request = Request("Pause");
        Task<(CommandResult? Result, int Status, string Error)> pending = commands.SubmitAsync(
            request, CancellationToken.None);
        var duplicate = await commands.SubmitAsync(request, CancellationToken.None).ConfigureAwait(false);
        Assert.That(duplicate.Result!.State, Is.EqualTo("Pending"));
        var changed = await commands.SubmitAsync(request with { Kind = "Resume", Retry = true },
            CancellationToken.None).ConfigureAwait(false);
        Assert.That(changed.Status, Is.EqualTo(409));
        completion.SetResult(new RecyclingCommandOutcome(
            request.RequestId, "run", 8, 4, StatusCodes.Good.Code, "Paused."));
        Assert.That((await pending.ConfigureAwait(false)).Result!.State, Is.EqualTo("Accepted"));
        feed.Verify(item => item.ExecuteAsync(It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Explicit domain rejections are not mislabeled as ambiguous transport outcomes.
    /// </summary>
    [TestCase("BadInvalidState")]
    [TestCase("BadOutOfRange")]
    [TestCase("BadUserAccessDenied")]
    public async Task ServerDomainFailureIsRejectedAndPreservesItsStatusAsync(string symbolicStatus)
    {
        StatusCode status = symbolicStatus switch
        {
            "BadInvalidState" => StatusCodes.BadInvalidState,
            "BadOutOfRange" => StatusCodes.BadOutOfRange,
            _ => StatusCodes.BadUserAccessDenied
        };
        Mock<IOperatorPlantFeed> feed = Feed();
        feed.Setup(item => item.ExecuteAsync(It.IsAny<RecyclingCommand>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromException<RecyclingCommandOutcome>(new ServiceResultException(status)));
        var commands = Administrator(feed);
        var result = await commands.SubmitAsync(Request("Pause"), CancellationToken.None).ConfigureAwait(false);
        Assert.That(result.Result!.State, Is.EqualTo("Rejected"));
        Assert.That(result.Result.Status, Is.EqualTo(symbolicStatus));
    }

    /// <summary>
    /// One fixture request is run/revision scoped and never contains process credentials.
    /// </summary>
    private static CommandRequest Request(string kind) => new(
        Guid.NewGuid().ToString("D"), "run", "7", kind);

    /// <summary>
    /// Creates the maximum permitted host ceiling for command validation tests.
    /// </summary>
    private static ConsoleCommands Administrator(Mock<IOperatorPlantFeed> feed) => new(
        feed.Object, new ConsoleOptions { Role = "SimulatorAdministrator", LineId = "line" },
        Mock.Of<ITelemetryContext>(context => context.LoggerFactory == NullLoggerFactory.Instance));

    /// <summary>
    /// Creates a coherent transport substitute without introducing a runtime simulation mode.
    /// </summary>
    private static Mock<IOperatorPlantFeed> Feed()
    {
        var feed = new Mock<IOperatorPlantFeed>();
        feed.SetupGet(item => item.Snapshot).Returns(new CommittedSnapshot
        {
            RunId = "run", Revision = 7, Tick = 4, Lifecycle = RunLifecycle.Paused
        });
        feed.SetupGet(item => item.State).Returns(new ConsoleState(
            "Connected", string.Empty, true, DateTime.UtcNow, DateTime.UtcNow, "Good", "line", "run", "test", "hash",
            "7", "4", "4", "Paused", string.Empty, new SummaryDto("0", "0", "0", "0", "0", 0, 0), [], [], [], []));
        return feed;
    }
}
