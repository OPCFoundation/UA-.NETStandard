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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server.Builders;

namespace Opc.Ua.AutoID.Server.Builders
{
    /// <summary>
    /// The default <see cref="IAutoIdReaderBuilder"/>: collects the configuration and hands it to
    /// the runtime in one step.
    /// </summary>
    internal sealed class AutoIdReaderBuilder :
        IAutoIdReaderBuilder,
        IAutoIdRuntimeParametersBuilder,
        IAutoIdImagesBuilder,
        IAutoIdIODataBuilder,
        IAutoIdDiagnosticsBuilder
    {
        internal AutoIdReaderBuilder(
            AutoIdRuntime runtime,
            QualifiedName browseName,
            IAutoIdReaderProvider provider)
        {
            m_runtime = runtime;
            BrowseName = browseName;
            Provider = provider;
        }

        internal QualifiedName BrowseName { get; }

        internal IAutoIdReaderProvider Provider { get; }

        internal NodeState? Parent { get; private set; }

        internal BaseObjectState? Machine { get; private set; }

        internal string? DeviceName { get; private set; }

        internal string? DeviceInfo { get; private set; }

        internal Location? DeviceLocation { get; private set; }

        internal string? DeviceLocationName { get; private set; }

        internal ArrayOf<AntennaNameIdPair> AntennaNames { get; private set; }

        internal bool HasAntennaNames { get; private set; }

        internal RtlsUnits? Units { get; private set; }

        internal Action<DeviceIdentificationData>? Identification { get; private set; }

        internal bool OwnsProviderOnDispose { get; private set; }

        /// <summary>
        /// Gets whether the reader exposes the parameter capability.
        /// </summary>
        internal bool HasRuntimeParameters { get; private set; }

        /// <summary>
        /// Gets whether ScanActive is enabled.
        /// </summary>
        internal bool HasScanControl { get; private set; }

        /// <summary>
        /// Gets whether the reader exposes standard diagnostics.
        /// </summary>
        internal bool HasDiagnostics { get; private set; }

        /// <summary>
        /// Gets the fixed image slots configured before registration.
        /// </summary>
        internal ArrayOf<AutoIdImageSlot> Images { get; private set; }

        /// <summary>
        /// Gets the application callback that populates IOData before registration.
        /// </summary>
        internal Action<ISystemContext, BaseObjectState>? ConfigureIOData { get; private set; }

        /// <summary>
        /// Gets or sets the initial provider configuration.
        /// </summary>
        internal AutoIdScanParameters? Parameters { get; set; }

        /// <summary>
        /// Gets or sets the reader-specific capabilities captured before registration.
        /// </summary>
        internal AutoIdReaderParameterCapabilities? ParameterCapabilities { get; set; }

        /// <summary>
        /// Gets the reader-specific parameter composition callback.
        /// </summary>
        internal Action<ISystemContext, BaseObjectState>? ConfigureParameters { get; private set; }

        /// <summary>
        /// Gets the standard parameter and control write permissions.
        /// </summary>
        internal ArrayOf<RolePermissionType> ScanWritePermissions { get; private set; }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder WithIOData(Action<ISystemContext, BaseObjectState> configure)
        {
            ConfigureIOData = configure ?? throw new ArgumentNullException(nameof(configure));
            return this;
        }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder WithImages(ArrayOf<AutoIdImageSlot> images)
        {
            if (Provider.Kind is not (AutoIdReaderKind.Ocr or AutoIdReaderKind.Optical or
                AutoIdReaderKind.OpticalVerifier))
            {
                throw new InvalidOperationException("Only OCR and optical readers publish Images.");
            }
            if (images.Count > 16)
            {
                throw new ArgumentOutOfRangeException(nameof(images), "At most sixteen image slots are supported.");
            }
            var names = new System.Collections.Generic.HashSet<QualifiedName>();
            for (int i = 0; i < images.Count; i++)
            {
                if (images[i] == null || !names.Add(images[i].Name))
                {
                    throw new ArgumentException("Image slots must be nonnull and have unique names.", nameof(images));
                }
            }
            Images = images.Span.ToArray().ToArrayOf();
            return this;
        }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder WithScanWritePermissions(ArrayOf<RolePermissionType> permissions)
        {
            ScanWritePermissions = permissions.ToArray().ToArrayOf();
            return this;
        }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder WithDiagnostics()
        {
            HasDiagnostics = true;
            return this;
        }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder WithRuntimeParameters(
            bool enableScanControl = false,
            Action<ISystemContext, BaseObjectState>? configure = null)
        {
            if (Provider is not IAutoIdScanParametersProvider ||
                (enableScanControl && Provider is not IAutoIdAsyncScanProvider))
            {
                throw new InvalidOperationException(
                    "The provider does not support the requested parameter/control capability.");
            }
            HasRuntimeParameters = true;
            HasScanControl = enableScanControl;
            ConfigureParameters = configure;
            return this;
        }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder WithParent(NodeState parent)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            if (Machine != null)
            {
                throw new InvalidOperationException(
                    "A reader that is a component of a machine is placed below its Components add-in.");
            }
            Parent = parent;
            return this;
        }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder AsComponentOf(BaseObjectState machine)
        {
            if (machine == null)
            {
                throw new ArgumentNullException(nameof(machine));
            }
            if (Parent != null)
            {
                throw new InvalidOperationException(
                    "A reader placed below an explicit parent cannot also be a component of a machine.");
            }
            Machine = machine;
            return this;
        }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder WithDeviceName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A device name is required.", nameof(name));
            }
            DeviceName = name;
            return this;
        }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder WithDeviceInfo(string info)
        {
            DeviceInfo = info ?? throw new ArgumentNullException(nameof(info));
            return this;
        }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder WithDeviceLocation(Location location)
        {
            DeviceLocation = location ?? throw new ArgumentNullException(nameof(location));
            return this;
        }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder WithDeviceLocationName(string name)
        {
            DeviceLocationName = name ?? throw new ArgumentNullException(nameof(name));
            return this;
        }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder WithAntennaNames(ArrayOf<AntennaNameIdPair> antennas)
        {
            if (Provider.Kind != AutoIdReaderKind.Rfid)
            {
                throw new InvalidOperationException("Antenna names belong to RFID readers.");
            }
            AntennaNames = antennas;
            HasAntennaNames = true;
            return this;
        }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder WithRtlsUnits(
            EUInformation lengthUnit,
            EUInformation rotationalUnit,
            EUInformation geographicalUnit,
            EUInformation speedUnit)
        {
            if (Provider.Kind != AutoIdReaderKind.Rtls)
            {
                throw new InvalidOperationException("Units belong to RTLS devices.");
            }
            Units = new RtlsUnits(
                lengthUnit ?? throw new ArgumentNullException(nameof(lengthUnit)),
                rotationalUnit ?? throw new ArgumentNullException(nameof(rotationalUnit)),
                geographicalUnit ?? throw new ArgumentNullException(nameof(geographicalUnit)),
                speedUnit ?? throw new ArgumentNullException(nameof(speedUnit)));
            return this;
        }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder WithIdentification(Action<DeviceIdentificationData> configure)
        {
            Identification = configure ?? throw new ArgumentNullException(nameof(configure));
            return this;
        }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder OwnsProvider()
        {
            OwnsProviderOnDispose = true;
            return this;
        }

        /// <inheritdoc/>
        public ValueTask<AutoIdReader> BuildAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref m_built, 1) != 0)
            {
                throw new InvalidOperationException("The reader builder was already used.");
            }
            return m_runtime.CreateReaderAsync(this, cancellationToken);
        }

        /// <summary>
        /// The four units of an RTLS device.
        /// </summary>
        internal sealed record RtlsUnits(
            EUInformation Length,
            EUInformation Rotational,
            EUInformation Geographical,
            EUInformation Speed);

        private readonly AutoIdRuntime m_runtime;
        private int m_built;
    }
}
