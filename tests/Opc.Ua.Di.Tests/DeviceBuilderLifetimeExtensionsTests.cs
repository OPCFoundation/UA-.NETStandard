// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Tests for the §10.6 LifetimeVariable + indication classifier
    /// extensions on <see cref="IDeviceBuilder{TDevice}"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("DeviceBuilder")]
    public sealed class DeviceBuilderLifetimeExtensionsTests
    {
        private DiServerFixture m_fixture = null!;

        [OneTimeSetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new DiServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task AddLifetimeIndicationCreatesVariableUnderDevice()
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "LifetimeDevice1",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            LifetimeVariableState variable = device.AddLifetimeIndication(
                new QualifiedName("Hours", m_fixture.Manager.DiNamespaceIndex),
                LifetimeIndicationKind.Time,
                startValue: 0.0);

            Assert.That(variable, Is.Not.Null);
            Assert.That(variable.BrowseName.Name, Is.EqualTo("Hours"));
            Assert.That(variable.Parent, Is.SameAs(device.Device));
            Assert.That((double)variable.Value!, Is.Zero);
        }

        [Test]
        public async Task AddLifetimeIndicationRegistersWithManager()
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "LifetimeDevice2",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            LifetimeVariableState variable = device.AddLifetimeIndication(
                new QualifiedName("Parts", m_fixture.Manager.DiNamespaceIndex),
                LifetimeIndicationKind.NumberOfParts,
                startValue: 100.0);

            NodeState? found = m_fixture.Manager.FindPredefinedNode(variable.NodeId);
            Assert.That(found, Is.SameAs(variable));
        }

        [TestCase(LifetimeIndicationKind.Time, ObjectTypes.TimeIndicationType)]
        [TestCase(LifetimeIndicationKind.NumberOfParts,
            ObjectTypes.NumberOfPartsIndicationType)]
        [TestCase(LifetimeIndicationKind.NumberOfUsages,
            ObjectTypes.NumberOfUsagesIndicationType)]
        [TestCase(LifetimeIndicationKind.Length, ObjectTypes.LengthIndicationType)]
        [TestCase(LifetimeIndicationKind.Diameter, ObjectTypes.DiameterIndicationType)]
        [TestCase(LifetimeIndicationKind.SubstanceVolume,
            ObjectTypes.SubstanceVolumeIndicationType)]
        public void ResolveIndicationTypeIdReturnsMatchingId(
            LifetimeIndicationKind kind, uint expectedId)
        {
            var nsTable = new NamespaceTable();
            nsTable.Append(Opc.Ua.Di.Namespaces.OpcUaDi);

            NodeId resolved = kind
                .ResolveIndicationTypeId(nsTable);

            Assert.That(resolved.IdentifierAsString,
                Is.EqualTo(expectedId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(resolved.NamespaceIndex,
                Is.EqualTo(nsTable.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi)));
        }

        [Test]
        public void ResolveIndicationTypeIdWithNullNamespaceTableThrows()
        {
            Assert.That(() => LifetimeIndicationKind.Time
                .ResolveIndicationTypeId(null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public async Task AddLifetimeIndicationInvokesConfigureCallback()
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(
                    "LifetimeDevice3",
                    m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);

            bool invoked = false;
            _ = device.AddLifetimeIndication(
                new QualifiedName("Cycles", m_fixture.Manager.DiNamespaceIndex),
                LifetimeIndicationKind.NumberOfUsages,
                startValue: 5.0,
                configure: _ => invoked = true);

            Assert.That(invoked, Is.True);
        }
    }
}
