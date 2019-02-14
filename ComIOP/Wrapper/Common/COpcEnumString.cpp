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
#include "COpcEnumString.h"

//==============================================================================
// COpcEnumString

COpcEnumString::COpcEnumString()
{
    m_uIndex   = 0;
    m_uCount   = 0;
    m_pStrings = NULL;
}

// Constructor
COpcEnumString::COpcEnumString(UINT uCount, LPWSTR*& pStrings)
{
    m_uIndex   = 0;
    m_uCount   = uCount;
    m_pStrings = pStrings;

    // take ownership of memory.
    pStrings = NULL;
}

// Destructor 
COpcEnumString::~COpcEnumString()
{
    for (UINT ii = 0; ii < m_uCount; ii++)
    {
        OpcFree(m_pStrings[ii]);
    }

    OpcFree(m_pStrings);
}

//==============================================================================
// IEnumString
   
// Next
HRESULT COpcEnumString::Next(
    ULONG     celt,
    LPOLESTR* rgelt,
    ULONG*    pceltFetched)
{
    // check for invalid arguments.
    if (rgelt == NULL || pceltFetched == NULL)
    {
        return E_INVALIDARG;
    }

    *pceltFetched = NULL;

    // all strings already returned.
    if (m_uIndex >= m_uCount)
    {
        return S_FALSE;
    }

    // copy strings.
    for (UINT ii = m_uIndex; ii < m_uCount && *pceltFetched < celt; ii++)
    {
        rgelt[*pceltFetched] = (LPWSTR)CoTaskMemAlloc((wcslen(m_pStrings[ii])+1)*sizeof(WCHAR));

        if (m_pStrings[ii] == NULL)
        {
            rgelt[*pceltFetched] = NULL;
        }
        else
        {
            wcscpy(rgelt[*pceltFetched], m_pStrings[ii]);
        }

        (*pceltFetched)++;
    }

    // no enough strings left.
    if (*pceltFetched < celt)
    {
        m_uIndex = m_uCount;
        return S_FALSE;
    }

    m_uIndex = ii;
    return S_OK;
}

// Skip
HRESULT COpcEnumString::Skip(ULONG celt)
{
    if (m_uIndex + celt > m_uCount)
    {
        m_uIndex = m_uCount;
        return S_FALSE;
    }

    m_uIndex += celt;
    return S_OK;
}

// Reset
HRESULT COpcEnumString::Reset()
{
    m_uIndex = 0;
    return S_OK;
}

// Clone
HRESULT COpcEnumString::Clone(IEnumString** ppEnum)
{
    // check for invalid arguments.
    if (ppEnum == NULL)
    {
        return E_INVALIDARG;
    }

    // allocate enumerator.
    COpcEnumString* pEnum = new COpcEnumString();
 
    // copy strings.
    pEnum->m_pStrings = OpcArrayAlloc(LPWSTR, m_uCount);

    for (UINT ii = 0; ii < m_uCount; ii++)
    {
        pEnum->m_pStrings[ii] = OpcArrayAlloc(WCHAR, wcslen(m_pStrings[ii])+1);
        wcscpy(pEnum->m_pStrings[ii], m_pStrings[ii]);
    }

    // set index.
    pEnum->m_uIndex = m_uIndex;
    pEnum->m_uCount = m_uCount;

    HRESULT hResult = pEnum->QueryInterface(IID_IEnumString, (void**)ppEnum);

    // release local reference.
    pEnum->Release();

    return hResult;
}
