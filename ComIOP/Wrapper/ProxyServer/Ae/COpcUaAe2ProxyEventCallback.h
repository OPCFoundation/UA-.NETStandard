/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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

#pragma once

#include "COpcUaAe2ProxySubscription.h"

using namespace System;
using namespace System::Collections::Generic;
using namespace Opc::Ua;
using namespace Opc::Ua::Com;

/// <summary>
/// Used to dispatch callbacks.
/// </summary>>
public ref class COpcUaAe2ProxyEventCallback : IComAeEventCallback
{
public:

	/// <summary>
	/// Creates a new callback,
	/// </summary>
	COpcUaAe2ProxyEventCallback(IOPCEventSink* ipCallback);

	/// <summary>
	/// Releases all resources used by the callback.
	/// </summary>
	~COpcUaAe2ProxyEventCallback();

    /// <summary>
    /// The finializer implementation.
    /// </summary>
    !COpcUaAe2ProxyEventCallback();

    /// <summary>
    /// The on event callback.
    /// </summary>
    virtual void OnEvent(
        unsigned int hClientSubscription,
        bool bRefresh,
        bool bLastRefresh,
		array<OpcRcw::Ae::ONEVENTSTRUCT>^ pEvents);

private:

	/// <summary>
	/// An unmanaged container for unmanaged data stored in the channel.
	/// </summary>
	IOPCEventSink* m_ipCallback;

	/// <summary>
	/// A synchronization object for the object.
	/// </summary>
	Object^ m_lock;
};
