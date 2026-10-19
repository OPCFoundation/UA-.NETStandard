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
    internal sealed class AutoIdReaderBuilder : IAutoIdReaderBuilder
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

        internal string? DeviceName { get; private set; }

        internal string? DeviceInfo { get; private set; }

        internal Location? DeviceLocation { get; private set; }

        internal string? DeviceLocationName { get; private set; }

        internal ArrayOf<AntennaNameIdPair> AntennaNames { get; private set; }

        internal bool HasAntennaNames { get; private set; }

        internal RtlsUnits? Units { get; private set; }

        internal Action<DeviceIdentificationData>? Identification { get; private set; }

        internal bool OwnsProviderOnDispose { get; private set; }

        /// <inheritdoc/>
        public IAutoIdReaderBuilder WithParent(NodeState parent)
        {
            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
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
