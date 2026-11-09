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

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolRuntime<TState>
    {
        /// <summary>
        /// Materializes the selected monitoring inventory before root registration.
        /// </summary>
        private void CreateMonitoring(MachineToolSnapshot snapshot)
        {
            foreach (MachineToolChannel channel in PrepareChannels(snapshot))
            {
                var name = new QualifiedName(channel.Name, m_context.InstanceNamespaceIndex);
                ChannelMonitoringState state = channel.CombinedChannels.Count == 0
                    ? m_context.Context.CreateInstanceOfChannelMonitoringType(State.Monitoring!, name)
                    : m_context.Context.CreateInstanceOfCombinedChannelMonitoringType(State.Monitoring!, name);
                if (channel.RapidOverride.HasValue)
                {
                    state.AddRapidOverride(m_context.Context);
                }
                if (channel.Modifiers.HasValue)
                {
                    state.AddChannelModifiers(m_context.Context);
                    state.ChannelModifiers!.AddBlockSkip(m_context.Context);
                    state.ChannelModifiers.AddTestMode(m_context.Context);
                }
                ConfigureOverride(state.FeedOverride!, channel.MaximumOverride);
                if (state.RapidOverride != null)
                {
                    ConfigureOverride(state.RapidOverride, channel.MaximumOverride);
                }
                State.Monitoring!.AddChild(state);
                m_channels.Add(channel.Name, state);
            }
        }

        /// <summary>
        /// Validates and aggregates the entire channel observation before any scalar is committed.
        /// </summary>
        private ArrayOf<MachineToolChannel> PrepareChannels(MachineToolSnapshot snapshot)
        {
            if ((Features & MachineToolFeatures.Channels) == 0)
            {
                return [];
            }
            if (snapshot.Channels.Count > 256)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                    "A MachineTool channel inventory is limited to 256 entries.");
            }
            var byName = new Dictionary<string, MachineToolChannel>(StringComparer.Ordinal);
            foreach (MachineToolChannel channel in snapshot.Channels)
            {
                if (channel == null || string.IsNullOrWhiteSpace(channel.Name) ||
                    !byName.TryAdd(channel.Name, channel) ||
                    (int)channel.State is < 0 or > 2 || (int)channel.Mode is < 0 or > 7 ||
                    !IsFinite(channel.MaximumOverride) || channel.MaximumOverride <= 0 ||
                    !ValidOverride(channel.FeedOverride, channel.MaximumOverride) ||
                    (channel.RapidOverride is double rapid && !ValidOverride(rapid, channel.MaximumOverride)))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument, "Invalid channel observation.");
                }
                ValidateSample(channel.Sample);
            }
            var resolved = new List<MachineToolChannel>(byName.Count);
            foreach (MachineToolChannel channel in snapshot.Channels)
            {
                if (channel.CombinedChannels.Count == 0)
                {
                    resolved.Add(channel);
                    continue;
                }
                if (channel.CombinedChannels.Count > 256)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                        "A combined channel is limited to 256 source channels.");
                }
                ChannelState state = ChannelState.Active;
                ChannelMode mode = ChannelMode.Automatic;
                MachineToolChannelModifiers? modifiers = null;
                MachineToolSampleInfo sample = channel.Sample;
                var sources = new HashSet<string>(StringComparer.Ordinal);
                foreach (string name in channel.CombinedChannels)
                {
                    if (name == null || !sources.Add(name) || !byName.TryGetValue(name, out MachineToolChannel? source) ||
                        source.CombinedChannels.Count != 0)
                    {
                        throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                            "A combined channel must refer to distinct ordinary channels in the same machine.");
                    }
                    if (source.State == ChannelState.Reset)
                    {
                        state = ChannelState.Reset;
                    }
                    else if (source.State == ChannelState.Interrupted && state != ChannelState.Reset)
                    {
                        state = ChannelState.Interrupted;
                    }
                    if (mode == ChannelMode.Automatic && source.Mode != ChannelMode.Automatic)
                    {
                        mode = source.Mode;
                    }
                    if (source.Modifiers is MachineToolChannelModifiers flags)
                    {
                        MachineToolChannelModifiers previous = modifiers.GetValueOrDefault();
                        modifiers = new MachineToolChannelModifiers(
                            previous.DryRun || flags.DryRun,
                            previous.OptionalStop || flags.OptionalStop,
                            previous.SingleStep || flags.SingleStep,
                            previous.BlockSkip || flags.BlockSkip,
                            previous.TestMode || flags.TestMode);
                    }
                    sample = MergeSamples(sample, source.Sample);
                }
                resolved.Add(channel with { State = state, Mode = mode, Modifiers = modifiers, Sample = sample });
            }
            return resolved.ToArrayOf();
        }

        /// <summary>
        /// Checks the fixed monitoring topology before refreshing any published values.
        /// </summary>
        private void ValidateChannelTopology(ArrayOf<MachineToolChannel> channels)
        {
            if (channels.Count != m_channels.Count)
            {
                throw new ServiceResultException(StatusCodes.BadInvalidState,
                    "Monitoring topology is fixed for the lifetime of a machine instance.");
            }
            foreach (MachineToolChannel channel in channels)
            {
                if (!m_channels.TryGetValue(channel.Name, out ChannelMonitoringState? state) ||
                    channel.RapidOverride.HasValue != (state.RapidOverride != null) ||
                    channel.Modifiers.HasValue != (state.ChannelModifiers != null) ||
                    (channel.CombinedChannels.Count != 0) != (state is CombinedChannelMonitoringState))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidState,
                        "A refresh cannot change the selected channel topology.");
                }
            }
        }

        /// <summary>
        /// Publishes a previously validated channel set, including acquisition quality for every observation.
        /// </summary>
        private void ApplyChannels(ArrayOf<MachineToolChannel> channels)
        {
            foreach (MachineToolChannel channel in channels)
            {
                ChannelMonitoringState state = m_channels[channel.Name];
                MachineToolSampleInfo sample = channel.Sample;
                SetSample(state.Name!, Variant.From(channel.Name), sample);
                SetSample(state.ChannelState!, Variant.From(channel.State), sample);
                SetSample(state.ChannelMode!, Variant.From(channel.Mode), sample);
                SetSample(state.FeedOverride!, Variant.From(channel.FeedOverride), sample);
                ConfigureOverride(state.FeedOverride!, channel.MaximumOverride);
                if (channel.RapidOverride is double rapid)
                {
                    SetSample(state.RapidOverride!, Variant.From(rapid), sample);
                    ConfigureOverride(state.RapidOverride!, channel.MaximumOverride);
                }
                if (channel.Modifiers is MachineToolChannelModifiers modifiers)
                {
                    ChannelModifierState node = state.ChannelModifiers!;
                    SetSample(node.DryRun!, Variant.From(modifiers.DryRun), sample);
                    SetSample(node.OptionalStop!, Variant.From(modifiers.OptionalStop), sample);
                    SetSample(node.SingleStep!, Variant.From(modifiers.SingleStep), sample);
                    SetSample(node.BlockSkip!, Variant.From(modifiers.BlockSkip), sample);
                    SetSample(node.TestMode!, Variant.From(modifiers.TestMode), sample);
                }
            }
        }

        /// <summary>
        /// Configures the normative percent unit and controller range of an override signal.
        /// </summary>
        private void ConfigureOverride(BaseVariableState variable, double maximum)
        {
            var units = (BaseVariableState)variable.FindChild(
                m_context.Context, new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits))!;
            units.Value = new Variant(new ExtensionObject(new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = 20529,
                DisplayName = new LocalizedText("%"),
                Description = new LocalizedText("percent")
            }));
            var range = (BaseVariableState)variable.FindChild(
                m_context.Context, new QualifiedName(Opc.Ua.BrowseNames.EURange))!;
            range.Value = new Variant(new ExtensionObject(new Range { Low = 0, High = maximum }));
        }

        /// <summary>
        /// Checks a reported percentage against its real controller range.
        /// </summary>
        private static bool ValidOverride(double value, double maximum)
        {
            return IsFinite(value) && value >= 0 && value <= maximum;
        }

        /// <summary>
        /// Checks numeric finiteness on every supported target framework.
        /// </summary>
        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>
        /// Propagates the least usable quality and oldest acquisition time into a combined observation.
        /// </summary>
        private static MachineToolSampleInfo MergeSamples(MachineToolSampleInfo first, MachineToolSampleInfo second)
        {
            StatusCode quality = first.StatusCode;
            if (StatusCode.IsBad(second.StatusCode) ||
                (StatusCode.IsUncertain(second.StatusCode) && StatusCode.IsGood(quality)))
            {
                quality = second.StatusCode;
            }
            DateTimeUtc acquired = first.SourceTimestamp < second.SourceTimestamp
                ? first.SourceTimestamp : second.SourceTimestamp;
            return new MachineToolSampleInfo(acquired, quality);
        }

        /// <summary>The selected per-instance channel nodes, keyed by stable provider names.</summary>
        private readonly Dictionary<string, ChannelMonitoringState> m_channels = new(StringComparer.Ordinal);
    }
}
