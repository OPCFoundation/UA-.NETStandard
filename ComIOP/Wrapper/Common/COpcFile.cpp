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
#include "COpcFile.h"

//==============================================================================
// COpcFile

// Constructor
COpcFile::COpcFile()
{
	m_hFile     = INVALID_HANDLE_VALUE;
	m_hMapping  = NULL;
	m_pView     = NULL;
	m_bReadOnly = false;
}
        
// Destructor
COpcFile::~COpcFile()
{
	Close();
}

// Create
bool COpcFile::Create(const COpcString& cFileName)
{
	// check if file has aleady been opened.
	if (m_hFile != INVALID_HANDLE_VALUE)
	{
		return false;
	}

	// create a new file.
	m_hFile = CreateFile(
		cFileName,
		GENERIC_READ | GENERIC_WRITE,
		NULL,
		NULL,
		CREATE_ALWAYS,
		FILE_ATTRIBUTE_NORMAL,
		NULL
	);

	// check for error.
	if (m_hFile == INVALID_HANDLE_VALUE)
	{
		return false;
	}

	return true;
}

// Open
bool COpcFile::Open(const COpcString& cFileName, bool bReadOnly)
{
	// check if file has aleady been opened.
	if (m_hFile != INVALID_HANDLE_VALUE)
	{
		return false;
	}

	m_bReadOnly = bReadOnly;

	// create a new file.
	m_hFile = CreateFile(
		cFileName,
		(bReadOnly)?GENERIC_READ:(GENERIC_READ | GENERIC_WRITE),
		NULL,
		NULL,
		OPEN_EXISTING,
		FILE_ATTRIBUTE_NORMAL,
		NULL
	);

	// check for error.
	if (m_hFile == INVALID_HANDLE_VALUE)
	{
		return false;
	}

	return true;
}

// Close
void COpcFile::Close()
{
	if (m_hFile != INVALID_HANDLE_VALUE)
	{
		UnmapViewOfFile(m_pView);
		CloseHandle(m_hMapping);
		CloseHandle(m_hFile);

		m_hFile    = INVALID_HANDLE_VALUE;
		m_hMapping = NULL;
		m_pView    = NULL;
	}
}

// Read
UINT COpcFile::Read(BYTE* pBuffer, UINT uSize)
{
	// check if file has been opened.
	if (m_hFile == INVALID_HANDLE_VALUE)
	{
		return 0;
	}

	// intialize buffer.
	memset(pBuffer, 0, uSize);

	// read into buffer.
	DWORD dwBytesRead = 0;

	BOOL bResult = ReadFile(
		m_hFile,
		pBuffer,
		uSize,
		&dwBytesRead,
		NULL
	);

	// check for error.
	if (!bResult)
	{
		return 0;
	}

	// return number of bytes read.
	return (UINT)dwBytesRead;
}

// Write
UINT COpcFile::Write(BYTE* pBuffer, UINT uSize)
{
	// check if file has been opened.
	if (m_hFile == INVALID_HANDLE_VALUE)
	{
		return 0;
	}

	// write from buffer.
	DWORD dwBytesWritten = 0;

	BOOL bResult = WriteFile(
		m_hFile,
		pBuffer,
		uSize,
		&dwBytesWritten,
		NULL
	);

	// check for error.
	if (!bResult)
	{
		return 0;
	}

	// return number of bytes read.
	return (UINT)dwBytesWritten;
}

// GetFileSize
UINT COpcFile::GetFileSize()
{
	return (UINT)::GetFileSize(m_hFile, NULL);
}

// GetLastModified
FILETIME COpcFile::GetLastModified()
{
	FILETIME ftLastModified = { 0, 0 };
	::GetFileTime(m_hFile, NULL, NULL, &ftLastModified);
	return ftLastModified;
}

// GetMemoryMapping
BYTE* COpcFile::GetMemoryMapping()
{
	// check if the mapping already exists.
	if (m_hMapping != NULL && m_pView != NULL)
	{
		return m_pView;
	}

	// create the file mapping.
	m_hMapping = CreateFileMapping(
		m_hFile,
		NULL,
		(m_bReadOnly)?PAGE_READONLY:PAGE_READWRITE,
		NULL,
		NULL,
		NULL
	);

	if (m_hMapping == NULL)
	{
		return NULL;
	}

	// create a view of the mapping in memory.
	m_pView = (BYTE*)MapViewOfFile(
		m_hMapping,
		(m_bReadOnly)?FILE_MAP_READ:FILE_MAP_WRITE,
		0,
		0,
		0
	);

	if (m_pView == NULL)
	{
		return NULL;
	}

	// return a pointer to the memory view of the file.
	return m_pView;
}
