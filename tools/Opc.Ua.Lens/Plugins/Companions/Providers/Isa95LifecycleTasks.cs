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
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.ISA95.Client;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed partial class Isa95CompanionProvider
    {
        public async ValueTask<CompanionTaskInput> PrepareInputAsync(
            CompanionContext context,
            CompanionTarget target,
            string operationId,
            ArrayOf<CompanionValue> inputs,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(target);
            cancellationToken.ThrowIfCancellationRequested();
            bool version2 = IsV2(target);
            var task = Isa95JobTask.Find(version2, operationId);
            RequireTaskTarget(target, task);
            CheckResultBudget(context, task);
            var client = new Isa95Client(context.Session, context.Telemetry);
            JobRequest request = SnapshotRequest(
                client.Session.MessageContext, version2, task, inputs, context.MaxFields);
            await IndustrialCompanionAccess.RequireTargetAsync(
                context, target, Descriptor.Id, s_types, cancellationToken).ConfigureAwait(false);
            Isa95TaskBinding binding = await FindBindingAsync(context, target, cancellationToken).ConfigureAwait(false)
                ?? throw IndustrialCompanionAccess.Unsupported(
                    "A unique complete Job Control endpoint set is required.");
            Isa95TaskObservation? observation = null;
            if (operationId != "request-job-response")
            {
                observation = await ReadObservationAsync(
                    context, binding, version2, request.JobOrderId, cancellationToken).ConfigureAwait(false);
                if (operationId == "store-job" && observation.Present)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEntryExists, "The job already exists. Store does not replace an existing job.");
                }
                if (operationId != "store-job" && !observation.Present)
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound, "The job is absent from JobOrderList.");
                }
            }
            NodeId methodId = NodeId.Null;
            if (task.MethodName.Length != 0)
            {
                Isa95TaskMethod method = await Isa95TaskAccess.ReadMethodAsync(
                    context, target.NodeId, version2, task.MethodName, cancellationToken).ConfigureAwait(false);
                method.RequireAvailable();
                methodId = method.NodeId;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = new Isa95PreparedTask(
                target, version2, task, binding, methodId, request.JobOrderId,
                request.JobOrder, request.Comment, observation, context.Session.MessageContext);
            cancellationToken.ThrowIfCancellationRequested();
            return prepared;
        }

        public async ValueTask<CompanionOperationResult> ExecutePreparedAsync(
            CompanionContext context,
            CompanionTarget target,
            string operationId,
            CompanionTaskInput input,
            IProgress<CompanionTaskProgress>? progress,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(target);
            cancellationToken.ThrowIfCancellationRequested();
            if (input is not Isa95PreparedTask prepared ||
                prepared.Target != target ||
                prepared.OperationId != operationId ||
                prepared.Version2 != IsV2(target))
            {
                throw new ArgumentException("Prepare this exact ISA-95 target, version and operation first.",
                    nameof(input));
            }
            var task = Isa95JobTask.Find(prepared.Version2, operationId);
            RequireTaskTarget(target, task);
            CheckResultBudget(context, task);
            var client = new Isa95Client(context.Session, context.Telemetry);
            await IndustrialCompanionAccess.RequireTargetAsync(
                context, target, Descriptor.Id, s_types, cancellationToken).ConfigureAwait(false);
            Isa95TaskBinding? binding = await FindBindingAsync(context, target, cancellationToken)
                .ConfigureAwait(false);
            if (binding != prepared.Binding)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidState, "The prepared endpoint association changed. Prepare again.");
            }
            Isa95TaskObservation? observation = null;
            if (operationId != "request-job-response")
            {
                observation = await ReadObservationAsync(
                    context, prepared.Binding, prepared.Version2, prepared.JobOrderId, cancellationToken)
                    .ConfigureAwait(false);
                if (task.IsMutation && (prepared.Observation is null || !prepared.Observation.Matches(observation)))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The observed job order or state changed. Prepare again.");
                }
            }
            if (task.MethodName.Length != 0)
            {
                Isa95TaskMethod method = await Isa95TaskAccess.ReadMethodAsync(
                    context, target.NodeId, prepared.Version2, task.MethodName, cancellationToken)
                    .ConfigureAwait(false);
                method.RequireAvailable();
                if (method.NodeId != prepared.MethodId)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "The prepared method instance changed. Prepare again.");
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (operationId == "request-job-response")
            {
                return await RequestResponseAsync(context, client, prepared, cancellationToken).ConfigureAwait(false);
            }
            if (operationId == "observe-job-status")
            {
                if (observation is null || !observation.Present)
                {
                    throw new ServiceResultException(StatusCodes.BadNotFound, "The job is absent from JobOrderList.");
                }
                return new CompanionOperationResult(
                    "A fresh server job-order/state snapshot was read. No lifecycle method was invoked.",
                    ObservationValues(context, prepared, observation));
            }

            progress?.Report(new CompanionTaskProgress("Invoking " + task.Operation.DisplayName + " once."));
            cancellationToken.ThrowIfCancellationRequested();
            ulong status = await InvokeMutationAsync(client, task, prepared, cancellationToken).ConfigureAwait(false);
            Isa95TaskAccess.CheckJobStatus(status);
            progress?.Report(new CompanionTaskProgress(
                "ReturnStatus indicates success. Reading server state; do not retry if observation fails."));
            Isa95TaskObservation after;
            try
            {
                after = await ReadObservationAsync(
                    context, prepared.Binding, prepared.Version2, prepared.JobOrderId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException failure)
            {
                throw new ServiceResultException(failure.StatusCode,
                    "The method returned ReturnStatus Success, but reading the subsequent server state failed. " +
                    "Its outcome is not confirmed; do not retry automatically.", failure);
            }
            if (!after.Present && operationId != "clear-job")
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    "The method returned ReturnStatus Success, but the job is not observable. " +
                    "Its outcome is not confirmed; do not retry automatically.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            ArrayOf<CompanionValue> values = ObservationValues(context, prepared, after);
            IndustrialCompanionAccess.CheckFields(context, values.Count + 1);
            return new CompanionOperationResult(
                $"The server returned ReturnStatus Success for {task.Operation.DisplayName}. " +
                "Displayed values are fresh server observations, not inferred execution, completion or removal. " +
                (prepared.Version2 ? string.Empty : "V1 does not expose live execution state in its catalog. ") +
                "No accepted or ambiguous request was replayed.",
                [new("ISA-95 return status", Variant.From(status)), .. values]);
        }

        private static async ValueTask AddJobOperationsAsync(
            CompanionContext context,
            CompanionTarget target,
            bool catalogAvailable,
            List<CompanionOperation> operations,
            List<CompanionValue> values,
            CancellationToken cancellationToken)
        {
            bool version2 = IsV2(target);
            bool order = IsOrderReceiver(target);
            if (!order && !IsResponseProvider(target))
            {
                return;
            }
            string sampleMethod = order ? version2 ? "Store" : "ReceiveJobOrder" :
                version2 ? "RequestJobResponseByJobOrderID" : "RequestJobResponse";
            Isa95TaskMethod sample = await Isa95TaskAccess.ReadMethodAsync(
                context, target.NodeId, version2, sampleMethod, cancellationToken).ConfigureAwait(false);
            var methods = new Dictionary<string, Isa95TaskMethod>(StringComparer.Ordinal)
            {
                [sampleMethod] = sample
            };
            if (StatusCode.IsGood(sample.Status))
            {
                operations.Add(order
                    ? new CompanionOperation("store-sample-job", "Store an empty sample job (do not start)",
                        CompanionOperationSafety.SampleMutation, kSampleHint)
                    : new CompanionOperation("request-sample-response", "Request the sample job response",
                        CompanionOperationSafety.ReadOnly, kSampleHint));
            }
            else
            {
                values.Add(new CompanionValue("Sample method availability", Variant.From(sample.Status)));
            }
            if (order && !catalogAvailable)
            {
                values.Add(new CompanionValue("Deployment task availability", Variant.From(StatusCodes.BadNotFound)));
                return;
            }
            ArrayOf<Isa95JobTask> tasks = Isa95JobTask.ForVersion(version2);
            for (int index = 0; index < tasks.Count; index++)
            {
                Isa95JobTask task = tasks[index];
                bool responseTask = task.Operation.Id == "request-job-response";
                if (order == responseTask)
                {
                    continue;
                }
                if (task.MethodName.Length != 0)
                {
                    if (!methods.TryGetValue(task.MethodName, out Isa95TaskMethod? method))
                    {
                        method = await Isa95TaskAccess.ReadMethodAsync(
                            context, target.NodeId, version2, task.MethodName, cancellationToken)
                            .ConfigureAwait(false);
                        methods.Add(task.MethodName, method);
                    }
                    values.Add(new CompanionValue(task.Operation.DisplayName + " availability",
                        StatusCode.IsGood(method.Status) ? Variant.From(method.NodeId) : Variant.From(method.Status)));
                    if (!StatusCode.IsGood(method.Status))
                    {
                        continue;
                    }
                }
                operations.Add(task.Operation);
            }
        }

        private static JobRequest SnapshotRequest(
            IServiceMessageContext messageContext,
            bool version2,
            Isa95JobTask task,
            ArrayOf<CompanionValue> inputs,
            int maximumFields)
        {
            if (inputs.Count != task.Operation.Inputs.Count)
            {
                throw new ArgumentException("Supply the exact typed ISA-95 task fields.", nameof(inputs));
            }
            for (int index = 0; index < inputs.Count; index++)
            {
                CompanionInputDefinition definition = task.Operation.Inputs[index];
                CompanionValue value = inputs[index];
                if (value is null ||
                    value.Name != definition.Name ||
                    value.Value.TypeInfo.BuiltInType != definition.DataType ||
                    value.Value.TypeInfo.ValueRank != definition.ValueRank)
                {
                    throw new ArgumentException("The ISA-95 field name, type or rank is incorrect.", nameof(inputs));
                }
            }
            Variant request = Isa95PreparedTask.Decode(
                Isa95PreparedTask.Encode(inputs[0].Value, messageContext), messageContext);
            string? jobId;
            if (task.UsesJobOrder)
            {
                if (version2 &&
                    request.TryGetValue<V2.ISA95JobOrderDataType>(
                        out V2.ISA95JobOrderDataType? v2, messageContext) &&
                    v2 is not null)
                {
                    jobId = v2.JobOrderID;
                    request = Variant.FromStructure(v2);
                }
                else if (!version2 &&
                    request.TryGetValue<V1.ISA95JobOrderDataType>(
                        out V1.ISA95JobOrderDataType? v1, messageContext) &&
                    v1 is not null)
                {
                    jobId = v1.ID;
                    request = Variant.FromStructure(v1);
                }
                else
                {
                    throw new ServiceResultException(
                        StatusCodes.BadTypeMismatch, "The job structure does not match the selected ISA-95 version.");
                }
            }
            else if (!request.TryGetValue(out jobId))
            {
                throw new ArgumentException("A scalar job order ID is required.", nameof(inputs));
            }
            RequireJobId(jobId);
            Variant comment = default;
            if (version2 && task.IsMutation)
            {
                comment = Isa95PreparedTask.Decode(
                    Isa95PreparedTask.Encode(inputs[1].Value, messageContext), messageContext);
                if (!comment.TryGetValue(out ArrayOf<LocalizedText> comments))
                {
                    throw new ArgumentException("Comments must be a LocalizedText array.", nameof(inputs));
                }
                CellCompanionSupport.CheckCount(comments.Count, maximumFields, "ISA-95 comments");
            }
            return new JobRequest(jobId!, request, comment);
        }

        private static async ValueTask<ulong> InvokeMutationAsync(
            Isa95Client client,
            Isa95JobTask task,
            Isa95PreparedTask prepared,
            CancellationToken cancellationToken)
        {
            Variant request = prepared.ReadRequest(client.Session.MessageContext);
            if (prepared.Version2)
            {
                Isa95JobControlV2Client jobs = client.CreateJobControlV2Client(prepared.Binding.OrderReceiver,
                    prepared.Binding.ResponseProvider, prepared.Binding.ResponseReceiver);
                if (!prepared.ReadComment(client.Session.MessageContext).TryGetValue(
                    out ArrayOf<LocalizedText> comments))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Typed comments are required.");
                }
                if (task.UsesJobOrder)
                {
                    if (!request.TryGetValue<V2.ISA95JobOrderDataType>(
                        out V2.ISA95JobOrderDataType? job, client.Session.MessageContext) ||
                        job is null)
                    {
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch, "A prepared V2 job is required.");
                    }
                    return task.Operation.Id == "store-job"
                        ? await jobs.StoreAsync(job, comments, cancellationToken).ConfigureAwait(false)
                        : await jobs.UpdateAsync(job, comments, cancellationToken).ConfigureAwait(false);
                }
                return task.Operation.Id switch
                {
                    "start-job" => await jobs.StartAsync(prepared.JobOrderId, comments, cancellationToken)
                        .ConfigureAwait(false),
                    "cancel-job" => await jobs.CancelAsync(prepared.JobOrderId, comments, cancellationToken)
                        .ConfigureAwait(false),
                    "clear-job" => await jobs.ClearAsync(prepared.JobOrderId, comments, cancellationToken)
                        .ConfigureAwait(false),
                    "pause-job" => await jobs.PauseAsync(prepared.JobOrderId, comments, cancellationToken)
                        .ConfigureAwait(false),
                    "resume-job" => await jobs.ResumeAsync(prepared.JobOrderId, comments, cancellationToken)
                        .ConfigureAwait(false),
                    "abort-job" => await jobs.AbortAsync(prepared.JobOrderId, comments, cancellationToken)
                        .ConfigureAwait(false),
                    _ => throw IndustrialCompanionAccess.Unsupported("The V2 lifecycle command is unavailable.")
                };
            }
            Isa95JobControlV1Client v1 = client.CreateJobControlV1Client(prepared.Binding.OrderReceiver,
                prepared.Binding.ResponseProvider, prepared.Binding.ResponseReceiver);
            V1.ISA95JobOrderDataType order = new() { ID = prepared.JobOrderId };
            if (task.UsesJobOrder)
            {
                if (!request.TryGetValue<V1.ISA95JobOrderDataType>(
                    out V1.ISA95JobOrderDataType? decoded, client.Session.MessageContext) ||
                    decoded is null)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch, "A prepared V1 job is required.");
                }
                order = decoded;
            }
            return await v1.ReceiveJobOrderAsync(task.V1Command, order, cancellationToken).ConfigureAwait(false);
        }

        private static async ValueTask<CompanionOperationResult> RequestResponseAsync(
            CompanionContext context,
            Isa95Client client,
            Isa95PreparedTask prepared,
            CancellationToken cancellationToken)
        {
            ArrayOf<Variant> outputs;
            if (prepared.Version2)
            {
                Isa95JobControlV2Client jobs = client.CreateJobControlV2Client(prepared.Binding.OrderReceiver,
                    prepared.Binding.ResponseProvider, prepared.Binding.ResponseReceiver);
                (V2.ISA95JobResponseDataType returnedResponse, ulong status) =
                    await jobs.RequestJobResponseByJobOrderIdAsync(prepared.JobOrderId, cancellationToken)
                        .ConfigureAwait(false);
                Isa95TaskAccess.CheckJobStatus(status);
                outputs = [Variant.FromStructure(returnedResponse), Variant.From(status)];
            }
            else
            {
                Isa95JobControlV1Client jobs = client.CreateJobControlV1Client(prepared.Binding.OrderReceiver,
                    prepared.Binding.ResponseProvider, prepared.Binding.ResponseReceiver);
                (ArrayOf<V1.ISA95JobResponseDataType> returnedResponses, ulong status) =
                    await jobs.RequestJobResponseAsync(
                        prepared.JobOrderId, V1.ISA95JobOrderStateEnum.Undefined, cancellationToken)
                            .ConfigureAwait(false);
                Isa95TaskAccess.CheckJobStatus(status);
                outputs = [Variant.FromStructure(returnedResponses), Variant.From(status)];
            }
            Variant responses;
            int count;
            if (prepared.Version2 &&
                outputs[0].TryGetValue<V2.ISA95JobResponseDataType>(
                    out V2.ISA95JobResponseDataType? response, context.Session.MessageContext) &&
                response is not null)
            {
                RequireResponseJob(prepared.JobOrderId, response.JobOrderID);
                if (response.JobState.IsEmpty)
                {
                    throw new ServiceResultException(StatusCodes.BadNoData, "The response contains no job state.");
                }
                IndustrialCompanionAccess.CheckFields(context, response.JobState.Count);
                foreach (V2.ISA95StateDataType state in response.JobState)
                {
                    if (state is null)
                    {
                        throw new ServiceResultException(StatusCodes.BadDecodingError, "A response state is null.");
                    }
                }
                responses = Variant.FromStructure(response);
                count = 1;
            }
            else if (!prepared.Version2 &&
                outputs[0].TryGetStructure(context.Session.MessageContext,
                    out ArrayOf<V1.ISA95JobResponseDataType> v1Responses))
            {
                if (v1Responses.IsEmpty)
                {
                    throw new ServiceResultException(StatusCodes.BadNoData, "No job responses are available.");
                }
                IndustrialCompanionAccess.CheckFields(context, v1Responses.Count);
                foreach (V1.ISA95JobResponseDataType item in v1Responses)
                {
                    if (item is null)
                    {
                        throw new ServiceResultException(StatusCodes.BadDecodingError, "A job response is null.");
                    }
                    RequireResponseJob(prepared.JobOrderId, item.JobOrderID);
                }
                responses = Variant.FromStructure(v1Responses);
                count = v1Responses.Count;
            }
            else
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "The response has the wrong ISA-95 type.");
            }
            _ = Isa95PreparedTask.Encode(responses, context.Session.MessageContext);
            IndustrialCompanionAccess.CheckFields(context, 4);
            cancellationToken.ThrowIfCancellationRequested();
            return new CompanionOperationResult(
                "The server returned typed responses for the exact job ID. Responses may be historical; " +
                "their states are not asserted to be the current execution state.",
                [
                    new("Job order ID", Variant.From(prepared.JobOrderId)),
                    new("ISA-95 return status", outputs[1]),
                    new("Response count", Variant.From(count)),
                    new("Job responses", responses)
                ]);
        }

        private static async ValueTask<JobCatalog?> ReadCatalogAsync(
            CompanionContext context,
            NodeId receiver,
            bool version2,
            bool required,
            CancellationToken cancellationToken)
        {
            NodeId catalogId = await IndustrialCompanionAccess.ResolveChildAsync(
                context, receiver, Isa95TaskAccess.NamespaceUri(version2), "JobOrderList",
                !required, cancellationToken)
                .ConfigureAwait(false);
            if (catalogId.IsNull)
            {
                return null;
            }
            ReadResponse read = await context.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [new ReadValueId { NodeId = catalogId, AttributeId = Attributes.Value }], cancellationToken)
                .ConfigureAwait(false);
            Isa95TaskAccess.RequireGoodHeader(read.ResponseHeader);
            if (read.Results.Count != 1)
            {
                throw new ServiceResultException(StatusCodes.BadUnexpectedError, "Incomplete JobOrderList read.");
            }
            Isa95TaskAccess.RequireGood(read.Results[0].StatusCode);
            Variant orders = read.Results[0].WrappedValue;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int count;
            if (version2 &&
                orders.TryGetStructure(
                    context.Session.MessageContext, out ArrayOf<V2.ISA95JobOrderAndStateDataType> v2Orders) &&
                !v2Orders.IsNull)
            {
                count = v2Orders.Count;
                IndustrialCompanionAccess.CheckFields(context, count);
                foreach (V2.ISA95JobOrderAndStateDataType order in v2Orders)
                {
                    RequireCatalogId(ids, order?.JobOrder?.JobOrderID);
                }
                orders = Variant.FromStructure(v2Orders);
            }
            else if (!version2 &&
                orders.TryGetStructure(
                    context.Session.MessageContext, out ArrayOf<V1.ISA95JobOrderDataType> v1Orders) &&
                !v1Orders.IsNull)
            {
                count = v1Orders.Count;
                IndustrialCompanionAccess.CheckFields(context, count);
                foreach (V1.ISA95JobOrderDataType order in v1Orders)
                {
                    RequireCatalogId(ids, order?.ID);
                }
                orders = Variant.FromStructure(v1Orders);
            }
            else
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch, "JobOrderList is null or has the wrong ISA-95 version/type.");
            }
            _ = Isa95PreparedTask.Encode(orders, context.Session.MessageContext);
            cancellationToken.ThrowIfCancellationRequested();
            return new JobCatalog(catalogId, orders, count);
        }

        private static async ValueTask<Isa95TaskObservation> ReadObservationAsync(
            CompanionContext context,
            Isa95TaskBinding binding,
            bool version2,
            string jobOrderId,
            CancellationToken cancellationToken)
        {
            JobCatalog catalog = await ReadCatalogAsync(
                context, binding.OrderReceiver, version2, true, cancellationToken).ConfigureAwait(false)
                ?? throw new ServiceResultException(StatusCodes.BadNotFound, "JobOrderList is unavailable.");
            IServiceMessageContext messageContext = context.Session.MessageContext;
            if (version2 &&
                catalog.Orders.TryGetStructure(
                    messageContext, out ArrayOf<V2.ISA95JobOrderAndStateDataType> v2Orders))
            {
                foreach (V2.ISA95JobOrderAndStateDataType order in v2Orders)
                {
                    if (order.JobOrder.JobOrderID != jobOrderId)
                    {
                        continue;
                    }
                    if (order.State.IsEmpty)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNoData, "The V2 job has no observable execution state.");
                    }
                    IndustrialCompanionAccess.CheckFields(context, order.State.Count);
                    var summary = new StringBuilder("Present; V2 state numbers:");
                    foreach (V2.ISA95StateDataType state in order.State)
                    {
                        if (state is null)
                        {
                            throw new ServiceResultException(StatusCodes.BadDecodingError, "A V2 job state is null.");
                        }
                        summary.Append(' ').Append(state.StateNumber.ToString(CultureInfo.InvariantCulture));
                    }
                    summary.Append(". This is an observation, not a promised transition.");
                    return new Isa95TaskObservation(
                        catalog.NodeId, true, Variant.FromStructure(order), summary.ToString(), messageContext);
                }
            }
            else if (!version2 &&
                catalog.Orders.TryGetStructure(messageContext, out ArrayOf<V1.ISA95JobOrderDataType> v1Orders))
            {
                foreach (V1.ISA95JobOrderDataType order in v1Orders)
                {
                    if (order.ID == jobOrderId)
                    {
                        return new Isa95TaskObservation(
                            catalog.NodeId, true, Variant.FromStructure(order),
                            "Present; V1 exposes the stored order but not its live execution state.", messageContext);
                    }
                }
            }
            return new Isa95TaskObservation(catalog.NodeId, false, Variant.From(StatusCodes.BadNotFound),
                "The exact job ID is absent from the server's readable JobOrderList.", messageContext);
        }

        private static ArrayOf<CompanionValue> ObservationValues(
            CompanionContext context,
            Isa95PreparedTask prepared,
            Isa95TaskObservation observation)
        {
            IndustrialCompanionAccess.CheckFields(context, 5);
            return
            [
                new("Job order ID", Variant.From(prepared.JobOrderId)),
                new("ISA-95 version", Variant.From(prepared.Version2 ? "V2" : "V1")),
                new("Job present", Variant.From(observation.Present)),
                new("Observed job", observation.ReadValue(context.Session.MessageContext)),
                new("State observation", Variant.From(observation.Summary))
            ];
        }

        private static void RequireTaskTarget(CompanionTarget target, Isa95JobTask task)
        {
            if (!IsJobEndpoint(target) ||
                (task.Operation.Id == "request-job-response" ? !IsResponseProvider(target) : !IsOrderReceiver(target)))
            {
                throw IndustrialCompanionAccess.Unsupported("Select the matching Job Control endpoint for this task.");
            }
        }

        private static void CheckResultBudget(CompanionContext context, Isa95JobTask task)
        {
            IndustrialCompanionAccess.CheckFields(
                context, task.IsMutation ? 6 : task.Operation.Id == "request-job-response" ? 4 : 5);
        }

        private static void RequireJobId(string? jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId) || jobId.Length > 256)
            {
                throw new ArgumentException("A job order ID must contain 1-256 characters.", nameof(jobId));
            }
            foreach (char character in jobId)
            {
                if (char.IsControl(character))
                {
                    throw new ArgumentException("A job order ID cannot contain control characters.", nameof(jobId));
                }
            }
        }

        private static void RequireCatalogId(HashSet<string> ids, string? jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId) || !ids.Add(jobId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError, "JobOrderList has a null, empty or duplicate job identity.");
            }
        }

        private static void RequireResponseJob(string requestedId, string? returnedId)
        {
            if (requestedId != returnedId)
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError, "A response for a different job was returned.");
            }
        }

        private sealed record JobCatalog(NodeId NodeId, Variant Orders, int Count);

        private sealed record JobRequest(string JobOrderId, Variant JobOrder, Variant Comment);
    }
}
