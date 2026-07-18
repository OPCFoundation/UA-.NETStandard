/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Verifies that programmatically created DI devices satisfy the DI
    /// companion-spec structural rules that the compliance checker exercises:
    /// mandatory DeviceType children (GEN-01 / DI-01), DI-namespace
    /// BrowseNames (GEN-14), DeviceType HasInterface references (DI-05), and
    /// software-update state-machine method declarations (GEN-03).
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Compliance")]
    public sealed class DeviceComplianceTests
    {
        private static readonly string[] s_mandatoryNameplate =
        [
            "Manufacturer",
            "Model",
            "HardwareRevision",
            "SoftwareRevision",
            "DeviceRevision",
            "DeviceManual",
            "SerialNumber",
            "RevisionCounter"
        ];

        private DiServerFixture m_fixture = null!;
        private MemoryPackageStore m_store = null!;

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
        public async Task CreateDeviceMaterialisesMandatoryNameplateInDiNamespace()
        {
            ushort diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext ctx = m_fixture.Manager.SystemContext;
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName("CompliantDevice", diNs))
                .ConfigureAwait(false);

            foreach (string name in s_mandatoryNameplate)
            {
                NodeState? child = builder.Device.FindChild(
                    ctx, new QualifiedName(name, diNs));
                Assert.That(child, Is.Not.Null,
                    $"Mandatory DeviceType child '{name}' must be materialised (GEN-01/DI-01).");
                Assert.That(child!.BrowseName.NamespaceIndex, Is.EqualTo(diNs),
                    $"'{name}' BrowseName must be in the DI namespace (GEN-14).");
            }
        }

        [Test]
        public async Task CreateDeviceRebasesMandatoryNameplateNodeIdsPerInstance()
        {
            ushort diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext ctx = m_fixture.Manager.SystemContext;
            IDeviceBuilder<DeviceState> first = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName("NameplateDeviceA", diNs))
                .ConfigureAwait(false);
            IDeviceBuilder<DeviceState> second = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName("NameplateDeviceB", diNs))
                .ConfigureAwait(false);

            Assert.That(first.Device.NodeId, Is.Not.EqualTo(second.Device.NodeId));
            foreach (string name in s_mandatoryNameplate)
            {
                NodeState? firstChild = first.Device.FindChild(
                    ctx, new QualifiedName(name, diNs));
                NodeState? secondChild = second.Device.FindChild(
                    ctx, new QualifiedName(name, diNs));

                Assert.That(firstChild, Is.Not.Null);
                Assert.That(secondChild, Is.Not.Null);
                Assert.That(firstChild!.NodeId.IsNull, Is.False);
                Assert.That(secondChild!.NodeId.IsNull, Is.False);
                Assert.That(
                    firstChild.NodeId,
                    Is.Not.EqualTo(secondChild.NodeId),
                    $"{name} must be rebased for each DeviceType instance.");
            }
        }

        [Test]
        public async Task WithIdentificationCreatesOptionalNameplateInDiNamespace()
        {
            ushort diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext ctx = m_fixture.Manager.SystemContext;
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName("OptionalNameplateDevice", diNs))
                .ConfigureAwait(false);

            builder.WithIdentification(id => id.DeviceClass = "Pump");

            NodeState? deviceClass = builder.Device.FindChild(
                ctx, new QualifiedName("DeviceClass", diNs));
            Assert.That(deviceClass, Is.Not.Null,
                "DeviceClass must be created under the DI namespace, not namespace 0.");
            Assert.That(deviceClass!.BrowseName.NamespaceIndex, Is.EqualTo(diNs),
                "DeviceClass BrowseName must be in the DI namespace, not namespace 0 (GEN-14).");
        }

        [Test]
        public async Task CreateDeviceAddsDeviceTypeHasInterfaceReferences()
        {
            ushort diNs = m_fixture.Manager.DiNamespaceIndex;
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName("InterfaceDevice", diNs))
                .ConfigureAwait(false);

            NodeId hasInterface = Opc.Ua.Types.ReferenceTypeIds.HasInterface;
            NodeId deviceHealthIf = NodeId.Create(
                global::Opc.Ua.Di.ObjectTypes.IDeviceHealthType,
                DiNodeManager.DiNamespaceUri,
                m_fixture.Server.CurrentInstance.NamespaceUris);

            Assert.That(
                builder.Device.ReferenceExists(hasInterface, false, deviceHealthIf),
                Is.True,
                "DeviceType instance must carry HasInterface to IDeviceHealthType (DI-05).");
        }

        [Test]
        public async Task InstallationMethodsCarryInTypeMethodDeclarations()
        {
            ushort diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext ctx = m_fixture.Manager.SystemContext;
            IDeviceBuilder<DeviceState> builder = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName("SuMethodsDevice", diNs))
                .ConfigureAwait(false);

            builder.WithSoftwareUpdate(m_store, su => su.UsePackageLoading());

            NodeState su2 = builder.Device.FindChild(
                ctx, new QualifiedName("SoftwareUpdate", diNs))!;
            var installation = (InstallationStateMachineState)su2.FindChild(
                ctx, new QualifiedName("Installation", diNs))!;
            var prepare = (PrepareForUpdateStateMachineState)su2.FindChild(
                ctx, new QualifiedName("PrepareForUpdate", diNs))!;

            AssertDeclaration(installation.InstallSoftwarePackage,
                global::Opc.Ua.Di.Methods.InstallationStateMachineType_InstallSoftwarePackage,
                diNs, "InstallSoftwarePackage");
            AssertDeclaration(installation.InstallFiles,
                global::Opc.Ua.Di.Methods.InstallationStateMachineType_InstallFiles,
                diNs, "InstallFiles");
            AssertDeclaration(installation.Uninstall,
                global::Opc.Ua.Di.Methods.InstallationStateMachineType_Uninstall,
                diNs, "Uninstall");
            AssertDeclaration(prepare.Resume,
                global::Opc.Ua.Di.Methods.PrepareForUpdateStateMachineType_Resume,
                diNs, "Resume");
        }

        [Test]
        public async Task SoftwareUpdateNestedChildrenGetPerInstanceNodeIds()
        {
            ushort diNs = m_fixture.Manager.DiNamespaceIndex;
            ISystemContext ctx = m_fixture.Manager.SystemContext;

            IDeviceBuilder<DeviceState> a = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName("SuDeviceA", diNs))
                .ConfigureAwait(false);
            a.WithSoftwareUpdate(m_store, su => su.UsePackageLoading());

            IDeviceBuilder<DeviceState> b = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName("SuDeviceB", diNs))
                .ConfigureAwait(false);
            b.WithSoftwareUpdate(m_store, su => su.UsePackageLoading());

            NodeId timeoutA = FindConfirmationTimeout(ctx, a.Device, diNs);
            NodeId timeoutB = FindConfirmationTimeout(ctx, b.Device, diNs);

            Assert.That(timeoutA.IsNull, Is.False);
            Assert.That(timeoutB.IsNull, Is.False);
            Assert.That(timeoutA, Is.Not.EqualTo(timeoutB),
                "Nested SU children must get per-instance NodeIds so multiple " +
                "SU devices do not collide (GEN-05).");
        }

        private static NodeId FindConfirmationTimeout(
            ISystemContext ctx, DeviceState device, ushort diNs)
        {
            NodeState su = device.FindChild(
                ctx, new QualifiedName("SoftwareUpdate", diNs))!;
            NodeState confirmation = su.FindChild(
                ctx, new QualifiedName("Confirmation", diNs))!;
            return confirmation.FindChild(
                ctx, new QualifiedName("ConfirmationTimeout", diNs))!.NodeId;
        }

        private static void AssertDeclaration(
            MethodState? method,
            uint expectedDeclarationId,
            ushort diNs,
            string name)
        {
            Assert.That(method, Is.Not.Null, $"{name} method must be materialised.");
            Assert.That(method!.MethodDeclarationId,
                Is.EqualTo(new NodeId(expectedDeclarationId, diNs)),
                $"{name} MethodDeclarationId must point at the in-type declaration (GEN-03).");
        }
    }
}
