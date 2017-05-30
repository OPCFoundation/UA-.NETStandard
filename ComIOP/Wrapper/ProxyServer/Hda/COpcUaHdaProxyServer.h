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

#ifndef _COpcUaHdaProxyServer_H_
#define _COpcUaHdaProxyServer_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcUaComProxyServer.h"

using namespace Opc::Ua;
using namespace Opc::Ua::Com::Server;

class COpcUaHdaProxyServer :
    public COpcComObject,
    public COpcCPContainer,
    public IOPCCommon,
    public IOPCHDA_Server,
    public IOPCHDA_SyncRead,
    public IOPCHDA_SyncUpdate,
	public IOPCHDA_SyncAnnotations,
    public IOPCHDA_AsyncRead,
    public IOPCHDA_AsyncUpdate,
    public IOPCHDA_AsyncAnnotations,
    public IOPCHDA_Playback,
	public COpcSynchObject
{
    OPC_CLASS_NEW_DELETE()

    OPC_BEGIN_INTERFACE_TABLE(COpcUaHdaProxyServer)
        OPC_INTERFACE_ENTRY(IOPCCommon)
        OPC_INTERFACE_ENTRY(IConnectionPointContainer)
        OPC_INTERFACE_ENTRY(IOPCHDA_Server)
        OPC_INTERFACE_ENTRY(IOPCHDA_SyncRead)
        OPC_INTERFACE_ENTRY(IOPCHDA_SyncUpdate)
        OPC_INTERFACE_ENTRY(IOPCHDA_SyncAnnotations)
        OPC_INTERFACE_ENTRY(IOPCHDA_AsyncRead)
        OPC_INTERFACE_ENTRY(IOPCHDA_AsyncUpdate)
        OPC_INTERFACE_ENTRY(IOPCHDA_AsyncAnnotations)
        //OPC_INTERFACE_ENTRY(IOPCHDA_Playback)
    OPC_END_INTERFACE_TABLE()

public:

    //=========================================================================
    // Operators

    // Constructor
    COpcUaHdaProxyServer();

    // Destructor 
    ~COpcUaHdaProxyServer();

    //=========================================================================
    // Public Methods

    // FinalConstruct
    virtual HRESULT FinalConstruct();

    // FinalRelease
    virtual bool FinalRelease();
	
	// OnAdvise
	virtual void OnAdvise(REFIID riid, DWORD dwCookie);

	// OnUnadvise
	virtual void OnUnadvise(REFIID riid, DWORD dwCookie);

	//==========================================================================
    // IOPCCommon

    // SetLocaleID
    STDMETHODIMP SetLocaleID(LCID dwLcid);

    // GetLocaleID
    STDMETHODIMP GetLocaleID(LCID *pdwLcid);

    // QueryAvailableLocaleIDs
    STDMETHODIMP QueryAvailableLocaleIDs(DWORD* pdwCount, LCID** pdwLcid);

    // GetErrorString
    STDMETHODIMP GetErrorString(HRESULT dwError, LPWSTR* ppString);

    // SetClientName
    STDMETHODIMP SetClientName(LPCWSTR szName);

    //=========================================================================
    // IOPCHDA_Server

	STDMETHODIMP GetItemAttributes( 
		DWORD*    pdwCount,
		DWORD**   ppdwAttrID,
		LPWSTR**  ppszAttrName,
		LPWSTR**  ppszAttrDesc,
		VARTYPE** ppvtAttrDataType
	);

	STDMETHODIMP GetAggregates(
		DWORD*   pdwCount,
		DWORD**  ppdwAggrID,
		LPWSTR** ppszAggrName,
		LPWSTR** ppszAggrDesc
	);

	STDMETHODIMP GetHistorianStatus(
		OPCHDA_SERVERSTATUS* pwStatus,
		FILETIME**           pftCurrentTime,
		FILETIME**           pftStartTime,
		WORD*                pwMajorVersion,
		WORD*                pwMinorVersion,
		WORD*                pwBuildNumber,
		DWORD*               pdwMaxReturnValues,
		LPWSTR*              ppszStatusString,
		LPWSTR*              ppszVendorInfo
	);

	STDMETHODIMP GetItemHandles(
		DWORD		dwCount,
		LPWSTR*     pszItemID,
		OPCHANDLE*  phClient,
		OPCHANDLE** pphServer,
		HRESULT**   ppErrors
	);

	STDMETHODIMP ReleaseItemHandles(
		DWORD		dwCount,
		OPCHANDLE* phServer,
		HRESULT**  ppErrors
	);

	STDMETHODIMP ValidateItemIDs(
		DWORD	   dwCount,
		LPWSTR*   pszItemID,
		HRESULT** ppErrors
	);

	STDMETHODIMP CreateBrowse(
		DWORD			      dwCount,
		DWORD*                pdwAttrID,
		OPCHDA_OPERATORCODES* pOperator,
		VARIANT*              vFilter,
		IOPCHDA_Browser**     pphBrowser,
		HRESULT**             ppErrors
	);

    //=========================================================================
    // IOPCHDA_SyncRead

	STDMETHODIMP ReadRaw(
		OPCHDA_TIME*  htStartTime,
		OPCHDA_TIME*  htEndTime,
		DWORD	      dwNumValues,
		BOOL	      bBounds,
		DWORD	      dwNumItems,
		OPCHANDLE*    phServer, 
		OPCHDA_ITEM** ppItemValues,
		HRESULT**     ppErrors
	);

	STDMETHODIMP ReadProcessed(
		OPCHDA_TIME*  htStartTime,
		OPCHDA_TIME*  htEndTime,
		FILETIME      ftResampleInterval,
		DWORD         dwNumItems,
		OPCHANDLE*    phServer, 
		DWORD*        haAggregate, 
		OPCHDA_ITEM** ppItemValues,
		HRESULT**     ppErrors
	);

	STDMETHODIMP ReadAtTime(
		DWORD         dwNumTimeStamps,
		FILETIME*     ftTimeStamps,
		DWORD         dwNumItems,
		OPCHANDLE*    phServer, 
		OPCHDA_ITEM** ppItemValues,
		HRESULT**     ppErrors
	);

	STDMETHODIMP ReadModified(
		OPCHDA_TIME*          htStartTime,
		OPCHDA_TIME*          htEndTime,
		DWORD                 dwNumValues,
		DWORD                 dwNumItems,
		OPCHANDLE*            phServer, 
		OPCHDA_MODIFIEDITEM** ppItemValues,
		HRESULT**             ppErrors
	);

	STDMETHODIMP ReadAttribute(
		OPCHDA_TIME*       htStartTime,
		OPCHDA_TIME*       htEndTime,
		OPCHANDLE          hServer, 
		DWORD              dwNumAttributes,
		DWORD*             pdwAttributeIDs, 
		OPCHDA_ATTRIBUTE** ppAttributeValues,
		HRESULT**          ppErrors
	);

    //=========================================================================
    // IOPCHDA_SyncUpdate

	STDMETHODIMP QueryCapabilities(
		 OPCHDA_UPDATECAPABILITIES* pCapabilities
	);

	STDMETHODIMP Insert(
		DWORD      dwNumItems, 
		OPCHANDLE* phServer, 
		FILETIME*  ftTimeStamps,
		VARIANT*   vDataValues,
		DWORD*     pdwQualities,
		HRESULT**  ppErrors
	);

	STDMETHODIMP Replace(
		DWORD      dwNumItems, 
		OPCHANDLE* phServer, 
		FILETIME*  ftTimeStamps,
		VARIANT*   vDataValues,
		DWORD*     pdwQualities,
		HRESULT**  ppErrors
	);

	STDMETHODIMP InsertReplace(
		DWORD      dwNumItems, 
		OPCHANDLE* phServer, 
		FILETIME*  ftTimeStamps,
		VARIANT*   vDataValues,
		DWORD*     pdwQualities,
		HRESULT**  ppErrors
	);

	STDMETHODIMP DeleteRaw(
		OPCHDA_TIME* htStartTime,
		OPCHDA_TIME* htEndTime,
		DWORD        dwNumItems,
		OPCHANDLE*   phServer,
		HRESULT**    ppErrors
	);

	STDMETHODIMP DeleteAtTime(
		DWORD      dwNumItems,
		OPCHANDLE* phServer,
		FILETIME*  ftTimeStamps,
		HRESULT**  ppErrors
	);

    //=========================================================================
    // IOPCHDA_SyncAnnotations

	STDMETHODIMP QueryCapabilities(
		 OPCHDA_ANNOTATIONCAPABILITIES* pCapabilities
	);

	STDMETHODIMP Read(
		OPCHDA_TIME*        htStartTime,
		OPCHDA_TIME*        htEndTime,
		DWORD	            dwNumItems,
		OPCHANDLE*          phServer, 
		OPCHDA_ANNOTATION** ppAnnotationValues,
		HRESULT**           ppErrors
	);

	STDMETHODIMP Insert(
		DWORD              dwNumItems, 
		OPCHANDLE*         phServer, 
		FILETIME*          ftTimeStamps,
		OPCHDA_ANNOTATION* pAnnotationValues,
		HRESULT**          ppErrors
	);

    //=========================================================================
    // IOPCHDA_AsyncRead

	STDMETHODIMP ReadRaw(
		DWORD        dwTransactionID,
		OPCHDA_TIME* htStartTime,
		OPCHDA_TIME* htEndTime,
		DWORD        dwNumValues,
		BOOL         bBounds,
		DWORD        dwNumItems,
		OPCHANDLE*   phServer,
		DWORD*       pdwCancelID,
		HRESULT**    ppErrors
	);

	STDMETHODIMP AdviseRaw(
		DWORD        dwTransactionID,
		OPCHDA_TIME* htStartTime,
		FILETIME     ftUpdateInterval,
		DWORD        dwNumItems,
		OPCHANDLE*   phServer,
		DWORD*       pdwCancelID,
		HRESULT**    ppErrors
	);

	STDMETHODIMP ReadProcessed(
		DWORD        dwTransactionID,
		OPCHDA_TIME* htStartTime,
		OPCHDA_TIME* htEndTime,
		FILETIME     ftResampleInterval,
		DWORD        dwNumItems,
		OPCHANDLE*   phServer,
		DWORD*       haAggregate,
		DWORD*       pdwCancelID,
		HRESULT**    ppErrors
	);

	STDMETHODIMP AdviseProcessed(
		DWORD        dwTransactionID,
		OPCHDA_TIME* htStartTime,
		FILETIME     ftResampleInterval,
		DWORD        dwNumItems,
		OPCHANDLE*   phServer,
		DWORD*       haAggregate,
		DWORD        dwNumIntervals,
		DWORD*       pdwCancelID,
		HRESULT**    ppErrors
	);

	STDMETHODIMP ReadAtTime(
		DWORD      dwTransactionID,
		DWORD      dwNumTimeStamps,
		FILETIME*  ftTimeStamps,
		DWORD      dwNumItems,
		OPCHANDLE* phServer, 
		DWORD*     pdwCancelID,
		HRESULT**  ppErrors
	);

	STDMETHODIMP ReadModified(
		DWORD        dwTransactionID,
		OPCHDA_TIME* htStartTime,
		OPCHDA_TIME* htEndTime,
		DWORD        dwNumValues,
		DWORD        dwNumItems,
		OPCHANDLE*   phServer, 
		DWORD*       pdwCancelID,
		HRESULT**    ppErrors
	);

	STDMETHODIMP ReadAttribute(
		DWORD        dwTransactionID,
		OPCHDA_TIME* htStartTime,
		OPCHDA_TIME* htEndTime,
		OPCHANDLE    hServer, 
		DWORD        dwNumAttributes,
		DWORD*       dwAttributeIDs, 
		DWORD*       pdwCancelID,
		HRESULT**    ppErrors
	);

	STDMETHODIMP Cancel(
		 DWORD dwCancelID
	);

	//=========================================================================
    // IOPCHDA_AsyncUpdate

	STDMETHODIMP Insert(
		DWORD      dwTransactionID,
		DWORD      dwNumItems,
		OPCHANDLE* phServer,
		FILETIME*  ftTimeStamps,
		VARIANT*   vDataValues,
		DWORD*     pdwQualities,
		DWORD*     pdwCancelID,
		HRESULT**  ppErrors
	);

	STDMETHODIMP Replace(
		DWORD      dwTransactionID,
		DWORD      dwNumItems,
		OPCHANDLE* phServer,
		FILETIME*  ftTimeStamps,
		VARIANT*   vDataValues,
		DWORD*     pdwQualities,
		DWORD*     pdwCancelID,
		HRESULT**  ppErrors
	);

	STDMETHODIMP InsertReplace(
		DWORD      dwTransactionID,
		DWORD      dwNumItems,
		OPCHANDLE* phServer,
		FILETIME*  ftTimeStamps,
		VARIANT*   vDataValues,
		DWORD*     pdwQualities,
		DWORD*     pdwCancelID,
		HRESULT**  ppErrors
	);

	STDMETHODIMP DeleteRaw(
		DWORD        dwTransactionID,
		OPCHDA_TIME* htStartTime,
		OPCHDA_TIME* htEndTime,
		DWORD        dwNumItems,
		OPCHANDLE*   phServer,
		DWORD*       pdwCancelID,
		HRESULT**    ppErrors
	);

	STDMETHODIMP DeleteAtTime(
		DWORD      dwTransactionID,
		DWORD      dwNumItems,
		OPCHANDLE* phServer,
		FILETIME*  ftTimeStamps,
		DWORD*     pdwCancelID,
		HRESULT**  ppErrors
	);

	//=========================================================================
    // IOPCHDA_AsyncAnnotations

	STDMETHODIMP Read(
		DWORD        dwTransactionID,
		OPCHDA_TIME* htStartTime,
		OPCHDA_TIME* htEndTime,
		DWORD        dwNumItems,
		OPCHANDLE*   phServer, 
		DWORD*       pdwCancelID,
		HRESULT**    ppErrors
	);

	STDMETHODIMP Insert(
		DWORD              dwTransactionID,
		DWORD              dwNumItems, 
		OPCHANDLE*         phServer, 
		FILETIME*          ftTimeStamps,
		OPCHDA_ANNOTATION* pAnnotationValues,
		DWORD*             pdwCancelID,
		HRESULT**          ppErrors
	);

	//=========================================================================
    // IOPCHDA_Playback

	STDMETHODIMP ReadRawWithUpdate(
		DWORD        dwTransactionID,
		OPCHDA_TIME* htStartTime,
		OPCHDA_TIME* htEndTime,
		DWORD        dwNumValues,
		FILETIME     ftUpdateDuration,
		FILETIME     ftUpdateInterval,
		DWORD        dwNumItems,
		OPCHANDLE*   phServer,
		DWORD*       pdwCancelID,
		HRESULT**    ppErrors
	);

	STDMETHODIMP ReadProcessedWithUpdate(
		DWORD        dwTransactionID,
		OPCHDA_TIME* htStartTime,
		OPCHDA_TIME* htEndTime,
		FILETIME     ftResampleInterval,
		DWORD        dwNumIntervals,
		FILETIME     ftUpdateInterval,
		DWORD        dwNumItems,
		OPCHANDLE*   phServer,
		DWORD*       haAggregate,
		DWORD*       pdwCancelID,
		HRESULT**    ppErrors
	);

private:
        
	// Returns the wrapped server instance.
	ComHdaProxy^ GetInnerServer();

	// UpdateRaw
	HRESULT UpdateRaw(
		PerformUpdateType        updateType,
		DWORD                    dwNumItems, 
		OPCHANDLE*               phServer, 
		::FILETIME*              ftTimeStamps,
		VARIANT*                 vDataValues,
		DWORD*                   pdwQualities,
		HRESULT**                ppErrors);

    // UpdateRaw
    HRESULT UpdateRaw(
		PerformUpdateType        updateType,
	    DWORD                    dwTransactionID,
	    DWORD                    dwNumItems,
	    OPCHANDLE*               phServer,
	    ::FILETIME*              ftTimeStamps,
	    VARIANT*                 vDataValues,
	    DWORD*                   pdwQualities,
	    DWORD*                   pdwCancelID,
	    HRESULT**                ppErrors);

    //==========================================================================
    // Private Members

	void* m_pInnerServer;
	LPWSTR m_pClientName;
    ::FILETIME m_ftStartTime;
	WORD m_wMajorVersion;
	WORD m_wMinorVersion;
	WORD m_wBuildNumber;
	LPWSTR m_szVendorInfo;
};

#endif // _COpcUaHdaProxyServer_H_
