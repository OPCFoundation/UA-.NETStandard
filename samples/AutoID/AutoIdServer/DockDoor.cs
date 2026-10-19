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
using System.Text;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.AutoID;
using Opc.Ua.AutoID.Server;
using Opc.Ua.AutoID.Server.Hosting;

namespace AutoIdSample
{
    /// <summary>
    /// The RFID reader of the dock door and the pallets that pass it.
    /// </summary>
    /// <remarks>
    /// The reader is an in-memory simulation of an EPC Gen2 reader: the library takes care of the OPC UA
    /// side (the methods, the events, the device status), the provider stands in for the hardware.
    /// </remarks>
    internal sealed class DockDoor : IAsyncDisposable
    {
        /// <summary>
        /// The browse name of the reader.
        /// </summary>
        public const string ReaderName = "DockDoor1";

        /// <summary>
        /// Gets the simulated reader hardware.
        /// </summary>
        public InMemoryRfidReaderProvider Hardware { get; } = new(TimeSpan.FromMilliseconds(1000));

        /// <summary>
        /// Gets the reader once the address space is ready.
        /// </summary>
        public AutoIdReader? Reader { get; private set; }

        /// <summary>
        /// Creates the reader and puts the first pallets into the antenna field.
        /// </summary>
        public async ValueTask ConfigureAsync(IAutoIdSetupContext context)
        {
            // Two pallets are always in range; the simulation adds and removes a third.
            await Hardware.PlaceTagAsync(Pallet(1), context.CancellationToken).ConfigureAwait(false);
            await Hardware.PlaceTagAsync(Pallet(2), context.CancellationToken).ConfigureAwait(false);
            foreach (int number in new[] { 1, 2 })
            {
                var identifier = new ScanData { SwitchField = ScanDataFields.ByteString, ByteString = EpcOf(number) };
                await Hardware.WriteTagAsync(
                    identifier, "UID", 3, 0, ByteString.From(LabelOf(number)), default, context.CancellationToken)
                    .ConfigureAwait(false);
            }

            Reader = await context.Runtime
                .Reader(new QualifiedName(ReaderName, context.Manager.InstanceNamespaceIndex), Hardware)
                .WithDeviceInfo("Dock door antenna portal")
                .WithDeviceLocationName("Dock 1, gate A")
                .WithDeviceLocation(new Location { SwitchField = LocationFields.Name, Name = "Dock 1" })
                .WithAntennaNames(
                [
                    new AntennaNameIdPair { AntennaId = 1, AntennaName = "Left post" },
                    new AntennaNameIdPair { AntennaId = 2, AntennaName = "Right post" }
                ])
                .WithIdentification(identification =>
                {
                    identification.Manufacturer = new LocalizedText("Example Automation");
                    identification.Model = new LocalizedText("RFID portal R-100");
                    identification.SerialNumber = "R100-0001";
                })
                .BuildAsync(context.CancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds a pallet tag: the pallet number in the EPC and in the user memory.
        /// </summary>
        public static InMemoryRfidTag Pallet(int number)
        {
            byte[] epc = [0xE2, 0x80, 0x11, 0x60, 0x00, 0x00, 0x20, 0x00, 0x00, (byte)(number >> 8), (byte)number, 0x01];
            return new InMemoryRfidTag(ByteString.From(epc))
            {
                UserMemorySize = 32,
                Antenna = 1 + (number % 2),
                Strength = -45 - (number * 3)
            };
        }

        /// <summary>
        /// Gets the EPC of a pallet.
        /// </summary>
        public static ByteString EpcOf(int number)
        {
            return Pallet(number).Epc;
        }

        /// <summary>
        /// Text for the user memory of a pallet.
        /// </summary>
        public static byte[] LabelOf(int number)
        {
            return Encoding.ASCII.GetBytes("PALLET-" + number.ToString("D4", System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            // The reader owns its provider only when asked to; this sample does not ask, so it disposes it.
            Hardware.Dispose();
            return default;
        }
    }
}
