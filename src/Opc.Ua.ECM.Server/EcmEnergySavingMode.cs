// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
