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

#ifndef _OpcUa_ThreadPool_H_
#define _OpcUa_ThreadPool_H_ 1

#if OPCUA_HAVE_THREADPOOL

/** 
 * @brief Threadpool Handle.
 */
typedef OpcUa_Void* OpcUa_ThreadPool;

OPCUA_BEGIN_EXTERN_C

/** 
 * @brief Create a thread pool with uMinThreads static threads and uMaxThreads - uMinThreads dynamic threads.
 */
OPCUA_EXPORT OpcUa_StatusCode   OPCUA_DLLCALL OpcUa_ThreadPool_Create(  OpcUa_ThreadPool*       phThreadPool,
                                                                        OpcUa_UInt32            uMinThreads,
                                                                        OpcUa_UInt32            uMaxThreads,
                                                                        OpcUa_UInt32            uMaxJobs,
                                                                        OpcUa_Boolean           bBlockIfFull,
                                                                        OpcUa_UInt32            uTimeout);

/** 
 * @brief Destroy a thread pool.
 */
OPCUA_EXPORT OpcUa_Void         OPCUA_DLLCALL OpcUa_ThreadPool_Delete(  OpcUa_ThreadPool*       phThreadPool);

/** 
 * @brief Assing a job to a thread pool. The job may be queued for later execution.
 */
OPCUA_EXPORT OpcUa_StatusCode   OPCUA_DLLCALL OpcUa_ThreadPool_AddJob(  OpcUa_ThreadPool        hThreadPool,
                                                                        OpcUa_PfnThreadMain*    pFunction,
                                                                        OpcUa_Void*             pArgument);

OPCUA_END_EXTERN_C

#endif /* OPCUA_HAVE_THREADPOOL */

#endif /* _OpcUa_ThreadPool_H_ */
