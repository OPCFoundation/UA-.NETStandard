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

#ifndef _COpcConnectionPoint_H_
#define _COpcConnectionPoint_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "ocidl.h"

#include "OpcDefs.h"
#include "COpcComObject.h"
#include "COpcCriticalSection.h"

class COpcCPContainer;

//==============================================================================
// CLASS:   COpcConnectionPoint
// PURPOSE: Implements the IConnectionPoint interface.
// NOTES:

class OPCUTILS_API COpcConnectionPoint
: 
    public COpcComObject,
    public COpcSynchObject,
    public IConnectionPoint
{
    OPC_BEGIN_INTERFACE_TABLE(COpcConnectionPoint)
        OPC_INTERFACE_ENTRY(IConnectionPoint)
    OPC_END_INTERFACE_TABLE()

    OPC_CLASS_NEW_DELETE()

public:

    //==========================================================================
    // Operators

    // Constructor
    COpcConnectionPoint();

    // Constructor
    COpcConnectionPoint(const IID& tIid, COpcCPContainer* pContainer);

    // Destructor 
    ~COpcConnectionPoint();

    //==========================================================================
    // IConnectionPoint

    // GetConnectionInterface
    STDMETHODIMP GetConnectionInterface(IID* pIID);

    // GetConnectionPointContainer
    STDMETHODIMP GetConnectionPointContainer(IConnectionPointContainer** ppCPC);

    // Advise
    STDMETHODIMP Advise(IUnknown* pUnkSink, DWORD* pdwCookie);

    // Unadvise
    STDMETHODIMP Unadvise(DWORD dwCookie);

    // EnumConnections
    STDMETHODIMP EnumConnections(IEnumConnections** ppEnum);

    //==========================================================================
    // Public Methods

    // GetCallback
    IUnknown* GetCallback() { return m_ipCallback; }

    // GetInterface
    const IID& GetInterface() { return m_tInterface; }

    // Delete
    bool Delete();

    // IsConnected
    bool IsConnected() { return (m_dwCookie != NULL); }
    
private:

    //==========================================================================
    // Private Members

    IID              m_tInterface;
    COpcCPContainer* m_pContainer;
    IUnknown*        m_ipCallback;
    DWORD            m_dwCookie;
    bool             m_bFetched;
};

//==============================================================================
// FUNCTION: OpcConnect
// PURPOSE:  Establishes a connection to the server.

OPCUTILS_API HRESULT OpcConnect(
    IUnknown* ipSource, 
    IUnknown* ipSink, 
    REFIID    riid, 
    DWORD*    pdwConnection);

//==============================================================================
// FUNCTION: OpcDisconnect
// PURPOSE:  Closes a connection to the server.

OPCUTILS_API HRESULT OpcDisconnect(
    IUnknown* ipSource, 
    REFIID    riid, 
    DWORD     dwConnection);

#endif // _COpcConnectionPoint_H_
