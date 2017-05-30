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

#ifndef _COpcUaDaProxyGroup_H_
#define _COpcUaDaProxyGroup_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

class COpcUaDaProxyServer;
using namespace Opc::Ua;
using namespace Opc::Ua::Com;
using namespace Opc::Ua::Com::Server;

//============================================================================
// CLASS:   COpcUaDaProxyGroup
// PURPOSE: A class that implements the IOPCGroup interface.

class COpcUaDaProxyGroup :
    public COpcComObject,
    public COpcCPContainer,
    public IOPCItemMgt,

#ifdef OPCUA_NO_DA3_SUPPORT
    public IOPCGroupStateMgt,
    public IOPCSyncIO,
    public IOPCAsyncIO2,
#else
    public IOPCItemDeadbandMgt,
    public IOPCItemSamplingMgt,
    public IOPCSyncIO2,
    public IOPCAsyncIO3,
    public IOPCGroupStateMgt2,
#endif
    
	public COpcSynchObject
{
    OPC_CLASS_NEW_DELETE()

    OPC_BEGIN_INTERFACE_TABLE(COpcUaDaProxyGroup)
        OPC_INTERFACE_ENTRY(IConnectionPointContainer)
        OPC_INTERFACE_ENTRY(IOPCItemMgt)
        OPC_INTERFACE_ENTRY(IOPCSyncIO)
        OPC_INTERFACE_ENTRY(IOPCAsyncIO2)
        OPC_INTERFACE_ENTRY(IOPCGroupStateMgt)

#ifndef OPCUA_NO_DA3_SUPPORT
        OPC_INTERFACE_ENTRY(IOPCItemDeadbandMgt)
        OPC_INTERFACE_ENTRY(IOPCItemSamplingMgt)
        OPC_INTERFACE_ENTRY(IOPCSyncIO2)
        OPC_INTERFACE_ENTRY(IOPCAsyncIO3)
        OPC_INTERFACE_ENTRY(IOPCGroupStateMgt2)
#endif
    
	OPC_END_INTERFACE_TABLE()

public:

    //=========================================================================
    // Operators

    // Constructor
    COpcUaDaProxyGroup();
    COpcUaDaProxyGroup(COpcUaDaProxyServer* pServer, ComDaGroup^ group);

    // Destructor 
    ~COpcUaDaProxyGroup();

    //=========================================================================
    // Public Methods

	// Delete
	void Delete();

	// OnAdvise
	virtual void OnAdvise(REFIID riid, DWORD dwCookie);

	// OnUnadvise
	virtual void OnUnadvise(REFIID riid, DWORD dwCookie);

	//=========================================================================
    // IOPCGroupStateMgt

    // GetState
    STDMETHODIMP  GetState(
        DWORD     * pUpdateRate, 
        BOOL      * pActive, 
        LPWSTR    * ppName,
        LONG      * pTimeBias,
        FLOAT     * pPercentDeadband,
        DWORD     * pLCID,
        OPCHANDLE * phClientGroup,
        OPCHANDLE * phServerGroup
    );

    // SetState
    STDMETHODIMP  SetState( 
        DWORD     * pRequestedUpdateRate, 
        DWORD     * pRevisedUpdateRate, 
        BOOL      * pActive, 
        LONG      * pTimeBias,
        FLOAT     * pPercentDeadband,
        DWORD     * pLCID,
        OPCHANDLE * phClientGroup
    );

    // SetName
    STDMETHODIMP  SetName( 
        LPCWSTR szName
    );

    // CloneGroup
    STDMETHODIMP  CloneGroup(
        LPCWSTR     szName,
        REFIID      riid,
        LPUNKNOWN * ppUnk
    );
    
	//=========================================================================
    // IOPCGroupStateMgt2

	// SetKeepAlive
    STDMETHODIMP SetKeepAlive( 
        DWORD   dwKeepAliveTime,
        DWORD * pdwRevisedKeepAliveTime 
    );

	// GetKeepAlive
    STDMETHODIMP GetKeepAlive( 
        DWORD * pdwKeepAliveTime 
    );

    //=========================================================================
    // IOPCItemMgt

    // AddItems
    STDMETHODIMP  AddItems( 
        DWORD            dwCount,
        OPCITEMDEF     * pItemArray,
        OPCITEMRESULT ** ppAddResults,
        HRESULT       ** ppErrors
    );

    // ValidateItems
    STDMETHODIMP  ValidateItems( 
        DWORD             dwCount,
        OPCITEMDEF      * pItemArray,
        BOOL              bBlobUpdate,
        OPCITEMRESULT  ** ppValidationResults,
        HRESULT        ** ppErrors
    );

    // RemoveItems
    STDMETHODIMP  RemoveItems( 
        DWORD        dwCount,
        OPCHANDLE  * phServer,
        HRESULT   ** ppErrors
    );

    // SetActiveState
    STDMETHODIMP  SetActiveState(
        DWORD        dwCount,
        OPCHANDLE  * phServer,
        BOOL         bActive, 
        HRESULT   ** ppErrors
    );

    // SetClientHandles
    STDMETHODIMP  SetClientHandles(
        DWORD        dwCount,
        OPCHANDLE  * phServer,
        OPCHANDLE  * phClient,
        HRESULT   ** ppErrors
    );

    // SetDatatypes
    STDMETHODIMP  SetDatatypes(
        DWORD        dwCount,
        OPCHANDLE  * phServer,
        VARTYPE    * pRequestedDatatypes,
        HRESULT   ** ppErrors
    );

    // CreateEnumerator
    STDMETHODIMP  CreateEnumerator(
        REFIID      riid,
        LPUNKNOWN * ppUnk
    );

    //=========================================================================
    // IOPCAsyncIO2

    // Read
    STDMETHODIMP  Read(
        DWORD           dwCount,
        OPCHANDLE     * phServer,
        DWORD           dwTransactionID,
        DWORD         * pdwCancelID,
        HRESULT      ** ppErrors
    );

    // Write
    STDMETHODIMP  Write(
        DWORD           dwCount, 
        OPCHANDLE     * phServer,
        VARIANT       * pItemValues, 
        DWORD           dwTransactionID,
        DWORD         * pdwCancelID,
        HRESULT      ** ppErrors
    );

    // Refresh2
    STDMETHODIMP  Refresh2(
        OPCDATASOURCE   dwSource,
        DWORD           dwTransactionID,
        DWORD         * pdwCancelID
    );

    // Cancel2
    STDMETHODIMP  Cancel2(DWORD dwCancelID);

    // SetEnable
    STDMETHODIMP  SetEnable(BOOL bEnable);

    // GetEnable
    STDMETHODIMP  GetEnable(BOOL* pbEnable);

    //=========================================================================
    // IOPCItemDeadbandMgt

    // SetItemDeadband
    STDMETHODIMP SetItemDeadband( 
        DWORD 	    dwCount,
        OPCHANDLE * phServer,
        FLOAT     * pPercentDeadband,
        HRESULT  ** ppErrors
    );

    // GetItemDeadband
    STDMETHODIMP GetItemDeadband( 
        DWORD 	    dwCount,
        OPCHANDLE * phServer,
        FLOAT    ** ppPercentDeadband,
        HRESULT  ** ppErrors
    );

    // ClearItemDeadband
    STDMETHODIMP ClearItemDeadband(
        DWORD       dwCount,
        OPCHANDLE * phServer,
        HRESULT  ** ppErrors
    );

    //=========================================================================
    // IOPCItemSamplingMgt

    // SetItemSamplingRate
    STDMETHODIMP SetItemSamplingRate(
        DWORD 	    dwCount,
        OPCHANDLE * phServer,
        DWORD     * pdwRequestedSamplingRate,
        DWORD    ** ppdwRevisedSamplingRate,
        HRESULT  ** ppErrors
    );

    // GetItemSamplingRate
    STDMETHODIMP GetItemSamplingRate(
        DWORD 	    dwCount,
        OPCHANDLE * phServer,
        DWORD    ** ppdwSamplingRate,
        HRESULT  ** ppErrors
    );

    // ClearItemSamplingRate
    STDMETHODIMP ClearItemSamplingRate(
        DWORD 	    dwCount,
        OPCHANDLE * phServer,
        HRESULT  ** ppErrors
    );

    // SetItemBufferEnable
    STDMETHODIMP SetItemBufferEnable(
        DWORD       dwCount, 
        OPCHANDLE * phServer, 
        BOOL      * pbEnable,
        HRESULT  ** ppErrors
    );

    // GetItemBufferEnable
    STDMETHODIMP GetItemBufferEnable(
        DWORD       dwCount, 
        OPCHANDLE * phServer, 
        BOOL     ** ppbEnable,
        HRESULT  ** ppErrors
    );

    //=========================================================================
    // IOPCSyncIO2

    // Read
    STDMETHODIMP  Read(
        OPCDATASOURCE   dwSource,
        DWORD           dwCount, 
        OPCHANDLE     * phServer, 
        OPCITEMSTATE ** ppItemValues,
        HRESULT      ** ppErrors
    );

    // Write
    STDMETHODIMP  Write(
        DWORD        dwCount, 
        OPCHANDLE  * phServer, 
        VARIANT    * pItemValues, 
        HRESULT   ** ppErrors
    );

    // ReadMaxAge
    STDMETHODIMP ReadMaxAge(
        DWORD       dwCount, 
        OPCHANDLE * phServer, 
        DWORD     * pdwMaxAge,
        VARIANT  ** ppvValues,
        WORD     ** ppwQualities,
        FILETIME ** ppftTimeStamps,
        HRESULT  ** ppErrors
    );

    // WriteVQT
    STDMETHODIMP WriteVQT(
        DWORD         dwCount, 
        OPCHANDLE  *  phServer, 
        OPCITEMVQT *  pItemVQT,
        HRESULT    ** ppErrors
    );
    
	//=========================================================================
    // IOPCAsyncIO3

    STDMETHODIMP ReadMaxAge(
        DWORD       dwCount, 
        OPCHANDLE * phServer,
        DWORD     * pdwMaxAge,
        DWORD       dwTransactionID,
        DWORD     * pdwCancelID,
        HRESULT  ** ppErrors
    );

    STDMETHODIMP WriteVQT(
        DWORD        dwCount, 
        OPCHANDLE  * phServer,
        OPCITEMVQT * pItemVQT,
        DWORD        dwTransactionID,
        DWORD      * pdwCancelID,
        HRESULT   ** ppErrors
    );

    STDMETHODIMP RefreshMaxAge(
        DWORD   dwMaxAge,
        DWORD   dwTransactionID,
        DWORD * pdwCancelID
    );

private:

	// GetInnerGroup
	ComDaGroup^ GetInnerGroup();

	COpcUaDaProxyServer* m_pServer;
	void* m_pInnerGroup;
};

#endif // _COpcUaDaProxyGroup_H_
