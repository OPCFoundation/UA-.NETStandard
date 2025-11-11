/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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

#pragma warning disable 1591

namespace Opc.Ua.Types
{
#if !INTERNAL
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public
#else
    internal
#endif
        static class DataTypeIds
    {
        public static readonly NodeId BaseDataType = new(DataTypes.BaseDataType);

        public static readonly NodeId Number = new(DataTypes.Number);

        public static readonly NodeId Integer = new(DataTypes.Integer);

        public static readonly NodeId UInteger = new(DataTypes.UInteger);

        public static readonly NodeId Enumeration = new(DataTypes.Enumeration);

        public static readonly NodeId Boolean = new(DataTypes.Boolean);

        public static readonly NodeId SByte = new(DataTypes.SByte);

        public static readonly NodeId Byte = new(DataTypes.Byte);

        public static readonly NodeId Int16 = new(DataTypes.Int16);

        public static readonly NodeId UInt16 = new(DataTypes.UInt16);

        public static readonly NodeId Int32 = new(DataTypes.Int32);

        public static readonly NodeId UInt32 = new(DataTypes.UInt32);

        public static readonly NodeId Int64 = new(DataTypes.Int64);

        public static readonly NodeId UInt64 = new(DataTypes.UInt64);

        public static readonly NodeId Float = new(DataTypes.Float);

        public static readonly NodeId Double = new(DataTypes.Double);

        public static readonly NodeId String = new(DataTypes.String);

        public static readonly NodeId DateTime = new(DataTypes.DateTime);

        public static readonly NodeId Guid = new(DataTypes.Guid);

        public static readonly NodeId ByteString = new(DataTypes.ByteString);

        public static readonly NodeId XmlElement = new(DataTypes.XmlElement);

        public static readonly NodeId NodeId = new(DataTypes.NodeId);

        public static readonly NodeId ExpandedNodeId = new(DataTypes.ExpandedNodeId);

        public static readonly NodeId StatusCode = new(DataTypes.StatusCode);

        public static readonly NodeId QualifiedName = new(DataTypes.QualifiedName);

        public static readonly NodeId LocalizedText = new(DataTypes.LocalizedText);

        public static readonly NodeId Structure = new(DataTypes.Structure);

        public static readonly NodeId DataValue = new(DataTypes.DataValue);

        public static readonly NodeId DiagnosticInfo = new(DataTypes.DiagnosticInfo);

        public static readonly NodeId RolePermissionType = new(DataTypes.RolePermissionType);

        public static readonly NodeId DataTypeDefinition = new(DataTypes.DataTypeDefinition);

        public static readonly NodeId StructureField = new(DataTypes.StructureField);

        public static readonly NodeId StructureDefinition = new(DataTypes.StructureDefinition);

        public static readonly NodeId EnumDefinition = new(DataTypes.EnumDefinition);

        public static readonly NodeId Node = new(DataTypes.Node);

        public static readonly NodeId InstanceNode = new(DataTypes.InstanceNode);

        public static readonly NodeId TypeNode = new(DataTypes.TypeNode);

        public static readonly NodeId ObjectNode = new(DataTypes.ObjectNode);

        public static readonly NodeId ObjectTypeNode = new(DataTypes.ObjectTypeNode);

        public static readonly NodeId VariableNode = new(DataTypes.VariableNode);

        public static readonly NodeId VariableTypeNode = new(DataTypes.VariableTypeNode);

        public static readonly NodeId ReferenceTypeNode = new(DataTypes.ReferenceTypeNode);

        public static readonly NodeId MethodNode = new(DataTypes.MethodNode);

        public static readonly NodeId ViewNode = new(DataTypes.ViewNode);

        public static readonly NodeId DataTypeNode = new(DataTypes.DataTypeNode);

        public static readonly NodeId ReferenceNode = new(DataTypes.ReferenceNode);

        public static readonly NodeId Argument = new(DataTypes.Argument);

        public static readonly NodeId EnumValueType = new(DataTypes.EnumValueType);

        public static readonly NodeId EnumField = new(DataTypes.EnumField);

        public static readonly NodeId Duration = new(DataTypes.Duration);

        public static readonly NodeId OptionSet = new(DataTypes.OptionSet);

        public static readonly NodeId ViewDescription = new(DataTypes.ViewDescription);

        public static readonly NodeId BrowseDescription = new(DataTypes.BrowseDescription);

        public static readonly NodeId ReferenceDescription = new(DataTypes.ReferenceDescription);

        public static readonly NodeId RelativePathElement = new(DataTypes.RelativePathElement);

        public static readonly NodeId RelativePath = new(DataTypes.RelativePath);
    }
}
