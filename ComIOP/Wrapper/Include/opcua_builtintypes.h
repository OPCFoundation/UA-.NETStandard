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

#ifndef _OpcUa_BuiltInTypes_H_
#define _OpcUa_BuiltInTypes_H_ 1

OPCUA_BEGIN_EXTERN_C

/*============================================================================
 * The OpcUa_BuiltInType enumeration
 *===========================================================================*/
typedef enum _OpcUa_BuiltInType
{
    OpcUaType_Null = 0,
    OpcUaType_Boolean = 1,
    OpcUaType_SByte = 2,
    OpcUaType_Byte = 3,
    OpcUaType_Int16 = 4,
    OpcUaType_UInt16 = 5,
    OpcUaType_Int32 = 6,
    OpcUaType_UInt32 = 7,
    OpcUaType_Int64 = 8,
    OpcUaType_UInt64 = 9,
    OpcUaType_Float = 10,
    OpcUaType_Double = 11,
    OpcUaType_String = 12,
    OpcUaType_DateTime = 13,
    OpcUaType_Guid = 14,
    OpcUaType_ByteString = 15,
    OpcUaType_XmlElement = 16,
    OpcUaType_NodeId = 17,
    OpcUaType_ExpandedNodeId = 18,
    OpcUaType_StatusCode = 19,
    OpcUaType_QualifiedName = 20,
    OpcUaType_LocalizedText = 21,
    OpcUaType_ExtensionObject = 22,
    OpcUaType_DataValue = 23,
    OpcUaType_Variant = 24,
    OpcUaType_DiagnosticInfo = 25
}
OpcUa_BuiltInType;

/*============================================================================
 * The Boolean type
 *===========================================================================*/

#define OpcUa_Boolean_Initialize(xValue) *(xValue) = OpcUa_False;

#define OpcUa_Boolean_Clear(xValue) *(xValue) = OpcUa_False;

/*============================================================================
 * The SByte type
 *===========================================================================*/

#define OpcUa_SByte_Initialize(xValue) *(xValue) = (OpcUa_SByte)0;

#define OpcUa_SByte_Clear(xValue) *(xValue) = (OpcUa_SByte)0;

/*============================================================================
 * The Byte type
 *===========================================================================*/

#define OpcUa_Byte_Initialize(xValue) *(xValue) = (OpcUa_Byte)0;

#define OpcUa_Byte_Clear(xValue) *(xValue) = (OpcUa_Byte)0;

/*============================================================================
 * The Int16 type
 *===========================================================================*/

#define OpcUa_Int16_Initialize(xValue) *(xValue) = (OpcUa_Int16)0;

#define OpcUa_Int16_Clear(xValue) *(xValue) = (OpcUa_Int16)0;

/*============================================================================
 * The UInt16 type
 *===========================================================================*/

#define OpcUa_UInt16_Initialize(xValue) *(xValue) = (OpcUa_UInt16)0;

#define OpcUa_UInt16_Clear(xValue) *(xValue) = (OpcUa_UInt16)0;

/*============================================================================
 * The Int32 type
 *===========================================================================*/

#define OpcUa_Int32_Initialize(xValue) *(xValue) = (OpcUa_Int32)0;

#define OpcUa_Int32_Clear(xValue) *(xValue) = (OpcUa_Int32)0;

/*============================================================================
 * The UInt32 type
 *===========================================================================*/

#define OpcUa_UInt32_Initialize(xValue) *(xValue) = (OpcUa_UInt32)0;

#define OpcUa_UInt32_Clear(xValue) *(xValue) = (OpcUa_UInt32)0;

/*============================================================================
 * The Int64 type
 *===========================================================================*/

#if OPCUA_USE_NATIVE_64BIT_INTEGERS

#define OpcUa_Int64_Initialize(xValue) *(xValue) = (OpcUa_Int64)0;

#define OpcUa_Int64_Clear(xValue) *(xValue) = (OpcUa_Int64)0;

#else /* OPCUA_USE_NATIVE_64BIT_INTEGERS */

#define OpcUa_Int64_Initialize(xValue) OpcUa_MemSet(xValue, 0, sizeof(OpcUa_Int64));

#define OpcUa_Int64_Clear(xValue) OpcUa_MemSet(xValue, 0, sizeof(OpcUa_Int64));

#endif /* OPCUA_USE_NATIVE_64BIT_INTEGERS */

/*============================================================================
 * The UInt64 type
 *===========================================================================*/

#if OPCUA_USE_NATIVE_64BIT_INTEGERS

#define OpcUa_UInt64_Initialize(xValue) *(xValue) = (OpcUa_UInt64)0;

#define OpcUa_UInt64_Clear(xValue) *(xValue) = (OpcUa_UInt64)0;

#else /* OPCUA_USE_NATIVE_64BIT_INTEGERS */

#define OpcUa_UInt64_Initialize(xValue) OpcUa_MemSet(xValue, 0, sizeof(OpcUa_UInt64));

#define OpcUa_UInt64_Clear(xValue) OpcUa_MemSet(xValue, 0, sizeof(OpcUa_UInt64));

#endif /* OPCUA_USE_NATIVE_64BIT_INTEGERS */

/*============================================================================
 * The Float type
 *===========================================================================*/

#define OpcUa_Float_Initialize(xValue) *(xValue) = (OpcUa_Float)0.0;

#define OpcUa_Float_Clear(xValue) *(xValue) = (OpcUa_Float)0.0;

/*============================================================================
 * The Double type
 *===========================================================================*/

#define OpcUa_Double_Initialize(xValue) *(xValue) = (OpcUa_Double)0.0;

#define OpcUa_Double_Clear(xValue) *(xValue) = (OpcUa_Double)0.0;

/*============================================================================
 * The String type
 *===========================================================================*/
/* see opcua_string.h */

/*============================================================================
 * The DateTime type
 *===========================================================================*/

#define OpcUa_DateTime_Initialize(xValue) OpcUa_MemSet(xValue, 0, sizeof(OpcUa_DateTime));

#define OpcUa_DateTime_Clear(xValue) OpcUa_MemSet(xValue, 0, sizeof(OpcUa_DateTime));

/*============================================================================
 * The Guid type
 *===========================================================================*/

#define OpcUa_Guid_Initialize(xValue) *(xValue) = OpcUa_Guid_Null;

#define OpcUa_Guid_Clear(xValue) *(xValue) = OpcUa_Guid_Null;

/*============================================================================
 * The ByteString type
 *===========================================================================*/

OPCUA_EXPORT OpcUa_Void OpcUa_ByteString_Initialize(OpcUa_ByteString* value);

OPCUA_EXPORT OpcUa_Void OpcUa_ByteString_Clear(OpcUa_ByteString* value);

/*============================================================================
 * The XmlElement type
 *===========================================================================*/

typedef OpcUa_ByteString OpcUa_XmlElement;

#define OpcUa_XmlElement_Initialize(xValue) OpcUa_ByteString_Initialize((OpcUa_ByteString*)xValue);

#define OpcUa_XmlElement_Clear(xValue) OpcUa_ByteString_Clear((OpcUa_ByteString*)xValue);

/*============================================================================
 * The NodeId type
 *===========================================================================*/

/* The set of known node identifier types */
typedef enum _OpcUa_IdentifierType
{
    OpcUa_IdentifierType_Numeric = 0x00,
    OpcUa_IdentifierType_String  = 0x01,
    OpcUa_IdentifierType_Guid    = 0x02,
    OpcUa_IdentifierType_Opaque  = 0x03
}
OpcUa_IdentifierType;

typedef struct _OpcUa_NodeId
{
    OpcUa_UInt16 IdentifierType;
    OpcUa_UInt16 NamespaceIndex;

    union
    {
        OpcUa_UInt32     Numeric;
        OpcUa_String     String;
        OpcUa_Guid*      Guid;
        OpcUa_ByteString ByteString;
    } 
    Identifier;
}
OpcUa_NodeId;

OPCUA_EXPORT OpcUa_Void OpcUa_NodeId_Initialize(OpcUa_NodeId* pValue);

OPCUA_EXPORT OpcUa_Void OpcUa_NodeId_Clear(OpcUa_NodeId* pValue);

OPCUA_EXPORT OpcUa_Boolean OpcUa_NodeId_IsNull(OpcUa_NodeId* pValue);

/*============================================================================
 * The ExpandedNodeId type
 *===========================================================================*/
typedef struct _OpcUa_ExpandedNodeId
{
    OpcUa_NodeId NodeId;
    OpcUa_String NamespaceUri;
    OpcUa_UInt32 ServerIndex;
}
OpcUa_ExpandedNodeId;

OPCUA_EXPORT OpcUa_Void OpcUa_ExpandedNodeId_Initialize(OpcUa_ExpandedNodeId* pValue);

OPCUA_EXPORT OpcUa_Void OpcUa_ExpandedNodeId_Clear(OpcUa_ExpandedNodeId* pValue);

OPCUA_EXPORT OpcUa_Boolean OpcUa_ExpandedNodeId_IsNull(OpcUa_ExpandedNodeId* pValue);

/*============================================================================
 * The StatusCode type
 *===========================================================================*/

#define OpcUa_StatusCode_Initialize(xValue) *(xValue) = (OpcUa_StatusCode)0;

#define OpcUa_StatusCode_Clear(xValue) *(xValue) = (OpcUa_StatusCode)0;

/*============================================================================
 * The DiagnosticsInfo type
 *===========================================================================*/
typedef struct _OpcUa_DiagnosticInfo
{
    OpcUa_Int32                   SymbolicId;
    OpcUa_Int32                   NamespaceUri;
    OpcUa_Int32                   Locale;
    OpcUa_Int32                   LocalizedText;
    OpcUa_String                  AdditionalInfo;
    OpcUa_StatusCode              InnerStatusCode;
    struct _OpcUa_DiagnosticInfo* InnerDiagnosticInfo;
}
OpcUa_DiagnosticInfo;

OPCUA_EXPORT OpcUa_Void OpcUa_DiagnosticInfo_Initialize(OpcUa_DiagnosticInfo* value);

OPCUA_EXPORT OpcUa_Void OpcUa_DiagnosticInfo_Clear(OpcUa_DiagnosticInfo* value);

/*============================================================================
 * The LocalizedText structure.
 *===========================================================================*/
typedef struct _OpcUa_LocalizedText
{
    OpcUa_String Locale;
    OpcUa_String Text;
}
OpcUa_LocalizedText;

OPCUA_EXPORT OpcUa_Void OpcUa_LocalizedText_Initialize(OpcUa_LocalizedText* pValue);

OPCUA_EXPORT OpcUa_Void OpcUa_LocalizedText_Clear(OpcUa_LocalizedText* pValue);

/*============================================================================
 * The QualifiedName structure.
 *===========================================================================*/
typedef struct _OpcUa_QualifiedName
{
    OpcUa_UInt16 NamespaceIndex;
    OpcUa_UInt16 Reserved;
    OpcUa_String Name;
}
OpcUa_QualifiedName;

OPCUA_EXPORT OpcUa_Void OpcUa_QualifiedName_Initialize(OpcUa_QualifiedName* pValue);

OPCUA_EXPORT OpcUa_Void OpcUa_QualifiedName_Clear(OpcUa_QualifiedName* pValue);

/*============================================================================
 * The OpcUa_ExtensionObject type
 *===========================================================================*/
struct _OpcUa_EncodeableType;

typedef enum _OpcUa_ExtensionObjectEncoding
{
    OpcUa_ExtensionObjectEncoding_None = 0,
    OpcUa_ExtensionObjectEncoding_Binary = 1,
    OpcUa_ExtensionObjectEncoding_Xml = 2,
    OpcUa_ExtensionObjectEncoding_EncodeableObject = 3
}
OpcUa_ExtensionObjectEncoding;

typedef struct _OpcUa_ExtensionObject
{
    /*! @brief The full data type identifier. */
    OpcUa_ExpandedNodeId TypeId;
    
    /*! @brief The encoding used for the body. */
    OpcUa_ExtensionObjectEncoding Encoding;

    /*! @brief The body of the extension object. */
    union _OpcUa_ExtensionObject_Body
    {
        /*! @brief A pre-encoded binary body. */
        OpcUa_ByteString Binary;

        /*! @brief A pre-encoded XML body. */
        OpcUa_XmlElement Xml;

        struct _OpcUa_EncodeableObjectBody
        {           
            /*! @brief The object contained in the extension object. */
            OpcUa_Void* Object;
            
            /*! @brief Provides information necessary to encode/decode the object. */
            struct _OpcUa_EncodeableType* Type;
        }
        EncodeableObject;
    }
    Body;

    /*! @brief The length of the encoded body in bytes (updated automatically when GetSize is called). */   
    OpcUa_Int32 BodySize;
}
OpcUa_ExtensionObject;

OPCUA_EXPORT OpcUa_Void OpcUa_ExtensionObject_Create(OpcUa_ExtensionObject** value);

OPCUA_EXPORT OpcUa_Void OpcUa_ExtensionObject_Initialize(OpcUa_ExtensionObject* value);

OPCUA_EXPORT OpcUa_Void OpcUa_ExtensionObject_Clear(OpcUa_ExtensionObject* value);

OPCUA_EXPORT OpcUa_Void OpcUa_ExtensionObject_Delete(OpcUa_ExtensionObject** value);

/*============================================================================
 * The Variant type
 *===========================================================================*/
struct _OpcUa_Variant;
struct _OpcUa_DataValue;

/* A union that contains arrays of one of the built in types. */
typedef union _OpcUa_VariantArrayUnion
{
    OpcUa_Void*              Array;
    OpcUa_Boolean*           BooleanArray;
    OpcUa_SByte*             SByteArray;
    OpcUa_Byte*              ByteArray;
    OpcUa_Int16*             Int16Array;
    OpcUa_UInt16*            UInt16Array;
    OpcUa_Int32*             Int32Array;
    OpcUa_UInt32*            UInt32Array;
    OpcUa_Int64*             Int64Array;
    OpcUa_UInt64*            UInt64Array;
    OpcUa_Float*             FloatArray;
    OpcUa_Double*            DoubleArray;
    OpcUa_String*            StringArray;
    OpcUa_DateTime*          DateTimeArray;
    OpcUa_Guid*              GuidArray;
    OpcUa_ByteString*        ByteStringArray;
    OpcUa_ByteString*        XmlElementArray;
    OpcUa_NodeId*            NodeIdArray;
    OpcUa_ExpandedNodeId*    ExpandedNodeIdArray;
    OpcUa_StatusCode*        StatusCodeArray;
    OpcUa_QualifiedName*     QualifiedNameArray;
    OpcUa_LocalizedText*     LocalizedTextArray;
    OpcUa_ExtensionObject*   ExtensionObjectArray;
    struct _OpcUa_DataValue* DataValueArray;
    struct _OpcUa_Variant*   VariantArray;
}
OpcUa_VariantArrayUnion;

/* A union that contains a one dimensional array of one of the built in types. */
typedef struct _OpcUa_VariantArrayValue
{
    /* The total number of elements in all dimensions. */
    OpcUa_Int32  Length;

    /* The data stored in the array. */
    OpcUa_VariantArrayUnion Value;
}
OpcUa_VariantArrayValue;

/* A union that contains a multi-dimensional array of one of the built in types. */
typedef struct _OpcUa_VariantMatrixValue
{
    /* The number of dimensions in the array. */
    OpcUa_Int32 NoOfDimensions;

    /* The length of each dimension. */
    OpcUa_Int32* Dimensions;

    /* The data stored in the array. 
    
       The higher rank dimensions appear in the array first.
       e.g. a array with dimensions [2,2,2] is written in this order: 
       [0,0,0], [0,0,1], [0,1,0], [0,1,1], [1,0,0], [1,0,1], [1,1,0], [1,1,1]

       Using [3] to access the pointer stored in this field would return element [0,1,1] */
    OpcUa_VariantArrayUnion Value;
}
OpcUa_VariantMatrixValue;

/* Returns the total number of elements stored in a matrix value. */
OPCUA_EXPORT OpcUa_Int32 OpcUa_VariantMatrix_GetElementCount(OpcUa_VariantMatrixValue* pValue);

/* A union that contains one of the built in types. */
typedef union _OpcUa_VariantUnion
{
    OpcUa_Boolean            Boolean;
    OpcUa_SByte              SByte;
    OpcUa_Byte               Byte;
    OpcUa_Int16              Int16;
    OpcUa_UInt16             UInt16;
    OpcUa_Int32              Int32;
    OpcUa_UInt32             UInt32;
    OpcUa_Int64              Int64;
    OpcUa_UInt64             UInt64;
    OpcUa_Float              Float;
    OpcUa_Double             Double;
    OpcUa_DateTime           DateTime;
    OpcUa_String             String;
    OpcUa_Guid*              Guid;
    OpcUa_ByteString         ByteString;
    OpcUa_XmlElement         XmlElement;
    OpcUa_NodeId*            NodeId;
    OpcUa_ExpandedNodeId*    ExpandedNodeId;
    OpcUa_StatusCode         StatusCode;
    OpcUa_QualifiedName*     QualifiedName;
    OpcUa_LocalizedText*     LocalizedText;
    OpcUa_ExtensionObject*   ExtensionObject;
    struct _OpcUa_DataValue* DataValue;
    OpcUa_VariantArrayValue  Array;
    OpcUa_VariantMatrixValue Matrix;
}
OpcUa_VariantUnion;

#define OpcUa_VariantArrayType_Scalar 0x00
#define OpcUa_VariantArrayType_Array  0x01
#define OpcUa_VariantArrayType_Matrix 0x02

typedef struct _OpcUa_Variant
{
    /* Indicates the datatype stored in the Variant. This is always one of the OpcUa_BuiltInType values. */
    /* This is the datatype of a single element if the Variant contains an array. */
    OpcUa_Byte          Datatype;

    /* A flag indicating that an array with one or more dimensions is stored in the Variant. */
    OpcUa_Byte          ArrayType;

    /* Not used. Must be ignored. */
    OpcUa_UInt16        Reserved;

    /* The value stored in the Variant. */
    OpcUa_VariantUnion  Value;
}
OpcUa_Variant;

OPCUA_EXPORT OpcUa_Void OpcUa_Variant_Initialize(OpcUa_Variant* value);

OPCUA_EXPORT OpcUa_Void OpcUa_Variant_Clear(OpcUa_Variant* value);

#define OpcUa_Variant_InitializeArray(xValue, xLength) OpcUa_MemSet(xValue, 0, (xLength)*sizeof(OpcUa_Variant));

#define OpcUa_Variant_ClearArray(xValue, xLength) OpcUa_ClearArray(xValue, xLength, OpcUa_Variant);

/*============================================================================
 * The DataValue type
 *===========================================================================*/
typedef struct _OpcUa_DataValue
{
    OpcUa_Variant    Value;
    OpcUa_StatusCode StatusCode;
    OpcUa_DateTime   SourceTimestamp;
    OpcUa_DateTime   ServerTimestamp;
    OpcUa_UInt16     SourcePicoseconds;
    OpcUa_UInt16     ServerPicoseconds;
}
OpcUa_DataValue;

OPCUA_EXPORT OpcUa_Void OpcUa_DataValue_Initialize(OpcUa_DataValue* value);

OPCUA_EXPORT OpcUa_Void OpcUa_DataValue_Clear(OpcUa_DataValue* value);

#define OpcUa_DataValue_InitializeArray(xValue, xLength) OpcUa_MemSet(xValue, 0, (xLength)*sizeof(OpcUa_DataValue));

#define OpcUa_DataValue_ClearArray(xValue, xLength) OpcUa_ClearArray(xValue, xLength, OpcUa_DataValue);

/*============================================================================
 * OpcUa_Field_Initialize
 *===========================================================================*/
#define OpcUa_Field_Initialize(xType, xName) OpcUa_##xType##_Initialize(&a_pValue->xName);

/*============================================================================
 * OpcUa_Field_InitializeEncodeable
 *===========================================================================*/
#define OpcUa_Field_InitializeEncodeable(xType, xName) xType##_Initialize(&a_pValue->xName);

/*============================================================================
 * OpcUa_Field_InitializeEnumerated
 *===========================================================================*/
#define OpcUa_Field_InitializeEnumerated(xType, xName) xType##_Initialize(&a_pValue->xName);

/*============================================================================
 * OpcUa_Field_InitializeArray
 *===========================================================================*/
#define OpcUa_Field_InitializeArray(xType, xName) \
{ \
    a_pValue->xName = OpcUa_Null; \
    a_pValue->NoOf##xName = 0; \
}

/*============================================================================
 * OpcUa_Field_InitializeEncodeableArray
 *===========================================================================*/
#define OpcUa_Field_InitializeEncodeableArray(xType, xName) OpcUa_Field_InitializeArray(xType, xName)

/*============================================================================
 * OpcUa_Field_InitializeEnumeratedArray
 *===========================================================================*/
#define OpcUa_Field_InitializeEnumeratedArray(xType, xName) OpcUa_Field_InitializeArray(xType, xName)

/*============================================================================
 * OpcUa_Field_Clear
 *===========================================================================*/
#define OpcUa_Field_Clear(xType, xName) OpcUa_##xType##_Clear(&a_pValue->xName);

/*============================================================================
 * OpcUa_Field_ClearEncodeable
 *===========================================================================*/
#define OpcUa_Field_ClearEncodeable(xType, xName) xType##_Clear(&a_pValue->xName);

/*============================================================================
 * OpcUa_Field_ClearEnumerated
 *===========================================================================*/
#define OpcUa_Field_ClearEnumerated(xType, xName) xType##_Clear(&a_pValue->xName);

/*============================================================================
 * OpcUa_Field_ClearArray
 *===========================================================================*/
#define OpcUa_Field_ClearArray(xType, xName)\
{ \
    int ii; \
\
    for (ii = 0; ii < a_pValue->NoOf##xName && a_pValue->xName != OpcUa_Null; ii++) \
    { \
        OpcUa_##xType##_Clear(&(a_pValue->xName[ii])); \
    } \
\
    OpcUa_Free(a_pValue->xName); \
\
    a_pValue->xName = OpcUa_Null; \
    a_pValue->NoOf##xName = 0; \
}

/*============================================================================
 * OpcUa_Field_ClearEncodeableArray
 *===========================================================================*/
#define OpcUa_Field_ClearEncodeableArray(xType, xName) \
{ \
    int ii; \
\
    for (ii = 0; ii < a_pValue->NoOf##xName && a_pValue->xName != OpcUa_Null; ii++) \
    { \
        xType##_Clear(&(a_pValue->xName[ii])); \
    } \
\
    OpcUa_Free(a_pValue->xName); \
\
    a_pValue->xName = OpcUa_Null; \
    a_pValue->NoOf##xName = 0; \
}

/*============================================================================
 * OpcUa_Field_ClearEnumeratedArray
 *===========================================================================*/
#define OpcUa_Field_ClearEnumeratedArray(xType, xName) \
{ \
    OpcUa_Free(a_pValue->xName); \
    a_pValue->xName = OpcUa_Null; \
    a_pValue->NoOf##xName = 0; \
}

/*============================================================================
 * Flags that can be set for the EventNotifier attribute.
 *===========================================================================*/
  
/* The Object or View produces no event and has no event history. */
#define OpcUa_EventNotifiers_None 0x0

/* The Object or View produces event notifications. */
#define OpcUa_EventNotifiers_SubscribeToEvents 0x1

/* The Object has an event history which may be read. */
#define OpcUa_EventNotifiers_HistoryRead 0x4

/* The Object has an event history which may be updated. */
#define OpcUa_EventNotifiers_HistoryWrite 0x8

/*============================================================================
 * Flags that can be set for the AccessLevel attribute.
 *===========================================================================*/

/* The Variable value cannot be accessed and has no event history. */
#define OpcUa_AccessLevels_None 0x0

/* The current value of the Variable may be read.*/
#define OpcUa_AccessLevels_CurrentRead 0x1

/* The current value of the Variable may be written.*/
#define OpcUa_AccessLevels_CurrentWrite 0x2

/* The current value of the Variable may be read or written.*/
#define OpcUa_AccessLevels_CurrentReadOrWrite 0x3

/* The history for the Variable may be read.*/
#define OpcUa_AccessLevels_HistoryRead 0x4

/* The history for the Variable may be updated.*/
#define OpcUa_AccessLevels_HistoryWrite 0x8

/* The history value of the Variable may be read or updated. */
#define OpcUa_AccessLevels_HistoryReadOrWrite 0xC

/*============================================================================
 * Constants defined for the ValueRank attribute.
 *===========================================================================*/

/* The variable may be a scalar or a one dimensional array. */
#define OpcUa_ValueRanks_ScalarOrOneDimension -3
        
/* The variable may be a scalar or an array of any dimension. */
#define OpcUa_ValueRanks_Any -2

/* The variable is always a scalar. */
#define OpcUa_ValueRanks_Scalar -1

/* The variable is always an array with one or more dimensions. */
#define OpcUa_ValueRanks_OneOrMoreDimensions 0

/* The variable is always one dimensional array. */
#define OpcUa_ValueRanks_OneDimension 1

/* The variable is always an array with two or more dimensions. */
#define OpcUa_ValueRanks_TwoDimensions 2
    
/*============================================================================
 *  The bit masks used to indicate the write access to the attributes for a node.
 *===========================================================================*/

/* No attributes are writeable. */
#define OpcUa_WriteAccessMasks_None 0

/* The BrowseName attribute is writeable. */
#define OpcUa_WriteAccessMasks_BrowseName 1

/* The DisplayName attribute is writeable. */
#define OpcUa_WriteAccessMasks_DisplayName 2

/* The Description attribute is writeable. */
#define OpcUa_WriteAccessMasks_Description 4

/* The IsAbstract attribute is writeable. */
#define OpcUa_WriteAccessMasks_IsAbstract 8

/* The Symmetric attribute is writeable. */
#define OpcUa_WriteAccessMasks_Symmetric 16

/* The InverseName attribute is writeable. */
#define OpcUa_WriteAccessMasks_InverseName 32

/* The ContainsNoLoops attribute is writeable. */
#define OpcUa_WriteAccessMasks_ContainsNoLoops 64

/* The EventNotifier attribute is writeable. */
#define OpcUa_WriteAccessMasks_EventNotifier 128

/* The DataType attribute is writeable. */
#define OpcUa_WriteAccessMasks_DataType 256

/* The ValueRank attribute is writeable. */
#define OpcUa_WriteAccessMasks_ValueRank 512

/* The AccessLevel attribute is writeable. */
#define OpcUa_WriteAccessMasks_AccessLevel 1024

/* The UserAccessLevel attribute is writeable. */
#define OpcUa_WriteAccessMasks_UserAccessLevel 2048

/* The MinimumSamplingInterval attribute is writeable. */
#define OpcUa_WriteAccessMasks_MinimumSamplingInterval 4096

/* The Historizing attribute is writeable. */
#define OpcUa_WriteAccessMasks_Historizing 8192

/* The Executable attribute is writeable. */
#define OpcUa_WriteAccessMasks_Executable 16384

/* The UserExecutable attribute is writeable. */
#define OpcUa_WriteAccessMasks_UserExecutable 32768

/* The WriteAccess attribute is writeable. */
#define OpcUa_WriteAccessMasks_WriteAccess 65536

/* The UserWriteAccess attribute is writeable. */
#define OpcUa_WriteAccessMasks_UserWriteAccess 131072

/*============================================================================
 * Constants defined for the MinimumSamplingInterval attribute.
 *===========================================================================*/

/* The server does not know how fast the value can be sampled. */
#define OpcUa_MinimumSamplingIntervals_Indeterminate -1

/* The server can sample the variable continuously. */
#define OpcUa_MinimumSamplingIntervals_Continuous 0

/*============================================================================
 * Constants defined for the DiagnosticsMasks parameter.
 *===========================================================================*/

#define OpcUa_DiagnosticsMasks_ServiceSymbolicId 1
#define OpcUa_DiagnosticsMasks_ServiceLocalizedText 2
#define OpcUa_DiagnosticsMasks_ServiceAdditionalInfo 4
#define OpcUa_DiagnosticsMasks_ServiceInnerStatusCode 8
#define OpcUa_DiagnosticsMasks_ServiceInnerDiagnostics 16
#define OpcUa_DiagnosticsMasks_ServiceSymbolicIdAndText 3
#define OpcUa_DiagnosticsMasks_ServiceNoInnerStatus 15
#define OpcUa_DiagnosticsMasks_ServiceAll 31
#define OpcUa_DiagnosticsMasks_OperationSymbolicId 32
#define OpcUa_DiagnosticsMasks_OperationLocalizedText 64
#define OpcUa_DiagnosticsMasks_OperationAdditionalInfo 128
#define OpcUa_DiagnosticsMasks_OperationInnerStatusCode 256
#define OpcUa_DiagnosticsMasks_OperationInnerDiagnostics 512
#define OpcUa_DiagnosticsMasks_OperationSymbolicIdAndText 96
#define OpcUa_DiagnosticsMasks_OperationNoInnerStatus 224
#define OpcUa_DiagnosticsMasks_OperationAll 992
#define OpcUa_DiagnosticsMasks_SymbolicId 33
#define OpcUa_DiagnosticsMasks_LocalizedText 66
#define OpcUa_DiagnosticsMasks_AdditionalInfo 132
#define OpcUa_DiagnosticsMasks_InnerStatusCode 264
#define OpcUa_DiagnosticsMasks_InnerDiagnostics 528
#define OpcUa_DiagnosticsMasks_SymbolicIdAndText 99
#define OpcUa_DiagnosticsMasks_NoInnerStatus 239
#define OpcUa_DiagnosticsMasks_All 1023

OPCUA_END_EXTERN_C

#endif /* _OpcUa_BuiltInTypes_H_ */
