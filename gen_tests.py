import os
BASE = r'D:\git\UA-.NETStandard2\Tests\Opc.Ua.Ctt.Tests'
print('Writing test files to', BASE)
print('GDS dir:', os.path.isdir(os.path.join(BASE, 'GDS')))
print('DA dir:', os.path.isdir(os.path.join(BASE, 'DataAccess')))
print('HA dir:', os.path.isdir(os.path.join(BASE, 'HistoricalAccess')))
