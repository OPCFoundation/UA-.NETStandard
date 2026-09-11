/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Globalization;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Adapts tests imported from the newer API to the unchanged master378
    /// public aggregate contracts.
    /// </summary>
    internal static partial class HistorianBackportTestCompatibility
    {
        public static bool TryGetProcessedValue(
            this IAggregateCalculator calculator,
            bool returnPartial,
            out DataValue value)
        {
            value = calculator.GetProcessedValue(returnPartial);
            return value != null;
        }

        public static double ConvertToDouble(this Variant value)
        {
            return Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
        }

        public static double GetDouble(this double value)
        {
            return value;
        }

        public static bool TryGetValue<T>(this Variant value, out T result)
        {
            if (value.Value is T typed)
            {
                result = typed;
                return true;
            }

            try
            {
                result = (T)Convert.ChangeType(
                    value.Value,
                    typeof(T),
                    CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                result = default;
                return false;
            }
        }

    }
}
