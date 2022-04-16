/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Opc.Ua;
using Opc.Ua.Server;


namespace Alarms
{
    /// <summary>
    /// Helper class to allow for creation of various entities
    /// </summary>

    public class AlarmHelpers
    {
        /// <summary>
        /// Create a mechanism to create a folder
        /// </summary>
        public static FolderState CreateFolder(NodeState parent, ushort nameSpaceIndex, string path, string name )
        {
            FolderState folder = new FolderState(parent);

            folder.SymbolicName = name;
            folder.ReferenceTypeId = ReferenceTypes.Organizes;
            folder.TypeDefinitionId = ObjectTypeIds.FolderType;
            folder.NodeId = new NodeId(path, nameSpaceIndex);
            folder.BrowseName = new QualifiedName(path, nameSpaceIndex);
            folder.DisplayName = new LocalizedText("en", name);
            folder.WriteMask = AttributeWriteMask.None;
            folder.UserWriteMask = AttributeWriteMask.None;
            folder.EventNotifier = EventNotifiers.None;

            if (parent != null)
            {
                parent.AddChild(folder);
            }

            return folder;
        }

        /// <summary>
        /// Create a mechanism to create a variable
        /// </summary>
        public static BaseDataVariableState CreateVariable(NodeState parent, ushort nameSpaceIndex, string path, string name, bool boolValue = false)
        {
            uint dataTypeIdentifier = Opc.Ua.DataTypes.Int32;
            if ( boolValue )
            {
                dataTypeIdentifier = Opc.Ua.DataTypes.Boolean;
            }
            return CreateVariable(parent, nameSpaceIndex, path, name, dataTypeIdentifier);
        }

        /// <summary>
        /// Create a mechanism to create a Variable
        /// </summary>
        public static BaseDataVariableState CreateVariable(NodeState parent, ushort nameSpaceIndex, string path, string name, uint dataTypeIdentifier)
        {
            BaseDataVariableState variable = new BaseDataVariableState(parent);

            variable.SymbolicName = name;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.TypeDefinitionId = VariableTypeIds.BaseDataVariableType;
            variable.NodeId = new NodeId(path, nameSpaceIndex);
            variable.BrowseName = new QualifiedName(name, nameSpaceIndex);
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;
            variable.UserWriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;
            switch(dataTypeIdentifier)
            {
                case Opc.Ua.DataTypes.Boolean:
                    variable.DataType = DataTypeIds.Boolean;
                    variable.Value = false;
                    break;
                case Opc.Ua.DataTypes.Int32:
                    variable.DataType = DataTypeIds.Int32;
                    variable.Value = AlarmDefines.NORMAL_START_VALUE;
                    break;
                case Opc.Ua.DataTypes.Double:
                    variable.DataType = DataTypeIds.Int32;
                    variable.Value = (double)AlarmDefines.NORMAL_START_VALUE;
                    break;

            }
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;

            if (parent != null)
            {
                parent.AddChild(variable);
            }

            return variable;
        }

        /// <summary>
        /// Create a mechanism to create a method
        /// </summary>
        public static MethodState CreateMethod(NodeState parent, ushort nameSpaceIndex, string path, string name)
        {
            MethodState method = new MethodState(parent);

            method.SymbolicName = name;
            method.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            method.NodeId = new NodeId(path, nameSpaceIndex);
            method.BrowseName = new QualifiedName(path, nameSpaceIndex);
            method.DisplayName = new LocalizedText("en", name);
            method.WriteMask = AttributeWriteMask.None;
            method.UserWriteMask = AttributeWriteMask.None;
            method.Executable = true;
            method.UserExecutable = true;

            if (parent != null)
            {
                parent.AddChild(method);
            }

            return method;
        }


    }
}
