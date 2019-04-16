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

#include <float.h>
#include <limits.h>

#include "OpcXmlType.h"
#include "COpcTextReader.h"

using namespace OpcXml;

//==============================================================================
// Local Delcarations

#define MAX_VALUE_BUF_SIZE 1024

// numeric upper/lower bounds.
#define MIN_SBYTE        _I8_MIN
#define MAX_SBYTE        _I8_MAX
#define MAX_BYTE         _UI8_MAX
#define MIN_SHORT        _I16_MIN
#define MAX_SHORT        _I16_MAX
#define MAX_USHORT       _UI16_MAX
#define MIN_INT          _I32_MIN
#define MAX_INT          _I32_MAX
#define MAX_UINT         _UI32_MAX        
#define MIN_LONG         _I64_MIN
#define MAX_LONG         _I64_MAX
#define MAX_ULONG        _UI64_MAX      
#define MIN_FLOAT        FLT_MIN
#define MAX_FLOAT        FLT_MAX    
#define MIN_DOUBLE       DBL_MIN
#define MAX_DOUBLE       DBL_MAX    
#define MIN_DECIMAL      (_I64_MIN/10000)
#define MAX_DECIMAL      (_I64_MAX/10000)
#define MAX_DEC_FRACTION 9999
#define MIN_YEAR         1601
#define MAX_YEAR         9999
#define MIN_MONTH        1
#define MAX_MONTH        12
#define MIN_DAY          1
#define MAX_DAY          31
#define MIN_HOUR         0
#define MAX_HOUR         23
#define MIN_MINUTE       0
#define MAX_MINUTE       59
#define MIN_SECOND       0
#define MAX_SECOND       59
#define MIN_SEC_FRACTION 0
#define MAX_SEC_FRACTION 999

//==============================================================================
// Local Functions

// CharToValue
static UINT CharToValue(TCHAR tzChar, UINT uBase)
{
   static const LPCTSTR g_tsDigits = _T("0123456789ABCDEF");

   TCHAR tzDigit = (_istlower(tzChar))?_toupper(tzChar):tzChar;

   for (UINT ii = 0; ii < uBase; ii++)
   {
      if (tzDigit == g_tsDigits[ii])
      {
         return ii;
      }
   }

   return -1;
}

// ToUnsigned
static bool ToUnsigned(
    const COpcString& cString, 
    ULong&            nValue,
    ULong             nMax, 
    ULong             nMin, 
    UINT              uBase
)
{ 
    bool bResult = true;

    TRY
    {
        COpcText cText;
        COpcTextReader cReader(cString);

        // extract non-whitespace.
        cText.SetType(COpcText::NonWhitespace);
        cText.SetEofDelim();

        if (!cReader.GetNext(cText))
        {
            THROW_(bResult, false);
        }

        COpcString cValue = cText;

        nValue = 0;

        for (UINT ii = 0; ii < cValue.GetLength(); ii++)
        {
            UINT uDigit = CharToValue(cValue[ii], uBase);

            // invalid digit found.
            if (uDigit == -1) 
            {
                bResult = false;
                break;
            }

            // detect overflow
            if (nValue > nMax/uBase) THROW_(bResult, false);

            // shift result up by base.
            nValue *= uBase;

            // detect overflow
            if (nValue > nMax - uDigit) THROW_(bResult, false);

            // add digit.
            nValue += uDigit;
        }

        // detect underflow
        if (nMin > nValue) THROW_(bResult, false);
    }
    CATCH
    {
        nValue = 0;
    }

    return bResult;
}

// ToUnsigned
static bool ToUnsigned(
    const COpcString& cString, 
    ULong&            nValue,
    ULong             nMax, 
    ULong             nMin
)
{
    // extract base
    COpcText cText;
    COpcTextReader cReader(cString);

    UINT uBase = 10;

    cText.SetType(COpcText::Literal);
    cText.SetText(L"0x");
    cText.SetIgnoreCase();

    if (cReader.GetNext(cText))
    {
        uBase = 16;
    }

    // read unsigned value.
    return ToUnsigned(cReader.GetBuf(), nValue, nMax, nMin, uBase);
}

// ToSigned
static bool ToSigned(
    const COpcString& cString, 
    Long&             nValue,
    Long              nMax, 
    Long              nMin
)
{
    nValue = 0;
     
    // extract plus sign
    bool bSign = true;

    COpcText cText;
    COpcTextReader cReader(cString);

    cText.SetType(COpcText::Literal);
    cText.SetText(L"+");

    if (!cReader.GetNext(cText))
    {
        // extract minus sign
        cText.SetType(COpcText::Literal);
        cText.SetText(L"-");

        bSign = !cReader.GetNext(cText);
    }

    // read unsigned value.
    ULong uValue = 0;
    
    if (!ToUnsigned(cReader.GetBuf(), uValue, (bSign)?nMax:-nMin, 0, 10))
    {
        return false;
    }

    nValue = (Long)uValue;

    // apply sign.
    if (!bSign)
    {
        nValue = -nValue;
    }

    return true;
}

// ToReal
static bool ToReal(
    const COpcString& cString, 
    Double&           nValue, 
    Double            nMax, 
    Double            nMin
)
{
    bool bResult = true;

    TRY
    {
        // extract non-whitespace token.
        COpcText cText;
        COpcTextReader cReader(cString);

        cText.SetType(COpcText::NonWhitespace);
        cText.SetSkipLeading();

        if (!cReader.GetNext(cText))
        {
            THROW_(bResult, false);
        }

        // parse double with C runtime function.
        WCHAR* pzEnd = NULL;
        nValue = (Double)wcstod((LPCWSTR)(COpcString&)cText, &pzEnd);

        // check for error - all text must have been parsed.
        if (pzEnd == NULL || wcslen(pzEnd) != 0) 
        {
            THROW_(bResult, false);
        }

        // check limits.
        if (nValue > nMax && nValue < nMin) THROW_(bResult, false);
    }
    CATCH
    {
        nValue = 0;
    }

    return bResult;
}

// ToDate
static bool ToDate(
    const COpcString& cString, 
    WORD&             wYear,
    WORD&             wMonth,
    WORD&             wDay
)
{
    bool bResult = true;

    TRY
    {
        COpcText cText;
        COpcTextReader cReader(cString);

        ULong uValue = 0;

        // parse year field.
        cText.SetType(COpcText::Delimited);
        cText.SetDelims(L"-");

        if (!cReader.GetNext(cText)) THROW_(bResult, false);
        if (!ToUnsigned(cText, uValue, MAX_YEAR, MIN_YEAR, 10)) THROW_(bResult, false);
        wYear = (WORD)uValue;

        // parse month field.
        cText.SetType(COpcText::Delimited);
        cText.SetDelims(L"-");

        if (!cReader.GetNext(cText)) THROW_(bResult, false);
        if (!ToUnsigned(cText, uValue, MAX_MONTH, MIN_MONTH, 10)) THROW_(bResult, false);
        wMonth = (WORD)uValue;

        // parse day field.
        cText.SetType(COpcText::Delimited);
        cText.SetEofDelim();

        if (!cReader.GetNext(cText)) THROW_(bResult, false);
        if (!ToUnsigned(cText, uValue, MAX_DAY, MIN_DAY, 10)) THROW_(bResult, false);
        wDay = (WORD)uValue;
    }
    CATCH
    {
        wYear  = 0;
        wMonth = 0;
        wDay   = 0;
    }

    return bResult;
}

// ToTime
static bool ToTime(
    const COpcString& cString, 
    WORD&             wHour,
    WORD&             wMinute,
    WORD&             wSeconds,
    WORD&             wFraction
)
{
    bool bResult = true;

    TRY
    {
        COpcText cText;
        COpcTextReader cReader(cString);

        ULong uValue = 0;

        // parse hour field.
        cText.SetType(COpcText::Delimited);
        cText.SetDelims(L":");

        if (!cReader.GetNext(cText)) THROW_(bResult, false);
        if (!ToUnsigned(cText, uValue, MAX_HOUR, MIN_HOUR, 10)) THROW_(bResult, false);
        wHour = (WORD)uValue;

        // parse month field.
        cText.SetType(COpcText::Delimited);
        cText.SetDelims(L":");

        if (!cReader.GetNext(cText)) THROW_(bResult, false);
        if (!ToUnsigned(cText, uValue, MAX_MINUTE, MIN_MINUTE, 10)) THROW_(bResult, false);
        wMinute = (WORD)uValue;

        // parse seconds field.
        cText.SetType(COpcText::Delimited);
        cText.SetDelims(L".");
        cText.SetEofDelim();

        if (!cReader.GetNext(cText)) THROW_(bResult, false);
        if (!ToUnsigned(cText, uValue, MAX_SECOND, MIN_SECOND, 10)) THROW_(bResult, false);
        wSeconds = (WORD)uValue;

        // parse seconds fraction field.
        wFraction = 0;

        if (cText.GetDelimChar() == L'.')
        {
            cText.SetType(COpcText::Delimited);
            cText.SetEofDelim();

            if (!cReader.GetNext(cText)) THROW_(bResult, false);

            // preprocess text.
            COpcString cFraction = cText;

            // add trailing zeros.
            while (cFraction.GetLength() < 3) cFraction += _T("0");

            // truncate extra digits.
            if (cFraction.GetLength() > 3) cFraction = cFraction.SubStr(0,3);

            if (!ToUnsigned(cFraction, uValue, MAX_ULONG, 0, 10))
            {
                THROW_(bResult, false);
            }

            // result is in milliseconds.
            wFraction = (WORD)uValue;
        }
    }
    CATCH
    {
        wHour     = 0;
        wMinute   = 0;
        wSeconds  = 0;
        wFraction = 0;
    }

    return bResult;
}

// ToOffset
static bool ToOffset(
    const COpcString& cString, 
	bool              bNegative,
    SHORT&            sMinutes
)
{
    bool bResult = true;

    TRY
    {
        COpcText cText;
        COpcTextReader cReader(cString);

        ULong uValue = 0;

        // parse hour field.
        cText.SetType(COpcText::Delimited);
        cText.SetDelims(L":");

        if (!cReader.GetNext(cText)) THROW_(bResult, false);
        if (!ToUnsigned(cText, uValue, MAX_HOUR, MIN_HOUR, 10)) THROW_(bResult, false);
      
		sMinutes = (SHORT)uValue*60;

        // parse minute field.
        cText.SetType(COpcText::Delimited);
        cText.SetDelims(L".");
        cText.SetEofDelim();

        if (!cReader.GetNext(cText)) THROW_(bResult, false);
        if (!ToUnsigned(cText, uValue, MAX_MINUTE, MIN_MINUTE, 10)) THROW_(bResult, false);
        
		sMinutes += (SHORT)uValue;

		// add sign.
		if (bNegative)
		{
			sMinutes = -sMinutes;
		}
    }
    CATCH
    {
        sMinutes = 0;
    }

    return bResult;
}

//==============================================================================
// FUNCTION: Read<TYPE>
// PURPOSE   Explicit template implementations for common data types.

// Read
template<> bool OpcXml::Read(const COpcString& cText, SByte& cValue)
{ 
    Long lValue = 0;

    if (!ToSigned(cText, lValue, MAX_SBYTE, MIN_SBYTE))
    {
        cValue = 0;
        return false;
    }

    cValue = (SByte)lValue;
    return true;
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, Byte& cValue)
{ 
    ULong ulValue = 0;

    if (!ToUnsigned(cString, ulValue, MAX_BYTE, 0)) 
    {
        cValue = 0;
        return false;
    }

    cValue = (Byte)ulValue;
    return true;
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, Short& cValue)
{ 
    Long lValue = 0;

    if (!ToSigned(cString, lValue, MAX_SHORT, MIN_SHORT)) 
    {
        cValue = 0;
        return false;
    }

    cValue = (Short)lValue;
    return true;
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, UShort& cValue)
{ 
    ULong ulValue = 0;

    if (!ToUnsigned(cString, ulValue, MAX_USHORT, 0)) 
    {
        cValue = 0;
        return false;
    }

    cValue = (UShort)ulValue;
    return true;
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, Int& cValue)
{ 
    Long lValue = 0;

    if (!ToSigned(cString, lValue, MAX_INT, MIN_INT)) 
    {
        cValue = 0;
        return false;
    }

    cValue = (Int)lValue;
    return true;
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, UInt& cValue)
{ 
    ULong ulValue = 0;

    if (!ToUnsigned(cString, ulValue, MAX_UINT, 0)) 
    {
        cValue = 0;
        return false;
    }

    cValue = (UInt)ulValue;
    return true;
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, long& cValue)
{ 
    Long lValue = 0;

    if (!ToSigned(cString, lValue, MAX_INT, MIN_INT)) 
    {
        cValue = 0;
        return false;
    }

    cValue = (Int)lValue;
    return true;
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, unsigned long& cValue)
{ 
    ULong ulValue = 0;

    if (!ToUnsigned(cString, ulValue, MAX_UINT, 0)) 
    {
        cValue = 0;
        return false;
    }

    cValue = (UInt)ulValue;
    return true;
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, Long& cValue)
{ 
    return ToSigned(cString, cValue, MAX_LONG, MIN_LONG);
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, ULong& cValue)
{ 
    return ToUnsigned(cString, cValue, MAX_ULONG, 0);
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, Float& cValue)
{ 
    Double dblValue = 0;

    if (!ToReal(cString, dblValue, MAX_FLOAT, MIN_FLOAT)) 
    {
        cValue = 0;
        return false;
    }

    cValue = (Float)dblValue;
    return true;
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, Double& cValue)
{ 
    return ToReal(cString, cValue, MAX_DOUBLE, MIN_DOUBLE);
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, Decimal& cValue)
{
    bool bResult = true;

    TRY
    {
        COpcText cText;
        COpcTextReader cReader(cString);

        // parse whole integer portion.
        cText.SetType(COpcText::Delimited);
        cText.SetDelims(L".");
        cText.SetEofDelim();

        if (!cReader.GetNext(cText))
        {
            THROW_(bResult, false);
        }

        // convert to signed integer.
        Long nValue = 0;

        if (!ToSigned(cText, nValue, MAX_DECIMAL, MIN_DECIMAL))
        {
            THROW_(bResult, false);
        }

        cValue.int64 = nValue*10000;
        
        if (cText.GetDelimChar() == L'.')
        {
            // parse decimal portion.
            cText.SetType(COpcText::Delimited);
            cText.SetEofDelim();

            if (!cReader.GetNext(cText))
            {
                THROW_(bResult, false);
            }

            // convert to unsigned integer.
            ULong uValue = 0;

            if (!ToUnsigned(cText, uValue, MAX_DEC_FRACTION, 0, 10))
            {
                THROW_(bResult, false);
            }

            cValue.int64 += (Long)uValue;
        }
    }
    CATCH
    {
        cValue.int64 = 0;
    }

    return bResult;
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, DateTime& cValue)
{
    bool bResult = true;

    FILETIME cFileTime;

	// check for invalid date.
	if (cString.IsEmpty())
	{
		cValue = OpcMinDate();
		return true;
	}

    TRY
    {
        SYSTEMTIME cSystemTime; ZeroMemory(&cSystemTime, sizeof(cSystemTime));

        COpcText cText;
        COpcTextReader cReader(cString);

        // parse date fields.
        cText.SetType(COpcText::Delimited);
        cText.SetDelims(L"T");

        if (!cReader.GetNext(cText)) THROW_(bResult, false);

        if (!ToDate(cText, cSystemTime.wYear, cSystemTime.wMonth, cSystemTime.wDay))
        {
            THROW_(bResult, false);
        }

        // parse time fields.
        cText.SetType(COpcText::Delimited);
        cText.SetDelims(L"Z-+");
        cText.SetEofDelim();

        if (!cReader.GetNext(cText)) THROW_(bResult, false);

        bResult = ToTime(
            cText, 
            cSystemTime.wHour, 
            cSystemTime.wMinute, 
            cSystemTime.wSecond, 
            cSystemTime.wMilliseconds);

        if (!bResult)
        {
            THROW_(bResult, false);
        }

		// convert to a UTC file time.
		if (!SystemTimeToFileTime(&cSystemTime, &cFileTime))
		{
			THROW_(bResult, false);
		}

		if (cText.GetDelimChar() != _T('Z'))
		{
			// convert local system time to UTC file time.
			if (cText.GetEof())
			{
				FILETIME ftUtcTime;

				if (!OpcLocalTimeToUtcTime(cFileTime, ftUtcTime))
				{
					THROW_(bResult, false);
				}
				
				cFileTime = ftUtcTime;
			}

			// apply offset specified in the datetime string.
			else
			{
				bool bNegative = (cText.GetDelimChar() == _T('-'));

				// parse time fields.
				cText.SetType(COpcText::Delimited);
				cText.SetEofDelim();

				if (!cReader.GetNext(cText)) THROW_(bResult, false);

				SHORT sMinutes = 0;

				bResult = ToOffset(
					cText, 
					bNegative,
					sMinutes);

				if (!bResult)
				{
					THROW_(bResult, false);
				}

				// apply timezone offset.
				LONGLONG llTime = OpcToInt64(cFileTime);

				llTime -= ((LONGLONG)sMinutes)*60*10000000;

				cFileTime = OpcToFILETIME(llTime);	
			}
		}
			
	    cValue = cFileTime;
    }
    CATCH
    {
        ZeroMemory(&cFileTime, sizeof(cFileTime));
        cValue = cFileTime;
    }

    return bResult;
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, Boolean& cValue) 
{ 
    // check for an integer representation (any non-zero value is true).
    Long nValue = 0;

    if (Read(cString, nValue))
    {
        cValue = (nValue != 0);
        return true;
    }

    // check for alphabetic representation.
    COpcString cBoolean(cString);
    cBoolean = cBoolean.ToLower();

    // test for true value.
    if (cBoolean == _T("true")) 
    {
        cValue = true;
        return true;
    }

    // test for false value.
    if (cBoolean == _T("false")) 
    {
        cValue = false;
        return true;
    }

    return false; 
}

// Read
template<> bool OpcXml::Read(const COpcString& cString, String& cValue) 
{ 
    cValue = OpcStrDup((LPCWSTR)cString);
    return true; 
}

//==============================================================================
// FUNCTION: Write<TYPE>
// PURPOSE   Explicit template implementations for common data types.

// Write
template<> bool OpcXml::Write(const SByte& cValue, COpcString& cString) 
{ 
    WCHAR szBuf[MAX_VALUE_BUF_SIZE];
    swprintf(szBuf, L"%d", (Int)cValue);
    cString = szBuf;
    return true; 
}

// Write
template<> bool OpcXml::Write(const Byte& cValue, COpcString& cString)
{ 
    WCHAR szBuf[MAX_VALUE_BUF_SIZE];
    swprintf(szBuf, L"%u", (UInt)cValue);
    cString = szBuf;
    return true; 
}

// Write
template<> bool OpcXml::Write(const Short& cValue, COpcString& cString)
{ 
    WCHAR szBuf[MAX_VALUE_BUF_SIZE];
    swprintf(szBuf, L"%d", (Int)cValue);
    cString = szBuf;
    return true; 
}

// Write
template<> bool OpcXml::Write(const UShort& cValue, COpcString& cString)
{ 
    WCHAR szBuf[MAX_VALUE_BUF_SIZE];
    swprintf(szBuf, L"%u", (UInt)cValue);
    cString = szBuf;
    return true; 
}

// Write
template<> bool OpcXml::Write(const Int& cValue, COpcString& cString)
{ 
    WCHAR szBuf[MAX_VALUE_BUF_SIZE];
    swprintf(szBuf, L"%d", (Int)cValue);
    cString = szBuf;
    return true; 
}

// Write
template<> bool OpcXml::Write(const UInt& cValue, COpcString& cString)
{ 
    WCHAR szBuf[MAX_VALUE_BUF_SIZE];
    swprintf(szBuf, L"%u", (UInt)cValue);
    cString = szBuf;
    return true; 
}

// Write
template<> bool OpcXml::Write(const long& cValue, COpcString& cString)
{ 
    WCHAR szBuf[MAX_VALUE_BUF_SIZE];
    swprintf(szBuf, L"%d", (Int)cValue);
    cString = szBuf;
    return true; 
}

// Write
template<> bool OpcXml::Write(const unsigned long& cValue, COpcString& cString)
{ 
    WCHAR szBuf[MAX_VALUE_BUF_SIZE];
    swprintf(szBuf, L"%u", (UInt)cValue);
    cString = szBuf;
    return true; 
}

// Write
template<> bool OpcXml::Write(const Long& cValue, COpcString& cString)
{ 
    WCHAR szBuf[MAX_VALUE_BUF_SIZE];
    swprintf(szBuf, L"%I64d", cValue);
    cString = szBuf;
    return true; 
}

// Write
template<> bool OpcXml::Write(const ULong& cValue, COpcString& cString)
{ 
    WCHAR szBuf[MAX_VALUE_BUF_SIZE];
    swprintf(szBuf, L"%I64u", cValue);
    cString = szBuf;
    return true; 
}

// Write
template<> bool OpcXml::Write(const Float& cValue, COpcString& cString)
{ 
    WCHAR szBuf[MAX_VALUE_BUF_SIZE];
    swprintf(szBuf, L"%0.8g", (Double)cValue);
    cString = szBuf;
    return true; 
}

// Write
template<> bool OpcXml::Write(const Double& cValue, COpcString& cString)
{ 
    WCHAR szBuf[MAX_VALUE_BUF_SIZE];
    swprintf(szBuf, L"%0.16g", (Double)cValue);
    cString = szBuf;
    return true; 
}

// Write
template<> bool OpcXml::Write(const Decimal& cValue, COpcString& cString)
{  
    WCHAR szBuf[MAX_VALUE_BUF_SIZE];
    swprintf(szBuf, L"%0I64d.%0I64u", (cValue.int64/10000), (cValue.int64%10000));
    cString = szBuf;
    return true; 
}

// Write
template<> bool OpcXml::Write(const DateTime& cValue, COpcString& cString)
{
	// check for invalid date.
	if (cValue == OpcMinDate())
	{
		cString.Empty();
		return true;
	}

	// convert to xml dateTime representation.
    SYSTEMTIME cSystemTime;
    
    if (!FileTimeToSystemTime(&((FILETIME)cValue), &cSystemTime)) 
    {
        return false;
    }

    WCHAR szBuf[MAX_VALUE_BUF_SIZE];

    swprintf(
        szBuf, 
        L"%04hu-%02hu-%02huT%02hu:%02hu:%02hu.%03hu", 
        cSystemTime.wYear,
        cSystemTime.wMonth,
        cSystemTime.wDay,
        cSystemTime.wHour,
        cSystemTime.wMinute,
        cSystemTime.wSecond,
        cSystemTime.wMilliseconds);

    cString = szBuf;
    return true;
}

// Write
template<> bool OpcXml::Write(const Boolean& cValue, COpcString& cString)
{
    cString = (cValue)?_T("True"):_T("False");
    return true;
}

// Write
template<> bool OpcXml::Write(const String& cValue, COpcString& cString)
{
    // TBD - add any escape sequences.
    cString = cValue;
    return true;
}

//==============================================================================
// XXX<COpcString>

// Init
template<> void OpcXml::Init(COpcString& cValue) 
{ 
    cValue.Empty(); 
}

// Clear
template<> void OpcXml::Clear(COpcString& cValue) 
{ 
    cValue.Empty(); 
}

// Read
template<> bool OpcXml::Read(const COpcString& cText, COpcString& cValue) 
{ 
    cValue = cText; 
    return true; 
}

// Write
template<> bool OpcXml::Write(const COpcString& cValue, COpcString& cText) 
{ 
    cText = cValue; 
    return true; 
}

//==============================================================================
// XXX<GUID>

// Init
template<> void OpcXml::Init(GUID& cValue) 
{ 
    cValue = GUID_NULL; 
}

// Clear
template<> void OpcXml::Clear(GUID& cValue) 
{ 
    cValue = GUID_NULL; 
}

// Read
template<> bool OpcXml::Read(const COpcString& cText, GUID& cValue) 
{ 
    return cText.ToGuid(cValue);
}

// Write
template<> bool OpcXml::Write(const GUID& cValue, COpcString& cText) 
{ 
    cText.FromGuid(cValue);
    return true;
}
