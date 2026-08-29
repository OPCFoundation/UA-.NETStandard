#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the image, deploys the chart to a kind cluster, and drives it with a
    real OPC UA client.

.DESCRIPTION
    The chart tests prove the manifests say the right things. They cannot prove
    the image runs, that the Server comes up inside a pod, or that an OPC UA
    session survives the network between them - and every one of those has
    failed for reasons no template rendering would reveal.

    A stub inference endpoint is deployed alongside, so the round trip completes
    without a hosted service or a local model runtime. It is a test fixture and
    lives outside the chart: a stub shipped in the chart would eventually be
    deployed by someone who thought it was a feature.

    This is the slowest and most fragile check in the suite - image build, image
    load, chart install and a live session. Run it deliberately, and quarantine
    it rather than letting it gate everything else.

.PARAMETER Cluster
    Name of the kind cluster to create. Deleted on exit unless -Keep is given.

.PARAMETER Keep
    Leaves the cluster running, for investigating a failure.

.EXAMPLE
    pwsh deploy/helm/smoke-test.ps1
#>
[CmdletBinding()]
param(
    [string]$Cluster = 'ai-sample-smoke',
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $here '..' '..' '..' '..')

function Step($message) {
    Write-Host ''
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Require($tool) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool is required and was not found on PATH."
    }
}

Require docker
Require kind
Require kubectl
Require helm

$image = 'modelmanagementserver:smoke'
$release = 'ai-smoke'
$created = $false

try {
    Step "Building $image"
    docker build `
        -f (Join-Path $repoRoot 'samples/AI/ModelManagementServer/Dockerfile') `
        -t $image `
        $repoRoot
    if ($LASTEXITCODE -ne 0) { throw 'The image build failed.' }

    Step "Creating the kind cluster '$Cluster'"
    kind create cluster --name $Cluster --wait 180s
    if ($LASTEXITCODE -ne 0) { throw 'The cluster did not come up.' }
    $created = $true

    Step 'Loading the image into the cluster'
    kind load docker-image $image --name $Cluster
    if ($LASTEXITCODE -ne 0) { throw 'The image did not load.' }

    Step 'Deploying the stub inference endpoint'
    kubectl apply -f (Join-Path $here 'test-fixtures/stub-backend.yaml')
    kubectl wait --for=condition=available deployment/stub-inference-backend --timeout=180s
    if ($LASTEXITCODE -ne 0) { throw 'The stub endpoint did not become available.' }

    Step 'Installing the chart'
    helm install $release (Join-Path $here 'ai-model-management') `
        --set image.repository=modelmanagementserver `
        --set image.tag=smoke `
        --set image.pullPolicy=Never `
        --set backend.endpointUri=http://stub-inference-backend:5273/ `
        --set backend.site=EdgeOffServer `
        --set backend.egressPermitted=true `
        --wait --timeout 240s
    if ($LASTEXITCODE -ne 0) { throw 'The chart did not install.' }

    Step 'Opening a session from outside the cluster'
    $service = "svc/$release-ai-model-management"
    $forward = Start-Process kubectl `
        -ArgumentList 'port-forward', $service, '62640:62640' `
        -NoNewWindow -PassThru
    try {
        Start-Sleep -Seconds 6

        $output = dotnet run `
            --project (Join-Path $repoRoot 'samples/AI/ModelManagementClient') `
            -f net10.0 -- opc.tcp://localhost:62640/ModelManagementServer 2>&1 |
            Out-String
    }
    finally {
        Stop-Process -Id $forward.Id -Force -ErrorAction SilentlyContinue
    }

    Write-Host $output

    Step 'Checking what came back'

    # Each of these is a distinct claim, and a run that satisfies some and not
    # others is a more useful report than a single pass or fail.
    $checks = [ordered]@{
        'the Server published an AI root'      = 'AI root: '
        'the inference reached the endpoint'   = 'SMOKE-TEST-OK'
        'the result named the model that ran'  = 'ModelUsed     ns='
        'the chunked transfer completed'       = 'transfer      ns='
        'the asynchronous job produced a result' = 'job           ns='
        'the source reported itself reachable' = 'reachable            True'
    }

    $failed = @()

    foreach ($check in $checks.GetEnumerator()) {
        if ($output -match [regex]::Escape($check.Value)) {
            Write-Host "  ok   $($check.Key)" -ForegroundColor Green
        }
        else {
            Write-Host "  FAIL $($check.Key)" -ForegroundColor Red
            $failed += $check.Key
        }
    }

    if ($failed.Count -gt 0) {
        Write-Host ''
        kubectl logs "deployment/$release-ai-model-management" --tail=60
        throw "$($failed.Count) of $($checks.Count) checks failed."
    }

    Write-Host ''
    Write-Host "All $($checks.Count) checks passed." -ForegroundColor Green
}
finally {
    if ($created -and -not $Keep) {
        Step "Deleting the cluster '$Cluster'"
        kind delete cluster --name $Cluster | Out-Null
    }
    elseif ($created) {
        Write-Host ''
        Write-Host "The cluster '$Cluster' was left running. Delete it with:" -ForegroundColor Yellow
        Write-Host "  kind delete cluster --name $Cluster"
    }
}
