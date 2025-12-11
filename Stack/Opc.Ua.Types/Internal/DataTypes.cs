/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Types
{
#if !INTERNAL
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public
#else
    internal
#endif
        static class DataTypes
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

        public const uint Image = 30;

        public const uint Decimal = 50;

        public const uint ImageBMP = 2000;

        public const uint ImageGIF = 2001;

        public const uint ImageJPG = 2002;

        public const uint ImagePNG = 2003;

        public const uint AudioDataType = 16307;

        public const uint Union = 12756;

        public const uint UriString = 23751;

        public const uint BitFieldMaskDataType = 11737;

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

        public const uint NormalizedString = 12877;

        public const uint DecimalString = 12878;

        public const uint DurationString = 12879;

        public const uint TimeString = 12880;

        public const uint DateString = 12881;

        public const uint Duration = 290;

        public const uint UtcTime = 294;

        public const uint LocaleId = 295;

        public const uint TimeZoneDataType = 8912;

        public const uint Index = 17588;

        public const uint IntegerId = 288;

        public const uint VersionTime = 20998;

        public const uint ApplicationInstanceCertificate = 311;

        public const uint SessionAuthenticationToken = 388;

        public const uint ViewDescription = 511;

        public const uint BrowseDescription = 514;

        public const uint ReferenceDescription = 518;

        public const uint ContinuationPoint = 521;

        public const uint RelativePathElement = 537;

        public const uint RelativePath = 540;

        public const uint Counter = 289;

        public const uint NumericRange = 291;
    }
}
