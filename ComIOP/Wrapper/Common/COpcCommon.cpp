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
#include "COpcCommon.h"

//==============================================================================
// Local Declarations

#define MAX_ERROR_STRING       1024

#define LOCALE_ENGLISH_US      MAKELCID(MAKELANGID(LANG_ENGLISH, SUBLANG_ENGLISH_US), SORT_DEFAULT)
#define LOCALE_ENGLISH_NEUTRAL MAKELCID(MAKELANGID(LANG_ENGLISH, SUBLANG_NEUTRAL), SORT_DEFAULT)

//==============================================================================
// COpcCommon

COpcCommon::COpcCommon()
{
    m_dwLcid      = LOCALE_SYSTEM_DEFAULT;
    m_cClientName = _T("");
}

// Destructor 
COpcCommon::~COpcCommon()
{
    // do nothing.
}

// SetLocaleID
HRESULT COpcCommon::SetLocaleID(LCID dwLcid)
{
    // check that the locale is supported.
    switch (dwLcid)
    {
        case LOCALE_ENGLISH_US:
        case LOCALE_ENGLISH_NEUTRAL:
        case LOCALE_NEUTRAL:
        case LOCALE_INVARIANT:
        case LOCALE_SYSTEM_DEFAULT:
        case LOCALE_USER_DEFAULT:
        {
            m_dwLcid = dwLcid;
            return S_OK;
        }

        default:
        {
            const LCID* pLcids = GetAvailableLocaleIDs();

            if (pLcids != NULL)
            {
                for (int ii = 0; pLcids[ii] != NULL; ii++)
                {
                    if (pLcids[ii] == dwLcid)
                    {
                        m_dwLcid = dwLcid;
                        return S_OK;
                    }
                }
            }

            break;
        }
    }

    // locale not available - return error.
    return E_INVALIDARG;
}

// GetLocaleID
HRESULT COpcCommon::GetLocaleID(LCID* pdwLcid)
{
    // invalid arguments - return error.
    if (pdwLcid == NULL)
    {
        return E_INVALIDARG;
    }

    *pdwLcid = m_dwLcid;
    return S_OK;
}

// QueryAvailableLocaleIDs
HRESULT COpcCommon::QueryAvailableLocaleIDs(DWORD* pdwCount, LCID** pdwLcid)
{
    // invalid arguments - return error.
    if (pdwCount == NULL || pdwLcid == NULL)
    {
        return E_INVALIDARG;
    }

	// always supports english and neutral cultures.
	*pdwCount = 4;

	// get additional locales.
    const LCID* pLcids = GetAvailableLocaleIDs();

	// count the available locales.
	if (pLcids != NULL)
	{
		for (int ii = 0; pLcids[ii] != NULL; ii++) (*pdwCount)++;
	}

	// allocate array.
	*pdwLcid = (LCID*)CoTaskMemAlloc((*pdwCount)*sizeof(LCID));

	// add default locales.
	(*pdwLcid)[0] = LOCALE_ENGLISH_US;
	(*pdwLcid)[1] = LOCALE_ENGLISH_NEUTRAL;
	(*pdwLcid)[2] = LOCALE_NEUTRAL;
	(*pdwLcid)[3] = LOCALE_INVARIANT;

	DWORD temp = GetSystemDefaultLCID();

	// add additional locales.
	if (pLcids != NULL)
	{
		for (int ii = 0; pLcids[ii] != NULL; ii++) (*pdwLcid)[ii+4] = pLcids[ii];
	}

	// everything ok.
    return S_OK;
}

// GetErrorString
HRESULT COpcCommon::GetErrorString(HRESULT dwError, LPWSTR* ppString)
{
    return GetErrorString(dwError, m_dwLcid, ppString);
}

// GetErrorString
COpcString COpcCommon::GetErrorString(
	const COpcString& cModuleName,
    HRESULT           hError
)
{
	COpcString cString;
	LPWSTR pString = NULL;

	HRESULT hResult = COpcCommon::GetErrorString(cModuleName, hError, LOCALE_NEUTRAL, &pString);

	if (SUCCEEDED(hResult))
	{
		cString = pString;
		OpcFree(pString);
	}

	return cString;
}

// FormatMessage
static bool FormatMessage(
	LANGID  langID, 
	DWORD   dwCode, 
	LPCTSTR szModuleName, 
	TCHAR*  tsBuffer, 
	DWORD   dwLength
)
{
	// attempt to load string from module message table.
	DWORD dwResult = FormatMessage(
		FORMAT_MESSAGE_FROM_HMODULE | FORMAT_MESSAGE_IGNORE_INSERTS,
		::GetModuleHandle(szModuleName), 
		dwCode,
		langID, 
		tsBuffer,
		dwLength-1,
		NULL 
	);

	// attempt to load string from system message table.
	if (dwResult == 0)
	{
		dwResult = FormatMessage(
			FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
			NULL, 
			dwCode,
			langID, 
			tsBuffer,
			dwLength-1,
			NULL 
		);
	}

	return (dwResult != 0);
}

// GetErrorString
HRESULT COpcCommon::GetErrorString( 
	LPCTSTR szModuleName,
    HRESULT dwError,
    LCID    dwLocale,
    LPWSTR* ppString
)
{  
    // check arguments.
    if (ppString == NULL)
    {
        return E_INVALIDARG;
    }

    *ppString = NULL;

	LANGID langID = LANGIDFROMLCID(dwLocale);

	// lookup languages for 'special' locales.
	switch (dwLocale)
	{
		case LOCALE_SYSTEM_DEFAULT:
		{
			langID = GetSystemDefaultLangID();
			break;
		}

		case LOCALE_USER_DEFAULT:
		{
			langID = GetUserDefaultLangID();
			break;
		}

		case LOCALE_INVARIANT:
		{
			langID = LANGIDFROMLCID(LOCALE_NEUTRAL);
			break;
		}
	}

    // initialize buffer for message.
    TCHAR tsMsg[MAX_ERROR_STRING+1];
    memset(tsMsg, 0, sizeof(tsMsg));

	if (!FormatMessage(langID, dwError, szModuleName, tsMsg, MAX_ERROR_STRING+1))
	{
		// check if the locale explicitly requested a particular language.
		if (dwLocale == LOCALE_SYSTEM_DEFAULT || dwLocale == LOCALE_USER_DEFAULT)
		{
			// try to load message using special locales.
			if (!FormatMessage(LANGIDFROMLCID(dwLocale), dwError, szModuleName, tsMsg, MAX_ERROR_STRING+1))
			{
				// use US english if a default locale was requested but language is not supported by server.
				if (!FormatMessage(LANGIDFROMLCID(LOCALE_ENGLISH_US), dwError, szModuleName, tsMsg, MAX_ERROR_STRING+1))
				{
					return E_INVALIDARG;
				}
			}
		}
		else
		{
			// return US english for variants for english.
			if (PRIMARYLANGID(langID) == LANG_ENGLISH)
			{	
				if (!FormatMessage(LANGIDFROMLCID(LOCALE_ENGLISH_US), dwError, szModuleName, tsMsg, MAX_ERROR_STRING+1))
				{
					return E_INVALIDARG;
				}
			}

			// locale is not supported at all.
			else
			{
				return E_INVALIDARG;
			}
		}
	}

    COpcString cMsg = tsMsg;

    // remove trailing \r\n.
	if (cMsg.ReverseFind(_T("\r\n")) == (int)(cMsg.GetLength()-2))
	{
		cMsg = cMsg.SubStr(0, cMsg.GetLength()-2);
	}

    // allocate string for return.
    *ppString = OpcStrDup((LPCWSTR)cMsg);

    return S_OK;
}

// SetClientName
HRESULT COpcCommon::SetClientName(LPCWSTR szName)
{
    m_cClientName = szName;
    return S_OK;
}
