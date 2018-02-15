#!/bin/bash
echo Test the .Net Core console server and console client
workdir=$(pwd)
testresult=0
testresulthttps=0
serverresult=0

cd SampleApplications/Samples/NetCoreConsoleServer
echo build server
rm -r obj
dotnet build NetCoreConsoleServer.csproj
cd ../NetCoreConsoleClient
echo build client
rm -r obj
dotnet build NetCoreConsoleClient.csproj
cd $workdir

cd SampleApplications/Samples/NetCoreConsoleServer
echo start server
dotnet run --no-restore --no-build --project NetCoreConsoleServer.csproj -t 60 -a >./server.log &
serverpid="$!"
echo wait for server started
grep -m 1 "start" <(tail -f ./server.log --pid=$serverpid)
tail -f ./server.log --pid=$serverpid &
cd $workdir

cd SampleApplications/Samples/NetCoreConsoleClient
echo start client for tcp connection
dotnet run --no-restore --no-build --project NetCoreConsoleClient.csproj -t 20 -a &
clientpid="$!"
echo start client for https connection
dotnet run --no-restore --no-build --project NetCoreConsoleClient.csproj -t 15 -a https://localhost:51212 &
httpsclientpid="$!"
cd $workdir

echo wait for opc.tcp client
wait $clientpid
if [ $? -eq 0 ]; then
	echo "SUCCESS - Client test passed"
else
	testresult=$?
	echo "FAILED - Client test failed with a status of $testresult"
fi

cd SampleApplications/Samples/NetCoreConsoleClient
cd $workdir

echo wait for https client
wait $httpsclientpid
if [ $? -eq 0 ]; then
	echo "SUCCESS - Client test passed"
else
	testresulthttps=$?
	echo "WARN - Client test failed with a status of $testresulthttps"
	echo "WARN - Client may require to use trusted TLS server cert to pass this test"
fi

echo send Ctrl-C to server
kill -s SIGINT $serverpid

echo wait for server
wait $serverpid
serverresult=$?

if [ $? -eq 0 ]; then
	echo "SUCCESS - Server test passed"
else
	serverresult=$?
	echo "FAILED - Server test failed with a status of $serverresult"
fi

echo "Test results: Client:$testresult Server:$serverresult ClientHttps:$testresulthttps"
exit $((testresult + serverresult + testresulthttps))



