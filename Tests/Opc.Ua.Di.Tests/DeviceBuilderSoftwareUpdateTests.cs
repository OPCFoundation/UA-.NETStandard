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
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Address-space tests for the
    /// <see cref="DeviceBuilderSoftwareUpdateExtensions.WithSoftwareUpdate{TDevice}(IDeviceBuilder{TDevice}, ISoftwarePackageStore, System.Action{ISoftwareUpdateBuilder})"/>
    /// extension. Verifies that the OPC 10000-100 §10.3 software-update
    /// facet (SoftwareUpdate + Loading + the four state machines)
    /// materialises under the device and that the method handlers
    /// drive the supplied callbacks.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("DeviceBuilder")]
    [Category("SoftwareUpdate")]
    public sealed class DeviceBuilderSoftwareUpdateTests
    {
        private DiServerFixture m_fixture = null!;
        private ISoftwarePackageStore m_store = null!;

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new DiServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
            m_store = new MemoryPackageStore();
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task WithSoftwareUpdateAttachesSoftwareUpdateSubtree()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceWithSoftwareUpdate",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            builder.WithSoftwareUpdate(m_store);

            NodeState? su = builder.Device.FindChild(
                m_fixture.Manager.SystemContext,
                new QualifiedName("SoftwareUpdate", m_fixture.Manager.DiNamespaceIndex));

            Assert.That(su, Is.Not.Null,
                "SoftwareUpdate child must be attached to the device.");
            Assert.That(su, Is.TypeOf<SoftwareUpdateState>(),
                "SoftwareUpdate child must be a SoftwareUpdateState instance.");
        }

        [Test]
        public async Task WithSoftwareUpdateMaterialisesAllStateMachineChildren()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceWithFullSU",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            builder.WithSoftwareUpdate(m_store, su => su.UsePackageLoading());

            var diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext ctx = m_fixture.Manager.SystemContext;
            NodeState su2 = builder.Device.FindChild(
                ctx, new QualifiedName("SoftwareUpdate", diNs))!;

            Assert.That(su2, Is.Not.Null);

            // Loading + four state machines all materialised.
            Assert.That(su2.FindChild(ctx, new QualifiedName("Loading", diNs)),
                Is.TypeOf<PackageLoadingState>(),
                "Loading must default to PackageLoadingType.");
            Assert.That(su2.FindChild(ctx, new QualifiedName("PrepareForUpdate", diNs)),
                Is.TypeOf<PrepareForUpdateStateMachineState>());
            Assert.That(su2.FindChild(ctx, new QualifiedName("Installation", diNs)),
                Is.TypeOf<InstallationStateMachineState>());
            Assert.That(su2.FindChild(ctx, new QualifiedName("PowerCycle", diNs)),
                Is.TypeOf<PowerCycleStateMachineState>());
            Assert.That(su2.FindChild(ctx, new QualifiedName("Confirmation", diNs)),
                Is.TypeOf<ConfirmationStateMachineState>());
        }

        [Test]
        public async Task UseDirectLoadingSwapsLoadingSubtype()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceDirect",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            builder.WithSoftwareUpdate(m_store, su => su.UseDirectLoading());

            var diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext ctx = m_fixture.Manager.SystemContext;
            NodeState su = builder.Device.FindChild(
                ctx, new QualifiedName("SoftwareUpdate", diNs))!;

            Assert.That(su.FindChild(ctx, new QualifiedName("Loading", diNs)),
                Is.TypeOf<DirectLoadingState>(),
                "UseDirectLoading must materialise DirectLoadingType.");
        }

        [Test]
        public async Task UseCachedLoadingSwapsLoadingSubtype()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceCached",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            builder.WithSoftwareUpdate(m_store, su => su.UseCachedLoading());

            var diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext ctx = m_fixture.Manager.SystemContext;
            NodeState su = builder.Device.FindChild(
                ctx, new QualifiedName("SoftwareUpdate", diNs))!;

            Assert.That(su.FindChild(ctx, new QualifiedName("Loading", diNs)),
                Is.TypeOf<CachedLoadingState>(),
                "UseCachedLoading must materialise CachedLoadingType.");
        }

        [Test]
        public async Task OnPrepareCallbackFiresOnPrepareForUpdate()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DevicePrepareCallback",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            bool prepareInvoked = false;
            builder.WithSoftwareUpdate(m_store, su => su.OnPrepare((_, _) =>
            {
                prepareInvoked = true;
                return default;
            }));

            var diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext ctx = m_fixture.Manager.SystemContext;
            NodeState su = builder.Device.FindChild(
                ctx, new QualifiedName("SoftwareUpdate", diNs))!;
            var prep = (PrepareForUpdateStateMachineState)su.FindChild(
                ctx, new QualifiedName("PrepareForUpdate", diNs))!;
            Assert.That(prep.Prepare, Is.Not.Null);

            ServiceResult result = await InvokeMethodAsync(
                prep.Prepare!, su.NodeId, new ArrayOf<Variant>(System.Array.Empty<Variant>())).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            Assert.That(prepareInvoked, Is.True,
                "OnPrepare callback must be invoked when Prepare is called.");
        }

        [Test]
        public async Task DefaultOnInstallRecordsVersionInFolder()
        {
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "DeviceDefaultInstall",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            var folder = new MemorySoftwareFolder(builder.Device.NodeId);
            builder.WithSoftwareUpdate(m_store, su => su.WithSoftwareFolder(folder));

            var diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext ctx = m_fixture.Manager.SystemContext;
            NodeState su = builder.Device.FindChild(
                ctx, new QualifiedName("SoftwareUpdate", diNs))!;
            var install = (InstallationStateMachineState)su.FindChild(
                ctx, new QualifiedName("Installation", diNs))!;
            Assert.That(install.InstallSoftwarePackage, Is.Not.Null);

            // InstallSoftwarePackage(manufacturerUri, softwareRevision,
            //                        patchIdentifiers[], hash)
            var inputs = new ArrayOf<Variant>(new[]
            {
                new Variant("urn:acme:firmware"),
                new Variant("2.0.0"),
                new Variant(System.Array.Empty<string>()),
                new Variant(System.Array.Empty<byte>()),
            });

            ServiceResult result = await InvokeMethodAsync(
                install.InstallSoftwarePackage!, su.NodeId, inputs).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(ServiceResult.Good));
            SoftwarePackage? current = await folder.GetCurrentVersionAsync().ConfigureAwait(false);
            Assert.That(current, Is.Not.Null,
                "Default OnInstall stub must record the version in the folder.");
            Assert.That(current!.Version, Is.EqualTo("2.0.0"));
        }

        private static async Task<ServiceResult> InvokeMethodAsync(
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs)
        {
            var outputs = new System.Collections.Generic.List<Variant>();
            if (method.OnCallMethod2Async is not null)
            {
                return await method.OnCallMethod2Async(
                    null!, method, objectId, inputs, outputs,
                    CancellationToken.None).ConfigureAwait(false);
            }
            if (method.OnCallMethod2 is not null)
            {
                return method.OnCallMethod2(null!, method, objectId, inputs, outputs);
            }
            return new ServiceResult(StatusCodes.BadNotImplemented);
        }
    }
}
