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

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Identifies a numeric setting on a monitored or controlled parameter.
    /// </summary>
    public enum MonitoredParameterSetting
    {
        /// <summary>
        /// The reference value; hardware control only for ControlledParameter and its subtypes.
        /// </summary>
        SetValue,

        /// <summary>
        /// The absolute minimum.
        /// </summary>
        Minimum,

        /// <summary>
        /// The absolute maximum.
        /// </summary>
        Maximum,

        /// <summary>
        /// The first lower tolerance offset.
        /// </summary>
        LowerTolerance,

        /// <summary>
        /// The first upper tolerance offset.
        /// </summary>
        UpperTolerance,

        /// <summary>
        /// The second lower tolerance offset.
        /// </summary>
        LowerTolerance2,

        /// <summary>
        /// The second upper tolerance offset.
        /// </summary>
        UpperTolerance2,

        /// <summary>
        /// The increasing setpoint rate on a controlled parameter.
        /// </summary>
        RampUp,

        /// <summary>
        /// The decreasing setpoint rate on a controlled parameter.
        /// </summary>
        RampDown
    }

    /// <summary>
    /// Commits parameter settings and performs device-specific monitoring calibration.
    /// </summary>
    /// <remarks>
    /// A plain MonitoredParameter setpoint is informational; it must not command hardware.
    /// A ControlledParameter setpoint controls its device. The provider is bound to the
    /// concrete instance and must preserve that distinction.
    /// </remarks>
    public interface IMonitoredParameterProvider
    {
        /// <summary>
        /// Commits a numeric setting after the binding has validated its type and limits.
        /// </summary>
        ValueTask WriteSettingAsync(
            PlasticsRubberRequestContext context,
            MonitoredParameterSetting setting,
            double value,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits selection of automatic or manual monitoring tolerances.
        /// </summary>
        ValueTask SetAutomaticMonitoringAsync(
            PlasticsRubberRequestContext context,
            bool automatic,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits the supported sensitivity value, zero through two.
        /// </summary>
        ValueTask SetSensitivityAsync(
            PlasticsRubberRequestContext context,
            ushort sensitivity,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits the supported suppression value, zero through two.
        /// </summary>
        ValueTask SetAlarmSuppressionAsync(
            PlasticsRubberRequestContext context,
            ushort suppression,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Recalculates and commits tolerances from the current measurement and device calibration.
        /// </summary>
        ValueTask<MonitoredParameterLimits> ResetMonitoringAsync(
            PlasticsRubberRequestContext context,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implements monitoring status and authorized optional settings on generated parameter instances.
    /// </summary>
    public sealed class MonitoredParameterBinding : IAsyncDisposable
    {
        /// <summary>
        /// Binds the selected members without inventing unselected options or calibration constants.
        /// </summary>
        public MonitoredParameterBinding(
            MonitoredParameterState node,
            ISystemContext context,
            IMonitoredParameterProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy)
            : this(node, context, provider, accessPolicy, readCommittedSettings: null)
        {
        }

        /// <summary>
        /// Binds parameter commands while optionally leaving value publication to an external snapshot owner.
        /// </summary>
        /// <param name="node">The selected generated parameter.</param>
        /// <param name="context">The owning address-space context.</param>
        /// <param name="provider">The asynchronous command authority.</param>
        /// <param name="accessPolicy">The original-caller authorization policy.</param>
        /// <param name="readCommittedSettings">
        /// Supplies limits and monitoring mode from one committed observation. A non-null reader suppresses
        /// write-through and reset-result publication; the owner publishes observations and calls
        /// <see cref="RefreshWriteAccess"/> when settings change. Null preserves standalone behavior.
        /// </param>
        public MonitoredParameterBinding(
            MonitoredParameterState node,
            ISystemContext context,
            IMonitoredParameterProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy,
            Func<(MonitoredParameterLimits Limits, bool AutomaticMonitoring)>? readCommittedSettings)
        {
            Node = node ?? throw new ArgumentNullException(nameof(node));
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            m_readCommittedSettings = readCommittedSettings;
            if (node.ActualValue == null || node.ActualValue.EngineeringUnits == null)
            {
                throw new ArgumentException("ActualValue and its EngineeringUnits are required.", nameof(node));
            }
            m_binding = new PlasticsRubberMethodBinding(
                node, context, accessPolicy, suppressCacheUpdate: readCommittedSettings != null);
            ReadLimits().Validate();
            BindNumeric(node.SetValue, MonitoredParameterSetting.SetValue);
            BindNumeric(node.MinValue, MonitoredParameterSetting.Minimum);
            BindNumeric(node.MaxValue, MonitoredParameterSetting.Maximum);
            BindNumeric(node.LowerTolerance, MonitoredParameterSetting.LowerTolerance);
            BindNumeric(node.UpperTolerance, MonitoredParameterSetting.UpperTolerance);
            BindNumeric(node.LowerTolerance2, MonitoredParameterSetting.LowerTolerance2);
            BindNumeric(node.UpperTolerance2, MonitoredParameterSetting.UpperTolerance2);
            if (node is ControlledParameterState)
            {
                BindNumeric(node.SetRampUp, MonitoredParameterSetting.RampUp);
                BindNumeric(node.SetRampDown, MonitoredParameterSetting.RampDown);
            }
            if (node.AutomaticMonitoring != null)
            {
                m_binding.BindWrite(node.AutomaticMonitoring, async (request, value, ct) =>
                {
                    if (!value.TryGetValue(out bool automatic))
                    {
                        throw Error(StatusCodes.BadTypeMismatch, "AutomaticMonitoring requires a Boolean.");
                    }
                    await provider.SetAutomaticMonitoringAsync(request, automatic, ct).ConfigureAwait(false);
                    if (m_readCommittedSettings == null)
                    {
                        SetToleranceWriteability(!automatic);
                    }
                });
            }
            if (node.MonitoringSensitivity != null)
            {
                m_binding.BindWrite(node.MonitoringSensitivity, (request, value, ct) =>
                    provider.SetSensitivityAsync(request, DiscreteValue(value), ct));
            }
            if (node.AlarmSuppression != null)
            {
                m_binding.BindWrite(node.AlarmSuppression, (request, value, ct) =>
                    provider.SetAlarmSuppressionAsync(request, DiscreteValue(value), ct));
            }
            if (node.ResetMonitoring != null)
            {
                m_binding.Bind(node.ResetMonitoring, async (request, ct) =>
                {
                    MonitoredParameterLimits limits =
                        await provider.ResetMonitoringAsync(request, ct).ConfigureAwait(false);
                    limits.Validate();
                    if (m_readCommittedSettings == null)
                    {
                        ApplyLimits(limits);
                        AnalogItemState<double> actual = Node.ActualValue ??
                            throw Error(StatusCodes.BadConfigurationError, "The monitored value was removed.");
                        Publish(actual.Value, actual.StatusCode, actual.Timestamp);
                    }
                });
            }
            SetToleranceWriteability(node.AutomaticMonitoring?.Value != true);
        }

        /// <summary>
        /// Gets the generated monitored or controlled parameter.
        /// </summary>
        public MonitoredParameterState Node { get; }

        /// <summary>
        /// Publishes an absolute measurement with its original quality and timestamp.
        /// </summary>
        /// <remarks>
        /// Status remains observable when alarms are suppressed. This method does not synthesize
        /// device-specific alarm side effects or make a good-quality product claim.
        /// </remarks>
        public void Publish(double actualValue, StatusCode quality, DateTimeUtc sourceTimestamp)
        {
            ushort status = ReadSettings().Limits.Evaluate(actualValue, quality);
            Node.ActualValue!.Value = actualValue;
            Node.ActualValue.StatusCode = quality;
            Node.ActualValue.Timestamp = sourceTimestamp;
            if (Node.Status != null)
            {
                Node.Status.Value = status;
                Node.Status.StatusCode = StatusCodes.Good;
                Node.Status.Timestamp = sourceTimestamp;
                if (Node.Status.ValueAsText != null)
                {
                    Node.Status.ValueAsText.Value = new LocalizedText(s_statusNames[status]);
                }
            }
            if (m_readCommittedSettings != null)
            {
                RefreshWriteAccess();
            }
            Node.ClearChangeMasks(m_context, true);
        }

        /// <summary>
        /// Updates advertised tolerance access from the current committed monitoring mode without publishing values.
        /// </summary>
        /// <param name="allowWrites">Whether the owner currently permits mutation of the device.</param>
        public void RefreshWriteAccess(bool allowWrites = true)
        {
            SetToleranceWriteability(allowWrites && !ReadSettings().AutomaticMonitoring);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_binding.DisposeAsync();
        }

        /// <summary>
        /// Binds a selected numeric member to its typed provider operation.
        /// </summary>
        private void BindNumeric(AnalogItemState<double>? member, MonitoredParameterSetting setting)
        {
            if (member == null)
            {
                return;
            }
            if (member.EngineeringUnits == null)
            {
                throw new ArgumentException("Every selected AnalogItem requires EngineeringUnits.", nameof(member));
            }
            m_binding.BindWrite(member, async (request, value, ct) =>
            {
                if (!value.TryGetValue(out double number))
                {
                    throw Error(StatusCodes.BadTypeMismatch, "A Double setting is required.");
                }
                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    throw Error(StatusCodes.BadOutOfRange, "A setting must be finite.");
                }
                bool tolerance = setting is MonitoredParameterSetting.LowerTolerance
                    or MonitoredParameterSetting.UpperTolerance
                    or MonitoredParameterSetting.LowerTolerance2
                    or MonitoredParameterSetting.UpperTolerance2;
                (MonitoredParameterLimits currentLimits, bool automatic) = ReadSettings();
                if (tolerance && automatic)
                {
                    throw Error(StatusCodes.BadNotWritable, "Automatic monitoring owns the tolerance settings.");
                }
                if ((setting is MonitoredParameterSetting.RampUp or MonitoredParameterSetting.RampDown) && number < 0)
                {
                    throw Error(StatusCodes.BadOutOfRange, "Ramp rates must be nonnegative.");
                }
                MonitoredParameterLimits limits = currentLimits;
                limits = setting switch
                {
                    MonitoredParameterSetting.SetValue => limits with { SetValue = number },
                    MonitoredParameterSetting.Minimum => limits with { Minimum = number },
                    MonitoredParameterSetting.Maximum => limits with { Maximum = number },
                    MonitoredParameterSetting.LowerTolerance => limits with { LowerTolerance = number },
                    MonitoredParameterSetting.UpperTolerance => limits with { UpperTolerance = number },
                    MonitoredParameterSetting.LowerTolerance2 => limits with { LowerTolerance2 = number },
                    MonitoredParameterSetting.UpperTolerance2 => limits with { UpperTolerance2 = number },
                    _ => limits
                };
                try
                {
                    limits.Validate();
                }
                catch (ArgumentException exception)
                {
                    throw Error(StatusCodes.BadOutOfRange, exception.Message);
                }
                await m_provider.WriteSettingAsync(request, setting, number, ct).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// Reads validation inputs from one observation, or the standalone binding's current values.
        /// </summary>
        private (MonitoredParameterLimits Limits, bool AutomaticMonitoring) ReadSettings()
        {
            if (m_readCommittedSettings == null)
            {
                return (ReadLimits(), Node.AutomaticMonitoring?.Value == true);
            }
            (MonitoredParameterLimits limits, bool automatic) = m_readCommittedSettings();
            if (limits == null)
            {
                throw Error(StatusCodes.BadConfigurationError, "The committed parameter settings are missing.");
            }
            return (limits, automatic);
        }

        /// <summary>
        /// Reads selected limits without treating missing optional nodes as zero limits.
        /// </summary>
        private MonitoredParameterLimits ReadLimits()
        {
            return new MonitoredParameterLimits
            {
                SetValue = Node.SetValue?.Value,
                Minimum = Node.MinValue?.Value,
                Maximum = Node.MaxValue?.Value,
                LowerTolerance = Node.LowerTolerance?.Value,
                UpperTolerance = Node.UpperTolerance?.Value,
                LowerTolerance2 = Node.LowerTolerance2?.Value,
                UpperTolerance2 = Node.UpperTolerance2?.Value
            };
        }

        /// <summary>
        /// Applies committed tolerance calibration only to the selected optional members.
        /// </summary>
        private void ApplyLimits(MonitoredParameterLimits limits)
        {
            if ((Node.LowerTolerance != null && !limits.LowerTolerance.HasValue) ||
                (Node.UpperTolerance != null && !limits.UpperTolerance.HasValue) ||
                (Node.LowerTolerance2 != null && !limits.LowerTolerance2.HasValue) ||
                (Node.UpperTolerance2 != null && !limits.UpperTolerance2.HasValue))
            {
                throw Error(StatusCodes.BadConfigurationError, "The provider omitted a selected tolerance.");
            }
            SetSelected(Node.LowerTolerance, limits.LowerTolerance);
            SetSelected(Node.UpperTolerance, limits.UpperTolerance);
            SetSelected(Node.LowerTolerance2, limits.LowerTolerance2);
            SetSelected(Node.UpperTolerance2, limits.UpperTolerance2);
        }

        /// <summary>
        /// Keeps advertised tolerance access in step with AutomaticMonitoring.
        /// </summary>
        private void SetToleranceWriteability(bool writable)
        {
            SetWriteable(Node.LowerTolerance, writable);
            SetWriteable(Node.UpperTolerance, writable);
            SetWriteable(Node.LowerTolerance2, writable);
            SetWriteable(Node.UpperTolerance2, writable);
        }

        /// <summary>
        /// Updates an existing tolerance after device-specific calibration.
        /// </summary>
        private static void SetSelected(AnalogItemState<double>? member, double? value)
        {
            if (member != null)
            {
                member.Value = value ?? throw Error(
                    StatusCodes.BadConfigurationError,
                    "The provider omitted a selected tolerance.");
            }
        }

        /// <summary>
        /// Updates access without removing unrelated access bits.
        /// </summary>
        private static void SetWriteable(BaseVariableState? member, bool writable)
        {
            if (member != null)
            {
                member.AccessLevel = writable
                    ? (byte)(member.AccessLevel | AccessLevels.CurrentWrite)
                    : (byte)(member.AccessLevel & ~AccessLevels.CurrentWrite);
                member.UserAccessLevel = writable
                    ? (byte)(member.UserAccessLevel | AccessLevels.CurrentWrite)
                    : (byte)(member.UserAccessLevel & ~AccessLevels.CurrentWrite);
            }
        }

        /// <summary>
        /// Validates the defined sensitivity and alarm-suppression domains.
        /// </summary>
        private static ushort DiscreteValue(Variant value)
        {
            if (!value.TryGetValue(out ushort result))
            {
                throw Error(StatusCodes.BadTypeMismatch, "A UInt16 discrete value is required.");
            }
            if (result > 2)
            {
                throw Error(StatusCodes.BadOutOfRange, "The discrete value must be zero, one or two.");
            }
            return result;
        }

        /// <summary>
        /// Creates an explicit OPC UA operation failure.
        /// </summary>
        private static ServiceResultException Error(StatusCode status, string message)
        {
            return ServiceResultException.Create(status, message);
        }

        /// <summary>
        /// The owner's context.
        /// </summary>
        private readonly ISystemContext m_context;

        /// <summary>The optional atomic settings reader supplied by the external snapshot owner.</summary>
        private readonly Func<(MonitoredParameterLimits Limits, bool AutomaticMonitoring)>? m_readCommittedSettings;

        /// <summary>
        /// The device-specific setting and calibration provider.
        /// </summary>
        private readonly IMonitoredParameterProvider m_provider;

        /// <summary>
        /// The operation lifetime and access guard.
        /// </summary>
        private readonly PlasticsRubberMethodBinding m_binding;

        /// <summary>
        /// The standardized UInt16 status labels.
        /// </summary>
        private static readonly string[] s_statusNames =
        [
            "NONE", "UNKNOWN", "BELOW_MIN_VALUE", "BELOW_LOWER_TOLERANCE2", "BELOW_LOWER_TOLERANCE",
            "WITHIN_TOLERANCE", "ABOVE_UPPER_TOLERANCE", "ABOVE_UPPER_TOLERANCE2", "ABOVE_MAX_VALUE"
        ];
    }
}
