/** 
  @file opcua_base64.h
  @brief Defines BASE64 handling functions.

  Copyright (c) 2005-2019 The OPC Foundation
  ALL RIGHTS RESERVED.

  DISCLAIMER:
  This code is provided by the OPC Foundation solely to assist in 
  understanding and use of the appropriate OPC Specification(s) and may be 
  used as set forth in the License Grant section of the OPC Specification.
  This code is provided as-is and without warranty or support of any sort
  and is subject to the Warranty and Liability Disclaimers which appear
  in the printed OPC Specification.
*/

#ifndef _OpcUa_Base64_H_
#define _OpcUa_Base64_H_ 1

#ifdef OPCUA_HAVE_BASE64

OPCUA_BEGIN_EXTERN_C

OpcUa_StatusCode OpcUa_Base64_Encode(
    OpcUa_Byte*     a_pBytes,
    OpcUa_Int32     a_iByteCount,
    OpcUa_StringA*  a_psString);

OpcUa_StatusCode OpcUa_Base64_Decode(
    OpcUa_StringA   a_sString,
    OpcUa_Int32*    a_piByteCount,
    OpcUa_Byte**    a_ppBytes);

OPCUA_END_EXTERN_C

#endif /* OPCUA_HAVE_BASE64 */
#endif /* _OpcUa_Base64_H_ */
