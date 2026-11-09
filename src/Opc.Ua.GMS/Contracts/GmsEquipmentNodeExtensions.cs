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
using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS
{
    /// <summary>
    /// Selects native nested declarations through their generated instance factories rather than cloning type nodes.
    /// </summary>
    public static class GmsEquipmentNodeExtensions
    {
        /// <summary>
        /// Selects the exact Double Qualified instance without materializing unspecified optional life thresholds.
        /// The containing runtime must supply its remaining-time sample and IsCountingUp=false observation.
        /// </summary>
        public static SensorState AddQualified(this SensorState sensor, ISystemContext context)
        {
            if (sensor == null)
            {
                throw new ArgumentNullException(nameof(sensor));
            }
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            BaseObjectState parent = sensor.ToolLife ?? throw new ServiceResultException(
                StatusCodes.BadTypeDefinitionInvalid, "A native sensor must contain ToolLife.");
            var name = new QualifiedName(BrowseNames.Qualified,
                (ushort)context.NamespaceUris.GetIndex(GmsModel.NamespaceUri));
            BaseInstanceState? existing = parent.FindChild(context, name);
            if (existing != null)
            {
                if (existing is not ToolLifeState<double>)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                        "Qualified must retain its generated Double ToolLife declaration.");
                }
                return sensor;
            }
            ToolLifeState<double> life = context.CreateSensorType_ToolLife_Qualified(parent, forInstance: true);
            parent.AddChild(life);
            NodeId previous = context.AssignInstanceNodeId(life);
            context.AssignInstanceChildNodeIds(life, previous);
            return sensor;
        }
    }
}
