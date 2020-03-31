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
#include "COpcEnumCPs.h"

// Constructor
COpcEnumCPs::COpcEnumCPs()
{
    Reset();
}

// Constructor
COpcEnumCPs::COpcEnumCPs(const COpcConnectionPointList& cCPs)
{
    // copy connection points.
    OPC_POS pos = cCPs.GetHeadPosition();

    while (pos != NULL)
    {
        COpcConnectionPoint* pCP = cCPs.GetNext(pos);

        m_cCPs.AddTail(pCP);
        pCP->AddRef();
    }      

    // set pointer to start of list.
    Reset();
}

// Destructor 
COpcEnumCPs::~COpcEnumCPs()
{
    // release connection points.
    OPC_POS pos = m_cCPs.GetHeadPosition();

    while (pos != NULL)
    {
        m_cCPs.GetNext(pos)->Release();
    }
}

//==============================================================================
// IEnumConnectionPoints

// Next
HRESULT COpcEnumCPs::Next(
    ULONG              cConnections,
    LPCONNECTIONPOINT* ppCP,
    ULONG*             pcFetched
)
{
    // invalid arguments - return error.
    if (pcFetched == NULL)
    {
        return E_INVALIDARG;
    }
        
    *pcFetched = 0;

    // trivial case - return nothing.
    if (cConnections == 0)
    {
        return S_OK;
    }
    
    // read connection points.
    for (ULONG ii = 0; ii < cConnections; ii++)
    {
        // end of list reached before count reached.
        if (m_pos == NULL)
        {
            *pcFetched = ii;
            return S_FALSE;
        }

        ppCP[ii] = m_cCPs.GetNext(m_pos);
        
        // client must release the reference.
        ppCP[ii]->AddRef();
    } 

    *pcFetched = ii;
    return S_OK;
}

// Skip
HRESULT COpcEnumCPs::Skip(ULONG cConnections)
{
    // skip connection points.
    OPC_POS pos = m_cCPs.GetHeadPosition();

    for (ULONG ii = 0; ii < cConnections; ii++)
    {
        // end of list reached before count reached.
        if (m_pos == NULL)
        {
            return S_FALSE;
        }

        m_cCPs.GetNext(m_pos);
    } 

    return S_OK;
}

// Reset
HRESULT COpcEnumCPs::Reset()
{
    m_pos = m_cCPs.GetHeadPosition();
    return S_OK;
}

// Clone
HRESULT COpcEnumCPs::Clone(IEnumConnectionPoints** ppEnum)
{
    // create a new enumeration object.
    COpcEnumCPs* ipEnum = new COpcEnumCPs();

    // copy connection points.
    OPC_POS pos = m_cCPs.GetHeadPosition();

    while (pos != NULL)
    {
        COpcConnectionPoint* pCP = m_cCPs[pos];

        ipEnum->m_cCPs.AddTail(pCP);
    
        // clone must release the reference.
        pCP->AddRef();

        // save the current location.
        if (pos == m_pos)
        {
            ipEnum->m_pos = ipEnum->m_cCPs.GetTailPosition();   
        }

        m_cCPs.GetNext(pos);
    }      

    // query interface.
    HRESULT hResult = ipEnum->QueryInterface(IID_IEnumConnectionPoints, (void**)ppEnum);

    // release local reference.
    ipEnum->Release();

    return hResult;
}
