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

#ifndef _OpcUa_Memory_H_
#define _OpcUa_Memory_H_ 1

OPCUA_BEGIN_EXTERN_C

/** 
 * @brief Allocates a new block of memory.
 *
 * @param nSize [in] The size of the block to allocate.
*/
OPCUA_EXPORT OpcUa_Void* OPCUA_DLLCALL OpcUa_Memory_Alloc(OpcUa_UInt32 nSize);

/** 
 * @brief Reallocates a block of memory
 *
 * @param pBuffer [in] The existing memory block.
 * @param nSize   [in] The size of the block to allocate.
*/
OPCUA_EXPORT OpcUa_Void* OPCUA_DLLCALL OpcUa_Memory_ReAlloc(OpcUa_Void*  pBuffer, 
                                                            OpcUa_UInt32 nSize);

/** 
 * @brief Frees a block of memory.
 *
 * @param pvBuffer [in] The existing memory block.
*/
OPCUA_EXPORT OpcUa_Void OPCUA_DLLCALL OpcUa_Memory_Free(OpcUa_Void* pvBuffer);

/** 
 * @brief Copies a block of memory.
 *
 * @param pBuffer      [in] The destination memory block.
 * @param nSizeInBytes [in] The size of the destination memory block. 
 * @param pSource      [in] The memory block being copied.
 * @param nCount       [in] The number of bytes to copy.
 *
 * @return StatusCode:
 *   OpcUa_BadInvalidArgument if Buffer or Source equals OpcUa_Null;
 *   OpcUa_BadOutOfRange      if number of bytes to copy greater nSizeInBytes
*/
OPCUA_EXPORT OpcUa_StatusCode OPCUA_DLLCALL OpcUa_Memory_MemCpy(   OpcUa_Void*  pBuffer,
    OpcUa_UInt32 nSizeInBytes,
    OpcUa_Void*  pSource,
    OpcUa_UInt32 nCount);

OPCUA_END_EXTERN_C

#endif /* _OpcUa_Memory_H_ */
