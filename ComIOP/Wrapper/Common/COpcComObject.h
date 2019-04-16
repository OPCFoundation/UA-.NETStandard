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

#ifndef _COpcComObject_H
#define _COpcComObject_H

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcDefs.h"

//==============================================================================
// TITLE:   COpcComObject.h
// PURPOSE: Defines the base class for COM servers
// NOTES:

class OPCUTILS_API COpcComObject
{
    OPC_CLASS_NEW_DELETE_ARRAY();

public:

    //==========================================================================
    // Operators

    // Constructor
    COpcComObject()
    {
        // set the reference count to one - calling function must release.
        m_ulRefs = 1;
    }

    // Destructor
    virtual ~COpcComObject()
    {
        OPC_ASSERT(m_ulRefs == 0);
    }

    //==========================================================================
    // Public Methods

    // InternalAddRef
    // 
    // Description
    //
    // Adds a reference to the COM server.
    //
    // Return Codes
    //
    // The current number of references.

    virtual ULONG InternalAddRef() 
    { 
        return InterlockedIncrement((LONG*)&m_ulRefs); 
    } 

    // InternalRelease
    // 
    // Description
    //
    // Removes a reference to the COM server. If the reference reaches zero
    // it calls FinalRelease() and deletes the instance.
    //
    // Return Codes
    //
    // The current number of references.

    virtual ULONG InternalRelease() 
    { 
        ULONG ulRefs = InterlockedDecrement((LONG*)&m_ulRefs); 

        if (ulRefs == 0) 
        { 
            if (FinalRelease())
			{
				delete this;
			}

			return 0; 
        } 

        return ulRefs; 
    } 

    // InternalQueryInterface
    // 
    // Description
    //
    // An pure virtual method that the COM server's interface map implements.
    // The inteface map macro's add a stub QueryInterface implmentation
    // that call this function. It calls AddRef() so the client must call
    // Release() on the returned interface.
    //
    // Parameters;
    //
    // iid The desired interface IID.
    // ppInterface The returned interface.
    //
    // Return Codes
    //
    // S_OK if the interface is supported
    // E_NOINTERFACE if not.

    virtual HRESULT InternalQueryInterface(REFIID iid, LPVOID* ppInterface) = 0;

    // FinalConstruct
    // 
    // Description
    //
    // A function called by the class factory after creating the object.
    // The COM server does any server specific initialization.
    //
    // Return Codes
    //
    // S_OK if intialization succeeded.

    virtual HRESULT FinalConstruct() 
    { 
        return S_OK;
    } 

    // FinalRelease
    // 
    // Description
    //
    // A function called by release after the reference count drops to zero
    // before deleting the object. The COM server does any server specific 
    // uninitialization.
    //

    virtual bool FinalRelease() 
    { 
		// returning false would stop the caller from explicitly deleting the object.
		return true;
    } 

private: 

    //==========================================================================
    // Private Members

    ULONG m_ulRefs;
};

//==============================================================================
// MACRO:   OPC_BEGIN_INTERFACE_TABLE
// PURPOSE: Starts the COM server's interface table.
// NOTES:

#define OPC_BEGIN_INTERFACE_TABLE(xClass) \
private: \
\
const CLSID* m_pClsid; \
\
protected: \
\
REFCLSID GetCLSID() { return *m_pClsid; } \
\
public: \
\
static HRESULT __stdcall CreateInstance(IUnknown** ippUnknown, const CLSID* pClsid) \
{ \
    if (ippUnknown == NULL) return E_POINTER; \
    *ippUnknown = NULL; \
\
    xClass* pObject = new xClass(); \
\
	pObject->m_pClsid = pClsid; \
\
    HRESULT hResult = pObject->FinalConstruct(); \
\
    if (FAILED(hResult)) \
    { \
       pObject->Release(); \
       return hResult; \
    } \
\
    hResult = pObject->QueryInterface(IID_IUnknown, (void**)ippUnknown); \
    pObject->Release(); \
    return hResult; \
} \
\
virtual HRESULT InternalQueryInterface(REFIID iid, LPVOID* ppInterface) \
{ \
    if (ppInterface == NULL) return E_POINTER; \
    *ppInterface = NULL; 

//==============================================================================
// MACRO:   OPC_INTERFACE_ENTRY
// PURPOSE: Adds an interface to the COM server's interface table.
// NOTES:

#define OPC_INTERFACE_ENTRY(xInterface) \
\
if (iid == __uuidof(xInterface) || iid == IID_IUnknown) \
{ \
    *ppInterface = (dynamic_cast<xInterface*>(this)); \
    AddRef(); \
    return S_OK; \
} 

//==============================================================================
// MACRO:   OPC_AGGREGATE_OBJECT
// PURPOSE: Adds an interface to the COM server's interface table.
// NOTES:

#define OPC_AGGREGATE_OBJECT(xObject) \
\
if (xObject != NULL) \
{ \
    return xObject->QueryInterface(iid, ppInterface); \
}

//==============================================================================
// MACRO:   OPC_END_INTERFACE_TABLE
// PURPOSE: Completes the COM server's interface table.
// NOTES:

#define OPC_END_INTERFACE_TABLE() \
return E_NOINTERFACE; } \
\
STDMETHODIMP QueryInterface(REFIID iid, LPVOID* ppInterface) {return InternalQueryInterface(iid, ppInterface);} \
STDMETHODIMP_(ULONG) AddRef() {return InternalAddRef();} \
STDMETHODIMP_(ULONG) Release() {return InternalRelease();} 

#define OPC_END_INTERFACE_TABLE_DEBUG() \
return E_NOINTERFACE; }

#endif // _COpcComObject_H
