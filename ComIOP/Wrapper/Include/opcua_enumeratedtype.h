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

#ifndef _OpcUa_EnumeratedType_H_
#define _OpcUa_EnumeratedType_H_ 1

OPCUA_BEGIN_EXTERN_C

/** 
  @brief Describes an enumerated valie.
*/
typedef struct _OpcUa_EnumeratedValue
{
    /*! @brief The name. */
    OpcUa_StringA Name;

    /*! @brief The value associated with the name. */
    OpcUa_Int32 Value;
}
OpcUa_EnumeratedValue;

/** 
  @brief Describes an enumerated type.
*/
typedef struct _OpcUa_EnumeratedType
{
    /*! @brief The name of the enumerated type. */
    OpcUa_StringA TypeName;

    /*! @brief A null terminated list of values. */
    OpcUa_EnumeratedValue* Values;
}
OpcUa_EnumeratedType;

/** 
  @brief Finds the name associated with a value of an enumerated type.

  @param pType  [in]  The enumerated type to search.
  @param nValue [in]  The value to look for.
  @param pName  [out] The name associated with the value.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_EnumeratedType_FindName(
    OpcUa_EnumeratedType* pType,
    OpcUa_Int32           nValue,
    OpcUa_StringA*        pName);

/** 
  @brief Finds the value associated with a name for an enumerated type.

  @param pType  [in]  The enumerated type to search.
  @param sName  [in]  The name to look for.
  @param pValue [out] The value associated with the name.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_EnumeratedType_FindValue(
    OpcUa_EnumeratedType* pType,
    OpcUa_StringA         sName,
    OpcUa_Int32*          pValue);

OPCUA_END_EXTERN_C

#endif /* _OpcUa_EnumeratedType_H_ */
