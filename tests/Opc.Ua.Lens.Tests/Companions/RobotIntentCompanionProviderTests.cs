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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.RobotIntent;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using UaLens.Tests.Observe;
using RI = Opc.Ua.RobotIntent;
using static UaLens.Tests.Companions.RoboticsTaskTestContext;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed partial class RobotIntentCompanionProviderTests
{
    [Test]
    public async Task DiscoveryAndInspectionKeepDraftDistinctAndDoNotAcquireAuthorityAsync()
    {
        var test = new RoboticsTaskTestContext();
        NodeId wait = test.AddWait();
        NodeId mission = test.AddWaitMission();
        ArrayOf<CompanionTarget> targets = await test.Provider.DiscoverAsync(test.Context, CancellationToken.None)
            .ConfigureAwait(false);
        CompanionInspection inspection = await test.Provider.InspectAsync(
            test.Context, test.Target, CancellationToken.None)
            .ConfigureAwait(false);
        Assert.That(targets, Has.Count.EqualTo(1));
        Assert.That(targets[0].NodeId, Is.EqualTo(test.Controller));
        Assert.That(targets[0].TypeName, Is.EqualTo("DraftIntentController"));
        Assert.That(test.Provider.Descriptor.Maturity, Does.Contain("DRAFT").And.Contain("not published OPC 40010"));
        Assert.That(test.Provider.Descriptor.ModelUri,
            Is.Not.EqualTo(new RoboticsCompanionProvider().Descriptor.ModelUri));
        Assert.That(inspection.Values.ToList().Single(field => field.Name == "Intent operation").Value,
            Is.EqualTo(Variant.From(wait)));
        Assert.That(inspection.Values.ToList().Single(field => field.Name == "Mission operation").Value,
            Is.EqualTo(Variant.From(mission)));
        Assert.That(inspection.Values.ToList().Single(field => field.Name == "Intent owned-wait").SourceNodeId,
            Is.EqualTo(test.ExecutionStates[wait]));
        Assert.That(inspection.Values.ToList().Single(field => field.Name == "Mission owned-mission").SourceNodeId,
            Is.EqualTo(test.ExecutionStates[mission]));
        Assert.That(inspection.Operations.ToList().Single(operation => operation.Id == "submit-wait").Safety,
            Is.EqualTo(CompanionOperationSafety.DeploymentMutation));
        Assert.That(inspection.Summary, Does.Contain("no server Preview").And.Contain("arbitration"));
        Assert.That(test.Services.Calls, Is.Empty);
        test.VerifyBorrowed();
    }

    [Test]
    public async Task EmptyControllerInspectionDoesNotConstructAStreamingTransportAsync()
    {
        var test = new RoboticsTaskTestContext();
        var provider = new RobotIntentCompanionProvider(test.Policy.Object, test.Clock, (_, _) =>
            throw new AssertionException("An empty controller does not need an operation stream."));
        CompanionInspection inspection = await provider.InspectAsync(
            test.Context, test.Target, CancellationToken.None).ConfigureAwait(false);
        Assert.That(inspection.Values.ToList().Single(value => value.Name == "ControlOwner").Value,
            Is.EqualTo(Variant.From(test.SessionId)));
        Assert.That(test.Services.Calls, Is.Empty);
    }

    [Test]
    public async Task RestoredDraftDocumentDropsConfirmationAndDoesNotCreateTransportOrStartWorkAsync()
    {
        var host = new ObserveTestHost();
        await using (host.ConfigureAwait(false))
        {
            int transports = 0;
            var provider = new RobotIntentCompanionProvider(createTransport: (_, _) =>
            {
                transports++;
                throw new InvalidOperationException("Restore must stay passive.");
            });
            var workspace = new CompanionWorkspace([provider], host.Telemetry);
            var document = new CompanionPlugin(host.Host, workspace)
            {
                ConfirmLocalSample = true,
                OperationInput = "never-restore-a-command"
            };
            await using (document.ConfigureAwait(false))
            {
                JsonElement state = JsonSerializer.SerializeToElement(
                    new CompanionDocumentState(1, "robot-intent", "nsu=urn:owned;i=1"),
                    CompanionJsonContext.Default.CompanionDocumentState);
                await document.RestoreStateAsync(state).ConfigureAwait(false);
                Assert.That(transports, Is.Zero);
                Assert.That(document.SelectedProvider?.Id, Is.EqualTo("robot-intent"));
                Assert.That(document.Operations, Is.Empty);
                Assert.That(document.Targets, Is.Empty);
                Assert.That(document.ConfirmLocalSample, Is.False);
                Assert.That(document.OperationInput, Is.Empty);
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LocalPreviewCopiesFiniteNonblockingWaitOnlyDataWithoutAdmissionAsync(bool mission)
    {
        var test = new RoboticsTaskTestContext();
        string operation = mission ? "preview-wait-mission" : "preview-wait";
        ArrayOf<CompanionValue> values = mission
            ? [.. WaitInputs(), new("count", Variant.From(2u))]
            : WaitInputs();
        CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(
            test.Context, test.Target, operation, values, CancellationToken.None).ConfigureAwait(false);
        CompanionOperationResult result = await test.ExecuteAsync(prepared, operation).ConfigureAwait(false);
        if (mission)
        {
            Assert.That(Value(result, "Mission preview").TryGetValue<MissionDataType>(
                out MissionDataType? preview, test.Services.MessageContext), Is.True);
            Assert.That(preview, Is.Not.Null);
            Assert.That(preview!.Steps, Has.Count.EqualTo(2));
            Assert.That(preview.Transitions, Is.Empty);
            Assert.That(preview.Steps.ToList().All(step => step.Released &&
                step.Intent is WaitIntentDataType
                {
                    Duration: 1000, BufferMode: BufferModeEnum.Buffered,
                    BlockingMode: BlockingModeEnum.None, Signal.IsNull: true
                }), Is.True);
        }
        else
        {
            Assert.That(Value(result, "Intent preview").TryGetValue<WaitIntentDataType>(
                out WaitIntentDataType? preview, test.Services.MessageContext), Is.True);
            Assert.That(preview, Is.Not.Null);
            Assert.That(preview!.Duration, Is.EqualTo(1000));
            Assert.That(preview.IntentId, Is.EqualTo("owned-wait"));
            Assert.That(preview.Signal.IsNull, Is.True);
            Assert.That(preview.BufferMode, Is.EqualTo(BufferModeEnum.Buffered));
            Assert.That(preview.BlockingMode, Is.EqualTo(BlockingModeEnum.None));
        }
        Assert.That(result.Summary, Does.Contain("No method was called").And.Contain("not a path/safety check"));
        Assert.That(test.Services.Calls, Is.Empty);
        test.VerifyBorrowed();
    }

    [TestCase(1d)]
    [TestCase(10000d)]
    public async Task FiniteWaitInclusiveDurationBoundariesArePreviewableAsync(double duration)
    {
        var test = new RoboticsTaskTestContext();
        CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(
            test.Context, test.Target, "preview-wait", WaitInputs(duration), CancellationToken.None)
            .ConfigureAwait(false);
        CompanionOperationResult result = await test.ExecuteAsync(prepared, "preview-wait").ConfigureAwait(false);
        Assert.That(Value(result, "Intent preview").TryGetValue<WaitIntentDataType>(
            out WaitIntentDataType? wait, test.Services.MessageContext), Is.True);
        Assert.That(wait!.Duration, Is.EqualTo(duration));
        Assert.That(test.Services.Calls, Is.Empty);
    }

    [TestCase(0d)]
    [TestCase(-1d)]
    [TestCase(10000.001d)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public async Task UnboundedInvalidWaitCannotReachMetadataOrDispatchAsync(double duration)
    {
        var test = new RoboticsTaskTestContext();
        await Assert.ThatAsync(() => test.Provider.PrepareInputAsync(
            test.Context, test.Target, "submit-wait", WaitInputs(duration), CancellationToken.None).AsTask(),
            Throws.ArgumentException).ConfigureAwait(false);
        Assert.That(test.Services.BrowseCalls, Is.Empty);
        Assert.That(test.Services.Calls, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AdmissionIsCorrelatedAndNeverReportedAsObservedCompletionAsync(bool mission)
    {
        var test = new RoboticsTaskTestContext();
        string operation = mission ? "submit-wait-mission" : "submit-wait";
        test.Services.CallHandler = (call, _) =>
        {
            Assert.That(call.ObjectId, Is.EqualTo(test.Controller));
            if (mission)
            {
                Assert.That(call.InputArguments[0].TryGetValue<MissionDataType>(
                    out MissionDataType? value, test.Services.MessageContext), Is.True);
                Assert.That(value!.Steps, Has.Count.EqualTo(2));
                Assert.That(value.Steps.ToList().All(step => step.Intent is WaitIntentDataType), Is.True);
            }
            else
            {
                Assert.That(call.MethodId, Is.EqualTo(test.Submit));
                Assert.That(call.InputArguments[0].TryGetValue<WaitIntentDataType>(
                    out WaitIntentDataType? value, test.Services.MessageContext), Is.True);
                Assert.That(value!.Duration, Is.EqualTo(1000));
                Assert.That(value.Signal.IsNull, Is.True);
            }
            return Accepted(test);
        };
        CompanionTaskInput prepared = await test.Provider.PrepareInputAsync(test.Context, test.Target, operation,
            mission ? [.. WaitInputs(), new("count", Variant.From(2u))] : WaitInputs(), CancellationToken.None)
            .ConfigureAwait(false);
        Assert.That(test.Services.Calls, Is.Empty);
        CompanionOperationResult result = await test.ExecuteAsync(prepared, operation).ConfigureAwait(false);
        Assert.That(Value(result, "Accepted"), Is.EqualTo(Variant.From(true)));
        Assert.That(Value(result, "Operation"), Is.EqualTo(Variant.From(test.Services.Id("admitted"))));
        Assert.That(result.Summary, Does.Contain("Admission is not completion"));
        Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
        await Assert.ThatAsync(() => test.ExecuteAsync(prepared, operation),
            Throws.InvalidOperationException).ConfigureAwait(false);
        Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
        test.VerifyBorrowed();
    }

    [Test]
    public async Task RefusalIsNotCollapsedIntoSuccessfulAdmissionAsync()
    {
        var test = new RoboticsTaskTestContext();
        test.Services.CallHandler = (_, _) => new CallMethodResult
        {
            OutputArguments = [Variant.From(false), Variant.From("owned-wait"), Variant.From(NodeId.Null),
                Variant.From((int)IntentFailureEnum.NotPermittedInMode), Variant.From(new LocalizedText("not ready"))]
        };
        CompanionTaskInput prepared = await test.PrepareWaitAsync().ConfigureAwait(false);
        CompanionOperationResult result = await test.ExecuteAsync(prepared).ConfigureAwait(false);
        Assert.That(Value(result, "Accepted"), Is.EqualTo(Variant.From(false)));
        Assert.That(Value(result, "Failure"), Is.EqualTo(Variant.From("NotPermittedInMode")));
        Assert.That(result.Summary, Does.Contain("refused"));
        Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
    }

    [TestCase("identity")]
    [TestCase("peer")]
    [TestCase("namespace")]
    [TestCase("grant")]
    [TestCase("expiry")]
    [TestCase("authority")]
    [TestCase("ready")]
    [TestCase("mode")]
    [TestCase("permission")]
    [TestCase("method")]
    public async Task ChangedPreparedEvidencePreventsDispatchAndConsumesThePreparationAsync(string change)
    {
        var test = new RoboticsTaskTestContext();
        CompanionTaskInput prepared = await test.PrepareWaitAsync().ConfigureAwait(false);
        bool serviceError = false;
        switch (change)
        {
            case "identity":
                test.Services.Session.SetupGet(session => session.Identity)
                    .Returns(new UserIdentity(new UserNameIdentityToken { UserName = "different-operator" }));
                break;
            case "peer":
                test.Endpoint.ServerCertificate = ByteString.From("another-test-peer"u8);
                break;
            case "namespace":
                test.Services.NamespaceUris.GetIndexOrAppend("urn:changed");
                break;
            case "grant":
                test.Grant = test.Grant with { Revision = "revoked" };
                break;
            case "expiry":
                test.Clock.Now = test.Grant.ExpiresAt;
                break;
            case "authority":
                test.Services.SetValue(test.ControlOwner, Variant.From(new NodeId(900u)));
                break;
            case "ready":
                test.Services.SetValue(test.Ready, Variant.From(false));
                serviceError = true;
                break;
            case "mode":
                test.Services.SetValue(test.Mode, Variant.From((int)OperationalModeEnum.ManualReducedSpeed));
                serviceError = true;
                break;
            case "permission":
                test.Services.SetValue(test.Submit, Variant.From(false), Attributes.UserExecutable);
                serviceError = true;
                break;
            case "method":
                NodeId replacement = test.AddMethod(test.Controller, RI.Namespaces.RobotIntent, "Replacement",
                    [RI.DataTypeIds.IntentDataType], [Opc.Ua.DataTypeIds.Boolean, Opc.Ua.DataTypeIds.String,
                        Opc.Ua.DataTypeIds.NodeId, RI.DataTypeIds.IntentFailureEnum, Opc.Ua.DataTypeIds.LocalizedText]);
                test.Services.AddChild(test.Controller, replacement, RI.Namespaces.RobotIntent, "SubmitIntent");
                serviceError = true;
                break;
            default:
                throw new AssertionException("Missing evidence partition.");
        }
        await Assert.ThatAsync(() => test.ExecuteAsync(prepared),
            Throws.TypeOf(serviceError ? typeof(ServiceResultException) : typeof(UnauthorizedAccessException)))
            .ConfigureAwait(false);
        await Assert.ThatAsync(() => test.ExecuteAsync(prepared), Throws.InvalidOperationException)
            .ConfigureAwait(false);
        Assert.That(test.Services.Calls, Is.Empty);
    }

    [Test]
    public async Task RebindingAnIdenticalSessionIdStillInvalidatesTheOriginalPreparationAsync()
    {
        var original = new RoboticsTaskTestContext();
        var replacement = new RoboticsTaskTestContext();
        CompanionTaskInput prepared = await original.PrepareWaitAsync().ConfigureAwait(false);
        Assert.That(original.SessionId, Is.EqualTo(replacement.SessionId));
        await Assert.ThatAsync(() => original.Provider.ExecutePreparedAsync(
            replacement.Context, original.Target, "submit-wait", prepared, null, CancellationToken.None).AsTask(),
            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        Assert.That(original.Services.Calls, Is.Empty);
        Assert.That(replacement.Services.Calls, Is.Empty);
        original.VerifyBorrowed();
        replacement.VerifyBorrowed();
    }

    [TestCase("timeout")]
    [TestCase("uncertain")]
    [TestCase("missing")]
    [TestCase("wrong-type")]
    [TestCase("wrong-correlation")]
    [TestCase("cancellation")]
    public async Task UnknownOrMalformedOutcomeHasExactlyOneDispatchAndNeverReplaysAsync(string outcome)
    {
        var test = new RoboticsTaskTestContext();
        using var cancellation = new CancellationTokenSource();
        test.Services.CallHandler = (_, _) =>
        {
            if (outcome == "timeout")
            {
                throw new TimeoutException("Unknown remote outcome");
            }
            if (outcome == "cancellation")
            {
                cancellation.Cancel();
                return Accepted(test);
            }
            if (outcome == "uncertain")
            {
                return new CallMethodResult { StatusCode = StatusCodes.Uncertain };
            }
            if (outcome == "missing")
            {
                return new CallMethodResult();
            }
            CallMethodResult result = Accepted(test);
            result.OutputArguments = outcome == "wrong-type"
                ? [Variant.From("true"), .. result.OutputArguments.ToList().Skip(1)]
                : [result.OutputArguments[0], Variant.From("another-operation"),
                    .. result.OutputArguments.ToList().Skip(2)];
            return result;
        };
        CompanionTaskInput prepared = await test.PrepareWaitAsync().ConfigureAwait(false);
        await Assert.ThatAsync(() => test.ExecuteAsync(prepared, token: cancellation.Token),
            Throws.InstanceOf(outcome == "timeout" ? typeof(TimeoutException) :
                outcome == "cancellation" ? typeof(OperationCanceledException) : typeof(ServiceResultException)))
            .ConfigureAwait(false);
        await Assert.ThatAsync(() => test.ExecuteAsync(prepared), Throws.InvalidOperationException)
            .ConfigureAwait(false);
        Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
        test.VerifyBorrowed();
    }

    [Test]
    public async Task AnonymousAndConfiguredDeploymentRulesCannotAuthorizeRobotMutationAsync()
    {
        var test = new RoboticsTaskTestContext();
        test.Services.Session.SetupGet(session => session.Identity).Returns(new UserIdentity());
        await Assert.ThatAsync(() => test.PrepareWaitAsync().AsTask(),
            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        var configured = new RobotIntentCompanionProvider(new ConfiguredCompanionDeploymentPolicy());
        await Assert.ThatAsync(() => configured.PrepareInputAsync(
            test.Context, test.Target, "request-control", [], CancellationToken.None).AsTask(),
            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        Assert.That(test.Services.Calls, Is.Empty);
        test.VerifyBorrowed();
    }

    [TestCase("move")]
    [TestCase("retry")]
    [TestCase("cancel-all")]
    public async Task UnsupportedDraftActuationAndRetryCannotDispatchAsync(string operation)
    {
        var test = new RoboticsTaskTestContext();
        await Assert.ThatAsync(() => test.Provider.PrepareInputAsync(
            test.Context, test.Target, operation, [], CancellationToken.None).AsTask(),
            Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotSupported))
            .ConfigureAwait(false);
        Assert.That(test.Services.Calls, Is.Empty);
    }

    private static CallMethodResult Accepted(RoboticsTaskTestContext test)
    {
        return new CallMethodResult
        {
            OutputArguments =
                [Variant.From(true), Variant.From("owned-wait"), Variant.From(test.Services.Id("admitted")),
                Variant.From((int)IntentFailureEnum.None), Variant.From(LocalizedText.Null)]
        };
    }
}
