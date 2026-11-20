// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.IA.Server
{
    /// <summary>
    /// Composes generated stacklight nodes before registering their application-owned subtree.
    /// </summary>
    public sealed class IaStacklightBuilder
    {
        /// <summary>
        /// Starts composition on an empty generated stacklight.
        /// </summary>
        public IaStacklightBuilder(ISystemContext context, BasicStacklightState state)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>
        /// Gets the existing generated stacklight, including optional device-health extensions.
        /// </summary>
        public BasicStacklightState State { get; }

        /// <summary>
        /// Adds a directly controlled light in creation order, with standard color, mode and on-state.
        /// </summary>
        public IaStacklightBuilder AddLight(QualifiedName name, Action<StackElementLightState>? configure = null)
        {
            StackElementLightState light = m_context.CreateInstanceOfStackElementLightType(State, name);
            light.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasOrderedComponent;
            light.NumberInList!.Value = m_nextElement++;
            light.AddSignalOn(m_context);
            light.AddSignalColor(m_context);
            light.AddSignalMode(m_context);
            configure?.Invoke(light);
            State.AddChild(light);
            return this;
        }

        /// <summary>
        /// Adds an acoustic element with one named signal; callbacks can add further signals and optional audio data.
        /// </summary>
        /// <param name="name">The acoustic element's browse name.</param>
        /// <param name="signalName">The first acoustic signal's browse name.</param>
        /// <param name="configure">Configures optional members on the acoustic element.</param>
        /// <param name="configureSignal">Configures optional members on the first acoustic signal.</param>
        public IaStacklightBuilder AddAcoustic(
            QualifiedName name,
            QualifiedName signalName,
            Action<StackElementAcousticState>? configure = null,
            Action<AcousticSignalState>? configureSignal = null)
        {
            StackElementAcousticState acoustic = m_context.CreateInstanceOfStackElementAcousticType(State, name);
            acoustic.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasOrderedComponent;
            acoustic.NumberInList!.Value = m_nextElement++;
            AcousticSignalState signal = m_context.CreateInstanceOfAcousticSignalType(
                acoustic.AcousticSignals!, signalName);
            signal.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasOrderedComponent;
            signal.NumberInList!.Value = 0u;
            configureSignal?.Invoke(signal);
            acoustic.AcousticSignals!.AddChild(signal);
            configure?.Invoke(acoustic);
            State.AddChild(acoustic);
            return this;
        }

        /// <summary>
        /// Enables the standard level-meter and running-light representations.
        /// </summary>
        public IaStacklightBuilder WithModes(float minimum = 0, float maximum = 100)
        {
            if (float.IsNaN(minimum) || float.IsNaN(maximum) || float.IsInfinity(minimum) ||
                float.IsInfinity(maximum) || minimum >= maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum));
            }
            State.AddStackLevel(m_context);
            State.StackLevel!.LevelPercent!.EURange!.Value = new Range { Low = minimum, High = maximum };
            State.AddStackRunning(m_context);
            return this;
        }

        /// <summary>
        /// Binds the final topology to its borrowed observation provider.
        /// </summary>
        public IaStacklight Build(IIaStacklightProvider provider)
        {
            return new IaStacklight(m_context, State, provider);
        }

        /// <summary>The owning model context.</summary>
        private readonly ISystemContext m_context;

        /// <summary>The next ordered element index.</summary>
        private uint m_nextElement;
    }
}
