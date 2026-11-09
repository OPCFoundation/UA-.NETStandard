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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.IndustrialCompanionTestSession;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    [Category("IndustrialCompanions")]
    public sealed class Isa95LifecycleTests
    {
        [TestCase(0UL, false)]
        [TestCase(1UL, true)]
        [TestCase(2UL, false)]
        [TestCase(3UL, false)]
        [TestCase(4UL, false)]
        [TestCase(8UL, false)]
        [TestCase(16UL, false)]
        [TestCase(4294967296UL, false)]
        [TestCase(ulong.MaxValue, false)]
        public void ReturnStatusRequiresOnlyTheSuccessBit(ulong status, bool successful)
        {
            if (successful)
            {
                Assert.That(() => Isa95TaskAccess.CheckJobStatus(status), Throws.Nothing);
            }
            else
            {
                Assert.That(() => Isa95TaskAccess.CheckJobStatus(status),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadInvalidState));
            }
        }

        [TestCase(false, "store-job", "ReceiveJobOrder", 1)]
        [TestCase(false, "start-job", "ReceiveJobOrder", 3)]
        [TestCase(false, "update-job", "ReceiveJobOrder", 4)]
        [TestCase(false, "cancel-job", "ReceiveJobOrder", 6)]
        [TestCase(false, "clear-job", "ReceiveJobOrder", 7)]
        [TestCase(true, "store-job", "Store", 0)]
        [TestCase(true, "start-job", "Start", 0)]
        [TestCase(true, "update-job", "Update", 0)]
        [TestCase(true, "pause-job", "Pause", 0)]
        [TestCase(true, "resume-job", "Resume", 0)]
        [TestCase(true, "abort-job", "Abort", 0)]
        [TestCase(true, "cancel-job", "Cancel", 0)]
        [TestCase(true, "clear-job", "Clear", 0)]
        public async Task EachSupportedLifecycleDispatchesTheExactVersionedRequestOnceAsync(
            bool version2, string operationId, string methodName, int command)
        {
            var model = new JobModel(version2, present: operationId != "store-job");
            var provider = new Isa95CompanionProvider();
            using var cancellation = new CancellationTokenSource();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, operationId, model.Inputs(operationId), cancellation.Token)
                .ConfigureAwait(false);
            Assert.That(model.Session.Calls.IsEmpty, Is.True, "Preparation must not invoke job methods.");
            model.Session.CallHandler = (request, token) =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(model.Order.NodeId));
                Assert.That(request.MethodId, Is.EqualTo(model.DeclarationMethod(methodName)));
                Assert.That(token, Is.EqualTo(cancellation.Token));
                Assert.That(request.InputArguments, Has.Count.EqualTo(2));
                bool authored = operationId is "store-job" or "update-job";
                if (version2)
                {
                    Assert.That(request.InputArguments[1].TryGetValue(
                        out ArrayOf<LocalizedText> comments), Is.True);
                    Assert.That(comments, Has.Count.EqualTo(2));
                    Assert.That(comments[0].Locale, Is.EqualTo("en-US"));
                    Assert.That(comments[0].Text, Is.EqualTo("Authorized batch operation"));
                    Assert.That(comments[1].Locale, Is.EqualTo("de-DE"));
                    if (authored)
                    {
                        AssertOrderFields(request.InputArguments[0], model);
                        model.SetOrder(request.InputArguments[0]);
                    }
                    else
                    {
                        Assert.That(request.InputArguments[0].TryGetValue(out string? id), Is.True);
                        Assert.That(id, Is.EqualTo(kJobId));
                    }
                }
                else
                {
                    Assert.That(request.InputArguments[0].TryGetValue(out int actualCommand), Is.True);
                    Assert.That(actualCommand, Is.EqualTo(command));
                    Assert.That(request.InputArguments[1].TryGetValue<V1.ISA95JobOrderDataType>(
                        out V1.ISA95JobOrderDataType? job, model.Session.MessageContext), Is.True);
                    Assert.That(job!.ID, Is.EqualTo(kJobId));
                    if (authored)
                    {
                        AssertOrderFields(request.InputArguments[1], model);
                        model.SetOrder(request.InputArguments[1]);
                    }
                    else
                    {
                        Assert.That(job.WorkMasterID.IsEmpty, Is.True,
                            "An ID-only command must not replace the order.");
                    }
                }
                if (operationId == "clear-job")
                {
                    model.SetCatalog(false);
                }
                return Good([Variant.From(1UL)]);
            };

            CompanionOperationResult result = await provider.ExecutePreparedAsync(
                model.Context, model.Order, operationId, prepared, null, cancellation.Token).ConfigureAwait(false);

            Assert.That(Field(result.Values, "ISA-95 return status").TryGetValue(out ulong status), Is.True);
            Assert.That(status, Is.EqualTo(1UL));
            Assert.That(Field(result.Values, "Job present").TryGetValue(out bool present), Is.True);
            Assert.That(present, Is.EqualTo(operationId != "clear-job"));
            Assert.That(model.Session.Calls, Has.Count.EqualTo(1));
        }

        [TestCase(false, "store-job")]
        [TestCase(false, "update-job")]
        [TestCase(true, "store-job")]
        [TestCase(true, "update-job")]
        public async Task TypedAuthoringSnapshotsJobFieldsAndNestedParametersWithoutLeakingReviewValuesAsync(
            bool version2, string operationId)
        {
            var model = new JobModel(version2, present: operationId != "store-job");
            Variant supplied = model.CreateOrder();
            LocalizedText[] callerComments = [new("en-US", "Authorized batch operation")];
            ArrayOf<CompanionValue> inputs = version2
                ?
                [
                    new("jobOrder", supplied),
                    new("comment", Variant.From(ArrayOf.Wrapped(callerComments)))
                ]
                : model.Inputs(operationId, supplied);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, operationId, inputs, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(prepared.Review, Does.Contain(kJobId));
            Assert.That(prepared.Review, Does.Contain(model.Order.NodeId.ToString()));
            Assert.That(prepared.Review, Does.Contain(version2 ? "V2" : "V1"));
            Assert.That(prepared.Review, Does.Not.Contain("private-batch-note"));
            Assert.That(prepared.Review, Does.Not.Contain("Authorized batch operation"));
            callerComments[0] = new LocalizedText("en-US", "caller-changed-comment");
            if (version2)
            {
                Assert.That(supplied.TryGetValue<V2.ISA95JobOrderDataType>(
                    out V2.ISA95JobOrderDataType? job, model.Session.MessageContext), Is.True);
                job!.JobOrderID = "caller-changed";
                job.Priority = -100;
                job.JobOrderParameters[0].Value = Variant.From(0.0);
                job.WorkMasterID[0].ID = "caller-changed-recipe";
            }
            else
            {
                Assert.That(supplied.TryGetValue<V1.ISA95JobOrderDataType>(
                    out V1.ISA95JobOrderDataType? job, model.Session.MessageContext), Is.True);
                job!.ID = "caller-changed";
                job.Priority = -100;
                job.JobOrderParameters[0].Value = Variant.From(0.0);
                job.WorkMasterID[0].ID = "caller-changed-recipe";
            }
            model.Session.CallHandler = (request, _) =>
            {
                Variant sent = request.InputArguments[version2 ? 0 : 1];
                AssertOrderFields(sent, model);
                if (version2)
                {
                    Assert.That(request.InputArguments[1].TryGetValue(
                        out ArrayOf<LocalizedText> comments), Is.True);
                    Assert.That(comments, Has.Count.EqualTo(1));
                    Assert.That(comments[0].Text, Is.EqualTo("Authorized batch operation"));
                }
                model.SetOrder(sent);
                return Good([Variant.From(1UL)]);
            };

            await provider.ExecutePreparedAsync(
                model.Context, model.Order, operationId, prepared, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(model.Session.Calls, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task InspectionOffersPortableTypedFormsAndDistinctDeploymentGuardsAsync(bool version2)
        {
            var model = new JobModel(version2);
            CompanionInspection inspection = await new Isa95CompanionProvider().InspectAsync(
                model.Context, model.Order, CancellationToken.None).ConfigureAwait(false);
            int mutations = 0;
            foreach (CompanionOperation operation in inspection.Operations)
            {
                if (operation.Safety == CompanionOperationSafety.DeploymentMutation)
                {
                    mutations++;
                    Assert.That(operation.HasTypedInput, Is.True);
                }
                if (operation.Id == "store-job")
                {
                    Assert.That(operation.Safety, Is.EqualTo(CompanionOperationSafety.DeploymentMutation));
                    CompanionInputDefinition job = operation.Inputs[0];
                    Assert.That(job.Name, Is.EqualTo("jobOrder"));
                    Assert.That(job.DataType, Is.EqualTo(BuiltInType.ExtensionObject));
                    Assert.That(job.DataTypeId, Is.EqualTo(version2
                        ? V2.DataTypeIds.ISA95JobOrderDataType : V1.DataTypeIds.ISA95JobOrderDataType));
                    Assert.That(job.DataTypeId.NamespaceUri, Is.EqualTo(model.NamespaceUri));
                    Assert.That(job.ValueRank, Is.EqualTo(ValueRanks.Scalar));
                    Assert.That(job.RequiresEditor, Is.True);
                    if (version2)
                    {
                        Assert.That(operation.Inputs[1].DataType, Is.EqualTo(BuiltInType.LocalizedText));
                        Assert.That(operation.Inputs[1].ValueRank, Is.EqualTo(ValueRanks.OneDimension));
                    }
                }
            }
            Assert.That(mutations, Is.EqualTo(version2 ? 8 : 5));
            Assert.That(inspection.Operations.Contains(operation =>
                operation.Id == "store-sample-job" &&
                operation.Safety == CompanionOperationSafety.SampleMutation), Is.True);
            Assert.That(inspection.Operations.Contains(operation => operation.Id == "request-job-response"), Is.False);
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [TestCase("pause-job")]
        [TestCase("resume-job")]
        [TestCase("abort-job")]
        [TestCase("observe-job-status")]
        [TestCase("begin-execution")]
        [TestCase("complete-job")]
        [TestCase("close-job")]
        public async Task V1DoesNotOfferOrPrepareUnsupportedLifecycleCombinationsAsync(string operationId)
        {
            var model = new JobModel(false);
            var provider = new Isa95CompanionProvider();
            CompanionInspection inspection = await provider.InspectAsync(
                model.Context, model.Order, CancellationToken.None).ConfigureAwait(false);

            Assert.That(inspection.Operations.Contains(operation => operation.Id == operationId), Is.False);
            Assert.That(inspection.Summary, Does.Contain("V1 has no Pause, Resume or Abort"));
            Assert.That(async () => await provider.PrepareInputAsync(
                model.Context, model.Order, operationId, [], CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotSupported));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WrongVersionJobStructuresCannotBePrepared(bool version2)
        {
            var model = new JobModel(version2, present: false);
            Variant wrong = version2
                ? Variant.FromStructure(CreateV1Order())
                : Variant.FromStructure(CreateV2Order());

            Assert.That(async () => await new Isa95CompanionProvider().PrepareInputAsync(
                model.Context, model.Order, "store-job", model.Inputs("store-job", wrong), CancellationToken.None)
                .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void IncompleteEndpointBindingCannotPrepareDeployment(bool version2)
        {
            var model = new JobModel(version2, completeBinding: false);

            Assert.That(async () => await new Isa95CompanionProvider().PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotSupported));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingMethodsAreNotOfferedAndCannotBePreparedAsync(bool version2)
        {
            var model = new JobModel(version2, exposeMethods: false);
            var provider = new Isa95CompanionProvider();
            CompanionInspection inspection = await provider.InspectAsync(
                model.Context, model.Order, CancellationToken.None).ConfigureAwait(false);

            Assert.That(inspection.Operations.Contains(operation =>
                operation.Safety is CompanionOperationSafety.DeploymentMutation or
                    CompanionOperationSafety.SampleMutation), Is.False);
            Assert.That(Field(inspection.Values, "Start job availability").TryGetValue(out StatusCode status),
                Is.True);
            StatusCode expected = StatusCodes.BadMethodInvalid;
            Assert.That(status, Is.EqualTo(expected));
            Assert.That(async () => await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadMethodInvalid));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task DeniedMethodsAreNotOfferedAndCannotBePreparedAsync(
            bool version2, bool userPermission)
        {
            var model = new JobModel(version2);
            uint attribute = userPermission ? Attributes.UserExecutable : Attributes.Executable;
            StatusCode expected = userPermission ? StatusCodes.BadUserAccessDenied : StatusCodes.BadNotExecutable;
            model.Session.SetValue(
                model.Method(version2 ? "Start" : "ReceiveJobOrder"), Variant.From(false), attribute);
            var provider = new Isa95CompanionProvider();
            CompanionInspection inspection = await provider.InspectAsync(
                model.Context, model.Order, CancellationToken.None).ConfigureAwait(false);

            Assert.That(inspection.Operations.Contains(operation => operation.Id == "start-job"), Is.False);
            Assert.That(Field(inspection.Values, "Start job availability").TryGetValue(out StatusCode status),
                Is.True);
            Assert.That(status, Is.EqualTo(expected));
            Assert.That(async () => await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(expected));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task NonzeroReturnStatusIsAnErrorAndNeverRetriedAsync(bool version2)
        {
            var model = new JobModel(version2);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false);
            model.Session.CallHandler = (_, _) => new CallMethodResult
            {
                StatusCode = StatusCodes.Uncertain,
                OutputArguments = [Variant.From(17UL)]
            };

            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Order, "start-job", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState)
                    .And.Message.Contains("17"));
            Assert.That(model.Session.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task BadMethodInvalidTriesOnlyTheResolvedInstanceWithoutRepeatingAnAcceptedCallAsync()
        {
            var model = new JobModel(true);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false);
            model.Session.CallHandler = (_, _) => new CallMethodResult { StatusCode = StatusCodes.BadMethodInvalid };

            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Order, "start-job", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadMethodInvalid));
            Assert.That(model.Session.Calls, Has.Count.EqualTo(2));
            Assert.That(model.Session.Calls[0].MethodId, Is.EqualTo(model.DeclarationMethod("Start")));
            Assert.That(model.Session.Calls[1].MethodId, Is.EqualTo(model.Method("Start")));
            Assert.That(model.Session.Calls[1].ObjectId, Is.EqualTo(model.Order.NodeId));
        }

        [Test]
        public async Task RejectedDeclarationFallsBackToTheInstanceWithExactlyOneAcceptedInvocationAsync()
        {
            var model = new JobModel(true);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false);
            int accepted = 0;
            model.Session.CallHandler = (request, _) =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(model.Order.NodeId));
                if (request.MethodId == model.DeclarationMethod("Start"))
                {
                    return new CallMethodResult { StatusCode = StatusCodes.BadMethodInvalid };
                }
                Assert.That(request.MethodId, Is.EqualTo(model.Method("Start")));
                accepted++;
                return Good([Variant.From(1UL)]);
            };

            CompanionOperationResult result = await provider.ExecutePreparedAsync(
                model.Context, model.Order, "start-job", prepared, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(accepted, Is.EqualTo(1));
            Assert.That(model.Session.Calls, Has.Count.EqualTo(2));
            Assert.That(Field(result.Values, "ISA-95 return status").TryGetValue(out ulong status), Is.True);
            Assert.That(status, Is.EqualTo(1UL));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ChangedStoredJobInvalidatesPreparationBeforeDispatchAsync(bool version2)
        {
            var model = new JobModel(version2);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false);
            model.SetCatalog(true, priority: 8);

            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Order, "start-job", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task ChangedV2StateInvalidatesPreparationBeforeDispatchAsync()
        {
            var model = new JobModel(true);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false);
            model.SetCatalog(true, state: 3);

            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Order, "start-job", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task AcceptedStartReportsActualUnchangedStateRatherThanInventingExecutionAsync()
        {
            var model = new JobModel(true);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false);
            model.Session.CallHandler = (_, _) => Good([Variant.From(1UL)]);

            CompanionOperationResult result = await provider.ExecutePreparedAsync(
                model.Context, model.Order, "start-job", prepared, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(Field(result.Values, "Observed job").TryGetValue<V2.ISA95JobOrderAndStateDataType>(
                out V2.ISA95JobOrderAndStateDataType? observed, model.Session.MessageContext), Is.True);
            Assert.That(observed!.State[0].StateNumber, Is.EqualTo(1));
            Assert.That(result.Summary, Does.Contain("not inferred execution"));
            Assert.That(model.Session.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task StatusObservationReturnsFreshStateWithoutInvokingLifecycleMethodsAsync()
        {
            var model = new JobModel(true);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "observe-job-status", model.Inputs("observe-job-status"),
                CancellationToken.None).ConfigureAwait(false);
            model.SetCatalog(true, state: 3);

            CompanionOperationResult result = await provider.ExecutePreparedAsync(
                model.Context, model.Order, "observe-job-status", prepared, null, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(Field(result.Values, "Observed job").TryGetValue<V2.ISA95JobOrderAndStateDataType>(
                out V2.ISA95JobOrderAndStateDataType? observed, model.Session.MessageContext), Is.True);
            Assert.That(observed!.State[0].StateNumber, Is.EqualTo(3));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ResponseObservationUsesTheExactProviderAndPreservesTypedStatesAsync(bool version2)
        {
            var model = new JobModel(version2);
            var provider = new Isa95CompanionProvider();
            using var cancellation = new CancellationTokenSource();
            CompanionInspection inspection = await provider.InspectAsync(
                model.Context, model.Response, cancellation.Token).ConfigureAwait(false);
            Assert.That(inspection.Operations.Contains(operation =>
                operation.Id == "request-job-response" &&
                operation.Safety == CompanionOperationSafety.ReadOnly), Is.True);
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Response, "request-job-response", model.Inputs("request-job-response"),
                cancellation.Token).ConfigureAwait(false);
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
            model.Session.CallHandler = (request, token) =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(model.Response.NodeId));
                Assert.That(request.MethodId, Is.EqualTo(model.DeclarationMethod(
                    version2 ? "RequestJobResponseByJobOrderID" : "RequestJobResponse")));
                Assert.That(token, Is.EqualTo(cancellation.Token));
                Assert.That(request.InputArguments, Has.Count.EqualTo(version2 ? 1 : 2));
                Assert.That(request.InputArguments[0].TryGetValue(out string? jobId), Is.True);
                Assert.That(jobId, Is.EqualTo(kJobId));
                if (!version2)
                {
                    Assert.That(request.InputArguments[1].TryGetValue(out int state), Is.True);
                    Assert.That(state, Is.EqualTo((int)V1.ISA95JobOrderStateEnum.Undefined));
                }
                return Good([ResponseValue(version2), Variant.From(1UL)]);
            };

            CompanionOperationResult result = await provider.ExecutePreparedAsync(
                model.Context, model.Response, "request-job-response", prepared, null, cancellation.Token)
                .ConfigureAwait(false);

            Assert.That(Field(result.Values, "Response count").TryGetValue(out int count), Is.True);
            Assert.That(count, Is.EqualTo(version2 ? 1 : 2));
            Assert.That(Field(result.Values, "ISA-95 return status").TryGetValue(out ulong status), Is.True);
            Assert.That(status, Is.EqualTo(1UL));
            if (version2)
            {
                Assert.That(Field(result.Values, "Job responses").TryGetValue<V2.ISA95JobResponseDataType>(
                    out V2.ISA95JobResponseDataType? response, model.Session.MessageContext), Is.True);
                Assert.That(response!.JobResponseID, Is.EqualTo("response-2"));
                Assert.That(response.JobState[0].StateNumber, Is.EqualTo(3));
            }
            else
            {
                Assert.That(Field(result.Values, "Job responses").TryGetStructure(
                    model.Session.MessageContext, out ArrayOf<V1.ISA95JobResponseDataType> responses), Is.True);
                Assert.That(responses[0].ID, Is.EqualTo("response-1"));
                Assert.That(responses[0].JobState, Is.EqualTo(V1.ISA95JobOrderStateEnum.Ready));
                Assert.That(responses[1].ID, Is.EqualTo("response-2"));
                Assert.That(responses[1].JobState, Is.EqualTo(V1.ISA95JobOrderStateEnum.Running));
            }
            Assert.That(result.Summary, Does.Contain("may be historical"));
            Assert.That(model.Session.Calls, Has.Count.EqualTo(1));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task InvalidResponseOrNonzeroReturnStatusNeverReportsAcceptanceAsync(
            bool version2, bool malformed)
        {
            var model = new JobModel(version2);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Response, "request-job-response", model.Inputs("request-job-response"),
                CancellationToken.None).ConfigureAwait(false);
            model.Session.CallHandler = (_, _) => Good(
                [malformed ? Variant.From("not a job response") : ResponseValue(version2), Variant.From(18UL)]);

            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Response, "request-job-response", prepared, null, CancellationToken.None)
                .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(malformed ? StatusCodes.BadUnexpectedError : StatusCodes.BadInvalidState));
            Assert.That(model.Session.Calls, Has.Count.EqualTo(1));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task EmptyOrMismatchedResponsesNeverBecomeSuccessfulObservationsAsync(
            bool version2, bool wrongJob)
        {
            var model = new JobModel(version2);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Response, "request-job-response", model.Inputs("request-job-response"),
                CancellationToken.None).ConfigureAwait(false);
            Variant response = wrongJob ? ResponseValue(version2, "other-job") :
                version2 ? Variant.FromStructure(new V2.ISA95JobResponseDataType
                {
                    JobOrderID = kJobId,
                    JobResponseID = "response-without-state"
                }) : Variant.FromStructure(ArrayOf<V1.ISA95JobResponseDataType>.Empty);
            model.Session.CallHandler = (_, _) => Good([response, Variant.From(1UL)]);

            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Response, "request-job-response", prepared, null, CancellationToken.None)
                .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(wrongJob ? StatusCodes.BadUnexpectedError : StatusCodes.BadNoData));
            Assert.That(model.Session.Calls, Has.Count.EqualTo(1));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task MissingOrWronglyTypedReturnStatusCannotReportAcceptanceAsync(
            bool version2, bool missing)
        {
            var model = new JobModel(version2);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false);
            model.Session.CallHandler = (_, _) => Good(missing ? [] : [Variant.From(0)]);

            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Order, "start-job", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(model.Session.Calls, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AcceptedStoreWithoutAnObservableJobDoesNotReportSuccessAsync(bool version2)
        {
            var model = new JobModel(version2, present: false);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "store-job", model.Inputs("store-job"), CancellationToken.None)
                .ConfigureAwait(false);
            model.Session.CallHandler = (_, _) => Good([Variant.From(1UL)]);

            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Order, "store-job", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotFound).And.Message.Contains("outcome is not confirmed"));
            Assert.That(model.Session.Calls, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PostCallReadFailureRetainsAcceptanceEvidenceWithoutClaimingSuccessAsync(bool version2)
        {
            var model = new JobModel(version2);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false);
            model.Session.CallHandler = (_, _) =>
            {
                model.Session.SetValue(model.CatalogId, default, status: StatusCodes.BadNotReadable);
                return Good([Variant.From(1UL)]);
            };

            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Order, "start-job", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotReadable).And.Message.Contains("ReturnStatus Success"));
            Assert.That(model.Session.Calls, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RevokedPermissionIsRecheckedBeforeDispatchAsync(bool version2)
        {
            var model = new JobModel(version2);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false);
            model.Session.SetValue(model.Method(version2 ? "Start" : "ReceiveJobOrder"),
                Variant.From(false), Attributes.UserExecutable);

            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Order, "start-job", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReplacedMethodCannotConsumeAnExistingPreparationAsync(bool version2)
        {
            var model = new JobModel(version2);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false);
            model.AddMethod(version2 ? "Start" : "ReceiveJobOrder");

            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Order, "start-job", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task EndpointAmbiguityIntroducedAfterPreparationPreventsDispatchAsync()
        {
            var model = new JobModel(true);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false);
            NodeId duplicate = model.Session.Id("duplicate-response-provider");
            model.Session.AddObject(duplicate, V2.ObjectTypeIds.ISA95JobResponseProviderObjectType);
            model.Session.AddChild(model.Parent, duplicate);

            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Order, "start-job", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task PreparationCannotBeReusedForAnotherTargetOrOperationAsync()
        {
            var model = new JobModel(true);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Response, "start-job", prepared, null, CancellationToken.None)
                .ConfigureAwait(false), Throws.ArgumentException);
            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Order, "cancel-job", prepared, null, CancellationToken.None)
                .ConfigureAwait(false), Throws.ArgumentException);
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AlreadyCancelledPreparationMakesNoServiceCalls(bool version2)
        {
            var model = new JobModel(version2);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.That(async () => await new Isa95CompanionProvider().PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), cancellation.Token)
                .ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>());
            model.Session.Session.Verify(session => session.ReadAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(model.Session.BrowseCalls.IsEmpty, Is.True);
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationDuringFreshStateReadPreventsDispatchAsync(bool version2)
        {
            var model = new JobModel(version2);
            var provider = new Isa95CompanionProvider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            model.Session.Session.Setup(session => session.ReadAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader? _, double _, TimestampsToReturn _,
                    ArrayOf<ReadValueId> reads, CancellationToken token) => ReadAsync(reads, token));
            Task<CompanionOperationResult> executing = provider.ExecutePreparedAsync(
                model.Context, model.Order, "start-job", prepared, null, cancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await cancellation.CancelAsync().ConfigureAwait(false);
                await Assert.ThatAsync(() => executing.WaitAsync(TimeSpan.FromSeconds(10)),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                Assert.That(model.Session.Calls.IsEmpty, Is.True);
            }
            finally
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                release.TrySetResult();
            }

            async ValueTask<ReadResponse> ReadAsync(ArrayOf<ReadValueId> reads, CancellationToken token)
            {
                Assert.That(token, Is.EqualTo(cancellation.Token));
                token.ThrowIfCancellationRequested();
                if (reads.Count == 1 && reads[0].NodeId == model.CatalogId)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                }
                return new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = reads.ConvertAll(model.Session.Read)
                };
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationAfterDispatchNeverReportsSuccessOrRetriesAsync(bool version2)
        {
            var model = new JobModel(version2);
            var provider = new Isa95CompanionProvider();
            using var cancellation = new CancellationTokenSource();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), cancellation.Token)
                .ConfigureAwait(false);
            model.Session.CallHandler = (_, token) =>
            {
                Assert.That(token, Is.EqualTo(cancellation.Token));
                cancellation.Cancel();
                return Good([Variant.From(1UL)]);
            };

            Assert.That(async () => await provider.ExecutePreparedAsync(
                model.Context, model.Order, "start-job", prepared, null, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(model.Session.Calls, Has.Count.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MissingCatalogIsExplicitAndCannotOfferDeploymentAsync(bool version2)
        {
            var model = new JobModel(version2);
            model.Session.TranslateHandler = (path, _) =>
                path.StartingNode == model.Order.NodeId &&
                path.RelativePath.Elements[0].TargetName.Name == "JobOrderList"
                    ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }
                    : model.Session.Translate(path);
            var provider = new Isa95CompanionProvider();
            CompanionInspection inspection = await provider.InspectAsync(
                model.Context, model.Order, CancellationToken.None).ConfigureAwait(false);

            Assert.That(inspection.Operations.Contains(operation =>
                operation.Safety == CompanionOperationSafety.DeploymentMutation), Is.False);
            Assert.That(Field(inspection.Values, "JobOrderList").TryGetValue(out StatusCode status), Is.True);
            StatusCode expected = StatusCodes.BadNotFound;
            Assert.That(status, Is.EqualTo(expected));
            Assert.That(async () => await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNoMatch));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DeniedCatalogReadCannotBecomeAnEmptySuccessfulInspection(bool version2)
        {
            var model = new JobModel(version2);
            model.Session.SetValue(model.CatalogId, default, status: StatusCodes.BadUserAccessDenied);

            Assert.That(async () => await new Isa95CompanionProvider().InspectAsync(
                model.Context, model.Order, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DuplicateStoreAndAbsentLifecycleTargetsAreRejectedDuringPreparation(bool version2)
        {
            var model = new JobModel(version2);
            var provider = new Isa95CompanionProvider();
            Assert.That(async () => await provider.PrepareInputAsync(
                model.Context, model.Order, "store-job", model.Inputs("store-job"), CancellationToken.None)
                .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadEntryExists));
            model.SetCatalog(false);

            Assert.That(async () => await provider.PrepareInputAsync(
                model.Context, model.Order, "start-job", model.Inputs("start-job"), CancellationToken.None)
                .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadNotFound));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TypedDeploymentCannotUseTheLegacyStringExecutionPath(bool version2)
        {
            var model = new JobModel(version2);

            Assert.That(async () => await new Isa95CompanionProvider().ExecuteAsync(
                model.Context, model.Order, "store-job", "{\"jobOrderId\":\"Production/Batch-007\"}",
                CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotSupported));
            Assert.That(model.Session.Calls.IsEmpty, Is.True);
        }

        private static Variant ResponseValue(bool version2, string jobId = kJobId)
        {
            return version2
                ? Variant.FromStructure(new V2.ISA95JobResponseDataType
                {
                    JobOrderID = jobId,
                    JobResponseID = "response-2",
                    JobState = [new V2.ISA95StateDataType
                    {
                        BrowsePath = new RelativePath(),
                        StateNumber = 3,
                        StateText = new LocalizedText("Running")
                    }]
                })
                : Variant.FromStructure(ArrayOf.Wrapped(
                [
                    new V1.ISA95JobResponseDataType
                    {
                        JobOrderID = jobId, ID = "response-1", JobState = V1.ISA95JobOrderStateEnum.Ready
                    },
                    new V1.ISA95JobResponseDataType
                    {
                        JobOrderID = jobId, ID = "response-2", JobState = V1.ISA95JobOrderStateEnum.Running
                    }
                ]));
        }

        private static void AssertOrderFields(Variant value, JobModel model)
        {
            if (model.Version2)
            {
                Assert.That(value.TryGetValue<V2.ISA95JobOrderDataType>(
                    out V2.ISA95JobOrderDataType? job, model.Session.MessageContext), Is.True);
                Assert.That(job!.JobOrderID, Is.EqualTo(kJobId));
                Assert.That(job.Description[0].Text, Is.EqualTo("Batch authoring"));
                Assert.That(job.Priority, Is.EqualTo(7));
                Assert.That(job.StartTime, Is.EqualTo(s_start));
                Assert.That(job.EndTime, Is.EqualTo(s_start.Add(TimeSpan.FromHours(1))));
                Assert.That(job.WorkMasterID[0].ID, Is.EqualTo("recipe-42"));
                Assert.That(job.WorkMasterID[0].Parameters[0].Value.TryGetValue(out bool enabled), Is.True);
                Assert.That(enabled, Is.True);
                Assert.That(job.JobOrderParameters, Has.Count.EqualTo(3));
                Assert.That(job.JobOrderParameters[0].Value.TryGetValue(out double temperature), Is.True);
                Assert.That(temperature, Is.EqualTo(175.5));
                Assert.That(job.JobOrderParameters[1].Value.TryGetValue(out ulong sequence), Is.True);
                Assert.That(sequence, Is.EqualTo(ulong.MaxValue));
                Assert.That(job.JobOrderParameters[2].Value.TryGetValue(out string? note), Is.True);
                Assert.That(note, Is.EqualTo("private-batch-note"));
                Assert.That(job.PersonnelRequirements[0].ID, Is.EqualTo("operator-A"));
                Assert.That(job.EquipmentRequirements[0].ID, Is.EqualTo("reactor-1"));
                Assert.That(job.PhysicalAssetRequirements[0].ID, Is.EqualTo("vessel-1"));
                Assert.That(job.MaterialRequirements[0].MaterialLotID, Is.EqualTo("lot-100"));
            }
            else
            {
                Assert.That(value.TryGetValue<V1.ISA95JobOrderDataType>(
                    out V1.ISA95JobOrderDataType? job, model.Session.MessageContext), Is.True);
                Assert.That(job!.ID, Is.EqualTo(kJobId));
                Assert.That(job.Description, Is.EqualTo("Batch authoring"));
                Assert.That(job.Priority, Is.EqualTo(7));
                Assert.That(job.StartTime, Is.EqualTo(s_start));
                Assert.That(job.EndTime, Is.EqualTo(s_start.Add(TimeSpan.FromHours(1))));
                Assert.That(job.WorkMasterID[0].ID, Is.EqualTo("recipe-42"));
                Assert.That(job.WorkMasterID[0].Parameters[0].Value.TryGetValue(out bool enabled), Is.True);
                Assert.That(enabled, Is.True);
                Assert.That(job.JobOrderParameters, Has.Count.EqualTo(3));
                Assert.That(job.JobOrderParameters[0].Value.TryGetValue(out double temperature), Is.True);
                Assert.That(temperature, Is.EqualTo(175.5));
                Assert.That(job.JobOrderParameters[1].Value.TryGetValue(out ulong sequence), Is.True);
                Assert.That(sequence, Is.EqualTo(ulong.MaxValue));
                Assert.That(job.JobOrderParameters[2].Value.TryGetValue(out string? note), Is.True);
                Assert.That(note, Is.EqualTo("private-batch-note"));
                Assert.That(job.PersonnelRequirements[0].ID, Is.EqualTo("operator-A"));
                Assert.That(job.EquipmentRequirements[0].ID, Is.EqualTo("reactor-1"));
                Assert.That(job.PhysicalAssetRequirements[0].ID, Is.EqualTo("vessel-1"));
                Assert.That(job.MaterialRequirements[0].MaterialLotID, Is.EqualTo("lot-100"));
            }
        }

        private static V1.ISA95JobOrderDataType CreateV1Order(short priority = 7)
        {
            return new V1.ISA95JobOrderDataType
            {
                ID = kJobId,
                Description = "Batch authoring",
                Priority = priority,
                StartTime = s_start,
                EndTime = s_start.Add(TimeSpan.FromHours(1)),
                WorkMasterID =
                [
                    new V1.ISA95WorkMasterDataType
                    {
                        ID = "recipe-42",
                        Parameters = [new V1.ISA95ParameterDataType { ID = "enabled", Value = Variant.From(true) }]
                    }
                ],
                JobOrderParameters =
                [
                    new V1.ISA95ParameterDataType { ID = "temperature", Value = Variant.From(175.5) },
                    new V1.ISA95ParameterDataType { ID = "sequence", Value = Variant.From(ulong.MaxValue) },
                    new V1.ISA95ParameterDataType { ID = "note", Value = Variant.From("private-batch-note") }
                ],
                PersonnelRequirements = [new V1.ISA95PersonnelDataType { ID = "operator-A" }],
                EquipmentRequirements = [new V1.ISA95EquipmentDataType { ID = "reactor-1" }],
                PhysicalAssetRequirements = [new V1.ISA95PhysicalAssetDataType { ID = "vessel-1" }],
                MaterialRequirements = [new V1.ISA95MaterialDataType { MaterialLotID = "lot-100" }]
            };
        }

        private static V2.ISA95JobOrderDataType CreateV2Order(short priority = 7)
        {
            return new V2.ISA95JobOrderDataType
            {
                JobOrderID = kJobId,
                EncodingMask = (uint)(
                    V2.ISA95JobOrderDataTypeFields.Description |
                    V2.ISA95JobOrderDataTypeFields.Priority |
                    V2.ISA95JobOrderDataTypeFields.StartTime |
                    V2.ISA95JobOrderDataTypeFields.EndTime |
                    V2.ISA95JobOrderDataTypeFields.WorkMasterID |
                    V2.ISA95JobOrderDataTypeFields.JobOrderParameters |
                    V2.ISA95JobOrderDataTypeFields.PersonnelRequirements |
                    V2.ISA95JobOrderDataTypeFields.EquipmentRequirements |
                    V2.ISA95JobOrderDataTypeFields.PhysicalAssetRequirements |
                    V2.ISA95JobOrderDataTypeFields.MaterialRequirements),
                Description = [new LocalizedText("en-US", "Batch authoring")],
                Priority = priority,
                StartTime = s_start,
                EndTime = s_start.Add(TimeSpan.FromHours(1)),
                WorkMasterID =
                [
                    new V2.ISA95WorkMasterDataType
                    {
                        ID = "recipe-42",
                        EncodingMask = (uint)V2.ISA95WorkMasterDataTypeFields.Parameters,
                        Parameters = [new V2.ISA95ParameterDataType { ID = "enabled", Value = Variant.From(true) }]
                    }
                ],
                JobOrderParameters =
                [
                    new V2.ISA95ParameterDataType { ID = "temperature", Value = Variant.From(175.5) },
                    new V2.ISA95ParameterDataType { ID = "sequence", Value = Variant.From(ulong.MaxValue) },
                    new V2.ISA95ParameterDataType { ID = "note", Value = Variant.From("private-batch-note") }
                ],
                PersonnelRequirements = [new V2.ISA95PersonnelDataType { ID = "operator-A" }],
                EquipmentRequirements = [new V2.ISA95EquipmentDataType { ID = "reactor-1" }],
                PhysicalAssetRequirements = [new V2.ISA95PhysicalAssetDataType { ID = "vessel-1" }],
                MaterialRequirements = [new V2.ISA95MaterialDataType
                {
                    EncodingMask = (uint)V2.ISA95MaterialDataTypeFields.MaterialLotID,
                    MaterialLotID = "lot-100"
                }]
            };
        }

        private const string kJobId = "Production/Batch-007";
        private static readonly DateTimeUtc s_start = new(2026, 9, 12, 8);

        private sealed class JobModel
        {
            public JobModel(
                bool version2, bool present = true, bool completeBinding = true, bool exposeMethods = true)
            {
                Version2 = version2;
                Session = new IndustrialCompanionTestSession();
                string version = version2 ? "V2 " : "V1 ";
                NamespaceUri = version2 ? V2.Namespaces.ISA95JobControlV2 : V1.Namespaces.ISA95JobControlV1;
                Parent = Session.Id(version + "job-control");
                Session.AddObject(Parent, ObjectTypeIds.FolderType);
                Session.AddChild(ObjectIds.ObjectsFolder, Parent);
                NodeId order = Session.Id(version + "order");
                NodeId response = Session.Id(version + "response");
                NodeId receiver = Session.Id(version + "receiver");
                Session.AddObject(order, version2
                    ? V2.ObjectTypeIds.ISA95JobOrderReceiverObjectType
                    : V1.ObjectTypeIds.ISA95JobOrderReceiverObjectType);
                Session.AddObject(response, version2
                    ? V2.ObjectTypeIds.ISA95JobResponseProviderObjectType
                    : V1.ObjectTypeIds.ISA95JobResponseProviderObjectType);
                Session.AddObject(receiver, version2
                    ? V2.ObjectTypeIds.ISA95JobResponseReceiverObjectType
                    : V1.ObjectTypeIds.ISA95JobResponseReceiverObjectType);
                Session.AddChild(Parent, order);
                Session.AddChild(Parent, response);
                if (completeBinding)
                {
                    Session.AddChild(Parent, receiver);
                }
                Order = new CompanionTarget("isa95", order, version + "orders", version + "order receiver");
                Response = new CompanionTarget(
                    "isa95", response, version + "responses", version + "response provider");
                Receiver = new CompanionTarget("isa95", receiver, version + "receiver", version + "response receiver");
                CatalogId = Session.AddProperty(order, NamespaceUri, "JobOrderList", default);
                SetCatalog(present);
                if (exposeMethods)
                {
                    ArrayOf<string> names = version2
                        ? ["Store", "Start", "Update", "Pause", "Resume", "Abort", "Cancel", "Clear"]
                        : ["ReceiveJobOrder"];
                    foreach (string name in names)
                    {
                        AddMethod(name);
                    }
                    AddMethod(version2 ? "RequestJobResponseByJobOrderID" : "RequestJobResponse", response: true);
                }
            }

            public bool Version2 { get; }

            public string NamespaceUri { get; }

            public IndustrialCompanionTestSession Session { get; }

            public CompanionContext Context => Session.Context();

            public NodeId Parent { get; }

            public NodeId CatalogId { get; }

            public CompanionTarget Order { get; }

            public CompanionTarget Response { get; }

            public CompanionTarget Receiver { get; }

            public Variant CreateOrder(short priority = 7)
            {
                return Version2
                    ? Variant.FromStructure(CreateV2Order(priority))
                    : Variant.FromStructure(CreateV1Order(priority));
            }

            public ArrayOf<CompanionValue> Inputs(string operationId, Variant job = default)
            {
                bool authored = operationId is "store-job" or "update-job";
                var request = new CompanionValue(
                    authored ? "jobOrder" : "jobOrderId",
                    authored ? job.IsNull ? CreateOrder() : job : Variant.From(kJobId));
                if (!Version2 || operationId is "request-job-response" or "observe-job-status")
                {
                    return [request];
                }
                return
                [
                    request,
                    new("comment", Variant.From(ArrayOf.Wrapped(
                    [
                        new LocalizedText("en-US", "Authorized batch operation"),
                        new LocalizedText("de-DE", "Freigegeben")
                    ])))
                ];
            }

            public void SetCatalog(bool present, uint state = 1, short priority = 7)
            {
                if (present)
                {
                    SetOrder(CreateOrder(priority), state);
                }
                else
                {
                    Session.SetValue(CatalogId, Version2
                        ? Variant.FromStructure(ArrayOf<V2.ISA95JobOrderAndStateDataType>.Empty)
                        : Variant.FromStructure(ArrayOf<V1.ISA95JobOrderDataType>.Empty));
                }
            }

            public void SetOrder(Variant order, uint state = 1)
            {
                if (Version2)
                {
                    Assert.That(order.TryGetValue<V2.ISA95JobOrderDataType>(
                        out V2.ISA95JobOrderDataType? job, Session.MessageContext), Is.True);
                    Session.SetValue(CatalogId, Variant.FromStructure(ArrayOf.Wrapped(
                    [
                        new V2.ISA95JobOrderAndStateDataType
                        {
                            JobOrder = job!,
                            State = [new V2.ISA95StateDataType
                            {
                                BrowsePath = new RelativePath(),
                                StateNumber = state,
                                StateText = new LocalizedText("Server state")
                            }]
                        }
                    ])));
                }
                else
                {
                    Assert.That(order.TryGetValue<V1.ISA95JobOrderDataType>(
                        out V1.ISA95JobOrderDataType? job, Session.MessageContext), Is.True);
                    Session.SetValue(CatalogId, Variant.FromStructure(ArrayOf.Wrapped([job!])));
                }
            }

            public NodeId AddMethod(string name, bool response = false)
            {
                NodeId method = Session.AddProperty(
                    response ? Response.NodeId : Order.NodeId, NamespaceUri, name, default);
                Session.SetValue(method, Variant.From((int)NodeClass.Method), Attributes.NodeClass);
                Session.SetValue(method, Variant.From(true), Attributes.Executable);
                Session.SetValue(method, Variant.From(true), Attributes.UserExecutable);
                m_methods[name] = method;
                return method;
            }

            public NodeId Method(string name)
            {
                return m_methods.TryGetValue(name, out NodeId method)
                    ? method
                    : throw new AssertionException("The test method was not configured: " + name);
            }

            public NodeId DeclarationMethod(string name)
            {
                ExpandedNodeId id = name switch
                {
                    "ReceiveJobOrder" => V1.MethodIds.ISA95JobOrderReceiverObjectType_ReceiveJobOrder,
                    "RequestJobResponse" => V1.MethodIds.ISA95JobResponseProviderObjectType_RequestJobResponse,
                    "RequestJobResponseByJobOrderID" =>
                        V2.MethodIds.ISA95JobResponseProviderObjectType_RequestJobResponseByJobOrderID,
                    "Store" => V2.MethodIds.ISA95JobOrderReceiverObjectType_Store,
                    "Start" => V2.MethodIds.ISA95JobOrderReceiverObjectType_Start,
                    "Update" => V2.MethodIds.ISA95JobOrderReceiverObjectType_Update,
                    "Pause" => V2.MethodIds.ISA95JobOrderReceiverObjectType_Pause,
                    "Resume" => V2.MethodIds.ISA95JobOrderReceiverObjectType_Resume,
                    "Abort" => V2.MethodIds.ISA95JobOrderReceiverObjectType_Abort,
                    "Cancel" => V2.MethodIds.ISA95JobOrderReceiverObjectType_Cancel,
                    "Clear" => V2.MethodIds.ISA95JobOrderReceiverObjectType_Clear,
                    _ => throw new AssertionException("No declaration for the test method: " + name)
                };
                return Session.Resolve(id);
            }

            private readonly Dictionary<string, NodeId> m_methods = new(StringComparer.Ordinal);
        }
    }
}
