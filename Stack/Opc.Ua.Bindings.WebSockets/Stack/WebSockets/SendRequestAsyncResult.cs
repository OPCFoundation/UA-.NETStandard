/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.

   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else

   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/

   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2

   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Security.Cryptography;

namespace Opc.Ua.Bindings
{
    internal class SendRequestAsyncResult : AsyncResultBase
    {
        public uint RequestId;
        public WebSocketConnection Connection;
        public IServiceRequest Request;
        public IServiceResponse Response;
        public Task WorkItem;
        public new CancellationToken CancellationToken;

        public SendRequestAsyncResult(AsyncCallback callback, object callbackData, int timeout)
        :
            base(callback, callbackData, timeout)
        {
            CancellationToken = new CancellationToken();
        }
    }
}
