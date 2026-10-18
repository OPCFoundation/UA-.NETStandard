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

namespace Opc.Ua.Vision.MachineVision
{
    /// <summary>
    /// Registers the OPC 40100-1 Machine Vision model and its generated factories.
    /// </summary>
    public static class OpcUaMachineVisionExtensions
    {
        /// <summary>
        /// Adds the generated Machine Vision model nodes to a collection.
        /// </summary>
        public static NodeStateCollection AddOpcUaMachineVision(
            this NodeStateCollection nodes,
            ISystemContext context)
        {
            return OpcUaVisionMachineVisionExtensions.AddOpcUaVisionMachineVision(
                nodes ?? throw new ArgumentNullException(nameof(nodes)),
                context ?? throw new ArgumentNullException(nameof(context)));
        }

        /// <summary>
        /// Registers the generated Machine Vision encodeable factories.
        /// </summary>
        public static IEncodeableFactoryBuilder AddOpcUaMachineVision(this IEncodeableFactoryBuilder builder)
        {
            return OpcUaVisionMachineVisionExtensions.AddOpcUaVisionMachineVision(
                builder ?? throw new ArgumentNullException(nameof(builder)));
        }

        /// <summary>
        /// Registers the generated Machine Vision node state factories.
        /// </summary>
        public static INodeStateFactoryBuilder AddOpcUaMachineVision(this INodeStateFactoryBuilder builder)
        {
            return OpcUaVisionMachineVisionExtensions.AddOpcUaVisionMachineVision(
                builder ?? throw new ArgumentNullException(nameof(builder)));
        }
    }
}
