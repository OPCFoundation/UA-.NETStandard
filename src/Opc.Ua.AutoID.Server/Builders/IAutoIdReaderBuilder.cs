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
    /// Stages one OPC 30010 reader and registers it in the address space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Obtain a builder from <see cref="AutoIdRuntime.Reader"/>. Nothing is added to the address
    /// space until <see cref="BuildAsync"/>; it then creates the device with exactly the
    /// components its provider supports, registers it and attaches the provider. A failure while
    /// registering is undone, so the name can be used again.
    /// </para>
    /// <para>
    /// The builder is single use: <see cref="BuildAsync"/> succeeds once.
    /// </para>
    /// </remarks>
    public interface IAutoIdReaderBuilder
    {
        /// <summary>
        /// Places the device below <paramref name="parent"/> (<c>HasComponent</c>) rather than
        /// directly below the DI <c>DeviceSet</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// OPC 10000-100 4.9 requires every <c>ComponentType</c> instance to be reachable from
        /// <c>DeviceSet</c> through hierarchical references, and only the root of a device composed
        /// of devices to be referenced from <c>DeviceSet</c> itself. When <c>DeviceSet</c> already
        /// reaches <paramref name="parent"/> (for example another reader, or a device it
        /// organizes), the reader is reached through it and gets no further reference. Otherwise
        /// <c>DeviceSet</c> also organizes the reader (<c>Organizes</c>), so it stays a starting
        /// point to locate the device.
        /// </para>
        /// <para>
        /// To make the reader a component of a Machinery machine, use
        /// <see cref="AsComponentOf"/> instead; the two cannot be combined.
        /// </para>
        /// </remarks>
        /// <param name="parent">The parent node, owned by the same node manager.</param>
        /// <exception cref="ArgumentNullException"><paramref name="parent"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// <see cref="AsComponentOf"/> was called on this builder.
        /// </exception>
        IAutoIdReaderBuilder WithParent(NodeState parent);

        /// <summary>
        /// Makes the device an identifiable component of a machine (OPC 40001-1 11).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Requires AutoID composed into the Machinery owner (<c>AddAutoIdToMachinery</c>, or a
        /// Device Integration owner that loads the Machinery model). The machine must be a node
        /// of that owner and publish the <c>Components</c> add-in, for example built with
        /// <c>IMachineBuilder.WithComponents</c>. When the reader is built, it is placed below
        /// <c>Components</c> (<c>HasComponent</c>) and gets the <c>Identification</c> add-in
        /// (<c>HasAddIn</c>) typed <c>MachineryComponentIdentificationType</c>. The add-in owns
        /// no copies of the nameplate: it references the reader's own DI nameplate properties
        /// (<c>Manufacturer</c>, <c>SerialNumber</c> and those of the other members that are set).
        /// Set the nameplate with <see cref="WithIdentification"/>; <c>Manufacturer</c> and
        /// <c>SerialNumber</c> are mandatory for a component and checked before anything is
        /// registered.
        /// </para>
        /// <para>
        /// The reader stays a device: <c>DeviceSet</c> organizes it unless it already reaches
        /// the machine (OPC 10000-100 4.9, see <see cref="WithParent"/>). It is not added to the
        /// <c>Machines</c> folder.
        /// </para>
        /// </remarks>
        /// <param name="machine">The machine, a node of the Machinery owner.</param>
        /// <exception cref="ArgumentNullException"><paramref name="machine"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// <see cref="WithParent"/> was called on this builder.
        /// </exception>
        IAutoIdReaderBuilder AsComponentOf(BaseObjectState machine);

        /// <summary>
        /// Sets the <c>DeviceName</c> property (OPC 30010 6.1.3.14); defaults to the browse name.
        /// </summary>
        /// <param name="name">The device name.</param>
        IAutoIdReaderBuilder WithDeviceName(string name);

        /// <summary>
        /// Publishes the optional <c>DeviceInfo</c> property (OPC 30010 6.1.3.11).
        /// </summary>
        /// <param name="info">The additional information for device management.</param>
        IAutoIdReaderBuilder WithDeviceInfo(string info);

        /// <summary>
        /// Publishes the optional <c>DeviceLocation</c> variable and the <c>GetDeviceLocation</c>
        /// method (OPC 30010 6.1.3.7 and 6.1.3.12). <see cref="AutoIdReader.SetDeviceLocation"/>
        /// updates it for a mobile reader.
        /// </summary>
        /// <param name="location">The location of the reader.</param>
        IAutoIdReaderBuilder WithDeviceLocation(Location location);

        /// <summary>
        /// Publishes the optional <c>DeviceLocationName</c> property (OPC 30010 6.1.3.13).
        /// </summary>
        /// <param name="name">A user defined name of the location, for example <c>Gate 21</c>.</param>
        IAutoIdReaderBuilder WithDeviceLocationName(string name);

        /// <summary>
        /// Publishes the optional <c>AntennaNames</c> property of an RFID reader (OPC 30010 6.5.3.10).
        /// </summary>
        /// <param name="antennas">The antenna identifiers and names.</param>
        /// <exception cref="InvalidOperationException">The reader is not an RFID reader.</exception>
        IAutoIdReaderBuilder WithAntennaNames(ArrayOf<AntennaNameIdPair> antennas);

        /// <summary>
        /// Sets the units of an RTLS device (OPC 30010 6.6.2). Defaults to metre, degree, degree
        /// and metre per second.
        /// </summary>
        /// <param name="lengthUnit">The unit of distances.</param>
        /// <param name="rotationalUnit">The unit of rotations.</param>
        /// <param name="geographicalUnit">The unit of geographical coordinates.</param>
        /// <param name="speedUnit">The unit of speeds.</param>
        /// <exception cref="InvalidOperationException">The reader is not an RTLS device.</exception>
        IAutoIdReaderBuilder WithRtlsUnits(
            EUInformation lengthUnit,
            EUInformation rotationalUnit,
            EUInformation geographicalUnit,
            EUInformation speedUnit);

        /// <summary>
        /// Sets the DI nameplate of the device (manufacturer, model, serial number and so on).
        /// </summary>
        /// <param name="configure">Fills the nameplate; members left unset are not written.</param>
        IAutoIdReaderBuilder WithIdentification(Action<DeviceIdentificationData> configure);

        /// <summary>
        /// Transfers ownership of the provider to the reader: it is disposed with the reader when it
        /// implements <see cref="IAsyncDisposable"/> or <see cref="IDisposable"/>. Without this the
        /// caller owns the provider.
        /// </summary>
        IAutoIdReaderBuilder OwnsProvider();

        /// <summary>
        /// Creates the device, registers it and attaches the provider.
        /// </summary>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>The live reader.</returns>
        /// <exception cref="ServiceResultException">
        /// The name is already taken below the parent (<c>Bad_BrowseNameDuplicated</c>), the
        /// parent is not available, or the machine of <see cref="AsComponentOf"/> cannot take the
        /// reader as a component or the nameplate lacks a mandatory member
        /// (<c>Bad_ConfigurationError</c>).
        /// </exception>
        /// <exception cref="InvalidOperationException">The builder was already used.</exception>
        /// <exception cref="ObjectDisposedException">The runtime has been disposed.</exception>
        ValueTask<AutoIdReader> BuildAsync(CancellationToken cancellationToken = default);
    }
}
