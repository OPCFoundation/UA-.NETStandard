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
#include "COpcClassFactory.h"
#include "COpcString.h"
#include "OpcRegistry.h"

#include "ntsecapi.h"
// #include "DCOM Config/dcomperm.h"

//============================================================================
// Local Functions

// InstallService
static bool InstallService(
	LPCTSTR szServiceName,
	LPCTSTR szServiceDescription,
	LPCTSTR tsFilePath
)
{
    bool bResult = false;

    SC_HANDLE hSCM = NULL;
    SC_HANDLE hService = NULL;
    
    TRY
    {
        // open service control manager.
        hSCM = OpenSCManager(NULL, NULL, SC_MANAGER_ALL_ACCESS);

        if (hSCM == NULL)
        {
            THROW_(bResult, false);
        }

        SC_HANDLE hService = OpenService(hSCM, szServiceName, SERVICE_QUERY_CONFIG);

        // check if sevice already installed.
        if (hService != NULL)
		{
            GOTOFINALLY();
        }

        // create service.
        hService = CreateService(
		    hSCM, 
            szServiceName, 
            szServiceDescription,
		    SERVICE_ALL_ACCESS, 
            SERVICE_WIN32_OWN_PROCESS,
		    SERVICE_DEMAND_START, 
            SERVICE_ERROR_NORMAL,
            tsFilePath, 
            NULL, 
            NULL, 
            _T("RPCSS\0"), 
            NULL, 
            NULL);

        if (hService == NULL)
        {
            THROW_(bResult, false);
        }
    }
    CATCH
    {
    }
    
    FINALLY

    if (hService != NULL) CloseServiceHandle(hService);
    if (hService != NULL) CloseServiceHandle(hSCM);

    return bResult;
}

// UninstallService
static void UninstallService(LPCTSTR szServiceName)
{
    SC_HANDLE hSCM = NULL;
    SC_HANDLE hService = NULL;
    
    TRY
    {
        // open service control manager.
        hSCM = OpenSCManager(NULL, NULL, SC_MANAGER_ALL_ACCESS);

        if (hSCM == NULL)
        {
            GOTOFINALLY();
        }

        hService = OpenService(hSCM, szServiceName, SERVICE_STOP | DELETE);

        // check if sevice already uninstalled.
        if (hService == NULL)
	    {
            GOTOFINALLY();
        }

        // send control message to service - ignore errors.
        SERVICE_STATUS cStatus;
	    ZeroMemory(&cStatus, sizeof(cStatus));
        ControlService(hService, SERVICE_CONTROL_STOP, &cStatus);

        // mark service for deletion.
        DeleteService(hService);
    }
    CATCH
    {
    }

    FINALLY

    if (hService != NULL) CloseServiceHandle(hService);
    if (hService != NULL) CloseServiceHandle(hSCM);

}

// UnregisterApplication
static void UnregisterApplication(
    const COpcString& cModuleName, 
    const COpcString& cApplicationName,
    const GUID&       cAppID
)
{
	COpcString cSubKey;

	// uninstall service.
	UninstallService(cApplicationName);

	// delete exe sub key.
	cSubKey = _T("AppID\\") + cModuleName + _T(".exe");
	OpcRegDeleteKey(HKEY_CLASSES_ROOT, cSubKey);

    // delete app id sub key.
    cSubKey = _T("AppID\\") + (COpcString)cAppID;
	OpcRegDeleteKey(HKEY_CLASSES_ROOT, cSubKey);
}

// RegisterApplication
static HRESULT RegisterApplication(
    const COpcString& cModuleName, 
    const COpcString& cModulePath, 
    const COpcString& cApplicationName,
    const COpcString& cApplicationDescription,
    const GUID&       cAppID,
	bool              bService,
	bool              bEveryone
)
{
    HRESULT hResult = S_OK;
    
    TRY
    {
        // create exe sub key.
        COpcString cSubKey = _T("AppID\\") + cModuleName + _T(".exe");

        if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, NULL, (COpcString)cAppID))
        {
           THROW_(hResult, SELFREG_E_CLASS);
        }

        // create app id sub key.
        cSubKey = _T("AppID\\") + (COpcString)cAppID;

		if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, NULL, (cApplicationDescription.IsEmpty())?cApplicationName:cApplicationDescription))
        {
           THROW_(hResult, SELFREG_E_CLASS);
        }

		/*
		// Set application LaunchPermission and AccessPermission to "Everyone".
		if (bEveryone)
		{
			// create authenication sub key.
			if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, _T("AuthenticationLevel"), (DWORD)1))
			{
				THROW_(hResult, SELFREG_E_CLASS);
			}

			// check for OSes earlier than XP SP2
			if (IsLegacySecurityModel())
			{
				static BYTE pEveryone[] = {
					0x01,0x00,0x04,0x80,0x34,0x00,0x00,0x00,0x50,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
						0x14,0x00,0x00,0x00,0x02,0x00,0x20,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x18,0x00,
						0x01,0x00,0x00,0x00,0x01,0x01,0x00,0x00,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x00,
						0x00,0x00,0x00,0x00,0x01,0x05,0x00,0x00,0x00,0x00,0x00,0x05,0x15,0x00,0x00,0x00,
						0xa0,0x5f,0x84,0x1f,0x5e,0x2e,0x6b,0x49,0xce,0x12,0x03,0x03,0xf4,0x01,0x00,0x00,
						0x01,0x05,0x00,0x00,0x00,0x00,0x00,0x05,0x15,0x00,0x00,0x00,0xa0,0x5f,0x84,0x1f,
						0x5e,0x2e,0x6b,0x49,0xce,0x12,0x03,0x03,0xf4,0x01,0x00,0x00};

				// create launch permission sub key.
				if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, _T("LaunchPermission"), pEveryone, sizeof(pEveryone)))
				{
					THROW_(hResult, SELFREG_E_CLASS);
				}

				// create access permission sub key.
				if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, _T("AccessPermission"), pEveryone, sizeof(pEveryone)))
				{
					THROW_(hResult, SELFREG_E_CLASS);
				}
			}

			// use the APIs to set permissions for OSes later than or equal XP S2.
			else
			{
				DWORD dwResult = ChangeAppIDAccessACL(
					(LPTSTR)(LPCTSTR)(COpcString)cAppID, 
					_T("Everyone"), 
					TRUE, 
					TRUE, 
					COM_RIGHTS_EXECUTE_LOCAL | COM_RIGHTS_EXECUTE_REMOTE | COM_RIGHTS_EXECUTE); 

				if (dwResult != ERROR_SUCCESS)
				{
					// could not set access permissions.
					THROW_(hResult, SELFREG_E_CLASS);
				}
				
				dwResult = ChangeAppIDLaunchAndActivateACL(
					(LPTSTR)(LPCTSTR)(COpcString)cAppID, 
					_T("Everyone"), 
					TRUE, 
					TRUE, 
					COM_RIGHTS_ACTIVATE_LOCAL | COM_RIGHTS_EXECUTE_LOCAL | COM_RIGHTS_ACTIVATE_REMOTE | COM_RIGHTS_EXECUTE_REMOTE | COM_RIGHTS_EXECUTE); 

				if (dwResult != ERROR_SUCCESS)
				{
					// could hResult set launch and activate permissions.
					THROW_(hResult, SELFREG_E_CLASS);
				}
			}
		}
		*/

		// register as a service.
        if (bService)
        {
			// set service application parameters.
            if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, _T("LocalService"), cApplicationName))
            {
                THROW_(hResult, SELFREG_E_CLASS);
            }

            if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, _T("ServiceParameters"), _T("-Service")))
            {
                THROW_(hResult, SELFREG_E_CLASS);
            }

			// install service.
            hResult = InstallService(cApplicationName, cApplicationDescription, cModulePath);

            if (FAILED(hResult))
            {
                THROW();
            }
        }
    }
    CATCH
    {
		UnregisterApplication(cModuleName, cApplicationName, cAppID); 
    }

    return hResult;
}

// GetModuleInfo
static HRESULT GetModuleInfo(
	HINSTANCE   hModule, 
	COpcString& cPath, 
	COpcString& cName, 
	COpcString& cExtension
)
{
	// initialize return parameters.
	cPath.Empty();
	cName.Empty();
	cExtension.Empty();

	// lookup the module path.
	TCHAR tsModulePath[MAX_PATH+1];
    memset(tsModulePath, 0, sizeof(tsModulePath));

	DWORD dwResult = GetModuleFileName(hModule, tsModulePath, MAX_PATH);

    if (dwResult == 0)
    {
        return E_UNEXPECTED;
    }

	cPath = tsModulePath;

    // parse module name.
    cName = tsModulePath;

    int iIndex = cName.ReverseFind(_T("\\"));

    if (iIndex != -1)
    {
        cName = cName.SubStr(iIndex+1);
    }

	// parse extension.
    iIndex = cName.ReverseFind(_T("."));

    if (iIndex != -1)
    {
        cExtension = cName.SubStr(iIndex+1).ToUpper();
        cName      = cName.SubStr(0, iIndex);
    }

	// check for valid values.
	if (cName.IsEmpty() || cExtension.IsEmpty())
	{
        return E_UNEXPECTED;
	}

	return S_OK;
}

//============================================================================
// OpcGetClassObject

HRESULT OpcGetClassObject(
    REFCLSID                   rclsid, 
    REFIID                     riid, 
    LPVOID*                    ppv, 
    const TOpcClassTableEntry* pClasses)
{
    // find the class factory for a COM server.
    for (int ii = 0; pClasses[ii].tsClassName != NULL; ii++)
    {
        if (*(pClasses[ii].pClsid) == rclsid)
        {
            // create the class factory - adds a reference.
			COpcClassFactory* pFactory = new COpcClassFactory(&(pClasses[ii]));

            // query for the desired interface - adds another reference.
            HRESULT hResult = pFactory->QueryInterface(riid, ppv);

            // release the local reference.
            pFactory->Release();

            return hResult;
        }
    }

    // clsid was not found in module.
    return CLASS_E_CLASSNOTAVAILABLE;
}

//============================================================================
// OpcRegisterClassObjects

OPCUTILS_API HRESULT OpcRegisterClassObjects(
    const TOpcClassTableEntry* pClasses,
    DWORD                      dwContext, 
    DWORD                      dwFlags,
    DWORD*                     pdwRegister)
{
    HRESULT hResult = S_OK;

    // register each server in the table.
    for (int ii = 0; pClasses[ii].tsClassName != NULL; ii++)
    {
        IUnknown* ipUnknown = NULL;

        // create the class factory - adds a reference.
		COpcClassFactory* pFactory = new COpcClassFactory(&(pClasses[ii]));

        // query for the IUnknown interface - adds another reference.
        hResult = pFactory->QueryInterface(IID_IUnknown, (void**)&ipUnknown);

        // release the local reference.
        pFactory->Release();

        if (FAILED(hResult))
        {
            return hResult;
        }

        // register the class factory.
        hResult = CoRegisterClassObject(
            *(pClasses[ii].pClsid), 
            ipUnknown,
            dwContext, 
            dwFlags, 
            &(pdwRegister[ii]));

        // release the local reference again.
        ipUnknown->Release();

        if (FAILED(hResult))
        {
            return hResult;
        }
    }

    // success.
    return S_OK;
}

//============================================================================
// OpcRevokeClassObjects

OPCUTILS_API HRESULT OpcRevokeClassObjects(
    const TOpcClassTableEntry* pClasses,
    DWORD*                     pdwRegister)
{
    // unregister each server in the table.
    for (int ii = 0; pClasses[ii].tsClassName != NULL; ii++)
    {
        CoRevokeClassObject(pdwRegister[ii]);
    }

    // success.
    return S_OK;
}

//============================================================================
// OpcRegisterServer

HRESULT OpcRegisterServer(
    HINSTANCE                  hModule, 
	LPCTSTR                    tsVendorName,
	LPCTSTR                    tsApplicationName,
	LPCTSTR                    tsApplicationDescription,
	const GUID&                cAppID,
	bool                       bService,
    const TOpcClassTableEntry* pClasses,
    const TClassCategories*    pCategories,
	bool                       bEveryone)
{

    HRESULT hResult = S_OK;

    TRY
    {
		// get module information.
		COpcString cModulePath;
		COpcString cModuleName;
		COpcString cModuleExtension;

		hResult = GetModuleInfo(hModule, cModulePath, cModuleName, cModuleExtension);

        if (FAILED(hResult))
        {
            THROW_(hResult, SELFREG_E_CLASS);
        }

		// determine if registering an out of process server.
		bool bIsExe = (cModuleExtension == _T("EXE"));

		// register application
        if (cAppID != GUID_NULL)
        {
			hResult = RegisterApplication(
				cModuleName,
				cModulePath,
				tsApplicationName,
				tsApplicationDescription,
				cAppID,
				bService,
				bEveryone
			);

            if (FAILED(hResult))
            {
                THROW();
            }
        }

        // construct type library path
	    COpcString cTypeLib(cModulePath);

        // register type library
        ITypeLib* ipTypeLib = NULL;
        hResult = LoadTypeLibEx((LPCWSTR)cTypeLib, REGKIND_REGISTER, &ipTypeLib);

        if (SUCCEEDED(hResult))
        {
            ipTypeLib->Release();
        }          

        hResult = S_OK;

        // add clsid and prog id registy keys for each class.
        for (UINT ii = 0; pClasses[ii].tsClassName != NULL; ii++)
        {    
            COpcString cSubKey;
            COpcString cValue;

            // create clsid string.
			COpcString cClsid;
            cClsid.FromGuid(*(pClasses[ii].pClsid));

            // create version independent prog id.
            COpcString cVersionIndependentProgID;

            cVersionIndependentProgID += tsVendorName;
            cVersionIndependentProgID += _T(".");
            cVersionIndependentProgID += pClasses[ii].tsClassName;
			
            // create prog id.
            COpcString cProgID;
			
			cProgID += cVersionIndependentProgID;
            cProgID += _T(".");
            cProgID += pClasses[ii].tsClassVersion;

			// determine class description.
			COpcString cClassDescription = pClasses[ii].tsClassDescription;

			if (cClassDescription.IsEmpty())
			{
				cClassDescription = cProgID;
			}

            // set description in clsid sub key.
            cSubKey = _T("CLSID\\") + cClsid;
               
            if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, NULL, cClassDescription))
            {
                THROW_(hResult, SELFREG_E_CLASS);
            }

            // register application.
            if (cAppID != GUID_NULL)
            {
                if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, _T("AppID"), (COpcString)cAppID))
                {
                    THROW_(hResult, SELFREG_E_CLASS);
                }
            }

            // set the prog id sub key.
            cSubKey = _T("CLSID\\") + cClsid + _T("\\ProgID");
        
            if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, NULL, cProgID))
            {
                THROW_(hResult, SELFREG_E_CLASS);
            }

            // set the version independent prog id sub key.
            cSubKey = _T("CLSID\\") + cClsid + _T("\\VersionIndependentProgID");
        
            if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, NULL, cVersionIndependentProgID))
            {
                THROW_(hResult, SELFREG_E_CLASS);
            }

            // set LocalServer32 or InprocServer32 sub key.			
			cSubKey =  _T("CLSID\\");
			cSubKey += cClsid;
			cSubKey += (bIsExe)?_T("\\LocalServer32"):_T("\\InprocServer32");
              
            if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, NULL, cModulePath))
            {
                THROW_(hResult, SELFREG_E_CLASS);
            }
         
            // set threading model.
            if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, _T("ThreadingModel"), _T("Free")))
            {
                THROW_(hResult, SELFREG_E_CLASS);
            }

            // set description in prog id sub key.
            cSubKey = cProgID;
               
            if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, NULL, cClassDescription))
            {
                THROW_(hResult, SELFREG_E_CLASS);
            }

            // set clsid in prog id sub key.
            cSubKey = cProgID + _T("\\CLSID");
               
            if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, NULL, cClsid))
            {
                THROW_(hResult, SELFREG_E_CLASS);
			}

            // set description in version independent prog id sub key.
            cSubKey = cVersionIndependentProgID;
               
            if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, NULL, cClassDescription))
            {
                THROW_(hResult, SELFREG_E_CLASS);
            }

            // set clsid in version independent prog id sub key.
            cSubKey = cVersionIndependentProgID + _T("\\CLSID");
               
            if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, NULL, cClsid))
            {
                THROW_(hResult, SELFREG_E_CLASS);
			}
			
            // set current version prog id in version independent prog id sub key. 
            cSubKey = cVersionIndependentProgID + _T("\\CurVer");
               
            if (!OpcRegSetValue(HKEY_CLASSES_ROOT, cSubKey, NULL, cProgID))
            {
                THROW_(hResult, SELFREG_E_CLASS);
			}
        }

        // add categories.
        for (ii = 0; pCategories[ii].pClsid != NULL; ii++)
        {
            RegisterClsidInCategory(*(pCategories[ii].pClsid), *(pCategories[ii].pCategory), pCategories[ii].szDescription);
        }
    }
    CATCH
    {
        OpcUnregisterServer(hModule, tsVendorName, tsApplicationName, cAppID, pClasses, pCategories);
    }

    return hResult;
}

//============================================================================
// OpcUnregisterServer

HRESULT OpcUnregisterServer(
    HINSTANCE                  hModule, 
	LPCTSTR                    tsVendorName,
	LPCTSTR                    tsApplicationName,
	const GUID&                cAppID,
    const TOpcClassTableEntry* pClasses,
    const TClassCategories*    pCategories
)
{  
    // remove categories.
    for (int ii = 0; pCategories[ii].pClsid != NULL; ii++)
    {
        UnregisterClsidInCategory(*(pCategories[ii].pClsid), *(pCategories[ii].pCategory));
    }

    // remove clsid and prog id registy keys for each class.
    for (ii = 0; pClasses[ii].tsClassName != NULL; ii++)
    {    
		COpcString cSubKey;

		// delete clsid sub key.
		COpcString cClsid;
        cClsid.FromGuid(*(pClasses[ii].pClsid));

		cSubKey = _T("CLSID\\") + cClsid;
        OpcRegDeleteKey(HKEY_CLASSES_ROOT, cSubKey);

        // delete version independent prog id.
        COpcString cVersionIndependentProgID;

        cVersionIndependentProgID += tsVendorName;
        cVersionIndependentProgID += _T(".");
        cVersionIndependentProgID += pClasses[ii].tsClassName;
		
		cSubKey = cVersionIndependentProgID;
        OpcRegDeleteKey(HKEY_CLASSES_ROOT, cSubKey);

        // delete prog id.
        COpcString cProgID;
		
		cProgID += cVersionIndependentProgID;
        cProgID += _T(".");
        cProgID += pClasses[ii].tsClassVersion;

		cSubKey = cProgID;
        OpcRegDeleteKey(HKEY_CLASSES_ROOT, cSubKey);      
    }

	// unregister application.
	COpcString cModulePath;
	COpcString cModuleName;
	COpcString cModuleExtension;

	HRESULT hResult = GetModuleInfo(hModule, cModulePath, cModuleName, cModuleExtension);

    if (SUCCEEDED(hResult))
    {
		UnregisterApplication(cModuleName, tsApplicationName, cAppID);
    }

    return S_OK;
}
