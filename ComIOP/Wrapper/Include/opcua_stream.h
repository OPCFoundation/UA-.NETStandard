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

#ifndef _OpcUa_Stream_H_
#define _OpcUa_Stream_H_ 1

#include <opcua_buffer.h>

OPCUA_BEGIN_EXTERN_C

struct _OpcUa_Stream;
struct _OpcUa_InputStream;
struct _OpcUa_OutputStream;

/** 
  @brief Called by the source stream when data is available for reading.
 
  @param istrm        [in] The stream.
  @param callbackData [in] The callback data specifed in the Read call.
*/
typedef OpcUa_StatusCode (OpcUa_Stream_PfnOnReadyToRead)(
    struct _OpcUa_InputStream* istrm, 
    OpcUa_Void*                callbackData);

/** 
  @brief Called by the source stream when stream is ready for writing.
 
  @param ostrm        [in] The stream.
  @param callbackData [in] The callback data specifed in the Write call.
*/
typedef OpcUa_StatusCode (OpcUa_Stream_PfnOnReadyToWrite)(
    struct _OpcUa_OutputStream* ostrm, 
    OpcUa_Void*                 callbackData);

/** 
  @brief Reads data from the stream.
 
  If no data is available then count is set to zero.
  If at the end of the stream then return code is 'BadEndOfStream'.
 
  The callback must be provided for non-blocking streams. 
  If if the stream is non-blocking no data is currently available then return 
  code is 'BadStreamNoDataAvailable'.

  @param istrm        [in]     The stream.
  @param buffer       [in]     The buffer to copy the data into.
  @param count        [in/out] The length of the buffer/amount of data copied.
  @param callback     [in]     The callback to use when there is more data ready for reading.
  @param callbackData [in]     The data to pass to the callback.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Stream_Read(
    struct _OpcUa_InputStream*     istrm, 
    OpcUa_Byte*                    buffer, 
    OpcUa_UInt32*                  count, 
    OpcUa_Stream_PfnOnReadyToRead* callback, 
    OpcUa_Void*                    callbackData);

typedef OpcUa_StatusCode (OpcUa_Stream_PfnRead)(
    struct _OpcUa_InputStream*     istrm, 
    OpcUa_Byte*                    buffer, 
    OpcUa_UInt32*                  count, 
    OpcUa_Stream_PfnOnReadyToRead* callback, 
    OpcUa_Void*                    callbackData);

/** 
  @brief Writes data to the stream.
 
  @param ostrm  [in] The stream.
  @param buffer [in] The buffer to copy the data from.
  @param count  [in] The amount of data to write.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Stream_Write(
    struct _OpcUa_OutputStream* ostrm, 
    OpcUa_Byte*                 buffer, 
    OpcUa_UInt32                count);

typedef OpcUa_StatusCode (OpcUa_Stream_PfnWrite)(
    struct _OpcUa_OutputStream* ostrm, 
    OpcUa_Byte*                 buffer, 
    OpcUa_UInt32                count);

/** 
  @brief Flushes any data in the output buffer.
 
  @param ostrm    [in] The stream.
  @param lastCall [in] Whether this is the last time data will be written to the stream.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Stream_Flush(
    struct _OpcUa_OutputStream* ostrm,
    OpcUa_Boolean               lastCall);

typedef OpcUa_StatusCode (OpcUa_Stream_PfnFlush)(
    struct _OpcUa_OutputStream* ostrm,
    OpcUa_Boolean               lastCall);

/** 
  @brief Closes a stream an releases all resources allocated to it.
 
  During normal operation all streams must be closed. Deleting a stream without
  closing it could have unexpected side effects.

  @param strm [in] The stream.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Stream_Close(
    struct _OpcUa_Stream* strm);

typedef OpcUa_StatusCode (OpcUa_Stream_PfnClose)(
    struct _OpcUa_Stream* strm);

/** 
  @brief Frees the stream structure.

  This function aborts all I/O operations and frees all memory. It should be
  used only to clean up when the stream is not needed.
 
  @param strm [in] The stream to free.
*/
OPCUA_EXPORT OpcUa_Void OpcUa_Stream_Delete(
    struct _OpcUa_Stream** strm);

typedef OpcUa_Void (OpcUa_Stream_PfnDelete)(
    struct _OpcUa_Stream** strm);

/** 
  @brief Returns the current position in the stream.

  Returns the number of bytes written to/read from the stream even if the stream
  does not support SetPosition.
  
  @param strm     [in]  The stream.
  @param position [out] The current position.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Stream_GetPosition(
    struct _OpcUa_Stream* strm, 
    OpcUa_UInt32*         position);

typedef OpcUa_StatusCode (OpcUa_Stream_PfnGetPosition)(
    struct _OpcUa_Stream* strm, 
    OpcUa_UInt32*         position);

/** 
  @brief Sets the current position in the stream.

  Returns 'BadNotSupported' for streams that do not support seeking.

  Some streams will support partial seeks within the most recent buffer. If the
  position is set to 'Start' then the position should be set to the start of 
  available data. The callers can use GetPosition to determine the absolute position
  in the buffer.
  
  @param strm     [in] The stream.
  @param position [in] The new position.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Stream_SetPosition(
    struct _OpcUa_Stream* strm, 
    OpcUa_UInt32          position);

typedef OpcUa_StatusCode (OpcUa_Stream_PfnSetPosition)(
    struct _OpcUa_Stream* strm, 
    OpcUa_UInt32          position);

/** 
  @brief Get the internal buffer size of the stream.

  Returns 'BadNotSupported' for streams that do not support this operation.
 
  Some streams use fixed internal buffer sizes. Output streams cut messages
  at this border and it is the maximum length of incoming messages. Streams
  with virtually unlimited bufferspace return infinite.

  @param strm     [in] The stream.
  @param pLength [out] The maximum length of the chunk.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Stream_GetChunkLength(
    struct _OpcUa_Stream* strm, 
    OpcUa_UInt32*         pLength);

typedef OpcUa_StatusCode (OpcUa_Stream_PfnGetChunkLength)(
    struct _OpcUa_Stream* strm, 
    OpcUa_UInt32*         pLength);


/** 
  @brief Get the internal buffer of the stream.

  Returns 'BadNotSupported' for streams that do not support this operation.
 
  Some streams 

  @param strm   [in] The stream.
  @param buffer [in] The buffer to be attached to the stream.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Stream_AttachBuffer(
    struct _OpcUa_Stream*   strm,
    OpcUa_Buffer*           buffer);

typedef OpcUa_StatusCode (OpcUa_Stream_PfnAttachBuffer)(
    struct _OpcUa_Stream*   strm,
    OpcUa_Buffer*           buffer);

/** 
  @brief Set the internal buffer of the stream.

  Returns 'BadNotSupported' for streams that do not support this operation.
 
  Some streams 

  @param strm     [in] The stream.
  @param pBuffer [out] The buffer of the stream.
*/
OPCUA_EXPORT OpcUa_StatusCode OpcUa_Stream_DetachBuffer(
    struct _OpcUa_Stream*   strm,
    OpcUa_Buffer*           pBuffer);

typedef OpcUa_StatusCode (OpcUa_Stream_PfnDetachBuffer)(
    struct _OpcUa_Stream*   strm,
    OpcUa_Buffer*           pBuffer);

/** 
  @brief A generic stream.
*/
typedef struct _OpcUa_Stream
{
    /*! @brief The type of stream (Input or Output). */
    OpcUa_Int32 Type;

    /*! @brief An opaque handle that contain data specific to the stream implementation. */
    OpcUa_Handle Handle;

    /*! @brief Returns the current position in the stream. */
    OpcUa_Stream_PfnGetPosition* GetPosition;
    
    /*! @brief Sets the current position in the stream. */
    OpcUa_Stream_PfnSetPosition* SetPosition;
    
    /*! @brief Returns the internal chunk length. */
    OpcUa_Stream_PfnGetChunkLength* GetChunkLength;

    /*! @brief Detach the internal buffer. */
    OpcUa_Stream_PfnDetachBuffer* DetachBuffer;

    /*! @brief Attach an external buffer. */
    OpcUa_Stream_PfnAttachBuffer* AttachBuffer;

    /*! @brief Closes the stream. */
    OpcUa_Stream_PfnClose* Close;
    
    /*! @brief Frees the structure. */
    OpcUa_Stream_PfnDelete* Delete;
}
OpcUa_Stream;

/** 
  @brief An input stream.
*/
typedef struct _OpcUa_InputStream
{
    /*! @brief The type of stream (always OpcUa_StreamType_Input). */
    OpcUa_Int32 Type;

    /*! @brief An opaque handle that contain data specific to the stream implementation. */
    OpcUa_Handle Handle;

    /*! @brief Returns the current position in the stream. */
    OpcUa_Stream_PfnGetPosition* GetPosition;
    
    /*! @brief Sets the current position in the stream. */
    OpcUa_Stream_PfnSetPosition* SetPosition;
    
    /*! @brief Returns the internal chunk length. */
    OpcUa_Stream_PfnGetChunkLength* GetChunkLength;

    /*! @brief Detach the internal buffer. */
    OpcUa_Stream_PfnDetachBuffer* DetachBuffer;

    /*! @brief Attach an external buffer. */
    OpcUa_Stream_PfnAttachBuffer* AttachBuffer;

    /*! @brief Closes the stream. */
    OpcUa_Stream_PfnClose* Close;
    
    /*! @brief Frees the structure. */
    OpcUa_Stream_PfnDelete* Delete;
    
    /*! @brief Whether the stream supports seeking. */
    OpcUa_Boolean CanSeek;

    /************************************************************/

    /*! @brief Reads data from the stream. */
    OpcUa_Stream_PfnRead* Read;

    /*! @brief Whether the stream is non-blocking. */
    OpcUa_Boolean NonBlocking;
}
OpcUa_InputStream;

/** 
  @brief An output stream.
*/
typedef struct _OpcUa_OutputStream
{
    /*! @brief The type of stream (always OpcUa_StreamType_Output). */
    OpcUa_Int32 Type;

    /*! @brief An opaque handle that contain data specific to the stream implementation. */
    OpcUa_Handle Handle;

    /*! @brief Returns the current position in the stream. */
    OpcUa_Stream_PfnGetPosition* GetPosition;
    
    /*! @brief Sets the current position in the stream. */
    OpcUa_Stream_PfnSetPosition* SetPosition;
    
    /*! @brief Returns the internal chunk length. */
    OpcUa_Stream_PfnGetChunkLength* GetChunkLength;

    /*! @brief Detach the internal buffer. */
    OpcUa_Stream_PfnDetachBuffer* DetachBuffer;

    /*! @brief Attach an external buffer. */
    OpcUa_Stream_PfnAttachBuffer* AttachBuffer;

    /*! @brief Closes the stream. */
    OpcUa_Stream_PfnClose* Close;
    
    /*! @brief Frees the structure. */
    OpcUa_Stream_PfnDelete* Delete;

    /*! @brief Whether the stream supports seeking. */
    OpcUa_Boolean CanSeek;

    /************************************************************/

    /*! @brief Writes data to the stream. */
    OpcUa_Stream_PfnWrite* Write;
    
    /*! @brief Flushes data to the stream. */
    OpcUa_Stream_PfnFlush* Flush;

}
OpcUa_OutputStream;

/** 
  @brief Set the stream position to the start of the available data.
*/
#define OpcUa_StreamPosition_Start OpcUa_BufferPosition_Start

/** 
  @brief Set the stream position to the end of the available data.
*/
#define OpcUa_StreamPosition_End   OpcUa_BufferPosition_End

#define OpcUa_StreamType_Input  0x01
#define OpcUa_StreamType_Output 0x02

OPCUA_END_EXTERN_C

#endif /* _OpcUa_Stream_H_ */
