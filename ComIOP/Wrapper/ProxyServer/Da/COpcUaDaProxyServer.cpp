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

#include "OpcUaComProxyServer.h"
#include "OpcUaComProxyServer_i.c"

#include "COpcUaDaProxyServer.h"
#include "COpcUaDaProxyGroup.h"
#include "COpcUaProxyUtils.h"

using namespace System;
using namespace System::Text;
using namespace System::Runtime::InteropServices;
using namespace System::Reflection;
using namespace System::Collections::Generic;
using namespace System::Security::Cryptography::X509Certificates;
using namespace Opc::Ua;
using namespace Opc::Ua::Com;
using namespace Opc::Ua::Com::Server;


//==============================================================================
// Static Functions

/// <summary>
/// Writes a trace message.
/// </summary>
static void TraceState(String^ context, ... array<Object^>^ args)
{
    #ifdef TRACESTATE
	COpcUaProxyUtils::TraceState("COpcUaDaProxyServer", context, args);
	#endif
}

//============================================================================
// COpcUaDaProxyServer

// Constructor
COpcUaDaProxyServer::COpcUaDaProxyServer()
{
	TraceState("COpcUaDaProxyServer");

	m_pInnerServer  = NULL;
	m_pClientName   = NULL;

	memset(&m_tServerStatus, 0, sizeof(OPCSERVERSTATUS));

	Version^ version = Assembly::GetExecutingAssembly()->GetName()->Version;

	m_tServerStatus.dwServerState = OPC_STATUS_FAILED;
    m_tServerStatus.ftStartTime   = OpcUtcNow();
	m_tServerStatus.dwBandWidth   = 0xFFFFFFFF;
	m_tServerStatus.wMajorVersion = (WORD)version->Major;
	m_tServerStatus.wMinorVersion = (WORD)version->Minor;
	m_tServerStatus.wBuildNumber  = (WORD)version->Build;
	m_tServerStatus.szVendorInfo  = L"OPC UA COM DA Proxy Server";


    try
    {
	    ComDaProxy^ server = gcnew ComDaProxy();
	    GCHandle hInnerServer = GCHandle::Alloc(server);
	    m_pInnerServer = ((IntPtr)hInnerServer).ToPointer();
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "COpcUaDaProxyServer: Unexpected error creating AE proxy.");
    }
}

// Destructor
COpcUaDaProxyServer::~COpcUaDaProxyServer()
{
	TraceState("~COpcUaDaProxyServer");

	if (m_pClientName != NULL)
	{
		CoTaskMemFree(m_pClientName);
		m_pClientName = NULL;
	}

	if (m_pInnerServer != NULL)
	{
		GCHandle hInnerServer = (GCHandle)IntPtr(m_pInnerServer);
		hInnerServer.Free();
		m_pInnerServer = NULL;
	}
}

// FinalConstruct
HRESULT COpcUaDaProxyServer::FinalConstruct()
{
	TraceState("FinalConstruct");

	COpcLock cLock(*this);

	HRESULT hResult = S_OK;

	ApplicationConfiguration^ configuration = nullptr;

	// load configuration.
	if (!COpcUaProxyUtils::Initialize(configuration))
	{
		return E_FAIL;
	}

	// get the CLSID being used.
	CLSID cClsid = GetCLSID();
	Guid clsid = (Guid)Marshal::PtrToStructure((IntPtr)&cClsid, Guid::typeid);

	// load the server.
    try
    {
	    ComProxy^ server = GetInnerServer();
	    server->Load(clsid, configuration);
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "COpcUaDaProxyServer: Unexpected error loading proxy for CLSID={0}.", clsid);
    }

	// register shutdown interface.
	RegisterInterface(IID_IOPCShutdown);

    return S_OK;
}

// FinalRelease
bool COpcUaDaProxyServer::FinalRelease()
{
	TraceState("FinalRelease");

	COpcLock cLock(*this);

	ComDaProxy^ server = GetInnerServer();

    try
    {
	    // release the group wrappers.
	    array<ComDaGroup^>^ groups = server->GetGroups();

	    for (int ii = 0; ii < groups->Length; ii++)
	    {
		    COpcUaDaProxyGroup* pGroup = (COpcUaDaProxyGroup*)groups[ii]->Handle.ToPointer();

		    if (pGroup != NULL)
		    {
			    pGroup->Release();
		    }
	    }

	    server->Unload();

		UnregisterInterface(IID_IOPCShutdown);
	    cLock.Unlock();
    }
    catch (Exception^ e)
    {
        Utils::Trace(e, "COpcUaDaProxyServer: Unexpected error unloading proxy.");
    }

	// decrement global reference count.
	COpcUaProxyUtils::Uninitialize();

	return true;
}

// GetInnerServer
ComDaProxy^ COpcUaDaProxyServer::GetInnerServer()
{
	if (m_pInnerServer == NULL)
	{
		return nullptr;
	}

	GCHandle hInnerServer = (GCHandle)IntPtr(m_pInnerServer);

	if (hInnerServer.IsAllocated)
	{
		return (ComDaProxy^)hInnerServer.Target;
	}

	return nullptr;
}

//==============================================================================
// IOPCCommon

// SetLocaleID
HRESULT COpcUaDaProxyServer::SetLocaleID(LCID dwLcid)
{
	TraceState("IOPCCommon.SetLocaleID");

	try
	{	
		ComDaProxy^ server = GetInnerServer();
		server->SetLocaleId(dwLcid);
		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// GetLocaleID
HRESULT COpcUaDaProxyServer::GetLocaleID(LCID *pdwLcid)
{
	TraceState("IOPCCommon.GetLocaleID");

	if (pdwLcid == 0)
	{
		return E_INVALIDARG;
	}

	*pdwLcid = 0;

	try
	{	
		ComDaProxy^ server = GetInnerServer();
		*pdwLcid = server->GetLocaleId();
		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// QueryAvailableLocaleIDs
HRESULT COpcUaDaProxyServer::QueryAvailableLocaleIDs(
	DWORD* pdwCount,
	LCID** pdwLcid)
{
	TraceState("IOPCCommon.QueryAvailableLocaleIDs");

	if (pdwCount == 0 || pdwLcid == 0)
	{
		return E_INVALIDARG;
	}

	*pdwCount = 0;
	*pdwLcid = NULL;

	LCID* pLcids = NULL;

	try
	{	
		ComDaProxy^ server = GetInnerServer();
		array<int>^ localeIds = server->GetAvailableLocaleIds();
		OpcProxy_AllocArrayToReturn(pLcids, localeIds->Length, LCID);

		for (int ii = 0; ii < localeIds->Length; ii++)
		{
			pLcids[ii] = localeIds[ii];
		}

		*pdwCount = localeIds->Length;
		*pdwLcid = pLcids;

		return S_OK;
	}
	catch (Exception^ e)
	{
		CoTaskMemFree(pLcids);
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// GetErrorString
HRESULT COpcUaDaProxyServer::GetErrorString(
	HRESULT dwError,
	LPWSTR* ppString)
{
	TraceState("IOPCCommon.GetErrorString");

	if (ppString == 0)
	{
		return E_INVALIDARG;
	}

	return COpcCommon::GetErrorString(OPC_MESSAGE_MODULE_NAME_DA, dwError, LOCALE_SYSTEM_DEFAULT, ppString);
}

// SetClientName
HRESULT COpcUaDaProxyServer::SetClientName(LPCWSTR szName)
{
	TraceState("SetClientName");

	// check arguments.
	if (szName == 0)
	{
		return E_INVALIDARG;
	}

	COpcLock cLock(*this);

	int length = wcslen(szName);

	// allocate memory.
	m_pClientName = (LPWSTR)CoTaskMemAlloc((sizeof(WCHAR)+1)*length);
	
	if (m_pClientName == 0)
	{
		return E_OUTOFMEMORY;
	}

	// copy the string.
	wcscpy_s(m_pClientName, length+1, szName);

	return S_OK;
}

//============================================================================
// IOPCServer

// AddGroup
HRESULT COpcUaDaProxyServer::AddGroup(
    LPCWSTR    szName,
    BOOL       bActive,
    DWORD      dwRequestedUpdateRate,
    OPCHANDLE  hClientGroup,
    LONG*      pTimeBias,
    FLOAT*     pPercentDeadband,
    DWORD      dwLCID,
    OPCHANDLE* phServerGroup,
    DWORD*     pRevisedUpdateRate,
    REFIID     riid,
    LPUNKNOWN* ppUnk
)
{
	TraceState("AddGroup");

	// check arguments.
	if (ppUnk == NULL)
	{
		return E_INVALIDARG;
	}

	// only support default locale.
	if (dwLCID != 0 && dwLCID != LOCALE_SYSTEM_DEFAULT)
	{
		dwLCID = LOCALE_SYSTEM_DEFAULT;
	}

	try
	{
		ComDaProxy^ server = GetInnerServer();

        // look up local timezone if timezone not specified.
        int timeBias = 0;

        if (pTimeBias == NULL)
        {
            TIME_ZONE_INFORMATION cZoneInfo;
            GetTimeZoneInformation(&cZoneInfo);    
            timeBias = cZoneInfo.Bias;
        }
        else
        {
            timeBias = *pTimeBias;
        }

		// get the deadband.
		float deadband = 0;

		if (pPercentDeadband != NULL)
		{
			deadband = *pPercentDeadband;
		}

		// unmarshal the group name.
		String^ groupName = Marshal::PtrToStringUni((IntPtr)(LPWSTR)szName);

		// add the group.
		ComDaGroup^ group = server->AddGroup(
			groupName,
			bActive!= 0,
			dwRequestedUpdateRate,
			hClientGroup,
			timeBias,
			deadband,
			dwLCID);

		// wrap the group.
		COpcUaDaProxyGroup* pGroup = new COpcUaDaProxyGroup(this, group);

		// fetch required interface.
		if (FAILED(pGroup->QueryInterface(riid, (void**)ppUnk)))
		{
			server->RemoveGroup(group);
			return E_NOINTERFACE;
		}

		*phServerGroup = group->ServerHandle;
		*pRevisedUpdateRate = group->ActualUpdateRate;

		// return necessary success code.
		if (*pRevisedUpdateRate != dwRequestedUpdateRate)
		{
			return OPC_S_UNSUPPORTEDRATE;
		}

		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}
}

// GetErrorString
HRESULT COpcUaDaProxyServer::GetErrorString( 
    HRESULT dwError,
    LCID    dwLocale,
    LPWSTR* ppString
)
{  
	TraceState("GetErrorString");

	if (ppString == 0)
	{
		return E_INVALIDARG;
	}

	try
	{
		if (dwLocale != 0 && dwLocale != LOCALE_SYSTEM_DEFAULT)
		{
			ComDaProxy^ server = GetInnerServer();
			array<int>^ localeIds = server->GetAvailableLocaleIds();

			bool found = false;

			for (int ii = 0; ii < localeIds->Length; ii++)
			{
				if (dwLocale == localeIds[ii])
				{
					found = true;
					break;
				}
			}

			if (!found)
			{
				return E_INVALIDARG;
			}
		}

		return COpcCommon::GetErrorString(OPC_MESSAGE_MODULE_NAME_DA, dwError, LOCALE_SYSTEM_DEFAULT, ppString);
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}
}

// GetGroupByName
HRESULT COpcUaDaProxyServer::GetGroupByName(
    LPCWSTR    szName,
    REFIID     riid,
    LPUNKNOWN* ppUnk
)
{
	TraceState("GetGroupByName");

	// check arguments.
	if (ppUnk == NULL || szName == NULL || szName[0] == 0)
	{
		return E_INVALIDARG;
	}

    COpcLock cLock(*this);

	// get inner server.
	ComDaProxy^ server = GetInnerServer();

	if (server == nullptr)
	{
		return E_FAIL;
	}

	// look up group.
	String^ groupName = Marshal::PtrToStringUni((IntPtr)(LPWSTR)szName);
	ComDaGroup^ group = server->GetGroupByName(groupName);

	if (group == nullptr)
	{
		return E_INVALIDARG;
	}

	// get the wrapper.
	COpcUaDaProxyGroup* pGroup = (COpcUaDaProxyGroup*)group->Handle.ToPointer();

	// fetch required interface.
	if (FAILED(pGroup->QueryInterface(riid, (void**)ppUnk)))
	{
		return E_NOINTERFACE;
	}

	return S_OK;
}

// GetStatus
HRESULT COpcUaDaProxyServer::GetStatus( 
    OPCSERVERSTATUS** ppServerStatus
)
{   
	TraceState("GetStatus");

	// check arguments.
	if (ppServerStatus == NULL)
	{
		return E_INVALIDARG;
	}

	// allocate result.
	OPCSERVERSTATUS* pStatus = *ppServerStatus = (OPCSERVERSTATUS*)CoTaskMemAlloc(sizeof(OPCSERVERSTATUS));

	if (ppServerStatus == NULL)
	{
		return E_OUTOFMEMORY;
	}

    COpcLock cLock(*this);

	// copy the current status.
	memcpy_s(pStatus, sizeof(OPCSERVERSTATUS), &m_tServerStatus, sizeof(OPCSERVERSTATUS));

	pStatus->ftCurrentTime = OpcUtcNow();
	pStatus->dwServerState = OPC_STATUS_FAILED;
	pStatus->szVendorInfo  = NULL;

	// get inner server.
	ComDaProxy^ server = GetInnerServer();

	if (server != nullptr)
	{
		pStatus->dwGroupCount = server->GroupCount;
		pStatus->ftLastUpdateTime = COpcUaProxyUtils::GetFILETIME(server->LastUpdateTime);

		if (server->Connected)
		{
			pStatus->dwServerState = OPC_STATUS_RUNNING;
		}

		ConfiguredEndpoint^ endpoint = server->Endpoint;

		if (endpoint != nullptr)
		{
			pStatus->szVendorInfo = (LPWSTR)Marshal::StringToCoTaskMemUni(endpoint->ToString()).ToPointer();
		}
	}

	return S_OK;
}

// RemoveGroup
HRESULT COpcUaDaProxyServer::RemoveGroup(
    OPCHANDLE hServerGroup,
    BOOL      bForce
)
{    
	TraceState("RemoveGroup");

	try
	{
		// get inner server.
		ComDaProxy^ server = GetInnerServer();

		// look up group.
		ComDaGroup^ group = server->GetGroupByHandle(hServerGroup);

		if (group == nullptr)
		{
			return E_INVALIDARG;
		}

		// get the wrapper.
		COpcUaDaProxyGroup* pGroup = (COpcUaDaProxyGroup*)group->Handle.ToPointer();

		// remove the group.
		server->RemoveGroup(group);

		// release the wrapper.
		pGroup->Delete();
		ULONG ulRefs = pGroup->Release();

		// check if group still in use.
		if (!bForce && ulRefs > 0)
		{
			return OPC_S_INUSE;
		}

		return S_OK;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}
}

// CreateGroupEnumerator
HRESULT COpcUaDaProxyServer::CreateGroupEnumerator(
    OPCENUMSCOPE dwScope, 
    REFIID       riid, 
    LPUNKNOWN*   ppUnk
)
{    
	TraceState("CreateGroupEnumerator");

	// check arguments.
	if (ppUnk == NULL)
	{
		return E_INVALIDARG;
	}

	HRESULT hResult = S_OK;

	// check scope.
	switch (dwScope)
	{
		// public groups not supported.
		case OPC_ENUM_PUBLIC_CONNECTIONS:
		case OPC_ENUM_PUBLIC:
		{
			hResult = S_FALSE;
			break;
		}
		
		// return the same list for all of these.
		case OPC_ENUM_PRIVATE_CONNECTIONS:
		case OPC_ENUM_ALL_CONNECTIONS:
		case OPC_ENUM_PRIVATE:
		case OPC_ENUM_ALL:
		{
			break;
		}

		// invalid scope.
		default:
		{
			return E_INVALIDARG;
		}
	}

	try
	{
		// get inner server.
		ComDaProxy^ server = GetInnerServer();

		// look up group.
		array<ComDaGroup^>^ groups = server->GetGroups();

		// return empty enumerator.
		if (groups == nullptr || groups->Length == 0 || hResult == S_FALSE)
		{
			IUnknown* ipEnum = NULL;

            if (riid == IID_IEnumString)
            {
                ipEnum = new COpcEnumString();
            }
            else
            {
                ipEnum = new COpcEnumUnknown();
            }

            // return requested interface.
            hResult = ipEnum->QueryInterface(riid, (void**)ppUnk);

            ipEnum->Release();

            if (FAILED(hResult))
            {
                return hResult;
            }

            return S_FALSE;
		}
		
		// check if enumerating by name.
        if (riid == IID_IEnumString)
		{
			// populate string enumerator.
			LPWSTR* wszNames = OpcArrayAlloc(LPWSTR, groups->Length);

			for (int ii = 0; ii < groups->Length; ii++)
			{
				wszNames[ii] = (LPWSTR)Marshal::StringToCoTaskMemUni(groups[ii]->Name).ToPointer();
			}

			IUnknown* ipEnum = new COpcEnumString(groups->Length, wszNames);

			// return requested interface.
			hResult = ipEnum->QueryInterface(riid, (void**)ppUnk);

			ipEnum->Release();

			if (FAILED(hResult))
			{
				return hResult;
			}

			return S_OK;
		}
		
		// check if enumerating by unknown.
        if (riid == IID_IEnumUnknown)
		{
			// populate unknown enumerator.
			IUnknown** ippUnknowns  = OpcArrayAlloc(IUnknown*, groups->Length);

			for (int ii = 0; ii < groups->Length; ii++)
            {
				// get the wrapper.
				COpcUaDaProxyGroup* pGroup = (COpcUaDaProxyGroup*)groups[ii]->Handle.ToPointer();

				// get interfaces.
                ((IOPCItemMgt*)pGroup)->QueryInterface(IID_IUnknown, (void**)&(ippUnknowns[ii]));
            }

            IUnknown* ipEnum = new COpcEnumUnknown(groups->Length, ippUnknowns);

			// return requested interface.
			hResult = ipEnum->QueryInterface(riid, (void**)ppUnk);

			ipEnum->Release();

			if (FAILED(hResult))
			{
				return hResult;
			}

			return S_OK;
		}

		return E_NOINTERFACE;
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}
}

//============================================================================
// IOPCItemIO

// Read
HRESULT COpcUaDaProxyServer::Read(
    DWORD         dwCount, 
    LPCWSTR     * pszItemIDs,
    DWORD       * pdwMaxAge,
    VARIANT    ** ppvValues,
    WORD       ** ppwQualities,
	::FILETIME ** ppftTimeStamps,
    HRESULT    ** ppErrors
)
{
	TraceState("Read");

	// check arguments.
	if (dwCount == 0 || pszItemIDs == NULL || pdwMaxAge == NULL || ppvValues == NULL || ppwQualities == NULL || ppftTimeStamps == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppvValues = NULL;
	*ppwQualities = NULL;
	*ppftTimeStamps = NULL;
	*ppErrors = NULL;

	// allocate memory for results.
	VARIANT* pvValues = (VARIANT*)CoTaskMemAlloc(sizeof(VARIANT)*dwCount);
	WORD* pwQualities = (WORD*)CoTaskMemAlloc(sizeof(WORD)*dwCount);
	::FILETIME* pftTimeStamps = (::FILETIME*)CoTaskMemAlloc(sizeof(::FILETIME)*dwCount);
	HRESULT* pErrors = (HRESULT*)CoTaskMemAlloc(sizeof(HRESULT)*dwCount);

	try
	{
		// check allocations.
		if (pvValues == NULL || pwQualities == NULL || pftTimeStamps == NULL || pErrors == NULL)
		{
			throw gcnew System::OutOfMemoryException();
		}

		// initialize results.
		memset(pvValues, 0, sizeof(VARIANT)*dwCount);
		memset(pwQualities, 0, sizeof(WORD)*dwCount);
		memset(pftTimeStamps, 0, sizeof(::FILETIME)*dwCount);
		memset(pErrors, 0, sizeof(HRESULT)*dwCount);

		// get inner server.
		ComDaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<String^>^ itemIds = gcnew array<String^>(dwCount);

		for (int ii = 0; ii < itemIds->Length; ii++)
		{
			itemIds[ii] = Marshal::PtrToStringUni((IntPtr)(LPWSTR)pszItemIDs[ii]);
		}

		// read values.
		array<DaValue^>^ values = server->Read(itemIds);

		// copy results.
		HRESULT hResult = S_OK;

		for (int ii = 0; ii < values->Length; ii++)
		{
			pErrors[ii] = values[ii]->Error;

			if (FAILED(pErrors[ii]))
			{
				hResult = S_FALSE;
				continue;
			}

			// need to watch for conversion errors for some values.
			if (!COpcUaProxyUtils::MarshalVARIANT(pvValues[ii], values[ii]->Value, pErrors[ii]))
			{
				hResult = S_FALSE;
				continue;
			}

			pwQualities[ii] = values[ii]->Quality;
			pftTimeStamps[ii] = COpcUaProxyUtils::GetFILETIME(values[ii]->Timestamp);
		}

		// fix any marshalling issues.
		COpcUaProxyUtils::FixupOutputVariants(dwCount, *ppvValues);
	
		*ppvValues = pvValues;
		*ppwQualities = pwQualities;
		*ppftTimeStamps = pftTimeStamps;
		*ppErrors = pErrors;

		return hResult;
	}
	catch (Exception^ e)
	{
		// free variants on error.
		if (pvValues != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				VariantClear(&(pvValues[ii]));
			}
		}

		// free allocated results.
		CoTaskMemFree(pvValues);
		CoTaskMemFree(pwQualities);
		CoTaskMemFree(pftTimeStamps);
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// WriteVQT
HRESULT COpcUaDaProxyServer::WriteVQT(
    DWORD         dwCount, 
    LPCWSTR    *  pszItemIDs,
    OPCITEMVQT *  pItemVQT,
    HRESULT    ** ppErrors
)
{
	TraceState("WriteVQT");

	// check arguments.
	if (dwCount == 0 || pszItemIDs == NULL || pItemVQT == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppErrors = NULL;

	// allocate memory for results.
	HRESULT* pErrors = (HRESULT*)CoTaskMemAlloc(sizeof(HRESULT)*dwCount);

	try
	{
		// check allocations.
		if (pErrors == NULL)
		{
			throw gcnew System::OutOfMemoryException();
		}

		// initialize results.
		memset(pErrors, 0, sizeof(HRESULT)*dwCount);

		// get inner server.
		ComDaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			throw gcnew System::NullReferenceException();
		}

		// convert item ids.
		array<String^>^ itemIds = gcnew array<String^>(dwCount);
		array<DaValue^>^ values = gcnew array<DaValue^>(dwCount);

		for (int ii = 0; ii < itemIds->Length; ii++)
		{
			itemIds[ii] = Marshal::PtrToStringUni((IntPtr)(LPWSTR)pszItemIDs[ii]);

			values[ii] = gcnew DaValue();
			values[ii]->Value = Marshal::GetObjectForNativeVariant((IntPtr)&(pItemVQT[ii].vDataValue));
			values[ii]->Quality = OPC_QUALITY_GOOD;
			values[ii]->Timestamp = DateTime::MinValue;

			if (pItemVQT[ii].bQualitySpecified)
			{
				values[ii]->Quality = pItemVQT[ii].wQuality;
			}

			if (pItemVQT[ii].bTimeStampSpecified)
			{
				values[ii]->Timestamp = ComUtils::GetDateTime((IntPtr)&(pItemVQT[ii].ftTimeStamp));
			}
		}

		// write values.
		array<int>^ results = server->Write(itemIds, values);

		// copy results.
		HRESULT hResult = S_OK;

		for (int ii = 0; ii < results->Length; ii++)
		{
			pErrors[ii] = results[ii];

			if (FAILED(pErrors[ii]))
			{
				hResult = S_FALSE;
			}
		}

		*ppErrors = pErrors;

		return hResult;
	}
	catch (Exception^ e)
	{
		// free allocated results.
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

//=============================================================================
// IOPCBrowseServerAddressSpace

// QueryOrganization
HRESULT COpcUaDaProxyServer::QueryOrganization(OPCNAMESPACETYPE* pNameSpaceType)
{
	TraceState("QueryOrganization");

	if (pNameSpaceType == NULL)
	{
		return E_INVALIDARG;
	}

	*pNameSpaceType = OPC_NS_HIERARCHIAL;

	return S_OK;
}

// ChangeBrowsePosition
HRESULT COpcUaDaProxyServer::ChangeBrowsePosition(
    OPCBROWSEDIRECTION dwBrowseDirection,  
    LPCWSTR            szString
)
{    
	TraceState("ChangeBrowsePosition");

	try
	{
		// get inner server.
		ComDaProxy^ server = GetInnerServer();

		if (server == nullptr)
		{
			return E_FAIL;
		}

		// get target name.
		String^ targetName = Marshal::PtrToStringUni((IntPtr)(LPWSTR)szString);

		// dispatch operation.
		switch (dwBrowseDirection)
		{
			case OPC_BROWSE_UP:		
			{
				if (szString != NULL && szString[0] != 0)
				{
					return E_INVALIDARG;
				}

				server->BrowseUp();
				break;
			}

			case OPC_BROWSE_DOWN:	
			{
				if (szString == NULL || szString[0] == 0)
				{
					return E_INVALIDARG;
				}

				server->BrowseDown(targetName);
				break;
			}

			case OPC_BROWSE_TO:
			{
				server->BrowseTo(targetName);
				break;
			}

			default:
			{
				return E_INVALIDARG;
			}
		}
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// BrowseOPCItemIDs
HRESULT COpcUaDaProxyServer::BrowseOPCItemIDs(
    OPCBROWSETYPE   dwBrowseFilterType,
    LPCWSTR         szFilterCriteria,  
    VARTYPE         vtDataTypeFilter,     
    DWORD           dwAccessRightsFilter,
    LPENUMSTRING*   ppIEnumString
)
{   
	TraceState("BrowseOPCItemIDs");

	*ppIEnumString = NULL;

	// validate browse filters.
	if (dwBrowseFilterType < OPC_BRANCH || dwBrowseFilterType > OPC_FLAT)
	{
		return E_INVALIDARG;
	}

	// validate access rights.
	if ((dwAccessRightsFilter & 0xFFFFFFFC) != 0)
	{
		return E_INVALIDARG;
	}

	LPWSTR* pNames = NULL;
	DWORD dwCount = 0;

	try
	{	
		// get inner server.
		ComDaProxy^ server = GetInnerServer();

		// get target name.
		String^ filter = Marshal::PtrToStringUni((IntPtr)(LPWSTR)szFilterCriteria);

		// fetch the matching items.
		IList<String^>^ names = nullptr;
		
		if (dwBrowseFilterType != OPC_FLAT)
		{
			names = server->BrowseForNames(
				dwBrowseFilterType == OPC_BRANCH,
				filter, 
				(short)vtDataTypeFilter, 
				(int)dwAccessRightsFilter);
		}
		else
		{
			names = server->BrowseForItems(
				filter, 
				(short)vtDataTypeFilter, 
				(int)dwAccessRightsFilter);
		}

		// create enumerator.
		return COpcUaProxyUtils::GetEnumerator(names, IID_IUnknown, (void**)ppIEnumString);
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}
}

// GetItemID
HRESULT COpcUaDaProxyServer::GetItemID(
    LPWSTR  wszItemName,
    LPWSTR* pszItemID
)
{   
	TraceState("GetItemID");

    // check for invalid arguments
    if (pszItemID == NULL)
    {
        return E_INVALIDARG;
    }

	// initialize output parameters.
	*pszItemID = NULL;

	try
	{
		// get inner server.
		ComDaProxy^ server = GetInnerServer();

		// get item name.
		String^ itemName = Marshal::PtrToStringUni((IntPtr)(LPWSTR)wszItemName);

		// look up item name.
		String^ itemId = server->GetItemId(itemName);

		if (itemId == nullptr)
		{			
			return E_INVALIDARG;
		}

		// return result.
		*pszItemID = (LPWSTR)Marshal::StringToCoTaskMemUni(itemId).ToPointer();
	}
	catch (Exception^ e)
	{
		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// BrowseAccessPaths
HRESULT COpcUaDaProxyServer::BrowseAccessPaths(
    LPCWSTR       szItemID,  
    LPENUMSTRING* ppIEnumString
)
{   
	TraceState("BrowseAccessPaths");

    if (ppIEnumString == NULL)
    {
        return E_INVALIDARG;
    }

    // access paths not implemented.
    *ppIEnumString = NULL;
    return E_NOTIMPL;
}

//============================================================================
// IOPCItemProperties

// QueryAvailableProperties
HRESULT COpcUaDaProxyServer::QueryAvailableProperties( 
    LPWSTR     szItemID,
    DWORD    * pdwCount,
    DWORD   ** ppPropertyIDs,
    LPWSTR  ** ppDescriptions,
    VARTYPE ** ppvtDataTypes
)
{   
	TraceState("QueryAvailableProperties");

    // check for invalid arguments
    if (pdwCount == NULL || ppPropertyIDs == NULL || ppDescriptions == NULL || ppvtDataTypes == NULL)
    {
        return E_INVALIDARG;
    }

	// initialize output parameters.
	*pdwCount = 0;
	*ppPropertyIDs = NULL;
	*ppDescriptions = NULL;
	*ppvtDataTypes = NULL;

	DWORD dwCount = 0;
	DWORD* pPropertyIDs = NULL;
	LPWSTR* pDescriptions = NULL;
	VARTYPE* pvtDataTypes = NULL;

	try
	{	
		// get inner server.
		ComDaProxy^ server = GetInnerServer();

		// get item name.
		String^ itemId = Marshal::PtrToStringUni((IntPtr)(LPWSTR)szItemID);

		// get properties.
		IList<DaProperty^>^ properties = server->GetProperties(itemId);

		if (properties == nullptr ||  properties->Count == 0)
		{
			return OPC_E_INVALIDITEMID;
		}

		// allocate arrays to return.
		dwCount = properties->Count;		
		OpcProxy_AllocArrayToReturn(pPropertyIDs, dwCount, DWORD);
		OpcProxy_AllocArrayToReturn(pDescriptions, dwCount, LPWSTR);
		OpcProxy_AllocArrayToReturn(pvtDataTypes, dwCount, VARTYPE);

		// copy results.
		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			DaProperty^ property = properties[ii];

			pPropertyIDs[ii] = property->PropertyId;
			pDescriptions[ii] = (LPWSTR)Marshal::StringToCoTaskMemUni(property->Name).ToPointer();
			pvtDataTypes[ii] = property->DataType;
		}

		// return result.
		*pdwCount = dwCount;
		*ppPropertyIDs = pPropertyIDs;
		*ppDescriptions = pDescriptions;
		*ppvtDataTypes = pvtDataTypes;
	}
	catch (Exception^ e)
	{
		// clean up on error.
		if (pDescriptions != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				CoTaskMemFree(pDescriptions[ii]);
			}
		}

		CoTaskMemFree(pPropertyIDs);
		CoTaskMemFree(pDescriptions);
		CoTaskMemFree(pvtDataTypes);

		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// GetItemProperties
HRESULT COpcUaDaProxyServer::GetItemProperties( 
    LPWSTR     szItemID,
    DWORD      dwCount,
    DWORD    * pdwPropertyIDs,
    VARIANT ** ppvData,
    HRESULT ** ppErrors
)
{    
	TraceState("GetItemProperties");

	// check arguments.
	if (dwCount == 0 || pdwPropertyIDs == NULL || ppvData == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppvData = NULL;
	*ppErrors = NULL;

	// allocate memory for results.
	VARIANT* pvData = NULL;
	HRESULT* pErrors = NULL;

	try
	{
		// get inner server.
		ComDaProxy^ server = GetInnerServer();

		// convert input parameters.
		String^ itemId = Marshal::PtrToStringUni((IntPtr)(LPWSTR)szItemID);

		array<int>^ propertyIds = gcnew array<int>(dwCount);

		for (int ii = 0; ii < propertyIds->Length; ii++)
		{
			propertyIds[ii] = pdwPropertyIDs[ii];
		}

		// read values.
		array<DaValue^>^ values = server->GetPropertyValues(itemId, propertyIds);

		if (values == nullptr || values->Length == 0)
		{
			return OPC_E_INVALIDITEMID;
		}

		// allocate arrays to return.
		OpcProxy_AllocArrayToReturn(pvData, dwCount, VARIANT);
		OpcProxy_AllocArrayToReturn(pErrors, dwCount, HRESULT);

		// copy results.
		HRESULT hResult = S_OK;

		for (int ii = 0; ii < values->Length; ii++)
		{
			pErrors[ii] = values[ii]->Error;

			if (pErrors[ii] != S_OK)
			{
				hResult = S_FALSE;
				continue;
			}

			// need to watch for conversion errors for some values.
			if (!COpcUaProxyUtils::MarshalVARIANT(pvData[ii], values[ii]->Value, pErrors[ii]))
			{
				hResult = S_FALSE;
				continue;
			}
		}

		// fix any marshalling issues.
		COpcUaProxyUtils::FixupOutputVariants(dwCount, pvData);
	
		*ppvData = pvData;
		*ppErrors = pErrors;

		return hResult;
	}
	catch (Exception^ e)
	{
		// free variants on error.
		if (pvData != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				VariantClear(&(pvData[ii]));
			}
		}

		// free allocated results.
		CoTaskMemFree(pvData);
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

// LookupItemIDs
HRESULT COpcUaDaProxyServer::LookupItemIDs( 
    LPWSTR     szItemID,
    DWORD      dwCount,
    DWORD    * pdwPropertyIDs,
    LPWSTR  ** ppszNewItemIDs,
    HRESULT ** ppErrors
)
{
	TraceState("LookupItemIDs");

	// check arguments.
	if (dwCount == 0 || pdwPropertyIDs == NULL || ppszNewItemIDs == NULL || ppErrors == NULL)
	{
		return E_INVALIDARG;
	}

	*ppszNewItemIDs = NULL;
	*ppErrors = NULL;

	// allocate memory for results.
	LPWSTR* pszNewItemIDs = NULL;
	HRESULT* pErrors = NULL;

	try
	{
		// get inner server.
		ComDaProxy^ server = GetInnerServer();

		// convert input parameters.
		String^ itemId = Marshal::PtrToStringUni((IntPtr)(LPWSTR)szItemID);
		array<int>^ propertyIds = gcnew array<int>(dwCount);

		for (int ii = 0; ii < propertyIds->Length; ii++)
		{
			propertyIds[ii] = pdwPropertyIDs[ii];
		}

		// read values.
		array<String^>^ itemIds = nullptr;	
		IList<int>^ results = server->GetItemIds(itemId, propertyIds, itemIds);

		if (results == nullptr || results->Count == 0)
		{
			return OPC_E_INVALIDITEMID;
		}

		// allocate arrays to return.
		OpcProxy_AllocArrayToReturn(pszNewItemIDs, dwCount, LPWSTR);
		OpcProxy_AllocArrayToReturn(pErrors, dwCount, HRESULT);

		// copy results.
		HRESULT hResult = S_OK;

		for (int ii = 0; ii < results->Count; ii++)
		{
			pErrors[ii] = results[ii];

			if (pErrors[ii] != S_OK)
			{
				hResult = S_FALSE;
			}

			if (!String::IsNullOrEmpty(itemIds[ii]))
			{
				pszNewItemIDs[ii] = (LPWSTR)Marshal::StringToCoTaskMemUni(itemIds[ii]).ToPointer();
			}
		}

		*ppszNewItemIDs = pszNewItemIDs;
		*ppErrors = pErrors;

		return hResult;
	}
	catch (Exception^ e)
	{
		// free strings on error.
		if (pszNewItemIDs != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				CoTaskMemFree(pszNewItemIDs[ii]);
			}
		}

		// free allocated results.
		CoTaskMemFree(pszNewItemIDs);
		CoTaskMemFree(pErrors);

		// extract unexpected error.
		return Marshal::GetHRForException(e);
	}
}

//=============================================================================
// IOPCBrowse

// GetProperties
HRESULT COpcUaDaProxyServer::GetProperties( 
    DWORD		        dwItemCount,
    LPWSTR*             pszItemIDs,
    BOOL		        bReturnPropertyValues,
    DWORD		        dwPropertyCount,
    DWORD*              pdwPropertyIDs,
    OPCITEMPROPERTIES** ppItemProperties 
)
{   
	TraceState("GetProperties");
 
    // check for invalid arguments
    if (dwItemCount == 0 || pszItemIDs == NULL || ppItemProperties == NULL)
    {
        return E_INVALIDARG;
    }

	// initialize output parameters.
	*ppItemProperties = NULL;
        
	OPCITEMPROPERTIES* pItemProperties = NULL;

	try
	{		
		ComDaProxy^ server = GetInnerServer();

		// unmarshal input parameters.
		array<int>^ propertyIds = gcnew array<int>(dwPropertyCount);

		for (DWORD ii = 0; ii < dwPropertyCount; ii++)
		{
			propertyIds[ii] = pdwPropertyIDs[ii];
		}

		array<ComDaReadPropertiesRequest^>^ requests = gcnew array<ComDaReadPropertiesRequest^>(dwItemCount);

		for (DWORD ii = 0; ii < dwItemCount; ii++)
		{
			requests[ii] = gcnew ComDaReadPropertiesRequest();
			requests[ii]->ItemId = Marshal::PtrToStringUni((IntPtr)pszItemIDs[ii]);
		}

		// get properties.
		IList<DaProperty^>^ descriptions = server->GetProperties(requests, propertyIds);

		// allocate arrays to return.
		OpcProxy_AllocArrayToReturn(pItemProperties, dwItemCount, OPCITEMPROPERTIES);

		// copy results.
		HRESULT hResult = S_OK;

		for (DWORD ii = 0; ii < dwItemCount; ii++)
		{
			OPCITEMPROPERTIES* pItemResult = &(pItemProperties[ii]);
			pItemResult->hrErrorID = requests[ii]->Error;

			if (requests[ii]->Error < 0)
			{
				hResult = S_FALSE;
				continue;
			}
			
			COpcUaProxyUtils::MarshalProperties(
				*pItemResult,
				propertyIds,
				bReturnPropertyValues != 0,
				descriptions,
				requests[ii]->Values);

			// propagate error notice.
			if (pItemResult->hrErrorID == S_FALSE)
			{
				hResult = S_FALSE;
			}
		}

		// return result.
		*ppItemProperties = pItemProperties;

		return hResult;
	}
	catch (Exception^ e)
	{
		// clean up on error.
		if (pItemProperties != NULL)
		{
			for (DWORD ii = 0; ii < dwItemCount; ii++)
			{
				COpcUaProxyUtils::FreeOPCITEMPROPERTIES(pItemProperties[ii]);
			}
		}

		CoTaskMemFree(pItemProperties);

		return Marshal::GetHRForException(e);
	}

	return S_OK;
}

// Browse
HRESULT COpcUaDaProxyServer::Browse(
	LPWSTR	           szItemName,
	LPWSTR*	           pszContinuationPoint,
	DWORD              dwMaxElementsReturned,
	OPCBROWSEFILTER    dwFilter,
	LPWSTR             szElementNameFilter,
	LPWSTR             szVendorFilter,
	BOOL               bReturnAllProperties,
	BOOL               bReturnPropertyValues,
	DWORD              dwPropertyCount,
	DWORD*             pdwPropertyIDs,
	BOOL*              pbMoreElements,
	DWORD*	           pdwCount,
	OPCBROWSEELEMENT** ppBrowseElements
)
{    
	TraceState("Browse");

    // check for invalid arguments
    if (szItemName == 0 || pbMoreElements == NULL || pdwCount == 0 || ppBrowseElements == NULL)
    {
        return E_INVALIDARG;
    }

	// initialize output parameters.
	*pdwCount = 0;
	*pbMoreElements = FALSE;
	*ppBrowseElements = NULL;
        
	DWORD dwCount = 0;
	OPCBROWSEELEMENT* pBrowseElements = NULL;

	try
	{		
		ComDaProxy^ server = GetInnerServer();

		// unmarshal input parameters.
		String^ itemId = Marshal::PtrToStringUni((IntPtr)szItemName);
		String^ nameFilter = Marshal::PtrToStringUni((IntPtr)szElementNameFilter);
		String^ continuationPoint = nullptr;

		if (*pszContinuationPoint != NULL)
		{
			continuationPoint = Marshal::PtrToStringUni((IntPtr)*pszContinuationPoint);
			CoTaskMemFree(*pszContinuationPoint);
			*pszContinuationPoint = NULL;
		}

		// browse the item.
		String^ revisedContinuationPoint = nullptr;
		
		IList<ComDaBrowseElement^>^ elements = server->BrowseForElements(
			itemId,
			continuationPoint,
			dwMaxElementsReturned,
			(int)dwFilter,
			nameFilter,
			revisedContinuationPoint);

		// allocate arrays to return.
		dwCount = elements->Count;
		OpcProxy_AllocArrayToReturn(pBrowseElements, dwCount, OPCBROWSEELEMENT);

		// copy results.
		HRESULT hResult = S_OK;

		for (DWORD ii = 0; ii < dwCount; ii++)
		{
			OPCBROWSEELEMENT* pElement = &(pBrowseElements[ii]);
			pElement->szName = (LPWSTR)Marshal::StringToCoTaskMemUni(elements[ii]->BrowseName).ToPointer();
			pElement->szItemID = (LPWSTR)Marshal::StringToCoTaskMemUni(elements[ii]->ItemId).ToPointer();
			
			pElement->dwFlagValue = 0;	

			if (elements[ii]->IsItem)
			{
				pElement->dwFlagValue |= OPC_BROWSE_ISITEM;
			}

			if (elements[ii]->HasChildren)
			{
				pElement->dwFlagValue |= OPC_BROWSE_HASCHILDREN;
			}
		}

		// fetch any requested properties.
		if (dwPropertyCount > 0 || bReturnAllProperties)
		{
			array<int>^ propertyIds = gcnew array<int>(dwPropertyCount);

			for (DWORD ii = 0; ii < dwPropertyCount; ii++)
			{
				propertyIds[ii] = pdwPropertyIDs[ii];
			}

			array<ComDaReadPropertiesRequest^>^ requests = gcnew array<ComDaReadPropertiesRequest^>(dwCount);

			for (int ii = 0; ii < elements->Count; ii++)
			{
				requests[ii] = gcnew ComDaReadPropertiesRequest();
				requests[ii]->ItemId = elements[ii]->ItemId;
			}

			// get properties.
			IList<DaProperty^>^ descriptions = server->GetProperties(requests, propertyIds);

			// copy results.
			HRESULT hResult = S_OK;

			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				OPCITEMPROPERTIES* pItemResult = &(pBrowseElements[ii].ItemProperties);
				pItemResult->hrErrorID = requests[ii]->Error;

				if (requests[ii]->Error < 0)
				{
					hResult = S_FALSE;
					continue;
				}
				
				COpcUaProxyUtils::MarshalProperties(
					*pItemResult,
					propertyIds,
					bReturnPropertyValues != 0,
					descriptions,
					requests[ii]->Values);

				// propagate error notice.
				if (pItemResult->hrErrorID == S_FALSE)
				{
					hResult = S_FALSE;
				}
			}		
		}

		// return continuation point.
		*pszContinuationPoint = (LPWSTR)Marshal::StringToCoTaskMemUni(revisedContinuationPoint).ToPointer();


		// return result.
		*pdwCount = dwCount;
		*ppBrowseElements = pBrowseElements;

		return hResult;
	}
	catch (Exception^ e)
	{
		// clean up on error.
		if (pBrowseElements != NULL)
		{
			for (DWORD ii = 0; ii < dwCount; ii++)
			{
				CoTaskMemFree(pBrowseElements[ii].szName);
				CoTaskMemFree(pBrowseElements[ii].szItemID);
				COpcUaProxyUtils::FreeOPCITEMPROPERTIES(pBrowseElements[ii].ItemProperties);
			}
		}

		CoTaskMemFree(pBrowseElements);

		return Marshal::GetHRForException(e);
	}

	return S_OK;
}
