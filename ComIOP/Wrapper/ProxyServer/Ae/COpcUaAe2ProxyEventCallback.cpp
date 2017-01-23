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

#include "StdAfx.h"
#include "COpcUaAe2ProxyEventCallback.h"
#include "COpcUaProxyUtils.h"

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;
using namespace System::Reflection;
using namespace System::Threading;
using namespace Opc::Ua;
using namespace Opc::Ua::Com;

/// <summary>
/// Dumps the current state.
/// </summary>
static void TraceState(String^ context, ... array<Object^>^ args)
{
	#ifdef TRACESTATE
    COpcUaProxyUtils::TraceState("COpcUaAe2ProxyEventCallback", context, args);
	#endif
}

// Constructor
COpcUaAe2ProxyEventCallback::COpcUaAe2ProxyEventCallback(IOPCEventSink* ipCallback)
{
	TraceState("COpcUaAe2ProxyEventCallback");

	m_lock = gcnew Object();
	m_ipCallback = ipCallback;
	m_ipCallback->AddRef();
}

// Destructor
COpcUaAe2ProxyEventCallback::~COpcUaAe2ProxyEventCallback(void)
{
	this->!COpcUaAe2ProxyEventCallback();
}

// Finalizer
COpcUaAe2ProxyEventCallback::!COpcUaAe2ProxyEventCallback() 
{
	TraceState("!COpcUaAe2ProxyEventCallback");

	Monitor::Enter(m_lock);

	if (m_ipCallback != NULL)
	{
		m_ipCallback->Release();
		m_ipCallback = NULL;
	}

	Monitor::Exit(m_lock);
}

// OnEvent
void COpcUaAe2ProxyEventCallback::OnEvent(
    unsigned int hClientSubscription,
    bool bRefresh,
    bool bLastRefresh,
	array<OpcRcw::Ae::ONEVENTSTRUCT>^ events)
{
	TraceState("OnEvent", "hClientSubscription", hClientSubscription, "bRefresh", bRefresh, "bLastRefresh", bLastRefresh);

	DWORD dwCount = (events != nullptr)?events->Length:0;
	IOPCEventSink* ipCallback = NULL;

	Monitor::Enter(m_lock);

	// check if callback has been released.
	if (m_ipCallback == NULL)
	{
		TraceState("OnEvent NO CALLBACK");
		return;
	}

	ipCallback = m_ipCallback;
	ipCallback->AddRef();
	
	Monitor::Exit(m_lock);

	ONEVENTSTRUCT* pEvents = NULL;

	try
	{
		// allocate return parameters.
		OpcProxy_AllocArrayToReturn(pEvents, dwCount+1, ONEVENTSTRUCT);

		// marshal return parameters.
		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			pEvents[ii].wChangeMask = events[ii].wChangeMask;
			pEvents[ii].wNewState = events[ii].wNewState;
			pEvents[ii].szSource = (LPWSTR)Marshal::StringToCoTaskMemUni(events[ii].szSource).ToPointer();
			pEvents[ii].ftTime.dwHighDateTime = events[ii].ftTime.dwHighDateTime;
			pEvents[ii].ftTime.dwLowDateTime = events[ii].ftTime.dwLowDateTime;
			pEvents[ii].szMessage = (LPWSTR)Marshal::StringToCoTaskMemUni(events[ii].szMessage).ToPointer();
			pEvents[ii].dwEventType = events[ii].dwEventType;
			pEvents[ii].dwEventCategory = events[ii].dwEventCategory;
			pEvents[ii].dwSeverity = events[ii].dwSeverity;
			pEvents[ii].szConditionName = (LPWSTR)Marshal::StringToCoTaskMemUni(events[ii].szConditionName).ToPointer();
			pEvents[ii].szSubconditionName = (LPWSTR)Marshal::StringToCoTaskMemUni(events[ii].szSubconditionName).ToPointer();
			pEvents[ii].wQuality = events[ii].wQuality;
			pEvents[ii].bAckRequired = events[ii].bAckRequired;
			pEvents[ii].ftActiveTime.dwHighDateTime = events[ii].ftActiveTime.dwHighDateTime;
			pEvents[ii].ftActiveTime.dwLowDateTime = events[ii].ftActiveTime.dwLowDateTime;
			pEvents[ii].dwCookie = events[ii].dwCookie;
			pEvents[ii].dwNumEventAttrs = events[ii].dwNumEventAttrs;
			pEvents[ii].pEventAttributes = (VARIANT*)events[ii].pEventAttributes.ToPointer();
			pEvents[ii].szActorID = (LPWSTR)Marshal::StringToCoTaskMemUni(events[ii].szActorID).ToPointer();
		}

		// invoke callback.
		HRESULT hr = m_ipCallback->OnEvent(
			hClientSubscription,
			(bRefresh)?1:0,
			(bLastRefresh)?1:0,
			dwCount,
			pEvents);

		TraceState("OnEvent Complete", "hResult", hr);
	}
    catch (Exception^ e)
    {
        TraceState("OnEvent Error", e->Message);
    }
	finally
	{
		ipCallback->Release();

		if (pEvents != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				CoTaskMemFree(pEvents[ii].szActorID);
				CoTaskMemFree(pEvents[ii].szConditionName);
				CoTaskMemFree(pEvents[ii].szSubconditionName);
				CoTaskMemFree(pEvents[ii].szMessage);
				CoTaskMemFree(pEvents[ii].szSource);
			}
		}

		CoTaskMemFree(pEvents);
	}
}
