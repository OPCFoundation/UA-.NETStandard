/* ========================================================================
 * Copyright (c) 2005-2016 The OPC Foundation, Inc. All rights reserved.
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
#include "COpcHdaTime.h"

using namespace System;

//==============================================================================
// Local Declarations

enum OpcHdaTimeBase
{
	OPCHDA_INVALID = -1,
	OPCHDA_NOW,
	OPCHDA_SECOND,
	OPCHDA_MINUTE,
	OPCHDA_HOUR,
	OPCHDA_DAY,
	OPCHDA_WEEK,
	OPCHDA_MONTH,
	OPCHDA_YEAR
};

struct OpcHdaTimeOffset
{
	int            iOffset;
	OpcHdaTimeBase eOffsetType;
};

struct OpcHdaTimeBaseMapping
{
	OpcHdaTimeBase eTimeBase;
	LPCTSTR        szBaseTime;
	LPCTSTR        szOffsetTime;
};

OpcHdaTimeBaseMapping g_pMappings[] =
{
	{ OPCHDA_NOW,     _T("NOW"),    NULL     },
	{ OPCHDA_YEAR,    _T("YEAR"),   _T("Y")  },
	{ OPCHDA_MONTH,   _T("MONTH"),  _T("MO") },
	{ OPCHDA_WEEK,    _T("WEEK"),   _T("W")  },
	{ OPCHDA_DAY,     _T("DAY"),    _T("D")  },
	{ OPCHDA_HOUR,    _T("HOUR"),   _T("H")  },
	{ OPCHDA_MINUTE,  _T("MINUTE"), _T("M")  },
	{ OPCHDA_SECOND,  _T("SECOND"), _T("S")  },
	{ OPCHDA_INVALID, NULL,         NULL     }
};

// GetTimeBase
static OpcHdaTimeBase GetTimeBase(LPCTSTR szBuffer, int& iIndex)
{
	// find relative time base.
	if (szBuffer != NULL)
	{
		for (UINT ii = 0; g_pMappings[ii].eTimeBase != OPCHDA_INVALID; ii++)
		{
			LPCTSTR szString = g_pMappings[ii].szBaseTime;
			
			if (szString != NULL)
			{
				if (_tcsncmp(szString, szBuffer + iIndex, _tcslen(szString)) == 0)
				{
					iIndex += _tcslen(szString);
					return g_pMappings[ii].eTimeBase;
				}
			}
		}
	}

	// not found.
	return OPCHDA_INVALID;
}

// GetTimeOffset
static OpcHdaTimeBase GetTimeOffset(LPCTSTR szBuffer, int& iIndex)
{
	// find relative time base.
	if (szBuffer != NULL)
	{
		for (UINT ii = 0; g_pMappings[ii].eTimeBase != OPCHDA_INVALID; ii++)
		{
			LPCTSTR szString = g_pMappings[ii].szOffsetTime;

			if (szString != NULL)
			{
				if (_tcsncmp(szString, szBuffer + iIndex, _tcslen(szString)) == 0)
				{
					iIndex += _tcslen(szString);
					return g_pMappings[ii].eTimeBase;
				}
			}
		}
	}

	// not found.
	return OPCHDA_INVALID;
}

//============================================================================
// OpcClearOffsets

static void OpcClearOffsets(COpcList<OpcHdaTimeOffset*>& cOffsets)
{
	while (cOffsets.GetCount() > 0)
	{
		OpcHdaTimeOffset* pOffset = cOffsets.RemoveHead();
		delete pOffset;
	}

	cOffsets.RemoveAll();
}

//============================================================================
// OpcParseOffsets

static bool OpcParseOffsets(COpcString& cBuffer, COpcList<OpcHdaTimeOffset*>& cOffsets)
{
	bool       bPositive  = true;
	int        iMagnitude = 0;
	COpcString cUnits     = "";
	int        iState     = 0;
	int        iIndex     = 0;

	// iState = 0 - looking for start of next offset field.
	// iState = 1 - looking for beginning of offset value.
	// iState = 2 - reading offset value.

	LPCTSTR szBuffer = cBuffer;

	while (iIndex < (int)cBuffer.GetLength())
	{
		// check for sign part of the offset field.
		if (szBuffer[iIndex] == _T('+') || szBuffer[iIndex] == _T('-'))
		{
			if (iState != 0)
			{
				OpcClearOffsets(cOffsets);
				return false;
			}
	
			bPositive = szBuffer[iIndex] == _T('+');
			iState    = 1;
			iIndex++;
		}

		// check for integer part of the offset field.
		else if (_istdigit(szBuffer[iIndex]) != 0)
		{
			if (iState == 0)
			{
				iState = 1;
			}

			iMagnitude *= 10;				
			iMagnitude += (int)(szBuffer[iIndex] - _T('0'));
			
			iState = 2;
			iIndex++;
		}

		// check for cUnits part of the offset field.
		else if (_istspace(szBuffer[iIndex]) == 0)
		{
			if (iState != 2)
			{
				OpcClearOffsets(cOffsets);
				return false;
			}

			// validate offset type.
			OpcHdaTimeBase eOffset = GetTimeOffset(cBuffer, iIndex);

			if (eOffset == OPCHDA_INVALID)
			{
				OpcClearOffsets(cOffsets);
				return false;
			}

			// create offset.
			OpcHdaTimeOffset* pOffset = new OpcHdaTimeOffset();
			
			pOffset->iOffset     = (bPositive)?iMagnitude:-iMagnitude;
			pOffset->eOffsetType = eOffset;

			cOffsets.AddTail(pOffset);
	
			// reset state variables.
			iMagnitude = 0;
			iState     = 0;
		}
		else
		{
			iIndex++;
		}
	}

	// check final iState.
	if (iState != 0)
	{
		OpcClearOffsets(cOffsets);
		return false;
	}

	return true;
}

//============================================================================
// OpcParseOffsets

static bool OpcApplyTimeOffset(SYSTEMTIME& stTime, OpcHdaTimeOffset& cOffset)
{
	LONGLONG llOffset = cOffset.iOffset;

	// convert offset to days or years depending on what it is.
	OpcHdaTimeBase eOffsetType = cOffset.eOffsetType;

	do
	{
		switch (eOffsetType)
		{
			case OPCHDA_SECOND:
			{
                LONGLONG iSecond = llOffset%60 + stTime.wSecond;
                LONGLONG iMinute = llOffset/60 + stTime.wMinute;

                if (iSecond >= 60)
                {
                    iSecond -= 60;
                    iMinute++;
                }

                if (iSecond < 0)
                {
                    iSecond += 60;
                    iMinute--;
                }

                stTime.wSecond = (WORD)iSecond;
                stTime.wMinute = 0;

				llOffset = iMinute;
				eOffsetType = OPCHDA_MINUTE;
				break;
			}

			case OPCHDA_MINUTE:
			{
                LONGLONG iMinute = llOffset%60 + stTime.wMinute;
                LONGLONG iHour = llOffset/60 + stTime.wHour;

                if (iMinute >= 60)
                {
                    iMinute -= 60;
                    iHour++;
                }

                if (iMinute < 0)
                {
                    iMinute += 60;
                    iHour--;
                }

                stTime.wMinute = (WORD)iMinute;
                stTime.wHour = 0;

				llOffset = iHour;
				eOffsetType = OPCHDA_HOUR;
				break;
			}

			case OPCHDA_HOUR:
			{
                LONGLONG iHour = llOffset%24 + stTime.wHour;
                LONGLONG iDay = llOffset/24 + stTime.wDay;

                if (iHour >= 24)
                {
                    iHour -= 24;
                    iDay++;
                }

                if (iHour < 0)
                {
                    iHour += 24;
                    iDay--;
                }

                stTime.wHour = (WORD)iHour;
                stTime.wDay = 1;
				
                llOffset = iDay;
				eOffsetType = OPCHDA_DAY;
				break;
			}

			case OPCHDA_DAY:
			{
				llOffset += stTime.wDay;
				eOffsetType = OPCHDA_DAY;
				break;
			}

			case OPCHDA_WEEK:
			{
				llOffset *= 7;
				llOffset += stTime.wDay;
				eOffsetType = OPCHDA_DAY;
				break;
			}
			
			case OPCHDA_MONTH:
			{			
                LONGLONG iMonth = llOffset%12 + stTime.wMonth;
                LONGLONG iYear = llOffset/12 + stTime.wYear;

                if (iMonth > 12)
                {
                    iMonth -= 12;
                    iYear++;
                }

                if (iMonth <= 0)
                {
                    iMonth += 12;
                    iYear--;
                }

                stTime.wMonth = (WORD)iMonth;
                stTime.wYear = 0;

				llOffset = iYear;
				eOffsetType = OPCHDA_YEAR;
				break;
			}

			case OPCHDA_YEAR:
			{			
				llOffset += stTime.wYear;
				eOffsetType = OPCHDA_YEAR;
				break;
			}
		}
	}
	while (eOffsetType != OPCHDA_DAY && eOffsetType != OPCHDA_YEAR);

	// convert offset in days to months/years.
	if (eOffsetType == OPCHDA_DAY)
	{
		// handle positive offsets.
		if (llOffset > 0)
		{
			LONGLONG llDays = OpcGetDaysInMonth(stTime.wYear, stTime.wMonth);

			while (llOffset > llDays)
			{
				llOffset -= llDays;

				stTime.wMonth++;

				if (stTime.wMonth > 12)
				{
					stTime.wYear++;
					stTime.wMonth = 1;
				}

				llDays = OpcGetDaysInMonth(stTime.wYear, stTime.wMonth);
			}
			
			stTime.wDay = (WORD)llOffset;
		}
		
		// handle negative offsets.
		else
		{
			LONGLONG llDays = 0;

			do
			{
				if (stTime.wMonth > 1)
				{
					stTime.wMonth--;
				}
				else
				{
					stTime.wYear--;
					stTime.wMonth = 12;
				}

				llDays = OpcGetDaysInMonth(stTime.wYear, stTime.wMonth);
				llOffset += llDays;
			}
			while (llOffset <= 0);

			stTime.wDay = (WORD)llOffset;
		}
	}

	// apply offset in years - must fix up days.
	else if (eOffsetType == OPCHDA_YEAR)
	{
		stTime.wYear = (WORD)llOffset;

		UINT uDays = OpcGetDaysInMonth(stTime.wYear, stTime.wMonth);

		if (uDays < stTime.wDay)
		{
			stTime.wDay = uDays;
		}
	}

	// check that year can be converted to a filetime.
	if (stTime.wYear < 1600 || stTime.wYear > 9999)
	{
		return false;
	}

	return true;
}

//============================================================================
// OpcHdaResolveTime

LONGLONG OpcHdaResolveTime(OPCHDA_TIME& cTime)
{
	if (!cTime.bString)
	{
		LONGLONG llAbsoluteTime = ::OpcToInt64(cTime.ftTime);

		// '0' for an absolute time means 1601/1/1 which is a meaningless date for any application
		// so changing it to 1601/1/1 + 100ns is not going to affect any results returned and it
		// ensures the value '0' means 'time not specified'.
		if (llAbsoluteTime == 0)
		{
			llAbsoluteTime = 1;
		}

		return llAbsoluteTime;
	}
	
	COpcString cBuffer = cTime.szTime;

	// remove leading and trailing whitespace
	cBuffer.Trim();

	// read time base.
	int iIndex = 0;

	OpcHdaTimeBase eBase = GetTimeBase(cBuffer, iIndex);

	if (eBase == OPCHDA_INVALID)
	{
		return 0;
	}

	// remove time base from string.
	cBuffer = cBuffer.SubStr(iIndex);
	cBuffer.Trim();
		
	// get time offsets.
	COpcList<OpcHdaTimeOffset*> cOffsets;

	if (!OpcParseOffsets(cBuffer, cOffsets))
	{
		return 0;
	}

	// get current local time.
	SYSTEMTIME stBase;
	GetSystemTime(&stBase);

	// adjust for base time.
	switch (eBase)
	{		
		// beginning of week is always midnight sunday.
		case OPCHDA_WEEK:
		{
			// check if sunday is in different month.
			if (stBase.wDay <= stBase.wDayOfWeek)
			{
				// check if sunday is in the same year.
				if (stBase.wMonth > 1)
				{
					stBase.wMonth -= 1;
				}

				// sunday is in a different year.
				else
				{
					stBase.wYear -= 1;
					stBase.wMonth = 12;
				}

				// add the number of days in the previous month.
				stBase.wDay += OpcGetDaysInMonth(stBase.wYear, stBase.wMonth);
			}

			// go back to the sunday.
			stBase.wDay -= stBase.wDayOfWeek;
			break;
		}

		// these case statements deliberately fall through to the next statement.
		case OPCHDA_YEAR:   { stBase.wMonth        = 1; }
		case OPCHDA_MONTH:  { stBase.wDay          = 1; }
		case OPCHDA_DAY:    { stBase.wHour         = 0; }
		case OPCHDA_HOUR:   { stBase.wMinute       = 0; }
		case OPCHDA_MINUTE: { stBase.wSecond       = 0; }
		case OPCHDA_SECOND: { stBase.wMilliseconds = 0; }

		// use the current time.
		default: { break; }
	}

	// apply offsets.
	LONGLONG llSeconds = 0;

	while (cOffsets.GetCount() > 0)
	{
		OpcHdaTimeOffset* pOffset = cOffsets.RemoveHead();

		bool bResult = OpcApplyTimeOffset(stBase, *pOffset);

		delete pOffset;

		if (!bResult)
		{
			return 0;
		}
	}
	
	FILETIME ftTime;
	SystemTimeToFileTime(&stBase, &ftTime);

	// return actual start time in time arguement.
	if (cTime.bString)
	{
		OpcFree(cTime.szTime);
		cTime.szTime = NULL;
	}

	cTime.bString = FALSE;
	cTime.ftTime  = ftTime;

	// convert to 64 bit integer value.
	return ::OpcToInt64(ftTime);
}
