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
using Opc.Ua.Machinery.Server.Builders;

namespace Opc.Ua.CranesHoists.Server
{
    /// <summary>
    /// Selects a crane motor's optional monitoring and component identification.
    /// </summary>
    public sealed class CraneMotorBuilder
    {
        /// <summary>
        /// Creates a motor definition.
        /// </summary>
        internal CraneMotorBuilder(CraneSystemBuilder owner, string id)
        {
            Owner = owner;
            Id = id;
        }

        /// <summary>
        /// Gets the system-local provider identity.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Gets the owning system definition.
        /// </summary>
        internal CraneSystemBuilder Owner { get; }

        /// <summary>
        /// Gets whether overtemperature monitoring is selected.
        /// </summary>
        internal bool Overheated { get; private set; }

        /// <summary>
        /// Gets whether inherited service-brake monitoring is selected.
        /// </summary>
        internal bool ServiceBrake { get; private set; }

        /// <summary>
        /// Gets the optional shared nameplate configuration.
        /// </summary>
        internal Action<MachineryIdentificationData>? Identification { get; private set; }

        /// <summary>
        /// Selects provider-reported overtemperature state.
        /// </summary>
        public CraneMotorBuilder WithOverheated()
        {
            Owner.EnsureMutable();
            Overheated = true;
            return this;
        }

        /// <summary>
        /// Selects the inherited Robotics service-brake monitoring signal.
        /// </summary>
        public CraneMotorBuilder WithServiceBrake()
        {
            Owner.EnsureMutable();
            ServiceBrake = true;
            return this;
        }

        /// <summary>
        /// Selects the optional component nameplate, using the Machinery writer.
        /// </summary>
        public CraneMotorBuilder WithIdentification(Action<MachineryIdentificationData> configure)
        {
            Owner.EnsureMutable();
            Identification = configure ?? throw new ArgumentNullException(nameof(configure));
            return this;
        }
    }
}
