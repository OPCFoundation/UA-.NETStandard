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

using Opc.Ua.GMS;
using Opc.Ua.Machinery.Server;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Adds the real generated GMS model before the shared runtime fixture establishes namespace ownership.
    /// </summary>
    internal sealed class MachineToolEquipmentModelProvider : IMachineryModelProvider
    {
        /// <inheritdoc/>
        public int Order => 200;

        /// <inheritdoc/>
        public ArrayOf<string> NamespaceUris => GmsModel.GetNamespaceUris();

        /// <inheritdoc/>
        public void AddPredefinedNodes(NodeStateCollection nodes, ISystemContext context)
        {
            nodes.AddGmsTypeSystem(context);
            context.EncodeableFactory.Builder.AddGmsTypeSystem().Commit();
        }
    }
}
