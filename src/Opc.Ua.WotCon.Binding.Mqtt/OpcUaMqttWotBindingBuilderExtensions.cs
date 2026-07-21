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
using Opc.Ua;
using Opc.Ua.WotCon.Binding.Mqtt;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// <see cref="IOpcUaBuilder"/> extensions that register the MQTT WoT binding
    /// executor alongside the shipped planner binders.
    /// </summary>
    public static class OpcUaMqttWotBindingBuilderExtensions
    {
        /// <summary>
        /// Registers the eight shipped planner binders and the MQTT executor, so
        /// MQTT binding forms are validated, compiled and executable.
        /// </summary>
        public static IOpcUaBuilder AddMqttWotBinding(
            this IOpcUaBuilder builder, Action<MqttWotBindingOptions>? configure = null)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            var options = new MqttWotBindingOptions();
            configure?.Invoke(options);
            return builder
                .AddWotProtocolBinders()
                .AddWotBindingExecutor(new MqttWotBindingExecutor(options));
        }
    }
}
