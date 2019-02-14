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

/* platformdefs and types must be known before including this file */

#ifndef _OpcUa__P_Interface_H_
#define _OpcUa__P_Interface_H_ 1

#include "opcua_p_crypto.h"
#include "opcua_p_pki.h"

OPCUA_BEGIN_EXTERN_C

/***********************************************************************************************/
/** @brief The handle for the platform thread. */
typedef OpcUa_Void* OpcUa_RawThread;

/***********************************************************************************************/

/** @brief Function prototype for receiving callbacks from the timer module. */
typedef OpcUa_StatusCode (OPCUA_DLLCALL OpcUa_P_Timer_Callback)(OpcUa_Void*     pvCallbackData,
                                                                OpcUa_Timer     hTimer,
                                                                OpcUa_UInt32    msecElapsed);

/***********************************************************************************************/

/** @brief Trace hook function type. */
typedef OpcUa_Void (OPCUA_DLLCALL *OpcUa_P_TraceHook)(const OpcUa_CharA* sMessage);

/***********************************************************************************************/

struct _OpcUa_XmlWriter;

typedef OpcUa_StatusCode (OpcUa_XmlWriter_PfnWriteCallback)(
    struct _OpcUa_XmlWriter*    a_pXmlWriter,
    OpcUa_Void*                 a_pWriteContext, 
    OpcUa_Byte*                 a_pWriteBuffer, 
    OpcUa_UInt32                a_uBufferLength);

typedef OpcUa_StatusCode (OpcUa_XmlWriter_PfnCloseCallback)(
    struct _OpcUa_XmlWriter*    a_pXmlWriter,
    OpcUa_Void*                 a_pWriteContext);

struct _OpcUa_XmlReader;

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnReadCallback)(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_Void*                 a_pReadContext, 
    OpcUa_Byte*                 a_pReadBuffer, 
    OpcUa_UInt32*               a_pBufferLength);

typedef OpcUa_StatusCode (OpcUa_XmlReader_PfnCloseCallback)(
    struct _OpcUa_XmlReader*    a_pXmlReader,
    OpcUa_Void*                 a_pReadContext);

/** @brief returned on error, where other type than statuscode is used (write). */
#define OPCUA_SOCKET_ERROR ((OpcUa_Int32)(-1))

/** @brief maximum time to wait for a send operation to complete. */
#define OPCUA_SOCKET_SELECT_TIMEOUT 1000

/**
 * These types of events can be sent to the registered callback function from the socket.
 * The receiver can register to them has to react on this events.
 */
#define OPCUA_SOCKET_NO_EVENT           0x0000 /* no event happened... */
#define OPCUA_SOCKET_READ_EVENT         0x0001 /* socket ready for receiving */
#define OPCUA_SOCKET_WRITE_EVENT        0x0002 /* socket ready for writing */
#define OPCUA_SOCKET_CLOSE_EVENT        0x0004 /* socket has been closed */
#define OPCUA_SOCKET_EXCEPT_EVENT       0x0008 /* an exception ocurred on a socket */
#define OPCUA_SOCKET_TIMEOUT_EVENT      0x0010 /* the connection on a socket timed out */
#define OPCUA_SOCKET_SHUTDOWN_EVENT     0x0020 /* server shuts down */
#define OPCUA_SOCKET_CONNECT_EVENT      0x0040 /* the socket has connected to the remote node (client) */
#define OPCUA_SOCKET_ACCEPT_EVENT       0x0080 /* a remote node has connected to this socket (server) */
#define OPCUA_SOCKET_NEED_BUFFER_EVENT  0x0100 /* the socketmanager requests a temporary buffer */
#define OPCUA_SOCKET_FREE_BUFFER_EVENT  0x0200 /* the socketmanager releases the temporary buffer */

/** @brief Events which are set outside the event loop. (external events) */
#define OPCUA_SOCKET_RENEWLOOP_EVENT 0x0300 /* restarts loop to reinterpret socket list */
#define OPCUA_SOCKET_USER_EVENT      0x0400 /* user fired an event */

/** @brief SocketManager behaviour control. */
#define OPCUA_SOCKET_NO_FLAG                    0   /* standard behaviour */
#define OPCUA_SOCKET_REJECT_ON_NO_THREAD        1   /* thread pooling; reject connection if no worker thread i available */
#define OPCUA_SOCKET_DONT_CLOSE_ON_EXCEPT       2   /* don't close a socket if an except event occured */
#define OPCUA_SOCKET_SPAWN_THREAD_ON_ACCEPT     4   /* assing each accepted socket a new thread */

/** @brief PeerInfo settings */
#define OPCUA_P_SOCKETGETPEERINFO_V2                OPCUA_CONFIG_YES
#define OPCUA_P_PEERINFO_MIN_SIZE                   64

/*============================================================================
 * The Socket Event Callback
 *===========================================================================*/
/** @brief Function prototype for receiving event callbacks from the socket module. */
typedef OpcUa_StatusCode (*OpcUa_Socket_EventCallback)( OpcUa_Socket   hSocket,
                                                        OpcUa_UInt32   uintSocketEvent,
                                                        OpcUa_Void*    pUserData,
                                                        OpcUa_UInt16   usPortNumber,
                                                        OpcUa_Boolean  bIsSSL);


/*============================================================================
 * va_list definitions
 *===========================================================================*/
typedef va_list OpcUa_P_VA_List;

#define OPCUA_P_VA_START(ap,v)  va_start(ap,v)
#define OPCUA_P_VA_END(ap)      va_end(ap)

/***********************************************************************************************/

/** @brief Servicetable exposing the platform layer functionality to the stack implementation. */
typedef struct S_OpcUa_Port_CallTable OpcUa_Port_CallTable;

struct S_OpcUa_Port_CallTable
{
    /**@name CallTable Header */
    /**@{*/

    /** Size of the platform layer calltable */
    OpcUa_UInt32 uSize;

    /** Reserved for platform layer internal use. */
    OpcUa_Void*  pReserved;

    /**@} CallTable Header */
    /**@name Memory Functions */
    /**@{*/

    /** @brief Standard malloc functionality.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void*         (OPCUA_DLLCALL* MemAlloc)                 ( OpcUa_UInt32                uSize);

    /** @brief Standard free functionality.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* MemFree)                  ( OpcUa_Void*                 pMemory);

    /** @brief Standard realloc functionality.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void*         (OPCUA_DLLCALL* MemReAlloc)               ( OpcUa_Void*                 pBuffer,
                                                                    OpcUa_UInt32                nSize);

    /** @brief Standard memcpy functionality but with target and source buffer size for additional checks.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* MemCpy)                   ( OpcUa_Void*                 pBuffer,
                                                                    OpcUa_UInt32                nSizeInBytes,
                                                                    OpcUa_Void*                 pSource,
                                                                    OpcUa_UInt32                nCount);

    /** @brief Standard memset functionality.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void*         (OPCUA_DLLCALL* MemSet)                   ( OpcUa_Void*                 pMemory,
                                                                    OpcUa_Byte                  uValue,
                                                                    OpcUa_UInt32                uMemorySize);

    /**@} Memory Functions */
    /**@name Date and Time Functions */
    /**@{*/

    /** @brief Returns the current time in the OpcUa_DateTime format.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_DateTime      (OPCUA_DLLCALL* UtcNow)                   ();

    /** @brief Returns the current time in the OpcUa_TimeVal format.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* GetTimeOfDay)             ( OpcUa_TimeVal*              pValue);

    /** @brief Converts the given datetime into a string format format. The given buffer must have a lengt of
     *         at least 25 bytes. The length parameter is used for verification. The format is %04d-%02d-%02dT%02d:%02d:%02d.%03dZ.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* GetStringFromDateTime)    ( OpcUa_DateTime              datetime,
                                                                    OpcUa_StringA               buffer,
                                                                    OpcUa_UInt32                length);

    /** @brief Converts a given DateTimeString into the OpcUa_DateTime format. The string format is
     *         %04d-%02d-%02dT%02d:%02d:%02d.%03dZ
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* GetDateTimeFromString)    ( OpcUa_StringA               DateTimeString,
                                                                    OpcUa_DateTime*             DateTime);

    /**@} Date and Time Functions */
    /**@name Mutex Functions */
    /**@{*/

#if OPCUA_MUTEX_ERROR_CHECKING   /* debug version of mutex */
    /** @brief Create a recursive mutex and get information about the origin of the call.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* MutexCreate)              ( OpcUa_Mutex*                phNewMutex, char* file, int line);

    /** @brief Delete a mutex and get information about the origin of the call.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* MutexDelete)              ( OpcUa_Mutex*                phMutex,    char* file, int line);

    /** @brief Lock the given mutex and get information about the origin of the call.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* MutexLock)                ( OpcUa_Mutex                 hMutex,     char* file, int line);

    /** @brief Unlock the given mutex and get information about the origin of the call.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* MutexUnlock)              ( OpcUa_Mutex                 hMutex,     char* file, int line);

#else /* OPCUA_MUTEX_ERROR_CHECKING */

    /** @brief Create a recursive mutex.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* MutexCreate)              ( OpcUa_Mutex*                phNewMutex);

    /** @brief Delete a mutex.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* MutexDelete)              ( OpcUa_Mutex*                phMutex);

    /** @brief Lock the given mutex.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* MutexLock)                ( OpcUa_Mutex                 hMutex);

    /** @brief Unlock the given mutex.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* MutexUnlock)              ( OpcUa_Mutex                 hMutex);

#endif /* OPCUA_MUTEX_ERROR_CHECKING */

    /**@} Mutex Functions */
    /**@name Guid Functions */
    /**@{*/

    /* Guid */
    /** @brief Create a guid and store it at the given memory position.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Guid*         (OPCUA_DLLCALL* GuidCreate)               ( OpcUa_Guid*                 pGuid);

    /**@} Guid Functions */
    /**@name Semaphore Functions */
    /**@{*/

    /** @brief Create a semaphore object and set its initial and max value.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* SemaphoreCreate)          ( OpcUa_Semaphore*            phNewSemaphore,
                                                                    OpcUa_UInt32                uInitalValue,
                                                                    OpcUa_UInt32                uMaxRange);

    /** @brief Delete the semaphore.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* SemaphoreDelete)          ( OpcUa_Semaphore*            phSemaphore);

    /** @brief Wait on a semaphore until you can acquire 1.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* SemaphoreWait)            ( OpcUa_Semaphore             HSemaphore);

    /** @brief Same as @see SemaphoreWait but with a maximum waiting time of msecTimeout milliseconds before
     *         OpcUa_GoodNonCriticalTimeout is returned.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* SemaphoreTimedWait)       ( OpcUa_Semaphore             hSemaphore,
                                                                    OpcUa_UInt32                msecTimeout);

    /** @brief Post/Release a semaphore with a count of uReleaseCount.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* SemaphorePost)            ( OpcUa_Semaphore             hSemaphore,
                                                                    OpcUa_UInt32                uReleaseCount);

    /**@} Semaphore Functions */
    /**@name Thread Functions */
    /**@{*/

    /** @brief Reserve resources for a system thread object.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* ThreadCreate)             ( OpcUa_RawThread*            pThread);

    /** @brief Free the resources reserved for the system thread object.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* ThreadDelete)             ( OpcUa_RawThread*            pRawThread);

    /** @brief Start the system thread and let it execute the given function with pArguments.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* ThreadStart)              ( OpcUa_RawThread             pThread,
                                                                    OpcUa_PfnInternalThreadMain pfnStartFunction,
                                                                    OpcUa_Void*                 pArguments);

    /** @brief Let the calling thread sleep for msecTimeout milliseconds.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* ThreadSleep)              ( OpcUa_UInt32                msecTimeout);

    /** @brief Get an unique id for the calling system thread.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_UInt32        (OPCUA_DLLCALL* ThreadGetCurrentId)       ();

    /**@} Thread Functions */
    /**@name Trace Functions */
    /**@{*/

    /** @brief Output the given zero terminated string to the systems tracing device.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* Trace)                    ( OpcUa_CharA*                sFormat);

    /** @brief Initialize tracing functionality during stack initialization before any call to Trace is made.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* TraceInitialize)          ();

    /** @brief Clean up the tracing functionality after stack clean up after the last call to Trace was made.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* TraceClear)               ();

    /**@} Trace Functions */
    /**@name String Functions */
    /**@{*/

    /** @brief Standard strncpy functionality.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* StrnCpy)                  ( OpcUa_StringA               strDestination,
                                                                    OpcUa_UInt32                uiDestSize,
                                                                    OpcUa_StringA               strSource,
                                                                    OpcUa_UInt32                uiLength);

    /** @brief Standard strncat functionality.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* StrnCat)                  ( OpcUa_StringA               strDestination,
                                                                    OpcUa_UInt32                uiDestSize,
                                                                    OpcUa_StringA               strSource,
                                                                    OpcUa_UInt32                uiLength);

    /** @brief Standard strlen functionality.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Int32         (OPCUA_DLLCALL* StrLen)                   ( OpcUa_StringA               pCString);

    /** @brief Standard strncmp functionality.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Int32         (OPCUA_DLLCALL* StrnCmp)                  ( OpcUa_StringA               string1,
                                                                    OpcUa_StringA               string2,
                                                                    OpcUa_UInt32                uiLength);

    /** @brief Standard strnicmp functionality.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Int32         (OPCUA_DLLCALL* StrniCmp)                 ( OpcUa_StringA               string1,
                                                                    OpcUa_StringA               string2,
                                                                    OpcUa_UInt32                uiLength);

    /** @brief Standard strvsnprintf functionality.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Int32         (OPCUA_DLLCALL* StrVsnPrintf)             ( OpcUa_StringA               sDest,
                                                                    OpcUa_UInt32                uCount,
                                                                    const OpcUa_StringA         sFormat,
                                                                    OpcUa_P_VA_List             argptr);

    /**@} String Functions */
    /**@name Utility Functions */
    /**@{*/

    /** @brief Implementation of the qsort algorithm.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* qSort)                    ( OpcUa_Void*                 pElements,
                                                                    OpcUa_UInt32                nElementCount,
                                                                    OpcUa_UInt32                nElementSize,
                                                                    OpcUa_PfnCompare*           pfnCompare,
                                                                    OpcUa_Void*                 pContext);

    /** @brief Implementation of the bsearch algorithm.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void*         (OPCUA_DLLCALL* bSearch)                  ( OpcUa_Void*                 pKey,
                                                                    OpcUa_Void*                 pElements,
                                                                    OpcUa_UInt32                nElementCount,
                                                                    OpcUa_UInt32                nElementSize,
                                                                    OpcUa_PfnCompare*           pfnCompare,
                                                                    OpcUa_Void*                 pContext);

    /** @brief Get last error (aka errno);
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_UInt32        (OPCUA_DLLCALL* UtilGetLastError)         ();

    /** @brief Get the current millisecond tick count of the system.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_UInt32        (OPCUA_DLLCALL* UtilGetTickCount)         ();

    /** @brief Convert the given string containing a number into OpcUa_Int32.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Int32         (OPCUA_DLLCALL* CharToInt)                ( OpcUa_StringA               sValue);

    /**@} Utility Functions */
    /**@name Network Functions */
    /**@{*/

    /** @brief Convert the given IPv4 network address into its binary representation.
     *         No longer required by the stack. Can be ignored and may be removed in future versions.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_UInt32        (OPCUA_DLLCALL* InetAddr)                 ( OpcUa_StringA               sRemoteAddress);

    /** @brief Create a socket manager with the ability to host nSockets sockets and use the given runtime behavior flags.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* SocketManagerCreate)      ( OpcUa_SocketManager*        ppSocketManager,
                                                                    OpcUa_UInt32                nSockets,
                                                                    OpcUa_UInt32                uintFlags);

    /** @brief Delete the given socket manager and block until the operation is completed and no more callbacks will be invoked.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* SocketManagerDelete)      ( OpcUa_SocketManager*        pSocketManager);

    /** @brief Request the given socket manager to use one of its sockets as a listen socket for incoming connect requests.
     *         The given address contains a network address (and port number if required). This function is completed
     *         synchronously. The given callback will be invoked when client connection requests on the listen socket are accepted.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* SocketManagerCreateServer)( OpcUa_SocketManager         pSocketManager,
                                                                    OpcUa_StringA               sRemoteAdress,
                                                                    OpcUa_Socket_EventCallback  pfnSocketCallBack,
                                                                    OpcUa_Void*                 pCookie,
                                                                    OpcUa_Socket*               ppSocket);

    /** @brief Request the given socket manager to use one of its sockets for a client connection to the given address data.
     *         The created socket handle is returned immediately if the operation could be started without errors. The local
     *         port number is optional and ignored if 0. Else, the socket manager tries to bind the connection to the local
     *         port. This function is completed asynchronously by calling pfnSocketCallBack providing pCookie. This callback
     *         is also called for follow-up events on the socket. If the connection can not be established, the callback is
     *         called with an exception event.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* SocketManagerCreateClient)( OpcUa_SocketManager         pSocketManager,
                                                                    OpcUa_StringA               sRemoteAdress,
                                                                    OpcUa_UInt16                usLocalPort,
                                                                    OpcUa_Socket_EventCallback  pfnSocketCallBack,
                                                                    OpcUa_Void*                 pCookie,
                                                                    OpcUa_Socket*               ppSocket);

    /** @brief Raise the given event for the given socket manager or all socket managers hosted by the platform layer.
     *         If pSocketManager is OpcUa_Null and bAllManager is OpcUa_False, the call is directed to the default socket
     *         manager in single thread configuration. The only event raised by the stack itself is the OPCUA_SOCKET_SHUTDOWN_EVENT
     *         when the stack wants an socket manager to stop processing network events.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* SocketManagerSignalEvent) ( OpcUa_SocketManager         pSocketManager,
                                                                    OpcUa_UInt32                uintEvent,
                                                                    OpcUa_Boolean               bAllManagers);

    /** @brief Invoke the communication message loop for the given socket manager and block for a maximum of msecTimout seconds.
     *         Caller can decide whether the loop should be executed once or until the shutdown event is signalled to the loop by
     *         setting bRunOnce approbriately. The implementation decides whether timer callbacks are also invoked during the call.
     *         This function is mainly intended to process network and timer events in a single threaded environment. In a multi
     *         threaded environment, this function is executed in loop by a dedicated thread and not by the application.
     *         If pSocketManager is OpcUa_Null, the call is directed to the default socket manager in single thread configuration.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* SocketManagerServeLoop)   ( OpcUa_SocketManager         pSocketManager,
                                                                    OpcUa_UInt32                msecTimeout,
                                                                    OpcUa_Boolean               bRunOnce);

    /** @brief Copy a maximum of BufferSize bytes from the socket into pBuffer and store the actual number bytes copied in puintBytesRead.
     *         A *puintBytesRead is interpreted as a shutdown of the inbound direction by the peer.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* SocketRead)               ( OpcUa_Socket                hSocket,
                                                                    OpcUa_Byte*                 pBuffer,
                                                                    OpcUa_UInt32                BufferSize,
                                                                    OpcUa_UInt32*               puintBytesRead);

    /** @brief Write BufferSize bytes from pBuffer to the given Socket and dont return until all data is copied if bBlock is OpcUa_True.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Int32         (OPCUA_DLLCALL* SocketWrite)              ( OpcUa_Socket                hSocket,
                                                                    OpcUa_Byte*                 pBuffer,
                                                                    OpcUa_UInt32                BufferSize,
                                                                    OpcUa_Boolean               bBlock);

    /** @brief Close the given socket handle.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* SocketClose)              ( OpcUa_Socket                hSocket);

#if OPCUA_P_SOCKETGETPEERINFO_V2
    /** @brief Get address information for the peer connected to the given socket socket handle.
               This function was changed to create a string. The old version only supported IPv4 addresses.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* SocketGetPeerInfo)        ( OpcUa_Socket                hSocket,
                                                                    OpcUa_CharA*                achPeerInfoBuffer,
                                                                    OpcUa_UInt32                uiPeerInfoBufferSize);
#else /* OPCUA_P_SOCKETGETPEERINFO_V2 */
    /** @brief Get IPv4 address and port number for the peer connected to the given socket.
     *         (Ignore call for non IPv4 connections. May get changed in future revisions.)
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* SocketGetPeerInfo)        ( OpcUa_Socket                hSocket,
                                                                    OpcUa_UInt32*               pIP,
                                                                    OpcUa_UInt16*               pPort);
#endif /* OPCUA_P_SOCKETGETPEERINFO_V2 */

    /** @brief Get the status code for the last error that occurred on the given socket.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* SocketGetLastError)       ( OpcUa_Socket                hSocket);

    /** @brief Initialize all network resources required by the platform layer. Called during proxystub initialization.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* NetworkInitialize)        ();

    /** @brief Clean up and free all network resources. Called during proxystub cleanup procedure.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* NetworkCleanup)           ();

    /**@} Network Functions */
    /**@name Crypto and PKI Functions */
    /**@{*/

    /** @brief Create a crypto provider based on the given security policy URI.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* CreateCryptoProvider)     ( OpcUa_StringA               Uri,
                                                                    OpcUa_CryptoProvider*       pProvider);

    /** @brief Delete the given crypto provider.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* DeleteCryptoProvider)     ( OpcUa_CryptoProvider*       pProvider);

    /** @brief Create an PKI Provider based on the given certificate store configuration.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* CreatePKIProvider)        ( OpcUa_Void*                 pCertificateStoreConfig,
                                                                    OpcUa_PKIProvider*          pProvider);

    /** @brief Delete the given PKI provider.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* DeletePKIProvider)        ( OpcUa_PKIProvider*          pProvider);


    /**@} Crypto and PKI Functions*/
    /**@name Timer Functions */
    /**@{*/

    /** @brief Create a timer which calls the TimerCallback every msecInterval milliseconds with pvCallbackData.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* TimerCreate)              ( OpcUa_Timer*                phTimer,
                                                                    OpcUa_UInt32                msecInterval,
                                                                    OpcUa_P_Timer_Callback*     fTimerCallback,
                                                                    OpcUa_P_Timer_Callback*     fKillCallback,
                                                                    OpcUa_Void*                 pvCallbackData);

    /** @brief Delete the given timer and call the KillCallback when it is guaranteed that no more TimerCallbacks occur.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* TimerDelete)              ( OpcUa_Timer*                phTimer);

    /** @brief Called before cleanup to stop all active timers and invoke timer delete callbacks.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_Void          (OPCUA_DLLCALL* TimersCleanup)            ();

    /**@} Timer Functions */


#ifdef OPCUA_HAVE_XMLAPI    
    /**@name XML Functions */
    /**@{*/

    /** @brief Create a XML writer context.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* CreateXmlWriter)          ( OpcUa_Void*                         a_pWriterContext,
                                                                    OpcUa_XmlWriter_PfnWriteCallback*   a_pWriteCallback,
                                                                    OpcUa_XmlWriter_PfnCloseCallback*   a_pCloseCallback,
                                                                    struct _OpcUa_XmlWriter*            a_pXmlWriter);
    
    /** @brief Delete a XML writer context.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* DeleteXmlWriter)          ( struct _OpcUa_XmlWriter*            a_pXmlWriter);

    /** @brief Create a XML reader context.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* CreateXmlReader)          ( OpcUa_Void*                         a_pReaderContext,
                                                                    OpcUa_XmlReader_PfnReadCallback*    a_pReaderCallback,
                                                                    OpcUa_XmlReader_PfnCloseCallback*   a_pCloseCallback,
                                                                    struct _OpcUa_XmlReader*            a_pXmlReader);   

    /** @brief Delete a XML reader context.
     *  @ingroup opcua_platformlayer_interface
     */
    OpcUa_StatusCode    (OPCUA_DLLCALL* DeleteXmlReader)          ( struct _OpcUa_XmlReader*            a_pXmlReader);

    /**@} XML Functions */

#endif /* OPCUA_HAVE_XMLAPI */
}; /* struct S_OpcUa_Port_CallTable */


/** @brief Platform layer initialization. */
OPCUA_EXPORT OpcUa_StatusCode OPCUA_DLLCALL OpcUa_P_Initialize(OpcUa_Handle* ppCallTable);

/** @brief Platform layer clean up. */
OPCUA_EXPORT OpcUa_StatusCode OPCUA_DLLCALL OpcUa_P_Clean(     OpcUa_Handle* ppCallTable);

/** @brief Get version information as string from static buffer. Must not be freed! */
OPCUA_EXPORT OpcUa_StringA    OPCUA_DLLCALL OpcUa_P_GetVersion();

/** @brief Get config information as string from static buffer. Must not be freed! */
OPCUA_EXPORT OpcUa_StringA    OPCUA_DLLCALL OpcUa_P_GetConfigString();

OPCUA_END_EXTERN_C
#endif /* _OpcUa__P_Interface_H_ */
