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
using Moq;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;
using Opc.Ua.AutoID.Server.Builders;
using Opc.Ua.Di.Server.Builders;

namespace Opc.Ua.AutoID.Tests
{
    public sealed partial class AutoIdParameterControlTests
    {
        /// <summary>
        /// Keeps the published builder contract implementable without the new optional capabilities.
        /// </summary>
        [Test]
        public async Task LegacyBuilderRetainsOriginalContractAndCanBuildAsync()
        {
            IAutoIdReaderBuilder builder = new LegacyReaderBuilder(
                m_fixture.Runtime.Reader(Name(), new FakeProvider()));
            AutoIdReader reader = await builder.WithDeviceName("Legacy reader")
                .WithDeviceInfo("Original interface only")
                .BuildAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(reader.Device.DeviceName!.Value, Is.EqualTo("Legacy reader"));
                Assert.That(reader.Device.DeviceInfo!.Value, Is.EqualTo("Original interface only"));
                Assert.That(reader.Device.RuntimeParameters, Is.Null);
                Assert.That(typeof(IAutoIdReaderBuilder).GetMethods().Select(method => method.Name),
                    Is.EquivalentTo(new[]
                    {
                        "WithParent", "AsComponentOf", "WithDeviceName", "WithDeviceInfo",
                        "WithDeviceLocation", "WithDeviceLocationName", "WithAntennaNames", "WithRtlsUnits",
                        "WithIdentification", "OwnsProvider", "BuildAsync"
                    }));
            });
        }

        /// <summary>
        /// Rejects unsupported options without consuming or changing a legacy builder.
        /// </summary>
        [Test]
        public async Task LegacyBuilderRejectsOptionalCapabilitiesWithoutChangingConfigurationAsync()
        {
            IAutoIdReaderBuilder builder = new LegacyReaderBuilder(
                m_fixture.Runtime.Reader(Name(), new ParameterProvider()));
            bool configured = false;
            Assert.Multiple(() =>
            {
                Assert.Throws<NotSupportedException>(() =>
                    builder.WithRuntimeParameters(true, (_, _) => configured = true));
                Assert.Throws<NotSupportedException>(() => builder.WithScanWritePermissions([]));
                Assert.Throws<NotSupportedException>(() => builder.WithDiagnostics());
                Assert.That(configured, Is.False);
            });

            AutoIdReader reader = await builder.BuildAsync().ConfigureAwait(false);
            Assert.That(reader.Device.RuntimeParameters, Is.Null);
            Assert.That(reader.Device.ScanActive, Is.Null);
            Assert.That(reader.Device.Diagnostics, Is.Null);
        }

        /// <summary>
        /// Retains fluent composition of the SDK builder's parameter, permission and diagnostic capabilities.
        /// </summary>
        [Test]
        public async Task DefaultBuilderRetainsFluentParametersPermissionsAndDiagnosticsAsync()
        {
            IAutoIdReaderBuilder builder = m_fixture.Runtime.Reader(Name(), new ParameterProvider());
            BaseObjectState? configuredParent = null;
            IAutoIdReaderBuilder configured = builder
                .WithRuntimeParameters(true, (_, parent) => configuredParent = parent)
                .WithScanWritePermissions([new RolePermissionType
                {
                    RoleId = Opc.Ua.ObjectIds.WellKnownRole_Operator,
                    Permissions = (uint)(PermissionType.Browse | PermissionType.Read | PermissionType.Write)
                }])
                .WithDiagnostics();
            Assert.That(configured, Is.SameAs(builder));

            AutoIdReader reader = await configured.BuildAsync().ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(configuredParent, Is.SameAs(reader.Device.RuntimeParameters));
                Assert.That(reader.Device.ScanActive, Is.Not.Null);
                Assert.That(reader.Device.Diagnostics, Is.Not.Null);
                Assert.That(reader.Device.ScanActive!.RolePermissions[0].RoleId,
                    Is.EqualTo(Opc.Ua.ObjectIds.WellKnownRole_Operator));
                Assert.That(Parameter(reader, "Cycles").RolePermissions[0].Permissions,
                    Is.EqualTo((uint)(PermissionType.Browse | PermissionType.Read | PermissionType.Write)));
            });
        }

        /// <summary>
        /// Forwards every parameter option and the capability's returned builder without replacement.
        /// </summary>
        [Test]
        public void ParameterExtensionsForwardArgumentsAndReturnedBuilder()
        {
            var capability = new Mock<IAutoIdRuntimeParametersBuilder>(MockBehavior.Strict);
            var next = new Mock<IAutoIdReaderBuilder>(MockBehavior.Strict);
            Action<ISystemContext, BaseObjectState> configure = (_, _) => { };
            ArrayOf<RolePermissionType> permissions =
            [
                new RolePermissionType
                {
                    RoleId = Opc.Ua.ObjectIds.WellKnownRole_Operator,
                    Permissions = (uint)PermissionType.Write
                }
            ];
            capability.Setup(value => value.WithRuntimeParameters(true, configure)).Returns(next.Object);
            capability.Setup(value => value.WithScanWritePermissions(permissions)).Returns(next.Object);
            IAutoIdReaderBuilder builder = capability.Object;

            Assert.That(builder.WithRuntimeParameters(true, configure), Is.SameAs(next.Object));
            Assert.That(builder.WithScanWritePermissions(permissions), Is.SameAs(next.Object));
            Assert.Throws<NotSupportedException>(() => builder.WithDiagnostics());
            capability.VerifyAll();
            capability.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Allows diagnostics independently of the parameter capability.
        /// </summary>
        [Test]
        public void DiagnosticsExtensionForwardsToIndependentCapability()
        {
            var capability = new Mock<IAutoIdDiagnosticsBuilder>(MockBehavior.Strict);
            var next = new Mock<IAutoIdReaderBuilder>(MockBehavior.Strict);
            capability.Setup(value => value.WithDiagnostics()).Returns(next.Object);
            IAutoIdReaderBuilder builder = capability.Object;

            Assert.That(builder.WithDiagnostics(), Is.SameAs(next.Object));
            Assert.Throws<NotSupportedException>(() => builder.WithRuntimeParameters());
            Assert.Throws<NotSupportedException>(() => builder.WithScanWritePermissions([]));
            capability.VerifyAll();
            capability.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Rejects null receivers before checking optional capabilities.
        /// </summary>
        [Test]
        public void OptionalBuilderExtensionsRejectNullReceivers()
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(() =>
                    AutoIdReaderBuilderExtensions.WithRuntimeParameters(null!));
                Assert.Throws<ArgumentNullException>(() =>
                    AutoIdReaderBuilderExtensions.WithScanWritePermissions(null!, []));
                Assert.Throws<ArgumentNullException>(() =>
                    AutoIdReaderBuilderExtensions.WithDiagnostics(null!));
            });
        }

        /// <summary>
        /// Preserves provider validation errors from the capability implementation.
        /// </summary>
        [Test]
        public void OptionalBuilderExtensionsPreserveCapabilityExceptions()
        {
            var parameters = new Mock<IAutoIdRuntimeParametersBuilder>(MockBehavior.Strict);
            var diagnostics = new Mock<IAutoIdDiagnosticsBuilder>(MockBehavior.Strict);
            var error = new InvalidOperationException("Capability validation failed.");
            parameters.Setup(value => value.WithRuntimeParameters(false, null)).Throws(error);
            parameters.Setup(value => value.WithScanWritePermissions(default)).Throws(error);
            diagnostics.Setup(value => value.WithDiagnostics()).Throws(error);
            IAutoIdReaderBuilder parameterBuilder = parameters.Object;
            IAutoIdReaderBuilder diagnosticsBuilder = diagnostics.Object;

            Assert.That(Assert.Throws<InvalidOperationException>(() => parameterBuilder.WithRuntimeParameters()),
                Is.SameAs(error));
            Assert.That(Assert.Throws<InvalidOperationException>(() =>
                parameterBuilder.WithScanWritePermissions(default)), Is.SameAs(error));
            Assert.That(Assert.Throws<InvalidOperationException>(() => diagnosticsBuilder.WithDiagnostics()),
                Is.SameAs(error));
            parameters.VerifyAll();
            parameters.VerifyNoOtherCalls();
            diagnostics.VerifyAll();
            diagnostics.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Implements only the original master interface, delegating device creation to the runtime.
        /// </summary>
        private sealed class LegacyReaderBuilder(IAutoIdReaderBuilder inner) : IAutoIdReaderBuilder
        {
            /// <inheritdoc/>
            public IAutoIdReaderBuilder WithParent(NodeState parent)
            {
                inner.WithParent(parent);
                return this;
            }

            /// <inheritdoc/>
            public IAutoIdReaderBuilder AsComponentOf(BaseObjectState machine)
            {
                inner.AsComponentOf(machine);
                return this;
            }

            /// <inheritdoc/>
            public IAutoIdReaderBuilder WithDeviceName(string name)
            {
                inner.WithDeviceName(name);
                return this;
            }

            /// <inheritdoc/>
            public IAutoIdReaderBuilder WithDeviceInfo(string info)
            {
                inner.WithDeviceInfo(info);
                return this;
            }

            /// <inheritdoc/>
            public IAutoIdReaderBuilder WithDeviceLocation(Location location)
            {
                inner.WithDeviceLocation(location);
                return this;
            }

            /// <inheritdoc/>
            public IAutoIdReaderBuilder WithDeviceLocationName(string name)
            {
                inner.WithDeviceLocationName(name);
                return this;
            }

            /// <inheritdoc/>
            public IAutoIdReaderBuilder WithAntennaNames(ArrayOf<AntennaNameIdPair> antennas)
            {
                inner.WithAntennaNames(antennas);
                return this;
            }

            /// <inheritdoc/>
            public IAutoIdReaderBuilder WithRtlsUnits(
                EUInformation lengthUnit,
                EUInformation rotationalUnit,
                EUInformation geographicalUnit,
                EUInformation speedUnit)
            {
                inner.WithRtlsUnits(lengthUnit, rotationalUnit, geographicalUnit, speedUnit);
                return this;
            }

            /// <inheritdoc/>
            public IAutoIdReaderBuilder WithIdentification(Action<DeviceIdentificationData> configure)
            {
                inner.WithIdentification(configure);
                return this;
            }

            /// <inheritdoc/>
            public IAutoIdReaderBuilder OwnsProvider()
            {
                inner.OwnsProvider();
                return this;
            }

            /// <inheritdoc/>
            public ValueTask<AutoIdReader> BuildAsync(CancellationToken cancellationToken = default)
            {
                return inner.BuildAsync(cancellationToken);
            }
        }
    }
}
