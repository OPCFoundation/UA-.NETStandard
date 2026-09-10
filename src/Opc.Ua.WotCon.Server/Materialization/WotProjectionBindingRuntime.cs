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
using System.Threading.Tasks;
using Opc.Ua.Server.Fluent;
using Opc.Ua.WotCon.Bindings;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// The per-generation OPC UA projection binding runtime wired onto a
    /// freshly imported NodeSet by <see cref="WotProjectionBindingRuntimeFactory"/>.
    /// It groups executable, target-mapped compiled forms by their resolved
    /// target variable, wires either a direct (whole-value) or a structured
    /// (field-by-field) handler per group, and owns every channel it lazily
    /// opens for the lifetime of the generation. Local Method and EventType
    /// identities are resolved independently of those property target mappings.
    /// Event subscriptions and Condition occurrence routes share that ownership.
    /// </summary>
    public sealed partial class WotProjectionBindingRuntime : IAsyncDisposable
    {
        internal WotProjectionBindingRuntime(
            INodeManagerBuilder builder,
            IWotBindingChannelFactory channelFactory,
            IWotTargetVariableResolver resolver,
            IWotProjectionEventPublisher eventPublisher,
            IWotProjectionConditionFactory conditionFactory,
            WotProjectionBindingRuntimeOptions options,
            WotProjectedEventRouteRegistry eventRoutes)
        {
            m_builder = builder;
            m_channelFactory = channelFactory;
            m_resolver = resolver;
            m_eventPublisher = eventPublisher;
            m_conditionFactory = conditionFactory;
            m_options = options;
            m_generationToken = m_lifetime.Token;
            m_eventRoutes = eventRoutes;
        }

        /// <summary>
        /// Groups the closure's target-mapped, executable compiled forms by
        /// resolved target variable and wires each group. Runs entirely
        /// against the address space (no transport I/O); channel opens are
        /// deferred to first use. Condition instance registration is asynchronous.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// See <see cref="IWotProjectionBindingRuntimeFactory.CreateAsync"/>.
        /// </exception>
        internal async ValueTask WireAsync(
            ArrayOf<WotBindingPlan> bindingPlans, CancellationToken cancellationToken)
        {
            var groups = new Dictionary<NodeId, VariableGroup>();
            for (int p = 0; p < bindingPlans.Count; p++)
            {
                WotBindingPlan plan = bindingPlans[p];
                if (plan is null || plan.IsDeclarationContext)
                {
                    continue;
                }
                var selected = new HashSet<(WotAffordanceKind Kind, string Name, string Pointer,
                    WoTBindingCapabilityEnum Operation)>();
                foreach (WotCompiledForm form in plan.CompiledForms)
                {
                    if (form is null || !form.IsExecutable)
                    {
                        continue;
                    }
                    WotTargetMappingDescriptor target = form.TargetMapping;
                    if (target.IsEmpty)
                    {
                        WotProjectedAffordance? local = plan.ProjectedAffordances.Find(declaration =>
                            declaration.Kind == WotAffordanceKind.Property &&
                            form.AffordanceKind == declaration.Kind &&
                            form.JsonPointer.StartsWith(declaration.JsonPointer + "/forms/", StringComparison.Ordinal));
                        if (local is null)
                        {
                            continue;
                        }
                        NodeId localId = ResolveLocalNodeId(local.NodeId, plan.ResourceXid, local.JsonPointer);
                        target = new WotTargetMappingDescriptor(targetNodeId: localId.ToString());
                    }

                    if (form.Operation is not (WoTBindingCapabilityEnum.ReadProperty
                        or WoTBindingCapabilityEnum.WriteProperty or WoTBindingCapabilityEnum.ObserveProperty))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError,
                            "Affordance '{0}' carries an OPC UA target mapping on its '{1}' form, but " +
                            "only readproperty, writeproperty and observeproperty support target binding.",
                            form.AffordanceName,
                            form.OpToken);
                    }

                    int formSegment = form.JsonPointer.LastIndexOf("/forms/", StringComparison.Ordinal);
                    string pointer = formSegment < 0 ? form.JsonPointer : form.JsonPointer.Substring(0, formSegment);
                    if (!selected.Add((form.AffordanceKind, form.AffordanceName, pointer, form.Operation)))
                    {
                        continue;
                    }
                    BaseVariableState variable = m_resolver.Resolve(m_builder, target);
                    if (!groups.TryGetValue(variable.NodeId, out VariableGroup? group))
                    {
                        group = new VariableGroup(variable);
                        groups.Add(variable.NodeId, group);
                    }
                    m_formOwners.TryAdd(form, plan);
                    group.Entries.Add(form);
                }
            }

            foreach (VariableGroup group in groups.Values)
            {
                WireGroup(group);
            }

            await WireProjectedEventsAsync(bindingPlans, cancellationToken).ConfigureAwait(false);
            WireProjectedMethods(bindingPlans);
            RegisterEventPublishers();
        }

        /// <summary>
        /// Disposes every channel this generation successfully opened.
        /// Faulted opens have no resource and are silently skipped by
        /// <see cref="WotBindingChannelSlot.DisposeAsync"/>; actual disposal
        /// failures are aggregated.
        /// </summary>
        /// <exception cref="AggregateException"></exception>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }
            List<Exception>? errors = null;
            try
            {
                m_lifetime.Cancel();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                errors = [ex];
            }
            foreach (WotObservedPropertySource source in m_observedProperties)
            {
                try
                {
                    await source.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    (errors ??= []).Add(ex);
                }
            }
            foreach (WotProjectedEventSource source in m_eventSources)
            {
                try
                {
                    await source.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    (errors ??= []).Add(ex);
                }
            }
            foreach (WotBindingChannelSlot slot in m_slots.Values)
            {
                try
                {
                    await slot.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    (errors ??= []).Add(ex);
                }
            }
            foreach (WotProjectedEventBinding binding in m_events.Values)
            {
                binding.Clear();
                m_eventRoutes.Remove(binding);
            }
            m_lifetime.Dispose();
            if (errors is { Count: > 0 })
            {
                throw new AggregateException(
                    "One or more WoT projection binding channels failed to dispose.", errors);
            }
        }

        private void WireGroup(VariableGroup group)
        {
            bool hasDirect = false;
            bool hasField = false;
            foreach (WotCompiledForm entry in group.Entries)
            {
                if (string.IsNullOrEmpty(entry.TargetMapping.FieldPath))
                {
                    hasDirect = true;
                }
                else
                {
                    hasField = true;
                }
            }
            if (hasDirect && hasField)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Target '{0}' is mapped both directly ('uav:mapToNodeId' / 'uav:mapToType' alone) and " +
                    "by field path ('uav:mapByFieldPath'); a target cannot be both a whole-value and a " +
                    "structured-field target.",
                    group.Variable.NodeId);
            }

            INodeBuilder nodeBuilder = m_builder.Node(group.Variable.NodeId);
            if (hasField)
            {
                WireStructuredGroup(group, nodeBuilder);
            }
            else
            {
                WireDirectGroup(group, nodeBuilder);
            }
        }

        private void WireDirectGroup(VariableGroup group, INodeBuilder nodeBuilder)
        {
            WotCompiledForm? read = null;
            WotCompiledForm? write = null;
            WotCompiledForm? observe = null;
            foreach (WotCompiledForm entry in group.Entries)
            {
                switch (entry.Operation)
                {
                    case WoTBindingCapabilityEnum.ObserveProperty:
                        if (observe is not null)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadConfigurationError,
                                "Target '{0}' has more than one observeproperty target mapping.",
                                group.Variable.NodeId);
                        }
                        observe = entry;
                        continue;
                    case WoTBindingCapabilityEnum.ReadProperty:
                        if (read is not null)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadConfigurationError,
                                "Target '{0}' has more than one readproperty target mapping.",
                                group.Variable.NodeId);
                        }
                        read = entry;
                        break;
                    case WoTBindingCapabilityEnum.WriteProperty:
                        if (write is not null)
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadConfigurationError,
                                "Target '{0}' has more than one writeproperty target mapping.",
                                group.Variable.NodeId);
                        }
                        write = entry;
                        break;
                }
            }

            if (read is not null)
            {
                nodeBuilder.OnRead(BuildDirectReadHandler(GetOrCreateSlot(read)));
            }
            if (write is not null)
            {
                nodeBuilder.OnWrite(BuildDirectWriteHandler(GetOrCreateSlot(write)));
            }
            if (observe is not null && (read is null || !SharesAuthoredForm(read, observe)))
            {
                m_observedProperties.Add(new WotObservedPropertySource(
                    nodeBuilder, GetOrCreateSlot(observe), read is not null,
                    m_options.MaxQueuedPropertyValues, m_generationToken));
            }
        }

        /// <summary>
        /// Wires a structured (field-mapped) group. Target-variable resolution
        /// and read/write duplicate-path detection run now, synchronously
        /// against the address space; the structure encodeable lookup, root
        /// instance validation and per-field
        /// <see cref="WotStructuredFieldNavigator.BuildPlan"/> calls are
        /// deferred to <see cref="WotStructuredGroupState.EnsureResolved"/> on
        /// the first structured read or write, because the target's structure
        /// type is not guaranteed to be registered in the shared
        /// <see cref="IEncodeableFactory"/> yet at wiring time (see the class
        /// remarks on <see cref="WotStructuredGroupState"/>).
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void WireStructuredGroup(VariableGroup group, INodeBuilder nodeBuilder)
        {
            NodeId targetNodeId = group.Variable.NodeId;

            var readByPath = new Dictionary<string, WotCompiledForm>(StringComparer.Ordinal);
            var writeByPath = new Dictionary<string, WotCompiledForm>(StringComparer.Ordinal);
            var observeByPath = new Dictionary<string, WotCompiledForm>(StringComparer.Ordinal);
            foreach (WotCompiledForm entry in group.Entries)
            {
                string fieldPath = entry.TargetMapping.FieldPath ?? string.Empty;
                switch (entry.Operation)
                {
                    case WoTBindingCapabilityEnum.ObserveProperty:
                        if (observeByPath.ContainsKey(fieldPath))
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadConfigurationError,
                                "Target '{0}' field '{1}' has more than one observeproperty mapping.",
                                targetNodeId,
                                fieldPath);
                        }
                        observeByPath.Add(fieldPath, entry);
                        continue;
                    case WoTBindingCapabilityEnum.ReadProperty:
                        if (readByPath.ContainsKey(fieldPath))
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadConfigurationError,
                                "Target '{0}' field '{1}' has more than one readproperty mapping.",
                                targetNodeId,
                                fieldPath);
                        }
                        readByPath.Add(fieldPath, entry);
                        break;
                    case WoTBindingCapabilityEnum.WriteProperty:
                        if (writeByPath.ContainsKey(fieldPath))
                        {
                            throw ServiceResultException.Create(
                                StatusCodes.BadConfigurationError,
                                "Target '{0}' field '{1}' has more than one writeproperty mapping.",
                                targetNodeId,
                                fieldPath);
                        }
                        writeByPath.Add(fieldPath, entry);
                        break;
                }
            }

            List<(string Path, WotBindingChannelSlot Slot)> readSlots = [.. readByPath
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => (kv.Key, GetOrCreateSlot(kv.Value)))];
            List<(string Path, WotBindingChannelSlot Slot)> writeSlots = [.. writeByPath
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => (kv.Key, GetOrCreateSlot(kv.Value)))];

            var state = new WotStructuredGroupState(
                m_builder.Context.EncodeableFactory,
                m_builder.Context.NamespaceUris,
                group.Variable.DataType,
                targetNodeId,
                readSlots,
                writeSlots);

            if (readSlots.Count > 0)
            {
                nodeBuilder.OnRead(BuildStructuredReadHandler(state));
            }
            if (writeSlots.Count > 0)
            {
                nodeBuilder.OnWrite(BuildStructuredWriteHandler(state));
            }
            if (observeByPath.Any(pair => !readByPath.TryGetValue(pair.Key, out WotCompiledForm? read) ||
                !SharesAuthoredForm(read, pair.Value)))
            {
                List<(string Path, WotBindingChannelSlot Slot)> observeSlots = [.. observeByPath
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => (pair.Key, GetOrCreateSlot(pair.Value)))];
                var observed = new WotStructuredGroupState(
                    m_builder.Context.EncodeableFactory,
                    m_builder.Context.NamespaceUris,
                    group.Variable.DataType,
                    targetNodeId,
                    observeSlots,
                    []);
                m_observedProperties.Add(new WotObservedPropertySource(
                    nodeBuilder,
                    (notification, token) => WotStructuredPropertyObservation.StartAsync(
                        observed, m_builder.Context, notification, token),
                    readSlots.Count > 0,
                    m_options.MaxQueuedPropertyValues,
                    m_generationToken));
            }
        }

        private bool SharesAuthoredForm(WotCompiledForm first, WotCompiledForm second)
        {
            return first.JsonPointer == second.JsonPointer &&
                ReferenceEquals(m_formOwners[first], m_formOwners[second]);
        }

        private WotBindingChannelSlot GetOrCreateSlot(WotCompiledForm form)
        {
            if (!m_slots.TryGetValue(form, out WotBindingChannelSlot? slot))
            {
                slot = new WotBindingChannelSlot(
                    form, m_channelFactory, m_builder.Context.AsMessageContext(), m_generationToken);
                m_slots.Add(form, slot);
            }
            return slot;
        }

        private static NodeValueEventHandlerAsync BuildDirectReadHandler(WotBindingChannelSlot slot)
        {
            return async (context, node, indexRange, dataEncoding, cancellationToken) =>
            {
                IWotBindingChannel channel = await slot.GetAsync(cancellationToken).ConfigureAwait(false);
                WotReadResult result;
                bool nativeRead = channel is IWotPropertyBindingChannel;
                if (channel is IWotPropertyBindingChannel propertyChannel)
                {
                    var request = new WotReadRequest(context.AsMessageContext(), indexRange, dataEncoding);
                    result = await propertyChannel.ReadAsync(request, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    result = await channel.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                bool isWholeValue = !nativeRead || (indexRange.IsNull && dataEncoding.IsNull);
                if (StatusCode.IsBad(result.Status))
                {
                    DateTimeUtc failedTimestamp = result.Value.SourceTimestamp != DateTimeUtc.MinValue
                        ? result.Value.SourceTimestamp
                        : DateTimeUtc.Now;
                    if (isWholeValue && node is BaseVariableState failedVariable)
                    {
                        failedVariable.Value = Variant.Null;
                        failedVariable.StatusCode = result.Status;
                        failedVariable.Timestamp = failedTimestamp;
                    }
                    return new AttributeReadResult(
                        new ServiceResult(result.Status),
                        Variant.Null,
                        result.Status,
                        failedTimestamp);
                }
                DataValue value = TranslateReadValue(result, context);
                StatusCode status = SelectStatus(value.StatusCode, result.Status);
                DateTimeUtc timestamp = value.SourceTimestamp != DateTimeUtc.MinValue
                    ? value.SourceTimestamp
                    : DateTimeUtc.Now;
                if (isWholeValue && node is BaseVariableState variable)
                {
                    variable.Value = value.WrappedValue;
                    variable.StatusCode = status;
                    variable.Timestamp = timestamp;
                }
                return nativeRead
                    ? new AttributeReadResult(ServiceResult.Good, value.WrappedValue, status, timestamp)
                    : ApplyReadConstraints(context, indexRange, dataEncoding, value.WrappedValue, status, timestamp);
            };
        }

        private static NodeValueWriteEventHandlerAsync BuildDirectWriteHandler(WotBindingChannelSlot slot)
        {
            return async (context, node, indexRange, value, cancellationToken) =>
            {
                IWotBindingChannel channel = await slot.GetAsync(cancellationToken).ConfigureAwait(false);
                WotWriteResult result = await WritePropertyAsync(
                    channel, value, context.AsMessageContext(), indexRange, cancellationToken).ConfigureAwait(false);
                return new AttributeWriteResult(new ServiceResult(result.Status));
            };
        }

        private static NodeValueEventHandlerAsync BuildStructuredReadHandler(WotStructuredGroupState state)
        {
            return async (context, node, indexRange, dataEncoding, cancellationToken) =>
            {
                WotStructuredGroupResolution resolution = state.EnsureResolved();
                if (!resolution.Success)
                {
                    return new AttributeReadResult(
                        resolution.Error, Variant.Null, resolution.Error.StatusCode, DateTimeUtc.Now);
                }

                IEncodeable rootEncodeable = resolution.RootType!.CreateInstance();
                if (rootEncodeable is not IStructure root)
                {
                    return new AttributeReadResult(
                        new ServiceResult(StatusCodes.BadConfigurationError),
                        Variant.Null,
                        StatusCodes.BadConfigurationError,
                        DateTimeUtc.Now);
                }

                List<(WotFieldPathPlan Plan, WotBindingChannelSlot Slot)> fields = resolution.ReadFields;
                IServiceMessageContext messageContext = context.AsMessageContext();
                var tasks = new Task<(WotFieldPathPlan Plan, WotReadResult Result)>[fields.Count];
                for (int i = 0; i < fields.Count; i++)
                {
                    tasks[i] = ReadFieldAsync(fields[i].Plan, fields[i].Slot, messageContext, cancellationToken);
                }
                (WotFieldPathPlan Plan, WotReadResult Result)[] results = await Task.WhenAll(tasks)
                    .ConfigureAwait(false);

                foreach ((WotFieldPathPlan _, WotReadResult result) in results)
                {
                    if (StatusCode.IsBad(result.Status))
                    {
                        DateTimeUtc failedTimestamp = result.Value.SourceTimestamp != DateTimeUtc.MinValue
                            ? result.Value.SourceTimestamp
                            : DateTimeUtc.Now;
                        return new AttributeReadResult(
                            new ServiceResult(result.Status), Variant.Null, result.Status, failedTimestamp);
                    }
                }

                foreach ((WotFieldPathPlan plan, WotReadResult result) in results)
                {
                    IStructure parent = WotStructuredFieldNavigator.CreateOrGetChild(root, plan.IntermediateSegments);
                    parent[plan.LeafFieldName] = TranslateReadValue(result, context).WrappedValue;
                }

                (StatusCode status, DateTimeUtc timestamp) = AggregateFieldMetadata(results);
                return ApplyReadConstraints(
                    context,
                    indexRange,
                    dataEncoding,
                    new Variant(new ExtensionObject(rootEncodeable)),
                    status,
                    timestamp);
            };
        }

        private static NodeValueWriteEventHandlerAsync BuildStructuredWriteHandler(WotStructuredGroupState state)
        {
            return async (context, node, indexRange, value, cancellationToken) =>
            {
                if (!indexRange.IsNull)
                {
                    return new AttributeWriteResult(new ServiceResult(StatusCodes.BadWriteNotSupported));
                }
                WotStructuredGroupResolution resolution = state.EnsureResolved();
                if (!resolution.Success)
                {
                    return new AttributeWriteResult(resolution.Error);
                }

                IServiceMessageContext messageContext = context.AsMessageContext();
                if (!value.TryGetValue(out ExtensionObject extensionObject) ||
                    !extensionObject.TryGetValue(out IEncodeable? rootEncodeable, messageContext) ||
                    rootEncodeable is not IStructure root)
                {
                    return new AttributeWriteResult(new ServiceResult(StatusCodes.BadTypeMismatch));
                }

                List<(WotFieldPathPlan Plan, WotBindingChannelSlot Slot)> fields = resolution.WriteFields;
                var tasks = new Task<WotWriteResult>[fields.Count];
                for (int i = 0; i < fields.Count; i++)
                {
                    tasks[i] = WriteFieldAsync(
                        root,
                        fields[i].Plan,
                        fields[i].Slot,
                        state.TargetNodeId,
                        messageContext,
                        cancellationToken);
                }
                WotWriteResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);

                StatusCode status = StatusCodes.Good;
                foreach (WotWriteResult result in results)
                {
                    status = SelectStatus(status, result.Status);
                }
                return new AttributeWriteResult(new ServiceResult(status));
            };
        }

        /// <summary>
        /// Aggregates per-field metadata into the first non-default status
        /// at the highest severity and the oldest
        /// non-<see cref="DateTimeUtc.MinValue"/> source
        /// timestamp across the fields (or now, if none carried one).
        /// </summary>
        internal static (StatusCode Status, DateTimeUtc Timestamp) AggregateFieldMetadata(
            (WotFieldPathPlan Plan, WotReadResult Result)[] results)
        {
            StatusCode status = StatusCodes.Good;
            DateTimeUtc oldest = DateTimeUtc.MinValue;
            foreach ((WotFieldPathPlan _, WotReadResult result) in results)
            {
                StatusCode fieldStatus = SelectStatus(result.Value.StatusCode, result.Status);
                status = SelectStatus(status, fieldStatus);

                DateTimeUtc fieldTimestamp = result.Value.SourceTimestamp;
                if (fieldTimestamp != DateTimeUtc.MinValue &&
                    (oldest == DateTimeUtc.MinValue || fieldTimestamp < oldest))
                {
                    oldest = fieldTimestamp;
                }
            }
            return (status, oldest == DateTimeUtc.MinValue ? DateTimeUtc.Now : oldest);
        }

        private static StatusCode SelectStatus(StatusCode current, StatusCode candidate)
        {
            if (current.Code == StatusCodes.Good.Code ||
                (StatusCode.IsBad(candidate) && StatusCode.IsNotBad(current)) ||
                (StatusCode.IsUncertain(candidate) && StatusCode.IsGood(current)))
            {
                return candidate;
            }
            return current;
        }

        private static DataValue TranslateReadValue(WotReadResult result, ISystemContext context)
        {
            DataValue value = result.Value;
            if (StatusCode.IsBad(value.StatusCode) || !WotBindingValueMapper.RequiresContext(value.WrappedValue))
            {
                return value;
            }
            IServiceMessageContext source = result.Context ??
                new ServiceMessageContext(context.Telemetry, context.EncodeableFactory);
            Variant mapped = WotBindingValueMapper.Translate(
                value.WrappedValue, source, context.AsMessageContext(), allowNamespaceGrowth: true);
            return value.WithWrappedValue(mapped);
        }

        private static AttributeReadResult ApplyReadConstraints(
            ISystemContext context,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            Variant value,
            StatusCode status,
            DateTimeUtc timestamp)
        {
            if (StatusCode.IsNotBad(status))
            {
                ServiceResult result = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                    context, indexRange, dataEncoding, ref value);
                if (ServiceResult.IsBad(result))
                {
                    return new AttributeReadResult(result, Variant.Null, result.StatusCode, timestamp);
                }
            }
            return new AttributeReadResult(ServiceResult.Good, value, status, timestamp);
        }

        private static async Task<(WotFieldPathPlan Plan, WotReadResult Result)> ReadFieldAsync(
            WotFieldPathPlan plan,
            WotBindingChannelSlot slot,
            IServiceMessageContext messageContext,
            CancellationToken cancellationToken)
        {
            IWotBindingChannel channel = await slot.GetAsync(cancellationToken).ConfigureAwait(false);
            WotReadResult result = channel is IWotPropertyBindingChannel propertyChannel
                ? await propertyChannel.ReadAsync(new WotReadRequest(messageContext), cancellationToken)
                    .ConfigureAwait(false)
                : await channel.ReadAsync(cancellationToken).ConfigureAwait(false);
            return (plan, result);
        }

        private static async Task<WotWriteResult> WriteFieldAsync(
            IStructure root,
            WotFieldPathPlan plan,
            WotBindingChannelSlot slot,
            NodeId targetNodeId,
            IServiceMessageContext messageContext,
            CancellationToken cancellationToken)
        {
            IStructure parent;
            try
            {
                parent = WotStructuredFieldNavigator.GetExistingChild(
                    root,
                    plan.IntermediateSegments,
                    targetNodeId,
                    messageContext);
            }
            catch (ServiceResultException ex)
            {
                return new WotWriteResult(ex.StatusCode, ex.Message);
            }
            Variant fieldValue = parent[plan.LeafFieldName];
            IWotBindingChannel channel = await slot.GetAsync(cancellationToken).ConfigureAwait(false);
            return await WritePropertyAsync(channel, fieldValue, messageContext, default, cancellationToken)
                .ConfigureAwait(false);
        }

        private static async ValueTask<WotWriteResult> WritePropertyAsync(
            IWotBindingChannel channel,
            Variant value,
            IServiceMessageContext messageContext,
            NumericRange indexRange,
            CancellationToken cancellationToken)
        {
            if (channel is IWotPropertyBindingChannel propertyChannel)
            {
                var request = new WotWriteRequest(new DataValue(value), messageContext, indexRange);
                return await propertyChannel.WriteAsync(request, cancellationToken).ConfigureAwait(false);
            }
            if (!indexRange.IsNull)
            {
                return new WotWriteResult(
                    StatusCodes.BadWriteNotSupported, "The property channel does not support native indexed writes.");
            }
            Variant writtenValue = value;
            if (WotBindingValueMapper.RequiresContext(value))
            {
                var independent = new ServiceMessageContext(messageContext.Telemetry, messageContext.Factory);
                writtenValue = WotBindingValueMapper.Translate(value, messageContext, independent);
            }
            return await channel.WriteAsync(new DataValue(writtenValue), cancellationToken).ConfigureAwait(false);
        }

        private readonly INodeManagerBuilder m_builder;
        private readonly IWotBindingChannelFactory m_channelFactory;
        private readonly IWotTargetVariableResolver m_resolver;
        private readonly Dictionary<WotCompiledForm, WotBindingPlan> m_formOwners = [];
        private readonly Dictionary<WotCompiledForm, WotBindingChannelSlot> m_slots = [];
        private readonly List<WotObservedPropertySource> m_observedProperties = [];
        private readonly IWotProjectionEventPublisher m_eventPublisher;
        private readonly IWotProjectionConditionFactory m_conditionFactory;
        private readonly WotProjectionBindingRuntimeOptions m_options;
        private readonly WotProjectedEventRouteRegistry m_eventRoutes;
        private readonly CancellationTokenSource m_lifetime = new();
        private readonly CancellationToken m_generationToken;
        private int m_disposed;

        private sealed class VariableGroup
        {
            public VariableGroup(BaseVariableState variable)
            {
                Variable = variable;
            }

            public BaseVariableState Variable { get; }

            public List<WotCompiledForm> Entries { get; } = [];
        }
    }
}
