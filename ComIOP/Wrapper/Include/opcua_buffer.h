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

#ifndef _OpcUa_Buffer_H_
#define _OpcUa_Buffer_H_ 1

OPCUA_BEGIN_EXTERN_C

/*============================================================================
 * OpcUa_Buffer
 *
 * An expandable memory buffer
 *
 * Data       - The memory buffer that stores the stream data.
 * Size       - The size of the memory buffer.
 * Position   - The current position in the memory buffer.
 * EndOfData  - The position just after the last piece of data in the buffer.
 * BlockSize  - The size of block that should be added to the buffer when required.
 * MaxSize    - The maximum size of the buffer.
 * FreeBuffer - Whether the buffer should be free when the stream is destroyed.
 *
 * If the buffer size is fixed then MaxSize is 0.
 *===========================================================================*/
typedef struct _OpcUa_Buffer
{
    OpcUa_UInt32  SanityCheck;
    OpcUa_UInt32  Size;
    OpcUa_UInt32  EndOfData;
    OpcUa_UInt32  Position;
    OpcUa_UInt32  BlockSize;
    OpcUa_UInt32  MaxSize;
    OpcUa_Byte*   Data;
    OpcUa_Boolean FreeBuffer;
}
OpcUa_Buffer;

/** 
  @brief Creates a new buffer.
 
  @param data       [in]  A memory block to store in the buffer. 
  @param dataSize   [in]  The size of the memory block.
  @param blockSize  [in]  The size of block allocate when more space is required.
  @param maxSize    [in]  The maximum size of the buffer (zero means no limit).
  @param freeBuffer [in]  Wether the memory block should be freed when the buffer is freed.
  @param ppBuffer  [out] The new buffer.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Buffer_Create(
    OpcUa_Byte*    data,
    OpcUa_UInt32   dataSize,
    OpcUa_UInt32   blockSize,
    OpcUa_UInt32   maxSize,
    OpcUa_Boolean  freeBuffer,
    OpcUa_Buffer** ppBuffer);

/** 
  @brief Initializes an allocated buffer.

  @param pBuffer     [bi]  The buffer.
  @param pData       [in]  A memory block to store in the buffer. 
  @param uDataSize   [in]  The size of the memory block.
  @param uBlockSize  [in]  The size of block allocate when more space is required.
  @param uMaxSize    [in]  The maximum size of the buffer (zero means no limit).
  @param bFreeBuffer [in]  Wether the memory block should be freed when the buffer is freed.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Buffer_Initialize(
    OpcUa_Buffer* pBuffer,
    OpcUa_Byte*   pData,
    OpcUa_UInt32  uDataSize,
    OpcUa_UInt32  uBlockSize,
    OpcUa_UInt32  uMaxSize,
    OpcUa_Boolean bFreeBuffer);


/** 
  @brief Frees an existing buffer.
 
  @param ppBuffer [in/out] The buffer to free. 
*/
OPCUA_EXPORT OpcUa_Void OpcUa_Buffer_Delete(OpcUa_Buffer** ppBuffer);

OPCUA_EXPORT OpcUa_Void OpcUa_Buffer_Clear(OpcUa_Buffer* pBuffer);

/** 
  @brief Reads data from the buffer.
 
  @param handle [in]     The buffer to read.
  @param data   [in]     The destination for the data read from the buffer.
  @param count  [in/out] The size of the data/number of bytes copied.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Buffer_Read(
    OpcUa_Handle  handle,
    OpcUa_Byte*   data,
    OpcUa_UInt32* count);

/** 
  @brief Writes data to a buffer.
 
  @param handle [in] The buffer to write.
  @param data   [in] The data to copy.
  @param count  [in] The size of the data.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Buffer_Write(
    OpcUa_Handle handle,
    OpcUa_Byte*  data,
    OpcUa_UInt32 count);

/** 
  @brief Gets the current position in the buffer.
 
  @param handle   [in]  The buffer.
  @param position [out] The current position.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Buffer_GetPosition(
    OpcUa_Handle  handle,
    OpcUa_UInt32* position);

/** 
  @brief Sets the current position in the buffer.
 
  @param handle   [in] The buffer.
  @param position [in] The new position.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Buffer_SetPosition(
    OpcUa_Handle handle,
    OpcUa_UInt32 position);

/** 
  @brief Gets a pointer to data inside the buffer.
 
  @param handle [in]  The buffer.
  @param data   [out] The data block. (optional)
  @param length [out] The number of bytes of valid data in the block. (optional)
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Buffer_GetData(
    OpcUa_Handle  handle,
    OpcUa_Byte**  data,
    OpcUa_UInt32* length);

/** 
  @brief Skip a given amount of bytes. (same as read without data copying)
 
  @param handle [in]  The buffer.
  @param length [out] The number of bytes of valid data in the block.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Buffer_Skip(
    OpcUa_Handle  handle,
    OpcUa_UInt32  length);

/** 
  @brief Resets the internal buffer to an empty state.

  End of data and position a set to the beginning of the data buffer.
 
  @param pBuffer [in]  The buffer.
*/
OpcUa_StatusCode OpcUa_Buffer_SetEmpty(OpcUa_Buffer* pBuffer);

/** 
  @brief Checks, if the buffer contains data.

  @param pBuffer [in]  The buffer.
*/
OpcUa_Boolean OpcUa_Buffer_IsEmpty( OpcUa_Buffer* pBuffer);

/** 
  @brief Sets the end of valid data in the buffer. Must not be larger than buffer size.
 
  @param handle   [in] The buffer.
  @param position [in] The new end of data.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Buffer_SetEndOfData(
    OpcUa_Handle pBuffer,
    OpcUa_UInt32 uEndOfData);


/** 
  @brief Set the position to the start of the buffer.
*/
#define OpcUa_BufferPosition_Start 0

/** 
  @brief Set the position to the end of the buffer.
*/
#define OpcUa_BufferPosition_End 0xFFFFFFFF

OPCUA_END_EXTERN_C

#endif /* _OpcUa_Buffer_H_ */
