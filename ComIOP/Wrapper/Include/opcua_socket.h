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

#ifndef _OpcUa_Socket_H_
#define _OpcUa_Socket_H_ 1

OPCUA_BEGIN_EXTERN_C

#define OPCUA_P_SOCKETMANAGER_CREATE        OpcUa_ProxyStub_g_PlatformLayerCalltable->SocketManagerCreate
#define OPCUA_P_SOCKETMANAGER_DELETE        OpcUa_ProxyStub_g_PlatformLayerCalltable->SocketManagerDelete
#define OPCUA_P_SOCKETMANAGER_CREATESERVER  OpcUa_ProxyStub_g_PlatformLayerCalltable->SocketManagerCreateServer
#define OPCUA_P_SOCKETMANAGER_CREATECLIENT  OpcUa_ProxyStub_g_PlatformLayerCalltable->SocketManagerCreateClient
#define OPCUA_P_SOCKETMANAGER_SIGNALEVENT   OpcUa_ProxyStub_g_PlatformLayerCalltable->SocketManagerSignalEvent
#define OPCUA_P_SOCKETMANAGER_SERVELOOP     OpcUa_ProxyStub_g_PlatformLayerCalltable->SocketManagerServeLoop

#define OPCUA_P_SOCKET_READ                 OpcUa_ProxyStub_g_PlatformLayerCalltable->SocketRead
#define OPCUA_P_SOCKET_WRITE                OpcUa_ProxyStub_g_PlatformLayerCalltable->SocketWrite
#define OPCUA_P_SOCKET_CLOSE                OpcUa_ProxyStub_g_PlatformLayerCalltable->SocketClose
#define OPCUA_P_SOCKET_GETPEERINFO          OpcUa_ProxyStub_g_PlatformLayerCalltable->SocketGetPeerInfo
#define OPCUA_P_SOCKET_CHANGEEVENTLIST      /* Todo */
#define OPCUA_P_SOCKET_GETLASTERROR         OpcUa_ProxyStub_g_PlatformLayerCalltable->SocketGetLastError

#define OPCUA_P_INITIALIZENETWORK           OpcUa_ProxyStub_g_PlatformLayerCalltable->NetworkInitialize
#define OPCUA_P_CLEANUPNETWORK              OpcUa_ProxyStub_g_PlatformLayerCalltable->NetworkCleanup

OPCUA_END_EXTERN_C

#endif /* _OpcUa_Socket_H_ */
