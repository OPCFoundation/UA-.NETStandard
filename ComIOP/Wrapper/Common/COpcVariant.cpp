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
#include "COpcVariant.h"

#include "math.h"

using namespace OpcXml;

//==============================================================================
// Local Functions

// MakeByRef
static void MakeByRef(VARTYPE vtType, void* pData, VARIANT& cValue)
{
    OpcVariantInit(&cValue);

    // return a reference to array memory.
    cValue.vt = (vtType & VT_TYPEMASK) | VT_BYREF;

    switch (vtType & VT_TYPEMASK)
    {
        case VT_I1:      { cValue.pcVal    = (CHAR*)pData;         break; }
        case VT_UI1:     { cValue.pbVal    = (BYTE*)pData;         break; }
        case VT_I2:      { cValue.piVal    = (SHORT*)pData;        break; }
        case VT_UI2:     { cValue.puiVal   = (USHORT*)pData;       break; }
        case VT_I4:      { cValue.plVal    = (LONG*)pData;         break; }
        case VT_UI4:     { cValue.pulVal   = (ULONG*)pData;        break; }
        case VT_I8:      { cValue.pllVal   = (LONGLONG*)pData;     break; }
        case VT_UI8:     { cValue.pullVal  = (ULONGLONG*)pData;    break; }
        case VT_R4:      { cValue.pfltVal  = (FLOAT*)pData;        break; }
        case VT_R8:      { cValue.pdblVal  = (DOUBLE*)pData;       break; }
        case VT_CY:      { cValue.pcyVal   = (CY*)pData;           break; }
        case VT_DATE:    { cValue.pdate    = (DATE*)pData;         break; }  
        case VT_BSTR:    { cValue.pbstrVal = (BSTR*)pData;         break; }  
        case VT_BOOL:    { cValue.pboolVal = (VARIANT_BOOL*)pData; break; }
        case VT_VARIANT: { cValue.pvarVal  = (VARIANT*)pData;      break; }
    }
}

// GetValuePtr
static void* GetValuePtr(VARIANT& cValue)
{
    switch (cValue.vt)
    {
        case VT_I1:      { return &cValue.cVal;    }
        case VT_UI1:     { return &cValue.bVal;    }
        case VT_I2:      { return &cValue.iVal;    }
        case VT_UI2:     { return &cValue.uiVal;   }
        case VT_I4:      { return &cValue.lVal;    }
        case VT_UI4:     { return &cValue.ulVal;   }
        case VT_I8:      { return &cValue.llVal;   }
        case VT_UI8:     { return &cValue.ullVal;  }
        case VT_R4:      { return &cValue.fltVal;  }
        case VT_R8:      { return &cValue.dblVal;  }
        case VT_CY:      { return &cValue.cyVal;   }
        case VT_DATE:    { return &cValue.date;    }  
        case VT_BSTR:    { return &cValue.bstrVal; }  
        case VT_BOOL:    { return &cValue.boolVal; }
    }

    return NULL;
}

//============================================================================
// GetUtcTime
// 
// These functions were provided by the because they preserve the milliseconds 
// in the conversion between FILETIME and DATE and they are more efficient.

inline DATE FileTimeToDate(FILETIME *pft)
{
	return (double)((double)(*(__int64 *)pft) / 8.64e11) - (double)(363 + (1899 - 1601) * 365 + (24 + 24 + 24));
}

inline void FileTimeToDate(FILETIME *pft, DATE *pdate)
{
	*pdate = FileTimeToDate(pft);
}

inline FILETIME DateToFileTime(DATE *pdate)
{
	__int64 temp = (__int64)((*pdate + (double)(363 + (1899 - 1601) * 365 + (24 + 24 + 24))) * 8.64e11);
	return *(FILETIME *)&temp;
}

inline void DateToFileTime(DATE *pdate, FILETIME *pft)
{
	*pft = DateToFileTime(pdate);
}

//==============================================================================
// Free Functions

// OpcVariantInit
void OpcVariantInit(VARIANT* pValue)
{
    if (pValue != NULL)
    {
        memset(pValue, 0, sizeof(pValue));
    }
}

// OpcVariantClear
void OpcVariantClear(VARIANT* pValue)
{
    if (pValue == NULL)
    {
        return;
    }

    VariantClear(pValue);
    memset(pValue, 0, sizeof(VARIANT));
}

// OpcVariantCopy
void OpcVariantCopy(VARIANT* pDst, const VARIANT* pSrc)
{
    if (pDst == NULL)
    {
        return;
    }

    OpcVariantClear(pDst);

    if (pSrc == NULL)
    {
        return;
    }

    VariantCopy(pDst, (VARIANT*)pSrc);
}

struct TOpcXmlVarType 
{
    Type    eType;
    VARTYPE varType;
};

// XML data type to VARIANT type mapping table.
static const TOpcXmlVarType g_pVarTypes[] =
{
    { XML_SBYTE,    VT_I1      },
    { XML_BYTE,     VT_UI1     },
    { XML_SHORT,    VT_I2      },
    { XML_USHORT,   VT_UI2     },
    { XML_INT,      VT_I4      },
    { XML_UINT,     VT_UI4     },
    { XML_LONG,     VT_I8      },
    { XML_ULONG,    VT_UI8     },
    { XML_FLOAT,    VT_R4      },
    { XML_DOUBLE,   VT_R8      },
    { XML_DECIMAL,  VT_CY      },
    { XML_DATETIME, VT_DATE    },
    { XML_BOOLEAN,  VT_BOOL    },
    { XML_STRING,   VT_BSTR    },
    { XML_ANY_TYPE, VT_VARIANT },
    { XML_EMPTY,    VT_EMPTY   }     
};

// GetVarType
VARTYPE OpcXml::GetVarType(Type eType)
{
    for (int ii = 0; g_pVarTypes[ii].varType != VT_EMPTY; ii++)
    {
        if (eType == g_pVarTypes[ii].eType)
        {
            return g_pVarTypes[ii].varType;
        }
    }

    return VT_EMPTY;
}

// GetXmlType
Type OpcXml::GetXmlType(VARTYPE varType)
{
    for (int ii = 0; g_pVarTypes[ii].varType != VT_EMPTY; ii++)
    {
        if (varType == g_pVarTypes[ii].varType)
        {
            return g_pVarTypes[ii].eType;
        }
    }

    return XML_EMPTY;
}

// GetVarDate
DATE OpcXml::GetVarDate(const DateTime& cValue)
{	
	DATE dblDate = FileTimeToDate((FILETIME*)&cValue);

	dblDate = floor(dblDate*1e9 + 0.5)/1e9;

	return dblDate;
}

// GetXmlDateTime
DateTime OpcXml::GetXmlDateTime(const DATE& cDate)
{
	return DateToFileTime((DATE*)&cDate);
}

//==============================================================================
// COpcVariant

// Clear
void COpcVariant::Clear() 
{     
    OpcVariantClear(&m_cValue);
}

// Assignment
COpcVariant& COpcVariant::operator=(bool cValue) 
{ 
    Clear(); m_cValue.vt = VT_BOOL; m_cValue.boolVal = (cValue)?VARIANT_TRUE:VARIANT_FALSE; return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(char cValue)
{ 
    Clear(); m_cValue.vt = VT_I1; m_cValue.cVal = cValue; return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(unsigned char cValue)
{ 
    Clear(); m_cValue.vt = VT_UI1; m_cValue.bVal = cValue; return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(short cValue)
{ 
    Clear(); m_cValue.vt = VT_I2; m_cValue.iVal = cValue; return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(unsigned short cValue)
{ 
    Clear(); m_cValue.vt = VT_UI2; m_cValue.uiVal = cValue; return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(int cValue)
{ 
    Clear(); m_cValue.vt = VT_I4; m_cValue.lVal = cValue; return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(unsigned int cValue)
{ 
    Clear(); m_cValue.vt = VT_UI4; m_cValue.ulVal = cValue; return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(long cValue)
{ 
    Clear(); m_cValue.vt = VT_I4; m_cValue.lVal = cValue; return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(unsigned long cValue)
{ 
    Clear(); m_cValue.vt = VT_UI4; m_cValue.ulVal = cValue; return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(__int64 cValue)
{ 
    Clear(); m_cValue.vt = VT_I8; m_cValue.llVal = cValue; return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(unsigned __int64 cValue)
{ 
    Clear(); m_cValue.vt = VT_UI8; m_cValue.ullVal = cValue; return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(float cValue)
{ 
    Clear(); m_cValue.vt = VT_R4; m_cValue.fltVal = cValue; return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(double cValue)
{ 
    Clear(); m_cValue.vt = VT_R8; m_cValue.dblVal = cValue; return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(CY cValue)
{ 
    Clear(); m_cValue.vt = VT_CY; m_cValue.cyVal = cValue; return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(FILETIME cValue)
{ 
    Clear(); m_cValue.vt = VT_DATE; m_cValue.date = GetVarDate((DateTime)cValue); return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(const COpcString& cValue)
{ 
    Clear(); m_cValue.vt = VT_BSTR; m_cValue.bstrVal = SysAllocString((LPCWSTR)cValue); return *this;
}

// Assignment
COpcVariant& COpcVariant::operator=(const VARIANT& cValue)
{ 
    Clear(); 
    OpcVariantCopy(&m_cValue, &cValue);

    return *this;
}

COpcVariant& COpcVariant::operator=(const COpcVariant& cValue) 
{ 
    if (&cValue == this)
    {
        return *this;
    }

    Clear(); 
    OpcVariantCopy(&m_cValue, &cValue.m_cValue);
    
    return *this; 
}

// Cast
COpcVariant::operator bool() const
{
    OPC_ASSERT(m_cValue.vt == VT_BOOL); return (m_cValue.boolVal)?true:false;
}

// Cast
COpcVariant::operator char() const
{
    OPC_ASSERT(m_cValue.vt == VT_I1); return m_cValue.cVal;
}

// Cast
COpcVariant::operator unsigned char() const
{
    OPC_ASSERT(m_cValue.vt == VT_UI1); return m_cValue.bVal;
}

// Cast
COpcVariant::operator short() const
{
    OPC_ASSERT(m_cValue.vt == VT_I2); return m_cValue.iVal;
}

// Cast
COpcVariant::operator unsigned short() const
{
    OPC_ASSERT(m_cValue.vt == VT_UI2); return m_cValue.uiVal;
}

// Cast
COpcVariant::operator int() const
{
    OPC_ASSERT(m_cValue.vt == VT_I4); return m_cValue.lVal;
}

// Cast
COpcVariant::operator unsigned int() const
{
    OPC_ASSERT(m_cValue.vt == VT_UI4); return m_cValue.ulVal;
}

// Cast
COpcVariant::operator long() const
{
    OPC_ASSERT(m_cValue.vt == VT_I4); return m_cValue.lVal;
}

// Cast
COpcVariant::operator unsigned long() const
{
    OPC_ASSERT(m_cValue.vt == VT_UI4); return m_cValue.ulVal;
}

// Cast
COpcVariant::operator __int64() const
{
    OPC_ASSERT(m_cValue.vt == VT_I8); return m_cValue.llVal;
}

// Cast
COpcVariant::operator unsigned __int64() const
{
    OPC_ASSERT(m_cValue.vt == VT_UI8); return m_cValue.ullVal;
}

// Cast
COpcVariant::operator float() const
{
    OPC_ASSERT(m_cValue.vt == VT_R4); return m_cValue.fltVal;
}

// Cast
COpcVariant::operator double() const
{
    OPC_ASSERT(m_cValue.vt == VT_R8); return m_cValue.dblVal;
}

// Cast
COpcVariant::operator CY() const
{
    OPC_ASSERT(m_cValue.vt == VT_CY); return m_cValue.cyVal;
}

// Cast
COpcVariant::operator FILETIME() const
{
    OPC_ASSERT(m_cValue.vt == VT_DATE); return (FILETIME)GetXmlDateTime(m_cValue.date);
}

// Cast
COpcVariant::operator COpcString() const
{
    OPC_ASSERT(m_cValue.vt == VT_BSTR); return (LPCWSTR)m_cValue.bstrVal;
}

// IsEqual
bool COpcVariant::IsEqual(const VARIANT& cValue1, const VARIANT& cValue2)
{
    if (cValue1.vt != cValue2.vt) return false;

    if (cValue1.vt & VT_ARRAY)
    {
        return COpcSafeArray::IsEqual(cValue1, cValue2);
    }

    switch (cValue1.vt)
    {
        case VT_EMPTY: return (cValue1.vt          == cValue2.vt);
        case VT_I1:    return (cValue1.cVal        == cValue2.cVal);
        case VT_UI1:   return (cValue1.bVal        == cValue2.bVal);
        case VT_I2:    return (cValue1.iVal        == cValue2.iVal);
        case VT_UI2:   return (cValue1.uiVal       == cValue2.uiVal);
        case VT_I4:    return (cValue1.lVal        == cValue2.lVal);
        case VT_UI4:   return (cValue1.ulVal       == cValue2.ulVal);
        case VT_I8:    return (cValue1.llVal       == cValue2.llVal);
        case VT_UI8:   return (cValue1.ullVal      == cValue2.ullVal);
        case VT_R4:    return (cValue1.fltVal      == cValue2.fltVal);
        case VT_R8:    return (cValue1.dblVal      == cValue2.dblVal);
        case VT_CY:    return (cValue1.cyVal.int64 == cValue2.cyVal.int64);
        case VT_DATE:  return (cValue1.date        == cValue2.date);
        case VT_BOOL:  return (cValue1.boolVal     == cValue2.boolVal);   
     
        case VT_BSTR:  
		{
			if (cValue1.bstrVal != NULL && cValue2.bstrVal != NULL)
			{
				return (wcscmp(cValue1.bstrVal, cValue2.bstrVal) == 0);
			}

			return (cValue1.bstrVal == cValue2.bstrVal);
		}
    }

    return false;
}
  
// ChangeType
HRESULT COpcVariant::ChangeType(
    VARIANT&       cDst, 
    const VARIANT& cSrc, 
    LCID           lcid, 
    VARTYPE        vtType
)
{
    if (vtType == VT_EMPTY || vtType == VT_VARIANT || cSrc.vt == vtType)
    {
        OpcVariantCopy(&cDst, &cSrc);
        return S_OK;
    }

    OpcVariantInit(&cDst);

	// check for conversion from an array.
    if ((cSrc.vt & VT_ARRAY) != 0)
    {
        return COpcSafeArray::ChangeType(cDst, cSrc, lcid, vtType);
    }

	// check for conversion from a byref variant containing an array.
	if (cSrc.vt == (VT_BYREF | VT_VARIANT) && (cSrc.pvarVal->vt & VT_ARRAY) != 0)
	{
        return COpcSafeArray::ChangeType(cDst, *cSrc.pvarVal, lcid, vtType);
	}

	// convert value.     
    HRESULT hResult = VariantChangeTypeEx(&cDst, (VARIANT*)&cSrc, lcid, VARIANT_NOVALUEPROP, vtType);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

    // check for incorrect conversion from signed to unsigned types.
    switch (vtType)
    {
        case VT_UI1:
        case VT_UI2:
        case VT_UI4:
        case VT_UI8:
        {
            switch (cSrc.vt)
            { 
                case VT_I1: { if (cSrc.cVal < 0) return DISP_E_OVERFLOW; break; }
                case VT_I2: { if (cSrc.iVal < 0) return DISP_E_OVERFLOW; break; }
                case VT_I4: { if (cSrc.lVal < 0) return DISP_E_OVERFLOW; break; }
                case VT_I8: { if (cSrc.lVal < 0) return DISP_E_OVERFLOW; break; }
            }

            break;
        }
    }

    // check for incorrect conversion from unsigned to signed types.
    switch (cSrc.vt)
    {
        case VT_UI1:
        case VT_UI2:
        case VT_UI4:
        case VT_UI8:
        {
            switch (cDst.vt)
            { 
                case VT_I1: { if (cDst.cVal < 0) return DISP_E_OVERFLOW; break; }
                case VT_I2: { if (cDst.iVal < 0) return DISP_E_OVERFLOW; break; }
                case VT_I4: { if (cDst.lVal < 0) return DISP_E_OVERFLOW; break; }
                case VT_I8: { if (cDst.lVal < 0) return DISP_E_OVERFLOW; break; }
            }

            break;
        }
    }

    return S_OK;
}

//==============================================================================
// COpcSafeArray

// Constructor
COpcSafeArray::COpcSafeArray(VARIANT& cValue) : m_cValue(cValue), m_uLocks(0) 
{ 
    OPC_ASSERT(m_cValue.vt == VT_EMPTY  || m_cValue.vt & VT_ARRAY); 
}

// Destructor
COpcSafeArray::~COpcSafeArray()
{
    if (m_cValue.vt != VT_EMPTY)
    {
        // release all locks.
        for (;m_uLocks > 0; m_uLocks--)
        {    
            HRESULT hResult = SafeArrayUnlock(m_cValue.parray);    
            OPC_ASSERT(SUCCEEDED(hResult));
        }
    }
}

// Alloc
void COpcSafeArray::Alloc(VARTYPE vtType, UINT uLength)
{
    OpcVariantClear(&m_cValue);

    SAFEARRAYBOUND cBound;
    
    cBound.lLbound   = 0;
    cBound.cElements = uLength;

    m_cValue.vt     = vtType | VT_ARRAY;
    m_cValue.parray = SafeArrayCreate(vtType, 1, &cBound);

    OPC_ASSERT(m_cValue.parray != NULL);
}

// Lock
void COpcSafeArray::Lock()
{
    if (m_cValue.vt != VT_EMPTY)
    {
        m_uLocks++;
        HRESULT hResult = SafeArrayLock(m_cValue.parray);    
        OPC_ASSERT(SUCCEEDED(hResult));
    }
}

// Unlock
void COpcSafeArray::Unlock()
{
    if (m_cValue.vt != VT_EMPTY)
    {
        m_uLocks--;
        HRESULT hResult = SafeArrayUnlock(m_cValue.parray);    
        OPC_ASSERT(SUCCEEDED(hResult));
    }
}

// GetLength
UINT COpcSafeArray::GetLength() const
{
    LONG lLength = 0;

    if (m_cValue.vt != VT_EMPTY)
    {
		HRESULT hResult = SafeArrayGetUBound(m_cValue.parray, 1, &lLength);
        OPC_ASSERT(SUCCEEDED(hResult));

        return (UINT)(lLength+1);
	}

	return 0;
}

// GetData
void* COpcSafeArray::GetData() const
{
    void* pData = NULL;

    if (m_cValue.vt != VT_EMPTY)
    {   
		HRESULT hResult = SafeArrayAccessData(m_cValue.parray, &pData);
        OPC_ASSERT(SUCCEEDED(hResult));

        SafeArrayUnaccessData(m_cValue.parray);
	}

	return pData;
}

// GetElemType
VARTYPE COpcSafeArray::GetElemType() const
{
    VARTYPE vtType = VT_EMPTY;
    
    if (m_cValue.vt != VT_EMPTY)
    {
        HRESULT hResult = SafeArrayGetVartype(m_cValue.parray, &vtType);
        OPC_ASSERT(SUCCEEDED(hResult));
    }

	return vtType;
}

// GetElemSize
UINT COpcSafeArray::GetElemSize() const
{
    if (m_cValue.vt != VT_EMPTY)
    {
        return SafeArrayGetElemsize(m_cValue.parray);
    }

    return 0;
}

// GetElem
void* COpcSafeArray::GetElem(UINT uIndex)
{
    void* pData = NULL;
    
    if (m_cValue.vt != VT_EMPTY)
    {
        HRESULT hResult = SafeArrayPtrOfIndex(m_cValue.parray, (LONG*)&uIndex, (void**)&pData);
        OPC_ASSERT(SUCCEEDED(hResult));
    }

    return pData;
}

// SetElem
void COpcSafeArray::SetElem(UINT uIndex, void* pElem)
{
    if (pElem == NULL) return;

    void* pData = NULL;
    
    if (m_cValue.vt != VT_EMPTY)
    {
        ULONG uSize = SafeArrayGetElemsize(m_cValue.parray);
        BYTE* pData = (BYTE*)GetData();

        memcpy(pData + uSize*uIndex, pElem, uSize);
    }
}

// IsEqual
bool COpcSafeArray::IsEqual(const VARIANT& cValue1, const VARIANT& cValue2)
{
    if (cValue1.vt != cValue2.vt) return false;

    COpcSafeArray cArray1((VARIANT&)cValue1);
    COpcSafeArray cArray2((VARIANT&)cValue2);

    // check if the length differs.
    if (cArray1.GetLength() != cArray2.GetLength()) return false;

    // compare each element,
    UINT uLength = cArray1.GetLength();

    cArray1.Lock();
    cArray2.Lock();

    bool bIsEqual = true;

    for (UINT ii = 0; ii < uLength; ii++)
    {
        void* pElem1 = cArray1.GetElem(ii);
        void* pElem2 = cArray2.GetElem(ii);

        switch (cValue1.vt & VT_TYPEMASK)
        {
            default:         { bIsEqual = false; }

            case VT_I1:      { bIsEqual = (*((char*)pElem1)             == *((char*)pElem2));              break; }
            case VT_UI1:     { bIsEqual = (*((unsigned char*)pElem1)    == *((unsigned char*)pElem2));     break; }
            case VT_I2:      { bIsEqual = (*((short*)pElem1)            == *((short*)pElem2));             break; }
            case VT_UI2:     { bIsEqual = (*((unsigned short*)pElem1)   == *((unsigned short*)pElem2));    break; }
            case VT_I4:      { bIsEqual = (*((long*)pElem1)             == *((long*)pElem2));              break; }
            case VT_UI4:     { bIsEqual = (*((unsigned long*)pElem1)    == *((unsigned long*)pElem2));     break; }
            case VT_I8:      { bIsEqual = (*((__int64*)pElem1)          == *((__int64*)pElem2));           break; }
            case VT_UI8:     { bIsEqual = (*((unsigned __int64*)pElem1) == *((unsigned __int64*)pElem2));  break; }
            case VT_R4:      { bIsEqual = (*((float*)pElem1)            == *((float*)pElem2));             break; }
            case VT_R8:      { bIsEqual = (*((double*)pElem1)           == *((double*)pElem2));            break; }
            case VT_CY:      { bIsEqual = (((CY*)pElem1)->int64         == ((CY*)pElem2)->int64);          break; }
            case VT_DATE:    { bIsEqual = (*((DATE*)pElem1)             == *((DATE*)pElem2));              break; }
            case VT_BOOL:    { bIsEqual = (*((VARIANT_BOOL*)pElem1)     == *((VARIANT_BOOL*)pElem2));      break; }

            case VT_BSTR:    { bIsEqual = (wcscmp(*((BSTR*)pElem1), *((BSTR*)pElem2)) == 0); break; }
            
            case VT_VARIANT: { bIsEqual = COpcVariant::IsEqual(*((VARIANT*)pElem1), *((VARIANT*)pElem2)); break; }
        }  

        if (!bIsEqual)
        {
            break;
        }
    }

    cArray1.Unlock();
    cArray2.Unlock();

    return bIsEqual;
}

// ChangeType
HRESULT COpcSafeArray::ChangeType(VARIANT& cDst, const VARIANT& cSrc, LCID lcid, VARTYPE vtType)
{
    if (((cSrc.vt & VT_ARRAY) == 0) || ((vtType & VT_ARRAY) == 0 && vtType != VT_BSTR))
	{
		return DISP_E_TYPEMISMATCH;
	}

	HRESULT hResult = S_OK;

	COpcSafeArray cSrcArr((VARIANT&)cSrc);
	cSrcArr.Lock();

	UINT uLength = cSrcArr.GetLength();

	if (vtType != VT_BSTR)
	{
		COpcSafeArray cDstArr((VARIANT&)cDst);

		// allocate new array.
		cDstArr.Alloc(vtType & VT_TYPEMASK, uLength);

		cDstArr.Lock();

		for (UINT ii = 0; ii < uLength; ii++)
		{
			VARIANT cSrcElem; OpcVariantInit(&cSrcElem);
			VARIANT cDstElem; OpcVariantInit(&cDstElem);
	        
			MakeByRef(cSrc.vt & VT_TYPEMASK, cSrcArr.GetElem(ii), cSrcElem);
	        
			hResult = COpcVariant::ChangeType(cDstElem, cSrcElem, lcid, vtType & VT_TYPEMASK); 

			if (FAILED(hResult))
			{
				hResult = DISP_E_OVERFLOW;
				break;
			}

			if ((vtType & VT_TYPEMASK) == VT_VARIANT)
			{
				cDstArr.SetElem(ii, &cDstElem);
			}
			else
			{
				cDstArr.SetElem(ii, GetValuePtr(cDstElem));
			}
		}

		cDstArr.Unlock();
	}
	else
	{
		COpcString cDstStr;

		cDstStr += _T("{");

		for (UINT ii = 0; ii < uLength; ii++)
		{
			VARIANT cDstElem; OpcVariantInit(&cDstElem);
			VARIANT cSrcElem; OpcVariantInit(&cSrcElem);
	        
			MakeByRef(cSrc.vt & VT_TYPEMASK, cSrcArr.GetElem(ii), cSrcElem);
	        
			hResult = COpcVariant::ChangeType(cDstElem, cSrcElem, lcid, VT_BSTR); 

			if (FAILED(hResult))
			{
				hResult = DISP_E_OVERFLOW;
				break;
			}

			cDstStr += (LPCWSTR)cDstElem.bstrVal;

			if (ii < uLength-1)
			{
				cDstStr += _T(" | ");
			}
		}

		cDstStr += _T("}");

		cDst.vt      = VT_BSTR;
		cDst.bstrVal = SysAllocString((LPCWSTR)cDstStr);
	}
	
	cSrcArr.Unlock();

    if (FAILED(hResult))
    {
        OpcVariantClear(&cDst);
    }

    return hResult;
}

// OpcReadVariant
HRESULT OpcReadVariant(bool& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_BOOL);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = (vDst.boolVal)?true:false;
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(char& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_I1);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = vDst.cVal;
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(unsigned char& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_UI1);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = vDst.bVal;
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(short& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_I2);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = vDst.iVal;
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(unsigned short& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_UI2);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = vDst.uiVal;
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(int& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_I4);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = vDst.lVal;
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(unsigned int& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_UI4);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = vDst.lVal;
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(long& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_I4);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = vDst.lVal;
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(unsigned long& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_UI4);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = vDst.lVal;
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(__int64& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_I8);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = vDst.lVal;
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(unsigned __int64& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_UI8);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = vDst.lVal;
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(float& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_R4);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = vDst.fltVal;
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(double& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_R8);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = vDst.dblVal;
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(CY& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_CY);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = vDst.cyVal;
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(FILETIME& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_DATE);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = OpcXml::GetXmlDateTime(vDst.date);
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(COpcString& cDst, const VARIANT& cSrc)
{
	VARIANT vDst; VariantInit(&vDst);

    HRESULT hResult = VariantChangeType(&vDst, (VARIANT*)&cSrc, VARIANT_NOVALUEPROP, VT_BSTR);
    
    if (FAILED(hResult))
    {
        return hResult;
    }

	cDst = vDst.bstrVal;
	VariantClear(&vDst);
	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(COpcStringList& cDst, const VARIANT& cSrc)
{
	cDst.RemoveAll();

	if (cSrc.vt != (VT_ARRAY | VT_BSTR))
	{
		return DISP_E_TYPEMISMATCH;
	}

	COpcSafeArray cArray((VARIANT&)cSrc);

	cArray.Lock();

	UINT uLength = cArray.GetLength();
	BSTR* pData = (BSTR*)cArray.GetData();

	for (UINT ii = 0; ii < uLength; ii++)
	{
		cDst.AddTail(pData[ii]);
	}

	cArray.Unlock();

	return S_OK;
}

// OpcReadVariant
HRESULT OpcReadVariant(COpcStringArray& cDst, const VARIANT& cSrc)
{
	cDst.RemoveAll();

	if (cSrc.vt != (VT_ARRAY | VT_BSTR))
	{
		return DISP_E_TYPEMISMATCH;
	}

	COpcSafeArray cArray((VARIANT&)cSrc);

	cArray.Lock();

	UINT uLength = cArray.GetLength();
	BSTR* pData = (BSTR*)cArray.GetData();
	
	cDst.SetSize(uLength);

	for (UINT ii = 0; ii < uLength; ii++)
	{
		cDst[ii] = pData[ii];
	}

	cArray.Unlock();

	return S_OK;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, bool cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_BOOL;
	cDst.boolVal = cSrc;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, char cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_I1;
	cDst.cVal = cSrc;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, unsigned char cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_UI1;
	cDst.bVal = cSrc;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, short cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_I2;
	cDst.iVal = cSrc;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, unsigned short cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_UI2;
	cDst.uiVal = cSrc;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, int cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_I4;
	cDst.lVal = cSrc;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, unsigned int cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_UI4;
	cDst.ulVal = cSrc;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, long cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_I4;
	cDst.lVal = cSrc;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, unsigned long cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_UI4;
	cDst.ulVal = cSrc;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, __int64 cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_I8;
	cDst.llVal = cSrc;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, unsigned __int64 cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_UI8;
	cDst.ullVal = cSrc;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, float cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_R4;
	cDst.fltVal = cSrc;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, double cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_R8;
	cDst.dblVal = cSrc;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, CY cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_CY;
	cDst.cyVal = cSrc;
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, FILETIME& cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_DATE;
	cDst.date = OpcXml::GetVarDate(cSrc);
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, LPWSTR cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_BSTR;
	cDst.bstrVal = SysAllocString(cSrc);
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, const COpcString& cSrc)
{
	VariantClear(&cDst);
	cDst.vt = VT_BSTR;
	cDst.bstrVal = SysAllocString((LPCWSTR)cSrc);
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, const COpcStringList& cSrc)
{
	VariantClear(&cDst);

	UINT uLength = cSrc.GetCount();

	if (uLength > 0)
	{
		COpcSafeArray cArray(cDst);

		cArray.Alloc(VT_BSTR, uLength);
		cArray.Lock();

		BSTR* pData = (BSTR*)cArray.GetData();

		OPC_POS pos = cSrc.GetHeadPosition();

		for (UINT ii = 0; pos != NULL; ii++)
		{
			pData[ii] = SysAllocString((LPCWSTR)cSrc.GetNext(pos));
		}

		cArray.Unlock();
	}
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, const COpcStringArray& cSrc)
{
	VariantClear(&cDst);

	UINT uLength = cSrc.GetSize();

	if (uLength > 0)
	{
		COpcSafeArray cArray(cDst);

		cArray.Alloc(VT_BSTR, uLength);
		cArray.Lock();

		BSTR* pData = (BSTR*)cArray.GetData();

		for (UINT ii = 0; ii < uLength; ii++)
		{
			pData[ii] = SysAllocString((LPCWSTR)cSrc[ii]);
		}

		cArray.Unlock();
	}
}

// OpcWriteVariant
void OpcWriteVariant(VARIANT& cDst, const LPCWSTR* pszStrings)
{
	VariantClear(&cDst);

	if (pszStrings == NULL)
	{
		return;
	}

	for (UINT uLength = 0; pszStrings[uLength] != NULL; uLength++);

	if (uLength > 0)
	{
		COpcSafeArray cArray(cDst);

		cArray.Alloc(VT_BSTR, uLength);
		cArray.Lock();

		BSTR* pData = (BSTR*)cArray.GetData();

		for (UINT ii = 0; ii < uLength; ii++)
		{
			pData[ii] = SysAllocString(pszStrings[ii]);
		}

		cArray.Unlock();
	}
}
