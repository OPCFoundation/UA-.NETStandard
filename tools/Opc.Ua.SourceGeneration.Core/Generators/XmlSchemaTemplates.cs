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

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Template strings
    /// </summary>
    internal static class XmlSchemaTemplates
    {
        /// <summary>
        /// Xml schema file template
        /// </summary>
        public static readonly TemplateString File = TemplateString.Parse(
            $$"""
            <xs:schema
              {{Tokens.XmlnsS0ListOfNamespaces}}
              xmlns:xs="http://www.w3.org/2001/XMLSchema"
              xmlns:ua="http://opcfoundation.org/UA/2008/02/Types.xsd"
              xmlns:tns="{{Tokens.Namespace}}"
              targetNamespace="{{Tokens.Namespace}}"
              elementFormDefault="qualified"
            >
              <xs:annotation>
                <xs:appinfo>
                  <ua:Model ModelUri="{{Tokens.ModelUri}}" Version="{{Tokens.TargetVersion}}" PublicationDate="{{Tokens.TargetPublicationDate}}" />
                </xs:appinfo>
              </xs:annotation>

              {{Tokens.Imports}}
              {{Tokens.BuiltInTypes}}
              {{Tokens.ListOfTypes}}

            </xs:schema>
            """);

        /// <summary>
        /// Built-in types schema template
        /// </summary>
        public static readonly TemplateString BuiltInTypes =
              """
              <xs:element name="Boolean" type="xs:boolean" />

              <xs:complexType name="ListOfBoolean">
                <xs:sequence>
                  <xs:element name="Boolean" type="xs:boolean" minOccurs="0" maxOccurs="unbounded" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfBoolean" type="tns:ListOfBoolean" nillable="true"></xs:element>

              <xs:element name="Number" nillable="true" type="xs:decimal" />

              <xs:element name="Integer" nillable="true" type="xs:integer" />

              <xs:element name="UInteger" nillable="true" type="xs:positiveInteger" />

              <xs:element name="SByte" type="xs:byte" />

              <xs:complexType name="ListOfSByte">
                <xs:sequence>
                  <xs:element name="SByte" type="xs:byte" minOccurs="0" maxOccurs="unbounded" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfSByte" type="tns:ListOfSByte" nillable="true"></xs:element>

              <xs:element name="Byte" type="xs:unsignedByte" />

              <xs:complexType name="ListOfByte">
                <xs:sequence>
                  <xs:element name="Byte" type="xs:unsignedByte" minOccurs="0" maxOccurs="unbounded" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfByte" type="tns:ListOfByte" nillable="true"></xs:element>

              <xs:element name="Int16" type="xs:short" />

              <xs:complexType name="ListOfInt16">
                <xs:sequence>
                  <xs:element name="Int16" type="xs:short" minOccurs="0" maxOccurs="unbounded" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfInt16" type="tns:ListOfInt16" nillable="true"></xs:element>

              <xs:element name="UInt16" type="xs:unsignedShort" />

              <xs:complexType name="ListOfUInt16">
                <xs:sequence>
                  <xs:element name="UInt16" type="xs:unsignedShort" minOccurs="0" maxOccurs="unbounded" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfUInt16" type="tns:ListOfUInt16" nillable="true"></xs:element>

              <xs:element name="Int32" type="xs:int" />

              <xs:complexType name="ListOfInt32">
                <xs:sequence>
                  <xs:element name="Int32" type="xs:int" minOccurs="0" maxOccurs="unbounded" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfInt32" type="tns:ListOfInt32" nillable="true"></xs:element>

              <xs:element name="UInt32" type="xs:unsignedInt" />

              <xs:complexType name="ListOfUInt32">
                <xs:sequence>
                  <xs:element name="UInt32" type="xs:unsignedInt" minOccurs="0" maxOccurs="unbounded" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfUInt32" type="tns:ListOfUInt32" nillable="true"></xs:element>

              <xs:element name="Int64" type="xs:long" />

              <xs:complexType name="ListOfInt64">
                <xs:sequence>
                  <xs:element name="Int64" type="xs:long" minOccurs="0" maxOccurs="unbounded" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfInt64" type="tns:ListOfInt64" nillable="true"></xs:element>

              <xs:element name="UInt64" type="xs:unsignedLong" />

              <xs:complexType name="ListOfUInt64">
                <xs:sequence>
                  <xs:element name="UInt64" type="xs:unsignedLong" minOccurs="0" maxOccurs="unbounded" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfUInt64" type="tns:ListOfUInt64" nillable="true"></xs:element>

              <xs:element name="Float" type="xs:float" />

              <xs:complexType name="ListOfFloat">
                <xs:sequence>
                  <xs:element name="Float" type="xs:float" minOccurs="0" maxOccurs="unbounded" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfFloat" type="tns:ListOfFloat" nillable="true"></xs:element>

              <xs:element name="Double" type="xs:double" />

              <xs:complexType name="ListOfDouble">
                <xs:sequence>
                  <xs:element name="Double" type="xs:double" minOccurs="0" maxOccurs="unbounded" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfDouble" type="tns:ListOfDouble" nillable="true"></xs:element>

              <xs:element name="String" nillable="true" type="xs:string" />

              <xs:complexType name="ListOfString">
                <xs:sequence>
                  <xs:element name="String" type="xs:string" minOccurs="0" maxOccurs="unbounded" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfString" type="tns:ListOfString" nillable="true"></xs:element>

              <xs:element name="DateTime" nillable="true" type="xs:dateTime" />

              <xs:complexType name="ListOfDateTime">
                <xs:sequence>
                  <xs:element name="DateTime" type="xs:dateTime" minOccurs="0" maxOccurs="unbounded" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfDateTime" type="tns:ListOfDateTime" nillable="true"></xs:element>

              <xs:complexType name="Guid">
                <xs:sequence>
                  <xs:element name="String" type="xs:string" minOccurs="0" maxOccurs="1" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="Guid" type="tns:Guid" nillable="true"></xs:element>

              <xs:complexType name="ListOfGuid">
                <xs:sequence>
                  <xs:element name="Guid" type="tns:Guid" minOccurs="0" maxOccurs="unbounded" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfGuid" type="tns:ListOfGuid" nillable="true"></xs:element>

              <xs:element name="ByteString" nillable="true" type="xs:base64Binary" />

              <xs:complexType name="ListOfByteString">
                <xs:sequence>
                  <xs:element name="ByteString" type="xs:base64Binary" minOccurs="0" maxOccurs="unbounded" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfByteString" type="tns:ListOfByteString" nillable="true"></xs:element>

              <xs:complexType name="XmlElement">
                <xs:sequence>
                  <xs:any minOccurs="0" processContents="lax"/>
                </xs:sequence>
              </xs:complexType>
              <xs:element name="XmlElement" type="tns:XmlElement" nillable="true"></xs:element>

              <xs:complexType name="ListOfXmlElement">
                <xs:sequence>
                  <xs:element name="XmlElement" minOccurs="0" maxOccurs="unbounded" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfXmlElement" type="tns:ListOfXmlElement" nillable="true"></xs:element>

              <xs:complexType name="NodeId">
                <xs:sequence>
                  <xs:element name="Identifier" type="xs:string" minOccurs="0" maxOccurs="1" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="NodeId" type="tns:NodeId" nillable="true"></xs:element>

              <xs:complexType name="ListOfNodeId">
                <xs:sequence>
                  <xs:element name="NodeId" type="tns:NodeId" minOccurs="0" maxOccurs="unbounded" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfNodeId" type="tns:ListOfNodeId" nillable="true"></xs:element>

              <xs:complexType name="ExpandedNodeId">
                <xs:sequence>
                  <xs:element name="Identifier" type="xs:string" minOccurs="0" maxOccurs="1" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ExpandedNodeId" type="tns:ExpandedNodeId" nillable="true"></xs:element>

              <xs:complexType name="ListOfExpandedNodeId">
                <xs:sequence>
                  <xs:element name="ExpandedNodeId" type="tns:ExpandedNodeId" minOccurs="0" maxOccurs="unbounded" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfExpandedNodeId" type="tns:ListOfExpandedNodeId" nillable="true"></xs:element>

              <xs:complexType name="StatusCode">
                <xs:sequence>
                  <xs:element name="Code" type="xs:unsignedInt" minOccurs="0" maxOccurs="1" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="StatusCode" type="tns:StatusCode"></xs:element>

              <xs:complexType name="ListOfStatusCode">
                <xs:sequence>
                  <xs:element name="StatusCode" type="tns:StatusCode" minOccurs="0" maxOccurs="unbounded" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfStatusCode" type="tns:ListOfStatusCode" nillable="true"></xs:element>

              <xs:complexType name="DiagnosticInfo">
                <xs:sequence>
                  <xs:element name="SymbolicId" type="xs:int" minOccurs="0" maxOccurs="1" />
                  <xs:element name="NamespaceUri" type="xs:int" minOccurs="0" maxOccurs="1" />
                  <xs:element name="Locale" type="xs:int" minOccurs="0" maxOccurs="1" />
                  <xs:element name="LocalizedText" type="xs:int" minOccurs="0" maxOccurs="1" />
                  <xs:element name="AdditionalInfo" type="xs:string" minOccurs="0" maxOccurs="1" />
                  <xs:element name="InnerStatusCode" type="tns:StatusCode" minOccurs="0" maxOccurs="1" />
                  <xs:element name="InnerDiagnosticInfo" type="tns:DiagnosticInfo" minOccurs="0" maxOccurs="1" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="DiagnosticInfo" type="tns:DiagnosticInfo" nillable="true"></xs:element>

              <xs:complexType name="ListOfDiagnosticInfo">
                <xs:sequence>
                  <xs:element name="DiagnosticInfo" type="tns:DiagnosticInfo" minOccurs="0" maxOccurs="unbounded" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfDiagnosticInfo" type="tns:ListOfDiagnosticInfo" nillable="true"></xs:element>

              <xs:complexType name="LocalizedText">
                <xs:sequence>
                  <xs:element name="Locale" type="xs:string" minOccurs="0" nillable="true" />
                  <xs:element name="Text" type="xs:string" minOccurs="0"  nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="LocalizedText" type="tns:LocalizedText" nillable="true" />

              <xs:complexType name="ListOfLocalizedText">
                <xs:sequence>
                  <xs:element name="LocalizedText" type="tns:LocalizedText" minOccurs="0" maxOccurs="unbounded" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfLocalizedText" type="tns:ListOfLocalizedText" nillable="true"></xs:element>

              <xs:complexType name="QualifiedName">
                <xs:sequence>
                  <xs:element name="NamespaceIndex" type="xs:unsignedShort" minOccurs="0" />
                  <xs:element name="Name" type="xs:string" minOccurs="0" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="QualifiedName" type="tns:QualifiedName" nillable="true" />

              <xs:complexType name="ListOfQualifiedName">
                <xs:sequence>
                  <xs:element name="QualifiedName" type="tns:QualifiedName" minOccurs="0" maxOccurs="unbounded" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfQualifiedName" type="tns:ListOfQualifiedName" nillable="true"></xs:element>

              <xs:complexType name="ExtensionObjectBody">
                <xs:sequence>
                  <xs:any minOccurs="0" processContents="lax" />
                </xs:sequence>
              </xs:complexType>

              <xs:complexType name="ExtensionObject">
                <xs:sequence>
                  <xs:element name="TypeId" type="tns:ExpandedNodeId" minOccurs="0" nillable="true" />
                  <xs:element name="Body" minOccurs="0" type ="tns:ExtensionObjectBody" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ExtensionObject" type="tns:ExtensionObject" nillable="true" />

              <xs:complexType name="ListOfExtensionObject">
                <xs:sequence>
                  <xs:element name="ExtensionObject" type="tns:ExtensionObject" minOccurs="0" maxOccurs="unbounded" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfExtensionObject" type="tns:ListOfExtensionObject" nillable="true"></xs:element>

              <xs:complexType name="Decimal">
                <xs:sequence>
                  <xs:element name="TypeId" type="tns:NodeId" minOccurs="0" />
                  <xs:element name="Body" minOccurs="0">
                    <xs:complexType>
                      <xs:sequence>
                        <xs:element name="Scale" type="xs:short" />
                        <xs:element name="Value" type="xs:string" />
                      </xs:sequence>
                    </xs:complexType>
                  </xs:element>
                </xs:sequence>
              </xs:complexType>

              <xs:complexType name="Matrix">
                <xs:sequence>
                  <xs:element name="Dimensions" type="tns:ListOfInt32" minOccurs="0" nillable="true" />
                  <xs:element name="Value" minOccurs="0" nillable="true">
                    <xs:complexType mixed="false">
                      <xs:choice maxOccurs="unbounded">
                        <xs:element name="Boolean" type="xs:boolean" minOccurs="0" />
                        <xs:element name="SByte" type="xs:byte" minOccurs="0" />
                        <xs:element name="Byte" type="xs:unsignedByte" minOccurs="0" />
                        <xs:element name="Int16" type="xs:short" minOccurs="0" />
                        <xs:element name="UInt16" type="xs:unsignedShort" minOccurs="0" />
                        <xs:element name="Int32" type="xs:int" minOccurs="0" />
                        <xs:element name="UInt32" type="xs:unsignedInt" minOccurs="0" />
                        <xs:element name="Int64" type="xs:long" minOccurs="0" />
                        <xs:element name="UInt64" type="xs:unsignedLong" minOccurs="0" />
                        <xs:element name="Float" type="xs:float" minOccurs="0" />
                        <xs:element name="Double" type="xs:double" minOccurs="0" />
                        <xs:element name="String" type="xs:string" minOccurs="0" />
                        <xs:element name="DateTime" type="xs:dateTime" minOccurs="0" />
                        <xs:element name="Guid" type="tns:Guid" minOccurs="0" />
                        <xs:element name="ByteString" type="xs:base64Binary" minOccurs="0" />
                        <xs:element name="XmlElement" minOccurs="0" nillable="true">
                          <xs:complexType>
                            <xs:sequence>
                              <xs:any minOccurs="0" processContents="lax" />
                            </xs:sequence>
                          </xs:complexType>
                        </xs:element>
                        <xs:element name="StatusCode" type="tns:StatusCode" minOccurs="0" />
                        <xs:element name="NodeId" type="tns:NodeId" minOccurs="0" />
                        <xs:element name="ExpandedNodeId" type="tns:ExpandedNodeId" minOccurs="0" />
                        <xs:element name="QualifiedName" type="tns:QualifiedName" minOccurs="0" />
                        <xs:element name="LocalizedText" type="tns:LocalizedText" minOccurs="0" />
                        <xs:element name="ExtensionObject" type="tns:ExtensionObject" minOccurs="0" />
                        <xs:element name="Variant" type="tns:Variant" minOccurs="0" />
                      </xs:choice>
                    </xs:complexType>
                  </xs:element>
                </xs:sequence>
              </xs:complexType>
              <xs:element name="Matrix" type="tns:Matrix" nillable="true" />

              <xs:complexType name="VariantValue">
                <xs:choice>
                  <xs:element name="Boolean" type="xs:boolean" minOccurs="0" />
                  <xs:element name="SByte" type="xs:byte" minOccurs="0" />
                  <xs:element name="Byte" type="xs:unsignedByte" minOccurs="0" />
                  <xs:element name="Int16" type="xs:short" minOccurs="0" />
                  <xs:element name="UInt16" type="xs:unsignedShort" minOccurs="0" />
                  <xs:element name="Int32" type="xs:int" minOccurs="0" />
                  <xs:element name="UInt32" type="xs:unsignedInt" minOccurs="0" />
                  <xs:element name="Int64" type="xs:long" minOccurs="0" />
                  <xs:element name="UInt64" type="xs:unsignedLong" minOccurs="0" />
                  <xs:element name="Float" type="xs:float" minOccurs="0" />
                  <xs:element name="Double" type="xs:double" minOccurs="0" />
                  <xs:element name="String" type="xs:string" minOccurs="0" />
                  <xs:element name="DateTime" type="xs:dateTime" minOccurs="0" />
                  <xs:element name="Guid" type="tns:Guid" minOccurs="0" />
                  <xs:element name="ByteString" type="xs:base64Binary" minOccurs="0" />
                  <xs:element name="XmlElement" minOccurs="0" nillable="true">
                    <xs:complexType>
                      <xs:sequence>
                        <xs:any minOccurs="0" processContents="lax" />
                      </xs:sequence>
                    </xs:complexType>
                  </xs:element>
                  <xs:element name="StatusCode" type="tns:StatusCode" minOccurs="0" />
                  <xs:element name="NodeId" type="tns:NodeId" minOccurs="0" />
                  <xs:element name="ExpandedNodeId" type="tns:ExpandedNodeId" minOccurs="0" />
                  <xs:element name="QualifiedName" type="tns:QualifiedName" minOccurs="0" />
                  <xs:element name="LocalizedText" type="tns:LocalizedText" minOccurs="0" />
                  <xs:element name="ExtensionObject" type="tns:ExtensionObject" minOccurs="0" />
                  <xs:element name="ListOfBoolean" type="tns:ListOfBoolean" minOccurs="0" />
                  <xs:element name="ListOfSByte" type="tns:ListOfSByte" minOccurs="0" />
                  <xs:element name="ListOfByte" type="tns:ListOfByte" minOccurs="0" />
                  <xs:element name="ListOfInt16" type="tns:ListOfInt16" minOccurs="0" />
                  <xs:element name="ListOfUInt16" type="tns:ListOfUInt16" minOccurs="0" />
                  <xs:element name="ListOfInt32" type="tns:ListOfInt32" minOccurs="0" />
                  <xs:element name="ListOfUInt32" type="tns:ListOfUInt32" minOccurs="0" />
                  <xs:element name="ListOfInt64" type="tns:ListOfInt64" minOccurs="0" />
                  <xs:element name="ListOfUInt64" type="tns:ListOfUInt64" minOccurs="0" />
                  <xs:element name="ListOfFloat" type="tns:ListOfFloat" minOccurs="0" />
                  <xs:element name="ListOfDouble" type="tns:ListOfDouble" minOccurs="0" />
                  <xs:element name="ListOfString" type="tns:ListOfString" minOccurs="0" />
                  <xs:element name="ListOfDateTime" type="tns:ListOfDateTime" minOccurs="0" />
                  <xs:element name="ListOfGuid" type="tns:ListOfGuid" minOccurs="0" />
                  <xs:element name="ListOfByteString" type="tns:ListOfByteString" minOccurs="0" />
                  <xs:element name="ListOfXmlElement" type="tns:ListOfXmlElement" minOccurs="0" />
                  <xs:element name="ListOfStatusCode" type="tns:ListOfStatusCode" minOccurs="0" />
                  <xs:element name="ListOfNodeId" type="tns:ListOfNodeId" minOccurs="0" />
                  <xs:element name="ListOfExpandedNodeId" type="tns:ListOfExpandedNodeId" minOccurs="0" />
                  <xs:element name="ListOfQualifiedName" type="tns:ListOfQualifiedName" minOccurs="0" />
                  <xs:element name="ListOfLocalizedText" type="tns:ListOfLocalizedText" minOccurs="0" />
                  <xs:element name="ListOfExtensionObject" type="tns:ListOfExtensionObject" minOccurs="0" />
                  <xs:element name="ListOfVariant" type="tns:ListOfVariant" minOccurs="0" />
                  <xs:element name="Matrix" type="tns:Matrix" minOccurs="0" />
                </xs:choice>
              </xs:complexType>

              <xs:complexType name="Variant">
                <xs:sequence>
                  <xs:element name="Value" type="tns:VariantValue" minOccurs="0" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="Variant" type="tns:Variant" nillable="true" />

              <xs:complexType name="ListOfVariant">
                <xs:sequence>
                  <xs:element name="Variant" type="tns:Variant" minOccurs="0" maxOccurs="unbounded" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfVariant" type="tns:ListOfVariant" nillable="true"></xs:element>

              <xs:complexType name="DataValue">
                <xs:sequence>
                  <xs:element name="Value" type="tns:Variant" minOccurs="0" />
                  <xs:element name="StatusCode" type="tns:StatusCode" minOccurs="0" />
                  <xs:element name="SourceTimestamp" type="xs:dateTime" minOccurs="0" />
                  <xs:element name="SourcePicoseconds" type="xs:unsignedShort" minOccurs="0" />
                  <xs:element name="ServerTimestamp" type="xs:dateTime" minOccurs="0" />
                  <xs:element name="ServerPicoseconds" type="xs:unsignedShort" minOccurs="0" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="DataValue" type="tns:DataValue" nillable="true"/>

              <xs:complexType name="ListOfDataValue">
                <xs:sequence>
                  <xs:element name="DataValue" type="tns:DataValue" minOccurs="0" maxOccurs="unbounded" nillable="true" />
                </xs:sequence>
              </xs:complexType>
              <xs:element name="ListOfDataValue" type="tns:ListOfDataValue" nillable="true"></xs:element>

              <xs:element name="InvokeServiceRequest" type="xs:base64Binary" nillable="true" />
              <xs:element name="InvokeServiceResponse" type="xs:base64Binary" nillable="true" />
              """;

        /// <summary>
        /// Derived type schema template
        /// </summary>
        public static readonly TemplateString DerivedType = TemplateString.Parse(
            $$"""
            <xs:complexType name="{{Tokens.TypeName}}">
              {{Tokens.Documentation}}
              <xs:complexContent mixed="false">
                <xs:extension base="{{Tokens.BaseType}}">
                  <xs:sequence>
                    {{Tokens.ListOfFields}}
                  </xs:sequence>
                </xs:extension>
              </xs:complexContent>
            </xs:complexType>
            <xs:element name="{{Tokens.TypeName}}" type="tns:{{Tokens.TypeName}}" />

            {{Tokens.CollectionType}}

            """);

        /// <summary>
        /// Enumerated type schema template
        /// </summary>
        public static readonly TemplateString EnumeratedType = TemplateString.Parse(
            $$"""
            <xs:simpleType name="{{Tokens.TypeName}}">
              {{Tokens.Documentation}}
              <xs:restriction base="{{Tokens.XsRestrictionBaseType}}">
                {{Tokens.ListOfFields}}
              </xs:restriction>
            </xs:simpleType>
            <xs:element name="{{Tokens.TypeName}}" type="tns:{{Tokens.TypeName}}" />

            {{Tokens.CollectionType}}

            """);

        /// <summary>
        /// Union type schema template
        /// </summary>
        public static readonly TemplateString Union = TemplateString.Parse(
            $$"""
            <xs:complexType name="{{Tokens.TypeName}}">
              {{Tokens.Documentation}}
              <xs:sequence>
                <xs:element name="SwitchField" type="xs:unsignedInt" minOccurs="0" />
                <xs:choice>
                  {{Tokens.ListOfFields}}
                </xs:choice>
              </xs:sequence>
            </xs:complexType>
            <xs:element name="{{Tokens.TypeName}}" type="tns:{{Tokens.TypeName}}" />

            {{Tokens.CollectionType}}

            """);

        /// <summary>
        /// Complex type schema template
        /// </summary>
        public static readonly TemplateString ComplexType = TemplateString.Parse(
            $$"""
            <xs:complexType name="{{Tokens.TypeName}}">
              {{Tokens.Documentation}}
              <xs:sequence>
                {{Tokens.ListOfFields}}
              </xs:sequence>
            </xs:complexType>
            <xs:element name="{{Tokens.TypeName}}" type="tns:{{Tokens.TypeName}}" />

            {{Tokens.CollectionType}}

            """);

        /// <summary>
        /// Simple type schema template
        /// </summary>
        public static readonly TemplateString SimpleType = TemplateString.Parse(
            $$"""
            <xs:element name="{{Tokens.TypeName}}" type="{{Tokens.BaseType}}" />

            """);

        /// <summary>
        /// Documentation schema template
        /// </summary>
        public static readonly TemplateString Documentation = TemplateString.Parse(
            $$"""
            <xs:annotation>
              <xs:documentation>{{Tokens.Description}}</xs:documentation>
            </xs:annotation>

            """);

        /// <summary>
        /// Collection type schema template
        /// </summary>
        public static readonly TemplateString CollectionType = TemplateString.Parse(
            $$"""
            <xs:complexType name="ListOf{{Tokens.TypeName}}">
              <xs:sequence>
                <xs:element name="{{Tokens.TypeName}}" type="tns:{{Tokens.TypeName}}" minOccurs="0" maxOccurs="unbounded" {{Tokens.Nillable}}/>
              </xs:sequence>
            </xs:complexType>
            <xs:element name="ListOf{{Tokens.TypeName}}" type="tns:ListOf{{Tokens.TypeName}}" nillable="true"></xs:element>

            """);
    }
}
