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
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Machinery;
using UaLens.Samples;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class MachineryZeroPointActionTests
    {
        [Test]
        public async Task InspectionOffersOnlyTheActualAuthorizedTypedProcessMethodWithoutCallingItAsync()
        {
            var test = new MachineryActionTestContext();
            Assert.That(test.Services.Calls, Is.Empty);
            Assert.That(test.Services.BrowseCalls, Is.Empty);
            ArrayOf<CompanionTarget> targets = await test.Provider.DiscoverAsync(test.Context, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(targets, Is.EqualTo((ArrayOf<CompanionTarget>)[test.Selection.Target]));
            CompanionInspection inspection = await test.Provider.InspectAsync(
                test.Context, targets[0], CancellationToken.None).ConfigureAwait(false);
            Assert.That(inspection.Operations, Has.Count.EqualTo(1));
            Assert.That(inspection.Operations[0].Id, Is.EqualTo("zero-point-adjustment"));
            Assert.That(inspection.Operations[0].Safety, Is.EqualTo(CompanionOperationSafety.DeploymentMutation));
            Assert.That(inspection.Operations[0].HasTypedInput, Is.True);
            Assert.That(inspection.Operations[0].Inputs, Is.Empty);
            Assert.That(inspection.Values.ToList().Single(value => value.Name == "Verified method").Value,
                Is.EqualTo(Variant.From(test.Method)));
            Assert.That(test.Services.Calls, Is.Empty);
        }

        [TestCase("absent")]
        [TestCase("not-method")]
        [TestCase("not-executable")]
        [TestCase("not-user-executable")]
        [TestCase("uncertain")]
        [TestCase("wrong-permission-type")]
        [TestCase("input-arguments")]
        [TestCase("output-arguments")]
        [TestCase("wrong-object-type")]
        [TestCase("wrong-signal-type")]
        [TestCase("wrong-status-type")]
        public async Task MissingDeniedOrIncorrectlyTypedMethodCannotBePreparedAsync(string change)
        {
            var test = new MachineryActionTestContext();
            switch (change)
            {
                case "absent":
                    test.Services.TranslateHandler = (path, _) =>
                        path.RelativePath.Elements[0].TargetName.Name == "ZeroPointAdjustment"
                            ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }
                            : test.Services.Translate(path);
                    break;
                case "not-method":
                    test.Services.SetValue(test.Method, Variant.From((int)NodeClass.Object), Attributes.NodeClass);
                    break;
                case "not-executable":
                    test.Services.SetValue(test.Method, Variant.From(false), Attributes.Executable);
                    break;
                case "not-user-executable":
                    test.Services.SetValue(test.Method, Variant.From(false), Attributes.UserExecutable);
                    break;
                case "uncertain":
                    test.Services.SetValue(
                        test.Method, Variant.From(true), Attributes.UserExecutable, StatusCodes.Uncertain);
                    break;
                case "wrong-permission-type":
                    test.Services.SetValue(test.Method, Variant.From(1), Attributes.Executable);
                    break;
                case "input-arguments":
                case "output-arguments":
                    test.Services.AddProperty(test.Method, Namespaces.OpcUa,
                        change == "input-arguments" ? BrowseNames.InputArguments : BrowseNames.OutputArguments,
                        Variant.FromStructure(
                            [new Argument { DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar }]));
                    break;
                case "wrong-object-type":
                    test.Services.AddObject(test.Process.NodeId, ObjectTypeIds.BaseObjectType);
                    break;
                case "wrong-signal-type":
                    test.Services.SetValue(test.Signal, Variant.From(DataTypeIds.String), Attributes.DataType);
                    break;
                case "wrong-status-type":
                    test.Services.SetValue(test.Status, Variant.From(0));
                    break;
                default:
                    throw new AssertionException("Unexpected metadata partition.");
            }
            await Assert.ThatAsync(async () => await PrepareAsync(test).ConfigureAwait(false),
                Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
            Assert.That(test.Services.Calls, Is.Empty);
        }

        [TestCase("no-consent")]
        [TestCase("expired")]
        [TestCase("wrong-peer")]
        [TestCase("wrong-sample")]
        [TestCase("stopped")]
        [TestCase("unowned")]
        [TestCase("sign-only")]
        public async Task OnlyAnOptedInLiveOwnedMachineryPeerCanAuthorizeAdjustmentAsync(string missing)
        {
            var test = new MachineryActionTestContext();
            switch (missing)
            {
                case "no-consent":
                    test.Snapshot = test.Snapshot with { CompanionOperationsAuthorized = false };
                    break;
                case "expired":
                    test.Clock.Now = test.Snapshot.CompanionOperationsExpireAt!.Value;
                    break;
                case "wrong-peer":
                    test.Snapshot = test.Snapshot with
                    {
                        Evidence = test.Snapshot.Evidence! with { CertificateSha256 = ByteString.From(new byte[32]) }
                    };
                    break;
                case "wrong-sample":
                    test.Snapshot = test.Snapshot with { Selection = RepositorySampleId.ScalesServer };
                    break;
                case "stopped":
                    test.Snapshot = test.Snapshot with { Phase = RepositorySamplePhase.Stopped };
                    break;
                case "unowned":
                    test.Snapshot = test.Snapshot with { OwnsResources = false };
                    break;
                case "sign-only":
                    test.Endpoint.SecurityMode = MessageSecurityMode.Sign;
                    break;
                default:
                    throw new AssertionException("Unexpected authorization partition.");
            }
            MachineryActionSelection selection = test.Select(
                MachinerySection.ProcessValues, test.Process, test.Selection.Values);
            var provider = new MachineryZeroPointProvider(selection, test.Policy, test.Clock);
            CompanionInspection inspection = await provider.InspectAsync(
                test.Context, selection.Target, CancellationToken.None).ConfigureAwait(false);
            Assert.That(inspection.Operations, Is.Empty);
            await Assert.ThatAsync(async () => await provider.PrepareInputAsync(test.Context, selection.Target,
                "zero-point-adjustment", [], CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            Assert.That(test.Services.Calls, Is.Empty);
        }

        [Test]
        public async Task ExistingWorkspaceRequiresPerTaskConsentAndDispatchesTheGeneratedProxyExactlyOnceAsync()
        {
            var test = new MachineryActionTestContext();
            var workspace = new CompanionWorkspace([test.Provider], test.Services.Telemetry, test.Clock, test.Policy);
            await using (workspace.ConfigureAwait(false))
            {
                await workspace.BindAsync(test.Context.Session).ConfigureAwait(false);
                CompanionTarget target = (await workspace.DiscoverAsync("machinery").ConfigureAwait(false))[0];
                _ = await workspace.InspectAsync(target).ConfigureAwait(false);
                CompanionOperationDraft denied = await workspace.PrepareTaskAsync(
                    target, "zero-point-adjustment", []).ConfigureAwait(false);
                await Assert.ThatAsync(() => workspace.ExecuteTaskAsync(
                    denied, true, false, null),
                    Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                Assert.That(test.Services.Calls, Is.Empty);
                await Assert.ThatAsync(() => workspace.ExecuteTaskAsync(
                    denied, false, true, null),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
                CompanionOperationDraft allowed = await workspace.PrepareTaskAsync(
                    target, "zero-point-adjustment", []).ConfigureAwait(false);
                Assert.That(allowed.DeploymentGrant!.ExpiresAt, Is.EqualTo(test.Snapshot.CompanionOperationsExpireAt));
                Assert.That(allowed.Summary, Does.Contain(test.Method.ToString()).And.Contain(test.Signal.ToString()));
                test.Services.CallHandler = (call, _) =>
                {
                    Assert.That(call.ObjectId, Is.EqualTo(test.Process.NodeId));
                    Assert.That(call.MethodId, Is.EqualTo(test.Method));
                    Assert.That(call.InputArguments, Is.Empty);
                    return new CallMethodResult();
                };
                CompanionOperationResult result = await workspace.ExecuteTaskAsync(
                    allowed, false, true, null).ConfigureAwait(false);
                Assert.That(result.Values[0].Value, Is.EqualTo(Variant.From(StatusCodes.Good)));
                Assert.That(result.Summary, Does.Contain("not physical calibration"));
                await Assert.ThatAsync(() => workspace.ExecuteTaskAsync(
                    allowed, false, true, null),
                    Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            }
            Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
            test.Services.Session.Verify(session => session.Dispose(), Times.Never);
            test.Transport.Verify(channel => channel.Dispose(), Times.Never);
        }

        [TestCase("selection")]
        [TestCase("peer")]
        [TestCase("identity")]
        [TestCase("namespace")]
        [TestCase("method")]
        [TestCase("source")]
        [TestCase("status")]
        [TestCase("suppression")]
        [TestCase("permission")]
        [TestCase("grant")]
        public async Task ChangedSelectionBindingStateOrPeerCannotUseAPreviousPreparationAsync(string change)
        {
            var test = new MachineryActionTestContext();
            CompanionTaskInput prepared = await PrepareAsync(test).ConfigureAwait(false);
            switch (change)
            {
                case "selection":
                    test.Current = false;
                    break;
                case "peer":
                    test.Endpoint.ServerCertificate = ByteString.From("another-peer"u8);
                    break;
                case "identity":
                    test.Services.Session.SetupGet(session => session.Identity).Returns(new UserIdentity());
                    break;
                case "namespace":
                    test.Services.NamespaceUris.GetIndexOrAppend("urn:changed");
                    break;
                case "method":
                case "source":
                    test.Services.SetValue(new NodeId(999u),
                        Variant.From((int)(change == "method" ? NodeClass.Method : NodeClass.Variable)),
                        Attributes.NodeClass);
                    test.Services.SetValue(new NodeId(999u), Variant.From(true), Attributes.Executable);
                    test.Services.SetValue(new NodeId(999u), Variant.From(true), Attributes.UserExecutable);
                    test.Services.SetValue(new NodeId(999u), Variant.From(DataTypeIds.Double), Attributes.DataType);
                    test.Services.TranslateHandler = (path, _) =>
                        path.RelativePath.Elements[0].TargetName.Name ==
                            (change == "method" ? "ZeroPointAdjustment" : "AnalogSignal")
                            ? new BrowsePathResult
                            {
                                Targets = [new BrowsePathTarget
                                {
                                    TargetId = new ExpandedNodeId(new NodeId(999u)), RemainingPathIndex = uint.MaxValue
                                }]
                            }
                            : test.Services.Translate(path);
                    break;
                case "status":
                    test.Services.SetValue(test.Status, Variant.From((ushort)1));
                    break;
                case "suppression":
                    test.Services.SetValue(test.Suppression, Variant.From((ushort)1));
                    break;
                case "permission":
                    test.Services.SetValue(test.Method, Variant.From(false), Attributes.UserExecutable);
                    break;
                case "grant":
                    test.Snapshot = test.Snapshot with { RunId = Guid.NewGuid() };
                    break;
                default:
                    throw new AssertionException("Unexpected freshness partition.");
            }
            await Assert.ThatAsync(async () => await RunAsync(test, prepared).ConfigureAwait(false),
                Throws.Exception.TypeOf<InvalidOperationException>().Or.TypeOf<UnauthorizedAccessException>()
                    .Or.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            Assert.That(test.Services.Calls, Is.Empty);
            await Assert.ThatAsync(async () => await RunAsync(test, prepared).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        public async Task FixedConsentDeadlineIsValidOnlyStrictlyBeforeExpiryAsync(int ticks)
        {
            var test = new MachineryActionTestContext();
            CompanionTaskInput prepared = await PrepareAsync(test).ConfigureAwait(false);
            test.Clock.Now = test.Snapshot.CompanionOperationsExpireAt!.Value.AddTicks(ticks);
            test.Services.CallHandler = (_, _) => new CallMethodResult();
            if (ticks < 0)
            {
                _ = await RunAsync(test, prepared).ConfigureAwait(false);
                Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
            }
            else
            {
                await Assert.ThatAsync(async () => await RunAsync(test, prepared).ConfigureAwait(false),
                    Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                Assert.That(test.Services.Calls, Is.Empty);
            }
        }

        [TestCase("uncertain")]
        [TestCase("unexpected-output")]
        [TestCase("timeout")]
        [TestCase("post-call-selection")]
        public async Task UnknownOrRejectedOutcomesNeverReplayAConsumedAdjustmentAsync(string outcome)
        {
            var test = new MachineryActionTestContext();
            CompanionTaskInput prepared = await PrepareAsync(test).ConfigureAwait(false);
            test.Services.CallHandler = (_, _) =>
            {
                if (outcome == "timeout")
                {
                    throw new TimeoutException("Outcome unknown.");
                }
                if (outcome == "post-call-selection")
                {
                    test.Current = false;
                }
                return outcome switch
                {
                    "uncertain" => new CallMethodResult { StatusCode = StatusCodes.Uncertain },
                    "unexpected-output" => new CallMethodResult { OutputArguments = [Variant.From(1)] },
                    _ => new CallMethodResult()
                };
            };
            await Assert.ThatAsync(async () => await RunAsync(test, prepared).ConfigureAwait(false),
                Throws.Exception.TypeOf<ServiceResultException>().Or.TypeOf<TimeoutException>()
                    .Or.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await RunAsync(test, prepared).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RawUnknownAndMismatchedRequestsCannotUseTheMachineryOwnedPolicyAsync()
        {
            var test = new MachineryActionTestContext();
            var raw = new CompanionOperationDraft(test.Selection.Target, MachineryZeroPointProvider.Operation,
                null, test.Context.Session, test.Clock.Now.AddMinutes(1));
            await Assert.ThatAsync(async () => await test.Policy.AuthorizeAsync(
                test.Context, raw, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            test.Samples.VerifyNoOtherCalls();
            CompanionTaskInput prepared = await PrepareAsync(test).ConfigureAwait(false);
            foreach (string operation in new[]
                { "call", "calibrate", "start-machine", "setpoint", "ZERO-POINT-ADJUSTMENT" })
            {
                var draft = new CompanionOperationDraft(test.Selection.Target,
                    new CompanionOperation(operation, operation, CompanionOperationSafety.DeploymentMutation),
                    null, test.Context.Session, test.Clock.Now.AddMinutes(1), prepared);
                await Assert.ThatAsync(async () => await test.Policy.AuthorizeAsync(
                    test.Context, draft, CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            }
            await Assert.ThatAsync(async () => await test.Provider.ExecuteAsync(
                test.Context, test.Selection.Target, "zero-point-adjustment", null, CancellationToken.None)
                .ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(test.Services.Calls, Is.Empty);
        }

        [TestCase("target")]
        [TestCase("provider")]
        [TestCase("operation")]
        public async Task MisdirectedPreparationIsConsumedWithoutDispatchAsync(string change)
        {
            var test = new MachineryActionTestContext();
            CompanionTaskInput prepared = await PrepareAsync(test).ConfigureAwait(false);
            MachineryZeroPointProvider provider = change == "provider"
                ? new MachineryZeroPointProvider(test.Selection, test.Policy, test.Clock) : test.Provider;
            CompanionTarget target = change == "target"
                ? test.Selection.Target with { NodeId = new NodeId(999u) } : test.Selection.Target;
            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                test.Context, target, change == "operation" ? "call" : "zero-point-adjustment",
                prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>()).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await RunAsync(test, prepared).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(test.Services.Calls, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OptionalStatusAbsenceDoesNotInventAMandatoryStateOrSignalPrecisionAsync(bool singlePrecision)
        {
            var test = new MachineryActionTestContext();
            test.Services.TranslateHandler = (path, _) =>
                path.RelativePath.Elements[0].TargetName.Name is "Status" or "AlarmSuppression"
                    ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }
                    : test.Services.Translate(path);
            test.Services.SetValue(test.Signal,
                Variant.From(singlePrecision ? DataTypeIds.Float : DataTypeIds.Double), Attributes.DataType);
            test.Services.CallHandler = (_, _) => new CallMethodResult();
            CompanionTaskInput prepared = await PrepareAsync(test).ConfigureAwait(false);
            Assert.That(prepared.Review, Does.Contain("not exposed"));
            CompanionOperationResult result = await RunAsync(test, prepared).ConfigureAwait(false);
            Assert.That(result.Values[0].Value, Is.EqualTo(Variant.From(StatusCodes.Good)));
            Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
        }

        [TestCase(DataTypes.Number)]
        [TestCase(DataTypes.Integer)]
        [TestCase(DataTypes.UInteger)]
        [TestCase(DataTypes.Int64)]
        [TestCase(DataTypes.UInt64)]
        [TestCase(DataTypes.Int32)]
        public async Task NumericSignalDeclarationDoesNotRequireFloatingPointPrecisionAsync(uint dataType)
        {
            var test = new MachineryActionTestContext();
            test.Services.SetValue(test.Signal, Variant.From(new NodeId(dataType)), Attributes.DataType);
            test.Services.ReadHandler = (read, _) =>
            {
                if (read.NodeId == test.Signal && read.AttributeId == Attributes.Value)
                {
                    throw new AssertionException("Zero adjustment must not reinterpret a sampled signal value.");
                }
                return test.Services.Read(read);
            };
            test.Services.CallHandler = (_, _) => new CallMethodResult();
            CompanionTaskInput prepared = await PrepareAsync(test).ConfigureAwait(false);
            CompanionOperationResult result = await RunAsync(test, prepared).ConfigureAwait(false);
            Assert.That(result.Values[0].Value, Is.EqualTo(Variant.From(StatusCodes.Good)));
            Assert.That(test.Services.Calls, Has.Count.EqualTo(1));
            Assert.That(test.Services.Calls[0].ObjectId, Is.EqualTo(test.Process.NodeId));
        }

        [Test]
        public async Task PreCanceledExecutionAndParentLifetimeConsumeOrRejectWithoutDispatchAsync()
        {
            var test = new MachineryActionTestContext();
            CompanionTaskInput prepared = await PrepareAsync(test).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await test.Provider.ExecutePreparedAsync(
                test.Context, test.Selection.Target, "zero-point-adjustment", prepared, null,
                new CancellationToken(true))
                .ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await RunAsync(test, prepared).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            using var parent = new CancellationTokenSource();
            var provider = new MachineryZeroPointProvider(test.Selection, test.Policy, test.Clock, parent.Token);
            await parent.CancelAsync().ConfigureAwait(false);
            await Assert.ThatAsync(async () => await provider.DiscoverAsync(test.Context, CancellationToken.None)
                .ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(test.Services.Calls, Is.Empty);
        }

        private static ValueTask<CompanionTaskInput> PrepareAsync(MachineryActionTestContext test)
        {
            return test.Provider.PrepareInputAsync(
                test.Context, test.Selection.Target, "zero-point-adjustment", [], CancellationToken.None);
        }

        private static ValueTask<CompanionOperationResult> RunAsync(
            MachineryActionTestContext test, CompanionTaskInput prepared)
        {
            return test.Provider.ExecutePreparedAsync(
                test.Context, test.Selection.Target, "zero-point-adjustment", prepared, null, CancellationToken.None);
        }
    }
}
