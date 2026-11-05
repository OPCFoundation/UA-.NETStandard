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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di;
using Opc.Ua.IA;

namespace Opc.Ua.IA.Server
{
    /// <summary>
    /// Binds source-reported stacklight observations to an existing generated IA stacklight state.
    /// </summary>
    public sealed class IaStacklight : IDisposable
    {
        /// <summary>
        /// Creates a runtime binding for an already-created IA stacklight state.
        /// </summary>
        /// <param name="context">The server system context used to inspect and publish the state.</param>
        /// <param name="state">The generated basic stacklight state or its stacklight subtype.</param>
        /// <param name="provider">The source of stacklight observations.</param>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ServiceResultException">
        /// The existing state has an invalid or unsupported topology.
        /// </exception>
        public IaStacklight(
            ISystemContext context,
            BasicStacklightState state,
            IIaStacklightProvider provider)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_state = state ?? throw new ArgumentNullException(nameof(state));
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));

            m_stacklightMode = RequireVariable(context, state, "StacklightMode");
            m_deviceHealth = FindVariable(context, state, "DeviceHealth");
            m_levelPercent = FindVariable(context, FindChild(context, state, "StackLevel"), "LevelPercent");
            m_levelDisplayMode = FindVariable(context, FindChild(context, state, "StackLevel"), "DisplayMode");
            m_stackRunning = FindChild(context, state, "StackRunning") as BaseObjectState;

            if ((m_levelPercent is null) != (m_levelDisplayMode is null))
            {
                throw Configuration("StackLevel must expose both LevelPercent and DisplayMode.");
            }

            m_levelPercentRange = m_levelPercent is null ? null : RequireRange(context, m_levelPercent);
            m_elements = BindElements(context, state);
            RestrictToReadOnly(context, state);
        }

        /// <summary>
        /// Gets the caller-owned generated stacklight.
        /// </summary>
        public BasicStacklightState State => m_state;

        /// <summary>
        /// Reads and publishes one coherent source observation, preserving source quality and timestamps.
        /// </summary>
        /// <remarks>
        /// The refresh gate remains held until committed notifications complete. Notification sinks must
        /// not await or synchronously block on another refresh through this binding. Notifications ignore
        /// caller cancellation; errors propagate without reverting published values.
        /// </remarks>
        /// <param name="cancellationToken">The token used to cancel acquisition or pre-publication validation.</param>
        /// <returns>A task that completes after the state change masks are cleared.</returns>
        public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            IaStacklightSnapshot snapshot;
            await m_refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                snapshot = await m_provider
                    .ReadSnapshotAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (snapshot is null)
                {
                    throw Configuration("The stacklight provider returned no snapshot.");
                }

                ValidateSnapshot(snapshot);
                cancellationToken.ThrowIfCancellationRequested();
                ApplySnapshot(snapshot);
                await m_state.ClearChangeMasksAsync(m_context, true, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                m_refreshGate.Release();
            }
        }

        /// <summary>
        /// Releases the refresh gate after the owning application has drained its operations.
        /// </summary>
        public void Dispose()
        {
            m_refreshGate.Dispose();
        }

        /// <summary>
        /// Captures the fixed ordered inventory and validates mounting-base placement.
        /// </summary>
        private static ElementBinding[] BindElements(ISystemContext context, BasicStacklightState state)
        {
            var elements = new List<ElementBinding>();
            foreach (BaseInstanceState child in GetChildren(context, state))
            {
                if (child is StackElementLightState light)
                {
                    elements.Add(new ElementBinding(
                        context, ElementKind.Light, light, ReadNumberInList(context, light)));
                }
                else if (child is StackElementAcousticState acoustic)
                {
                    elements.Add(new ElementBinding(
                        context,
                        ElementKind.Acoustic,
                        acoustic,
                        ReadNumberInList(context, acoustic)));
                }
            }

            ElementBinding[] orderedElements = elements.OrderBy(item => item.NumberInList).ToArray();

            if (orderedElements.Length == 0)
            {
                throw Configuration("A stacklight must contain at least one stack element.");
            }

            bool passedBase = false;
            for (int index = 0; index < orderedElements.Length; index++)
            {
                if (orderedElements[index].NumberInList != (uint)index)
                {
                    throw Configuration(
                        "Stack elements must be numbered consecutively from the base, starting at zero.");
                }

                orderedElements[index].Bind();
                BaseVariableState? baseProperty = FindVariable(
                    context, orderedElements[index].State, "IsPartOfBase");
                bool partOfBase = false;
                if (baseProperty != null && !baseProperty.WrappedValue.TryGetValue(out partOfBase))
                {
                    throw Configuration("A configured IsPartOfBase property requires a Boolean value.");
                }
                if (partOfBase && passedBase)
                {
                    throw Configuration("Elements that belong to the mounting base must precede all other elements.");
                }
                passedBase |= !partOfBase;
            }

            return orderedElements;
        }

        /// <summary>
        /// Validates all observations before changing any cached variable.
        /// </summary>
        private void ValidateSnapshot(IaStacklightSnapshot snapshot)
        {
            ValidateSample(snapshot.StacklightMode, "StacklightMode");
            ValidateEnumValue(snapshot.StacklightMode.Value, 0, 3, "StacklightMode");

            if (snapshot.Elements.Count != m_elements.Length)
            {
                throw Configuration("The stacklight element inventory changed after binding.");
            }

            for (int index = 0; index < m_elements.Length; index++)
            {
                ElementBinding binding = m_elements[index];
                IaStacklightElementSnapshot element = snapshot.Elements[index];
                if (element is null ||
                    (binding.Kind == ElementKind.Light && element is not IaStacklightLightSnapshot) ||
                    (binding.Kind == ElementKind.Acoustic && element is not IaStacklightAcousticSnapshot))
                {
                    throw Configuration("The ordered stacklight element inventory does not match the bound state.");
                }

                binding.Validate(element);
            }

            switch (snapshot.StacklightMode.Value)
            {
                case StacklightOperationMode.Segmented:
                    ValidateNoLevelSamples(snapshot);
                    break;
                case StacklightOperationMode.Levelmeter:
                    if (m_levelPercent is null || m_levelDisplayMode is null || m_levelPercentRange is null)
                    {
                        throw Configuration("Level-meter mode requires a configured StackLevel with EURange.");
                    }
                    if (!snapshot.LevelPercent.HasValue || !snapshot.LevelDisplayMode.HasValue)
                    {
                        throw Configuration("Level-meter mode requires LevelPercent and DisplayMode samples.");
                    }
                    ValidateSample(snapshot.LevelPercent.Value, "LevelPercent");
                    ValidateSample(snapshot.LevelDisplayMode.Value, "DisplayMode");
                    ValidateEnumValue(snapshot.LevelDisplayMode.Value.Value, 0, 2, "DisplayMode");
                    ValidateRange(snapshot.LevelPercent.Value.Value, m_levelPercentRange!, "LevelPercent");
                    break;
                case StacklightOperationMode.Running_Light:
                    if (m_stackRunning is null)
                    {
                        throw Configuration("Running-light mode requires the optional StackRunning object.");
                    }
                    ValidateNoLevelSamples(snapshot);
                    break;
                case StacklightOperationMode.Other:
                    ValidateNoLevelSamples(snapshot);
                    break;
                default:
                    throw Configuration("The stacklight operation mode is not defined by the IA model.");
            }

            RequireOptionalSample(m_deviceHealth, snapshot.DeviceHealth, "DeviceHealth");
            if (snapshot.DeviceHealth.HasValue)
            {
                ValidateEnumValue(snapshot.DeviceHealth.Value.Value, 0, 4, "DeviceHealth");
            }
        }

        /// <summary>
        /// Rejects level observations for representations where they have no meaning.
        /// </summary>
        private static void ValidateNoLevelSamples(IaStacklightSnapshot snapshot)
        {
            if (snapshot.LevelPercent.HasValue || snapshot.LevelDisplayMode.HasValue)
            {
                throw Configuration("Level samples are only valid in level-meter mode.");
            }
        }

        /// <summary>
        /// Publishes an already-validated observation without awaiting between fields.
        /// </summary>
        private void ApplySnapshot(IaStacklightSnapshot snapshot)
        {
            Apply(m_stacklightMode, snapshot.StacklightMode);
            for (int index = 0; index < m_elements.Length; index++)
            {
                m_elements[index].Apply(snapshot.Elements[index]);
            }

            if (snapshot.StacklightMode.Value == StacklightOperationMode.Levelmeter)
            {
                Apply(m_levelPercent!, snapshot.LevelPercent!.Value);
                Apply(m_levelDisplayMode!, snapshot.LevelDisplayMode!.Value);
            }

            if (m_deviceHealth is not null)
            {
                Apply(m_deviceHealth, snapshot.DeviceHealth!.Value);
            }
        }

        /// <summary>
        /// Matches observation presence to the configured optional variable.
        /// </summary>
        /// <typeparam name="T">The sampled scalar type.</typeparam>
        private static void RequireOptionalSample<T>(
            BaseVariableState? variable,
            IaStacklightSample<T>? sample,
            string path)
        {
            if ((variable is null) != !sample.HasValue)
            {
                throw Configuration($"{path} sample presence must match the configured state.");
            }

            if (sample.HasValue)
            {
                ValidateSample(sample.Value, path);
            }
        }

        /// <summary>
        /// Requires an acquisition time rather than substituting publication time.
        /// </summary>
        /// <typeparam name="T">The sampled scalar type.</typeparam>
        private static void ValidateSample<T>(IaStacklightSample<T> sample, string path)
        {
            if (sample.SourceTimestamp.IsNull)
            {
                throw Configuration($"{path} must carry a source timestamp.");
            }
        }

        /// <summary>
        /// Rejects undefined values from the contiguous IA enumerations.
        /// </summary>
        /// <typeparam name="T">The generated enumeration type.</typeparam>
        private static void ValidateEnumValue<T>(T value, int minimum, int maximum, string path)
            where T : struct, Enum
        {
            int numericValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (numericValue < minimum || numericValue > maximum)
            {
                throw Configuration($"{path} contains an enum value outside the IA model.");
            }
        }

        /// <summary>
        /// Reads the actual namespace-zero engineering range.
        /// </summary>
        private static Range RequireRange(ISystemContext context, BaseVariableState variable)
        {
            if (FindVariable(context, variable, "EURange") is not { } rangeVariable ||
                !rangeVariable.WrappedValue.TryGetStructure<Range>(out Range? range) ||
                range == null ||
                double.IsNaN(range.Low) ||
                double.IsInfinity(range.Low) ||
                double.IsNaN(range.High) ||
                double.IsInfinity(range.High) ||
                range.High <= range.Low)
            {
                throw Configuration($"{variable.BrowseName.Name} requires a valid EURange.");
            }

            return range;
        }

        /// <summary>
        /// Checks finite values against inclusive bounds and the acoustic positivity rule.
        /// </summary>
        private static void ValidateRange(float value, Range range, string path, bool strictlyPositive = false)
        {
            if (float.IsNaN(value) ||
                float.IsInfinity(value) ||
                value < range.Low ||
                value > range.High ||
                (strictlyPositive && value <= 0))
            {
                throw Configuration($"{path} is outside its valid range.");
            }
        }

        /// <summary>
        /// Reads the ordered-object index without converting an arbitrary variant.
        /// </summary>
        private static uint ReadNumberInList(ISystemContext context, BaseObjectState element)
        {
            BaseVariableState variable = RequireVariable(context, element, "NumberInList");
            if (!variable.WrappedValue.TryGetValue(out uint number))
            {
                throw Configuration($"{element.BrowseName.Name} has an invalid NumberInList value.");
            }

            return number;
        }

        /// <summary>
        /// Requires a configured standard variable.
        /// </summary>
        private static BaseVariableState RequireVariable(ISystemContext context, NodeState? parent, string name)
        {
            return FindVariable(context, parent, name) ??
                throw Configuration($"The stacklight state is missing the required {name} Variable.");
        }

        /// <summary>
        /// Finds an optional standard variable without accepting a same-named vendor object.
        /// </summary>
        private static BaseVariableState? FindVariable(ISystemContext context, NodeState? parent, string name)
        {
            return FindChild(context, parent, name) as BaseVariableState;
        }

        /// <summary>
        /// Resolves standard children by their owning namespace and rejects ambiguous duplicates.
        /// </summary>
        private static BaseInstanceState? FindChild(ISystemContext context, NodeState? parent, string name)
        {
            if (parent is null)
            {
                return null;
            }

            string uri = name switch
            {
                "NumberInList" or "EURange" => Opc.Ua.Namespaces.OpcUa,
                "DeviceHealth" => Opc.Ua.Di.Namespaces.OpcUaDi,
                _ => Namespaces.IA
            };
            int index = context.NamespaceUris.GetIndex(uri);
            if (index < 0)
            {
                return null;
            }
            var qualifiedName = new QualifiedName(name, (ushort)index);
            return GetChildren(context, parent).SingleOrDefault(child => child.BrowseName == qualifiedName);
        }

        /// <summary>
        /// Captures a fixed child inventory from a not-yet-registered subtree.
        /// </summary>
        private static List<BaseInstanceState> GetChildren(ISystemContext context, NodeState parent)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);
            return children;
        }

        /// <summary>
        /// Prevents network writes to provider-owned observations.
        /// </summary>
        private static void RestrictToReadOnly(ISystemContext context, NodeState root)
        {
            var pending = new Stack<NodeState>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                NodeState node = pending.Pop();
                if (node is BaseVariableState variable)
                {
                    variable.AccessLevel = AccessLevels.CurrentRead;
                    variable.UserAccessLevel = AccessLevels.CurrentRead;
                }

                foreach (BaseInstanceState child in GetChildren(context, node))
                {
                    pending.Push(child);
                }
            }
        }

        /// <summary>
        /// Encodes the closed set of model value types without reflected generic conversion.
        /// </summary>
        /// <typeparam name="T">The sampled scalar type.</typeparam>
        private static void Apply<T>(BaseVariableState variable, IaStacklightSample<T> sample)
        {
            variable.WrappedValue = sample.Value switch
            {
                bool value => Variant.From(value),
                float value => Variant.From(value),
                uint value => Variant.From(value),
                StacklightOperationMode value => Variant.From(value),
                SignalColor value => Variant.From(value),
                SignalModeLight value => Variant.From(value),
                LevelDisplayMode value => Variant.From(value),
                DeviceHealthEnumeration value => Variant.From(value),
                RGBWDataType value => Variant.FromStructure(new RGBWDataType
                {
                    EncodingMask = value.EncodingMask,
                    Red = value.Red,
                    Green = value.Green,
                    Blue = value.Blue,
                    White = value.White
                }),
                _ => throw Configuration("Unsupported stacklight sample type.")
            };
            variable.StatusCode = sample.StatusCode;
            variable.Timestamp = sample.SourceTimestamp;
        }

        /// <summary>
        /// Reports an invalid topology or provider observation explicitly.
        /// </summary>
        private static ServiceResultException Configuration(string message)
        {
            return new ServiceResultException(StatusCodes.BadConfigurationError, message);
        }

        /// <summary>
        /// Distinguishes the two concrete standard element representations.
        /// </summary>
        private enum ElementKind
        {
            /// <summary>A lamp with direct controls or separate channels.</summary>
            Light,

            /// <summary>An acoustic element selecting an ordered signal.</summary>
            Acoustic
        }

        /// <summary>
        /// Caches one fixed element topology and validates its provider observations.
        /// </summary>
        private sealed class ElementBinding
        {
            /// <summary>
            /// Retains the caller-owned element and its ordered position.
            /// </summary>
            public ElementBinding(
                ISystemContext context,
                ElementKind kind,
                BaseObjectState state,
                uint numberInList)
            {
                m_context = context;
                Kind = kind;
                m_state = state;
                m_numberInList = numberInList;
            }

            /// <summary>
            /// Gets the concrete element representation.
            /// </summary>
            public ElementKind Kind { get; }

            /// <summary>
            /// Gets the declared ordered position.
            /// </summary>
            public uint NumberInList => m_numberInList;

            /// <summary>
            /// Gets the existing element for fixed topology inspection.
            /// </summary>
            public BaseObjectState State => m_state;

            /// <summary>
            /// Validates and caches optional controls, ranges and signal inventory.
            /// </summary>
            public void Bind()
            {
                m_signalOn = FindVariable(m_context, m_state, "SignalOn");
                if (Kind == ElementKind.Light)
                {
                    m_signalColor = FindVariable(m_context, m_state, "SignalColor");
                    m_signalMode = FindVariable(m_context, m_state, "SignalMode");
                    m_rgbwValue = FindVariable(m_context, m_state, "SignalRGBWValue");
                    m_intensity = FindVariable(m_context, m_state, "Intensity");
                    m_channels = GetChildren(m_context, m_state)
                        .OfType<ControlChannelState>()
                        .OrderBy(channel => channel.BrowseName.NamespaceIndex)
                        .ThenBy(channel => channel.BrowseName.Name, StringComparer.Ordinal)
                        .Cast<BaseObjectState>()
                        .ToArray();

                    if (m_channels.Length > 0 &&
                        (m_signalOn is not null ||
                            m_signalColor is not null ||
                            m_signalMode is not null ||
                            m_rgbwValue is not null ||
                            m_intensity is not null))
                    {
                        throw Configuration("A light element cannot combine direct values and ControlChannels.");
                    }
                    if (m_rgbwValue != null && m_signalColor == null)
                    {
                        throw Configuration("SignalRGBWValue requires the SignalColor variable.");
                    }

                    m_channelBindings = m_channels
                        .Select(channel => new ChannelBinding(m_context, channel))
                        .ToArray();
                    if (m_intensity is not null)
                    {
                        m_intensityRange = RequireRange(m_context, m_intensity);
                    }
                    m_acousticSignalNumbers = [];
                    return;
                }

                m_operationMode = RequireVariable(m_context, m_state, "OperationMode");
                m_intensity = FindVariable(m_context, m_state, "Intensity");
                if (m_intensity is not null)
                {
                    m_intensityRange = RequireRange(m_context, m_intensity);
                }

                if (FindChild(m_context, m_state, "AcousticSignals") is not BaseObjectState signals)
                {
                    throw Configuration("An acoustic element must contain its AcousticSignals list.");
                }

                m_acousticSignalNumbers = GetChildren(m_context, signals)
                    .OfType<AcousticSignalState>()
                    .Select(signal => ReadNumberInList(m_context, signal))
                    .ToArray();
                if (m_acousticSignalNumbers.Length == 0 ||
                    m_acousticSignalNumbers.Distinct().Count() != m_acousticSignalNumbers.Length)
                {
                    throw Configuration("AcousticSignals must contain a non-empty inventory with unique numbers.");
                }

                m_channels = [];
                m_channelBindings = [];
            }

            /// <summary>
            /// Validates an observation after its discriminator has been checked.
            /// </summary>
            public void Validate(IaStacklightElementSnapshot snapshot)
            {
                if (Kind == ElementKind.Light)
                {
                    ValidateLight((IaStacklightLightSnapshot)snapshot);
                    return;
                }

                ValidateAcoustic((IaStacklightAcousticSnapshot)snapshot);
            }

            /// <summary>
            /// Publishes a previously validated element observation.
            /// </summary>
            public void Apply(IaStacklightElementSnapshot snapshot)
            {
                if (Kind == ElementKind.Light)
                {
                    ApplyLight((IaStacklightLightSnapshot)snapshot);
                    return;
                }

                ApplyAcoustic((IaStacklightAcousticSnapshot)snapshot);
            }

            /// <summary>
            /// Validates direct or channel-specific observations against the selected topology.
            /// </summary>
            private void ValidateLight(IaStacklightLightSnapshot light)
            {
                if (m_channels.Length == 0)
                {
                    if (light.Channels.Count != 0)
                    {
                        throw Configuration("A direct light observation cannot include unconfigured control channels.");
                    }
                    RequireOptionalSample(m_signalOn, light.SignalOn, Path("SignalOn"));
                    RequireOptionalSample(m_signalColor, light.SignalColor, Path("SignalColor"));
                    RequireOptionalSample(m_signalMode, light.SignalMode, Path("SignalMode"));
                    RequireOptionalSample(m_rgbwValue, light.SignalRGBWValue, Path("SignalRGBWValue"));
                    RequireOptionalSample(m_intensity, light.Intensity, Path("Intensity"));
                    ValidateColor(light.SignalColor, Path("SignalColor"));
                    ValidateLightMode(light.SignalMode, Path("SignalMode"));
                    if (light.SignalRGBWValue.HasValue && !light.SignalColor.HasValue)
                    {
                        throw Configuration("SignalRGBWValue requires a SignalColor sample.");
                    }
                    if (light.SignalRGBWValue is { } rgbw &&
                        (rgbw.Value == null || (rgbw.Value.EncodingMask & ~(uint)RGBWDataTypeFields.White) != 0))
                    {
                        throw Configuration("SignalRGBWValue requires a valid RGBW structure and optional-field mask.");
                    }
                    if (light.Intensity.HasValue)
                    {
                        ValidateRange(light.Intensity.Value.Value, m_intensityRange!, Path("Intensity"));
                    }
                    return;
                }

                if (light.Channels.Count != m_channelBindings.Length ||
                    light.SignalOn.HasValue ||
                    light.SignalColor.HasValue ||
                    light.SignalMode.HasValue ||
                    light.SignalRGBWValue.HasValue ||
                    light.Intensity.HasValue)
                {
                    throw Configuration($"{Path("ControlChannels")} inventory does not match its configured channels.");
                }

                for (int index = 0; index < m_channelBindings.Length; index++)
                {
                    m_channelBindings[index].Validate(light.Channels[index]);
                }
            }

            /// <summary>
            /// Validates selected signal identity and strictly positive acoustic intensity.
            /// </summary>
            private void ValidateAcoustic(IaStacklightAcousticSnapshot acoustic)
            {
                RequireOptionalSample(m_signalOn, acoustic.SignalOn, Path("SignalOn"));
                ValidateSample(acoustic.OperationMode, Path("OperationMode"));
                if (!m_acousticSignalNumbers.Contains(acoustic.OperationMode.Value))
                {
                    throw Configuration("Acoustic OperationMode must match a NumberInList in AcousticSignals.");
                }

                RequireOptionalSample(m_intensity, acoustic.Intensity, Path("Intensity"));
                if (acoustic.Intensity.HasValue)
                {
                    ValidateRange(
                        acoustic.Intensity.Value.Value, m_intensityRange!, Path("Intensity"), strictlyPositive: true);
                }
            }

            /// <summary>
            /// Publishes a direct light or its independent channels.
            /// </summary>
            private void ApplyLight(IaStacklightLightSnapshot light)
            {
                if (m_channels.Length == 0)
                {
                    ApplyIfConfigured(m_signalOn, light.SignalOn);
                    ApplyIfConfigured(m_signalColor, light.SignalColor);
                    ApplyIfConfigured(m_signalMode, light.SignalMode);
                    ApplyIfConfigured(m_rgbwValue, light.SignalRGBWValue);
                    ApplyIfConfigured(m_intensity, light.Intensity);
                    return;
                }

                for (int index = 0; index < m_channelBindings.Length; index++)
                {
                    m_channelBindings[index].Apply(light.Channels[index]);
                }
            }

            /// <summary>
            /// Publishes the selected acoustic observation.
            /// </summary>
            private void ApplyAcoustic(IaStacklightAcousticSnapshot acoustic)
            {
                ApplyIfConfigured(m_signalOn, acoustic.SignalOn);
                IaStacklight.Apply(m_operationMode!, acoustic.OperationMode);
                ApplyIfConfigured(m_intensity, acoustic.Intensity);
            }

            /// <summary>
            /// Identifies an element member in a configuration error.
            /// </summary>
            private string Path(string member)
            {
                return $"{m_state.BrowseName.Name}.{member}";
            }

            /// <summary>
            /// Validates an optional generated color enumeration.
            /// </summary>
            private static void ValidateColor(IaStacklightSample<SignalColor>? sample, string path)
            {
                if (sample.HasValue)
                {
                    ValidateSample(sample.Value, path);
                    ValidateEnumValue(sample.Value.Value, 0, 7, path);
                }
            }

            /// <summary>
            /// Validates an optional generated signal-mode enumeration.
            /// </summary>
            private static void ValidateLightMode(IaStacklightSample<SignalModeLight>? sample, string path)
            {
                if (sample.HasValue)
                {
                    ValidateSample(sample.Value, path);
                    ValidateEnumValue(sample.Value.Value, 0, 3, path);
                }
            }

            /// <summary>
            /// Publishes an optional observation after presence validation.
            /// </summary>
            /// <typeparam name="T">The sampled scalar type.</typeparam>
            private static void ApplyIfConfigured<T>(
                BaseVariableState? variable,
                IaStacklightSample<T>? sample)
            {
                if (variable is not null && sample.HasValue)
                {
                    IaStacklight.Apply(variable, sample.Value);
                }
            }

            /// <summary>The caller-owned element.</summary>
            private readonly BaseObjectState m_state;

            /// <summary>The configured ordered position.</summary>
            private readonly uint m_numberInList;

            /// <summary>The owning model context.</summary>
            private readonly ISystemContext m_context;

            /// <summary>The optional direct on-state.</summary>
            private BaseVariableState? m_signalOn;

            /// <summary>The optional direct color.</summary>
            private BaseVariableState? m_signalColor;

            /// <summary>The optional direct signal mode.</summary>
            private BaseVariableState? m_signalMode;

            /// <summary>The optional detailed color.</summary>
            private BaseVariableState? m_rgbwValue;

            /// <summary>The selected acoustic signal.</summary>
            private BaseVariableState? m_operationMode;

            /// <summary>The optional intensity.</summary>
            private BaseVariableState? m_intensity;

            /// <summary>The configured intensity range.</summary>
            private Range? m_intensityRange;

            /// <summary>The fixed channel inventory.</summary>
            private BaseObjectState[] m_channels = [];

            /// <summary>The valid acoustic signal indexes.</summary>
            private uint[] m_acousticSignalNumbers = [];

            /// <summary>The fixed channel bindings.</summary>
            private ChannelBinding[] m_channelBindings = [];
        }

        /// <summary>
        /// Caches one independently observed, namespace-qualified color channel.
        /// </summary>
        private sealed class ChannelBinding
        {
            /// <summary>
            /// Captures mandatory controls and any optional engineering range.
            /// </summary>
            public ChannelBinding(ISystemContext context, BaseObjectState state)
            {
                m_name = state.BrowseName;
                m_signalOn = RequireVariable(context, state, "SignalOn");
                m_channelColor = RequireVariable(context, state, "ChannelColor");
                m_signalMode = RequireVariable(context, state, "SignalMode");
                m_intensity = FindVariable(context, state, "Intensity");
                if (m_intensity is not null)
                {
                    m_intensityRange = RequireRange(context, m_intensity);
                }
            }

            /// <summary>
            /// Validates channel identity, enum values and optional intensity.
            /// </summary>
            public void Validate(IaStacklightControlChannelSnapshot snapshot)
            {
                if (snapshot.Name != m_name)
                {
                    throw Configuration("The ControlChannel inventory changed after binding.");
                }

                ValidateSample(snapshot.SignalOn, $"{m_name}.SignalOn");
                ValidateSample(snapshot.ChannelColor, $"{m_name}.ChannelColor");
                ValidateEnumValue(snapshot.ChannelColor.Value, 0, 7, $"{m_name}.ChannelColor");
                ValidateSample(snapshot.SignalMode, $"{m_name}.SignalMode");
                ValidateEnumValue(snapshot.SignalMode.Value, 0, 3, $"{m_name}.SignalMode");
                RequireOptionalSample(m_intensity, snapshot.Intensity, $"{m_name}.Intensity");
                if (snapshot.Intensity.HasValue)
                {
                    ValidateRange(snapshot.Intensity.Value.Value, m_intensityRange!, $"{m_name}.Intensity");
                }
            }

            /// <summary>
            /// Publishes a previously validated channel observation.
            /// </summary>
            public void Apply(IaStacklightControlChannelSnapshot snapshot)
            {
                IaStacklight.Apply(m_signalOn, snapshot.SignalOn);
                IaStacklight.Apply(m_channelColor, snapshot.ChannelColor);
                IaStacklight.Apply(m_signalMode, snapshot.SignalMode);
                if (m_intensity is not null)
                {
                    IaStacklight.Apply(m_intensity, snapshot.Intensity!.Value);
                }
            }

            /// <summary>The full channel identity.</summary>
            private readonly QualifiedName m_name;

            /// <summary>The mandatory on-state.</summary>
            private readonly BaseVariableState m_signalOn;

            /// <summary>The mandatory channel color.</summary>
            private readonly BaseVariableState m_channelColor;

            /// <summary>The mandatory signal mode.</summary>
            private readonly BaseVariableState m_signalMode;

            /// <summary>The optional intensity.</summary>
            private readonly BaseVariableState? m_intensity;

            /// <summary>The optional intensity range.</summary>
            private readonly Range? m_intensityRange;
        }

        /// <summary>The owning model context.</summary>
        private readonly ISystemContext m_context;

        /// <summary>The caller-owned stacklight.</summary>
        private readonly BasicStacklightState m_state;

        /// <summary>The borrowed observation provider.</summary>
        private readonly IIaStacklightProvider m_provider;

        /// <summary>Serializes acquisition, publication, and notification dispatch.</summary>
        private readonly SemaphoreSlim m_refreshGate = new(1, 1);

        /// <summary>The mandatory mode variable.</summary>
        private readonly BaseVariableState m_stacklightMode;

        /// <summary>The optional DI health variable.</summary>
        private readonly BaseVariableState? m_deviceHealth;

        /// <summary>The optional level value.</summary>
        private readonly BaseVariableState? m_levelPercent;

        /// <summary>The optional fractional display mode.</summary>
        private readonly BaseVariableState? m_levelDisplayMode;

        /// <summary>The configured level engineering range.</summary>
        private readonly Range? m_levelPercentRange;

        /// <summary>The optional running-light representation.</summary>
        private readonly BaseObjectState? m_stackRunning;

        /// <summary>The fixed ordered element inventory.</summary>
        private readonly ElementBinding[] m_elements;
    }
}
