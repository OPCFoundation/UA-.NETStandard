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

namespace Opc.Ua.ECM.Server
{
    /// <summary>
    /// An OPC 34100 energy-saving mode with millisecond, kW and kWh quantities.
    /// </summary>
    public sealed record EcmEnergySavingMode
    {
        /// <summary>
        /// Creates a mode; IDs 0x00, 0xF0, 0xFE and 0xFF are reserved.
        /// </summary>
        public EcmEnergySavingMode(
            byte id,
            string name,
            double timeMinPause,
            double timeToPause,
            double timeMinLengthOfStay,
            double timeMaxLengthOfStay,
            double regularTimeToOperate,
            float modePowerConsumption,
            float energyConsumptionToPause,
            float energyConsumptionToOperate,
            bool dynamicData = false)
        {
            if (id is 0 or 0xF0 or 0xFE or 0xFF)
            {
                throw new ArgumentOutOfRangeException(nameof(id), "The mode ID is reserved by OPC 34100.");
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A mode name is required.", nameof(name));
            }
            ValidateQuantity(timeMinPause, nameof(timeMinPause));
            ValidateQuantity(timeToPause, nameof(timeToPause));
            ValidateQuantity(timeMinLengthOfStay, nameof(timeMinLengthOfStay));
            ValidateQuantity(timeMaxLengthOfStay, nameof(timeMaxLengthOfStay));
            ValidateQuantity(regularTimeToOperate, nameof(regularTimeToOperate));
            ValidateQuantity(modePowerConsumption, nameof(modePowerConsumption));
            ValidateQuantity(energyConsumptionToPause, nameof(energyConsumptionToPause));
            ValidateQuantity(energyConsumptionToOperate, nameof(energyConsumptionToOperate));
            if (timeMaxLengthOfStay < timeMinLengthOfStay)
            {
                throw new ArgumentOutOfRangeException(nameof(timeMaxLengthOfStay));
            }
            Id = id;
            Name = name;
            TimeMinPause = timeMinPause;
            TimeToPause = timeToPause;
            TimeMinLengthOfStay = timeMinLengthOfStay;
            TimeMaxLengthOfStay = timeMaxLengthOfStay;
            RegularTimeToOperate = regularTimeToOperate;
            ModePowerConsumption = modePowerConsumption;
            EnergyConsumptionToPause = energyConsumptionToPause;
            EnergyConsumptionToOperate = energyConsumptionToOperate;
            DynamicData = dynamicData;
        }

        /// <summary>
        /// Gets the nonreserved unique mode ID.
        /// </summary>
        public byte Id { get; }

        /// <summary>
        /// Gets the unique BrowseName text.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the minimum pause duration.
        /// </summary>
        public double TimeMinPause { get; }

        /// <summary>
        /// Gets the entry duration.
        /// </summary>
        public double TimeToPause { get; }

        /// <summary>
        /// Gets the minimum stay.
        /// </summary>
        public double TimeMinLengthOfStay { get; }

        /// <summary>
        /// Gets the maximum stay.
        /// </summary>
        public double TimeMaxLengthOfStay { get; }

        /// <summary>
        /// Gets the regular recovery duration.
        /// </summary>
        public double RegularTimeToOperate { get; }

        /// <summary>
        /// Gets the mode power in kW.
        /// </summary>
        public float ModePowerConsumption { get; }

        /// <summary>
        /// Gets the entry energy in kWh.
        /// </summary>
        public float EnergyConsumptionToPause { get; }

        /// <summary>
        /// Gets the recovery energy in kWh.
        /// </summary>
        public float EnergyConsumptionToOperate { get; }

        /// <summary>
        /// Gets whether the device may revise these characteristics during operation.
        /// </summary>
        public bool DynamicData { get; }

        /// <summary>
        /// Checks a nonnegative finite physical quantity.
        /// </summary>
        private static void ValidateQuantity(double value, string parameter)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(parameter);
            }
        }
    }
}
