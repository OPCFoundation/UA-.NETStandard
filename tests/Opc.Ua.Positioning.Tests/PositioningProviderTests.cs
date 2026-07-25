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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gpos;
using Opc.Ua.Positioning.Server;
using Opc.Ua.Rsl;

namespace Opc.Ua.Positioning.Tests
{
    [TestFixture]
    [Category("Positioning")]
    [NonParallelizable]
    public sealed class PositioningProviderTests
    {
        private PositioningServerFixture? m_fixture;
        private PositioningAddressSpaceBuilder m_builder = null!;
        private BaseObjectState m_owner = null!;
        private ushort m_namespaceIndex;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new PositioningServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_builder = m_fixture.Manager.CreatePositioningBuilder();
            m_namespaceIndex = (ushort)m_fixture.Manager.Server.NamespaceUris
                .GetIndex(Rsl.Namespaces.RSL);
            m_owner = new BaseObjectState(null)
            {
                NodeId = new NodeId("ProviderOwner", m_namespaceIndex),
                BrowseName = new QualifiedName("ProviderOwner", m_namespaceIndex),
                DisplayName = new LocalizedText("ProviderOwner"),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };
            await m_fixture.Manager.AddPredefinedNodeAsync(m_owner)
                .ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task GlobalProviderAppliesUpdatesAndCancelsAsync()
        {
            GlobalLocationState state = await CreateGlobalLocationAsync()
                .ConfigureAwait(false);
            using var provider = new ControlledGlobalProvider(
                CreateGlobalSample("robot", 8.0, includeOptionalFields: true));
            PositioningProviderSubscription subscription =
                await m_builder.BindGlobalLocationAsync(
                    state,
                    provider,
                    "robot").ConfigureAwait(false);

            provider.Publish(
                CreateGlobalSample("robot", 9.0, includeOptionalFields: false));
            await WaitUntilAsync(
                () => state.Value.Position.Longitude == 9.0)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(state.Value.Position.Longitude, Is.EqualTo(9.0));
                Assert.That(state.Position!.Longitude!.Value, Is.EqualTo(9.0));
                Assert.That(
                    state.Position.Elevation!.StatusCode,
                    Is.EqualTo(StatusCodes.BadNoData));
                Assert.That(
                    state.Orientation!.StatusCode,
                    Is.EqualTo(StatusCodes.BadNoData));
                Assert.That(
                    state.Orientation.A!.StatusCode,
                    Is.EqualTo(StatusCodes.BadNoData));
                Assert.That(
                    state.Orientation.B!.StatusCode,
                    Is.EqualTo(StatusCodes.BadNoData));
                Assert.That(
                    state.Orientation.C!.StatusCode,
                    Is.EqualTo(StatusCodes.BadNoData));
                Assert.That(state.Orientation.C!.Value, Is.EqualTo(90.0));
            });

            await subscription.DisposeAsync().ConfigureAwait(false);
            Task completed = await Task.WhenAny(
                provider.CancellationObserved,
                Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
            Assert.That(completed, Is.SameAs(provider.CancellationObserved));
        }

        [Test]
        public async Task GlobalProviderFailurePreservesLastValueAndMarksStatusBadAsync()
        {
            GlobalLocationState state = await CreateGlobalLocationAsync()
                .ConfigureAwait(false);
            var provider = new FailingGlobalProvider(
                CreateGlobalSample("robot", 8.0, includeOptionalFields: true),
                CreateGlobalSample("robot", 9.0, includeOptionalFields: true));
            PositioningProviderSubscription subscription =
                await m_builder.BindGlobalLocationAsync(
                    state,
                    provider,
                    "robot").ConfigureAwait(false);

            InvalidOperationException? failure = null;
            try
            {
                await subscription.Completion.ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                failure = ex;
            }
            subscription.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Not.Null);
                Assert.That(state.Value.Position.Longitude, Is.EqualTo(9.0));
                Assert.That(
                    state.StatusCode,
                    Is.EqualTo(StatusCodes.BadCommunicationError));
                Assert.That(
                    state.Position!.Longitude!.StatusCode,
                    Is.EqualTo(StatusCodes.BadCommunicationError));
                Assert.That(
                    state.Orientation!.C!.StatusCode,
                    Is.EqualTo(StatusCodes.BadCommunicationError));
            });
        }

        [Test]
        public async Task ProviderSourceMismatchIsRejectedAndSurfacedAsync()
        {
            GlobalLocationState state = await CreateGlobalLocationAsync()
                .ConfigureAwait(false);
            using var provider = new ControlledGlobalProvider(
                CreateGlobalSample("other", 8.0, includeOptionalFields: true));

            ServiceResultException? failure = null;
            try
            {
                _ = await m_builder.BindGlobalLocationAsync(
                    state,
                    provider,
                    "robot").ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                failure = ex;
            }

            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Not.Null);
                Assert.That(
                    failure!.StatusCode,
                    Is.EqualTo(StatusCodes.BadInvalidArgument));
                Assert.That(
                    state.StatusCode,
                    Is.EqualTo(StatusCodes.BadCommunicationError));
            });
        }

        [Test]
        public async Task RelativeProviderFailurePreservesFrameAndMarksStatusBadAsync()
        {
            EUInformation metres = CreateUnit("m", "metre");
            EUInformation degrees = CreateUnit("deg", "degree");
            CartesianFrameAngleOrientationState frame =
                m_builder.CreateCartesianFrame(
                    m_owner,
                    new QualifiedName("TrackedFrame", m_namespaceIndex),
                    m_owner.NodeId,
                    CreateFrame(1.0),
                    metres,
                    degrees);
            m_owner.AddChild(frame);
            await m_builder.RegisterAsync(frame).ConfigureAwait(false);

            var provider = new FailingRelativeProvider(
                new RelativeSpatialLocationSample(
                    "robot",
                    CreateFrame(2.0),
                    StatusCodes.Good,
                    DateTimeUtc.Now),
                new RelativeSpatialLocationSample(
                    "robot",
                    CreateFrame(3.0),
                    StatusCodes.Good,
                    DateTimeUtc.Now));
            PositioningProviderSubscription subscription =
                await m_builder.BindRelativeSpatialLocationAsync(
                    frame,
                    provider,
                    "robot").ConfigureAwait(false);

            InvalidOperationException? failure = null;
            try
            {
                await subscription.Completion.ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                failure = ex;
            }
            subscription.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(failure, Is.Not.Null);
                Assert.That(frame.Value.CartesianCoordinates.X, Is.EqualTo(3.0));
                Assert.That(
                    frame.StatusCode,
                    Is.EqualTo(StatusCodes.BadCommunicationError));
                Assert.That(
                    frame.Position!.StatusCode,
                    Is.EqualTo(StatusCodes.BadCommunicationError));
                Assert.That(
                    frame.Position.X!.StatusCode,
                    Is.EqualTo(StatusCodes.BadCommunicationError));
                Assert.That(
                    frame.Orientation!.C!.StatusCode,
                    Is.EqualTo(StatusCodes.BadCommunicationError));
            });
        }

        private async ValueTask<GlobalLocationState> CreateGlobalLocationAsync()
        {
            GlobalLocationState state = m_builder.AttachGlobalLocation(
                m_owner,
                new QualifiedName("GlobalLocation", m_namespaceIndex),
                m_owner.NodeId,
                4326);
            await m_builder.RegisterAsync(state).ConfigureAwait(false);
            return state;
        }

        private static GlobalPositionSample CreateGlobalSample(
            string sourceId,
            double longitude,
            bool includeOptionalFields)
        {
            var position = new GlobalPositionDataType
            {
                EncodingMask = includeOptionalFields
                    ? (uint)S3DGeographicCoordinateDataTypeFields.Elevation |
                        (uint)GlobalPositionDataTypeFields.Accuracy |
                        (uint)GlobalPositionDataTypeFields.Floor
                    : 0,
                Longitude = longitude,
                Latitude = 47.0,
                Elevation = 500.0,
                Accuracy = 0.1,
                Floor = 2.0f
            };
            return new GlobalPositionSample(
                sourceId,
                new GlobalLocationDataType
                {
                    EncodingMask = includeOptionalFields
                        ? (uint)GlobalLocationDataTypeFields.Orientation
                        : 0,
                    Position = position,
                    Orientation = new ThreeDOrientation { C = 90.0 }
                },
                StatusCodes.Good,
                DateTimeUtc.Now);
        }

        private static ThreeDFrame CreateFrame(double x)
        {
            return new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = x },
                Orientation = new ThreeDOrientation()
            };
        }

        private static EUInformation CreateUnit(string unitId, string displayName)
        {
            return new EUInformation(
                unitId,
                displayName,
                "http://www.opcfoundation.org/UA/units/un/cefact");
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            for (int i = 0; i < 100; i++)
            {
                if (condition())
                {
                    return;
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
            Assert.Fail("The provider update was not applied before the timeout.");
        }

        private sealed class ControlledGlobalProvider :
            IGlobalPositionProvider,
            IDisposable
        {
            private readonly ConcurrentQueue<GlobalPositionSample> m_samples = new();
            private readonly SemaphoreSlim m_available = new(0);
            private readonly GlobalPositionSample m_initial;

            private readonly TaskCompletionSource<bool> m_cancellationObserved =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ControlledGlobalProvider(GlobalPositionSample initial)
            {
                m_initial = initial;
            }

            public Task CancellationObserved => m_cancellationObserved.Task;

            public void Publish(GlobalPositionSample sample)
            {
                m_samples.Enqueue(sample);
                m_available.Release();
            }

            public ValueTask<GlobalPositionSample> ReadAsync(
                string sourceId,
                CancellationToken cancellationToken)
            {
                return new ValueTask<GlobalPositionSample>(m_initial);
            }

            public async IAsyncEnumerable<GlobalPositionSample> WatchAsync(
                string sourceId,
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                try
                {
                    while (true)
                    {
                        await m_available.WaitAsync(cancellationToken)
                            .ConfigureAwait(false);
                        if (m_samples.TryDequeue(
                            out GlobalPositionSample? sample))
                        {
                            yield return sample;
                        }
                    }
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        m_cancellationObserved.TrySetResult(true);
                    }
                }
            }

            public void Dispose()
            {
                m_available.Dispose();
            }
        }

        private sealed class FailingGlobalProvider : IGlobalPositionProvider
        {
            private readonly GlobalPositionSample m_initial;
            private readonly GlobalPositionSample m_update;

            public FailingGlobalProvider(
                GlobalPositionSample initial,
                GlobalPositionSample update)
            {
                m_initial = initial;
                m_update = update;
            }

            public ValueTask<GlobalPositionSample> ReadAsync(
                string sourceId,
                CancellationToken cancellationToken)
            {
                return new ValueTask<GlobalPositionSample>(m_initial);
            }

            public async IAsyncEnumerable<GlobalPositionSample> WatchAsync(
                string sourceId,
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.Yield();
                yield return m_update;
                throw new InvalidOperationException("Provider failed.");
            }
        }

        private sealed class FailingRelativeProvider :
            IRelativeSpatialLocationProvider
        {
            private readonly RelativeSpatialLocationSample m_initial;
            private readonly RelativeSpatialLocationSample m_update;

            public FailingRelativeProvider(
                RelativeSpatialLocationSample initial,
                RelativeSpatialLocationSample update)
            {
                m_initial = initial;
                m_update = update;
            }

            public ValueTask<RelativeSpatialLocationSample> ReadAsync(
                string sourceId,
                CancellationToken cancellationToken)
            {
                return new ValueTask<RelativeSpatialLocationSample>(m_initial);
            }

            public async IAsyncEnumerable<RelativeSpatialLocationSample>
                WatchAsync(
                    string sourceId,
                    [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await Task.Yield();
                yield return m_update;
                throw new InvalidOperationException("Provider failed.");
            }
        }
    }
}
