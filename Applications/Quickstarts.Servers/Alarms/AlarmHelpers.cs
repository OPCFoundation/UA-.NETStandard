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
using Opc.Ua;

namespace Alarms
{
    /// <summary>
    /// Helper class to allow for creation of various entities
    /// </summary>
    public static class AlarmHelpers
    {
        /// <summary>
        /// Create a mechanism to create a folder
        /// </summary>
        public static FolderState CreateFolder(NodeState parent, ushort nameSpaceIndex, string path, string name)
        {
            var folder = new FolderState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId(path, nameSpaceIndex),
                BrowseName = new QualifiedName(path, nameSpaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None,
            };

            parent?.AddChild(folder);

            return folder;
        }

        /// <summary>
        /// Create a mechanism to create a variable
        /// </summary>
        public static BaseDataVariableState CreateVariable(
            NodeState parent,
            ushort nameSpaceIndex,
            string path,
            string name,
            bool boolValue = false
        )
        {
            uint dataTypeIdentifier = DataTypes.Int32;
            if (boolValue)
            {
                dataTypeIdentifier = DataTypes.Boolean;
            }
            return CreateVariable(parent, nameSpaceIndex, path, name, dataTypeIdentifier);
        }

        /// <summary>
        /// Create a mechanism to create a Variable
        /// </summary>
        public static BaseDataVariableState CreateVariable(
            NodeState parent,
            ushort nameSpaceIndex,
            string path,
            string name,
            uint dataTypeIdentifier
        )
        {
            var variable = new BaseDataVariableState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                NodeId = new NodeId(path, nameSpaceIndex),
                BrowseName = new QualifiedName(name, nameSpaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description,
                UserWriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description,
            };
            switch (dataTypeIdentifier)
            {
                case DataTypes.Boolean:
                    variable.DataType = DataTypeIds.Boolean;
                    variable.Value = false;
                    break;
                case DataTypes.Int32:
                    variable.DataType = DataTypeIds.Int32;
                    variable.Value = AlarmDefines.NORMAL_START_VALUE;
                    break;
                case DataTypes.Double:
                    variable.DataType = DataTypeIds.Double;
                    variable.Value = (double)AlarmDefines.NORMAL_START_VALUE;
                    break;
            }
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;

            parent?.AddChild(variable);

            return variable;
        }

        /// <summary>
        /// Create a mechanism to create a method
        /// </summary>
        public static MethodState CreateMethod(NodeState parent, ushort nameSpaceIndex, string path, string name)
        {
            var method = new MethodState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = new NodeId(path, nameSpaceIndex),
                BrowseName = new QualifiedName(path, nameSpaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                Executable = true,
                UserExecutable = true,
            };

            parent?.AddChild(method);

            return method;
        }

        /// <summary>
        /// Add the input parameter description for a Start method.
        /// </summary>
        public static void AddStartInputParameters(MethodState startMethod, ushort namespaceIndex)
        {
            // set input arguments
            startMethod.InputArguments = new PropertyState<Argument[]>(startMethod)
            {
                NodeId = new NodeId(startMethod.BrowseName.Name + "InArgs", namespaceIndex),
                BrowseName = BrowseNames.InputArguments,
            };
            startMethod.InputArguments.DisplayName = startMethod.InputArguments.BrowseName.Name;
            startMethod.InputArguments.TypeDefinitionId = VariableTypeIds.PropertyType;
            startMethod.InputArguments.ReferenceTypeId = ReferenceTypeIds.HasProperty;
            startMethod.InputArguments.DataType = DataTypeIds.Argument;
            startMethod.InputArguments.ValueRank = ValueRanks.OneDimension;

            startMethod.InputArguments.Value =
            [
                new Argument()
                {
                    Name = "UInt32 value",
                    Description = "Runtime of Alarms in seconds.",
                    DataType = DataTypeIds.UInt32,
                    ValueRank = ValueRanks.Scalar,
                },
            ];
        }
    }
}
