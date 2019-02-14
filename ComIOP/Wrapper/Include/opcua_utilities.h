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

#ifndef _OpcUa_Utilities_H_
#define _OpcUa_Utilities_H_ 1

#include <opcua_platformdefs.h>

OPCUA_BEGIN_EXTERN_C

enum _OpcUa_ProtocolType
{
    OpcUa_ProtocolType_Invalid,
    OpcUa_ProtocolType_Http,
    OpcUa_ProtocolType_Tcp
};
typedef enum _OpcUa_ProtocolType OpcUa_ProtocolType;


/** 
 * @brief Sorts an array.
 *
 * @param pElements     [in] The array of elements to sort.
 * @param nElementCount [in] The number of elements in the array.
 * @param nElementSize  [in] The size a single element in the array.
 * @param pfnCompare    [in] The function used to compare elements.
 * @param pContext      [in] A context that is passed to the compare function.
 */
OPCUA_EXPORT 
OpcUa_StatusCode OpcUa_QSort(   OpcUa_Void*       pElements, 
                                OpcUa_UInt32      nElementCount, 
                                OpcUa_UInt32      nElementSize, 
                                OpcUa_PfnCompare* pfnCompare, 
                                OpcUa_Void*       pContext);

/** 
 * @brief Searches a sorted array.
 *
 * @param pKey          [in] The element to find.
 * @param pElements     [in] The array of elements to sort.
 * @param nElementCount [in] The number of elements in the array.
 * @param nElementSize  [in] The size a single element in the array.
 * @param pfnCompare    [in] The function used to compare elements.
 * @param pContext      [in] A context that is passed to the compare function.
 */
OPCUA_EXPORT 
OpcUa_Void* OpcUa_BSearch(  OpcUa_Void*       pKey,
                            OpcUa_Void*       pElements, 
                            OpcUa_UInt32      nElementCount, 
                            OpcUa_UInt32      nElementSize, 
                            OpcUa_PfnCompare* pfnCompare, 
                            OpcUa_Void*       pContext);

/** 
 * @brief Returns the CRT errno constant.
 */
OPCUA_EXPORT 
OpcUa_UInt32 OpcUa_GetLastError();

/** 
 * @brief Returns the number of milliseconds since the system or process was started.
 */
OPCUA_EXPORT 
OpcUa_UInt32 OpcUa_GetTickCount();

/** 
 * @brief Convert string to integer.
 */
#define OpcUa_CharAToInt(xChar) OpcUa_ProxyStub_g_PlatformLayerCalltable->CharToInt(xChar)

OPCUA_END_EXTERN_C

#endif /* _OpcUa_Utilities_H_ */
