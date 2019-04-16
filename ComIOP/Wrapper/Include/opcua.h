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

#ifndef _OpcUa_H_
#define _OpcUa_H_

/******************************************************************/
/* STACK INTERNAL FILE - NOT MEANT TO BE INCLUDED BY APPLICATIONS */
/******************************************************************/

/* Collection of includes needed everywhere or at least very often.   */
/* All other includes MUST be included in the source file!            */
#include <opcua_platformdefs.h> /* includes typemapping of primitives */
#include <opcua_proxystub.h>
#include <opcua_statuscodes.h>
#include <opcua_stackstatuscodes.h>
#include <opcua_errorhandling.h>

#include <opcua_string.h>
#include <opcua_memory.h>
#include <opcua_trace.h>

/* platform interface and referenced stack files */
#include <opcua_types.h>       /* needed for some security related files in p_interface */
#include <opcua_crypto.h>      /* needed for some security related files in p_interface */
#include <opcua_pki.h>
#include <opcua_p_interface.h> /* standalone file */

/* Do not include headers in headers if not absolutely necessary.   */
/* This creates a cascade of includes up to the libraries API level.*/


OPCUA_BEGIN_EXTERN_C

/* import  */
extern OpcUa_Port_CallTable*            OpcUa_ProxyStub_g_PlatformLayerCalltable;
extern OpcUa_ProxyStubConfiguration     OpcUa_ProxyStub_g_Configuration;

/*============================================================================
 * OpcUa_InitializeArray
 *===========================================================================*/
#define OpcUa_InitializeArray(xArray, xLength, xType) \
{ \
    int ii; \
    \
    for (ii = 0; ii < xLength; ii++) \
    { \
        Initialize_##xType(&((xArray)[ii])); \
    } \
}

/*============================================================================
 * OpcUa_ClearArray
 *===========================================================================*/
#define OpcUa_ClearArray(xArray, xLength, xType) \
{ \
    int ii; \
    \
    for (ii = 0; ii < xLength; ii++) \
    { \
        Clear_##xType(&((xArray)[ii])); \
    } \
}

/*============================================================================
 * OpcUa_ProxyStub_RegisterChannel
 *===========================================================================*/
OpcUa_Void OpcUa_ProxyStub_RegisterChannel();

/*============================================================================
 * OpcUa_ProxyStub_RegisterEndpoint
 *===========================================================================*/
OpcUa_Void OpcUa_ProxyStub_RegisterEndpoint();

/*============================================================================
 * OpcUa_ProxyStub_DeRegisterChannel
 *===========================================================================*/
OpcUa_Void OpcUa_ProxyStub_DeRegisterChannel();

/*============================================================================
 * OpcUa_ProxyStub_DeRegisterEndpoint
 *===========================================================================*/
OpcUa_Void OpcUa_ProxyStub_DeRegisterEndpoint();

OPCUA_END_EXTERN_C
#endif /* _OpcUa_H_ */
