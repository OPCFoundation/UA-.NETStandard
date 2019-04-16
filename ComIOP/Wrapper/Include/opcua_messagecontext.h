/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

#ifndef _OpcUa_MessageContext_H_
#define _OpcUa_MessageContext_H_ 1

#include <opcua_stringtable.h>
#include <opcua_builtintypes.h>
#include <opcua_encodeableobject.h>

OPCUA_BEGIN_EXTERN_C

/** 
  @brief Stores data used to construct a message context.
*/
typedef struct _OpcUa_MessageContext
{
    /*! @brief The table of namespace URIs used by the server (memory not owned by the context). */
    OpcUa_StringTable* NamespaceUris;

    /*! @brief The table of known encodeable types. */
    OpcUa_EncodeableTypeTable* KnownTypes;

    /*! @brief Whether the encoder should always calculate the size of the encodeable objects (used for debugging) */
    OpcUa_Boolean AlwaysCheckLengths;

    /*! @brief The maximum length for any array. */
    OpcUa_UInt32 MaxArrayLength;

    /*! @brief The maximum length for any String value. */
    OpcUa_UInt32 MaxStringLength;

    /*! @brief The maximum length for any ByteString value. */
    OpcUa_UInt32 MaxByteStringLength;

    /*! @brief The maximum length for any message. */
    OpcUa_UInt32 MaxMessageLength;
}
OpcUa_MessageContext;

/** 
  @brief Puts the context into a known state.

  @param pContext [in] The context to initialize.
*/
OPCUA_EXPORT OpcUa_Void OpcUa_MessageContext_Initialize(
    OpcUa_MessageContext* pContext);

/** 
  @brief Frees all memory used by a string context.

  @param pContext [in] The context to clear.
*/
OPCUA_EXPORT OpcUa_Void OpcUa_MessageContext_Clear(
    OpcUa_MessageContext* pContext);

/** 
  @brief Adds a new encoded object position to the context.

  @param pContext [in] The context to update.
  @param nStart   [in] The stream position for the first byte of the object.
  @param nEnd     [in] The stream position immediately after the last byte of the object.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_MessageContext_SaveObjectPosition(
    OpcUa_MessageContext* pContext,
    OpcUa_UInt32          nStart,
    OpcUa_UInt32          nEnd);

/** 
  @brief Gets the length of an object at the specified position.

  @param pContext [in] The context to search.
  @param nStart   [in] The stream position for the first byte of the object.
  @param iLength  [in] The length of the encoded object.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_MessageContext_GetObjectLength(
    OpcUa_MessageContext* pContext,
    OpcUa_UInt32          nStart,
    OpcUa_Int32*          iLength);

OPCUA_END_EXTERN_C

#endif /* _OpcUa_MessageContext_H_ */
