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

#ifndef _OpcUa_EncodeableObject_H_
#define _OpcUa_EncodeableObject_H_ 1


OPCUA_BEGIN_EXTERN_C

struct _OpcUa_Encoder;
struct _OpcUa_Decoder;

/*============================================================================
 * The EncodeableObject type
 *===========================================================================*/
/** 
  @brief Initializes an encodeable object.

  This function should set the object to a known state. It should not allocate memory.
 
  @param pValue [in] The encodeable object to initialize.
*/
typedef OpcUa_Void (OpcUa_EncodeableObject_PfnInitialize)(OpcUa_Void* pValue);

/** 
  @brief Clear an encodeable object.

  This function must free all memory referenced by the object and set it to a known state.
 
  @param pValue [in] The encodeable object to clear.
*/
typedef OpcUa_Void (OpcUa_EncodeableObject_PfnClear)(OpcUa_Void* pValue);

/** 
  @brief Calculates the size the serialized form an encodeable object.

  Returns Bad_NotSupported if it is not possible to calculate the size.
 
  @param pValue   [in]  The object to encode.
  @param pEncoder [in]  The encoder used to serialize the object.
  @param pSize    [out] The size of the encoded object in bytes.
*/
typedef OpcUa_StatusCode (OpcUa_EncodeableObject_PfnGetSize)(
    OpcUa_Void*            pValue,
    struct _OpcUa_Encoder* pEncoder, 
    OpcUa_Int32*           pSize);

/** 
  @brief Encodes an encodeable object.
 
  @param pEncoder [in] The encoder used to serialize the object.
  @param pValue   [in] The object to encode.
*/
typedef OpcUa_StatusCode (OpcUa_EncodeableObject_PfnEncode)( 
    OpcUa_Void*            pValue,
    struct _OpcUa_Encoder* pEncoder);

/** 
  @brief Decodes an encodeable object.
 
  @param pDecoder [in] The decoder used to deserialize the object.
  @param pValue   [in] The object to decode.
*/
typedef OpcUa_StatusCode (OpcUa_EncodeableObject_PfnDecode)(
    OpcUa_Void*            pValue,
    struct _OpcUa_Decoder* pDecoder);

/** 
  @brief Describes an encodeable object.
*/
typedef struct _OpcUa_EncodeableType
{
    /*! @brief The name of the encodeable type. */
    OpcUa_StringA TypeName;

    /*! @brief The numeric type identifier. */
    OpcUa_UInt32 TypeId;

    /*! @brief The numeric type identifier for the binary encoding. */
    OpcUa_UInt32 BinaryEncodingTypeId;

    /*! @brief The numeric type identifier for the XML encoding. */
    OpcUa_UInt32 XmlEncodingTypeId;

    /*! @brief The namespace uri that qualifies the type identifier. */
    OpcUa_StringA NamespaceUri;

    /*! @brief The size of the structure in memory. */
    OpcUa_UInt32 AllocationSize;

    /*! @brief Initializes the object. */
    OpcUa_EncodeableObject_PfnInitialize* Initialize;

    /*! @brief Clears the object. */
    OpcUa_EncodeableObject_PfnClear* Clear;

    /*! @brief Precalculates the size the serialized object. */
    OpcUa_EncodeableObject_PfnGetSize* GetSize;

    /*! @brief Encodes the object. */
    OpcUa_EncodeableObject_PfnEncode* Encode;

    /*! @brief Decodes the object. */
    OpcUa_EncodeableObject_PfnDecode* Decode;
}
OpcUa_EncodeableType;

/** 
  @brief Compares two Encodeable Types and returns 0 if they are equal.

  @param pType1 [in]  First operand.
  @param pType2 [in]  Second operand.

  @return
*/
OPCUA_EXPORT OpcUa_Int OPCUA_CDECL OpcUa_EncodeableType_Compare(
    const OpcUa_EncodeableType* pType1,
    const OpcUa_EncodeableType* pType2);

struct _OpcUa_EncodeableTypeTableEntry;

/** 
  @brief A table of encodeable object types.
*/
typedef struct _OpcUa_EncodeableTypeTable
{
    /*! @brief The number of entries in the table. */
    OpcUa_Int32 Count;
    
    /*! @brief The table of known types. */
    OpcUa_EncodeableType* Entries;

    /*! @brief The number of entries in the index. */
    OpcUa_Int32 IndexCount;

    /*! @brief A sorted index to the known types. */
    struct _OpcUa_EncodeableTypeTableEntry* Index;

    /*! @brief A mutex used to synchronize access to the table. */
    OpcUa_Mutex Mutex;
}
OpcUa_EncodeableTypeTable;

/** 
  @brief Initializes an encodeable object type table.
 
  @param pTable [in] The table to clear.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_EncodeableTypeTable_Create(
    OpcUa_EncodeableTypeTable* pTable);

/** 
  @brief Clears an encodeable object type table.
 
  @param pTable [in] The table to clear.
*/
OPCUA_EXPORT OpcUa_Void OpcUa_EncodeableTypeTable_Delete(
    OpcUa_EncodeableTypeTable* pTable);

/** 
  @brief Populates and sorts an encodeable object type table.

  The new types are added to the table if it has already contains types.
 
  @param pTable  [in] The table to update.
  @param ppTypes [in] A null terminated list of encodeable object types.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_EncodeableTypeTable_AddTypes(
    OpcUa_EncodeableTypeTable* pTable,
    OpcUa_EncodeableType**     ppTypes);

/** 
  @brief Finds a encodeable object type in a table.

  @param pTable        [in]  The table to search.
  @param nTypeId       [in]  The type identifier for the encodeable object.
  @param sNamespaceUri [in]  The namespace uri that qualifies the type identifier.
  @param ppType        [out] The matching encodeable object type.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_EncodeableTypeTable_Find(
    OpcUa_EncodeableTypeTable*  pTable,
    OpcUa_UInt32                nTypeId,
    OpcUa_StringA               sNamespaceUri,
    OpcUa_EncodeableType**      ppType);

/** 
  @brief Creates and initializes an encodeable object.

  @param pType        [in]  The type of the object to create. 
  @param ppEncodeable [out] The new encodeable object.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_EncodeableObject_Create(
    OpcUa_EncodeableType* pType,
    OpcUa_Void**          ppEncodeable);

/** 
  @brief Deletes an encodeable object.

  @param pType        [in]     The type of the object. 
  @param ppEncodeable [in/out] The encodeable object.
*/
OPCUA_EXPORT OpcUa_Void OpcUa_EncodeableObject_Delete(
    OpcUa_EncodeableType* pType,
    OpcUa_Void**          ppEncodeable);


/** 
  @brief Creates an Encodeable Object at the given ExtensionObject.

  @param pType              [in]     The type of the object. 
  @param pExtension         [in/out] The extension object to which the encodeable object gets attached.
  @param ppEncodeableObject [in/out] Pointer to the encodeable object.
*/
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_EncodeableObject_CreateExtension(
    OpcUa_EncodeableType*  pType,
    OpcUa_ExtensionObject* pExtension,
    OpcUa_Void**           ppEncodeableObject);

struct _OpcUa_MessageContext;

/** 
  @brief Extracts an encodeable object from an extension object.

  @param pExtension         [in]     The extension object to parse.
  @param pContext           [in]     The message context to use during parsing.
  @param pType              [in]     The type of object to extract. 
  @param ppEncodeableObject [in/out] Pointer to the encodeable object.
*/
OPCUA_EXPORT
OpcUa_StatusCode OpcUa_EncodeableObject_ParseExtension(
    OpcUa_ExtensionObject* pExtension,
    struct _OpcUa_MessageContext* pContext,
    OpcUa_EncodeableType*  pType,
    OpcUa_Void**           ppEncodeableObject);

OPCUA_END_EXTERN_C

#endif /* _OpcUa_EncodeableObject_H_ */
