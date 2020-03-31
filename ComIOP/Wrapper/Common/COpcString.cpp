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
#include "COpcString.h"

//==============================================================================
// Local Declarations

#define GUID_STR_LENGTH 38

//==============================================================================
// COpcString

// Constructor
COpcString::COpcString()
{
    m_pBuf = NULL;
}

// Constructor
COpcString::COpcString(LPCSTR szStr)
{
    m_pBuf = NULL;
    Set(szStr);
}

// Constructor
COpcString::COpcString(LPCWSTR wszStr)
{
    m_pBuf = NULL;
    Set(wszStr);
}

// Copy Constructor
COpcString::COpcString(const COpcString& cStr)
{
    m_pBuf = NULL;
    Set(cStr);
}

COpcString::COpcString(const GUID& cGuid)
{
    m_pBuf = NULL;
    FromGuid(cGuid);
}

// Destructor
COpcString::~COpcString()
{
    Free();
}

// Cast
COpcString::operator LPCSTR() const
{
    TStrBuf* pBuf = (TStrBuf*)this->m_pBuf;

    if (pBuf == NULL)
    {
        return NULL;
    }

    #ifdef _UNICODE  
    if (pBuf->szStr == NULL)
    {
        ((LPSTR)pBuf->szStr) = ToMultiByte(pBuf->wszStr);
    }
    #endif

    return pBuf->szStr;
}

// Cast
COpcString::operator LPCWSTR() const
{
    TStrBuf* pBuf = (TStrBuf*)this->m_pBuf;

    if (pBuf == NULL)
    {
        return NULL;
    }

    #ifndef _UNICODE
    if (pBuf->wszStr == NULL)
    {
        ((LPWSTR)pBuf->wszStr) = ToUnicode(pBuf->szStr);
    }
    #endif

    return pBuf->wszStr;
}

// Assignment
COpcString& COpcString::operator=(const COpcString& cStr)
{
    Set(cStr);
    return *this;
}

// Index
TCHAR& COpcString::operator[](UINT uIndex)
{
    #ifdef _UNICODE
    OpcFree(m_pBuf->szStr);
    m_pBuf->szStr = NULL;
    #else
    OpcFree(m_pBuf->wszStr);
    m_pBuf->wszStr = NULL;
    #endif

    OPC_ASSERT(uIndex < GetLength());
    return ((LPTSTR)((LPCTSTR)*this))[uIndex];
}
 
// Index
TCHAR COpcString::operator[](UINT uIndex) const
{
    OPC_ASSERT(uIndex < GetLength());
    return ((LPCTSTR)*this)[uIndex];
}

// Append
COpcString& COpcString::operator+=(const COpcString& cStr)
{
	UINT uLength1 = GetLength();
	UINT uLength2 = uLength1 + cStr.GetLength();

	// check for buffer overflow.
	if (uLength2 < uLength1)
	{
        Empty();
        return *this;
	}

    TStrBuf* pBuf = Alloc(uLength2);

    if (pBuf == NULL)
    {
        Empty();
        return *this;
    }

    #ifdef _UNICODE

    if ((LPCWSTR)*this != NULL)
    {
        wcscpy(pBuf->wszStr, (LPCWSTR)*this);
    }

    if ((LPCWSTR)(COpcString&)cStr != NULL)
    {
        wcscat(pBuf->wszStr, (LPCWSTR)(COpcString&)cStr);
    }

    #else

    if ((LPCSTR)*this != NULL)
    {
        strcpy(pBuf->szStr, (LPCSTR)*this);
    }

    if ((LPCSTR)(COpcString&)cStr != NULL)
    {
        strcat(pBuf->szStr, (LPCSTR)(COpcString&)cStr);
    }

    #endif

    Free();
    m_pBuf = pBuf;
    return *this;
}

// Compare
int COpcString::Compare(const COpcString& cStr) const
{
    // check for self references.
    if (&cStr == this) return 0;
    if (cStr.m_pBuf == m_pBuf) return 0;

    // check for null.
    if (cStr.m_pBuf == NULL) return +1;
    if (m_pBuf == NULL) return -1;

    // compare strings.
    #ifdef _UNICODE
    return wcscmp((LPCWSTR)*this, (LPCWSTR)(COpcString&)cStr);
    #else
    return strcmp((LPCSTR)*this, (LPCSTR)(COpcString&)cStr);
    #endif
}

// Addition
COpcString operator+(const COpcString& cStr1, LPCSTR szStr2)
{
    COpcString cStr(cStr1);
    cStr += szStr2;
    return cStr;
}

// Addition
COpcString operator+(const COpcString& cStr1, LPCWSTR wszStr2)
{
    COpcString cStr(cStr1);
    cStr += wszStr2;
    return cStr;
}

// Addition
COpcString operator+(const COpcString& cStr1, const COpcString& cStr2)
{
    COpcString cStr(cStr1);
    cStr += cStr2;
    return cStr;
}

// Addition
COpcString operator+(LPCSTR  szStr1, const COpcString& cStr2)
{
    COpcString cStr(szStr1);
    cStr += cStr2;
    return cStr;
}

// Addition
COpcString operator+(LPCWSTR wszStr1, const COpcString& cStr2)
{
    COpcString cStr(wszStr1);
    cStr += cStr2;
    return cStr;
}

// IsEmpty
bool COpcString::IsEmpty() const
{
    return (m_pBuf == NULL);
}

// GetLength
UINT COpcString::GetLength() const
{
    if (m_pBuf == NULL)
    {
        return 0;
    }

    #ifdef _UNICODE
    return wcslen(m_pBuf->wszStr);
    #else
    return strlen(m_pBuf->szStr);
    #endif
}

// GetBuffer
LPTSTR COpcString::GetBuffer()
{
    if (m_pBuf == NULL)
    {
        // set it to a zero length string.
        Set(OPC_EMPTY_STRING);
    }

    #ifdef _UNICODE
    OpcFree(m_pBuf->szStr);
    m_pBuf->szStr = NULL;
    return m_pBuf->wszStr;
    #else
    OpcFree(m_pBuf->wszStr);
    m_pBuf->wszStr = NULL;
    return m_pBuf->szStr;
    #endif
}

// SetBuffer
void COpcString::SetBuffer(UINT uLength)
{
    Free();
    m_pBuf = Alloc(uLength);
}

// Free
void COpcString::Free()
{
    if (m_pBuf != NULL)
    {
        LONG lRefs = InterlockedDecrement((LONG*)&m_pBuf->uRefs);

        if (lRefs == 0)
        {
            OpcFree(m_pBuf->szStr);
            OpcFree(m_pBuf->wszStr);
            OpcFree(m_pBuf);
        }

        m_pBuf = NULL;
    }
}

// Set
void COpcString::Set(LPCSTR szStr)
{
    Free();

    if (szStr == NULL || strlen(szStr) == 0)
    {
        return;
    }

    m_pBuf          = (TStrBuf*)OpcAlloc(sizeof(TStrBuf));
    m_pBuf->uRefs   = 1;

    #ifdef _UNICODE
    m_pBuf->szStr   = NULL;
    m_pBuf->wszStr  = ToUnicode(szStr);
    #else
    m_pBuf->szStr   = Clone(szStr);
    m_pBuf->wszStr  = NULL;   
    #endif
}

// Set
void COpcString::Set(LPCWSTR wszStr)
{
    Free();

    if (wszStr == NULL || wcslen(wszStr) == 0)
    {
        return;
    }

    m_pBuf          = (TStrBuf*)OpcAlloc(sizeof(TStrBuf));
    m_pBuf->uRefs   = 1;

    #ifdef _UNICODE
    m_pBuf->szStr   = NULL;
    m_pBuf->wszStr  = Clone(wszStr);
    #else
    m_pBuf->szStr   = ToMultiByte(wszStr);
    m_pBuf->wszStr  = NULL;   
    #endif
}

// Set
void COpcString::Set(const COpcString& cStr)
{
    // check for self references.
    if (&cStr == this) return;
    if (cStr.m_pBuf == m_pBuf) return;

    Free();

    m_pBuf = cStr.m_pBuf;

    if (m_pBuf != NULL)
    {
        InterlockedIncrement((LONG*)&m_pBuf->uRefs);
    }
}

// Find
int COpcString::Find(LPCTSTR tsTarget) const
{
    LPCTSTR tsStr = (LPCTSTR)*this;

    if (tsStr == NULL || tsTarget == NULL || _tcslen(tsTarget) == 0)
    {
        return -1;
    }

    int iLength = _tcslen(tsTarget);
    int iLast   = _tcslen(tsStr) - iLength + 1;

    for (int ii = 0; ii < iLast; ii++)
    {
        if (_tcsncmp(tsStr+ii, tsTarget, iLength) == 0)
        {
            return ii;
        }
    }

    return -1;
}

// ReverseFind
int COpcString::ReverseFind(LPCTSTR tsTarget) const
{
    LPCTSTR tsStr = (LPCTSTR)*this;

    if (tsStr == NULL || tsTarget == NULL || _tcslen(tsTarget) == 0)
    {
        return -1;
    }

    int iLength = _tcslen(tsTarget);
    int iLast   = _tcslen(tsStr) - iLength;

    for (int ii = iLast; ii >=0; ii--)
    {
        if (_tcsncmp(tsStr+ii, tsTarget, iLength) == 0)
        {
            return ii;
        }
    }

    return -1;
}

// SubStr
COpcString COpcString::SubStr(UINT uStart, UINT uCount) const
{
    COpcString cStr;

    UINT uLength = GetLength();

    if (uCount == 0 || uStart >= uLength)
    {
        return cStr;
    }

    uLength = (uCount == -1 || uCount > uLength)?uLength-uStart:uCount;

    cStr.SetBuffer(uLength);
    _tcsncpy(cStr.GetBuffer(), ((LPCTSTR)*this)+uStart, uLength);

    return cStr;
}

// Trim
COpcString& COpcString::Trim()
{
    if (m_pBuf == NULL)
    {
        return *this;
    }

    UINT uStart = 0;
    UINT uEnd   = GetLength();

    // remove leading whitespace.
    for (UINT ii = 0; ii < uEnd; ii++)
    {
        if (!_istspace((*this)[ii]))
        {
            uStart = ii;
            break;
        }
    }

    // string only contained whitespace.
    if (uStart == uEnd)
    {
        Empty();
        return *this;
    }

    // remove trailing whitespace
    for (ii = uEnd-1; ii >= uStart; ii--)
    {
        if (!_istspace((*this)[ii]))
        {
            uEnd = ii+1;
            break;
        }
    }

    // update string.
    *this = SubStr(uStart, uEnd - uStart);

    return *this;
}

// ToLower
COpcString COpcString::ToLower(UINT uIndex)
{
	COpcString cCopy = *this;
    
    if (cCopy.m_pBuf == NULL)
    {
        return cCopy;
    }

    #ifdef _UNICODE
    OpcFree(cCopy.m_pBuf->szStr);
    cCopy.m_pBuf->szStr = NULL;
    #else
    OpcFree(cCopy.m_pBuf->wszStr);
    cCopy.m_pBuf->wszStr = NULL;
    #endif

    UINT uLength = cCopy.GetLength();

    if (uIndex == -1)
    {
        for (UINT ii = 0; ii < uLength; ii++)
        {
            #ifdef _UNICODE
            if (iswupper(cCopy.m_pBuf->wszStr[ii])) cCopy.m_pBuf->wszStr[ii] = towlower(cCopy.m_pBuf->wszStr[ii]);
            #else
            if (isupper(cCopy.m_pBuf->szStr[ii])) cCopy.m_pBuf->szStr[ii] = tolower(cCopy.m_pBuf->szStr[ii]);
            #endif
        }

        return *this;
    }

    if (uIndex < uLength)
    {
        #ifdef _UNICODE
        if (iswupper(cCopy.m_pBuf->wszStr[uIndex])) cCopy.m_pBuf->wszStr[uIndex] = towlower(cCopy.m_pBuf->wszStr[uIndex]);
        #else
        if (isupper(cCopy.m_pBuf->szStr[uIndex])) cCopy.m_pBuf->szStr[uIndex] = tolower(cCopy.m_pBuf->szStr[uIndex]);
        #endif
    }

    return cCopy;
}

// ToUpper
COpcString COpcString::ToUpper(UINT uIndex)
{
	COpcString cCopy = *this;

    if (cCopy.m_pBuf == NULL)
    {
        return *this;
    }

    #ifdef _UNICODE
    OpcFree(cCopy.m_pBuf->szStr);
    cCopy.m_pBuf->szStr = NULL;
    #else
    OpcFree(cCopy.m_pBuf->wszStr);
    cCopy.m_pBuf->wszStr = NULL;
    #endif

    UINT uLength = cCopy.GetLength();

    if (uIndex == -1)
    {
        for (UINT ii = 0; ii < uLength; ii++)
        {
            #ifdef _UNICODE
            if (iswlower(cCopy.m_pBuf->wszStr[ii])) cCopy.m_pBuf->wszStr[ii] = towupper(cCopy.m_pBuf->wszStr[ii]);
            #else
            if (islower(cCopy.m_pBuf->szStr[ii])) cCopy.m_pBuf->szStr[ii] = toupper(cCopy.m_pBuf->szStr[ii]);
            #endif
        }

        return *this;
    }

    if (uIndex < uLength)
    {
        #ifdef _UNICODE
        if (iswlower(cCopy.m_pBuf->wszStr[uIndex])) cCopy.m_pBuf->wszStr[uIndex] = towupper(cCopy.m_pBuf->wszStr[uIndex]);
        #else
        if (islower(cCopy.m_pBuf->szStr[uIndex]))cCopy.m_pBuf->szStr[uIndex] = toupper(cCopy.m_pBuf->szStr[uIndex]);
        #endif
    }

    return cCopy;
}

// Alloc
COpcString::TStrBuf* COpcString::Alloc(UINT uLength)
{
    if (uLength == 0)
    {
        return NULL;
    }

    TStrBuf* pBuf = (TStrBuf*)OpcAlloc(sizeof(TStrBuf));
    pBuf->uRefs   = 1;

    #ifdef _UNICODE
    pBuf->szStr  = NULL;
    pBuf->wszStr = OpcArrayAlloc(WCHAR, uLength+1);
    memset(pBuf->wszStr, 0, (uLength+1)*sizeof(WCHAR));
    #else
    pBuf->szStr  = OpcArrayAlloc(CHAR, uLength+1);
    pBuf->wszStr = NULL;
    memset(pBuf->szStr, 0, (uLength+1)*sizeof(CHAR));
    #endif

    return pBuf;
}

// Clone
LPSTR COpcString::Clone(LPCSTR szStr)
{
    if (szStr == NULL || strlen(szStr) == 0)
    {
        return NULL;
    }

    LPSTR szCopy = OpcArrayAlloc(CHAR, strlen(szStr)+1);
    strcpy(szCopy, szStr);
 
    return szCopy;
}

// Clone
LPWSTR COpcString::Clone(LPCWSTR wszStr)
{
    if (wszStr == NULL || wcslen(wszStr) == 0)
    {
        return NULL;
    }

    LPWSTR wszCopy = OpcArrayAlloc(WCHAR, wcslen(wszStr)+1);
    wcscpy(wszCopy, wszStr);

    return wszCopy;
}

// ToMultiByte
LPSTR COpcString::ToMultiByte(LPCWSTR wszStr, int iwszLen)
{
    if (wszStr == NULL || wcslen(wszStr) == 0)
    {
        return NULL;
    }

    int iLength = WideCharToMultiByte(
       CP_UTF8,
       0,
       wszStr,
       iwszLen,
       NULL,
       0,
       NULL,
       NULL
    );

    if (iLength == 0)
    {
        return NULL;
    }
    
    LPSTR szStr = OpcArrayAlloc(CHAR, iLength+1);

    iLength = WideCharToMultiByte(
       CP_UTF8,
       0,
       wszStr,
       iwszLen,
       szStr,
       iLength+1,
       NULL,
       NULL
    );

    if (iLength == 0)
    {
        OpcFree(szStr);
        return NULL;
    }

	szStr[iLength] = '\0';
    return szStr;
}

// ToUnicode
LPWSTR COpcString::ToUnicode(LPCSTR szStr, int iszLen)
{
    if (szStr == NULL || strlen(szStr) == 0)
    {
        return NULL;
    }

    int iLength = MultiByteToWideChar(
       CP_UTF8,
       0,
       szStr,
       iszLen,
       NULL,
       0
    );

    if (iLength == 0)
    {
        return NULL;
    }
    
    LPWSTR wszStr = OpcArrayAlloc(WCHAR, iLength+1);

    iLength = MultiByteToWideChar(
       CP_UTF8,
       0,
       szStr,
       iszLen,
       wszStr,
       iLength+1
    );

    if (iLength == 0)
    {
        OpcFree(wszStr);
        return NULL;
    }

	wszStr[iLength] = L'\0';
    return wszStr;
}

// ConvertULONG
static bool ConvertULONG(LPCSTR szStr, ULONG& ulValue)
{
    static const CHAR szHexDigits[] = "0123456789ABCDEF";

    ulValue = 0;

    for (int ii = 0; szStr[ii] != '\0'; ii++)
    {
        CHAR zDigit = szStr[ii];

        if (!isxdigit(zDigit))
        {
            return false;
        }

        if (islower(zDigit))
        {
            zDigit = toupper(zDigit);
        }

        for (int jj = 0; szHexDigits[jj] != '\0'; jj++)
        {
            if (szHexDigits[jj] == zDigit)
            {
                ulValue *= 16;
                ulValue += jj;
                break;
            }
        }
    }

    return true;
}

// ToGuid
bool COpcString::ToGuid(GUID& tGuid) const
{
    tGuid = GUID_NULL;

    // check for invalid guid string.
    LPCSTR szStr = (LPCSTR)*this;

    if (szStr == NULL || strlen(szStr) != GUID_STR_LENGTH)
    {
        return false;
    }

    ULONG ulBuf = 0;
    CHAR szBuf[GUID_STR_LENGTH];

    // convert first 32 bits.
    strncpy(szBuf, szStr+1, 8);
    szBuf[8] = '\0';

    if (!ConvertULONG(szBuf, ulBuf))
    {
        return false;
    }

    tGuid.Data1 = (DWORD)ulBuf;

    // convert next 16 bits.
    strncpy(szBuf, szStr+10, 4);
    szBuf[4] = '\0';

    if (!ConvertULONG(szBuf, ulBuf))
    {
        return false;
    }

    tGuid.Data2 = (WORD)ulBuf;

    // convert next 16 bits.
    strncpy(szBuf, szStr+15, 4);
    szBuf[4] = '\0';

    if (!ConvertULONG(szBuf, ulBuf))
    {
        return false;
    }

    tGuid.Data3 = (WORD)ulBuf;

    // convert next 8 bits.
    strncpy(szBuf, szStr+20, 2);
    szBuf[2] = '\0';

    if (!ConvertULONG(szBuf, ulBuf))
    {
        return false;
    }

    tGuid.Data4[0] = (BYTE)ulBuf;

    // convert next 8 bits.
    strncpy(szBuf, szStr+22, 2);
    szBuf[2] = '\0';

    if (!ConvertULONG(szBuf, ulBuf))
    {
        return false;
    }

    tGuid.Data4[1] = (BYTE)ulBuf;

    // convert next 48 bits.
    for (int ii = 2; ii < 8; ii++)
    {
        strncpy(szBuf, szStr+21+ii*2, 2);
        szBuf[2] = '\0';

        if (!ConvertULONG(szBuf, ulBuf))
        {
            return false;
        }

        tGuid.Data4[ii] = (BYTE)ulBuf;
    }

    return true;
}

// FromGuid
void COpcString::FromGuid(const GUID& tGuid)
{
    WCHAR wszBuf[GUID_STR_LENGTH+1];
    memset(wszBuf, 0, sizeof(wszBuf));
    StringFromGUID2(tGuid, wszBuf, GUID_STR_LENGTH+1);
    Set(wszBuf);
}

/*
// OpcStrMatch
bool OpcMatch(const COpcString& cString, const COpcString& cPattern)
{
    if (cPattern.IsEmpty()) return true;

    UINT uIndex = cPattern.Find(_T("*"));

    // must match exactly.
    if (uIndex == -1)
    {
        return (cString == cPattern);
    }

    COpcString cText = cPattern.SubStr(0, uIndex);

    // must match start of string.
    if (uIndex > 0)
    {
        if (cString.Find(cText) != 0)
        {
            return false;
        }
    }

    cText = cPattern.SubStr(uIndex+1);

    // must match end of string.
    if (!cText.IsEmpty())
    {
        uIndex = cString.ReverseFind(cText);

        if (uIndex == -1 || (cString.GetLength() - uIndex) > cText.GetLength())
        {
            return false;
        }
    }

    return true;
}
*/
