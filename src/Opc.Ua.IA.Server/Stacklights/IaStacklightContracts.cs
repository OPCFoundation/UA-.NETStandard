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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di;
using Opc.Ua.IA;

namespace Opc.Ua.IA.Server
{

    /// <summary>
    /// A stacklight observation and the acquisition metadata reported by its source.
    /// </summary>
    /// <typeparam name="T">The type of the sampled value.</typeparam>
    /// <param name="Value">The value reported by the source.</param>
    /// <param name="StatusCode">The source-reported OPC UA quality.</param>
    /// <param name="SourceTimestamp">The source-reported acquisition time.</param>
    public readonly record struct IaStacklightSample<T>(
        T Value,
        StatusCode StatusCode,
        DateTimeUtc SourceTimestamp);

    /// <summary>
    /// Supplies read-only stacklight observations for an <see cref="IaStacklight"/> binding.
    /// </summary>
    public interface IIaStacklightProvider
    {
        /// <summary>
        /// Reads one coherent observation of the configured stacklight.
        /// </summary>
        /// <param name="cancellationToken">The token used to cancel acquisition.</param>
        /// <returns>The observation, including source quality and timestamps for every sample.</returns>
        ValueTask<IaStacklightSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One coherent stacklight observation. The contained inventory must match the already-created
    /// stacklight state.
    /// </summary>
    /// <param name="StacklightMode">The source-reported operation mode.</param>
    /// <param name="Elements">The stack elements, ordered from the base upwards.</param>
    /// <param name="LevelPercent">The level value, required when the stacklight is in level-meter mode.</param>
    /// <param name="LevelDisplayMode">
    /// The level display mode, required when the stacklight is in level-meter mode.
    /// </param>
    /// <param name="DeviceHealth">The optional device-health observation, when the model exposes it.</param>
    public sealed record IaStacklightSnapshot(
        IaStacklightSample<StacklightOperationMode> StacklightMode,
        ArrayOf<IaStacklightElementSnapshot> Elements,
        IaStacklightSample<float>? LevelPercent,
        IaStacklightSample<LevelDisplayMode>? LevelDisplayMode,
        IaStacklightSample<DeviceHealthEnumeration>? DeviceHealth);

    /// <summary>
    /// Base type for a stacklight element observation.
    /// </summary>
    public abstract record IaStacklightElementSnapshot;

    /// <summary>
    /// A light element observed either through its direct values or through separate control channels.
    /// </summary>
    /// <param name="SignalOn">The optional direct signal state.</param>
    /// <param name="SignalColor">The optional direct signal color.</param>
    /// <param name="SignalMode">The optional direct light mode.</param>
    /// <param name="SignalRGBWValue">The optional detailed RGBW color value.</param>
    /// <param name="Intensity">The optional light intensity.</param>
    /// <param name="Channels">The independently controlled color channels, or an empty array for direct values.</param>
    public sealed record IaStacklightLightSnapshot(
        IaStacklightSample<bool>? SignalOn,
        IaStacklightSample<SignalColor>? SignalColor,
        IaStacklightSample<SignalModeLight>? SignalMode,
        IaStacklightSample<RGBWDataType>? SignalRGBWValue,
        IaStacklightSample<float>? Intensity,
        ArrayOf<IaStacklightControlChannelSnapshot> Channels) : IaStacklightElementSnapshot;

    /// <summary>
    /// An independently controlled light color channel.
    /// </summary>
    /// <param name="Name">The channel object's qualified browse name.</param>
    /// <param name="SignalOn">Whether this channel's color is switched on.</param>
    /// <param name="ChannelColor">The color assigned to this channel.</param>
    /// <param name="SignalMode">The light mode for this channel.</param>
    /// <param name="Intensity">The optional channel intensity.</param>
    public sealed record IaStacklightControlChannelSnapshot(
        QualifiedName Name,
        IaStacklightSample<bool> SignalOn,
        IaStacklightSample<SignalColor> ChannelColor,
        IaStacklightSample<SignalModeLight> SignalMode,
        IaStacklightSample<float>? Intensity);

    /// <summary>
    /// An acoustic element observation.
    /// </summary>
    /// <param name="SignalOn">The optional acoustic signal state.</param>
    /// <param name="OperationMode">The selected acoustic signal number.</param>
    /// <param name="Intensity">The optional sound-pressure intensity.</param>
    public sealed record IaStacklightAcousticSnapshot(
        IaStacklightSample<bool>? SignalOn,
        IaStacklightSample<uint> OperationMode,
        IaStacklightSample<float>? Intensity) : IaStacklightElementSnapshot
    {
        /// <summary>
        /// Gets optional audio samples for configured signals, in ascending NumberInList order.
        /// </summary>
        public ArrayOf<IaStacklightAudioSample> AudioSamples { get; init; }
    }

    /// <summary>
    /// One source-owned audio sample for a configured acoustic signal.
    /// </summary>
    /// <param name="SignalName">The acoustic signal's qualified browse name.</param>
    /// <param name="Sample">The audio bytes and acquisition metadata.</param>
    public sealed record IaStacklightAudioSample(
        QualifiedName SignalName,
        IaStacklightSample<ByteString> Sample);
}
