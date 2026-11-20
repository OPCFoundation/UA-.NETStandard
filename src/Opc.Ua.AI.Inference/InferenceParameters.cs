/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Opc.Ua.AI.Inference
{
    internal readonly record struct InferenceParameters(
        float? Temperature,
        int? MaxTokens,
        float? TopP)
    {
        public static InferenceParameters Parse(IReadOnlyDictionary<string, string> parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);

            float? temperature = null;
            int? maxTokens = null;
            float? topP = null;

            foreach (KeyValuePair<string, string> parameter in parameters)
            {
                if (string.Equals(parameter.Key, "temperature", StringComparison.OrdinalIgnoreCase))
                {
                    temperature = ParseFraction(parameter, 0, 2);
                }
                else if (string.Equals(parameter.Key, "max_tokens", StringComparison.OrdinalIgnoreCase))
                {
                    maxTokens = ParsePositiveInteger(parameter);
                }
                else if (string.Equals(parameter.Key, "top_p", StringComparison.OrdinalIgnoreCase))
                {
                    topP = ParseFraction(parameter, 0, 1);
                }
                else
                {
                    throw new ArgumentException(
                        "The backend does not support the call parameter '" + parameter.Key + "'.",
                        nameof(parameters));
                }
            }

            return new InferenceParameters(temperature, maxTokens, topP);
        }

        private static float ParseFraction(KeyValuePair<string, string> parameter, float minimum, float maximum)
        {
            if (!float.TryParse(
                parameter.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value) ||
                !float.IsFinite(value) ||
                value < minimum ||
                value > maximum)
            {
                throw new ArgumentException(
                    "The call parameter '" + parameter.Key + "' has an invalid value.",
                    nameof(parameter));
            }

            return value;
        }

        private static int ParsePositiveInteger(KeyValuePair<string, string> parameter)
        {
            if (!int.TryParse(
                parameter.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value) ||
                value <= 0)
            {
                throw new ArgumentException(
                    "The call parameter '" + parameter.Key + "' has an invalid value.",
                    nameof(parameter));
            }

            return value;
        }
    }
}
