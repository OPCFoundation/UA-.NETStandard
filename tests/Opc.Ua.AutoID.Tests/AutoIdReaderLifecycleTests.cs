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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;
using Opc.Ua.AutoID.Server.Builders;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Registration, rollback, ownership and shutdown of readers.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    public sealed class AutoIdReaderLifecycleTests
    {
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new AutoIdManagerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            await m_fixture!.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ReaderIsRegisteredWithItsMandatoryComponentsAsync()
        {
            var provider = new FakeProvider();

            AutoIdReader reader = await m_fixture!.Runtime
                .CreateReaderAsync(m_fixture.Name(Unique("Dock")), provider).ConfigureAwait(false);

            var device = (RfidReaderDeviceState)reader.Device;
            NodeState? registered = m_fixture.Manager.FindPredefinedNode(device.NodeId);
            Assert.Multiple(() =>
            {
                Assert.That(registered, Is.SameAs(device));
                Assert.That(reader.Kind, Is.EqualTo(AutoIdReaderKind.Rfid));
                Assert.That(reader.Name.Name, Is.EqualTo(Unique("Dock")));
                Assert.That(reader.NodeId, Is.EqualTo(device.NodeId));
                Assert.That(device.DeviceName!.Value, Is.EqualTo(Unique("Dock")));
                Assert.That(device.DeviceStatus!.Value, Is.EqualTo(DeviceStatusEnumeration.Idle));
                Assert.That(reader.Status, Is.EqualTo(DeviceStatusEnumeration.Idle));
                Assert.That(device.AutoIdModelVersion!.Value, Is.EqualTo("1.00"));
                Assert.That(device.LastScanData!.StatusCode, Is.EqualTo(StatusCodes.UncertainInitialValue));
                Assert.That(device.EventNotifier & EventNotifiers.SubscribeToEvents, Is.Not.EqualTo(EventNotifiers.None));
                Assert.That(provider.AttachCalls, Is.EqualTo(1));
                Assert.That(m_fixture.Runtime.Readers.ToArray(), Does.Contain(reader));
            });
        }

        [Test]
        public async Task OptionalMethodsFollowTheCapabilitiesOfTheProviderAsync()
        {
            var full = new FakeProvider();
            var partial = new FakeProvider
            {
                SupportedOperations = AutoIdRfidOperations.ReadTag | AutoIdRfidOperations.KillTag
            };
            var minimal = new MinimalProvider(AutoIdReaderKind.Optical);

            var fullDevice = (RfidReaderDeviceState)(await m_fixture!.Runtime
                .CreateReaderAsync(m_fixture.Name(Unique("Full")), full).ConfigureAwait(false)).Device;
            var partialDevice = (RfidReaderDeviceState)(await m_fixture.Runtime
                .CreateReaderAsync(m_fixture.Name(Unique("Partial")), partial).ConfigureAwait(false)).Device;
            var minimalDevice = (OpticalReaderDeviceState)(await m_fixture.Runtime
                .CreateReaderAsync(m_fixture.Name(Unique("Minimal")), minimal).ConfigureAwait(false)).Device;

            Assert.Multiple(() =>
            {
                Assert.That(fullDevice.Scan, Is.Not.Null);
                Assert.That(fullDevice.ScanStart, Is.Not.Null);
                Assert.That(fullDevice.ScanStop, Is.Not.Null);
                Assert.That(
                    new NodeState?[]
                    {
                        fullDevice.ReadTag, fullDevice.WriteTag, fullDevice.WriteTagID,
                        fullDevice.KillTag, fullDevice.LockTag, fullDevice.SetTagPassword
                    },
                    Has.All.Not.Null);
                Assert.That(partialDevice.ReadTag, Is.Not.Null);
                Assert.That(partialDevice.KillTag, Is.Not.Null);
                Assert.That(partialDevice.WriteTag, Is.Null);
                Assert.That(partialDevice.WriteTagID, Is.Null);
                Assert.That(partialDevice.LockTag, Is.Null);
                Assert.That(partialDevice.SetTagPassword, Is.Null);
                Assert.That(minimalDevice.Scan, Is.Null, "no synchronous scan support, no Scan method");
                Assert.That(minimalDevice.ScanStart, Is.Null);
                Assert.That(minimalDevice.ScanStop, Is.Null);
                Assert.That(minimalDevice.GetDeviceLocation, Is.Null, "no location configured");
            });
        }

        [Test]
        public async Task ConfiguredOptionalPropertiesArePublishedAsync()
        {
            var location = new Location { SwitchField = LocationFields.Name, Name = "Dock 1" };
            AutoIdReader reader = await m_fixture!.Runtime
                .Reader(m_fixture.Name(Unique("Configured")), new FakeProvider())
                .WithDeviceName("Custom name")
                .WithDeviceInfo("info")
                .WithDeviceLocationName("Gate")
                .WithDeviceLocation(location)
                .WithAntennaNames([new AntennaNameIdPair { AntennaId = 2, AntennaName = "South" }])
                .WithIdentification(data => data.SerialNumber = "SN-77")
                .BuildAsync().ConfigureAwait(false);

            var device = (RfidReaderDeviceState)reader.Device;
            Assert.Multiple(() =>
            {
                Assert.That(device.DeviceName!.Value, Is.EqualTo("Custom name"));
                Assert.That(device.DeviceInfo!.Value, Is.EqualTo("info"));
                Assert.That(device.DeviceLocationName!.Value, Is.EqualTo("Gate"));
                Assert.That(device.DeviceLocation!.Value!.Name, Is.EqualTo("Dock 1"));
                Assert.That(device.GetDeviceLocation, Is.Not.Null);
                Assert.That(device.AntennaNames!.Value[0].AntennaName, Is.EqualTo("South"));
                Assert.That(device.SerialNumber!.Value, Is.EqualTo("SN-77"));
            });
        }

        [Test]
        public async Task ADuplicateNameIsRejectedAndTheNameStaysUsableAsync()
        {
            await m_fixture!.Runtime.CreateReaderAsync(m_fixture.Name(Unique("Dock")), new FakeProvider())
                .ConfigureAwait(false);
            int before = m_fixture.Runtime.Readers.Count;

            ServiceResultException duplicate = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await m_fixture.Runtime.CreateReaderAsync(m_fixture.Name(Unique("Dock")), new FakeProvider())
                    .ConfigureAwait(false)))!;

            Assert.That(duplicate.StatusCode, Is.EqualTo((uint)StatusCodes.BadBrowseNameDuplicated));
            Assert.That(m_fixture.Runtime.Readers.Count, Is.EqualTo(before));
            await m_fixture.Runtime.CreateReaderAsync(m_fixture.Name(Unique("Dock2")), new FakeProvider())
                .ConfigureAwait(false);
        }

        [Test]
        public async Task ConcurrentCreationsOfOneNameAdmitExactlyOneAsync()
        {
            FakeProvider[] providers = Enumerable.Range(0, 8).Select(_ => new FakeProvider()).ToArray();
            int before = m_fixture!.Runtime.Readers.Count;

            Task<AutoIdReader>[] creations = [.. providers
                .Select(provider => Task.Run(async () => await m_fixture!.Runtime
                    .CreateReaderAsync(m_fixture.Name(Unique("Race")), provider).ConfigureAwait(false)))];
            int succeeded = 0;
            int duplicated = 0;
            foreach (Task<AutoIdReader> creation in creations)
            {
                try
                {
                    await creation.ConfigureAwait(false);
                    succeeded++;
                }
                catch (ServiceResultException ex) when (ex.StatusCode == StatusCodes.BadBrowseNameDuplicated)
                {
                    duplicated++;
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(succeeded, Is.EqualTo(1));
                Assert.That(duplicated, Is.EqualTo(7));
                Assert.That(m_fixture!.Runtime.Readers.Count, Is.EqualTo(before + 1));
                Assert.That(providers.Sum(provider => provider.AttachCalls), Is.EqualTo(1));
            });
        }

        [Test]
        public async Task AFailureAfterRegistrationRollsTheReaderBackAsync()
        {
            var provider = new FakeProvider();
            int before = m_fixture!.Runtime.Readers.Count;
            m_fixture.Runtime.ReaderRegisteredForTest = _ => throw new InvalidOperationException("registration failed");
            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await m_fixture.Runtime.CreateReaderAsync(m_fixture.Name(Unique("Dock")), provider)
                        .ConfigureAwait(false));
            }
            finally
            {
                m_fixture.Runtime.ReaderRegisteredForTest = null;
            }

            Assert.Multiple(() =>
            {
                Assert.That(m_fixture.Runtime.Readers.Count, Is.EqualTo(before));
                Assert.That(DeviceSetChild("Dock"), Is.Null, "the half-registered device is removed");
                Assert.That(provider.AttachCalls, Is.EqualTo(1));
                Assert.That(provider.DetachCalls, Is.EqualTo(1), "an attached provider is detached again");
            });

            AutoIdReader retry = await m_fixture.Runtime.CreateReaderAsync(m_fixture.Name(Unique("Dock")), new FakeProvider())
                .ConfigureAwait(false);
            Assert.That(DeviceSetChild("Dock"), Is.SameAs(retry.Device));
        }

        [Test]
        public async Task AProviderThatFailsToAttachRollsTheReaderBackAsync()
        {
            var provider = new FakeProvider { AttachException = new InvalidOperationException("no hardware") };
            int before = m_fixture!.Runtime.Readers.Count;

            InvalidOperationException failure = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await m_fixture!.Runtime.CreateReaderAsync(m_fixture.Name(Unique("Dock")), provider)
                    .ConfigureAwait(false)))!;

            Assert.Multiple(() =>
            {
                Assert.That(failure.Message, Is.EqualTo("no hardware"));
                Assert.That(DeviceSetChild("Dock"), Is.Null);
                Assert.That(m_fixture!.Runtime.Readers.Count, Is.EqualTo(before));
                Assert.That(provider.DetachCalls, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ABrokenIdentificationRollsTheReaderBackAsync()
        {
            var provider = new FakeProvider();

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await m_fixture!.Runtime.Reader(m_fixture.Name(Unique("Dock")), provider)
                    .WithIdentification(_ => throw new InvalidOperationException("bad nameplate"))
                    .BuildAsync().ConfigureAwait(false));

            Assert.Multiple(() =>
            {
                Assert.That(DeviceSetChild("Dock"), Is.Null);
                Assert.That(provider.AttachCalls, Is.Zero, "the provider is attached last");
            });
        }

        [Test]
        public void BuilderRejectsMisuse()
        {
            AutoIdRuntime runtime = m_fixture!.Runtime;
            var rfid = new FakeProvider();
            var ocr = new MinimalProvider(AutoIdReaderKind.Ocr);

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(() => runtime.Reader(default, rfid));
                Assert.Throws<ArgumentNullException>(() => runtime.Reader(m_fixture.Name(Unique("X")), null!));
                Assert.Throws<InvalidOperationException>(
                    () => runtime.Reader(m_fixture.Name(Unique("X")), ocr).WithAntennaNames(default));
                Assert.Throws<InvalidOperationException>(
                    () => runtime.Reader(m_fixture.Name(Unique("X")), rfid).WithRtlsUnits(
                        new EUInformation(), new EUInformation(), new EUInformation(), new EUInformation()));
                IAutoIdReaderBuilder builder = runtime.Reader(m_fixture.Name(Unique("X")), rfid);
                Assert.Throws<ArgumentNullException>(() => builder.WithParent(null!));
                Assert.Throws<ArgumentException>(() => builder.WithDeviceName(string.Empty));
                Assert.Throws<ArgumentNullException>(() => builder.WithDeviceInfo(null!));
                Assert.Throws<ArgumentNullException>(() => builder.WithDeviceLocation(null!));
                Assert.Throws<ArgumentNullException>(() => builder.WithDeviceLocationName(null!));
                Assert.Throws<ArgumentNullException>(() => builder.WithIdentification(null!));
            });
        }

        [Test]
        public async Task ABuilderBuildsOnceAsync()
        {
            IAutoIdReaderBuilder builder = m_fixture!.Runtime.Reader(m_fixture.Name(Unique("Once")), new FakeProvider());
            await builder.BuildAsync().ConfigureAwait(false);

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await builder.BuildAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task ReadersCanHangBelowAnExplicitParentAsync()
        {
            NodeState deviceSet = m_fixture!.Manager.FindPredefinedNode(
                NodeId.Create(Di.Objects.DeviceSet, Di.Namespaces.OpcUaDi, m_fixture.Context.NamespaceUris))!;
            AutoIdReader outer = await m_fixture.Runtime.CreateReaderAsync(m_fixture.Name(Unique("Outer")), new FakeProvider())
                .ConfigureAwait(false);

            AutoIdReader inner = await m_fixture.Runtime.Reader(m_fixture.Name(Unique("Inner")), new FakeProvider())
                .WithParent(outer.Device).BuildAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(inner.Device.Parent, Is.SameAs(outer.Device));
                Assert.That(deviceSet.FindChild(m_fixture.Context, m_fixture.Name(Unique("Inner"))), Is.Null);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposingTheRuntimeDetachesProvidersAndDisposesOnlyOwnedOnesAsync(bool owned)
        {
            await using AutoIdManagerFixture own = await StartOwnFixtureAsync().ConfigureAwait(false);
            var provider = new FakeProvider();
            IAutoIdReaderBuilder builder = own.Runtime.Reader(own.Name("Dock"), provider);
            if (owned)
            {
                builder.OwnsProvider();
            }
            AutoIdReader reader = await builder.BuildAsync().ConfigureAwait(false);

            await own.Runtime.DisposeAsync().ConfigureAwait(false);
            await own.Runtime.DisposeAsync().ConfigureAwait(false);

            await Assert.MultipleAsync(async () =>
            {
                Assert.That(provider.DetachCalls, Is.EqualTo(1));
                Assert.That(provider.Disposed, Is.EqualTo(owned));
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    async () => await reader.ReportPresenceAsync(1).ConfigureAwait(false));
                Assert.Throws<ObjectDisposedException>(
                    () => own.Runtime.Reader(own.Name("Late"), new FakeProvider()));
                await Assert.ThrowsAsync<ObjectDisposedException>(
                    async () => await own.Runtime.CreateReaderAsync(own.Name("Late"), new FakeProvider())
                        .ConfigureAwait(false));
            });
        }

        [Test]
        public async Task DisposingAReaderStopsItsActiveScanOnceAsync()
        {
            var provider = new FakeProvider();
            AutoIdReader reader = await m_fixture!.Runtime.CreateReaderAsync(m_fixture.Name(Unique("Dock")), provider)
                .ConfigureAwait(false);
            var device = (RfidReaderDeviceState)reader.Device;
            await device.ScanStart!.OnCallAsync!(
                m_fixture.Context, device.ScanStart, device.NodeId, AutoIdTestData.Settings(), CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(reader.IsAsyncScanActive, Is.True);

            await reader.DisposeAsync().ConfigureAwait(false);
            await reader.DisposeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(provider.StopCalls, Is.EqualTo(1));
                Assert.That(provider.DetachCalls, Is.EqualTo(1));
                Assert.That(reader.IsAsyncScanActive, Is.False);
            });
        }

        [Test]
        public async Task DisposingTheRuntimeCancelsAScanInFlightAsync()
        {
            await using AutoIdManagerFixture own = await StartOwnFixtureAsync().ConfigureAwait(false);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new FakeProvider
            {
                OnScan = async (settings, ct) =>
                {
                    started.SetResult(true);
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    return default;
                }
            };
            AutoIdReader reader = await own.Runtime.CreateReaderAsync(own.Name("Dock"), provider)
                .ConfigureAwait(false);
            var device = (RfidReaderDeviceState)reader.Device;
            Task<ScanRfidReaderDeviceTypeMethodStateResult> call = device.Scan!.OnCallAsync!(
                own.Context, device.Scan, device.NodeId, AutoIdTestData.Settings(duration: 1000),
                CancellationToken.None).AsTask();
            await started.Task.ConfigureAwait(false);

            await own.Runtime.DisposeAsync().ConfigureAwait(false);

            ServiceResultException halted = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await call.ConfigureAwait(false)))!;
            Assert.That(halted.StatusCode, Is.EqualTo((uint)StatusCodes.BadServerHalted));
        }

        [Test]
        public void ProvidersThatDoNotFitTheirKindAreRejected()
        {
            AutoIdRuntime runtime = m_fixture!.Runtime;
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(
                    () => runtime.Reader(m_fixture.Name(Unique("A")), new FakeProvider { Kind = AutoIdReaderKind.Ocr }),
                    "tag access on an OCR reader");
                Assert.Throws<ArgumentException>(
                    () => runtime.Reader(m_fixture.Name(Unique("B")), new FakeProvider
                    {
                        SupportedOperations = (AutoIdRfidOperations)0x100
                    }),
                    "unknown tag operations");
                Assert.Throws<ArgumentException>(
                    () => runtime.Reader(m_fixture.Name(Unique("C")), new MinimalProvider((AutoIdReaderKind)42)),
                    "unknown kind");
                Assert.Throws<ArgumentException>(
                    () => runtime.Reader(m_fixture.Name(Unique("D")), new MinimalProvider(AutoIdReaderKind.Rtls)),
                    "an RTLS device needs location support");
                Assert.Throws<ArgumentException>(
                    () => runtime.Reader(m_fixture.Name(Unique("E")), new FakeRtlsProvider(AutoIdReaderKind.Rfid, [])),
                    "RTLS support on an RFID reader");
                Assert.Throws<ArgumentException>(
                    () => runtime.Reader(m_fixture.Name(Unique("F")), new FakeRtlsProvider(AutoIdReaderKind.Rtls, [])),
                    "no location type");
                Assert.Throws<ArgumentException>(
                    () => runtime.Reader(m_fixture.Name(Unique("G")), new FakeRtlsProvider(
                        AutoIdReaderKind.Rtls, [(LocationTypeEnumeration)99])),
                    "undefined location type");
                Assert.Throws<ArgumentException>(
                    () => runtime.Reader(m_fixture.Name(Unique("H")), new InMemoryScanReaderProviderShim()),
                    "a verifier cannot publish the Scan method the NodeSet declares incorrectly");
            });
        }

        [Test]
        public void ServerOptionsAreValidated()
        {
            Assert.Multiple(() =>
            {
                Assert.DoesNotThrow(() => new AutoIdServerOptions().Validate());
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => new AutoIdServerOptions { MaximumScanDuration = TimeSpan.Zero }.Validate());
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => new AutoIdServerOptions { MaximumScanDuration = TimeSpan.FromHours(2) }.Validate());
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => new AutoIdServerOptions { MaximumTagReadLength = 0 }.Validate());
                Assert.Throws<ArgumentOutOfRangeException>(
                    () => new AutoIdServerOptions { MaximumScanResults = 0 }.Validate());
                Assert.Throws<ArgumentException>(
                    () => new AutoIdServerOptions { AdditionalNamespaceUris = [string.Empty] }.Validate());
            });
        }

        private static string Unique(string name)
        {
            return TestContext.CurrentContext.Test.Name + "_" + name;
        }

        private static async Task<AutoIdManagerFixture> StartOwnFixtureAsync()
        {
            var own = new AutoIdManagerFixture();
            await own.StartAsync().ConfigureAwait(false);
            return own;
        }

        private BaseInstanceState? DeviceSetChild(string name)
        {
            NodeState deviceSet = m_fixture!.Manager.FindPredefinedNode(
                NodeId.Create(Di.Objects.DeviceSet, Di.Namespaces.OpcUaDi, m_fixture.Context.NamespaceUris))!;
            return deviceSet.FindChild(m_fixture.Context, m_fixture.Name(Unique(name)));
        }

        private AutoIdManagerFixture? m_fixture;
    }

    /// <summary>
    /// A provider with nothing but the base contract: it scans on its own.
    /// </summary>
    internal sealed class MinimalProvider : IAutoIdReaderProvider
    {
        public MinimalProvider(AutoIdReaderKind kind)
        {
            Kind = kind;
        }

        public AutoIdReaderKind Kind { get; }

        public ValueTask AttachAsync(IAutoIdReaderSink sink, CancellationToken cancellationToken = default)
        {
            Sink = sink;
            return default;
        }

        public ValueTask DetachAsync(CancellationToken cancellationToken = default)
        {
            return default;
        }

        public IAutoIdReaderSink? Sink { get; private set; }
    }

    /// <summary>
    /// A location provider with configurable kind and location types.
    /// </summary>
    internal sealed class FakeRtlsProvider : IAutoIdRtlsProvider
    {
        public FakeRtlsProvider(AutoIdReaderKind kind, LocationTypeEnumeration[] types)
        {
            Kind = kind;
            SupportedLocationTypes = types.ToArrayOf();
        }

        public AutoIdReaderKind Kind { get; }

        public ArrayOf<LocationTypeEnumeration> SupportedLocationTypes { get; }

        public ValueTask AttachAsync(IAutoIdReaderSink sink, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask DetachAsync(CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask<RtlsLocationResult?> GetLocationAsync(
            ScanData identifier, string codeType, LocationTypeEnumeration locationType,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<RtlsLocationResult?>((RtlsLocationResult?)null);
        }
    }

    /// <summary>
    /// A verifier provider that wrongly offers the synchronous scan.
    /// </summary>
    internal sealed class InMemoryScanReaderProviderShim : IAutoIdScanProvider
    {
        public AutoIdReaderKind Kind => AutoIdReaderKind.OpticalVerifier;

        public ValueTask AttachAsync(IAutoIdReaderSink sink, CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask DetachAsync(CancellationToken cancellationToken = default)
        {
            return default;
        }

        public ValueTask<AutoIdScanOutcome> ScanAsync(ScanSettings settings, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
