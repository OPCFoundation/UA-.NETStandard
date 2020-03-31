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

#ifndef _OpcUa_Timer_H_
#define _OpcUa_Timer_H_ 1

OPCUA_BEGIN_EXTERN_C

#define OPCUA_P_TIMER_CREATE  OpcUa_ProxyStub_g_PlatformLayerCalltable->TimerCreate
#define OPCUA_P_TIMER_DELETE  OpcUa_ProxyStub_g_PlatformLayerCalltable->TimerDelete
#define OPCUA_P_CLEANUPTIMERS OpcUa_ProxyStub_g_PlatformLayerCalltable->TimersCleanup

typedef OpcUa_StatusCode (OPCUA_DLLCALL OpcUa_Timer_Callback)(  OpcUa_Void*             pvCallbackData, 
                                                                OpcUa_Timer             hTimer,
                                                                OpcUa_UInt32            msecElapsed);

OPCUA_EXPORT OpcUa_StatusCode OPCUA_DLLCALL OpcUa_Timer_Create( OpcUa_Timer*            hTimer,
                                                                OpcUa_UInt32            msecInterval, 
                                                                OpcUa_Timer_Callback*   fpTimerCallback,
                                                                OpcUa_Timer_Callback*   fpKillCallback,
                                                                OpcUa_Void*             pvCallbackData);

OPCUA_EXPORT OpcUa_StatusCode OPCUA_DLLCALL OpcUa_Timer_Delete( OpcUa_Timer*            phTimer);

OPCUA_END_EXTERN_C

#endif /*_OpcUa_Timer_H_ */
