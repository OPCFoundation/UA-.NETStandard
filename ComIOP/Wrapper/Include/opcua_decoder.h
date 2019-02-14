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

#ifndef _OpcUa_Decoder_H_
#define _OpcUa_Decoder_H_ 1

#include <opcua_builtintypes.h>
#include <opcua_stream.h>
#include <opcua_encodeableobject.h>
#include <opcua_enumeratedtype.h>
#include <opcua_messagecontext.h>

OPCUA_BEGIN_EXTERN_C

/** 
  @brief Opens a decoder and attaches it to the stream.
 
  The caller must ensure the context and stream are valid until the decoder is closed.

  @param pDecoder           [in] The decoder to open.
  @param pIstrm             [in] The stream to use.
  @param pContext           [in] The message context to use.
  @param phDecodeContext   [out] The designated decode handle.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnOpen)(
    struct _OpcUa_Decoder*  pDecoder,
    OpcUa_InputStream*      pIstrm,
    OpcUa_MessageContext*   pContext,
    OpcUa_Handle*           phDecodeContext);

/** 
  @brief Closes a stream an releases all resources allocated to it.
 
  During normal operation all decoders must be closed. Deleting an decoder without
  closing it could have unexpected side effects.

  @param ppDecoder          [in] The decoder to close.
  @param phDecodeContext    [bi] The decode handle used. Gets released.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnClose)(
    struct _OpcUa_Decoder*  pDecoder,
    OpcUa_Handle*           phDecodeContext);

OPCUA_EXPORT OpcUa_Void OpcUa_Decoder_Close(
    struct _OpcUa_Decoder*  pDecoder,
    OpcUa_Handle*           phDecodeContext);

/** 
  @brief Frees the decoder structure.

  This function aborts all I/O operations and frees all memory. It should be
  used only to clean up when the decoder is not needed.
 
  @param ppDecoder [in/out] The decoder to free.
*/
typedef OpcUa_Void (OpcUa_Decoder_PfnDelete)(
    struct _OpcUa_Decoder** ppDecoder);

OPCUA_EXPORT OpcUa_Void OpcUa_Decoder_Delete(
    struct _OpcUa_Decoder** ppDecoder);

/** 
  @brief Sets the default namespace for subsequent operations.
 
  @param pDecoder      [in]  The decoder.
  @param namespaceUri [in]  The namespace to push onto the stack.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnPushNamespace)(
    struct _OpcUa_Decoder* pDecoder, 
    OpcUa_String*          namespaceUri);

/** 
  @brief Restores the previous default namespace.
 
  @param pDecoder [in]  The decoder.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnPopNamespace)(
    struct _OpcUa_Decoder* pDecoder);

/** 
  @brief Reads a Boolean value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadBoolean)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Boolean*         pValue);

/** 
  @brief Reads a SByte value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadSByte)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_SByte*           pValue);

/** 
  @brief Reads a Byte value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadByte)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Byte*            pValue);

/** 
  @brief Reads a Int16 value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadInt16)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Int16*           pValue);

/** 
  @brief Reads a UInt16 value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadUInt16)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_UInt16*          pValue);

/** 
  @brief Reads a Int32 value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadInt32)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Int32*           pValue);

/** 
  @brief Reads a UInt32 value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadUInt32)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_UInt32*          pValue);

/** 
  @brief Reads a Int64 value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadInt64)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Int64*           pValue);

/** 
  @brief Reads a UInt64 value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadUInt64)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_UInt64*          pValue);

/** 
  @brief Reads a Float value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadFloat)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Float*           pValue);

/** 
  @brief Reads a Double value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadDouble)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Double*          pValue);

/** 
  @brief Reads a String value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadString)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_String*          pValue);

/** 
  @brief Reads a DateTime value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadDateTime)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_DateTime*        pValue);

/** 
  @brief Reads a Guid value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadGuid)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Guid*            pValue);

/** 
  @brief Reads a ByteString value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadByteString)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_ByteString*      pValue);

/** 
  @brief Reads a XmlElement value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadXmlElement)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_XmlElement*      pValue);

/** 
  @brief Reads a NodeId value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadNodeId)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_NodeId*          pValue);

/** 
  @brief Reads a ExpandedNodeId value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadExpandedNodeId)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_ExpandedNodeId*  pValue);

/** 
  @brief Reads a StatusCode value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadStatusCode)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_StatusCode*      pValue);

/** 
  @brief Reads a DiagnosticInfo value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadDiagnosticInfo)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_DiagnosticInfo*  pValue);

/** 
  @brief Reads a LocalizedText value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadLocalizedText)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_LocalizedText*   pValue);

/** 
  @brief Reads a QualifiedName value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadQualifiedName)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_QualifiedName*   pValue);

/** 
  @brief Reads a ExtensionObject value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadExtensionObject)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_ExtensionObject* pValue);

/** 
  @brief Reads a DataValue value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadDataValue)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_DataValue*       pValue);

/** 
  @brief Reads a Variant value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadVariant)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Variant*         pValue);
    
/** 
  @brief Reads an Encodeable value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pType      [in]  The type of the value to decode.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadEncodeable)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_EncodeableType*  pType,
    OpcUa_Void*            pValue);
    
/** 
  @brief Reads a enumerated value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pType      [in]  The type of the value to decode.
  @param pValue     [out] The decoded value.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadEnumerated)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_EnumeratedType*  pType,
    OpcUa_Int32*           pValue);

/** 
  @brief Reads a Boolean array value.
 
  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadBooleanArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Boolean**        ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a SByte array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadSByteArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_SByte**          ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a Byte array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadByteArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Byte**           ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a Int16 array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadInt16Array)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Int16**          ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a UInt16 array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadUInt16Array)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_UInt16**         ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a Int32 array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadInt32Array)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Int32**          ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a UInt32 array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadUInt32Array)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_UInt32**         ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a Int64 array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadInt64Array)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Int64**          ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a UInt64 array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadUInt64Array)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_UInt64**         ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a Float array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadFloatArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Float**          ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a Double array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadDoubleArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Double**         ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a String array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadStringArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_String**         ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a DateTime array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadDateTimeArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_DateTime**       ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a Guid array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadGuidArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Guid**           ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a ByteString array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadByteStringArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_ByteString**     ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a XmlElement array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadXmlElementArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_XmlElement**     ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a NodeId array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadNodeIdArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_NodeId**         ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a ExpandedNodeId array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadExpandedNodeIdArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_ExpandedNodeId** ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a StatusCode array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadStatusCodeArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_StatusCode**     ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a DiagnosticInfo array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadDiagnosticInfoArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_DiagnosticInfo** ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a LocalizedText array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadLocalizedTextArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_LocalizedText**  ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a QualifiedName array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadQualifiedNameArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_QualifiedName**  ppArray,
    OpcUa_Int32*           pCount);
/** 
  @brief Reads a ExtensionObject array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadExtensionObjectArray)(
    struct _OpcUa_Decoder*  pDecoder,
    OpcUa_StringA           sFieldName,
    OpcUa_ExtensionObject** ppArray,
    OpcUa_Int32*            pCount);

/** 
  @brief Reads a DataValue array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pArray     [in]  The array to decode.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadDataValueArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_DataValue**      ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Reads a Variant array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadVariantArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_Variant**        ppArray,
    OpcUa_Int32*           pCount);
    
/** 
  @brief Reads an Encodeable array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pType      [in]  The type of the elements in the array.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadEncodeableArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_EncodeableType*  pType,
    OpcUa_Void**           ppArray,
    OpcUa_Int32*           pCount);
    
/** 
  @brief Reads a enumerated array value.

  @param pDecoder   [in]  The decoder.
  @param sFieldName [in]  The name of the field being decoded.
  @param pType      [in]  The type of the elements in the array.
  @param ppArray    [out] The array to decode.
  @param pCount     [out] The number of elements in the array.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadEnumeratedArray)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_StringA          sFieldName,
    OpcUa_EnumeratedType*  pType,
    OpcUa_Int32**          ppArray,
    OpcUa_Int32*           pCount);

/** 
  @brief Writes a message.
  
  If the expected message type is not specified or the stream contains a different
  message type then the function will look up the actual message type and attempt to
  decode it. The caller must check the returned message type before using the returned
  message object.
   
  @param pDecoder     [in]     The encoder.
  @param pMessageType [in/out] The type of message being expected.
  @param ppMessage    [out]    The decoded message.
*/
typedef OpcUa_StatusCode (OpcUa_Decoder_PfnReadMessage)(
    struct _OpcUa_Decoder* pDecoder,
    OpcUa_EncodeableType** pMessageType,
    OpcUa_Void**           ppMessage);

/** 
  @brief A generic decoder.
*/
typedef struct _OpcUa_Decoder
{
    /*! @brief An opaque handle that contain data specific to the decoder implementation. */
    OpcUa_Handle Handle;

    /*! @brief The type of decoder. */
    OpcUa_EncoderType DecoderType;

    /*! @brief Initializes the decoder for use. */
    OpcUa_Decoder_PfnOpen* Open;
    
    /*! @brief Closes the decoder. */
    OpcUa_Decoder_PfnClose* Close;
    
    /*! @brief Frees the structure. */
    OpcUa_Decoder_PfnDelete* Delete;

    /*! @brief Sets the default namespace for subsequent operations. */
    OpcUa_Decoder_PfnPushNamespace* PushNamespace;

    /*! @brief Restores the previous default namespace. */
    OpcUa_Decoder_PfnPopNamespace* PopNamespace;

    /*! @brief Reads a Boolean value. */
    OpcUa_Decoder_PfnReadBoolean* ReadBoolean;

    /*! @brief Reads a SByte value. */
    OpcUa_Decoder_PfnReadSByte* ReadSByte;

    /*! @brief Reads a Byte value. */
    OpcUa_Decoder_PfnReadByte* ReadByte;

    /*! @brief Reads a Int16 value. */
    OpcUa_Decoder_PfnReadInt16* ReadInt16;

    /*! @brief Reads a UInt16 value. */
    OpcUa_Decoder_PfnReadUInt16* ReadUInt16;

    /*! @brief Reads a Int32 value. */
    OpcUa_Decoder_PfnReadInt32* ReadInt32;

    /*! @brief Reads a UInt32 value. */
    OpcUa_Decoder_PfnReadUInt32* ReadUInt32;

    /*! @brief Reads a Int64 value. */
    OpcUa_Decoder_PfnReadInt64* ReadInt64;

    /*! @brief Reads a UInt64 value. */
    OpcUa_Decoder_PfnReadUInt64* ReadUInt64;

    /*! @brief Reads a Float value. */
    OpcUa_Decoder_PfnReadFloat* ReadFloat;

    /*! @brief Reads a Double value. */
    OpcUa_Decoder_PfnReadDouble* ReadDouble;

    /*! @brief Reads a String value. */
    OpcUa_Decoder_PfnReadString* ReadString;

    /*! @brief Reads a DateTime value. */
    OpcUa_Decoder_PfnReadDateTime* ReadDateTime;

    /*! @brief Reads a Guid value. */
    OpcUa_Decoder_PfnReadGuid* ReadGuid;

    /*! @brief Reads a ByteString value. */
    OpcUa_Decoder_PfnReadByteString* ReadByteString;

    /*! @brief Reads an XmlElement value. */
    OpcUa_Decoder_PfnReadXmlElement* ReadXmlElement;

    /*! @brief Reads a NodeId value. */
    OpcUa_Decoder_PfnReadNodeId* ReadNodeId;

    /*! @brief Reads an ExpandedNodeId value. */
    OpcUa_Decoder_PfnReadExpandedNodeId* ReadExpandedNodeId;

    /*! @brief Reads a StatusCode value. */
    OpcUa_Decoder_PfnReadStatusCode* ReadStatusCode;

    /*! @brief Reads a DiagnosticInfo value. */
    OpcUa_Decoder_PfnReadDiagnosticInfo* ReadDiagnosticInfo;

    /*! @brief Reads a LocalizedText value. */
    OpcUa_Decoder_PfnReadLocalizedText* ReadLocalizedText;

    /*! @brief Reads a QualifiedName value. */
    OpcUa_Decoder_PfnReadQualifiedName* ReadQualifiedName;

    /*! @brief Reads a ExtensionObject value. */
    OpcUa_Decoder_PfnReadExtensionObject* ReadExtensionObject;

    /*! @brief Reads a DataValue value. */
    OpcUa_Decoder_PfnReadDataValue* ReadDataValue;

    /*! @brief Reads a Variant value. */
    OpcUa_Decoder_PfnReadVariant* ReadVariant;

    /*! @brief Reads a Encodeable value. */
    OpcUa_Decoder_PfnReadEncodeable* ReadEncodeable;

    /*! @brief Reads a Enumerated value. */
    OpcUa_Decoder_PfnReadEnumerated* ReadEnumerated;

    /*! @brief Reads a Boolean array value. */
    OpcUa_Decoder_PfnReadBooleanArray* ReadBooleanArray;

    /*! @brief Reads a SByte array value. */
    OpcUa_Decoder_PfnReadSByteArray* ReadSByteArray;

    /*! @brief Reads a Byte array value. */
    OpcUa_Decoder_PfnReadByteArray* ReadByteArray;

    /*! @brief Reads a Int16 array value. */
    OpcUa_Decoder_PfnReadInt16Array* ReadInt16Array;

    /*! @brief Reads a UInt16 array value. */
    OpcUa_Decoder_PfnReadUInt16Array* ReadUInt16Array;

    /*! @brief Reads a Int32 array value. */
    OpcUa_Decoder_PfnReadInt32Array* ReadInt32Array;

    /*! @brief Reads a UInt32 array value. */
    OpcUa_Decoder_PfnReadUInt32Array* ReadUInt32Array;

    /*! @brief Reads a Int64 array value. */
    OpcUa_Decoder_PfnReadInt64Array* ReadInt64Array;

    /*! @brief Reads a UInt64 array value. */
    OpcUa_Decoder_PfnReadUInt64Array* ReadUInt64Array;

    /*! @brief Reads a Float array value. */
    OpcUa_Decoder_PfnReadFloatArray* ReadFloatArray;

    /*! @brief Reads a Double array value. */
    OpcUa_Decoder_PfnReadDoubleArray* ReadDoubleArray;

    /*! @brief Reads a String array value. */
    OpcUa_Decoder_PfnReadStringArray* ReadStringArray;

    /*! @brief Reads a DateTime array value. */
    OpcUa_Decoder_PfnReadDateTimeArray* ReadDateTimeArray;

    /*! @brief Reads a Guid array value. */
    OpcUa_Decoder_PfnReadGuidArray* ReadGuidArray;

    /*! @brief Reads a ByteString array value. */
    OpcUa_Decoder_PfnReadByteStringArray* ReadByteStringArray;

    /*! @brief Reads an XmlElement array value. */
    OpcUa_Decoder_PfnReadXmlElementArray* ReadXmlElementArray;

    /*! @brief Reads a NodeId array value. */
    OpcUa_Decoder_PfnReadNodeIdArray* ReadNodeIdArray;

    /*! @brief Reads an ExpandedNodeId array value. */
    OpcUa_Decoder_PfnReadExpandedNodeIdArray* ReadExpandedNodeIdArray;

    /*! @brief Reads a StatusCode array value. */
    OpcUa_Decoder_PfnReadStatusCodeArray* ReadStatusCodeArray;

    /*! @brief Reads a DiagnosticInfo array value. */
    OpcUa_Decoder_PfnReadDiagnosticInfoArray* ReadDiagnosticInfoArray;

    /*! @brief Reads a LocalizedText array value. */
    OpcUa_Decoder_PfnReadLocalizedTextArray* ReadLocalizedTextArray;

    /*! @brief Reads a QualifiedName array value. */
    OpcUa_Decoder_PfnReadQualifiedNameArray* ReadQualifiedNameArray;

    /*! @brief Reads a ExtensionObject array value. */
    OpcUa_Decoder_PfnReadExtensionObjectArray* ReadExtensionObjectArray;

    /*! @brief Reads a DataValue array value. */
    OpcUa_Decoder_PfnReadDataValueArray* ReadDataValueArray;

    /*! @brief Reads a Variant array value. */
    OpcUa_Decoder_PfnReadVariantArray* ReadVariantArray;

    /*! @brief Reads a Encodeable array value. */
    OpcUa_Decoder_PfnReadEncodeableArray* ReadEncodeableArray;

    /*! @brief Reads a Enumerated array value. */
    OpcUa_Decoder_PfnReadEnumeratedArray* ReadEnumeratedArray;

    /*! @brief Reads a message. */
    OpcUa_Decoder_PfnReadMessage* ReadMessage;
}
OpcUa_Decoder;

/*============================================================================
 * OpcUa_Field_Read
 *===========================================================================*/
#define OpcUa_Field_Read(xType, xName) \
{ \
    uStatus = a_pDecoder->Read##xType(a_pDecoder, #xName, &a_pValue->xName); \
    OpcUa_GotoErrorIfBad(uStatus); \
}

/*============================================================================
 * OpcUa_Field_ReadEncodeable
 *===========================================================================*/
#define OpcUa_Field_ReadEncodeable(xType, xName) \
{ \
    uStatus = a_pDecoder->ReadEncodeable(a_pDecoder, #xName, &xType##_EncodeableType, &a_pValue->xName); \
    OpcUa_GotoErrorIfBad(uStatus); \
}

/*============================================================================
 * OpcUa_Field_ReadEnumerated
 *===========================================================================*/
#define OpcUa_Field_ReadEnumerated(xType, xName) \
{ \
    uStatus = a_pDecoder->ReadEnumerated(a_pDecoder, #xName, &xType##_EnumeratedType, (OpcUa_Int32*)&a_pValue->xName); \
    OpcUa_GotoErrorIfBad(uStatus); \
}

/*============================================================================
 * OpcUa_Field_ReadArray
 *===========================================================================*/
#define OpcUa_Field_ReadArray(xType, xName) \
{ \
    uStatus = a_pDecoder->Read##xType##Array(a_pDecoder, #xName, &(a_pValue->xName), &(a_pValue->NoOf##xName)); \
    OpcUa_GotoErrorIfBad(uStatus); \
}

/*============================================================================
 * OpcUa_Field_ReadEncodeableArray
 *===========================================================================*/
#define OpcUa_Field_ReadEncodeableArray(xType, xName) \
{ \
    uStatus = a_pDecoder->ReadEncodeableArray(a_pDecoder, #xName, &xType##_EncodeableType, (OpcUa_Void**)(&(a_pValue->xName)), &(a_pValue->NoOf##xName)); \
    OpcUa_GotoErrorIfBad(uStatus); \
}

/*============================================================================
 * OpcUa_Field_ReadEnumeratedArray
 *===========================================================================*/
#define OpcUa_Field_ReadEnumeratedArray(xType, xName) \
{ \
    uStatus = a_pDecoder->ReadEnumeratedArray(a_pDecoder, #xName, &xType##_EnumeratedType, (OpcUa_Int32**)&(a_pValue->xName), &(a_pValue->NoOf##xName)); \
    OpcUa_GotoErrorIfBad(uStatus); \
}

OPCUA_END_EXTERN_C

#endif /* _OpcUa_Decoder_H_ */
