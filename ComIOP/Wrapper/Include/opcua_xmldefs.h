/** 
  @file opcua_xmldefs.h
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

#ifndef _OpcUa_XmlDefs_H_
#define _OpcUa_XmlDefs_H_ 1

OPCUA_BEGIN_EXTERN_C

#define OPCUA_URI_XML                   "http://www.w3.org/XML/1998/namespace"
#define OPCUA_URI_XML_NAMESPACE         "http://www.w3.org/2000/xmlns/"
#define OPCUA_URI_XML_SCHEMA            "http://www.w3.org/2001/XMLSchema"
#define OPCUA_URI_XML_SCHEMA_INSTANCE   "http://www.w3.org/2001/XMLSchema-instance"
#define OPCUA_URI_SOAP_ENVELOPE         "http://www.w3.org/2003/05/soap-envelope"
#define OPCUA_URI_UA_XML_SCHEMA         "http://opcfoundation.org/UA/2008/02/Types.xsd"
#define OPCUA_URI_UA_STATUS_CODES       "http://www.opcfoundation.org/UAPart6/StatusCodes"

#define OPCUA_NAMESPACE_PREFIX_XML      "xml"
#define OPCUA_NAMESPACE_PREFIX_XMLNS    "xmlns"
#define OPCUA_NAMESPACE_PREFIX_XSI      "xsi"
#define OPCUA_NAMESPACE_PREFIX_SOAP     "soap"
#define OPCUA_NAMESPACE_PREFIX_OPC      "opc"

OPCUA_END_EXTERN_C

#endif /* _OpcUa_XmlDefs_H_ */