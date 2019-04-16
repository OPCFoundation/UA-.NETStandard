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

#ifndef _COpcCPContainer_H_
#define _COpcCPContainer_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcDefs.h"
#include "COpcList.h"
#include "COpcConnectionPoint.h"

//==============================================================================
// CLASS:   COpcConnectionPointList
// PURPOSE: Stores a list of connection points.

typedef COpcList<COpcConnectionPoint*> COpcConnectionPointList;
template class OPCUTILS_API COpcList<COpcConnectionPoint*>;

//==============================================================================
// CLASS:   COpcCPContainer
// PURPOSE: Implements the IConnectionPointContainer interface.
// NOTES:

class OPCUTILS_API COpcCPContainer : public IConnectionPointContainer
{
public:

    //==========================================================================
    // Operators

    // Constructor
    COpcCPContainer();

    // Destructor 
    ~COpcCPContainer();

    //==========================================================================
    // IConnectionPointContainer

    // EnumConnectionPoints
    STDMETHODIMP EnumConnectionPoints(IEnumConnectionPoints** ppEnum);

    // FindConnectionPoint
    STDMETHODIMP FindConnectionPoint(REFIID riid, IConnectionPoint** ppCP);

    //==========================================================================
    // Public Methods

	// OnAdvise
	virtual void OnAdvise(REFIID riid, DWORD dwCookie) {}

	// OnUnadvise
	virtual void OnUnadvise(REFIID riid, DWORD dwCookie) {}

protected:

    //==========================================================================
    // Protected Methods

    // RegisterInterface
    void RegisterInterface(const IID& tInterface);

    // UnregisterInterface
    void UnregisterInterface(const IID& tInterface);

    // GetCallback
    HRESULT GetCallback(const IID& tInterface, IUnknown** ippCallback);

    // IsConnected
    bool IsConnected(const IID& tInterface);

    //==========================================================================
    // Protected Members

    COpcConnectionPointList m_cCPs;
};

#endif // _COpcCPContainer_H_
