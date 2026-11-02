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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.PADIM.Server
{
    /// <summary>
    /// Publishes an acquired PA-DIM value without losing its wire type, quality or source timestamp.
    /// </summary>
    public sealed class PadimValueBinding
    {
        /// <summary>
        /// Creates a binding owned by its device.
        /// </summary>
        internal PadimValueBinding(
            PadimDevice device,
            BaseVariableState variable,
            string key,
            IPadimValueProvider provider)
        {
            m_device = device;
            Variable = variable;
            Key = key;
            m_provider = provider;
            Variable.StatusCode = StatusCodes.BadWaitingForInitialData;
            Variable.AccessLevel = AccessLevels.CurrentRead;
            Variable.UserAccessLevel = AccessLevels.CurrentRead;
        }

        /// <summary>
        /// Gets the generated variable carrying the value and its declared metadata.
        /// </summary>
        public BaseVariableState Variable { get; }

        /// <summary>
        /// Gets the stable provider key, which must not contain namespace indexes.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Gets whether provider-authorized simulation has been configured.
        /// </summary>
        internal bool SupportsSimulation => m_actualVariable != null;

        /// <summary>
        /// Gets or sets the device-owned follow-up after acquisition, such as health-condition publication.
        /// </summary>
        internal Func<DataValue, CancellationToken, ValueTask>? OnPublishedAsync { get; set; }

        /// <summary>
        /// Gets or sets additional domain validation performed before changing the published sample.
        /// </summary>
        internal Action<DataValue>? ValidateSample { get; set; }

        /// <summary>
        /// Configures the declared analog unit and engineering range without converting provider values.
        /// </summary>
        public PadimValueBinding WithEngineeringUnits(EUInformation units, Range range)
        {
            if (Variable is not AnalogSignalVariableState analog)
            {
                throw new InvalidOperationException("Engineering units require an analog signal variable.");
            }
            if (units == null)
            {
                throw new ArgumentNullException(nameof(units));
            }
            if (range == null)
            {
                throw new ArgumentNullException(nameof(range));
            }
            if (
                double.IsNaN(range.Low) || double.IsNaN(range.High) ||
                double.IsInfinity(range.Low) || double.IsInfinity(range.High) || range.Low > range.High)
            {
                throw new ArgumentException("A unit and finite ordered engineering range are required.", nameof(range));
            }
            analog.EngineeringUnits!.Value = units;
            analog.AddEURange(m_device.Context, property => property.Value = range);
            return this;
        }

        /// <summary>
        /// Enables provider-backed damping in seconds, initially using the published 1.0-second default.
        /// </summary>
        /// <remarks>
        /// The application owns the physical damping algorithm and stores its configuration.
        /// The acquisition key followed by /Damping identifies the provider parameter.
        /// </remarks>
        public PadimValueBinding WithDamping(
            IPadimValueProvider acquisition,
            IPadimWriteProvider writer,
            IPadimAuthorizer authorizer)
        {
            if (Variable is not AnalogSignalVariableState analog)
            {
                throw new InvalidOperationException("Damping requires an analog signal variable.");
            }
            if (acquisition == null)
            {
                throw new ArgumentNullException(nameof(acquisition));
            }
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }
            if (authorizer == null)
            {
                throw new ArgumentNullException(nameof(authorizer));
            }
            analog.AddDamping(m_device.Context);
            PadimValueBinding damping = m_device.BindValue(analog.Damping!, $"{Key}/Damping", acquisition);
            damping.Publish(new DataValue(
                Variant.From(1f), StatusCodes.Good, m_device.TimeProvider.GetUtcNow().UtcDateTime));
            return damping.WithWrites(writer, authorizer);
        }

        /// <summary>
        /// Enables provider-approved process simulation while retaining the unsimulated acquired sample.
        /// </summary>
        /// <remarks>
        /// The provider keys are the acquisition key followed by /SimulationValue and /SimulationState.
        /// A provider must commit each change before returning Good.
        /// </remarks>
        public PadimValueBinding WithSimulation(
            Variant initialValue,
            IPadimWriteProvider provider,
            IPadimAuthorizer authorizer)
        {
            if (Variable is not (AnalogSignalVariableState or DiscreteSignalVariableState or
                TwoStateDiscreteSignalVariableState or MultiStateDiscreteSignalVariableState))
            {
                throw new InvalidOperationException("Simulation requires a published PA-DIM process variable.");
            }
            if (initialValue.IsNull || !IsCompatible(initialValue))
            {
                throw new ArgumentException(
                    "The simulation value must match the declared signal type.", nameof(initialValue));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (authorizer == null)
            {
                throw new ArgumentNullException(nameof(authorizer));
            }
            if (m_actualVariable != null)
            {
                throw new InvalidOperationException("Simulation is already configured.");
            }
            if (Variable is MultiStateValueDiscreteState enumeration)
            {
                ServiceResult validation = enumeration.ValidateEnumValue(m_device.Context, initialValue);
                if (ServiceResult.IsBad(validation))
                {
                    throw new ServiceResultException(validation);
                }
            }
            m_simulationValue = initialValue;
            MaterializeSimulation();
            BaseVariableState actual = SimulationMember("ActualValue");
            BaseVariableState simulated = SimulationMember("SimulationValue");
            BaseVariableState enabled = SimulationMember("SimulationState");
            m_actualVariable = actual;
            actual.StatusCode = StatusCodes.BadWaitingForInitialData;
            simulated.WrappedValue = initialValue;
            enabled.WrappedValue = Variant.From(false);
            Configure(simulated, "SimulationValue", isState: false);
            Configure(enabled, "SimulationState", isState: true);
            return this;

            void Configure(BaseVariableState variable, string suffix, bool isState)
            {
                variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
                variable.OnSimpleWriteValueAsync = async (caller, node, value, ct) =>
                {
                    StatusCode status = StatusCodes.BadUserAccessDenied;
                    await m_device.RunAsync(async token =>
                    {
                        string key = $"{Key}/{suffix}";
                        if (!await authorizer.AuthorizeAsync(caller, node.NodeId, key, token).ConfigureAwait(false))
                        {
                            return;
                        }
                        if ((isState && !value.TryGetValue(out bool _)) ||
                            (!isState && (value.IsNull || !IsCompatible(value))))
                        {
                            status = StatusCodes.BadTypeMismatch;
                            return;
                        }
                        if (!isState && Variable is MultiStateValueDiscreteState simulationEnum)
                        {
                            ServiceResult validation = simulationEnum.ValidateEnumValue(m_device.Context, value);
                            if (ServiceResult.IsBad(validation))
                            {
                                status = validation.StatusCode;
                                return;
                            }
                        }
                        status = await provider.WriteAsync(key, value, token).ConfigureAwait(false);
                        if (!StatusCode.IsGood(status))
                        {
                            return;
                        }
                        if (isState)
                        {
                            value.TryGetValue(out m_simulating);
                            enabled.WrappedValue = Variant.From(m_simulating);
                        }
                        else
                        {
                            m_simulationValue = value;
                            simulated.WrappedValue = value;
                        }
                        variable.Timestamp = m_device.TimeProvider.GetUtcNow().UtcDateTime;
                        PublishReportedValue();
                        Variable.ClearChangeMasks(m_device.Context, includeChildren: true);
                    }, ct).ConfigureAwait(false);
                    return new AttributeWriteResult(new ServiceResult(status)) { SuppressCacheUpdate = true };
                };
            }
        }

        /// <summary>
        /// Enables remote writes only with an explicit provider and session-aware authorization policy.
        /// </summary>
        public PadimValueBinding WithWrites(IPadimWriteProvider provider, IPadimAuthorizer authorizer)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (authorizer == null)
            {
                throw new ArgumentNullException(nameof(authorizer));
            }
            m_writer = provider;
            m_authorizer = authorizer;
            Variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            Variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            Variable.OnSimpleWriteValueAsync = WriteAsync;
            return this;
        }

        /// <summary>
        /// Acquires and publishes a sample, notifying subscriptions of changes.
        /// </summary>
        public ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            return m_device.RunAsync(async ct =>
            {
                DataValue sample = await m_provider.ReadAsync(Key, ct).ConfigureAwait(false);
                Publish(sample);
                if (OnPublishedAsync != null)
                {
                    await OnPublishedAsync(sample, ct).ConfigureAwait(false);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Validates and publishes a provider sample before altering the current value.
        /// </summary>
        internal void Publish(in DataValue sample)
        {
            if (sample.IsNull ||
                (sample.WrappedValue.IsNull && !StatusCode.IsBad(sample.StatusCode)) ||
                (!sample.WrappedValue.IsNull && !IsCompatible(sample.WrappedValue)))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Provider key '{0}' returned a value incompatible with '{1}'.",
                    Key,
                    Variable.BrowseName);
            }
            ValidateSample?.Invoke(sample);
            if (!sample.WrappedValue.IsNull && Variable is MultiStateValueDiscreteState enumeration)
            {
                ServiceResult result = enumeration.ValidateEnumValue(m_device.Context, sample.WrappedValue);
                if (ServiceResult.IsBad(result))
                {
                    throw new ServiceResultException(result);
                }
            }
            m_actual = sample;
            if (m_actualVariable != null)
            {
                m_actualVariable.WrappedValue = sample.WrappedValue;
                m_actualVariable.StatusCode = sample.StatusCode;
                m_actualVariable.Timestamp = sample.SourceTimestamp;
            }
            PublishReportedValue();
            Variable.ClearChangeMasks(m_device.Context, includeChildren: true);
        }

        /// <summary>
        /// Reverses simulation without losing the latest acquired measurement.
        /// </summary>
        private void PublishReportedValue()
        {
            Variant value = m_simulating ? m_simulationValue : m_actual.WrappedValue;
            StatusCode status = m_simulating ? StatusCodes.Good : m_actual.StatusCode;
            DateTimeUtc timestamp = m_simulating
                ? m_device.TimeProvider.GetUtcNow().UtcDateTime
                : m_actual.SourceTimestamp;
            if (Variable is MultiStateValueDiscreteState enumeration)
            {
                ServiceResult result = enumeration.UpdateEnumValue(
                    m_device.Context, new DataValue(value, status, timestamp));
                if (ServiceResult.IsBad(result))
                {
                    throw new ServiceResultException(result);
                }
            }
            else
            {
                Variable.WrappedValue = value;
                Variable.StatusCode = status;
                Variable.Timestamp = timestamp;
            }
            Variable.ClearChangeMasks(m_device.Context, includeChildren: false);
        }

        /// <summary>
        /// Materializes the generated optional members for the selected analog or discrete family.
        /// </summary>
        private void MaterializeSimulation()
        {
            switch (Variable)
            {
                case AnalogSignalVariableState analog:
                    analog.AddActualValue(m_device.Context);
                    analog.AddSimulationValue(m_device.Context);
                    analog.AddSimulationState(m_device.Context);
                    break;
                case DiscreteSignalVariableState discrete:
                    discrete.AddActualValue(m_device.Context);
                    discrete.AddSimulationValue(m_device.Context);
                    discrete.AddSimulationState(m_device.Context);
                    break;
                case TwoStateDiscreteSignalVariableState twoState:
                    twoState.AddActualValue(m_device.Context);
                    twoState.AddSimulationValue(m_device.Context);
                    twoState.AddSimulationState(m_device.Context);
                    break;
                case MultiStateDiscreteSignalVariableState multiState:
                    multiState.AddActualValue(m_device.Context);
                    multiState.AddSimulationValue(m_device.Context);
                    multiState.AddSimulationState(m_device.Context);
                    break;
                default:
                    throw new InvalidOperationException("The selected process variable has no simulation members.");
            }
        }

        /// <summary>
        /// Resolves a member materialized by the generated family accessor.
        /// </summary>
        private BaseVariableState SimulationMember(string name)
        {
            var browseName = new QualifiedName(name,
                (ushort)m_device.Context.NamespaceUris.GetIndex(Namespaces.PADIM));
            return Variable.FindChild(m_device.Context, browseName) as BaseVariableState ??
                throw new InvalidOperationException($"The generated simulation member '{name}' is absent.");
        }

        /// <summary>
        /// Checks the generated DataType and ValueRank instead of coercing samples to Double.
        /// </summary>
        private bool IsCompatible(in Variant value)
        {
            return !TypeInfo.IsInstanceOfDataType(
                value,
                Variable.DataType,
                Variable.ValueRank,
                m_device.Context.NamespaceUris,
                m_device.Context.TypeTable).IsUnknown;
        }

        /// <summary>
        /// Commits authorized writes before changing the address-space value.
        /// </summary>
        private async ValueTask<AttributeWriteResult> WriteAsync(
            ISystemContext caller,
            NodeState node,
            Variant value,
            CancellationToken cancellationToken)
        {
            StatusCode status = StatusCodes.BadNotWritable;
            await m_device.RunAsync(async ct =>
            {
                if (!await m_authorizer!.AuthorizeAsync(caller, node.NodeId, Key, ct).ConfigureAwait(false))
                {
                    status = StatusCodes.BadUserAccessDenied;
                    return;
                }
                if (value.IsNull || !IsCompatible(value))
                {
                    status = StatusCodes.BadTypeMismatch;
                    return;
                }
                if (Variable is MultiStateValueDiscreteState enumeration)
                {
                    ServiceResult validation = enumeration.ValidateEnumValue(m_device.Context, value);
                    if (ServiceResult.IsBad(validation))
                    {
                        status = validation.StatusCode;
                        return;
                    }
                }
                ValidateSample?.Invoke(new DataValue(value));
                status = await m_writer!.WriteAsync(Key, value, ct).ConfigureAwait(false);
                if (StatusCode.IsGood(status))
                {
                    var sample = new DataValue(value, status, m_device.TimeProvider.GetUtcNow().UtcDateTime);
                    Publish(sample);
                }
            }, cancellationToken).ConfigureAwait(false);
            return new AttributeWriteResult(new ServiceResult(status)) { SuppressCacheUpdate = true };
        }

        /// <summary>The owning device and its serialized provider pipeline.</summary>
        private readonly PadimDevice m_device;

        /// <summary>The application-owned acquisition provider.</summary>
        private readonly IPadimValueProvider m_provider;

        /// <summary>The optional application-owned write provider.</summary>
        private IPadimWriteProvider? m_writer;

        /// <summary>The required write policy when writes are enabled.</summary>
        private IPadimAuthorizer? m_authorizer;

        /// <summary>The original acquisition member when optional simulation has been configured.</summary>
        private BaseVariableState? m_actualVariable;

        /// <summary>The latest original acquired sample.</summary>
        private DataValue m_actual = new(Variant.Null, StatusCodes.BadWaitingForInitialData);

        /// <summary>The configured simulated value, with its original numeric type.</summary>
        private Variant m_simulationValue;

        /// <summary>Whether the reported value substitutes the acquired sample.</summary>
        private bool m_simulating;
    }
}
