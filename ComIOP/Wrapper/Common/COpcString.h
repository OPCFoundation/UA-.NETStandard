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

#ifndef _COpcString_H_
#define _COpcString_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcDefs.h"

#define OPC_EMPTY_STRING _T("")

//==============================================================================
// Class:   COpcString
// PURPOSE: Implements a string class.

class OPCUTILS_API COpcString
{
    OPC_CLASS_NEW_DELETE_ARRAY();

public:

    //==========================================================================
    // Operators

    // Constructor
    COpcString();
    COpcString(LPCSTR szStr);
    COpcString(LPCWSTR wszStr);
    COpcString(const GUID& cGuid);

    // Copy Constructor
    COpcString(const COpcString& cStr);

    // Destructor
    ~COpcString();

    // Cast
    operator LPCSTR() const;
    operator LPCWSTR() const;

    // Assignment
    COpcString& operator=(const COpcString& cStr);

    // Append
    COpcString& operator+=(const COpcString& cStr);

    // Index
    TCHAR& operator[](UINT uIndex);
    TCHAR  operator[](UINT uIndex) const;

    // Comparison
    int Compare(const COpcString& cStr) const;

    bool operator==(LPCSTR szStr) const {return (Compare(szStr) == 0);}
    bool operator<=(LPCSTR szStr) const {return (Compare(szStr) <= 0);}
    bool operator <(LPCSTR szStr) const {return (Compare(szStr)  < 0);}
    bool operator!=(LPCSTR szStr) const {return (Compare(szStr) != 0);}
    bool operator >(LPCSTR szStr) const {return (Compare(szStr)  > 0);}
    bool operator>=(LPCSTR szStr) const {return (Compare(szStr) >= 0);}  
    
    bool operator==(LPCWSTR szStr) const {return (Compare(szStr) == 0);}
    bool operator<=(LPCWSTR szStr) const {return (Compare(szStr) <= 0);}
    bool operator <(LPCWSTR szStr) const {return (Compare(szStr)  < 0);}
    bool operator!=(LPCWSTR szStr) const {return (Compare(szStr) != 0);}
    bool operator >(LPCWSTR szStr) const {return (Compare(szStr)  > 0);}
    bool operator>=(LPCWSTR szStr) const {return (Compare(szStr) >= 0);}

    bool operator==(const COpcString& szStr) const {return (Compare(szStr) == 0);}
    bool operator<=(const COpcString& szStr) const {return (Compare(szStr) <= 0);}
    bool operator <(const COpcString& szStr) const {return (Compare(szStr)  < 0);}
    bool operator!=(const COpcString& szStr) const {return (Compare(szStr) != 0);}
    bool operator >(const COpcString& szStr) const {return (Compare(szStr)  > 0);}
    bool operator>=(const COpcString& szStr) const {return (Compare(szStr) >= 0);}

    // Addition
    OPCUTILS_API friend COpcString operator+(const COpcString& cStr1, LPCSTR szStr2);
    OPCUTILS_API friend COpcString operator+(const COpcString& cStr1, LPCWSTR wszStr2);
    OPCUTILS_API friend COpcString operator+(const COpcString& cStr1, const COpcString& cStr2);
    OPCUTILS_API friend COpcString operator+(LPCSTR  szStr1,          const COpcString& cStr2);
    OPCUTILS_API friend COpcString operator+(LPCWSTR wszStr1,         const COpcString& cStr2);

    //==========================================================================
    // Public Methods

    // GetLength
    UINT GetLength() const;

    // IsEmpty
    bool IsEmpty() const;

    // Empty
    void Empty() {Free();}

    // ToGuid
    bool ToGuid(GUID& tGuid) const;

    // FromGuid
    void FromGuid(const GUID& tGuid);

    // GetBuffer
    LPTSTR GetBuffer();

    // SetBuffer
    void SetBuffer(UINT uLength);

    // Find
    int Find(LPCTSTR tsTarget) const;

    // ReverseFind
    int ReverseFind(LPCTSTR tsTarget) const;

    // SubStr
    COpcString SubStr(UINT uStart, UINT uCount = -1) const;

    // Trim
    COpcString& Trim();

    // ToLower
    COpcString ToLower(UINT uIndex = -1);

    // ToUpper
    COpcString ToUpper(UINT uIndex = -1);

    // Clone
    static LPSTR Clone(LPCSTR szStr);

    // Clone
    static LPWSTR Clone(LPCWSTR wszStr);

    // ToMultiByte
    static LPSTR ToMultiByte(LPCWSTR wszStr, int iwszLen = -1);

    // ToUnicode
    static LPWSTR ToUnicode(LPCSTR szStr, int iszLen = -1);

private:

    // TStrBuf
    struct TStrBuf
    {
        UINT   uRefs;
        LPSTR  szStr;
        LPWSTR wszStr;
    };

    //==========================================================================
    // Private Methods
    
    // Set
    void Set(LPCSTR szStr);

    // Set
    void Set(LPCWSTR wszStr);

    // Set
    void Set(const COpcString& cStr);
    
    // Free
    void Free();

    // Alloc
    static TStrBuf* Alloc(UINT uLength);

    //==========================================================================
    // Private Members

    TStrBuf* m_pBuf;
};

//==============================================================================
// FUNCTION: Comparisons
// PURPOSE:  Compares two strings.

OPCUTILS_API inline bool operator==(LPCSTR szStr1, const COpcString& cStr2) {return (cStr2.Compare(szStr1) == 0);}
OPCUTILS_API inline bool operator<=(LPCSTR szStr1, const COpcString& cStr2) {return (cStr2.Compare(szStr1) <= 0);}
OPCUTILS_API inline bool operator <(LPCSTR szStr1, const COpcString& cStr2) {return (cStr2.Compare(szStr1)  < 0);}
OPCUTILS_API inline bool operator!=(LPCSTR szStr1, const COpcString& cStr2) {return (cStr2.Compare(szStr1) != 0);}
OPCUTILS_API inline bool operator >(LPCSTR szStr1, const COpcString& cStr2) {return (cStr2.Compare(szStr1)  > 0);}
OPCUTILS_API inline bool operator>=(LPCSTR szStr1, const COpcString& cStr2) {return (cStr2.Compare(szStr1) >= 0);}  

OPCUTILS_API inline bool operator==(LPCWSTR szStr1, const COpcString& cStr2) {return (cStr2.Compare(szStr1) == 0);}
OPCUTILS_API inline bool operator<=(LPCWSTR szStr1, const COpcString& cStr2) {return (cStr2.Compare(szStr1) <= 0);}
OPCUTILS_API inline bool operator <(LPCWSTR szStr1, const COpcString& cStr2) {return (cStr2.Compare(szStr1)  < 0);}
OPCUTILS_API inline bool operator!=(LPCWSTR szStr1, const COpcString& cStr2) {return (cStr2.Compare(szStr1) != 0);}
OPCUTILS_API inline bool operator >(LPCWSTR szStr1, const COpcString& cStr2) {return (cStr2.Compare(szStr1)  > 0);}
OPCUTILS_API inline bool operator>=(LPCWSTR szStr1, const COpcString& cStr2) {return (cStr2.Compare(szStr1) >= 0);}

#endif // _COpcString_H_
