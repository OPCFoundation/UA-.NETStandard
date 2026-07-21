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

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>
    /// Safety bounds applied while validating and executing binding forms. All
    /// bounds are conservative defaults that planners and executors enforce to
    /// avoid unbounded addressing, payloads or fan-out.
    /// </summary>
    public sealed class WotBindingBounds
    {
        /// <summary>Gets the shared default bounds.</summary>
        public static WotBindingBounds Default { get; } = new WotBindingBounds();

        /// <summary>Gets or sets the maximum accepted <c>href</c> / URI length.</summary>
        public int MaxUriLength { get; set; } = 2048;

        /// <summary>Gets or sets the maximum accepted MQTT topic length.</summary>
        public int MaxTopicLength { get; set; } = 65535;

        /// <summary>Gets or sets the maximum accepted request / response payload size (bytes).</summary>
        public int MaxPayloadBytes { get; set; } = 1024 * 1024;

        /// <summary>Gets or sets the maximum Modbus register quantity for a read.</summary>
        public int MaxRegisterQuantity { get; set; } = 125;

        /// <summary>Gets or sets the maximum Modbus coil / discrete-input quantity for a read.</summary>
        public int MaxCoilQuantity { get; set; } = 2000;

        /// <summary>Gets or sets the default operation timeout applied by executors.</summary>
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Validates a numeric bound and throws when it is non-positive.</summary>
        public static void EnsurePositive(int value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(name, value, "The value must be positive.");
            }
        }
    }
}
