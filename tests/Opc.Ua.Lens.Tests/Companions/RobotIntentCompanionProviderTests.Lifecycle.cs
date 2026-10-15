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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.RobotIntent;
using UaLens.Plugins.Companions;
using static UaLens.Tests.Companions.RoboticsTaskTestContext;

namespace UaLens.Tests.Companions;

public sealed partial class RobotIntentCompanionProviderTests
{
    [Test]
    public async Task ProgressCallbackRevocationIsRevalidatedBeforeDraftDispatchAsync()
    {
        var test = new RoboticsTaskTestContext();
        CompanionTaskInput prepared = await test.PrepareWaitAsync().ConfigureAwait(false);
        var progress = new ImmediateRoboticsProgress(() => test.Grant = test.Grant with { Revision = "revoked" });
        await Assert.ThatAsync(() => test.Provider.ExecutePreparedAsync(
            test.Context, test.Target, "submit-wait", prepared, progress, CancellationToken.None).AsTask(),
            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        Assert.That(test.Services.Calls, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AuthorityRefusalOrGrantIsExplicitAndReleaseIsNeverAutomaticAsync(bool granted)
    {
        var test = new RoboticsTaskTestContext();
        test.Services.SetValue(test.ControlOwner, Variant.From(NodeId.Null));
        test.Services.CallHandler = (call, _) =>
        {
            Assert.That(call.InputArguments, Is.Empty);
            if (call.MethodId == test.Services.Id(test.Controller + "-RequestControl"))
            {
                NodeId owner = granted ? test.SessionId : new NodeId(900u);
                test.Services.SetValue(test.ControlOwner, Variant.From(owner));
                return new CallMethodResult { OutputArguments = [Variant.From(granted), Variant.From(owner)] };
            }
            Assert.That(call.MethodId, Is.EqualTo(test.Services.Id(test.Controller + "-ReleaseControl")));
            test.Services.SetValue(test.ControlOwner, Variant.From(NodeId.Null));
            return new CallMethodResult();
        };
        CompanionTaskInput request = await test.Provider.PrepareInputAsync(
            test.Context, test.Target, "request-control", [], CancellationToken.None).ConfigureAwait(false);
        CompanionOperationResult result = await test.ExecuteAsync(request, "request-control").ConfigureAwait(false);
        Assert.That(Value(result, "Granted"), Is.EqualTo(Variant.From(granted)));
        Assert.That(result.Summary, Does.Contain(granted ? "not authorization" : "refused"));
        Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
        if (granted)
        {
            CompanionTaskInput release = await test.Provider.PrepareInputAsync(
                test.Context, test.Target, "release-control", [], CancellationToken.None).ConfigureAwait(false);
            Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
            CompanionOperationResult released = await test.ExecuteAsync(release, "release-control")
                .ConfigureAwait(false);
            Assert.That(released.Summary, Does.Contain("explicit authority release"));
            Assert.That(test.Services.Calls, Has.Count.EqualTo(2));
        }
        test.VerifyBorrowed();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancellationPinsSelectedWaitOrMissionAndDoesNotClaimTerminalStateAsync(bool mission)
    {
        var test = new RoboticsTaskTestContext();
        NodeId selected = mission ? test.AddWaitMission() : test.AddWait();
        string operation = mission ? "cancel-mission" : "cancel-intent";
        test.Services.CallHandler = (call, _) =>
        {
            Assert.That(call.InputArguments, Has.Count.EqualTo(2));
            Assert.That(call.InputArguments[0], Is.EqualTo(Variant.From(mission ? "owned-mission" : "owned-wait")));
            Assert.That(call.InputArguments[1], Is.EqualTo(Variant.From((int)StopModeEnum.OnPath)));
            return new CallMethodResult { OutputArguments = [Variant.From(true)] };
        };
        CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(
            test.Context, test.Target, operation,
            [new("operation", Variant.From(selected)), new("stopMode", Variant.From((int)StopModeEnum.OnPath))],
            CancellationToken.None).ConfigureAwait(false);
        CompanionOperationResult result = await test.ExecuteAsync(prepared, operation).ConfigureAwait(false);
        Assert.That(Value(result, "Accepted"), Is.EqualTo(Variant.From(true)));
        Assert.That(result.Summary, Does.Contain("inspect").And.Contain("terminal"));
        Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
        test.VerifyBorrowed();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ChangedOrCompletedSelectedWorkCannotBeCancelledFromAStalePreparationAsync(bool mission)
    {
        var test = new RoboticsTaskTestContext();
        NodeId selected = mission ? test.AddWaitMission() : test.AddWait();
        string operation = mission ? "cancel-mission" : "cancel-intent";
        CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(test.Context, test.Target, operation,
            [new("operation", Variant.From(selected)), new("stopMode", Variant.From((int)StopModeEnum.OnPath))],
            CancellationToken.None).ConfigureAwait(false);
        if (mission)
        {
            test.Missions[selected] = test.Missions[selected] with { ExecutionState = ExecutionStateEnum.Succeeded };
        }
        else
        {
            test.Operations[selected] = test.Operations[selected] with
            {
                ExecutionState = ExecutionStateEnum.Cancelled
            };
        }
        await Assert.ThatAsync(() => test.ExecuteAsync(prepared, operation),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadInvalidState))
            .ConfigureAwait(false);
        Assert.That(test.Services.Calls, Is.Empty);
    }

    [Test]
    public async Task MotionMissionsAndForeignInstancesAreNotCancellationTargetsAsync()
    {
        var test = new RoboticsTaskTestContext();
        NodeId selected = test.AddWaitMission();
        test.Missions[selected].Mission.Steps[0].Intent = new JointMoveIntentDataType();
        await Assert.ThatAsync(() => test.Provider.PrepareInputAsync(test.Context, test.Target, "cancel-mission",
            [new("operation", Variant.From(selected)), new("stopMode", Variant.From((int)StopModeEnum.OnPath))],
            CancellationToken.None).AsTask(),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotSupported))
            .ConfigureAwait(false);
        await Assert.ThatAsync(() => test.Provider.PrepareInputAsync(test.Context, test.Target, "cancel-intent",
            [new("operation", Variant.From(test.Services.Id("foreign"))),
                new("stopMode", Variant.From((int)StopModeEnum.OnPath))], CancellationToken.None).AsTask(),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNodeIdInvalid))
            .ConfigureAwait(false);
        Assert.That(test.Services.Calls, Is.Empty);
    }

    [TestCase(false, "missing")]
    [TestCase(true, "missing")]
    [TestCase(false, "quality")]
    [TestCase(true, "quality")]
    [TestCase(false, "type")]
    [TestCase(true, "unknown")]
    public async Task RuntimeStateMustBeGoodAndDefinedBeforeInspectionOrCancellationAsync(bool mission, string invalid)
    {
        var test = new RoboticsTaskTestContext();
        NodeId selected = mission ? test.AddWaitMission() : test.AddWait();
        StatusCode expected = StatusCodes.BadTypeMismatch;
        if (invalid == "missing")
        {
            expected = StatusCodes.BadNoMatch;
            test.Services.TranslateHandler = (path, _) =>
                path.StartingNode == selected && path.RelativePath.Elements[^1].TargetName.Name == "ExecutionState"
                    ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }
                    : test.Services.Translate(path);
        }
        else
        {
            test.Services.SetValue(test.ExecutionStates[selected], invalid == "type" ? Variant.From("Executing") :
                Variant.From(invalid == "unknown" ? 999 : (int)ExecutionStateEnum.Executing),
                status: invalid == "quality" ? StatusCodes.Uncertain : StatusCodes.Good);
            if (invalid == "quality")
            {
                expected = StatusCodes.Uncertain;
            }
        }
        await Assert.ThatAsync(() => test.Provider.InspectAsync(
            test.Context, test.Target, CancellationToken.None).AsTask(),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(expected))
            .ConfigureAwait(false);
        await Assert.ThatAsync(() => test.Provider.PrepareInputAsync(test.Context, test.Target,
            mission ? "cancel-mission" : "cancel-intent",
            [new("operation", Variant.From(selected)), new("stopMode", Variant.From((int)StopModeEnum.OnPath))],
            CancellationToken.None).AsTask(),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(expected))
            .ConfigureAwait(false);
        Assert.That(test.Services.Calls, Is.Empty);
    }

    [Test]
    public async Task AChildWithTheWrongRuntimeTypeCannotMasqueradeAsAWaitOperationAsync()
    {
        var test = new RoboticsTaskTestContext();
        NodeId selected = test.AddWait();
        test.Services.AddObject(selected, Opc.Ua.ObjectTypeIds.BaseObjectType);
        await Assert.ThatAsync(() => test.Provider.PrepareInputAsync(test.Context, test.Target, "cancel-intent",
            [new("operation", Variant.From(selected)), new("stopMode", Variant.From((int)StopModeEnum.OnPath))],
            CancellationToken.None).AsTask(),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadTypeMismatch))
            .ConfigureAwait(false);
        Assert.That(test.Services.Calls, Is.Empty);
    }

    [TestCase(0u, false)]
    [TestCase(1u, true)]
    [TestCase(8u, true)]
    [TestCase(9u, false)]
    public async Task MissionStepBoundsIncludeBothNearestValidEdgesAsync(uint count, bool valid)
    {
        var test = new RoboticsTaskTestContext();
        ValueTask<CompanionTaskInput> preparing = test.Provider.PrepareInputAsync(test.Context, test.Target,
            "preview-wait-mission", [.. WaitInputs(1250), new("count", Variant.From(count))], CancellationToken.None);
        if (!valid)
        {
            await Assert.ThatAsync(() => preparing.AsTask(), Throws.ArgumentException).ConfigureAwait(false);
        }
        else
        {
            CompanionTaskInput prepared = await preparing.ConfigureAwait(false);
            CompanionOperationResult preview = await test.ExecuteAsync(prepared, "preview-wait-mission")
                .ConfigureAwait(false);
            Assert.That(Value(preview, "Mission preview").TryGetValue<MissionDataType>(
                out MissionDataType? value, test.Services.MessageContext), Is.True);
            Assert.That(value!.Steps, Has.Count.EqualTo(count));
        }
        Assert.That(test.Services.Calls, Is.Empty);
    }

    [Test]
    public async Task BrokerLeaseIsFiniteAndNeverConnectsPublishesOrRenewsAsync()
    {
        var test = new RoboticsTaskTestContext();
        DateTimeOffset expiry = test.Clock.Now.AddSeconds(1);
        test.Services.CallHandler = (call, _) =>
        {
            Assert.That(call.MethodId, Is.EqualTo(test.Services.Id(test.Controller + "-OpenRealTimeChannel")));
            Assert.That(call.InputArguments[0], Is.EqualTo(Variant.From("joint-telemetry")));
            Assert.That(call.InputArguments[1], Is.EqualTo(Variant.From(1000d)));
            test.Services.SetValue(test.LeaseHolder, Variant.From(test.SessionId));
            test.Services.SetValue(test.LeaseExpiry, Variant.From(new DateTimeUtc(expiry.UtcDateTime)));
            return new CallMethodResult
            {
                OutputArguments = [Variant.From(true), Variant.From("opc.udp://239.0.0.40:4840"),
                    Variant.From("telemetry descriptor"), Variant.From(new DateTimeUtc(expiry.UtcDateTime)),
                    Variant.From(LocalizedText.Null)]
            };
        };
        CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(test.Context, test.Target, "open-channel",
            [new("channel", Variant.From(test.LeaseChannel)), new("lease", Variant.From(1000d))],
            CancellationToken.None)
            .ConfigureAwait(false);
        CompanionOperationResult result = await test.ExecuteAsync(prepared, "open-channel").ConfigureAwait(false);
        Assert.That(Value(result, "Granted"), Is.EqualTo(Variant.From(true)));
        Assert.That(Value(result, "Lease expiry"), Is.EqualTo(Variant.From(new DateTimeUtc(expiry.UtcDateTime))));
        Assert.That(result.Summary, Does.Contain("No transport").And.Contain("no data").And.Contain("no renewal"));
        test.Clock.Now = expiry.AddTicks(1);
        CompanionInspection inspection = await test.Provider.InspectAsync(
            test.Context, test.Target, CancellationToken.None)
            .ConfigureAwait(false);
        Assert.That(inspection.Values.ToList()
            .Single(field => field.Name.EndsWith("/ LeaseExpiry", StringComparison.Ordinal))
            .Value, Is.EqualTo(Variant.From(new DateTimeUtc(expiry.UtcDateTime))));
        Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
        test.VerifyBorrowed();
    }

    [TestCase(-1L)]
    [TestCase(0L)]
    [TestCase(1L)]
    public async Task BrokerCloseChecksTheExactExpiryBoundaryAsync(long ticksAfterExpiry)
    {
        var test = new RoboticsTaskTestContext();
        DateTimeOffset expiry = test.Clock.Now.AddSeconds(1);
        test.Services.SetValue(test.LeaseHolder, Variant.From(test.SessionId));
        test.Services.SetValue(test.LeaseExpiry, Variant.From(new DateTimeUtc(expiry.UtcDateTime)));
        CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(test.Context, test.Target, "close-channel",
            [new("channel", Variant.From(test.LeaseChannel))], CancellationToken.None).ConfigureAwait(false);
        test.Clock.Now = expiry.AddTicks(ticksAfterExpiry);
        if (ticksAfterExpiry < 0)
        {
            test.Services.CallHandler = (call, _) =>
            {
                Assert.That(call.InputArguments[0], Is.EqualTo(Variant.From("joint-telemetry")));
                return new CallMethodResult { OutputArguments = [Variant.From(true)] };
            };
            CompanionOperationResult result = await test.ExecuteAsync(prepared, "close-channel").ConfigureAwait(false);
            Assert.That(Value(result, "Closed"), Is.EqualTo(Variant.From(true)));
            Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
        }
        else
        {
            await Assert.ThatAsync(() => test.ExecuteAsync(prepared, "close-channel"),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            Assert.That(test.Services.Calls, Is.Empty);
        }
    }

    [TestCase(99d)]
    [TestCase(10000.01d)]
    [TestCase(double.NaN)]
    public async Task InvalidBrokerLeaseBoundsNeverCallTheServerAsync(double lease)
    {
        var test = new RoboticsTaskTestContext();
        await Assert.ThatAsync(() => test.Provider.PrepareInputAsync(test.Context, test.Target, "open-channel",
            [new("channel", Variant.From(test.LeaseChannel)), new("lease", Variant.From(lease))],
            CancellationToken.None).AsTask(), Throws.ArgumentException).ConfigureAwait(false);
        Assert.That(test.Services.Calls, Is.Empty);
    }

    [Test]
    public async Task OptionalBrokerExpiryAbsenceUsesServerCloseOutcomeWithoutInventingALocalLeaseAsync()
    {
        var test = new RoboticsTaskTestContext();
        test.Services.TranslateHandler = (path, _) =>
            path.StartingNode == test.LeaseChannel &&
                path.RelativePath.Elements[^1].TargetName.Name == "LeaseExpiry"
                ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }
                : test.Services.Translate(path);
        test.Services.SetValue(test.LeaseHolder, Variant.From(test.SessionId));
        CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(test.Context, test.Target, "close-channel",
            [new("channel", Variant.From(test.LeaseChannel))], CancellationToken.None).ConfigureAwait(false);
        Assert.That(prepared.Review, Does.Contain("only the server can decide"));
        test.Services.CallHandler = (_, _) => new CallMethodResult { OutputArguments = [Variant.From(false)] };
        CompanionOperationResult result = await test.ExecuteAsync(prepared, "close-channel").ConfigureAwait(false);
        Assert.That(Value(result, "Closed"), Is.EqualTo(Variant.From(false)));
        Assert.That(result.Summary, Does.Contain("refused"));
        Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
        test.VerifyBorrowed();
    }

    [Test]
    public async Task CancellationBeforeDispatchConsumesPreparationWithoutCallingTheServerAsync()
    {
        var test = new RoboticsTaskTestContext();
        CompanionTaskInput prepared = await test.PrepareWaitAsync().ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThatAsync(() => test.ExecuteAsync(prepared, token: cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        await Assert.ThatAsync(() => test.ExecuteAsync(prepared), Throws.InvalidOperationException)
            .ConfigureAwait(false);
        Assert.That(test.Services.Calls, Is.Empty);
    }
}
