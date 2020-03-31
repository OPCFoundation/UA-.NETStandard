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
#include "OpcRegistry.h"
#include "COpcString.h"

//==============================================================================
// OpcRegGetValue

bool OpcRegGetValue(
	HKEY    hBaseKey,
	LPCTSTR tsSubKey,
	LPCTSTR tsValueName,
	LPTSTR* ptsValue
)
{
    bool bResult = true;

    if (ptsValue == NULL)
    {
        return false;
    }

    *ptsValue = NULL;
    
    HKEY  hKey  = NULL;
    BYTE* pData = NULL;

    TRY
    {
        // open registry key.
	    LONG lResult = RegOpenKeyEx(
            hBaseKey,
            tsSubKey,
            NULL,
            KEY_READ,
            &hKey
        );

        if (lResult != ERROR_SUCCESS)
        {
            THROW_(bResult, false);
        }

        // determine the string value length.
        DWORD dwType   = 0;
        DWORD dwLength = 0;

        lResult = ::RegQueryValueEx(
            hKey,
            tsValueName,
            NULL,
            &dwType,
            NULL,
            &dwLength
        );

        if (lResult != ERROR_SUCCESS)
        {
            THROW_(bResult, false);
        }

        // check for correct value type.
        if ((dwType != REG_SZ) && (dwType != REG_EXPAND_SZ))
        {
            THROW_(bResult, false);
        }

        // allocate space and read the value.
        pData = (BYTE*)OpcAlloc(dwLength);
        memset(pData, 0, dwLength);

        lResult = ::RegQueryValueEx(
            hKey,
            tsValueName,
            NULL,
            NULL,
            pData,
            &dwLength
        );

        if (lResult != ERROR_SUCCESS)
        {
            THROW_(bResult, false);
        }

        *ptsValue = OpcArrayAlloc(TCHAR, dwLength/sizeof(TCHAR));
        _tcscpy(*ptsValue, (LPCTSTR)pData);
        (*ptsValue)[dwLength/sizeof(TCHAR)-1] = _T('\0');
    }
    CATCH
    {
    }
    FINALLY
    {
        if (hKey != NULL) RegCloseKey(hKey);
        OpcFree(pData);
    }   

    return bResult;
}
   
//==============================================================================
// OpcRegGetValue

bool OpcRegGetValue(
	HKEY    hBaseKey,
	LPCTSTR tsSubKey,
	LPCTSTR tsValueName,
	DWORD*  pdwValue
)
{
    return false;
}

//==============================================================================
// OpcRegSetValue

bool OpcRegSetValue(
	HKEY    hBaseKey,
	LPCTSTR tsSubKey,
	LPCTSTR tsValueName,
	DWORD   dwType,
	BYTE*   pValue,
	DWORD   dwLength
)
{
    bool bResult = true;

    HKEY hKey = NULL;

    TRY
    {
        DWORD dwDisposition = NULL;

        LONG lResult = RegCreateKeyEx(
            hBaseKey, 
            tsSubKey, 
            NULL, 
            NULL,
            REG_OPTION_NON_VOLATILE,
            KEY_WRITE,
            NULL,
            &hKey,
            &dwDisposition
        );

        if (lResult != ERROR_SUCCESS)
        {
            THROW_(bResult, false);
        }

        lResult = RegSetValueEx(
            hKey,
            tsValueName,
            NULL,
            dwType,
            pValue,
            dwLength
        );

        if (lResult != ERROR_SUCCESS)
        {
            THROW_(bResult, false);
        }
    }
    CATCH 
    {
    }
    FINALLY
    {
        if (hKey != NULL) RegCloseKey(hKey);
    }

    return bResult;
}

//==============================================================================
// OpcRegSetValue

bool OpcRegSetValue(
	HKEY    hBaseKey,
	LPCTSTR tsSubKey,
	LPCTSTR tsValueName,
	LPCTSTR tsValue
)
{
    return OpcRegSetValue(
		hBaseKey, 
		tsSubKey, 
		tsValueName,
		REG_SZ, 
        (BYTE*)tsValue,
        (_tcslen(tsValue)+1)*sizeof(TCHAR)
    );
}

//==============================================================================
// OpcRegSetValue

bool OpcRegSetValue(
	HKEY    hBaseKey,
	LPCTSTR tsSubKey,
	LPCTSTR tsValueName,
	DWORD   dwValue
)
{
    return OpcRegSetValue(
		hBaseKey, 
		tsSubKey, 
		tsValueName,
		REG_DWORD, 
        (BYTE*)&dwValue,
        sizeof(DWORD)
    );
}

//==============================================================================
// OpcRegSetValue

bool OpcRegSetValue(
	HKEY    hBaseKey,
	LPCTSTR tsSubKey,
	LPCTSTR tsValueName,
	BYTE*   pValue,
	DWORD   dwLength
)
{
    return OpcRegSetValue(
		hBaseKey, 
		tsSubKey, 
		tsValueName,
		REG_BINARY, 
        pValue,
        dwLength
    );
}

//==============================================================================
// OpcRegDeleteKey

bool OpcRegDeleteKey(
	HKEY    hBaseKey,
	LPCTSTR tsSubKey
)
{
    bool bResult = true;

    HKEY hKey = NULL;

    TRY
    {
        // parse the sub key path.
        COpcString cKey(tsSubKey);
        COpcString cParent;

        int iIndex = cKey.ReverseFind(_T("\\"));

        if (iIndex != -1)
        {
            cParent = cKey.SubStr(0, iIndex);
            cKey    = cKey.SubStr(iIndex+1);
        }

        // safety check - don't delete root keys
        if (cKey.IsEmpty())
        {
            THROW_(bResult, false);
        }

        // open the key for modifications.
        LONG lResult = RegOpenKeyEx(
            hBaseKey, 
            tsSubKey, 
			NULL,
            KEY_ALL_ACCESS, 
            &hKey
        );

        if (lResult != ERROR_SUCCESS)
        {
            THROW_(bResult, false);
        }

        // determine the number of sub keys.
        DWORD dwCount = 0;
        DWORD dwMaxLength = 0;

        lResult = RegQueryInfoKey(
            hKey,         // handle to key
            NULL,         // class buffer
            NULL,         // size of class buffer
            NULL,         // reserved
            &dwCount,     // number of subkeys
            &dwMaxLength, // longest subkey name
            NULL,         // longest class string
            NULL,         // number of value entries
            NULL,         // longest value name
            NULL,         // longest value data
            NULL,         // descriptor length
            NULL          // last write time
        );

        if (lResult != ERROR_SUCCESS)
        {
            THROW_(bResult, false);
        }

        // recursively delete sub keys. 
        LPTSTR tsName = new TCHAR[dwMaxLength+1];

        while (dwCount > 0 && (lResult == ERROR_SUCCESS || lResult == ERROR_MORE_DATA))
        {
            DWORD dwLength = dwMaxLength+1;

            lResult = RegEnumKeyEx(
                hKey, 
                0, 
                tsName, 
                &dwLength,
                NULL,
                NULL,
                NULL,
                NULL);

            if (lResult == ERROR_MORE_DATA || lResult == ERROR_SUCCESS)
            {
                COpcString cSubKey(tsSubKey);

                cSubKey += _T("\\");
                cSubKey += tsName;

                OpcRegDeleteKey(hBaseKey, cSubKey);
            }
        }

        delete [] tsName;

        // close the key before delete.
        RegCloseKey(hKey);

        // open the parent key for delete.
        lResult = RegOpenKeyEx(
            hBaseKey, 
            cParent, 
            NULL,
            KEY_ALL_ACCESS, 
            &hKey
        );

        if (lResult != ERROR_SUCCESS)
        {
            THROW_(bResult, false);
        }

        // delete key
        lResult = RegDeleteKey(hKey, cKey);

        if (lResult != ERROR_SUCCESS)
        {
            THROW_(bResult, false);
        }
    }
    CATCH 
    {
    }
    FINALLY
    {
        if (hKey != NULL) RegCloseKey(hKey);
    }

    return bResult;
}
