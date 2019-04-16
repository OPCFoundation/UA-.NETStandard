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

#ifndef _OpcUa_Trace_H_
#define _OpcUa_Trace_H_ 1

OPCUA_BEGIN_EXTERN_C

/*============================================================================
 * Trace Levels
 *===========================================================================*/
/* custom trace levels - add your trace levels here ... */
#define OPCUA_TRACE_LEVEL_YOURTRACELEVEL 0x00000040 
/* ... */

/* predefined trace levels */
#define OPCUA_TRACE_LEVEL_ERROR         0x00000020 /* in-system errors, which require bugfixing        */
#define OPCUA_TRACE_LEVEL_WARNING       0x00000010 /* in-system warnings and extern errors             */
#define OPCUA_TRACE_LEVEL_SYSTEM        0x00000008 /* rare system messages (start, stop, connect)      */
#define OPCUA_TRACE_LEVEL_INFO          0x00000004 /* more detailed information about system events    */
#define OPCUA_TRACE_LEVEL_DEBUG         0x00000002 /* information needed for debug reasons             */
#define OPCUA_TRACE_LEVEL_CONTENT       0x00000001 /* all message content                              */

/* trace level packages */
#define OPCUA_TRACE_OUTPUT_LEVEL_ERROR   (OPCUA_TRACE_LEVEL_ERROR)
#define OPCUA_TRACE_OUTPUT_LEVEL_WARNING (OPCUA_TRACE_LEVEL_ERROR | OPCUA_TRACE_LEVEL_WARNING)
#define OPCUA_TRACE_OUTPUT_LEVEL_SYSTEM  (OPCUA_TRACE_LEVEL_ERROR | OPCUA_TRACE_LEVEL_WARNING | OPCUA_TRACE_LEVEL_SYSTEM)
#define OPCUA_TRACE_OUTPUT_LEVEL_INFO    (OPCUA_TRACE_LEVEL_ERROR | OPCUA_TRACE_LEVEL_WARNING | OPCUA_TRACE_LEVEL_SYSTEM | OPCUA_TRACE_LEVEL_INFO)
#define OPCUA_TRACE_OUTPUT_LEVEL_DEBUG   (OPCUA_TRACE_LEVEL_ERROR | OPCUA_TRACE_LEVEL_WARNING | OPCUA_TRACE_LEVEL_SYSTEM | OPCUA_TRACE_LEVEL_INFO | OPCUA_TRACE_LEVEL_DEBUG)
#define OPCUA_TRACE_OUTPUT_LEVEL_CONTENT (OPCUA_TRACE_LEVEL_ERROR | OPCUA_TRACE_LEVEL_WARNING | OPCUA_TRACE_LEVEL_SYSTEM | OPCUA_TRACE_LEVEL_INFO | OPCUA_TRACE_LEVEL_DEBUG | OPCUA_TRACE_LEVEL_CONTENT)
#define OPCUA_TRACE_OUTPUT_LEVEL_ALL     (0xFFFFFFFF)
#define OPCUA_TRACE_OUTPUT_LEVEL_NONE    (0x00000000)
/*============================================================================
 * Trace Initialize
 *===========================================================================*/
/**
* Initialize all resources needed for tracing.
*/
OPCUA_EXPORT OpcUa_StatusCode OPCUA_DLLCALL OpcUa_Trace_Initialize();

/*============================================================================
 * Trace Initialize
 *===========================================================================*/
/**
* Clear all resources needed for tracing.
*/
OPCUA_EXPORT OpcUa_Void OPCUA_DLLCALL OpcUa_Trace_Clear();

/*============================================================================
 * Change Trace Level
 *===========================================================================*/
/**
 * Activate or deactivate trace output during runtime.
 */
OPCUA_EXPORT OpcUa_Void OPCUA_DLLCALL OpcUa_Trace_ChangeTraceLevel(OpcUa_UInt32 a_uNewTraceLevel);

/*============================================================================
 * Activate/Deactivate Trace 
 *===========================================================================*/
/**
 * Activate or deactivate trace output during runtime.
 */
OPCUA_EXPORT OpcUa_Void OPCUA_DLLCALL OpcUa_Trace_Toggle(OpcUa_Boolean a_bActive);

/*============================================================================
 * Tracefunction
 *===========================================================================*/
/**
* @brief Writes the given string and the parameters to the trace device, if the given 
* trace level is activated in the header file.
*
* @see OpcUa_P_Trace
*
* @return The number of bytes written to the trace device.
*/
#if OPCUA_TRACE_ENABLE
 #if OPCUA_TRACE_FILE_LINE_INFO
  #define OpcUa_Trace(xLevel, xFormat, ...) OpcUa_Trace_Imp(xLevel, xFormat, __FILE__, __LINE__, __VA_ARGS__)
 #else /* OPCUA_TRACE_FILE_LINE_INFO */
  #define OpcUa_Trace OpcUa_Trace_Imp
 #endif /* OPCUA_TRACE_FILE_LINE_INFO */
#else /* OPCUA_TRACE_ENABLE */
 #define OpcUa_Trace(xLevel, xFormat, ...) 
#endif /* OPCUA_TRACE_ENABLE */

OPCUA_EXPORT OpcUa_Boolean OPCUA_DLLCALL OpcUa_Trace_Imp( 
    OpcUa_UInt32 uTraceLevel, 
    const OpcUa_CharA* sFormat,
#if OPCUA_TRACE_FILE_LINE_INFO
    const OpcUa_CharA* sFile,
    OpcUa_UInt32 sLine,
#endif /* OPCUA_TRACE_FILE_LINE_INFO */
    ...);

OPCUA_END_EXTERN_C

#endif /* _OpcUa_Trace_H_ */
