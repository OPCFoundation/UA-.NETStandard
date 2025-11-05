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

using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using System.Xml;
using System.Runtime.Serialization;

#pragma warning disable 1591

namespace Opc.Ua
{
    internal static partial class DataTypes
    {
        public const uint BaseDataType = 24;

        public const uint Number = 26;

        public const uint Integer = 27;

        public const uint UInteger = 28;

        public const uint Enumeration = 29;

        public const uint Boolean = 1;

        public const uint SByte = 2;

        public const uint Byte = 3;

        public const uint Int16 = 4;

        public const uint UInt16 = 5;

        public const uint Int32 = 6;

        public const uint UInt32 = 7;

        public const uint Int64 = 8;

        public const uint UInt64 = 9;

        public const uint Float = 10;

        public const uint Double = 11;

        public const uint String = 12;

        public const uint DateTime = 13;

        public const uint Guid = 14;

        public const uint ByteString = 15;

        public const uint XmlElement = 16;

        public const uint NodeId = 17;

        public const uint ExpandedNodeId = 18;

        public const uint StatusCode = 19;

        public const uint QualifiedName = 20;

        public const uint LocalizedText = 21;

        public const uint Structure = 22;

        public const uint DataValue = 23;

        public const uint DiagnosticInfo = 25;

        public const uint Union = 12756;

        public const uint IdType = 256;

        public const uint NodeClass = 257;

        public const uint PermissionType = 94;

        public const uint AccessRestrictionType = 95;

        public const uint RolePermissionType = 96;

        public const uint DataTypeDefinition = 97;

        public const uint StructureType = 98;

        public const uint StructureField = 101;

        public const uint StructureDefinition = 99;

        public const uint EnumDefinition = 100;

        public const uint Node = 258;

        public const uint InstanceNode = 11879;

        public const uint TypeNode = 11880;

        public const uint ObjectNode = 261;

        public const uint ObjectTypeNode = 264;

        public const uint VariableNode = 267;

        public const uint VariableTypeNode = 270;

        public const uint ReferenceTypeNode = 273;

        public const uint MethodNode = 276;

        public const uint ViewNode = 279;

        public const uint DataTypeNode = 282;

        public const uint ReferenceNode = 285;

        public const uint Argument = 296;

        public const uint EnumValueType = 7594;

        public const uint EnumField = 102;

        public const uint OptionSet = 12755;

        public const uint Duration = 290;

        public const uint UtcTime = 294;

        public const uint IntegerId = 288;

        public const uint ViewDescription = 511;

        public const uint BrowseDescription = 514;

        public const uint RelativePathElement = 537;

        public const uint RelativePath = 540;

        public const uint Counter = 289;
    }

    internal static partial class Objects
    {

        public const uint ModellingRule_Mandatory = 78;

        public const uint ModellingRule_Optional = 80;

        public const uint ModellingRule_ExposesItsArray = 83;

        public const uint ModellingRule_OptionalPlaceholder = 11508;

        public const uint ModellingRule_MandatoryPlaceholder = 11510;

        public const uint XmlSchema_TypeSystem = 92;

        public const uint OPCBinarySchema_TypeSystem = 93;

        public const uint WellKnownRole_Anonymous = 15644;

        public const uint RolePermissionType_Encoding_DefaultBinary = 128;

        public const uint DataTypeDefinition_Encoding_DefaultBinary = 121;

        public const uint StructureField_Encoding_DefaultBinary = 14844;

        public const uint StructureDefinition_Encoding_DefaultBinary = 122;

        public const uint EnumDefinition_Encoding_DefaultBinary = 123;

        public const uint Node_Encoding_DefaultBinary = 260;

        public const uint InstanceNode_Encoding_DefaultBinary = 11889;

        public const uint TypeNode_Encoding_DefaultBinary = 11890;

        public const uint ObjectNode_Encoding_DefaultBinary = 263;

        public const uint ObjectTypeNode_Encoding_DefaultBinary = 266;

        public const uint VariableNode_Encoding_DefaultBinary = 269;

        public const uint VariableTypeNode_Encoding_DefaultBinary = 272;

        public const uint ReferenceTypeNode_Encoding_DefaultBinary = 275;

        public const uint MethodNode_Encoding_DefaultBinary = 278;

        public const uint ViewNode_Encoding_DefaultBinary = 281;

        public const uint DataTypeNode_Encoding_DefaultBinary = 284;

        public const uint ReferenceNode_Encoding_DefaultBinary = 287;

        public const uint Argument_Encoding_DefaultBinary = 298;

        public const uint EnumValueType_Encoding_DefaultBinary = 8251;

        public const uint EnumField_Encoding_DefaultBinary = 14845;

        public const uint ViewDescription_Encoding_DefaultBinary = 513;

        public const uint BrowseDescription_Encoding_DefaultBinary = 516;

        public const uint RelativePathElement_Encoding_DefaultBinary = 539;

        public const uint RelativePath_Encoding_DefaultBinary = 542;

        public const uint RolePermissionType_Encoding_DefaultXml = 16126;

        public const uint DataTypeDefinition_Encoding_DefaultXml = 14797;

        public const uint StructureField_Encoding_DefaultXml = 14800;

        public const uint StructureDefinition_Encoding_DefaultXml = 14798;

        public const uint EnumDefinition_Encoding_DefaultXml = 14799;

        public const uint Node_Encoding_DefaultXml = 259;

        public const uint InstanceNode_Encoding_DefaultXml = 11887;

        public const uint TypeNode_Encoding_DefaultXml = 11888;

        public const uint ObjectNode_Encoding_DefaultXml = 262;

        public const uint ObjectTypeNode_Encoding_DefaultXml = 265;

        public const uint VariableNode_Encoding_DefaultXml = 268;

        public const uint VariableTypeNode_Encoding_DefaultXml = 271;

        public const uint ReferenceTypeNode_Encoding_DefaultXml = 274;

        public const uint MethodNode_Encoding_DefaultXml = 277;

        public const uint ViewNode_Encoding_DefaultXml = 280;

        public const uint DataTypeNode_Encoding_DefaultXml = 283;

        public const uint ReferenceNode_Encoding_DefaultXml = 286;

        public const uint Argument_Encoding_DefaultXml = 297;

        public const uint EnumValueType_Encoding_DefaultXml = 7616;

        public const uint EnumField_Encoding_DefaultXml = 14801;

        public const uint ViewDescription_Encoding_DefaultXml = 512;

        public const uint BrowseDescription_Encoding_DefaultXml = 515;

        public const uint RelativePathElement_Encoding_DefaultXml = 538;

        public const uint RelativePath_Encoding_DefaultXml = 541;

        public const uint RolePermissionType_Encoding_DefaultJson = 15062;

        public const uint DataTypeDefinition_Encoding_DefaultJson = 15063;

        public const uint StructureField_Encoding_DefaultJson = 15065;

        public const uint StructureDefinition_Encoding_DefaultJson = 15066;

        public const uint EnumDefinition_Encoding_DefaultJson = 15067;

        public const uint Node_Encoding_DefaultJson = 15068;

        public const uint InstanceNode_Encoding_DefaultJson = 15069;

        public const uint TypeNode_Encoding_DefaultJson = 15070;

        public const uint ObjectNode_Encoding_DefaultJson = 15071;

        public const uint ObjectTypeNode_Encoding_DefaultJson = 15073;

        public const uint VariableNode_Encoding_DefaultJson = 15074;

        public const uint VariableTypeNode_Encoding_DefaultJson = 15075;

        public const uint ReferenceTypeNode_Encoding_DefaultJson = 15076;

        public const uint MethodNode_Encoding_DefaultJson = 15077;

        public const uint ViewNode_Encoding_DefaultJson = 15078;

        public const uint DataTypeNode_Encoding_DefaultJson = 15079;

        public const uint ReferenceNode_Encoding_DefaultJson = 15080;

        public const uint Argument_Encoding_DefaultJson = 15081;

        public const uint EnumValueType_Encoding_DefaultJson = 15082;

        public const uint EnumField_Encoding_DefaultJson = 15083;

        public const uint ViewDescription_Encoding_DefaultJson = 15179;

        public const uint BrowseDescription_Encoding_DefaultJson = 15180;

        public const uint RelativePathElement_Encoding_DefaultJson = 15188;

        public const uint RelativePath_Encoding_DefaultJson = 15189;
    }

    internal static partial class ObjectTypes
    {
        public const uint BaseObjectType = 58;

        public const uint DataTypeEncodingType = 76;
    }

    internal static partial class ReferenceTypes
    {
        public const uint References = 31;

        public const uint NonHierarchicalReferences = 32;

        public const uint HierarchicalReferences = 33;

        public const uint Organizes = 35;

        public const uint HasEventSource = 36;

        public const uint HasModellingRule = 37;

        public const uint HasEncoding = 38;

        public const uint HasDescription = 39;

        public const uint HasTypeDefinition = 40;

        public const uint GeneratesEvent = 41;

        public const uint AlwaysGeneratesEvent = 3065;

        public const uint Aggregates = 44;

        public const uint HasSubtype = 45;

        public const uint HasProperty = 46;

        public const uint HasComponent = 47;

        public const uint HasNotifier = 48;

        public const uint HasOrderedComponent = 49;

        public const uint FromState = 51;

        public const uint ToState = 52;

        public const uint HasCause = 53;

        public const uint HasEffect = 54;

        public const uint HasGuard = 15112;

        public const uint HasDictionaryEntry = 17597;

        public const uint HasInterface = 17603;

        public const uint HasAddIn = 17604;

        public const uint HasTrueSubState = 9004;

        public const uint HasFalseSubState = 9005;

        public const uint HasAlarmSuppressionGroup = 16361;

        public const uint AlarmGroupMember = 16362;

        public const uint AlarmSuppressionGroupMember = 32059;

        public const uint HasCondition = 9006;
    }

    internal static partial class VariableTypes
    {
        public const uint BaseVariableType = 62;

        public const uint BaseDataVariableType = 63;

        public const uint PropertyType = 68;

        public const uint DataTypeDictionaryType = 72;
    }

    internal static partial class DataTypeIds
    {
        public static readonly NodeId BaseDataType = new NodeId(Opc.Ua.DataTypes.BaseDataType);

        public static readonly NodeId Number = new NodeId(Opc.Ua.DataTypes.Number);

        public static readonly NodeId Integer = new NodeId(Opc.Ua.DataTypes.Integer);

        public static readonly NodeId UInteger = new NodeId(Opc.Ua.DataTypes.UInteger);

        public static readonly NodeId Enumeration = new NodeId(Opc.Ua.DataTypes.Enumeration);

        public static readonly NodeId Boolean = new NodeId(Opc.Ua.DataTypes.Boolean);

        public static readonly NodeId SByte = new NodeId(Opc.Ua.DataTypes.SByte);

        public static readonly NodeId Byte = new NodeId(Opc.Ua.DataTypes.Byte);

        public static readonly NodeId Int16 = new NodeId(Opc.Ua.DataTypes.Int16);

        public static readonly NodeId UInt16 = new NodeId(Opc.Ua.DataTypes.UInt16);

        public static readonly NodeId Int32 = new NodeId(Opc.Ua.DataTypes.Int32);

        public static readonly NodeId UInt32 = new NodeId(Opc.Ua.DataTypes.UInt32);

        public static readonly NodeId Int64 = new NodeId(Opc.Ua.DataTypes.Int64);

        public static readonly NodeId UInt64 = new NodeId(Opc.Ua.DataTypes.UInt64);

        public static readonly NodeId Float = new NodeId(Opc.Ua.DataTypes.Float);

        public static readonly NodeId Double = new NodeId(Opc.Ua.DataTypes.Double);

        public static readonly NodeId String = new NodeId(Opc.Ua.DataTypes.String);

        public static readonly NodeId DateTime = new NodeId(Opc.Ua.DataTypes.DateTime);

        public static readonly NodeId Guid = new NodeId(Opc.Ua.DataTypes.Guid);

        public static readonly NodeId ByteString = new NodeId(Opc.Ua.DataTypes.ByteString);

        public static readonly NodeId XmlElement = new NodeId(Opc.Ua.DataTypes.XmlElement);

        public static readonly NodeId NodeId = new NodeId(Opc.Ua.DataTypes.NodeId);

        public static readonly NodeId ExpandedNodeId = new NodeId(Opc.Ua.DataTypes.ExpandedNodeId);

        public static readonly NodeId StatusCode = new NodeId(Opc.Ua.DataTypes.StatusCode);

        public static readonly NodeId QualifiedName = new NodeId(Opc.Ua.DataTypes.QualifiedName);

        public static readonly NodeId LocalizedText = new NodeId(Opc.Ua.DataTypes.LocalizedText);

        public static readonly NodeId Structure = new NodeId(Opc.Ua.DataTypes.Structure);

        public static readonly NodeId DataValue = new NodeId(Opc.Ua.DataTypes.DataValue);

        public static readonly NodeId DiagnosticInfo = new NodeId(Opc.Ua.DataTypes.DiagnosticInfo);

        public static readonly NodeId RolePermissionType = new NodeId(Opc.Ua.DataTypes.RolePermissionType);

        public static readonly NodeId DataTypeDefinition = new NodeId(Opc.Ua.DataTypes.DataTypeDefinition);

        public static readonly NodeId StructureField = new NodeId(Opc.Ua.DataTypes.StructureField);

        public static readonly NodeId StructureDefinition = new NodeId(Opc.Ua.DataTypes.StructureDefinition);

        public static readonly NodeId EnumDefinition = new NodeId(Opc.Ua.DataTypes.EnumDefinition);

        public static readonly NodeId Node = new NodeId(Opc.Ua.DataTypes.Node);

        public static readonly NodeId InstanceNode = new NodeId(Opc.Ua.DataTypes.InstanceNode);

        public static readonly NodeId TypeNode = new NodeId(Opc.Ua.DataTypes.TypeNode);

        public static readonly NodeId ObjectNode = new NodeId(Opc.Ua.DataTypes.ObjectNode);

        public static readonly NodeId ObjectTypeNode = new NodeId(Opc.Ua.DataTypes.ObjectTypeNode);

        public static readonly NodeId VariableNode = new NodeId(Opc.Ua.DataTypes.VariableNode);

        public static readonly NodeId VariableTypeNode = new NodeId(Opc.Ua.DataTypes.VariableTypeNode);

        public static readonly NodeId ReferenceTypeNode = new NodeId(Opc.Ua.DataTypes.ReferenceTypeNode);

        public static readonly NodeId MethodNode = new NodeId(Opc.Ua.DataTypes.MethodNode);

        public static readonly NodeId ViewNode = new NodeId(Opc.Ua.DataTypes.ViewNode);

        public static readonly NodeId DataTypeNode = new NodeId(Opc.Ua.DataTypes.DataTypeNode);

        public static readonly NodeId ReferenceNode = new NodeId(Opc.Ua.DataTypes.ReferenceNode);

        public static readonly NodeId Argument = new NodeId(Opc.Ua.DataTypes.Argument);

        public static readonly NodeId EnumValueType = new NodeId(Opc.Ua.DataTypes.EnumValueType);

        public static readonly NodeId EnumField = new NodeId(Opc.Ua.DataTypes.EnumField);

        public static readonly NodeId OptionSet = new NodeId(Opc.Ua.DataTypes.OptionSet);

        public static readonly NodeId ViewDescription = new NodeId(Opc.Ua.DataTypes.ViewDescription);

        public static readonly NodeId BrowseDescription = new NodeId(Opc.Ua.DataTypes.BrowseDescription);

        public static readonly NodeId RelativePathElement = new NodeId(Opc.Ua.DataTypes.RelativePathElement);

        public static readonly NodeId RelativePath = new NodeId(Opc.Ua.DataTypes.RelativePath);
    }

    internal static partial class ObjectIds
    {

        public static readonly NodeId ModellingRule_Mandatory = new NodeId(Opc.Ua.Objects.ModellingRule_Mandatory);

        public static readonly NodeId ModellingRule_Optional = new NodeId(Opc.Ua.Objects.ModellingRule_Optional);

        public static readonly NodeId ModellingRule_ExposesItsArray = new NodeId(Opc.Ua.Objects.ModellingRule_ExposesItsArray);

        public static readonly NodeId ModellingRule_OptionalPlaceholder = new NodeId(Opc.Ua.Objects.ModellingRule_OptionalPlaceholder);

        public static readonly NodeId ModellingRule_MandatoryPlaceholder = new NodeId(Opc.Ua.Objects.ModellingRule_MandatoryPlaceholder);

        public static readonly NodeId XmlSchema_TypeSystem = new NodeId(Opc.Ua.Objects.XmlSchema_TypeSystem);

        public static readonly NodeId OPCBinarySchema_TypeSystem = new NodeId(Opc.Ua.Objects.OPCBinarySchema_TypeSystem);

        public static readonly NodeId RolePermissionType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RolePermissionType_Encoding_DefaultBinary);

        public static readonly NodeId DataTypeDefinition_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataTypeDefinition_Encoding_DefaultBinary);

        public static readonly NodeId StructureField_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.StructureField_Encoding_DefaultBinary);

        public static readonly NodeId StructureDefinition_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.StructureDefinition_Encoding_DefaultBinary);

        public static readonly NodeId EnumDefinition_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EnumDefinition_Encoding_DefaultBinary);

        public static readonly NodeId Node_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.Node_Encoding_DefaultBinary);

        public static readonly NodeId InstanceNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.InstanceNode_Encoding_DefaultBinary);

        public static readonly NodeId TypeNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.TypeNode_Encoding_DefaultBinary);

        public static readonly NodeId ObjectNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ObjectNode_Encoding_DefaultBinary);

        public static readonly NodeId ObjectTypeNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ObjectTypeNode_Encoding_DefaultBinary);

        public static readonly NodeId VariableNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.VariableNode_Encoding_DefaultBinary);

        public static readonly NodeId VariableTypeNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.VariableTypeNode_Encoding_DefaultBinary);

        public static readonly NodeId ReferenceTypeNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReferenceTypeNode_Encoding_DefaultBinary);

        public static readonly NodeId MethodNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.MethodNode_Encoding_DefaultBinary);

        public static readonly NodeId ViewNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ViewNode_Encoding_DefaultBinary);

        public static readonly NodeId DataTypeNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.DataTypeNode_Encoding_DefaultBinary);

        public static readonly NodeId ReferenceNode_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ReferenceNode_Encoding_DefaultBinary);

        public static readonly NodeId Argument_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.Argument_Encoding_DefaultBinary);

        public static readonly NodeId EnumValueType_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EnumValueType_Encoding_DefaultBinary);

        public static readonly NodeId EnumField_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.EnumField_Encoding_DefaultBinary);

        public static readonly NodeId ViewDescription_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.ViewDescription_Encoding_DefaultBinary);

        public static readonly NodeId BrowseDescription_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.BrowseDescription_Encoding_DefaultBinary);

        public static readonly NodeId RelativePathElement_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RelativePathElement_Encoding_DefaultBinary);

        public static readonly NodeId RelativePath_Encoding_DefaultBinary = new NodeId(Opc.Ua.Objects.RelativePath_Encoding_DefaultBinary);

        public static readonly NodeId RolePermissionType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RolePermissionType_Encoding_DefaultXml);

        public static readonly NodeId DataTypeDefinition_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataTypeDefinition_Encoding_DefaultXml);

        public static readonly NodeId StructureField_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.StructureField_Encoding_DefaultXml);

        public static readonly NodeId StructureDefinition_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.StructureDefinition_Encoding_DefaultXml);

        public static readonly NodeId EnumDefinition_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EnumDefinition_Encoding_DefaultXml);

        public static readonly NodeId Node_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.Node_Encoding_DefaultXml);

        public static readonly NodeId InstanceNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.InstanceNode_Encoding_DefaultXml);

        public static readonly NodeId TypeNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.TypeNode_Encoding_DefaultXml);

        public static readonly NodeId ObjectNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ObjectNode_Encoding_DefaultXml);

        public static readonly NodeId ObjectTypeNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ObjectTypeNode_Encoding_DefaultXml);

        public static readonly NodeId VariableNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.VariableNode_Encoding_DefaultXml);

        public static readonly NodeId VariableTypeNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.VariableTypeNode_Encoding_DefaultXml);

        public static readonly NodeId ReferenceTypeNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReferenceTypeNode_Encoding_DefaultXml);

        public static readonly NodeId MethodNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.MethodNode_Encoding_DefaultXml);

        public static readonly NodeId ViewNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ViewNode_Encoding_DefaultXml);

        public static readonly NodeId DataTypeNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.DataTypeNode_Encoding_DefaultXml);

        public static readonly NodeId ReferenceNode_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ReferenceNode_Encoding_DefaultXml);

        public static readonly NodeId Argument_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.Argument_Encoding_DefaultXml);

        public static readonly NodeId EnumValueType_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EnumValueType_Encoding_DefaultXml);

        public static readonly NodeId EnumField_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.EnumField_Encoding_DefaultXml);

        public static readonly NodeId ViewDescription_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.ViewDescription_Encoding_DefaultXml);

        public static readonly NodeId BrowseDescription_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.BrowseDescription_Encoding_DefaultXml);

        public static readonly NodeId RelativePathElement_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RelativePathElement_Encoding_DefaultXml);

        public static readonly NodeId RelativePath_Encoding_DefaultXml = new NodeId(Opc.Ua.Objects.RelativePath_Encoding_DefaultXml);

        public static readonly NodeId RolePermissionType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RolePermissionType_Encoding_DefaultJson);

        public static readonly NodeId DataTypeDefinition_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataTypeDefinition_Encoding_DefaultJson);

        public static readonly NodeId StructureField_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.StructureField_Encoding_DefaultJson);

        public static readonly NodeId StructureDefinition_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.StructureDefinition_Encoding_DefaultJson);

        public static readonly NodeId EnumDefinition_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EnumDefinition_Encoding_DefaultJson);

        public static readonly NodeId Node_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.Node_Encoding_DefaultJson);

        public static readonly NodeId InstanceNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.InstanceNode_Encoding_DefaultJson);

        public static readonly NodeId TypeNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.TypeNode_Encoding_DefaultJson);

        public static readonly NodeId ObjectNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ObjectNode_Encoding_DefaultJson);

        public static readonly NodeId ObjectTypeNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ObjectTypeNode_Encoding_DefaultJson);

        public static readonly NodeId VariableNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.VariableNode_Encoding_DefaultJson);

        public static readonly NodeId VariableTypeNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.VariableTypeNode_Encoding_DefaultJson);

        public static readonly NodeId ReferenceTypeNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReferenceTypeNode_Encoding_DefaultJson);

        public static readonly NodeId MethodNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.MethodNode_Encoding_DefaultJson);

        public static readonly NodeId ViewNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ViewNode_Encoding_DefaultJson);

        public static readonly NodeId DataTypeNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.DataTypeNode_Encoding_DefaultJson);

        public static readonly NodeId ReferenceNode_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ReferenceNode_Encoding_DefaultJson);

        public static readonly NodeId Argument_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.Argument_Encoding_DefaultJson);

        public static readonly NodeId EnumValueType_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EnumValueType_Encoding_DefaultJson);

        public static readonly NodeId EnumField_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.EnumField_Encoding_DefaultJson);

        public static readonly NodeId ViewDescription_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.ViewDescription_Encoding_DefaultJson);

        public static readonly NodeId BrowseDescription_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.BrowseDescription_Encoding_DefaultJson);

        public static readonly NodeId RelativePathElement_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RelativePathElement_Encoding_DefaultJson);

        public static readonly NodeId RelativePath_Encoding_DefaultJson = new NodeId(Opc.Ua.Objects.RelativePath_Encoding_DefaultJson);
    }

    internal static partial class ObjectTypeIds
    {
        public static readonly NodeId BaseObjectType = new NodeId(Opc.Ua.ObjectTypes.BaseObjectType);

        public static readonly NodeId DataTypeEncodingType = new NodeId(Opc.Ua.ObjectTypes.DataTypeEncodingType);
    }

    internal static partial class ReferenceTypeIds
    {
        public static readonly NodeId References = new NodeId(Opc.Ua.ReferenceTypes.References);

        public static readonly NodeId NonHierarchicalReferences = new NodeId(Opc.Ua.ReferenceTypes.NonHierarchicalReferences);

        public static readonly NodeId HierarchicalReferences = new NodeId(Opc.Ua.ReferenceTypes.HierarchicalReferences);

        public static readonly NodeId Organizes = new NodeId(Opc.Ua.ReferenceTypes.Organizes);

        public static readonly NodeId HasEventSource = new NodeId(Opc.Ua.ReferenceTypes.HasEventSource);

        public static readonly NodeId HasModellingRule = new NodeId(Opc.Ua.ReferenceTypes.HasModellingRule);

        public static readonly NodeId HasEncoding = new NodeId(Opc.Ua.ReferenceTypes.HasEncoding);

        public static readonly NodeId HasDescription = new NodeId(Opc.Ua.ReferenceTypes.HasDescription);

        public static readonly NodeId HasTypeDefinition = new NodeId(Opc.Ua.ReferenceTypes.HasTypeDefinition);

        public static readonly NodeId GeneratesEvent = new NodeId(Opc.Ua.ReferenceTypes.GeneratesEvent);

        public static readonly NodeId AlwaysGeneratesEvent = new NodeId(Opc.Ua.ReferenceTypes.AlwaysGeneratesEvent);

        public static readonly NodeId Aggregates = new NodeId(Opc.Ua.ReferenceTypes.Aggregates);

        public static readonly NodeId HasSubtype = new NodeId(Opc.Ua.ReferenceTypes.HasSubtype);

        public static readonly NodeId HasProperty = new NodeId(Opc.Ua.ReferenceTypes.HasProperty);

        public static readonly NodeId HasComponent = new NodeId(Opc.Ua.ReferenceTypes.HasComponent);

        public static readonly NodeId HasNotifier = new NodeId(Opc.Ua.ReferenceTypes.HasNotifier);

        public static readonly NodeId HasOrderedComponent = new NodeId(Opc.Ua.ReferenceTypes.HasOrderedComponent);

        public static readonly NodeId FromState = new NodeId(Opc.Ua.ReferenceTypes.FromState);

        public static readonly NodeId ToState = new NodeId(Opc.Ua.ReferenceTypes.ToState);

        public static readonly NodeId HasCause = new NodeId(Opc.Ua.ReferenceTypes.HasCause);

        public static readonly NodeId HasEffect = new NodeId(Opc.Ua.ReferenceTypes.HasEffect);

        public static readonly NodeId HasGuard = new NodeId(Opc.Ua.ReferenceTypes.HasGuard);

        public static readonly NodeId HasDictionaryEntry = new NodeId(Opc.Ua.ReferenceTypes.HasDictionaryEntry);

        public static readonly NodeId HasInterface = new NodeId(Opc.Ua.ReferenceTypes.HasInterface);

        public static readonly NodeId HasAddIn = new NodeId(Opc.Ua.ReferenceTypes.HasAddIn);

        public static readonly NodeId HasTrueSubState = new NodeId(Opc.Ua.ReferenceTypes.HasTrueSubState);

        public static readonly NodeId HasFalseSubState = new NodeId(Opc.Ua.ReferenceTypes.HasFalseSubState);

        public static readonly NodeId HasAlarmSuppressionGroup = new NodeId(Opc.Ua.ReferenceTypes.HasAlarmSuppressionGroup);

        public static readonly NodeId AlarmGroupMember = new NodeId(Opc.Ua.ReferenceTypes.AlarmGroupMember);

        public static readonly NodeId AlarmSuppressionGroupMember = new NodeId(Opc.Ua.ReferenceTypes.AlarmSuppressionGroupMember);

        public static readonly NodeId HasCondition = new NodeId(Opc.Ua.ReferenceTypes.HasCondition);
    }

    internal static partial class VariableTypeIds
    {
        public static readonly NodeId BaseVariableType = new NodeId(Opc.Ua.VariableTypes.BaseVariableType);

        public static readonly NodeId BaseDataVariableType = new NodeId(Opc.Ua.VariableTypes.BaseDataVariableType);

        public static readonly NodeId PropertyType = new NodeId(Opc.Ua.VariableTypes.PropertyType);

        public static readonly NodeId DataTypeDictionaryType = new NodeId(Opc.Ua.VariableTypes.DataTypeDictionaryType);
    }

    internal static partial class BrowseNames
    {
        public const string AlarmGroupMember = "AlarmGroupMember";

        public const string AlarmSuppressionGroupMember = "AlarmSuppressionGroupMember";

        public const string AlwaysGeneratesEvent = "AlwaysGeneratesEvent";

        public const string BaseDataType = "BaseDataType";

        public const string BaseDataVariableType = "BaseDataVariableType";

        public const string BaseObjectType = "BaseObjectType";

        public const string Boolean = "Boolean";

        public const string Byte = "Byte";

        public const string ByteString = "ByteString";

        public const string DateTime = "DateTime";

        public const string DefaultBinary = "Default Binary";

        public const string DefaultInstanceBrowseName = "DefaultInstanceBrowseName";

        public const string DefaultJson = "Default JSON";

        public const string DefaultXml = "Default XML";

        public const string Double = "Double";

        public const string Enumeration = "Enumeration";

        public const string EnumStrings = "EnumStrings";

        public const string ExpandedNodeId = "ExpandedNodeId";

        public const string Float = "Float";

        public const string FromState = "FromState";

        public const string GeneratesEvent = "GeneratesEvent";

        public const string Guid = "Guid";

        public const string HasAddIn = "HasAddIn";

        public const string HasAlarmSuppressionGroup = "HasAlarmSuppressionGroup";

        public const string HasCause = "HasCause";

        public const string HasComponent = "HasComponent";

        public const string HasCondition = "HasCondition";

        public const string HasDescription = "HasDescription";

        public const string HasDictionaryEntry = "HasDictionaryEntry";

        public const string HasEffect = "HasEffect";

        public const string HasEncoding = "HasEncoding";

        public const string HasEventSource = "HasEventSource";

        public const string HasFalseSubState = "HasFalseSubState";

        public const string HasGuard = "HasGuard";

        public const string HasInterface = "HasInterface";

        public const string HasModellingRule = "HasModellingRule";

        public const string HasNotifier = "HasNotifier";

        public const string HasOrderedComponent = "HasOrderedComponent";

        public const string HasProperty = "HasProperty";

        public const string HasSubtype = "HasSubtype";

        public const string HasTrueSubState = "HasTrueSubState";

        public const string HasTypeDefinition = "HasTypeDefinition";

        public const string HistoryUpdateDetails = "HistoryUpdateDetails";

        public const string InputArguments = "InputArguments";

        public const string Int16 = "Int16";

        public const string Int32 = "Int32";

        public const string Int64 = "Int64";

        public const string Integer = "Integer";

        public const string LocalizedText = "LocalizedText";

        public const string NamespacePublicationDate = "NamespacePublicationDate";

        public const string NamespaceVersion = "NamespaceVersion";

        public const string NodeId = "NodeId";

        public const string Number = "Number";

        public const string Organizes = "Organizes";

        public const string OutputArguments = "OutputArguments";

        public const string PropertyType = "PropertyType";

        public const string QualifiedName = "QualifiedName";

        public const string SByte = "SByte";

        public const string StatusCode = "StatusCode";

        public const string String = "String";

        public const string Structure = "Structure";

        public const string ToState = "ToState";

        public const string UInt16 = "UInt16";

        public const string UInt32 = "UInt32";

        public const string UInt64 = "UInt64";

        public const string UInteger = "UInteger";

        public const string XmlElement = "XmlElement";
    }
}
