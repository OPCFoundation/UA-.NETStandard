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

#ifndef _COpcVariant_H_
#define _COpcVariant_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcXmlType.h"
#include "COpcXmlElement.h"
#include "COpcList.h"
#include "COpcArray.h"

//==============================================================================
// FUNCTION: OpcVariantInit
// PURPOSE   Initializes a VARIANT.

OPCUTILS_API void OpcVariantInit(VARIANT* pValue);

//==============================================================================
// FUNCTION: OpcVariantClear
// PURPOSE   Clears a VARIANT.

OPCUTILS_API void OpcVariantClear(VARIANT* pValue);

//==============================================================================
// FUNCTION: OpcVariantCopy
// PURPOSE   Copies a VARIANT.

OPCUTILS_API void OpcVariantCopy(VARIANT* pDst, const VARIANT* pSrc);

//==============================================================================
// CLASS:   COpcVariant
// PURPOSE  A wrapper for an VARIANT.

class OPCUTILS_API COpcVariant
{
    OPC_CLASS_NEW_DELETE()

public:

    //==========================================================================
    // Public Operators

    // Constructor
    COpcVariant() { Init(); }

    COpcVariant(bool cValue)              { Init(); *this = cValue; }
    COpcVariant(char cValue)              { Init(); *this = cValue; }
    COpcVariant(unsigned char cValue)     { Init(); *this = cValue; }
    COpcVariant(short cValue)             { Init(); *this = cValue; }
    COpcVariant(unsigned short cValue)    { Init(); *this = cValue; }
    COpcVariant(int cValue)               { Init(); *this = cValue; }
    COpcVariant(unsigned int cValue)      { Init(); *this = cValue; }
    COpcVariant(long cValue)              { Init(); *this = cValue; }
    COpcVariant(unsigned long cValue)     { Init(); *this = cValue; }
    COpcVariant(__int64 cValue)           { Init(); *this = cValue; }
    COpcVariant(unsigned __int64 cValue)  { Init(); *this = cValue; }
    COpcVariant(float cValue)             { Init(); *this = cValue; }
    COpcVariant(double cValue)            { Init(); *this = cValue; }
    COpcVariant(CY cValue)                { Init(); *this = cValue; }
    COpcVariant(FILETIME cValue)          { Init(); *this = cValue; }
    COpcVariant(const COpcString& cValue) { Init(); *this = cValue; }
    COpcVariant(const VARIANT& cValue)    { Init(); *this = cValue; }

    // Copy Constructor
    COpcVariant(const COpcVariant& cValue) { Init(); *this = cValue; }

    // Destructor
    ~COpcVariant() { Clear(); }

    // Assignment    
    COpcVariant& operator=(bool cValue);
    COpcVariant& operator=(char cValue);
    COpcVariant& operator=(unsigned char cValue);
    COpcVariant& operator=(short cValue);
    COpcVariant& operator=(unsigned short cValue);
    COpcVariant& operator=(int cValue);
    COpcVariant& operator=(unsigned int cValue);
    COpcVariant& operator=(long cValue);
    COpcVariant& operator=(unsigned long cValue);
    COpcVariant& operator=(__int64 cValue);
    COpcVariant& operator=(unsigned __int64 cValue);
    COpcVariant& operator=(float cValue);
    COpcVariant& operator=(double cValue);
    COpcVariant& operator=(CY cValue);
    COpcVariant& operator=(FILETIME cValue);
    COpcVariant& operator=(const COpcString& cValue);
    COpcVariant& operator=(const VARIANT& cValue);   
    COpcVariant& operator=(const COpcVariant& cValue);

    // Cast
    operator bool() const;
    operator char() const;
    operator unsigned char() const;
    operator short() const;
    operator unsigned short() const;
    operator int() const;
    operator unsigned int() const;
    operator long() const;
    operator unsigned long() const;
    operator __int64() const;
    operator unsigned __int64() const;
    operator float() const;
    operator double() const;
    operator CY() const;
    operator FILETIME() const;
    operator COpcString() const;

    operator const VARIANT&() const { return m_cValue; }

    //==========================================================================
    // Public Methods

    // Init
    void Init() { memset(&m_cValue, 0, sizeof(VARIANT)); }
    
    // Clear
    void Clear();

    // GetType
    VARTYPE GetType() const { return m_cValue.vt; }

    // GetRef
    VARIANT& GetRef() { return m_cValue; }

    // GetPtr
    VARIANT* GetPtr() { return &m_cValue; }

    // Attach
    void Attach(VARIANT& cValue) { Clear(); *this = cValue; OpcVariantInit(&cValue); }

    // Detach
    void Detach(VARIANT& cValue) { cValue = *this; Init(); }

    // IsEqual
    static bool IsEqual(const VARIANT& cValue1, const VARIANT& cValue2);

    // ChangeType
    static HRESULT ChangeType(
        VARIANT&       cDst,
        const VARIANT& cSrc,
        LCID           lcid,
        VARTYPE        vtType
    );

private:

    VARIANT m_cValue;
};

//==============================================================================
// CLASS:   COpcSafeArray
// PURPOSE  A wrapper for an SAFEARRAY contained in a VARIANT.

class OPCUTILS_API COpcSafeArray
{
    OPC_CLASS_NEW_DELETE()

public:

    //==========================================================================
    // Public Operators

    // Constructor
    COpcSafeArray(VARIANT& cValue);

    // Destructor
    ~COpcSafeArray();

    //==========================================================================
    // Public Methods
    
    // Alloc
    void Alloc(VARTYPE vtType, UINT uLength);

    // Lock
    void Lock();
    
    // Unlock
    void Unlock();

    // GetLength
    UINT GetLength() const;

    // GetData
    void* GetData() const;

    // GetElemType
    VARTYPE GetElemType() const;

    // GetElemSize
    UINT GetElemSize() const;

    // GetElem
    void* GetElem(UINT uIndex);

    // SetElem
    void SetElem(UINT uIndex, void* pElem);
    
    // IsEqual
    static bool IsEqual(const VARIANT& cValue1, const VARIANT& cValue2);

    // ChangeType
    static HRESULT ChangeType(
        VARIANT&       cDst,
        const VARIANT& cSrc,
        LCID           lcid,
        VARTYPE        vtType
    );

private:

    VARIANT& m_cValue;
    UINT     m_uLocks;
};

//==============================================================================
// FUNCTION: OpcReadVariant
// PURPOSE   Uses function overloading and VariantChangeType() to simplify variant access. 

OPCUTILS_API HRESULT OpcReadVariant(bool& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(char& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(unsigned char& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(short& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(unsigned short& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(int& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(unsigned int& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(long& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(unsigned long& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(__int64& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(unsigned __int64& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(float& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(double& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(CY& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(FILETIME& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(COpcString& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(COpcStringList& cDst, const VARIANT& cSrc);
OPCUTILS_API HRESULT OpcReadVariant(COpcStringArray& cDst, const VARIANT& cSrc);

//==============================================================================
// FUNCTION: OpcWriteVariant
// PURPOSE   Uses function overloading ad VariantClear() to simplify variant access. 

OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, bool cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, char cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, unsigned char cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, short cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, unsigned short cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, int cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, unsigned int cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, long cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, unsigned long cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, __int64 cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, unsigned __int64 cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, float cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, double cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, CY cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, FILETIME& cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, LPWSTR cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, const COpcString& cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, const COpcStringList& cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, const COpcStringArray& cSrc);
OPCUTILS_API void OpcWriteVariant(VARIANT& cDst, const LPCWSTR* pszStrings);

namespace OpcXml
{

//==============================================================================
// FUNCTION: GetVarType
// PURPOSE   Converts an XML data type to a VARIANT type.

OPCUTILS_API VARTYPE GetVarType(Type eType);

//==============================================================================
// FUNCTION: GetXmlType
// PURPOSE   Converts an VARIANT type to an XML data type.

OPCUTILS_API Type GetXmlType(VARTYPE varType);

//==============================================================================
// FUNCTION: GetVarDate
// PURPOSE   Converts an XML DateTime type to an VARIANT DATE type.

OPCUTILS_API DATE GetVarDate(const DateTime& cDateTime);

//==============================================================================
// FUNCTION: GetXmlDateTime
// PURPOSE   Converts an VARIANT DATE type to an type XML DateTime.

OPCUTILS_API DateTime GetXmlDateTime(const DATE& cVarDate);

/*
//==============================================================================
// FUNCTION: XXX<VARIANT>
// PURPOSE   Defines conversion functions for COpcVariants.

template<> OPCUTILS_API void Init<VARIANT>(VARIANT& cValue);
template<> OPCUTILS_API void Clear<VARIANT>(VARIANT& cValue);
template<> OPCUTILS_API bool ReadXml<VARIANT>(IXMLDOMNode* ipNode, VARIANT& cValue);
template<> OPCUTILS_API bool WriteXml<VARIANT>(IXMLDOMNode* ipNode, const VARIANT& cValue);

//==============================================================================
// FUNCTION: XXX<COpcVariant>
// PURPOSE   Defines conversion functions for COpcVariants.

template<> OPCUTILS_API void Init<COpcVariant>(COpcVariant& cValue);
template<> OPCUTILS_API void Clear<COpcVariant>(COpcVariant& cValue);
template<> OPCUTILS_API bool ReadXml<COpcVariant>(IXMLDOMNode* ipNode, COpcVariant& cValue);
template<> OPCUTILS_API bool WriteXml<COpcVariant>(IXMLDOMNode* ipNode, const COpcVariant& cValue);
*/

}; // OpcXml

#endif // _COpcVariant_H_
