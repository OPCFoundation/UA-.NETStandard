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

#ifndef _OpcUa_Thread_H_
#define _OpcUa_Thread_H_ 1

OPCUA_BEGIN_EXTERN_C

/*============================================================================
 * Type Definition
 *===========================================================================*/

/**
 * @brief Thread main entry function definition.
 */
typedef OpcUa_Void (OpcUa_PfnThreadMain)(OpcUa_Void* pArgument);

/**
 * @brief Describes a thread handle.
 */

/*============================================================================
 * Type Management
 *===========================================================================*/

/**
 * @brief Create a thread.
 *
 * @param ppThread [in/out] Pointer to the thread handle. Contains the created thread or OpcUa_Null.
 *
 * @return An error code for the operation.
 */
OPCUA_EXPORT 
OpcUa_StatusCode    OpcUa_Thread_Create(        OpcUa_Thread*        pThread,
                                                OpcUa_PfnThreadMain* pThreadMain,
                                                OpcUa_Void*          pThreadArgument);

/**
 * @brief Delete a thread.
 *
 * @param ppThread [in] Pointer to the thread handle.
 *
 * @return
 */
OPCUA_EXPORT 
OpcUa_Void          OpcUa_Thread_Delete(        OpcUa_Thread* pThread);


/*============================================================================
 * Type Operations
 *===========================================================================*/

/**
 * @brief Start a Thread.
 *
 * @param Thread [in] The thread handle.
 *
 * @return An error code for the operation.
 */
OPCUA_EXPORT 
OpcUa_StatusCode OpcUa_Thread_Start(            OpcUa_Thread   Thread);

/**
 * @brief Wait For Thread Shutdown.
 *
 * @param Thread        [in] The thread handle.
 * @param msecTimeout   [in] The maximum time to wait for shutdown.
 *
 * @return An error code for the operation.
 */
OPCUA_EXPORT 
OpcUa_StatusCode OpcUa_Thread_WaitForShutdown(  OpcUa_Thread   Thread, 
                                                OpcUa_UInt32    msecTimeout);


/**
 * @brief Lets the thread sleep for a certain amount of time.
 *
 * @param msecTimeout [in] The time in milliseconds to suspend the calling thread.
 */
OPCUA_EXPORT 
OpcUa_Void OpcUa_Thread_Sleep(                  OpcUa_UInt32    msecTimeout);

/**
 * @brief Get the ID of the current thread.
 *
 * @return The thread ID.
 */
OPCUA_EXPORT
OpcUa_UInt32 OpcUa_Thread_GetCurrentThreadId();

/**
 * @brief Check if the main function of the given thread object is running.
 *        State may have already changed when function returns!
 *
 * @return OpcUa_True if running, OpcUa_False else.
 */
OPCUA_EXPORT
OpcUa_Boolean OpcUa_Thread_IsRunning(           OpcUa_Thread    hThread);

OPCUA_END_EXTERN_C

#endif /* _OpcUa_Thread_H_ */
