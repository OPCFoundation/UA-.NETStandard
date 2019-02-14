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

#ifndef _OpcUa_Encoder_H_
#define _OpcUa_Encoder_H_ 1

#include <opcua_builtintypes.h>
#include <opcua_stream.h>
#include <opcua_encodeableobject.h>
#include <opcua_enumeratedtype.h>
#include <opcua_messagecontext.h>

OPCUA_BEGIN_EXTERN_C

/** 
  @brief Opens an encoder and attaches it to the stream.
 
  The caller must ensure the context and stream are valid until the encoder is closed.

  @param pEncoder         [in] The encoder to open.
  @param pOstrm           [in] The stream to use.
  @param pContext         [in] The message context to use.
  @param phEncodeContext [out] The encoder instance is being created in this call.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnOpen)(
    struct _OpcUa_Encoder* pEncoder, 
    OpcUa_OutputStream*    pOstrm,
    OpcUa_MessageContext*  pContext,
    OpcUa_Handle*          phEncodeContext);

OPCUA_EXPORT OpcUa_Void OpcUa_Encoder_Open(
    struct _OpcUa_Encoder* pEncoder, 
    OpcUa_OutputStream*    pOstrm,
    OpcUa_MessageContext*  pContext,
    OpcUa_Handle*          phEncodeContext);

/** 
  @brief Closes a stream an releases all resources allocated to it.
 
  During normal operation all encoders must be closed. Deleting an encoder without
  closing it could have unexpected side effects.

  @param pEncoder        [in] The encoder to close.
  @param phEncodeContext [bi] The encoder instance is being deleted in this call.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnClose)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_Handle*          phEncodeContext);

OPCUA_EXPORT OpcUa_Void OpcUa_Encoder_Close(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_Handle*          phEncodeContext);

/** 
  @brief Frees the encoder structure.

  This function aborts all I/O operations and frees all memory. It should be
  used only to clean up when the encoder is not needed.
 
  @param ppEncoder [in/out] The encoder to free.
*/
typedef OpcUa_Void (OpcUa_Encoder_PfnDelete)(
    struct _OpcUa_Encoder** ppEncoder);

OPCUA_EXPORT OpcUa_Void OpcUa_Encoder_Delete(
    struct _OpcUa_Encoder** ppEncoder);

/** 
  @brief Sets the default namespace for subsequent operations.
 
  @param pEncoder      [in] The encoder.
  @param namespaceUri [in] The namespace to push onto the stack.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnPushNamespace)(
    struct _OpcUa_Encoder* pEncoder, 
    OpcUa_String*          namespaceUri);

/** 
  @brief Restores the previous default namespace.
 
  @param pEncoder [in] The encoder.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnPopNamespace)(
    struct _OpcUa_Encoder* pEncoder);

/** 
  @brief Writes a Boolean value.

  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.
 
  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteBoolean)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Boolean*         pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a SByte value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteSByte)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_SByte*           pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Byte value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteByte)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Byte*            pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Int16 value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteInt16)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Int16*           pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a UInt16 value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteUInt16)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_UInt16*          pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Int32 value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteInt32)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Int32*           pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a UInt32 value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteUInt32)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_UInt32*          pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Int64 value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value..
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteInt64)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Int64*           pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a UInt64 value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteUInt64)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_UInt64*          pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Float value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteFloat)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Float*           pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Double value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteDouble)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Double*          pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a String value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value..
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteString)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_String*          pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a DateTime value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteDateTime)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_DateTime*        pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Guid value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteGuid)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Guid*            pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a ByteString value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteByteString)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_ByteString*      pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a XmlElement value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteXmlElement)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_XmlElement*      pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a NodeId value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteNodeId)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_NodeId*          pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a ExpandedNodeId value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteExpandedNodeId)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_ExpandedNodeId*  pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a StatusCode value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteStatusCode)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_StatusCode*      pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a DiagnosticInfo value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteDiagnosticInfo)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_DiagnosticInfo*  pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a LocalizedText value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteLocalizedText)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_LocalizedText*   pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a QualifiedName value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteQualifiedName)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_QualifiedName*   pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a ExtensionObject value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteExtensionObject)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_ExtensionObject* pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a DataValue value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteDataValue)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_DataValue*       pValue,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Variant value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteVariant)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Variant*         pValue,
    OpcUa_Int32*           pSize);
    
/** 
  @brief Writes an Encodeable value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pType      [in]     The type of the value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteEncodeable)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Void*            pValue,
    OpcUa_EncodeableType*  pType,
    OpcUa_Int32*           pSize);
    
/** 
  @brief Writes a enumerated value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pValue     [in]     The value to encode.
  @param pType      [in]     The type of the value to encode.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteEnumerated)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Int32*           pValue,
    OpcUa_EnumeratedType*  pType,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Boolean array value.

  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.
 
  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteBooleanArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Boolean*         pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a SByte array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteSByteArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_SByte*           pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Byte array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteByteArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Byte*            pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Int16 array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteInt16Array)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Int16*           pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a UInt16 array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteUInt16Array)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_UInt16*          pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Int32 array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteInt32Array)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Int32*           pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a UInt32 array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteUInt32Array)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_UInt32*          pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Int64 array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteInt64Array)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Int64*           pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a UInt64 array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteUInt64Array)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_UInt64*          pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Float array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteFloatArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Float*           pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Double array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteDoubleArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Double*          pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a String array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteStringArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_String*          pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a DateTime array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteDateTimeArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_DateTime*        pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Guid array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteGuidArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Guid*            pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a ByteString array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteByteStringArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_ByteString*      pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a XmlElement array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteXmlElementArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_XmlElement*      pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a NodeId array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteNodeIdArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_NodeId*          pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a ExpandedNodeId array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteExpandedNodeIdArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_ExpandedNodeId*  pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a StatusCode array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteStatusCodeArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_StatusCode*      pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a DiagnosticInfo array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteDiagnosticInfoArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_DiagnosticInfo*  pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a LocalizedText array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteLocalizedTextArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_LocalizedText*   pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a QualifiedName array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteQualifiedNameArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_QualifiedName*   pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a ExtensionObject array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteExtensionObjectArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_ExtensionObject* pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a DataValue array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteDataValueArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_DataValue*       pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);

/** 
  @brief Writes a Variant array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteVariantArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Variant*         pArray,
    OpcUa_Int32            nCount,
    OpcUa_Int32*           pSize);
    
/** 
  @brief Writes an Encodeable array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pType      [in]     The type of the elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteEncodeableArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Void*            pArray,
    OpcUa_Int32            nCount,
    OpcUa_EncodeableType*  pType,
    OpcUa_Int32*           pSize);
    
/** 
  @brief Writes a enumerated array value.
 
  If the pSize parameter is not null then encoder only calculates the size of the encoded
  value and returns it in the pSize parameter. The value is NOT written to the stream.
  Returns Bad_NotSupported if size cannot be precalculated.

  @param pEncoder   [in]     The encoder.
  @param sFieldName [in]     The name of the field being encoded.
  @param pArray     [in]     The array to encode.
  @param nCount     [in]     The number of elements in the array.
  @param pType      [in]     The type of the elements in the array.
  @param pSize      [in/out] The size of the encoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteEnumeratedArray)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Int32*           pArray,
    OpcUa_Int32            nCount,
    OpcUa_EnumeratedType*  pType,
    OpcUa_Int32*           pSize);

/** 
  @brief Write a message.
 
  @param pEncoder     [in]  The encoder.
  @param pMessage     [in]  The message to encode.
  @param pMessageType [out] The type of message being encoded.
*/
typedef OpcUa_StatusCode (OpcUa_Encoder_PfnWriteMessage)(
    struct _OpcUa_Encoder* pEncoder,
    OpcUa_Void*            pMessage,
    OpcUa_EncodeableType*  pMessageType);

/** 
  @brief The type of encoder.
*/
typedef enum _OpcUa_EncoderType
{
    /*! @brief An encoder that uses the UA binary encoding */
    OpcUa_EncoderType_Binary,

    /*! @brief An encoder that uses the XML encoding */
    OpcUa_EncoderType_Xml
}
OpcUa_EncoderType;

/** 
  @brief A generic encoder.
*/
typedef struct _OpcUa_Encoder
{
    /*! @brief An opaque handle that contain data specific to the encoder implementation. */
    OpcUa_Handle Handle;

    /*! @brief The type of encoder. */
    OpcUa_EncoderType EncoderType;

    /*! @brief Initializes the decoder for use. */
    OpcUa_Encoder_PfnOpen* Open;
    
    /*! @brief Closes the encoder. */
    OpcUa_Encoder_PfnClose* Close;
    
    /*! @brief Frees the structure. */
    OpcUa_Encoder_PfnDelete* Delete;

    /*! @brief Sets the default namespace for subsequent operations. */
    OpcUa_Encoder_PfnPushNamespace* PushNamespace;

    /*! @brief Restores the previous default namespace. */
    OpcUa_Encoder_PfnPopNamespace* PopNamespace;

    /*! @brief Writes a Boolean value. */
    OpcUa_Encoder_PfnWriteBoolean* WriteBoolean;

    /*! @brief Writes a SByte value. */
    OpcUa_Encoder_PfnWriteSByte* WriteSByte;

    /*! @brief Writes a Byte value. */
    OpcUa_Encoder_PfnWriteByte* WriteByte;

    /*! @brief Writes a Int16 value. */
    OpcUa_Encoder_PfnWriteInt16* WriteInt16;

    /*! @brief Writes a UInt16 value. */
    OpcUa_Encoder_PfnWriteUInt16* WriteUInt16;

    /*! @brief Writes a Int32 value. */
    OpcUa_Encoder_PfnWriteInt32* WriteInt32;

    /*! @brief Writes a UInt32 value. */
    OpcUa_Encoder_PfnWriteUInt32* WriteUInt32;

    /*! @brief Writes a Int64 value. */
    OpcUa_Encoder_PfnWriteInt64* WriteInt64;

    /*! @brief Writes a UInt64 value. */
    OpcUa_Encoder_PfnWriteUInt64* WriteUInt64;

    /*! @brief Writes a Float value. */
    OpcUa_Encoder_PfnWriteFloat* WriteFloat;

    /*! @brief Writes a Double value. */
    OpcUa_Encoder_PfnWriteDouble* WriteDouble;

    /*! @brief Writes a String value. */
    OpcUa_Encoder_PfnWriteString* WriteString;

    /*! @brief Writes a DateTime value. */
    OpcUa_Encoder_PfnWriteDateTime* WriteDateTime;

    /*! @brief Writes a Guid value. */
    OpcUa_Encoder_PfnWriteGuid* WriteGuid;

    /*! @brief Writes a ByteString value. */
    OpcUa_Encoder_PfnWriteByteString* WriteByteString;

    /*! @brief Writes an XmlElement value. */
    OpcUa_Encoder_PfnWriteXmlElement* WriteXmlElement;

    /*! @brief Writes a NodeId value. */
    OpcUa_Encoder_PfnWriteNodeId* WriteNodeId;

    /*! @brief Writes an ExpandedNodeId value. */
    OpcUa_Encoder_PfnWriteExpandedNodeId* WriteExpandedNodeId;

    /*! @brief Writes a StatusCode value. */
    OpcUa_Encoder_PfnWriteStatusCode* WriteStatusCode;

    /*! @brief Writes a DiagnosticInfo value. */
    OpcUa_Encoder_PfnWriteDiagnosticInfo* WriteDiagnosticInfo;

    /*! @brief Writes a LocalizedText value. */
    OpcUa_Encoder_PfnWriteLocalizedText* WriteLocalizedText;

    /*! @brief Writes a QualifiedName value. */
    OpcUa_Encoder_PfnWriteQualifiedName* WriteQualifiedName;

    /*! @brief Writes a ExtensionObject value. */
    OpcUa_Encoder_PfnWriteExtensionObject* WriteExtensionObject;

    /*! @brief Writes a DataValue value. */
    OpcUa_Encoder_PfnWriteDataValue* WriteDataValue;

    /*! @brief Writes a Variant value. */
    OpcUa_Encoder_PfnWriteVariant* WriteVariant;

    /*! @brief Writes a Encodeable value. */
    OpcUa_Encoder_PfnWriteEncodeable* WriteEncodeable;

    /*! @brief Writes a Enumerated value. */
    OpcUa_Encoder_PfnWriteEnumerated* WriteEnumerated;

    /*! @brief Writes a Boolean array value. */
    OpcUa_Encoder_PfnWriteBooleanArray* WriteBooleanArray;

    /*! @brief Writes a SByte array value. */
    OpcUa_Encoder_PfnWriteSByteArray* WriteSByteArray;

    /*! @brief Writes a Byte array value. */
    OpcUa_Encoder_PfnWriteByteArray* WriteByteArray;

    /*! @brief Writes a Int16 array value. */
    OpcUa_Encoder_PfnWriteInt16Array* WriteInt16Array;

    /*! @brief Writes a UInt16 array value. */
    OpcUa_Encoder_PfnWriteUInt16Array* WriteUInt16Array;

    /*! @brief Writes a Int32 array value. */
    OpcUa_Encoder_PfnWriteInt32Array* WriteInt32Array;

    /*! @brief Writes a UInt32 array value. */
    OpcUa_Encoder_PfnWriteUInt32Array* WriteUInt32Array;

    /*! @brief Writes a Int64 array value. */
    OpcUa_Encoder_PfnWriteInt64Array* WriteInt64Array;

    /*! @brief Writes a UInt64 array value. */
    OpcUa_Encoder_PfnWriteUInt64Array* WriteUInt64Array;

    /*! @brief Writes a Float array value. */
    OpcUa_Encoder_PfnWriteFloatArray* WriteFloatArray;

    /*! @brief Writes a Double array value. */
    OpcUa_Encoder_PfnWriteDoubleArray* WriteDoubleArray;

    /*! @brief Writes a String array value. */
    OpcUa_Encoder_PfnWriteStringArray* WriteStringArray;

    /*! @brief Writes a DateTime array value. */
    OpcUa_Encoder_PfnWriteDateTimeArray* WriteDateTimeArray;

    /*! @brief Writes a Guid array value. */
    OpcUa_Encoder_PfnWriteGuidArray* WriteGuidArray;

    /*! @brief Writes a ByteString array value. */
    OpcUa_Encoder_PfnWriteByteStringArray* WriteByteStringArray;

    /*! @brief Writes an XmlElement array value. */
    OpcUa_Encoder_PfnWriteXmlElementArray* WriteXmlElementArray;

    /*! @brief Writes a NodeId array value. */
    OpcUa_Encoder_PfnWriteNodeIdArray* WriteNodeIdArray;

    /*! @brief Writes an ExpandedNodeId array value. */
    OpcUa_Encoder_PfnWriteExpandedNodeIdArray* WriteExpandedNodeIdArray;

    /*! @brief Writes a StatusCode array value. */
    OpcUa_Encoder_PfnWriteStatusCodeArray* WriteStatusCodeArray;

    /*! @brief Writes a DiagnosticInfo array value. */
    OpcUa_Encoder_PfnWriteDiagnosticInfoArray* WriteDiagnosticInfoArray;

    /*! @brief Writes a LocalizedText array value. */
    OpcUa_Encoder_PfnWriteLocalizedTextArray* WriteLocalizedTextArray;

    /*! @brief Writes a QualifiedName array value. */
    OpcUa_Encoder_PfnWriteQualifiedNameArray* WriteQualifiedNameArray;

    /*! @brief Writes a ExtensionObject array value. */
    OpcUa_Encoder_PfnWriteExtensionObjectArray* WriteExtensionObjectArray;

    /*! @brief Writes a DataValue array value. */
    OpcUa_Encoder_PfnWriteDataValueArray* WriteDataValueArray;

    /*! @brief Writes a Variant array value. */
    OpcUa_Encoder_PfnWriteVariantArray* WriteVariantArray;

    /*! @brief Writes a Encodeable array value. */
    OpcUa_Encoder_PfnWriteEncodeableArray* WriteEncodeableArray;

    /*! @brief Writes a Enumerated array value. */
    OpcUa_Encoder_PfnWriteEnumeratedArray* WriteEnumeratedArray;

    /*! @brief Writes a message. */
    OpcUa_Encoder_PfnWriteMessage* WriteMessage;
}
OpcUa_Encoder;

/*============================================================================
 * OpcUa_Field_GetSize
 *===========================================================================*/
#define OpcUa_Field_GetSize(xType, xName) \
{ \
    OpcUa_Int32 uFieldSize; \
    uStatus = a_pEncoder->Write##xType(a_pEncoder, #xName, &a_pValue->xName, &uFieldSize); \
    OpcUa_GotoErrorIfBad(uStatus); \
    iSize += uFieldSize; \
}

/*============================================================================
 * OpcUa_Field_GetSizeEncodeable
 *===========================================================================*/
#define OpcUa_Field_GetSizeEncodeable(xType, xName) \
{ \
    OpcUa_Int32 uFieldSize; \
    uStatus = a_pEncoder->WriteEncodeable(a_pEncoder, #xName, &a_pValue->xName, &xType##_EncodeableType, &uFieldSize); \
    OpcUa_GotoErrorIfBad(uStatus); \
    iSize += uFieldSize; \
}

/*============================================================================
 * OpcUa_Field_GetSizeEnumerated
 *===========================================================================*/
#define OpcUa_Field_GetSizeEnumerated(xType, xName) \
{ \
    OpcUa_Int32 uFieldSize; \
    uStatus = a_pEncoder->WriteEnumerated(a_pEncoder, #xName, (OpcUa_Int32*)&a_pValue->xName, &xType##_EnumeratedType, &uFieldSize); \
    OpcUa_GotoErrorIfBad(uStatus); \
    iSize += uFieldSize; \
}

/*============================================================================
 * OpcUa_Field_GetSizeArray
 *===========================================================================*/
#define OpcUa_Field_GetSizeArray(xType, xName) \
{ \
    OpcUa_Int32 uFieldSize; \
    uStatus = a_pEncoder->Write##xType##Array(a_pEncoder, #xName, a_pValue->xName, a_pValue->NoOf##xName, &uFieldSize); \
    OpcUa_GotoErrorIfBad(uStatus); \
    iSize += uFieldSize; \
}

/*============================================================================
 * OpcUa_Field_GetSizeEncodeableArray
 *===========================================================================*/
#define OpcUa_Field_GetSizeEncodeableArray(xType, xName) \
{ \
    OpcUa_Int32 uFieldSize; \
    uStatus = a_pEncoder->WriteEncodeableArray(a_pEncoder, #xName, a_pValue->xName, a_pValue->NoOf##xName, &xType##_EncodeableType, &uFieldSize); \
    OpcUa_GotoErrorIfBad(uStatus); \
    iSize += uFieldSize; \
}

/*============================================================================
 * OpcUa_Field_GetSizeEnumeratedArray
 *===========================================================================*/
#define OpcUa_Field_GetSizeEnumeratedArray(xType, xName) \
{ \
    OpcUa_Int32 uFieldSize; \
    uStatus = a_pEncoder->WriteEnumeratedArray(a_pEncoder, #xName, (OpcUa_Int32*)a_pValue->xName, a_pValue->NoOf##xName, &xType##_EnumeratedType, &uFieldSize); \
    OpcUa_GotoErrorIfBad(uStatus); \
    iSize += uFieldSize; \
}

/*============================================================================
 * OpcUa_Field_Write
 *===========================================================================*/
#define OpcUa_Field_Write(xType, xName) \
{ \
    uStatus = a_pEncoder->Write##xType(a_pEncoder, #xName, &a_pValue->xName, OpcUa_Null); \
    OpcUa_GotoErrorIfBad(uStatus); \
}

/*============================================================================
 * OpcUa_Field_WriteEncodeable
 *===========================================================================*/
#define OpcUa_Field_WriteEncodeable(xType, xName) \
{ \
    uStatus = a_pEncoder->WriteEncodeable(a_pEncoder, #xName, &a_pValue->xName, &xType##_EncodeableType, OpcUa_Null); \
    OpcUa_GotoErrorIfBad(uStatus); \
}

/*============================================================================
 * OpcUa_Field_WriteEnumerated
 *===========================================================================*/
#define OpcUa_Field_WriteEnumerated(xType, xName) \
{ \
    uStatus = a_pEncoder->WriteEnumerated(a_pEncoder, #xName, (OpcUa_Int32*)&a_pValue->xName, &xType##_EnumeratedType, OpcUa_Null); \
    OpcUa_GotoErrorIfBad(uStatus); \
}

/*============================================================================
 * OpcUa_Field_WriteArray
 *===========================================================================*/
#define OpcUa_Field_WriteArray(xType, xName) \
{ \
    uStatus = a_pEncoder->Write##xType##Array(a_pEncoder, #xName, a_pValue->xName, a_pValue->NoOf##xName, OpcUa_Null); \
    OpcUa_GotoErrorIfBad(uStatus); \
}

/*============================================================================
 * OpcUa_Field_WriteEncodeableArray
 *===========================================================================*/
#define OpcUa_Field_WriteEncodeableArray(xType, xName) \
{ \
    uStatus = a_pEncoder->WriteEncodeableArray(a_pEncoder, #xName, a_pValue->xName, a_pValue->NoOf##xName, &xType##_EncodeableType, OpcUa_Null); \
    OpcUa_GotoErrorIfBad(uStatus); \
}

/*============================================================================
 * OpcUa_Field_WriteEnumeratedArray
 *===========================================================================*/
#define OpcUa_Field_WriteEnumeratedArray(xType, xName) \
{ \
    uStatus = a_pEncoder->WriteEnumeratedArray(a_pEncoder, #xName, (OpcUa_Int32*)a_pValue->xName, a_pValue->NoOf##xName, &xType##_EnumeratedType, OpcUa_Null); \
    OpcUa_GotoErrorIfBad(uStatus); \
}

OPCUA_END_EXTERN_C

#endif /* _OpcUa_Encoder_H_ */
