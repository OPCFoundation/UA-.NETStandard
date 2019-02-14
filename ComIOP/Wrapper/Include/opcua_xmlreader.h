/** 
  @file opcua_xmlreader.h
  @brief 

  Copyright (c) 2019 The OPC Foundation
  ALL RIGHTS RESERVED.

  DISCLAIMER:
  This code is provided by the OPC Foundation solely to assist in 
  understanding and use of the appropriate OPC Specification(s) and may be 
  used as set forth in the License Grant section of the OPC Specification.
  This code is provided as-is and without warranty or support of any sort
  and is subject to the Warranty and Liability Disclaimers which appear
  in the printed OPC Specification.
*/

#ifndef _OpcUa_XmlReader_H_
#define _OpcUa_XmlReader_H_ 1

#ifdef OPCUA_HAVE_XMLAPI

OPCUA_BEGIN_EXTERN_C

typedef enum _OpcUa_XmlReader_NodeType
{
    OpcUa_XmlReader_NodeType_None                   = 0,
    OpcUa_XmlReader_NodeType_Element                = 1,
    OpcUa_XmlReader_NodeType_Attribute              = 2,
    OpcUa_XmlReader_NodeType_Text                   = 3,
    OpcUa_XmlReader_NodeType_CDATA                  = 4,
    OpcUa_XmlReader_NodeType_EntityReference        = 5,
    OpcUa_XmlReader_NodeType_Entity                 = 6,
    OpcUa_XmlReader_NodeType_ProcessingInstruction  = 7,
    OpcUa_XmlReader_NodeType_Comment                = 8,
    OpcUa_XmlReader_NodeType_Document               = 9,
    OpcUa_XmlReader_NodeType_DocumentType           = 10,
    OpcUa_XmlReader_NodeType_DocumentFragment       = 11,
    OpcUa_XmlReader_NodeType_Notation               = 12,
    OpcUa_XmlReader_NodeType_Whitespace             = 13,
    OpcUa_XmlReader_NodeType_SignificantWhitespace  = 14,
    OpcUa_XmlReader_NodeType_EndElement             = 15,
    OpcUa_XmlReader_NodeType_EndEntity              = 16,
    OpcUa_XmlReader_NodeType_XmlDeclaration         = 17
} OpcUa_XmlReader_NodeType;

struct _OpcUa_XmlReader;

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnMoveToContent)(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_Int32*                a_pNodeType);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_MoveToContent(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_Int32*                a_pNodeType);

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnMoveToElement)(
    struct _OpcUa_XmlReader*    a_pXmlReader);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_MoveToElement(
    struct _OpcUa_XmlReader*    a_pXmlReader);

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnMoveToFirstAttribute)(
    struct _OpcUa_XmlReader*    a_pXmlReader);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_MoveToFirstAttribute(
    struct _OpcUa_XmlReader*    a_pXmlReader);

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnMoveToNextAttribute)(
    struct _OpcUa_XmlReader*    a_pXmlReader);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_MoveToNextAttribute(
    struct _OpcUa_XmlReader*    a_pXmlReader);

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnIsStartElement)(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_StringA               a_sLocalName,
    OpcUa_StringA               a_sNamespaceUri,
    OpcUa_Boolean*              a_pResult);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_IsStartElement(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_StringA               a_sLocalName,
    OpcUa_StringA               a_sNamespaceUri,
    OpcUa_Boolean*              a_pResult);

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnIsEmptyElement)(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_Boolean*              a_pResult);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_IsEmptyElement(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_Boolean*              a_pResult);

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnHasAttributes)(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_Boolean*              a_pResult);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_HasAttributes(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_Boolean*              a_pResult);

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnIsDefault)(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_Boolean*              a_pResult);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_IsDefault(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_Boolean*              a_pResult);

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnReadStartElement)(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_StringA               a_sLocalName,
    OpcUa_StringA               a_sNamespaceUri);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_ReadStartElement(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_StringA               a_sLocalName,
    OpcUa_StringA               a_sNamespaceUri);

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnReadEndElement)(
    struct _OpcUa_XmlReader*    a_pXmlReader);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_ReadEndElement(
    struct _OpcUa_XmlReader*    a_pXmlReader);

typedef OpcUa_Int32 (OpcUa_XmlReader_PfnGetNodeType)(
    struct _OpcUa_XmlReader*    a_pXmlReader);

OPCUA_EXPORT OpcUa_Int32 OpcUa_XmlReader_GetNodeType(
    struct _OpcUa_XmlReader*    a_pXmlReader);

typedef OpcUa_Int32 (OpcUa_XmlReader_PfnGetDepth)(
    struct _OpcUa_XmlReader*    a_pXmlReader);

OPCUA_EXPORT OpcUa_Int32 OpcUa_XmlReader_GetDepth(
    struct _OpcUa_XmlReader*    a_pXmlReader);

typedef OpcUa_StringA  (OpcUa_XmlReader_PfnGetLocalName)(
    struct _OpcUa_XmlReader*    a_pXmlReader);

OPCUA_EXPORT OpcUa_StringA OpcUa_XmlReader_GetLocalName(
    struct _OpcUa_XmlReader*    a_pXmlReader);

typedef OpcUa_StringA  (OpcUa_XmlReader_PfnGetName)(
    struct _OpcUa_XmlReader*    a_pXmlReader);

OPCUA_EXPORT OpcUa_StringA OpcUa_XmlReader_GetName(
    struct _OpcUa_XmlReader*    a_pXmlReader);

typedef OpcUa_StringA  (OpcUa_XmlReader_PfnGetNamespaceUri)(
    struct _OpcUa_XmlReader*    a_pXmlReader);

OPCUA_EXPORT OpcUa_StringA OpcUa_XmlReader_GetNamespaceUri(
    struct _OpcUa_XmlReader*    a_pXmlReader);

typedef OpcUa_StringA (OpcUa_XmlReader_PfnGetPrefix)(
    struct _OpcUa_XmlReader*    a_pXmlReader);

OPCUA_EXPORT OpcUa_StringA OpcUa_XmlReader_GetPrefix(
    struct _OpcUa_XmlReader*    a_pXmlReader);

typedef OpcUa_StringA  (OpcUa_XmlReader_PfnGetValue)(
    struct _OpcUa_XmlReader*    a_pXmlReader);

OPCUA_EXPORT OpcUa_StringA  OpcUa_XmlReader_GetValue(
    struct _OpcUa_XmlReader*    a_pXmlReader);

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnGetAttribute)(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_StringA               a_sAttributeName,
    OpcUa_StringA               a_sNamespaceUri,
    OpcUa_StringA               a_sAttributeValue,
    OpcUa_UInt32*               a_pValueLength);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_GetAttribute(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_StringA               a_sAttributeName,
    OpcUa_StringA               a_sNamespaceUri,
    OpcUa_StringA               a_sAttributeValue,
    OpcUa_UInt32*               a_pValueLength);

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnRead)(
    struct _OpcUa_XmlReader*    a_pXmlReader);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_Read(
    struct _OpcUa_XmlReader*    a_pXmlReader);

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnSkip)(
    struct _OpcUa_XmlReader*    a_pXmlReader);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_Skip(
    struct _OpcUa_XmlReader*    a_pXmlReader);

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnClose)(
    struct _OpcUa_XmlReader*    a_pXmlReader);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_Close(
    struct _OpcUa_XmlReader*    a_pXmlReader);

typedef struct _OpcUa_XmlReader
{
    OpcUa_Void*                                 Handle;
    OpcUa_XmlReader_PfnMoveToContent*           MoveToContent;
    OpcUa_XmlReader_PfnMoveToElement*           MoveToElement;
    OpcUa_XmlReader_PfnMoveToFirstAttribute*    MoveToFirstAttribute;
    OpcUa_XmlReader_PfnMoveToNextAttribute*     MoveToNextAttribute;
    OpcUa_XmlReader_PfnIsStartElement*          IsStartElement;
    OpcUa_XmlReader_PfnIsEmptyElement*          IsEmptyElement;
    OpcUa_XmlReader_PfnHasAttributes*           HasAttributes;
    OpcUa_XmlReader_PfnIsDefault*               IsDefault;
    OpcUa_XmlReader_PfnReadStartElement*        ReadStartElement;
    OpcUa_XmlReader_PfnReadEndElement*          ReadEndElement;
    OpcUa_XmlReader_PfnGetNodeType*             GetNodeType;
    OpcUa_XmlReader_PfnGetDepth*                GetDepth;
    OpcUa_XmlReader_PfnGetLocalName*            GetLocalName;
    OpcUa_XmlReader_PfnGetName*                 GetName;
    OpcUa_XmlReader_PfnGetNamespaceUri*         GetNamespaceUri;
    OpcUa_XmlReader_PfnGetPrefix*               GetPrefix;
    OpcUa_XmlReader_PfnGetValue*                GetValue;
    OpcUa_XmlReader_PfnGetAttribute*            GetAttribute;
    OpcUa_XmlReader_PfnRead*                    Read;
    OpcUa_XmlReader_PfnSkip*                    Skip;
    OpcUa_XmlReader_PfnClose*                   Close;
} OpcUa_XmlReader;

struct _OpcUa_InputStream;
struct XmlElement;
struct OpcUa_String;

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_Create(
    struct _OpcUa_XmlReader**   a_ppXmlReader,
    struct _OpcUa_InputStream*  a_pInputStream);

OPCUA_EXPORT OpcUa_Void OpcUa_XmlReader_Delete(
    struct _OpcUa_XmlReader**   a_ppXmlReader);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_ReadString(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_String*               a_pValue);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_ReadInnerXml(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_XmlElement*           a_pInnerXml);

OPCUA_EXPORT OpcUa_StatusCode OpcUa_XmlReader_ReadOuterXml(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_XmlElement*           a_pOuterXml);

OPCUA_END_EXTERN_C

#endif /* OPCUA_HAVE_XMLAPI */
#endif /* _OpcUa_XmlReader_H_ */
