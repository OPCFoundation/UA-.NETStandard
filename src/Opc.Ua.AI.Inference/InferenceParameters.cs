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
