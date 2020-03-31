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

#include "StdAfx.h"
#include "COpcCPContainer.h"
#include "COpcEnumCPs.h"

//==============================================================================
// COpcCPContainer

// Constructor
COpcCPContainer::COpcCPContainer()
{
}

// Destructor 
COpcCPContainer::~COpcCPContainer()
{
    // release the connection points.
    OPC_POS pos = m_cCPs.GetHeadPosition();

    while (pos != NULL)
    {
        m_cCPs.GetNext(pos)->Release();
    }
}

// RegisterInterface
void COpcCPContainer::RegisterInterface(const IID& tInterface)
{
    // constructor adds one reference.
    COpcConnectionPoint* pCP = new COpcConnectionPoint(tInterface, this);
    m_cCPs.AddTail(pCP);
}

// UnregisterInterface
void COpcCPContainer::UnregisterInterface(const IID& tInterface)
{
    OPC_POS pos = m_cCPs.GetHeadPosition();

    while (pos != NULL)
    {
        COpcConnectionPoint* pCP = m_cCPs[pos];

        if (pCP->GetInterface() == tInterface)
        {
            m_cCPs.RemoveAt(pos);
            pCP->Delete();
            break;
        }

        m_cCPs.GetNext(pos);
    }
}

// GetCallback
HRESULT COpcCPContainer::GetCallback(const IID& tInterface, IUnknown** ippCallback)
{
    COpcConnectionPoint* pCP = NULL;

    OPC_POS pos = m_cCPs.GetHeadPosition();

    while (pos != NULL)
    {
        pCP = m_cCPs.GetNext(pos);

        if (pCP->GetInterface() == tInterface)
        {
            IUnknown* ipUnknown = pCP->GetCallback();
            
            if (ipUnknown != NULL)
            {
                return ipUnknown->QueryInterface(tInterface, (void**)ippCallback);
            }
        }
    }

    return E_FAIL;
}

// IsConnected
bool COpcCPContainer::IsConnected(const IID& tInterface)
{
    COpcConnectionPoint* pCP = NULL;

    OPC_POS pos = m_cCPs.GetHeadPosition();

    while (pos != NULL)
    {
        pCP = m_cCPs.GetNext(pos);

        if (pCP->GetInterface() == tInterface)
        {
            return pCP->IsConnected();
        }
    }

    return false;
}

//==============================================================================
// IConnectionPointContainer

// EnumConnectionPoints
HRESULT COpcCPContainer::EnumConnectionPoints(IEnumConnectionPoints** ppEnum)
{
    // invalid arguments.
    if (ppEnum == NULL)
    {
        return E_POINTER;
    }

    // create enumeration object.
    COpcEnumCPs* pEnumCPs = new COpcEnumCPs(m_cCPs);

    if (pEnumCPs == NULL)
    {
        return E_OUTOFMEMORY;
    }

    // query for enumeration interface.
    HRESULT hResult = pEnumCPs->QueryInterface(IID_IEnumConnectionPoints, (void**)ppEnum);

    // release local reference.
    pEnumCPs->Release();

    return hResult;
}

// FindConnectionPoint
HRESULT COpcCPContainer::FindConnectionPoint(REFIID riid, IConnectionPoint** ppCP)
{
    // invalid arguments.
    if (ppCP == NULL)
    {
        return E_POINTER;
    }

    // search for connection point.
    OPC_POS pos = m_cCPs.GetHeadPosition();

    while (pos != NULL)
    {
        COpcConnectionPoint* pCP = m_cCPs.GetNext(pos);

        if (pCP->GetInterface() == riid)
        {
            return pCP->QueryInterface(IID_IConnectionPoint, (void**)ppCP);
        }
    }

    // connection point not found.
    return CONNECT_E_NOCONNECTION;
}
