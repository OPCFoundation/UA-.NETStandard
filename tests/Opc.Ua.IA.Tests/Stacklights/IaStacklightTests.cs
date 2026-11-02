/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di;
using Opc.Ua.IA.Server;

namespace Opc.Ua.IA.Tests.Stacklights
{
    /// <summary>
    /// Exercises fixed stacklight topology and source-driven modes without a physical driver.
    /// </summary>
    [TestFixture]
    public sealed class IaStacklightTests
    {
        /// <summary>
        /// Level-meter information is independent of per-element on-state, including exact range boundaries.
        /// </summary>
        [TestCase(StacklightOperationMode.Segmented, 0f)]
        [TestCase(StacklightOperationMode.Levelmeter, 0f)]
        [TestCase(StacklightOperationMode.Levelmeter, 50f)]
        [TestCase(StacklightOperationMode.Levelmeter, 100f)]
        [TestCase(StacklightOperationMode.Running_Light, 0f)]
        [TestCase(StacklightOperationMode.Other, 0f)]
        public async Task ModesPreserveSourceMetadata(StacklightOperationMode mode, float level)
        {
            var fixture = new IaTestContext();
            BasicStacklightState state = fixture.Context.CreateInstanceOfBasicStacklightType(
                browseName: fixture.Name("Stacklight"));
            StackElementLightState? light = null;
            var builder = new IaStacklightBuilder(fixture.Context, state)
                .WithModes().AddLight(fixture.Name("Lamp"), item => light = item);
            IaStacklightSnapshot snapshot = Snapshot(mode, false, level);
            using IaStacklight binding = builder.Build(Provider(snapshot).Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(state.StacklightMode!.Value, Is.EqualTo(mode));
            Assert.That(state.StacklightMode.StatusCode, Is.EqualTo((StatusCode)StatusCodes.UncertainLastUsableValue));
            Assert.That(state.StacklightMode.Timestamp, Is.EqualTo(IaTestContext.Timestamp));
            Assert.That(light!.SignalOn!.Value, Is.False);
            Assert.That(light.SignalColor!.Value, Is.EqualTo(SignalColor.Red));
            Assert.That(light.SignalMode!.Value, Is.EqualTo(SignalModeLight.Continuous));
            if (mode == StacklightOperationMode.Levelmeter)
            {
                Assert.That(state.StackLevel!.LevelPercent!.Value, Is.EqualTo(level));
                Assert.That(state.StackLevel.DisplayMode!.Value, Is.EqualTo(LevelDisplayMode.Dimmed));
            }
        }

        /// <summary>
        /// Neither a late invalid level nor a missing acquisition time changes a prior light or mode.
        /// </summary>
        [TestCase(-0.1f)]
        [TestCase(100.1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public async Task InvalidSnapshotDoesNotPartiallyPublish(float level)
        {
            var fixture = new IaTestContext();
            BasicStacklightState state = fixture.Context.CreateInstanceOfBasicStacklightType(
                browseName: fixture.Name("Stacklight"));
            StackElementLightState? light = null;
            IaStacklightSnapshot snapshot = Snapshot(StacklightOperationMode.Segmented, false);
            Mock<IIaStacklightProvider> provider = Provider(snapshot);
            provider.Setup(source => source.ReadSnapshotAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IaStacklightSnapshot>(snapshot));
            using IaStacklight binding = new IaStacklightBuilder(fixture.Context, state).WithModes()
                .AddLight(fixture.Name("Lamp"), item => light = item).Build(provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            snapshot = Snapshot(StacklightOperationMode.Levelmeter, true, level);
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(light!.SignalOn!.Value, Is.False);
            Assert.That(state.StacklightMode!.Value, Is.EqualTo(StacklightOperationMode.Segmented));
        }

        /// <summary>
        /// A direct observation cannot silently discard channel data absent from the bound topology.
        /// </summary>
        [Test]
        public async Task DirectObservationRejectsUnconfiguredChannelsBeforePublication()
        {
            var fixture = new IaTestContext();
            BasicStacklightState state = fixture.Context.CreateInstanceOfBasicStacklightType(
                browseName: fixture.Name("Stacklight"));
            StackElementLightState? light = null;
            IaStacklightSnapshot snapshot = Snapshot(StacklightOperationMode.Segmented, false);
            Mock<IIaStacklightProvider> provider = Provider(snapshot);
            provider.Setup(source => source.ReadSnapshotAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IaStacklightSnapshot>(snapshot));
            using IaStacklight binding = new IaStacklightBuilder(fixture.Context, state)
                .AddLight(fixture.Name("Lamp"), item => light = item).Build(provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            snapshot = snapshot with
            {
                Elements =
                [
                    new IaStacklightLightSnapshot(Sample(true), Sample(SignalColor.Green),
                        Sample(SignalModeLight.Continuous), null, null,
                        [new IaStacklightControlChannelSnapshot(fixture.Name("UnexpectedChannel"),
                            Sample(true), Sample(SignalColor.Green), Sample(SignalModeLight.Continuous), null)])
                ]
            };
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await binding.RefreshAsync().ConfigureAwait(false));
            Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
            Assert.That(light!.SignalOn!.Value, Is.False);
            Assert.That(light.SignalColor!.Value, Is.EqualTo(SignalColor.Red));
        }

        /// <summary>
        /// Configuration rejects direct/channel mixing and RGBW without the standard color.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void InvalidLightTopologiesAreRejected(bool channel)
        {
            var fixture = new IaTestContext();
            BasicStacklightState state = fixture.Context.CreateInstanceOfBasicStacklightType(
                browseName: fixture.Name("Stacklight"));
            var builder = new IaStacklightBuilder(fixture.Context, state).AddLight(fixture.Name("Lamp"), light =>
            {
                if (channel)
                {
                    ControlChannelState control = fixture.Context.CreateInstanceOfControlChannelType(
                        light, fixture.Name("RedChannel"));
                    light.AddChild(control);
                }
                else
                {
                    light.SignalColor = null;
                    light.AddSignalRGBWValue(fixture.Context);
                }
            });
            Assert.That(() => builder.Build(Mock.Of<IIaStacklightProvider>()),
                Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// Acoustic selection refers to the signal's NumberInList, not its position in a browse result.
        /// </summary>
        [Test]
        public async Task OrderedElementsAndAcousticSelectionAreValidated()
        {
            var fixture = new IaTestContext();
            BasicStacklightState state = fixture.Context.CreateInstanceOfBasicStacklightType(
                browseName: fixture.Name("Stacklight"));
            StackElementAcousticState? acoustic = null;
            var builder = new IaStacklightBuilder(fixture.Context, state)
                .AddAcoustic(fixture.Name("Sound"), fixture.Name("Warning"), item => acoustic = item);
            IaStacklightSnapshot snapshot = new(Sample(StacklightOperationMode.Segmented),
                [new IaStacklightAcousticSnapshot(null, Sample(0u), null)], null, null, null);
            Mock<IIaStacklightProvider> provider = Provider(snapshot);
            provider.Setup(source => source.ReadSnapshotAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<IaStacklightSnapshot>(snapshot));
            using IaStacklight binding = builder.Build(provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(acoustic!.OperationMode!.Value, Is.EqualTo(0u));
            Assert.That(acoustic.NumberInList!.WrappedValue.TryGetValue(out uint number), Is.True);
            Assert.That(number, Is.Zero);
            snapshot = snapshot with { Elements = [new IaStacklightAcousticSnapshot(null, Sample(4u), null)] };
            await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>());
            Assert.That(acoustic.OperationMode.Value, Is.Zero);
        }

        /// <summary>
        /// Duplicate ordered indexes cannot silently pick one element or depend on creation order.
        /// </summary>
        [Test]
        public void DuplicateElementOrderIsRejected()
        {
            var fixture = new IaTestContext();
            BasicStacklightState state = fixture.Context.CreateInstanceOfBasicStacklightType(
                browseName: fixture.Name("Stacklight"));
            var builder = new IaStacklightBuilder(fixture.Context, state)
                .AddLight(fixture.Name("First"))
                .AddLight(fixture.Name("Second"), light => light.NumberInList!.Value = 0u);
            Assert.That(() => builder.Build(Mock.Of<IIaStacklightProvider>()),
                Throws.TypeOf<ServiceResultException>());
        }

        /// <summary>
        /// Mounting-base elements occupy a contiguous prefix rather than appearing above another element.
        /// </summary>
        [TestCase(false, false, true)]
        [TestCase(true, false, true)]
        [TestCase(true, true, true)]
        [TestCase(false, true, false)]
        public void MountingBaseElementsPrecedeOtherElements(bool first, bool second, bool accepted)
        {
            var fixture = new IaTestContext();
            BasicStacklightState state = fixture.Context.CreateInstanceOfBasicStacklightType(
                browseName: fixture.Name("Stacklight"));
            StackElementLightState? lower = null;
            StackElementLightState? upper = null;
            var builder = new IaStacklightBuilder(fixture.Context, state)
                .AddLight(fixture.Name("Lower"), light =>
                {
                    lower = light;
                    light.AddIsPartOfBase(fixture.Context);
                    light.IsPartOfBase!.Value = first;
                })
                .AddLight(fixture.Name("Upper"), light =>
                {
                    upper = light;
                    light.AddIsPartOfBase(fixture.Context);
                    light.IsPartOfBase!.Value = second;
                });
            if (!accepted)
            {
                Assert.That(() => builder.Build(Mock.Of<IIaStacklightProvider>()),
                    Throws.TypeOf<ServiceResultException>());
                return;
            }
            using IaStacklight binding = builder.Build(Mock.Of<IIaStacklightProvider>());
            Assert.That(lower!.NumberInList!.WrappedValue, Is.EqualTo(Variant.From(0u)));
            Assert.That(upper!.NumberInList!.WrappedValue, Is.EqualTo(Variant.From(1u)));
            Assert.That(lower.IsPartOfBase!.Value, Is.EqualTo(first));
            Assert.That(upper.IsPartOfBase!.Value, Is.EqualTo(second));
        }

        /// <summary>
        /// Omitted mounting-base information is accepted, but configured values must be Boolean.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void MountingBaseMetadataIsOptionalButMustBeBoolean(bool missing)
        {
            var fixture = new IaTestContext();
            BasicStacklightState state = fixture.Context.CreateInstanceOfBasicStacklightType(
                browseName: fixture.Name("Stacklight"));
            StackElementLightState? element = null;
            var builder = new IaStacklightBuilder(fixture.Context, state)
                .AddLight(fixture.Name("Lamp"), light =>
                {
                    element = light;
                    if (missing)
                    {
                        light.IsPartOfBase = null;
                    }
                    else
                    {
                        light.AddIsPartOfBase(fixture.Context);
                        light.IsPartOfBase!.WrappedValue = Variant.From("false");
                    }
                });
            byte access = element!.SignalOn!.AccessLevel;
            if (missing)
            {
                using IaStacklight binding = builder.Build(Mock.Of<IIaStacklightProvider>());
                Assert.That(binding.State, Is.SameAs(state));
                Assert.That(element.IsPartOfBase, Is.Null);
                return;
            }
            ServiceResultException? error = Assert.Throws<ServiceResultException>(() =>
                builder.Build(Mock.Of<IIaStacklightProvider>()));
            Assert.That(error!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
            Assert.That(element.SignalOn.AccessLevel, Is.EqualTo(access));
        }

        /// <summary>
        /// Independently controlled channels retain their namespace-qualified identity and source metadata.
        /// </summary>
        [Test]
        public async Task ChannelObservationUsesQualifiedIdentity()
        {
            var fixture = new IaTestContext();
            BasicStacklightState state = fixture.Context.CreateInstanceOfBasicStacklightType(
                browseName: fixture.Name("Stacklight"));
            ControlChannelState? channel = null;
            var builder = new IaStacklightBuilder(fixture.Context, state)
                .AddLight(fixture.Name("Lamp"), light =>
                {
                    light.SignalOn = null;
                    light.SignalColor = null;
                    light.SignalMode = null;
                    channel = fixture.Context.CreateInstanceOfControlChannelType(light, fixture.Name("Channel"));
                    light.AddChild(channel);
                });
            IaStacklightSnapshot snapshot = new(Sample(StacklightOperationMode.Segmented),
            [
                new IaStacklightLightSnapshot(null, null, null, null, null,
                    [new IaStacklightControlChannelSnapshot(fixture.Name("Channel"),
                        Sample(true), Sample(SignalColor.Green), Sample(SignalModeLight.Blinking), null)])
            ], null, null, null);
            using IaStacklight binding = builder.Build(Provider(snapshot).Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(channel!.ChannelColor!.Value, Is.EqualTo(SignalColor.Green));
            Assert.That(channel.SignalOn!.Value, Is.True);
            Assert.That(channel.SignalMode!.Value, Is.EqualTo(SignalModeLight.Blinking));
            Assert.That(channel.SignalOn.Timestamp, Is.EqualTo(IaTestContext.Timestamp));
        }

        /// <summary>
        /// Source cancellation is honored before any publication.
        /// </summary>
        [Test]
        public async Task CancelledRefreshDoesNotAcquireProvider()
        {
            var fixture = new IaTestContext();
            BasicStacklightState state = fixture.Context.CreateInstanceOfBasicStacklightType(
                browseName: fixture.Name("Stacklight"));
            var provider = new Mock<IIaStacklightProvider>(MockBehavior.Strict);
            using IaStacklight binding = new IaStacklightBuilder(fixture.Context, state)
                .AddLight(fixture.Name("Lamp")).Build(provider.Object);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThatAsync(async () => await binding.RefreshAsync(cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            provider.Verify(source => source.ReadSnapshotAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Detailed colors retain optional-field presence and cannot be changed through the provider's instance.
        /// </summary>
        [Test]
        public async Task RgbwObservationOwnsItsPublishedStructure()
        {
            var fixture = new IaTestContext();
            BasicStacklightState state = fixture.Context.CreateInstanceOfBasicStacklightType(
                browseName: fixture.Name("Stacklight"));
            StackElementLightState? light = null;
            var color = new RGBWDataType
            {
                Red = 200,
                Green = 10,
                Blue = 20,
                White = 30,
                EncodingMask = (uint)RGBWDataTypeFields.White
            };
            IaStacklightSnapshot snapshot = Snapshot(StacklightOperationMode.Segmented, true) with
            {
                Elements = [new IaStacklightLightSnapshot(Sample(true), Sample(SignalColor.Red),
                    Sample(SignalModeLight.Flashing), Sample(color), null, [])]
            };
            using IaStacklight binding = new IaStacklightBuilder(fixture.Context, state)
                .AddLight(fixture.Name("Lamp"), item =>
                {
                    light = item;
                    item.AddSignalRGBWValue(fixture.Context);
                }).Build(Provider(snapshot).Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            color.Red = 1;
            Assert.That(light!.SignalRGBWValue!.WrappedValue.TryGetStructure<RGBWDataType>(
                out RGBWDataType? published), Is.True);
            Assert.That(published, Is.Not.SameAs(color));
            Assert.That(published!.Red, Is.EqualTo(200));
            Assert.That(published.Green, Is.EqualTo(10));
            Assert.That(published.Blue, Is.EqualTo(20));
            Assert.That(published.White, Is.EqualTo(30));
            Assert.That(published.EncodingMask, Is.EqualTo((uint)RGBWDataTypeFields.White));
        }

        /// <summary>
        /// A provider that observes cancellation during acquisition cannot publish its returned observation.
        /// </summary>
        [Test]
        public async Task CancellationAfterAcquisitionPreservesPublishedState()
        {
            var fixture = new IaTestContext();
            BasicStacklightState state = fixture.Context.CreateInstanceOfBasicStacklightType(
                browseName: fixture.Name("Stacklight"));
            using var cancellation = new CancellationTokenSource();
            var provider = Provider(Snapshot(StacklightOperationMode.Segmented, false));
            using IaStacklight binding = new IaStacklightBuilder(fixture.Context, state).WithModes()
                .AddLight(fixture.Name("Lamp")).Build(provider.Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            provider.Setup(source => source.ReadSnapshotAsync(It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    cancellation.Cancel();
                    return new ValueTask<IaStacklightSnapshot>(
                        Snapshot(StacklightOperationMode.Levelmeter, true));
                });
            await Assert.ThatAsync(async () => await binding.RefreshAsync(cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(state.StacklightMode!.Value, Is.EqualTo(StacklightOperationMode.Segmented));
        }

        /// <summary>
        /// Light intensity permits the inclusive lower bound, while acoustic intensity must be positive.
        /// </summary>
        [TestCase(false, 0f, true)]
        [TestCase(false, 100f, true)]
        [TestCase(false, -0.1f, false)]
        [TestCase(false, 100.1f, false)]
        [TestCase(false, float.NaN, false)]
        [TestCase(true, 0f, false)]
        [TestCase(true, 0.1f, true)]
        [TestCase(true, 100f, true)]
        [TestCase(true, 100.1f, false)]
        public async Task IntensityUsesElementSpecificInclusiveBounds(bool acoustic, float intensity, bool valid)
        {
            var fixture = new IaTestContext();
            BasicStacklightState state = fixture.Context.CreateInstanceOfBasicStacklightType(
                browseName: fixture.Name("Stacklight"));
            BaseVariableState? variable = null;
            var builder = new IaStacklightBuilder(fixture.Context, state);
            IaStacklightElementSnapshot observation;
            if (acoustic)
            {
                builder.AddAcoustic(fixture.Name("Sound"), fixture.Name("Warning"), element =>
                {
                    element.AddIntensity(fixture.Context);
                    element.Intensity!.EURange!.Value = new Range { Low = 0, High = 100 };
                    variable = element.Intensity;
                });
                observation = new IaStacklightAcousticSnapshot(null, Sample(0u), Sample(intensity));
            }
            else
            {
                builder.AddLight(fixture.Name("Lamp"), element =>
                {
                    element.AddIntensity(fixture.Context);
                    element.Intensity!.EURange!.Value = new Range { Low = 0, High = 100 };
                    variable = element.Intensity;
                });
                observation = new IaStacklightLightSnapshot(Sample(true), Sample(SignalColor.Red),
                    Sample(SignalModeLight.Continuous), null, Sample(intensity), []);
            }
            var snapshot = new IaStacklightSnapshot(Sample(StacklightOperationMode.Segmented),
                [observation], null, null, null);
            using IaStacklight binding = builder.Build(Provider(snapshot).Object);
            Variant previous = variable!.WrappedValue;
            if (!valid)
            {
                await Assert.ThatAsync(async () => await binding.RefreshAsync().ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>());
                Assert.That(variable.WrappedValue, Is.EqualTo(previous));
                return;
            }
            await binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(variable.WrappedValue.TryGetValue(out float actual), Is.True);
            Assert.That(actual, Is.EqualTo(intensity));
            Assert.That(variable.Timestamp, Is.EqualTo(IaTestContext.Timestamp));
            Assert.That(variable.StatusCode, Is.EqualTo((StatusCode)StatusCodes.UncertainLastUsableValue));
        }

        /// <summary>
        /// DI health remains an independently timestamped observation of the device.
        /// </summary>
        [TestCase(0)]
        [TestCase(4)]
        public async Task DeviceHealthPreservesSourceMetadata(int health)
        {
            var fixture = new IaTestContext();
            StacklightState state = fixture.Context.CreateInstanceOfStacklightType(
                browseName: fixture.Name("Stacklight"));
            state.AddDeviceHealth(fixture.Context);
            IaStacklightSnapshot snapshot = Snapshot(StacklightOperationMode.Segmented, true) with
            {
                DeviceHealth = Sample((DeviceHealthEnumeration)health)
            };
            using IaStacklight binding = new IaStacklightBuilder(fixture.Context, state)
                .AddLight(fixture.Name("Lamp")).Build(Provider(snapshot).Object);
            await binding.RefreshAsync().ConfigureAwait(false);
            Assert.That(state.DeviceHealth!.Value, Is.EqualTo((DeviceHealthEnumeration)health));
            Assert.That(state.DeviceHealth.Timestamp, Is.EqualTo(IaTestContext.Timestamp));
            Assert.That(state.DeviceHealth.StatusCode, Is.EqualTo((StatusCode)StatusCodes.UncertainLastUsableValue));
        }

        /// <summary>
        /// Creates one direct light observation.
        /// </summary>
        private static IaStacklightSnapshot Snapshot(StacklightOperationMode mode, bool on, float level = 50)
        {
            return new IaStacklightSnapshot(Sample(mode),
                [new IaStacklightLightSnapshot(Sample(on), Sample(SignalColor.Red),
                    Sample(SignalModeLight.Continuous), null, null, [])],
                mode == StacklightOperationMode.Levelmeter ? Sample(level) : null,
                mode == StacklightOperationMode.Levelmeter ? Sample(LevelDisplayMode.Dimmed) : null, null);
        }

        /// <summary>
        /// Creates a source-quality-preserving typed sample.
        /// </summary>
        private static IaStacklightSample<T> Sample<T>(T value)
        {
            return new IaStacklightSample<T>(value, StatusCodes.UncertainLastUsableValue, IaTestContext.Timestamp);
        }

        /// <summary>
        /// Creates the borrowed observation provider.
        /// </summary>
        private static Mock<IIaStacklightProvider> Provider(IaStacklightSnapshot snapshot)
        {
            var provider = new Mock<IIaStacklightProvider>();
            provider.Setup(source => source.ReadSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
            return provider;
        }
    }
}
