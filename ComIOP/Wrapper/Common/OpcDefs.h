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

#ifndef _OpcDefs_H_
#define _OpcDefs_H_

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include <crtdbg.h>
#include <malloc.h>

//==============================================================================
// MACRO:   OPC_ASSERT
// PURPOSE: Halts program execution if condition is not met.

#ifdef _DEBUG
#define OPC_ASSERT(x) _ASSERTE(x)
#else
#define OPC_ASSERT(x)
#endif

//==============================================================================
// MACRO:   TRY, LEAVE. FINALLY
// PURPOSE: Macros to facilitate cleanup after error conditions.

#pragma warning(disable: 4102) 

#ifndef OPC_NO_EXCEPTIONS

#define TRY try 
#define CATCH catch (DWORD)
#define CATCH_FINALLY catch (DWORD) {} finally:
#define FINALLY finally:
#define THROW() throw (DWORD)0;
#define THROW_(xResult, xValue) { (xResult) = (xValue); throw (DWORD)0; }
#define GOTOFINALLY() goto finally;

#else

#define TRY {    
#define CATCH } goto finally; onerror:
#define CATCH_FINALLY onerror: finally:
#define FINALLY finally:
#define THROW() goto onerror;
#define THROW_(xResult, xValue) { (xResult) = (xValue); goto onerror; }
#define GOTOFINALLY() goto finally;

#endif

//==============================================================================
// FUNCTION: OpcAlloc
// PURPOSE:  Allocates a block of memory.

OPCUTILS_API void* OpcAlloc(size_t tSize);

//==============================================================================
// FUNCTION: OpcFree
// PURPOSE:  Frees a block of memory.

OPCUTILS_API void OpcFree(void* pBlock);

//==============================================================================
// MACRO:   OpcArrayAlloc
// PURPOSE: Allocates an array of simple data (i.e. not classes).

#define OpcArrayAlloc(xType,xLength) (xType*)OpcAlloc((xLength)*sizeof(xType))

//==============================================================================
// FUNCTION: OpcStrDup
// PURPOSE:  Duplicates a string.

OPCUTILS_API CHAR* OpcStrDup(LPCSTR szValue);
OPCUTILS_API WCHAR* OpcStrDup(LPCWSTR szValue);

//==============================================================================
// FUNCTION: OpcArrayDup
// PURPOSE:  Duplicates an array of types (bitwise copy must be valid).

#define OpcArrayDup(xCopy, xType, xLength, xValue) \
{ \
    UINT uLength = (xLength)*sizeof(xType); \
    xCopy = (xType*)OpcAlloc(uLength); \
 \
    if (xValue != NULL) memcpy(xCopy, xValue, uLength); \
    else memset(xCopy, 0, uLength); \
}

//==============================================================================
// MACRO:   OPC_CLASS_NEW_DELETE
// PURPOSE: Implements a class new and delete operators.

#define OPC_CLASS_NEW_DELETE() \
public: void* operator new(size_t nSize) {return OpcAlloc(nSize);} \
public: void operator delete(void* p) {OpcFree(p);}

#define OPC_CLASS_NEW_DELETE_EX(xCallType) \
xCallType void* operator new(size_t nSize) {return OpcAlloc(nSize);} \
xCallType void operator delete(void* p) {OpcFree(p);}

//==============================================================================
// MACRO:   OPC_CLASS_NEW_DELETE_ARRAY
// PURPOSE: Implements a class new and delete instance and array operators.

#define OPC_CLASS_NEW_DELETE_ARRAY() \
OPC_CLASS_NEW_DELETE() \
public: void* operator new[](size_t nSize) {return OpcAlloc(nSize);} \
public:  void operator delete[](void* p) {OpcFree(p);}

#define OPC_CLASS_NEW_DELETE_ARRAY_EX(xCallType) \
OPC_CLASS_NEW_DELETE_EX(xCallType) \
xCallType void* operator new[](size_t nSize) {return OpcAlloc(nSize);} \
xCallType void operator delete[](void* p) {OpcFree(p);}

//==============================================================================
// FUNCTION: OpcUtcNow
// PURPOSE:  Returns the current UTC time 

OPCUTILS_API FILETIME OpcUtcNow();

//==============================================================================
// FUNCTION: OpcMinDate
// PURPOSE:  Returns the minimum value for a file time.

OPCUTILS_API FILETIME OpcMinDate();

//==============================================================================
// FUNCTION: OpcToInt64
// PURPOSE:  Converts a FILETIME to a 64-bit integer.

OPCUTILS_API LONGLONG OpcToInt64(FILETIME ftTime);

//==============================================================================
// FUNCTION: OpcToFILETIME
// PURPOSE:  Converts a 64-bit integer to a FILETIME.

OPCUTILS_API FILETIME OpcToFILETIME(LONGLONG llTime);

//============================================================================
// FUNCTION: OpcLocalTimeToUtcTime
// PURPOSE:  Converts a local time to a UTC time.

OPCUTILS_API bool OpcLocalTimeToUtcTime(FILETIME& ftLocalTime, FILETIME& ftUtcTime);
OPCUTILS_API bool OpcLocalTimeToUtcTime(SYSTEMTIME& stLocalTime, FILETIME& ftUtcTime);

//============================================================================
// FUNCTION: OpcGetDaysInMonth
// PURPOSE:  Returns the number of days in a month.

OPCUTILS_API UINT OpcGetDaysInMonth(UINT uYear, UINT uMonth);

//==============================================================================
// FUNCTION: operator
// PURPOSE:  Implements various operators for FILETIME structures.

inline bool operator==(const FILETIME& a, const FILETIME& b) 
{
	return (memcmp(&a, &b, sizeof(FILETIME)) == 0); 
}

inline bool operator!=(const FILETIME& a, const FILETIME& b) 
{ 
	return (memcmp(&a, &b, sizeof(FILETIME)) != 0); 
}

inline bool operator<(const FILETIME& a, const FILETIME& b)
{
	return (a.dwHighDateTime != b.dwHighDateTime)?(a.dwHighDateTime < b.dwHighDateTime):(a.dwLowDateTime < b.dwLowDateTime);
}

inline bool operator<=(const FILETIME& a, const FILETIME& b)
{
	return (a.dwHighDateTime != b.dwHighDateTime)?(a.dwHighDateTime <= b.dwHighDateTime):(a.dwLowDateTime <= b.dwLowDateTime);
}

inline bool operator>(const FILETIME& a, const FILETIME& b)
{
	return (a.dwHighDateTime != b.dwHighDateTime)?(a.dwHighDateTime > b.dwHighDateTime):(a.dwLowDateTime > b.dwLowDateTime);
}

inline bool operator>=(const FILETIME& a, const FILETIME& b)
{
	return (a.dwHighDateTime != b.dwHighDateTime)?(a.dwHighDateTime >= b.dwHighDateTime):(a.dwLowDateTime >= b.dwLowDateTime);
}

//==============================================================================
// FUNCTION: operator
// PURPOSE:  Implements various operators for CY structures.

inline bool operator==(const CY& a, const CY& b) 
{
	return (a.int64 == b.int64); 
}

inline bool operator!=(const CY& a, const CY& b) 
{ 
	return (a.int64 != b.int64); 
}

inline bool operator<(const CY& a, const CY& b)
{
	return (a.int64 < b.int64); 
}

inline bool operator<=(const CY& a, const CY& b)
{
	return (a.int64 <= b.int64); 
}

inline bool operator>(const CY& a, const CY& b)
{
	return (a.int64 > b.int64); 
}

inline bool operator>=(const CY& a, const CY& b)
{
	return (a.int64 >= b.int64); 
}

//==============================================================================
// MACRO:    OPC_MASK_XXX
// PURPOSE:  Facilitate manipulation of bit masks.

#define OPC_MASK_ISSET(xValue, xMask) (((xValue) & (xMask)) == (xMask))
#define OPC_MASK_SET(xValue, xMask)   xValue |= (xMask)
#define OPC_MASK_UNSET(xValue, xMask) xValue &= ~(xMask)

#endif // _OpcDefs_H_
