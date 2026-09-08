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
using System.Linq;
using System.Threading;
using Opc.Ua.Server.Fluent;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    public sealed partial class WotProjectionBindingRuntime
    {
        private void WireProjectedMethods(ArrayOf<WotBindingPlan> plans)
        {
            var methods = new HashSet<NodeId>();
            var conditionMethods = new HashSet<(WotProjectedEventBinding Event, string Action)>();
            foreach (WotBindingPlan plan in plans)
            {
                if (plan.IsDeclarationContext)
                {
                    continue;
                }
                foreach (WotProjectedAffordance local in plan.ProjectedAffordances)
                {
                    if (local.Kind != WotAffordanceKind.Action)
                    {
                        continue;
                    }
                    WotCompiledForm form = SelectForm(plan, local, WoTBindingCapabilityEnum.InvokeAction);
                    NodeId methodId = ResolveLocalNodeId(local.NodeId, plan.ResourceXid, local.JsonPointer);
                    INodeBuilder<MethodState> builder = m_builder.Node<MethodState>(methodId);
                    if (!methods.Add(methodId))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError,
                            "More than one projected action owns local Method '{0}'.", methodId);
                    }
                    WotProjectedEventBinding? condition = null;
                    if (local.ConditionAction is not null)
                    {
                        WotProjectedAffordance? declaration = plan.ProjectedAffordances.Find(
                            item => item.Kind == WotAffordanceKind.Event && item.Name == local.ActsOn);
                        if (declaration is null ||
                            !m_events.TryGetValue((plan.ResourceXid, declaration.JsonPointer), out condition) ||
                            condition.Condition is null ||
                            !s_conditionActions.Contains(local.ConditionAction))
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadConfigurationError,
                                "Action '{0}' does not identify a projected Section 13 Condition.", local.JsonPointer);
                        }
                        if (!conditionMethods.Add((condition, local.ConditionAction)))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadConfigurationError,
                                "A Condition Method has more than one owning action.");
                        }
                    }
                    WotBindingChannelSlot slot = GetOrCreateSlot(form);
                    builder.OnCall(BuildMethodHandler(builder.Node, slot, condition, local.ConditionAction));
                    if (condition?.Condition is { } state)
                    {
                        MethodState standard = state.FindChild(
                            m_builder.Context, QualifiedName.From(local.ConditionAction!)) as MethodState
                            ?? throw new ServiceResultException(
                                StatusCodes.BadConfigurationError, "The local Condition lacks its declared Method.");
                        standard.RolePermissions = builder.Node.RolePermissions;
                        standard.UserRolePermissions = builder.Node.UserRolePermissions;
                        standard.AccessRestrictions = builder.Node.AccessRestrictions;
                        standard.Executable = builder.Node.Executable;
                        standard.UserExecutable = builder.Node.UserExecutable;
                        standard.MethodDeclarationId = GetConditionMethodDeclaration(local.ConditionAction!);
                        if (standard is AddCommentMethodState commentMethod)
                        {
                            // Generated typed handlers precede the fluent hook.
                            // A proxy must not run the default local transition.
                            commentMethod.OnCall = null;
                            commentMethod.OnCallAsync = null;
                        }
                        m_builder.Node(standard.NodeId).OnCall(
                            BuildMethodHandler(standard, slot, condition, local.ConditionAction));
                    }
                }
            }
            DisableUnboundConditionMethods(conditionMethods);
        }

        private void DisableUnboundConditionMethods(
            HashSet<(WotProjectedEventBinding Event, string Action)> conditionMethods)
        {
            foreach (WotProjectedEventBinding binding in m_events.Values)
            {
                if (binding.Condition is not { } condition)
                {
                    continue;
                }
                foreach (string action in s_conditionActions)
                {
                    if (!conditionMethods.Contains((binding, action)) &&
                        condition.FindChild(m_builder.Context, QualifiedName.From(action)) is MethodState method)
                    {
                        // An unbound proxy Method must not mutate only the local
                        // copy and report a successful source-side operation.
                        method.Executable = false;
                        method.UserExecutable = false;
                    }
                }
            }
        }

        private static NodeId GetConditionMethodDeclaration(string action)
        {
            return action switch
            {
                "Acknowledge" => Ua.MethodIds.AcknowledgeableConditionType_Acknowledge,
                "Confirm" => Ua.MethodIds.AcknowledgeableConditionType_Confirm,
                "AddComment" => Ua.MethodIds.ConditionType_AddComment,
                "Enable" => Ua.MethodIds.ConditionType_Enable,
                "Disable" => Ua.MethodIds.ConditionType_Disable,
                _ => throw new ServiceResultException(StatusCodes.BadNotSupported)
            };
        }

        private static WotCompiledForm SelectForm(
            WotBindingPlan plan,
            WotProjectedAffordance local,
            WoTBindingCapabilityEnum operation)
        {
            // Prepare preserves authored alternative order after applying binder
            // selection and security validation. Never retry a different source.
            foreach (WotCompiledForm form in plan.CompiledForms)
            {
                if (form.IsExecutable && form.Operation == operation &&
                    form.AffordanceKind == local.Kind &&
                    form.JsonPointer.StartsWith(local.JsonPointer + "/forms/", StringComparison.Ordinal))
                {
                    return form;
                }
            }
            throw ServiceResultException.Create(
                StatusCodes.BadConfigurationError,
                "Resource '{0}' declaration '{1}' has no executable '{2}' binding.",
                plan.ResourceXid, local.JsonPointer, operation);
        }

        private NodeId ResolveLocalNodeId(string text, string resourceXid, string pointer)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Resource '{0}' declaration '{1}' has no resolved local identity.",
                    resourceXid, pointer);
            }
            NodeId nodeId = ExpandedNodeId.Parse(text, m_builder.Context.NamespaceUris);
            if (nodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "Resource '{0}' declaration '{1}' has an invalid local identity '{2}'.",
                    resourceXid, pointer, text);
            }
            return nodeId;
        }

        private GenericMethodCalledEventHandler2Async BuildMethodHandler(
            MethodState method,
            WotBindingChannelSlot slot,
            WotProjectedEventBinding? condition,
            string? conditionAction)
        {
            ArrayOf<ArgumentSignature> inputs = CaptureSignature(method.InputArguments);
            ArrayOf<ArgumentSignature> outputs = CaptureSignature(method.OutputArguments);
            bool occurrenceAction = conditionAction is "Acknowledge" or "Confirm" or "AddComment";
            int eventIdIndex = -1;
            int commentIndex = -1;
            if (occurrenceAction)
            {
                for (int i = 0; i < inputs.Count; i++)
                {
                    if (inputs[i].Name == Ua.BrowseNames.EventId &&
                        inputs[i].DataType == Ua.DataTypeIds.ByteString && inputs[i].ValueRank == ValueRanks.Scalar)
                    {
                        eventIdIndex = i;
                    }
                    else if (inputs[i].Name == Ua.BrowseNames.Comment &&
                        inputs[i].DataType == Ua.DataTypeIds.LocalizedText && inputs[i].ValueRank == ValueRanks.Scalar)
                    {
                        commentIndex = i;
                    }
                    else
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadConfigurationError, "A Condition Method has an invalid input signature.");
                    }
                }
                if (eventIdIndex < 0 || inputs.Count != (commentIndex < 0 ? 1 : 2))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadConfigurationError, "A Condition Method requires one ByteString EventId.");
                }
            }
            else if (conditionAction is not null && inputs.Count != 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadConfigurationError, "Enable and Disable take no input arguments.");
            }
            return async (context, _, _, arguments, results, cancellationToken) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return new ServiceResult(StatusCodes.BadRequestCancelledByClient);
                }
                if (m_generationToken.IsCancellationRequested)
                {
                    return new ServiceResult(StatusCodes.BadShutdown);
                }
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, m_generationToken);
                CancellationToken token = lifetime.Token;
                ServiceResult inputStatus = ValidateArguments(context, arguments, inputs);
                if (ServiceResult.IsBad(inputStatus))
                {
                    return inputStatus;
                }
                ArrayOf<Variant> upstreamArguments = arguments;
                if (occurrenceAction)
                {
                    if (!arguments[eventIdIndex].TryGetValue(out ByteString eventId))
                    {
                        return new ServiceResult(StatusCodes.BadTypeMismatch);
                    }
                    ServiceResult routing = condition!.ResolveEventId(eventId, out ByteString originalEventId);
                    if (ServiceResult.IsBad(routing))
                    {
                        return routing;
                    }
                    upstreamArguments =
                    [
                        new Variant(originalEventId),
                        commentIndex < 0 ? new Variant(LocalizedText.Null) : arguments[commentIndex]
                    ];
                }
                WotInvokeResult response;
                var localContext = new ServiceMessageContext(context.Telemetry, context.EncodeableFactory)
                {
                    NamespaceUris = context.NamespaceUris,
                    ServerUris = context.ServerUris
                };
                try
                {
                    IWotBindingChannel channel = await slot.GetAsync(token).ConfigureAwait(false);
                    if (channel is IWotContextualBindingChannel contextual)
                    {
                        response = await contextual.InvokeAsync(
                            new WotInvokeRequest(upstreamArguments, localContext), token).ConfigureAwait(false);
                    }
                    else
                    {
                        var independent = new ServiceMessageContext(context.Telemetry, context.EncodeableFactory);
                        foreach (Variant input in upstreamArguments)
                        {
                            if (WotBindingValueMapper.RequiresContext(input))
                            {
                                _ = WotBindingValueMapper.Translate(input, localContext, independent);
                            }
                        }
                        response = await channel.InvokeAsync(upstreamArguments.Span.ToArray(), token)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return new ServiceResult(cancellationToken.IsCancellationRequested
                        ? StatusCodes.BadRequestCancelledByClient : StatusCodes.BadShutdown);
                }
                if (StatusCode.IsBad(response.Status))
                {
                    return new ServiceResult(response.Status);
                }
                if (response.Outputs.Count != outputs.Count)
                {
                    return new ServiceResult(StatusCodes.BadDecodingError);
                }
                StatusCode status = response.Status;
                foreach (DataValue output in response.Outputs)
                {
                    if (StatusCode.IsBad(output.StatusCode))
                    {
                        return new ServiceResult(output.StatusCode);
                    }
                    if (status == StatusCodes.Good && !StatusCode.IsGood(output.StatusCode))
                    {
                        status = output.StatusCode;
                    }
                }
                IServiceMessageContext sourceContext = response.Context ??
                    new ServiceMessageContext(context.Telemetry, context.EncodeableFactory);
                ArrayOf<Variant> values = response.Outputs.Select(value => WotBindingValueMapper.Translate(
                    value.WrappedValue, sourceContext, localContext, allowNamespaceGrowth: true)).ToArrayOf();
                ServiceResult outputStatus = ValidateArguments(context, values, outputs);
                if (ServiceResult.IsBad(outputStatus))
                {
                    return outputStatus;
                }
                results.Clear();
                results.AddRange(values);
                return new ServiceResult(status);
            };
        }

        private static ArrayOf<ArgumentSignature> CaptureSignature(PropertyState<ArrayOf<Argument>>? property)
        {
            return property is null ? [] : property.Value.ConvertAll(argument =>
                new ArgumentSignature(argument.Name, argument.DataType, argument.ValueRank));
        }

        private static ServiceResult ValidateArguments(
            ISystemContext context, ArrayOf<Variant> arguments, ArrayOf<ArgumentSignature> signature)
        {
            if (arguments.Count != signature.Count)
            {
                return new ServiceResult(arguments.Count < signature.Count
                    ? StatusCodes.BadArgumentsMissing : StatusCodes.BadTooManyArguments);
            }
            for (int i = 0; i < signature.Count; i++)
            {
                if (TypeInfo.IsInstanceOfDataType(
                    arguments[i], signature[i].DataType, signature[i].ValueRank,
                    context.NamespaceUris, context.TypeTable).IsUnknown)
                {
                    return new ServiceResult(StatusCodes.BadTypeMismatch);
                }
            }
            return ServiceResult.Good;
        }

        private readonly record struct ArgumentSignature(string? Name, NodeId DataType, int ValueRank);

        private static readonly ArrayOf<string> s_conditionActions =
            ["Acknowledge", "Confirm", "AddComment", "Enable", "Disable"];
    }
}
