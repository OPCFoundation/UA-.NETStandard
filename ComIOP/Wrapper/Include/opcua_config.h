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

#ifndef _OPCUA_CONFIG_H_
#define _OPCUA_CONFIG_H_ 1

/*============================================================================
 * build information
 *===========================================================================*/
/** @brief The version number of the official OPC Foundation SDK this build is based on. */
#ifndef OPCUA_BUILDINFO_VERSION_BASE
# define OPCUA_BUILDINFO_VERSION_BASE               "1.01.333.1"
#endif /* OPCUA_BUILDINFO_VERSION_BASE */

/** @brief The build number appended to OPCUA_BUILDINFO_VERSION_BASE. */
#ifndef OPCUA_BUILDINFO_VERSION_BUILD
# define OPCUA_BUILDINFO_VERSION_BUILD              0
#endif /* OPCUA_BUILDINFO_VERSION_BASE */

/** @brief The date and time when the source was last modified. */
#ifndef OPCUA_BUILDINFO_SOURCE_TIMESTAMP
# define OPCUA_BUILDINFO_SOURCE_TIMESTAMP           "OPCUA_BUILDINFO_SOURCE_TIMESTAMP not set"
#endif /* OPCUA_BUILDINFO_SOURCE_TIMESTAMP */

/** @brief The date and time when the binary is build. */
#ifndef OPCUA_BUILDINFO_BUILD_TIMESTAMP
# ifdef OPCUA_P_TIMESTAMP
#  define OPCUA_BUILDINFO_BUILD_TIMESTAMP            OPCUA_P_TIMESTAMP
# else /* OPCUA_P_TIMESTAMP */
#  define OPCUA_BUILDINFO_BUILD_TIMESTAMP            "OPCUA_BUILDINFO_BUILD_TIMESTAMP not set"
# endif /* OPCUA_P_TIMESTAMP */
#endif /* OPCUA_BUILDINFO_BUILD_TIMESTAMP */

/** @brief The name of the company which build the binary. */
#ifndef OPCUA_BUILDINFO_VENDOR_NAME
# define OPCUA_BUILDINFO_VENDOR_NAME                "OPCUA_BUILDINFO_VENDOR_NAME not set"
#endif /* OPCUA_BUILDINFO_VENDOR_NAME */

/** @brief Additional information from the company, ie. internal revision number. */
#ifndef OPCUA_BUILDINFO_VENDOR_INFO
# define OPCUA_BUILDINFO_VENDOR_INFO                "OPCUA_BUILDINFO_VENDOR_INFO not set"
#endif /* OPCUA_BUILDINFO_VENDOR_INFO */

/** @brief Information about the used compiler. */
#ifndef OPCUA_BUILDINFO_COMPILER
# ifdef OPCUA_P_COMPILERINFO
#  define OPCUA_BUILDINFO_COMPILER                  OPCUA_P_COMPILERINFO
# else /* OPCUA_P_COMPILERINFO */
#  define OPCUA_BUILDINFO_COMPILER                  "OPCUA_BUILDINFO_COMPILER not set"
# endif /* OPCUA_P_COMPILERINFO */
#endif /* OPCUA_BUILDINFO_COMPILER */

/** @brief The versionstring returned by OpcUa_ProxyStub_GetVersion. */
#define OPCUA_PROXYSTUB_VERSIONSTRING   OPCUA_BUILDINFO_VERSION_BASE                        \
                                        "."                                                 \
                                        OPCUA_TOSTRING(OPCUA_BUILDINFO_VERSION_BUILD) "\\"  \
                                        OPCUA_BUILDINFO_SOURCE_TIMESTAMP              "\\"  \
                                        OPCUA_BUILDINFO_BUILD_TIMESTAMP               "\\"  \
                                        OPCUA_BUILDINFO_VENDOR_NAME                   "\\"  \
                                        OPCUA_BUILDINFO_VENDOR_INFO                   "\\"  \
                                        OPCUA_BUILDINFO_COMPILER

/*============================================================================
 * source configuration switches
 *===========================================================================*/
#define OPCUA_CONFIG_NO  0
#define OPCUA_CONFIG_YES !OPCUA_CONFIG_NO

/*============================================================================
 * modules (removing unneeded modules reduces codesize)
 *===========================================================================*/
/** @brief Define or undefine to enable or disable client functionality */
#ifndef OPCUA_HAVE_CLIENTAPI
# define OPCUA_HAVE_CLIENTAPI                       OPCUA_CONFIG_YES
#endif /* OPCUA_HAVE_CLIENTAPI */
/** @brief Define or undefine to enable or disable server functionality */
#ifndef OPCUA_HAVE_SERVERAPI
# define OPCUA_HAVE_SERVERAPI                       OPCUA_CONFIG_YES
#endif /* OPCUA_HAVE_SERVERAPI */
/** @brief Define or undefine to enable or disable threadpool support. Required if secure listener shall use it. */
#ifndef OPCUA_HAVE_THREADPOOL
# define OPCUA_HAVE_THREADPOOL                      OPCUA_CONFIG_YES
#endif /* OPCUA_HAVE_THREADPOOL */
/** @brief define or undefine to enable or disable the memory stream module. */
#ifndef OPCUA_HAVE_MEMORYSTREAM
# define OPCUA_HAVE_MEMORYSTREAM                    OPCUA_CONFIG_YES
#endif /* OPCUA_HAVE_MEMORYSTREAM */
/** @brief define or undefine to enable or disable the https support. */
#ifndef OPCUA_HAVE_HTTPS
#define OPCUA_HAVE_HTTPS                            OPCUA_CONFIG_NO
#endif /* OPUA_HAVE_HTTPS */
/** @brief define or undefine to enable or disable the soap and http support. */
#ifndef OPCUA_HAVE_SOAPHTTP
# define OPCUA_HAVE_SOAPHTTP                        OPCUA_CONFIG_NO
#endif /* OPCUA_HAVE_SOAPHTTP */

/* * @brief AUTOMATIC; activate additional modules required by soap/http */
#if OPCUA_HAVE_SOAPHTTP
# ifndef OPCUA_HAVE_HTTPAPI
#  define OPCUA_HAVE_HTTPAPI                        OPCUA_CONFIG_YES
# endif /* OPCUA_HAVE_HTTPAPI */
# ifndef OPCUA_HAVE_XMLAPI
#  define OPCUA_HAVE_XMLAPI                         OPCUA_CONFIG_YES
# endif /* OPCUA_HAVE_XMLAPI */
# ifndef OPCUA_HAVE_BASE64
#  define OPCUA_HAVE_BASE64                         OPCUA_CONFIG_YES
# endif /* OPCUA_HAVE_BASE64 */
# if OPCUA_HAVE_MEMORYSTREAM == OPCUA_CONFIG_NO
#  error SOAP/HTTP UA-SC transport profile requires memory stream!
# endif /* OPCUA_HAVE_MEMORYSTREAM */
#endif /* OPCUA_HAVE_SOAPHTTP */

/*============================================================================
 * dotnet native stack wrapper requires this extension
 *===========================================================================*/
#ifndef OPCUA_SUPPORT_PREENCODED_MESSAGES
# define OPCUA_SUPPORT_PREENCODED_MESSAGES          OPCUA_CONFIG_YES
#endif /* OPCUA_SUPPORT_PREENCODED_MESSAGES */

/*============================================================================
 * general
 *===========================================================================*/
/** @brief Prefer the use of inline functions instead of function calls (see opcua_string) */
#ifndef OPCUA_PREFERINLINE
# define OPCUA_PREFERINLINE                         OPCUA_CONFIG_NO
#endif /* OPCUA_PREFERINLINE */

/** @brief Enable the use of safe functions like defined with VS2005 and higher. */
#ifndef OPCUA_USE_SAFE_FUNCTIONS
# define OPCUA_USE_SAFE_FUNCTIONS                   OPCUA_CONFIG_YES
#endif /* OPCUA_USE_SAFE_FUNCTIONS */

/** @brief Some temporary optimizations, to test their impact on performance. */
#ifndef OPCUA_PERFORMANCE_OPTIMIZATION_TESTING
# define OPCUA_PERFORMANCE_OPTIMIZATION_TESTING     OPCUA_CONFIG_NO
#endif /* OPCUA_PERFORMANCE_OPTIMIZATION_TESTING */

/** @brief Adds a value into enums to ensure their size is 32-bits (required for some compilers). */
#ifndef OPCUA_FORCE_INT32_ENUMS
# define OPCUA_FORCE_INT32_ENUMS                    OPCUA_CONFIG_YES
#endif /* OPCUA_FORCE_INT32_ENUMS */

/*============================================================================
 * threading
 *===========================================================================*/
/** @brief Run in multi thread mode. Each listen socket gets its own thread. */
#ifndef OPCUA_MULTITHREADED
#define OPCUA_MULTITHREADED                         OPCUA_CONFIG_YES
#endif /* OPCUA_MULTITHREADED */

/** @brief Use access synchronization. Required for OPCUA_MULTITHREADED */
#ifndef OPCUA_USE_SYNCHRONISATION
# define OPCUA_USE_SYNCHRONISATION                  OPCUA_CONFIG_YES
#endif /* OPCUA_USE_SYNCHRONISATION */

#if OPCUA_MULTITHREADED
# if !OPCUA_USE_SYNCHRONISATION
#  error MT needs SYNCHRO!
# endif
#endif

/** @brief Using a special mutex struct with debug information. */
#ifndef OPCUA_MUTEX_ERROR_CHECKING
# define OPCUA_MUTEX_ERROR_CHECKING                 OPCUA_CONFIG_NO
#endif /* OPCUA_MUTEX_ERROR_CHECKING */

/*============================================================================
 * timer
 *===========================================================================*/
/** @brief Maximum amount of milliseconds to stay inactive in the timer. */
#ifndef OPCUA_TIMER_MAX_WAIT
# define OPCUA_TIMER_MAX_WAIT                       200
#endif /* OPCUA_TIMER_MAX_WAIT */

/*============================================================================
 * serializer constraints
 *===========================================================================*/
/** @brief The maximum size of memory allocated by a serializer */
#ifndef OPCUA_SERIALIZER_MAXALLOC
# define OPCUA_SERIALIZER_MAXALLOC                  16777216
#endif /* OPCUA_SERIALIZER_MAXALLOC */

/** @brief Maximum String Length accepted */
#ifndef OPCUA_ENCODER_MAXSTRINGLENGTH
# define OPCUA_ENCODER_MAXSTRINGLENGTH              ((OpcUa_UInt32)16777216)
#endif /* OPCUA_ENCODER_MAXSTRINGLENGTH */

/** @brief Maximum Array Length accepted */
#ifndef OPCUA_ENCODER_MAXARRAYLENGTH
# define OPCUA_ENCODER_MAXARRAYLENGTH               ((OpcUa_UInt32)65536)
#endif /* OPCUA_ENCODER_MAXARRAYLENGTH */

/** @brief Maximum ByteString Length accepted */
#ifndef OPCUA_ENCODER_MAXBYTESTRINGLENGTH
# define OPCUA_ENCODER_MAXBYTESTRINGLENGTH          ((OpcUa_UInt32)16777216)
#endif /* OPCUA_ENCODER_MAXBYTESTRINGLENGTH */

/** @brief Maximum Message Length accepted */
#ifndef OPCUA_ENCODER_MAXMESSAGELENGTH
# define OPCUA_ENCODER_MAXMESSAGELENGTH             ((OpcUa_UInt32)16777216)
#endif /* OPCUA_ENCODER_MAXMESSAGELENGTH */

/*============================================================================
 * serializer checks
 *===========================================================================*/
/** @brief OpcUa_True or OpcUa_False; switches checks on or off; dont use with chunking enabled. */
#ifndef OPCUA_SERIALIZER_CHECKLENGTHS
# define OPCUA_SERIALIZER_CHECKLENGTHS              OpcUa_False
#endif /* OPCUA_SERIALIZER_CHECKLENGTHS */

/*============================================================================
 * thread pool
 *===========================================================================*/
/** @brief Allow to dynamically create threads to prevent delay in queue if no static thread is free. Not recommended! */
#ifndef OPCUA_THREADPOOL_EXPANSION
# define OPCUA_THREADPOOL_EXPANSION                 OPCUA_CONFIG_NO
#endif /* OPCUA_THREADPOOL_EXPANSION */

/** @brief Time in milliseconds after which a worker thread looks for further orders. Affects shutdown time. */
#ifndef OPCUA_THREADPOOL_RELOOPTIME
# define OPCUA_THREADPOOL_RELOOPTIME                500
#endif /* OPCUA_THREADPOOL_RELOOPTIME */

/*============================================================================
 * server call dispatching
 *===========================================================================*/
/** @brief Put fully received requests into the servers thread job queue. (Be careful with the blocking setting!) */
#ifndef OPCUA_SECURELISTENER_SUPPORT_THREADPOOL
# if OPCUA_MULTITHREADED
#  define OPCUA_SECURELISTENER_SUPPORT_THREADPOOL    OPCUA_CONFIG_YES
# else
#  define OPCUA_SECURELISTENER_SUPPORT_THREADPOOL    OPCUA_CONFIG_NO
# endif /* OPCUA_MULTITHREADED */
#endif /* OPCUA_SECURELISTENER_SUPPORT_THREADPOOL */

/** @brief Minimum number of threads (static) in the secure listeners job queue. */
#ifndef OPCUA_SECURELISTENER_THREADPOOL_MINTHREADS
# define OPCUA_SECURELISTENER_THREADPOOL_MINTHREADS 5
#endif /* OPCUA_SECURELISTENER_THREADPOOL_MINTHREADS */

/** @brief Maximum number of threads (static) in the secure listeners job queue. */
#ifndef OPCUA_SECURELISTENER_THREADPOOL_MAXTHREADS
# define OPCUA_SECURELISTENER_THREADPOOL_MAXTHREADS 5
#endif /* OPCUA_SECURELISTENER_THREADPOOL_MAXTHREADS */

/** @brief Maximum total number of jobs being processed by the thread pool. */
#ifndef OPCUA_SECURELISTENER_THREADPOOL_MAXJOBS
# define OPCUA_SECURELISTENER_THREADPOOL_MAXJOBS    20
#endif /* OPCUA_SECURELISTENER_THREADPOOL_MAXJOBS */

/*============================================================================
 * tracer
 *===========================================================================*/
/** @brief Enable output to trace device. */
#ifndef OPCUA_TRACE_ENABLE
# define OPCUA_TRACE_ENABLE                         OPCUA_CONFIG_YES
#endif /* OPCUA_TRACE_ENABLE */

/** @brief Enable output to trace device. */
#ifndef OPCUA_TRACE_MAXLENGTH
# define OPCUA_TRACE_MAXLENGTH                      200
#endif /* OPCUA_TRACE_MAXLENGTH */

/** @brief output the messages in errorhandling macros; requires OPCUA_ERRORHANDLING_OMIT_METHODNAME set to OPCUA_CONFIG_NO */
#ifndef OPCUA_TRACE_ERROR_MACROS
# define OPCUA_TRACE_ERROR_MACROS                   OPCUA_CONFIG_NO
#endif /* OPCUA_TRACE_ERROR_MACROS */

/** @brief Omit the methodname in initialize status macro. */
#ifndef OPCUA_ERRORHANDLING_OMIT_METHODNAME
# define OPCUA_ERRORHANDLING_OMIT_METHODNAME        OPCUA_CONFIG_YES
#endif /* OPCUA_ERRORHANDLING_OMIT_METHODNAME */

/** @brief Add __LINE__ and __FILE__ information to the trace line. */
#ifndef OPCUA_TRACE_FILE_LINE_INFO
# define OPCUA_TRACE_FILE_LINE_INFO                 OPCUA_CONFIG_NO
#endif /* OPCUA_TRACE_FILE_LINE_INFO */

/** @brief Set to YES if the file name comes first in the format string below. */
#ifndef OPCUA_TRACE_FILE_LINE_ORDER
# define OPCUA_TRACE_FILE_LINE_ORDER                OPCUA_CONFIG_YES
#endif /* OPCUA_TRACE_FILE_LINE_ORDER */

/** @brief Set to YES if the file and line information should be printed before the trace content. */
#ifndef OPCUA_TRACE_PREPEND_FILE_LINE
# define OPCUA_TRACE_PREPEND_FILE_LINE              OPCUA_CONFIG_YES
#endif /* OPCUA_TRACE_PREPEND_FILE_LINE */

/** @brief Formatting of the __LINE__ and __FILE__ information. */
#ifndef OPCUA_TRACE_FILE_LINE_INFO_FORMAT
# define OPCUA_TRACE_FILE_LINE_INFO_FORMAT          "In file %s at line %u: "
#endif /* OPCUA_TRACE_FILE_LINE_INFO_FORMAT */

/*============================================================================
 * security
 *===========================================================================*/
/** @brief The maximum lifetime of a secure channel security token in milliseconds. */
#ifndef OPCUA_SECURITYTOKEN_LIFETIME_MAX
# define OPCUA_SECURITYTOKEN_LIFETIME_MAX           3600000
#endif /* OPCUA_SECURITYTOKEN_LIFETIME_MAX */

/** @brief The minimum lifetime of a secure channel security token in milliseconds. */
#ifndef OPCUA_SECURITYTOKEN_LIFETIME_MIN
# define OPCUA_SECURITYTOKEN_LIFETIME_MIN           600000
#endif /* OPCUA_SECURITYTOKEN_LIFETIME_MIN */

/** @brief The interval in which securechannels get checked for lifetime timeout in milliseconds. */
#ifndef OPCUA_SECURELISTENER_WATCHDOG_INTERVAL
# define OPCUA_SECURELISTENER_WATCHDOG_INTERVAL     10000
#endif /* OPCUA_SECURELISTENER_WATCHDOG_INTERVAL */

/** @brief How many OPCUA_SECURELISTENER_WATCHDOG_INTERVAL an inactive secure channel may wait for its activation. */
#ifndef OPCUA_SECURELISTENER_INACTIVECHANNELTIMEOUT
# define OPCUA_SECURELISTENER_INACTIVECHANNELTIMEOUT 1
#endif /* OPCUA_SECURELISTENER_INACTIVECHANNELTIMEOUT */

/** @brief Shall the secureconnection validate the server certificate given by the client application? */
#ifndef OPCUA_SECURECONNECTION_VALIDATE_SERVERCERT
# define OPCUA_SECURECONNECTION_VALIDATE_SERVERCERT OPCUA_CONFIG_NO
#endif /* OPCUA_SECURECONNECTION_VALIDATE_SERVERCERT */

/*============================================================================
 * networking
 *===========================================================================*/
/** @brief The standard port for the opc.tcp protocol, defined in part 6. */
#ifndef OPCUA_TCP_DEFAULT_PORT
# define OPCUA_TCP_DEFAULT_PORT                     4840
#endif /* OPCUA_TCP_DEFAULT_PORT */

/** @brief The standard port for the http protocol. */
#ifndef OPCUA_HTTP_DEFAULT_PORT
# define OPCUA_HTTP_DEFAULT_PORT                    80
#endif /* OPCUA_HTTP_DEFAULT_PORT */

/* Enable SO_REUSEADDR option for sockets. */
#ifndef OPCUA_P_SOCKET_USE_REUSEADDR
# define OPCUA_P_SOCKET_USE_REUSEADDR               OPCUA_CONFIG_NO
#endif /* OPCUA_P_SOCKET_USE_REUSEADDR */

/** @brief Request this buffersize for the sockets sendbuffer. */
#ifndef OPCUA_P_SOCKET_SETTCPRCVBUFFERSIZE
# define OPCUA_P_SOCKET_SETTCPRCVBUFFERSIZE         OPCUA_CONFIG_NO
#endif /* OPCUA_P_SOCKET_SETTCPRCVBUFFERSIZE */
#ifndef OPCUA_P_TCPRCVBUFFERSIZE
# define OPCUA_P_TCPRCVBUFFERSIZE                   65536
#endif /* OPCUA_P_TCPRCVBUFFERSIZE */
#ifndef OPCUA_P_SOCKET_SETTCPSNDBUFFERSIZE
# define OPCUA_P_SOCKET_SETTCPSNDBUFFERSIZE         OPCUA_CONFIG_NO
#endif /* OPCUA_P_SOCKET_SETTCPSNDBUFFERSIZE */
#ifndef OPCUA_P_TCPSNDBUFFERSIZE
# define OPCUA_P_TCPSNDBUFFERSIZE                   65536
#endif /* OPCUA_P_TCPSNDBUFFERSIZE */

/** @brief default buffer(chunk sizes) (also max value) */
#ifndef OPCUA_TCPLISTENER_DEFAULTCHUNKSIZE
# define OPCUA_TCPLISTENER_DEFAULTCHUNKSIZE         ((OpcUa_UInt32)65536)
#endif /* OPCUA_TCPLISTENER_DEFAULTCHUNKSIZE */
#ifndef OPCUA_TCPCONNECTION_DEFAULTCHUNKSIZE
# define OPCUA_TCPCONNECTION_DEFAULTCHUNKSIZE       ((OpcUa_UInt32)65536)
#endif /* OPCUA_TCPCONNECTION_DEFAULTCHUNKSIZE */

/** @brief if defined, the tcpstream expects the write call to block until all data is sent */
#ifndef OPCUA_TCPSTREAM_BLOCKINGWRITE
# define OPCUA_TCPSTREAM_BLOCKINGWRITE              OPCUA_CONFIG_YES
#endif /* OPCUA_TCPSTREAM_BLOCKINGWRITE */

/** @brief The maximum number of client connections supported by a tcp listener. (maybe one reserved, see below) */
#ifndef OPCUA_TCPLISTENER_MAXCONNECTIONS
# define OPCUA_TCPLISTENER_MAXCONNECTIONS           50
#endif /* OPCUA_TCPLISTENER_MAXCONNECTIONS */

/** @brief Reserve one of the OPCUA_TCPLISTENER_MAXCONNECTIONS for an "MaxConnectionsReached" error channel?. */
#ifndef OPCUA_TCPLISTENER_USEEXTRAMAXCONNSOCKET
# define OPCUA_TCPLISTENER_USEEXTRAMAXCONNSOCKET    OPCUA_CONFIG_NO
#endif /* OPCUA_TCPLISTENER_USEEXTRAMAXCONNSOCKET */

/** @brief The maximum number of sockets supported by a socket manager. */
#ifndef OPCUA_P_SOCKETMANAGER_NUMBEROFSOCKETS
# define OPCUA_P_SOCKETMANAGER_NUMBEROFSOCKETS      60
#endif /* OPCUA_P_SOCKETMANAGER_NUMBEROFSOCKETS */

/** @brief The maximum number of socket managers in multithreading config, supported by the socket module. */
#ifndef OPCUA_SOCKET_MAXMANAGERS
# define OPCUA_SOCKET_MAXMANAGERS                   60
#endif /* OPCUA_SOCKET_MAXMANAGERS */

/** @brief the time interval in msec at which the secureconnection checks for timeouts. */
#ifndef OPCUA_SECURECONNECTION_TIMEOUTINTERVAL
# define OPCUA_SECURECONNECTION_TIMEOUTINTERVAL     1000
#endif /* OPCUA_SECURECONNECTION_TIMEOUTINTERVAL */

/*============================================================================
 * type support
 *===========================================================================*/
/** @brief type exclusion configuration */
#include "opcua_exclusions.h"

/*============================================================================
 * build configuration string
 *===========================================================================*/
#define OPCUA_PROXYSTUB_STATICCONFIGSTRING          "UseSafeFunction:"OPCUA_TOSTRING(OPCUA_USE_SAFE_FUNCTIONS)"\\"\
                                                    "Force32BitEnums:"OPCUA_TOSTRING(OPCUA_FORCE_INT32_ENUMS)"\\"\
                                                    "Multithreaded:"OPCUA_TOSTRING(OPCUA_MULTITHREADED)"\\"\
                                                    "Synchronized:"OPCUA_TOSTRING(OPCUA_USE_SYNCHRONISATION)"\\"\
                                                    "ThreadPoolSupported:"OPCUA_TOSTRING(OPCUA_SECURELISTENER_SUPPORT_THREADPOOL)"\\"\
                                                    "ThreadPoolDynamicSize:"OPCUA_TOSTRING(OPCUA_THREADPOOL_EXPANSION)"\\"\
                                                    "ThreadPoolReloopTime:"OPCUA_TOSTRING(OPCUA_THREADPOOL_RELOOPTIME)"\\"\
                                                    "TraceLength:"OPCUA_TOSTRING(OPCUA_TRACE_MAXLENGTH)"\\"\
                                                    "SecurityTokenLifeTimeMin:"OPCUA_TOSTRING(OPCUA_SECURITYTOKEN_LIFETIME_MIN)"\\"\
                                                    "SecurityTokenLifeTimeMax:"OPCUA_TOSTRING(OPCUA_SECURITYTOKEN_LIFETIME_MAX)"\\"\
                                                    "SecureListenerWatchdogInterval:"OPCUA_TOSTRING(OPCUA_SECURELISTENER_WATCHDOG_INTERVAL)"\\"\
                                                    "SecureListenerInactiveChannelLifetime:"OPCUA_TOSTRING(OPCUA_SECURELISTENER_INACTIVECHANNELTIMEOUT)"\\"\
                                                    "SecureConnectionTimeoutInterval:"OPCUA_TOSTRING(OPCUA_SECURECONNECTION_TIMEOUTINTERVAL)"\\"\
                                                    "SocketBlockingWrite:"OPCUA_TOSTRING(OPCUA_TCPSTREAM_BLOCKINGWRITE)"\\"\
                                                    "TcpListenerMaxConnections:"OPCUA_TOSTRING(OPCUA_TCPLISTENER_MAXCONNECTIONS)"\\"\
                                                    "TcpListenerUseExtraSocket:"OPCUA_TOSTRING(OPCUA_TCPLISTENER_USEEXTRAMAXCONNSOCKET)
                                                    

#endif /* _OPCUA_CONFIG_H_ */
