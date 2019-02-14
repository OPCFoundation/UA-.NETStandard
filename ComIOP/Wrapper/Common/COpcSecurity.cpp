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
#include "COpcSecurity.h"

#define OPCTRY(x) try{x;} catch(...) {}
#define OPCASSERT(expr) _ASSERTE(expr)

//==============================================================================
// COpcSecurity

COpcSecurity::COpcSecurity()
{
	m_pSD = NULL;
	m_pOwner = NULL;
	m_pGroup = NULL;
	m_pDACL = NULL;
	m_pSACL= NULL;
}

COpcSecurity::~COpcSecurity()
{
	if (m_pSD)
		delete m_pSD;
	if (m_pOwner)
		free(m_pOwner);
	if (m_pGroup)
		free(m_pGroup);
	if (m_pDACL)
		free(m_pDACL);
	if (m_pSACL)
		free(m_pSACL);
}

HRESULT COpcSecurity::Initialize()
{
	if (m_pSD)
	{
		delete m_pSD;
		m_pSD = NULL;
	}
	if (m_pOwner)
	{
		free(m_pOwner);
		m_pOwner = NULL;
	}
	if (m_pGroup)
	{
		free(m_pGroup);
		m_pGroup = NULL;
	}
	if (m_pDACL)
	{
		free(m_pDACL);
		m_pDACL = NULL;
	}
	if (m_pSACL)
	{
		free(m_pSACL);
		m_pSACL = NULL;
	}

	OPCTRY(m_pSD = new SECURITY_DESCRIPTOR);
	if (m_pSD == NULL)
		return E_OUTOFMEMORY;

	if (!InitializeSecurityDescriptor(m_pSD, SECURITY_DESCRIPTOR_REVISION))
	{
		HRESULT hr = HRESULT_FROM_WIN32(GetLastError());
		delete m_pSD;
		m_pSD = NULL;
		OPCASSERT(FALSE);
		return hr;
	}
	// Set the DACL to allow EVERYONE
	SetSecurityDescriptorDacl(m_pSD, TRUE, NULL, FALSE);
	return S_OK;
}

HRESULT COpcSecurity::InitializeFromProcessToken(BOOL bDefaulted)
{
	PSID pUserSid = NULL;
	PSID pGroupSid = NULL;
	HRESULT hr;

	Initialize();
	hr = GetProcessSids(&pUserSid, &pGroupSid);
	if (SUCCEEDED(hr))
	{
		hr = SetOwner(pUserSid, bDefaulted);
		if (SUCCEEDED(hr))
			hr = SetGroup(pGroupSid, bDefaulted);
	}
	if (pUserSid != NULL)
		free(pUserSid);
	if (pGroupSid != NULL)
		free(pGroupSid);
	return hr;
}

HRESULT COpcSecurity::InitializeFromThreadToken(BOOL bDefaulted, BOOL bRevertToProcessToken)
{
	PSID pUserSid = NULL;
	PSID pGroupSid = NULL;
	HRESULT hr;

	Initialize();
	hr = GetThreadSids(&pUserSid, &pGroupSid);
	if (HRESULT_CODE(hr) == ERROR_NO_TOKEN && bRevertToProcessToken)
		hr = GetProcessSids(&pUserSid, &pGroupSid);
	if (SUCCEEDED(hr))
	{
		hr = SetOwner(pUserSid, bDefaulted);
		if (SUCCEEDED(hr))
			hr = SetGroup(pGroupSid, bDefaulted);
	}
	if (pUserSid != NULL)
		free(pUserSid);
	if (pGroupSid != NULL)
		free(pGroupSid);
	return hr;
}

HRESULT COpcSecurity::SetOwner(PSID pOwnerSid, BOOL bDefaulted)
{
	OPCASSERT(m_pSD);

	// Mark the SD as having no owner
	if (!SetSecurityDescriptorOwner(m_pSD, NULL, bDefaulted))
	{
		HRESULT hr = HRESULT_FROM_WIN32(GetLastError());
		OPCASSERT(FALSE);
		return hr;
	}

	if (m_pOwner)
	{
		free(m_pOwner);
		m_pOwner = NULL;
	}

	// If they asked for no owner don't do the copy
	if (pOwnerSid == NULL)
		return S_OK;

	// Make a copy of the Sid for the return value
	DWORD dwSize = GetLengthSid(pOwnerSid);

	m_pOwner = (PSID) malloc(dwSize);
	if (m_pOwner == NULL)
		return E_OUTOFMEMORY;
	if (!CopySid(dwSize, m_pOwner, pOwnerSid))
	{
		HRESULT hr = HRESULT_FROM_WIN32(GetLastError());
		OPCASSERT(FALSE);
		free(m_pOwner);
		m_pOwner = NULL;
		return hr;
	}

	OPCASSERT(IsValidSid(m_pOwner));

	if (!SetSecurityDescriptorOwner(m_pSD, m_pOwner, bDefaulted))
	{
		HRESULT hr = HRESULT_FROM_WIN32(GetLastError());
		OPCASSERT(FALSE);
		free(m_pOwner);
		m_pOwner = NULL;
		return hr;
	}

	return S_OK;
}

HRESULT COpcSecurity::SetGroup(PSID pGroupSid, BOOL bDefaulted)
{
	OPCASSERT(m_pSD);

	// Mark the SD as having no Group
	if (!SetSecurityDescriptorGroup(m_pSD, NULL, bDefaulted))
	{
		HRESULT hr = HRESULT_FROM_WIN32(GetLastError());
		OPCASSERT(FALSE);
		return hr;
	}

	if (m_pGroup)
	{
		free(m_pGroup);
		m_pGroup = NULL;
	}

	// If they asked for no Group don't do the copy
	if (pGroupSid == NULL)
		return S_OK;

	// Make a copy of the Sid for the return value
	DWORD dwSize = GetLengthSid(pGroupSid);

	m_pGroup = (PSID) malloc(dwSize);
	if (m_pGroup == NULL)
		return E_OUTOFMEMORY;
	if (!CopySid(dwSize, m_pGroup, pGroupSid))
	{
		HRESULT hr = HRESULT_FROM_WIN32(GetLastError());
		OPCASSERT(FALSE);
		free(m_pGroup);
		m_pGroup = NULL;
		return hr;
	}

	OPCASSERT(IsValidSid(m_pGroup));

	if (!SetSecurityDescriptorGroup(m_pSD, m_pGroup, bDefaulted))
	{
		HRESULT hr = HRESULT_FROM_WIN32(GetLastError());
		OPCASSERT(FALSE);
		free(m_pGroup);
		m_pGroup = NULL;
		return hr;
	}

	return S_OK;
}

HRESULT COpcSecurity::Allow(LPCTSTR pszPrincipal, DWORD dwAccessMask)
{
	HRESULT hr = AddAccessAllowedACEToACL(&m_pDACL, pszPrincipal, dwAccessMask);
	if (SUCCEEDED(hr))
		SetSecurityDescriptorDacl(m_pSD, TRUE, m_pDACL, FALSE);
	return hr;
}

HRESULT COpcSecurity::Deny(LPCTSTR pszPrincipal, DWORD dwAccessMask)
{
	HRESULT hr = AddAccessDeniedACEToACL(&m_pDACL, pszPrincipal, dwAccessMask);
	if (SUCCEEDED(hr))
		SetSecurityDescriptorDacl(m_pSD, TRUE, m_pDACL, FALSE);
	return hr;
}

HRESULT COpcSecurity::Revoke(LPCTSTR pszPrincipal)
{
	HRESULT hr = RemovePrincipalFromACL(m_pDACL, pszPrincipal);
	if (SUCCEEDED(hr))
		SetSecurityDescriptorDacl(m_pSD, TRUE, m_pDACL, FALSE);
	return hr;
}

HRESULT COpcSecurity::GetProcessSids(PSID* ppUserSid, PSID* ppGroupSid)
{
	BOOL bRes;
	HRESULT hr;
	HANDLE hToken = NULL;
	if (ppUserSid)
		*ppUserSid = NULL;
	if (ppGroupSid)
		*ppGroupSid = NULL;
	bRes = OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &hToken);
	if (!bRes)
	{
		// Couldn't open process token
		hr = HRESULT_FROM_WIN32(GetLastError());
		OPCASSERT(FALSE);
		return hr;
	}
	hr = GetTokenSids(hToken, ppUserSid, ppGroupSid);
	CloseHandle(hToken);
	return hr;
}

HRESULT COpcSecurity::GetThreadSids(PSID* ppUserSid, PSID* ppGroupSid, BOOL bOpenAsSelf)
{
	BOOL bRes;
	HRESULT hr;
	HANDLE hToken = NULL;
	if (ppUserSid)
		*ppUserSid = NULL;
	if (ppGroupSid)
		*ppGroupSid = NULL;
	bRes = OpenThreadToken(GetCurrentThread(), TOKEN_QUERY, bOpenAsSelf, &hToken);
	if (!bRes)
	{
		// Couldn't open thread token
		hr = HRESULT_FROM_WIN32(GetLastError());
		return hr;
	}
	hr = GetTokenSids(hToken, ppUserSid, ppGroupSid);
	CloseHandle(hToken);
	return hr;
}

HRESULT COpcSecurity::GetTokenSids(HANDLE hToken, PSID* ppUserSid, PSID* ppGroupSid)
{
	DWORD dwSize;
	HRESULT hr;
	PTOKEN_USER ptkUser = NULL;
	PTOKEN_PRIMARY_GROUP ptkGroup = NULL;

	if (ppUserSid)
		*ppUserSid = NULL;
	if (ppGroupSid)
		*ppGroupSid = NULL;

	if (ppUserSid)
	{
		// Get length required for TokenUser by specifying buffer length of 0
		GetTokenInformation(hToken, TokenUser, NULL, 0, &dwSize);
		hr = GetLastError();
		if (hr != ERROR_INSUFFICIENT_BUFFER)
		{
			// Expected ERROR_INSUFFICIENT_BUFFER
			OPCASSERT(FALSE);
			hr = HRESULT_FROM_WIN32(hr);
			goto failed;
		}

		ptkUser = (TOKEN_USER*) malloc(dwSize);
		if (ptkUser == NULL)
		{
			hr = E_OUTOFMEMORY;
			goto failed;
		}
		// Get Sid of process token.
		if (!GetTokenInformation(hToken, TokenUser, ptkUser, dwSize, &dwSize))
		{
			// Couldn't get user info
			hr = HRESULT_FROM_WIN32(GetLastError());
			OPCASSERT(FALSE);
			goto failed;
		}

		// Make a copy of the Sid for the return value
		dwSize = GetLengthSid(ptkUser->User.Sid);

		PSID pSid;
		pSid = (PSID) malloc(dwSize);
		if (pSid == NULL)
		{
			hr = E_OUTOFMEMORY;
			goto failed;
		}
		if (!CopySid(dwSize, pSid, ptkUser->User.Sid))
		{
			hr = HRESULT_FROM_WIN32(GetLastError());
			OPCASSERT(FALSE);
			goto failed;
		}

		OPCASSERT(IsValidSid(pSid));
		*ppUserSid = pSid;
		free(ptkUser);
	}
	if (ppGroupSid)
	{
		// Get length required for TokenPrimaryGroup by specifying buffer length of 0
		GetTokenInformation(hToken, TokenPrimaryGroup, NULL, 0, &dwSize);
		hr = GetLastError();
		if (hr != ERROR_INSUFFICIENT_BUFFER)
		{
			// Expected ERROR_INSUFFICIENT_BUFFER
			OPCASSERT(FALSE);
			hr = HRESULT_FROM_WIN32(hr);
			goto failed;
		}

		ptkGroup = (TOKEN_PRIMARY_GROUP*) malloc(dwSize);
		if (ptkGroup == NULL)
		{
			hr = E_OUTOFMEMORY;
			goto failed;
		}
		// Get Sid of process token.
		if (!GetTokenInformation(hToken, TokenPrimaryGroup, ptkGroup, dwSize, &dwSize))
		{
			// Couldn't get user info
			hr = HRESULT_FROM_WIN32(GetLastError());
			OPCASSERT(FALSE);
			goto failed;
		}

		// Make a copy of the Sid for the return value
		dwSize = GetLengthSid(ptkGroup->PrimaryGroup);

		PSID pSid;
		pSid = (PSID) malloc(dwSize);
		if (pSid == NULL)
		{
			hr = E_OUTOFMEMORY;
			goto failed;
		}
		if (!CopySid(dwSize, pSid, ptkGroup->PrimaryGroup))
		{
			hr = HRESULT_FROM_WIN32(GetLastError());
			OPCASSERT(FALSE);
			goto failed;
		}

		OPCASSERT(IsValidSid(pSid));

		*ppGroupSid = pSid;
		free(ptkGroup);
	}

	return S_OK;

failed:
	if (ptkUser)
		free(ptkUser);
	if (ptkGroup)
		free (ptkGroup);
	return hr;
}


HRESULT COpcSecurity::GetCurrentUserSID(PSID *ppSid)
{
	HANDLE tkHandle;

	if (OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &tkHandle))
	{
		TOKEN_USER *tkUser;
		DWORD tkSize;
		DWORD sidLength;

		// Call to get size information for alloc
		GetTokenInformation(tkHandle, TokenUser, NULL, 0, &tkSize);
		tkUser = (TOKEN_USER *) malloc(tkSize);
		if (tkUser == NULL)
			return E_OUTOFMEMORY;

		// Now make the real call
		if (GetTokenInformation(tkHandle, TokenUser, tkUser, tkSize, &tkSize))
		{
			sidLength = GetLengthSid(tkUser->User.Sid);
			*ppSid = (PSID) malloc(sidLength);
			if (*ppSid == NULL)
				return E_OUTOFMEMORY;

			memcpy(*ppSid, tkUser->User.Sid, sidLength);
			CloseHandle(tkHandle);

			free(tkUser);
			return S_OK;
		}
		else
		{
			free(tkUser);
			return HRESULT_FROM_WIN32(GetLastError());
		}
	}
	return HRESULT_FROM_WIN32(GetLastError());
}


HRESULT COpcSecurity::GetPrincipalSID(LPCTSTR pszPrincipal, PSID *ppSid)
{
	HRESULT hr;
	LPTSTR pszRefDomain = NULL;
	DWORD dwDomainSize = 0;
	DWORD dwSidSize = 0;
	SID_NAME_USE snu;

	// Call to get size info for alloc
	LookupAccountName(NULL, pszPrincipal, *ppSid, &dwSidSize, pszRefDomain, &dwDomainSize, &snu);

	hr = GetLastError();
	if (hr != ERROR_INSUFFICIENT_BUFFER)
		return HRESULT_FROM_WIN32(hr);

	OPCTRY(pszRefDomain = new TCHAR[dwDomainSize]);
	if (pszRefDomain == NULL)
		return E_OUTOFMEMORY;

	*ppSid = (PSID) malloc(dwSidSize);
	if (*ppSid != NULL)
	{
		if (!LookupAccountName(NULL, pszPrincipal, *ppSid, &dwSidSize, pszRefDomain, &dwDomainSize, &snu))
		{
			free(*ppSid);
			*ppSid = NULL;
			delete[] pszRefDomain;
			return HRESULT_FROM_WIN32(GetLastError());
		}
		delete[] pszRefDomain;
		return S_OK;
	}
	delete[] pszRefDomain;
	return E_OUTOFMEMORY;
}


HRESULT COpcSecurity::Attach(PSECURITY_DESCRIPTOR pSelfRelativeSD)
{
	PACL    pDACL = NULL;
	PACL    pSACL = NULL;
	BOOL    bDACLPresent, bSACLPresent;
	BOOL    bDefaulted;
	PACL    m_pDACL = NULL;
	ACCESS_ALLOWED_ACE* pACE;
	HRESULT hr;
	PSID    pUserSid;
	PSID    pGroupSid;

	hr = Initialize();
	if(FAILED(hr))
		return hr;

	// get the existing DACL.
	if (!GetSecurityDescriptorDacl(pSelfRelativeSD, &bDACLPresent, &pDACL, &bDefaulted))
		goto failed;

	if (bDACLPresent)
	{
		if (pDACL)
		{
			// allocate new DACL.
			m_pDACL = (PACL) malloc(pDACL->AclSize);
			if (m_pDACL == NULL)
			{
				hr = E_OUTOFMEMORY;
				goto failedMemory;
			}

			// initialize the DACL
			if (!InitializeAcl(m_pDACL, pDACL->AclSize, ACL_REVISION))
				goto failed;

			// copy the ACES
			for (int i = 0; i < pDACL->AceCount; i++)
			{
				if (!GetAce(pDACL, i, (void **)&pACE))
					goto failed;

				if (!AddAccessAllowedAce(m_pDACL, ACL_REVISION, pACE->Mask, (PSID)&(pACE->SidStart)))
					goto failed;
			}

			if (!IsValidAcl(m_pDACL))
				goto failed;
		}

		// set the DACL
		if (!SetSecurityDescriptorDacl(m_pSD, m_pDACL ? TRUE : FALSE, m_pDACL, bDefaulted))
			goto failed;
	}

	// get the existing SACL.
	if (!GetSecurityDescriptorSacl(pSelfRelativeSD, &bSACLPresent, &pSACL, &bDefaulted))
		goto failed;

	if (bSACLPresent)
	{
		if (pSACL)
		{
			// allocate new SACL.
			m_pSACL = (PACL) malloc(pSACL->AclSize);
			if (m_pSACL == NULL)
			{
				hr = E_OUTOFMEMORY;
				goto failedMemory;
			}

			// initialize the SACL
			if (!InitializeAcl(m_pSACL, pSACL->AclSize, ACL_REVISION))
				goto failed;

			// copy the ACES
			for (int i = 0; i < pSACL->AceCount; i++)
			{
				if (!GetAce(pSACL, i, (void **)&pACE))
					goto failed;

				if (!AddAccessAllowedAce(m_pSACL, ACL_REVISION, pACE->Mask, (PSID)&(pACE->SidStart)))
					goto failed;
			}

			if (!IsValidAcl(m_pSACL))
				goto failed;
		}

		// set the SACL
		if (!SetSecurityDescriptorSacl(m_pSD, m_pSACL ? TRUE : FALSE, m_pSACL, bDefaulted))
			goto failed;
	}

	if (!GetSecurityDescriptorOwner(m_pSD, &pUserSid, &bDefaulted))
		goto failed;

	if (FAILED(SetOwner(pUserSid, bDefaulted)))
		goto failed;

	if (!GetSecurityDescriptorGroup(m_pSD, &pGroupSid, &bDefaulted))
		goto failed;

	if (FAILED(SetGroup(pGroupSid, bDefaulted)))
		goto failed;

	if (!IsValidSecurityDescriptor(m_pSD))
		goto failed;

	return hr;

failed:
	hr = HRESULT_FROM_WIN32(hr);

failedMemory:
	if (m_pDACL)
	{
		free(m_pDACL);
		m_pDACL = NULL;
	}
	if (m_pSD)
	{
		free(m_pSD);
		m_pSD = NULL;
	}
	return hr;
}

HRESULT COpcSecurity::AttachObject(HANDLE hObject)
{
	HRESULT hr;
	DWORD dwSize = 0;
	PSECURITY_DESCRIPTOR pSD = NULL;

	GetKernelObjectSecurity(hObject, OWNER_SECURITY_INFORMATION | GROUP_SECURITY_INFORMATION |
		DACL_SECURITY_INFORMATION, pSD, 0, &dwSize);

	hr = GetLastError();
	if (hr != ERROR_INSUFFICIENT_BUFFER)
		return HRESULT_FROM_WIN32(hr);

	pSD = (PSECURITY_DESCRIPTOR) malloc(dwSize);
	if (pSD == NULL)
		return E_OUTOFMEMORY;

	if (!GetKernelObjectSecurity(hObject, OWNER_SECURITY_INFORMATION | GROUP_SECURITY_INFORMATION |
		DACL_SECURITY_INFORMATION, pSD, dwSize, &dwSize))
	{
		hr = HRESULT_FROM_WIN32(GetLastError());
		free(pSD);
		return hr;
	}

	hr = Attach(pSD);
	free(pSD);
	return hr;
}


HRESULT COpcSecurity::CopyACL(PACL pDest, PACL pSrc)
{
	ACL_SIZE_INFORMATION aclSizeInfo;
	LPVOID pAce;
	ACE_HEADER *aceHeader;

	if (pSrc == NULL)
		return S_OK;

	if (!GetAclInformation(pSrc, (LPVOID) &aclSizeInfo, sizeof(ACL_SIZE_INFORMATION), AclSizeInformation))
		return HRESULT_FROM_WIN32(GetLastError());

	// Copy all of the ACEs to the new ACL
	for (UINT i = 0; i < aclSizeInfo.AceCount; i++)
	{
		if (!GetAce(pSrc, i, &pAce))
			return HRESULT_FROM_WIN32(GetLastError());

		aceHeader = (ACE_HEADER *) pAce;

		if (!AddAce(pDest, ACL_REVISION, 0xffffffff, pAce, aceHeader->AceSize))
			return HRESULT_FROM_WIN32(GetLastError());
	}

	return S_OK;
}

HRESULT COpcSecurity::AddAccessDeniedACEToACL(PACL *ppAcl, LPCTSTR pszPrincipal, DWORD dwAccessMask)
{
	ACL_SIZE_INFORMATION aclSizeInfo;
	int aclSize;
	DWORD returnValue;
	PSID principalSID;
	PACL oldACL, newACL = NULL;

	oldACL = *ppAcl;

	returnValue = GetPrincipalSID(pszPrincipal, &principalSID);
	if (FAILED(returnValue))
		return returnValue;

	aclSizeInfo.AclBytesInUse = 0;
	if (*ppAcl != NULL)
		GetAclInformation(oldACL, (LPVOID) &aclSizeInfo, sizeof(ACL_SIZE_INFORMATION), AclSizeInformation);

	aclSize = aclSizeInfo.AclBytesInUse + sizeof(ACL) + sizeof(ACCESS_DENIED_ACE) + GetLengthSid(principalSID) - sizeof(DWORD);

	OPCTRY(newACL = (PACL) new BYTE[aclSize]);
	if (newACL == NULL)
		return E_OUTOFMEMORY;

	if (!InitializeAcl(newACL, aclSize, ACL_REVISION))
	{
		free(principalSID);
		return HRESULT_FROM_WIN32(GetLastError());
	}

	if (!AddAccessDeniedAce(newACL, ACL_REVISION2, dwAccessMask, principalSID))
	{
		free(principalSID);
		return HRESULT_FROM_WIN32(GetLastError());
	}

	returnValue = CopyACL(newACL, oldACL);
	if (FAILED(returnValue))
	{
		free(principalSID);
		return returnValue;
	}

	*ppAcl = newACL;

	if (oldACL != NULL)
		free(oldACL);
	free(principalSID);
	return S_OK;
}


HRESULT COpcSecurity::AddAccessAllowedACEToACL(PACL *ppAcl, LPCTSTR pszPrincipal, DWORD dwAccessMask)
{
	ACL_SIZE_INFORMATION aclSizeInfo;
	int aclSize;
	DWORD returnValue;
	PSID principalSID;
	PACL oldACL, newACL = NULL;

	oldACL = *ppAcl;

	returnValue = GetPrincipalSID(pszPrincipal, &principalSID);
	if (FAILED(returnValue))
		return returnValue;

	aclSizeInfo.AclBytesInUse = 0;
	if (*ppAcl != NULL)
		GetAclInformation(oldACL, (LPVOID) &aclSizeInfo, (DWORD) sizeof(ACL_SIZE_INFORMATION), AclSizeInformation);

	aclSize = aclSizeInfo.AclBytesInUse + sizeof(ACL) + sizeof(ACCESS_ALLOWED_ACE) + GetLengthSid(principalSID) - sizeof(DWORD);

	OPCTRY(newACL = (PACL) new BYTE[aclSize]);
	if (newACL == NULL)
		return E_OUTOFMEMORY;

	if (!InitializeAcl(newACL, aclSize, ACL_REVISION))
	{
		free(principalSID);
		return HRESULT_FROM_WIN32(GetLastError());
	}

	returnValue = CopyACL(newACL, oldACL);
	if (FAILED(returnValue))
	{
		free(principalSID);
		return returnValue;
	}

	if (!AddAccessAllowedAce(newACL, ACL_REVISION2, dwAccessMask, principalSID))
	{
		free(principalSID);
		return HRESULT_FROM_WIN32(GetLastError());
	}

	*ppAcl = newACL;

	if (oldACL != NULL)
		free(oldACL);
	free(principalSID);
	return S_OK;
}


HRESULT COpcSecurity::RemovePrincipalFromACL(PACL pAcl, LPCTSTR pszPrincipal)
{
	ACL_SIZE_INFORMATION aclSizeInfo;
	ULONG i;
	LPVOID ace;
	ACCESS_ALLOWED_ACE *accessAllowedAce;
	ACCESS_DENIED_ACE *accessDeniedAce;
	SYSTEM_AUDIT_ACE *systemAuditAce;
	PSID principalSID;
	DWORD returnValue;
	ACE_HEADER *aceHeader;

	returnValue = GetPrincipalSID(pszPrincipal, &principalSID);
	if (FAILED(returnValue))
		return returnValue;

	GetAclInformation(pAcl, (LPVOID) &aclSizeInfo, (DWORD) sizeof(ACL_SIZE_INFORMATION), AclSizeInformation);

	for (i = 0; i < aclSizeInfo.AceCount; i++)
	{
		if (!GetAce(pAcl, i, &ace))
		{
			free(principalSID);
			return HRESULT_FROM_WIN32(GetLastError());
		}

		aceHeader = (ACE_HEADER *) ace;

		if (aceHeader->AceType == ACCESS_ALLOWED_ACE_TYPE)
		{
			accessAllowedAce = (ACCESS_ALLOWED_ACE *) ace;

			if (EqualSid(principalSID, (PSID) &accessAllowedAce->SidStart))
			{
				DeleteAce(pAcl, i);
				free(principalSID);
				return S_OK;
			}
		} else

		if (aceHeader->AceType == ACCESS_DENIED_ACE_TYPE)
		{
			accessDeniedAce = (ACCESS_DENIED_ACE *) ace;

			if (EqualSid(principalSID, (PSID) &accessDeniedAce->SidStart))
			{
				DeleteAce(pAcl, i);
				free(principalSID);
				return S_OK;
			}
		} else

		if (aceHeader->AceType == SYSTEM_AUDIT_ACE_TYPE)
		{
			systemAuditAce = (SYSTEM_AUDIT_ACE *) ace;

			if (EqualSid(principalSID, (PSID) &systemAuditAce->SidStart))
			{
				DeleteAce(pAcl, i);
				free(principalSID);
				return S_OK;
			}
		}
	}
	free(principalSID);
	return S_OK;
}


HRESULT COpcSecurity::SetPrivilege(LPCTSTR privilege, BOOL bEnable, HANDLE hToken)
{
	HRESULT hr;
	TOKEN_PRIVILEGES tpPrevious;
	TOKEN_PRIVILEGES tp;
	DWORD  cbPrevious = sizeof(TOKEN_PRIVILEGES);
	LUID   luid;
	HANDLE hTokenUsed;

	// if no token specified open process token
	if (hToken == 0)
	{
		if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &hTokenUsed))
		{
			hr = HRESULT_FROM_WIN32(GetLastError());
			OPCASSERT(FALSE);
			return hr;
		}
	}
	else
		hTokenUsed = hToken;

	if (!LookupPrivilegeValue(NULL, privilege, &luid ))
	{
		hr = HRESULT_FROM_WIN32(GetLastError());
		OPCASSERT(FALSE);
		if (hToken == 0)
			CloseHandle(hTokenUsed);
		return hr;
	}

	tp.PrivilegeCount = 1;
	tp.Privileges[0].Luid = luid;
	tp.Privileges[0].Attributes = 0;

	if (!AdjustTokenPrivileges(hTokenUsed, FALSE, &tp, sizeof(TOKEN_PRIVILEGES), &tpPrevious, &cbPrevious))
	{
		hr = HRESULT_FROM_WIN32(GetLastError());
		OPCASSERT(FALSE);
		if (hToken == 0)
			CloseHandle(hTokenUsed);
		return hr;
	}

	tpPrevious.PrivilegeCount = 1;
	tpPrevious.Privileges[0].Luid = luid;

	if (bEnable)
		tpPrevious.Privileges[0].Attributes |= (SE_PRIVILEGE_ENABLED);
	else
		tpPrevious.Privileges[0].Attributes ^= (SE_PRIVILEGE_ENABLED & tpPrevious.Privileges[0].Attributes);

	if (!AdjustTokenPrivileges(hTokenUsed, FALSE, &tpPrevious, cbPrevious, NULL, NULL))
	{
		hr = HRESULT_FROM_WIN32(GetLastError());
		OPCASSERT(FALSE);
		if (hToken == 0)
			CloseHandle(hTokenUsed);
		return hr;
	}
	return S_OK;
}
