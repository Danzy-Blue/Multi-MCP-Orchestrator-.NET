param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$Location,

    [string]$DeploymentName = 'mcpnet',

    [string]$ImageTag = 'latest',

    [string]$ParametersFile = (Join-Path $PSScriptRoot 'main.parameters.example.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$templateFile = Join-Path $PSScriptRoot 'main.bicep'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')

if (-not (Test-Path $templateFile)) {
    throw "Template file not found: $templateFile"
}

if (-not (Test-Path $ParametersFile)) {
    throw "Parameters file not found: $ParametersFile"
}

Write-Host "Ensuring Azure CLI dependencies are available..."
az extension add --name containerapp --upgrade | Out-Null
az provider register --namespace Microsoft.App | Out-Null
az provider register --namespace Microsoft.OperationalInsights | Out-Null
az provider register --namespace Microsoft.ContainerRegistry | Out-Null

Write-Host "Creating or updating resource group '$ResourceGroupName' in '$Location'..."
az group create --name $ResourceGroupName --location $Location | Out-Null

Write-Host "Bootstrapping shared infrastructure..."
$bootstrapOutputs = az deployment group create `
    --resource-group $ResourceGroupName `
    --name "$DeploymentName-bootstrap" `
    --template-file $templateFile `
    --parameters "@$ParametersFile" deployContainerApps=false imageTag=$ImageTag `
    --query properties.outputs `
    --output json | ConvertFrom-Json

$acrName = $bootstrapOutputs.acrName.value
$mcpHostRepo = $bootstrapOutputs.mcpHostImageRepository.value
$uwMcpRepo = $bootstrapOutputs.uwMcpImageRepository.value
$devOpsRepo = $bootstrapOutputs.devOpsMcpImageRepository.value

Write-Host "Building backend images in ACR '$acrName'..."
az acr build --registry $acrName --image "$mcpHostRepo:$ImageTag" --file (Join-Path $repoRoot 'McpHost\Dockerfile') $repoRoot
az acr build --registry $acrName --image "$uwMcpRepo:$ImageTag" --file (Join-Path $repoRoot 'Servers\UwMcp\Dockerfile') $repoRoot
az acr build --registry $acrName --image "$devOpsRepo:$ImageTag" --file (Join-Path $repoRoot 'Servers\DevOpsMcp\Dockerfile') $repoRoot

Write-Host "Deploying Container Apps..."
$deploymentOutputs = az deployment group create `
    --resource-group $ResourceGroupName `
    --name $DeploymentName `
    --template-file $templateFile `
    --parameters "@$ParametersFile" deployContainerApps=true imageTag=$ImageTag `
    --query properties.outputs `
    --output json | ConvertFrom-Json

Write-Host ''
Write-Host "Deployment completed."
Write-Host "McpHost URL: $($deploymentOutputs.mcpHostUrl.value)"
