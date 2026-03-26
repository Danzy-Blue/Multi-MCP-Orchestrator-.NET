# Azure Deployment

This folder contains Azure Container Apps infrastructure for the three backend services:

- `mcp-host`
- `uw-mcp`
- `devops-mcp`

`MyChat` is intentionally excluded from this deployment plan and should be hosted separately.

## Design Assumptions

- `mcp-host` is the only externally exposed backend service.
- `uw-mcp` and `devops-mcp` are internal-only Container Apps.
- `mcp-host` runs at a single replica because session state is currently stored in-memory.
- Images are built into Azure Container Registry before the final app deployment step.

## Files

- `main.bicep`: shared infrastructure plus the three Container Apps
- `main.parameters.example.json`: parameter template
- `deploy.ps1`: bootstrap, build, push, and deploy wrapper

## Required Azure Prerequisites

- Azure CLI authenticated to the target subscription
- Permission to create resource groups, ACR, Log Analytics, Container Apps, and Container Apps environments
- Bicep support in Azure CLI

## Deployment

1. Copy `main.parameters.example.json` to an environment-specific parameters file.
2. Fill in the real external URLs and secrets.
3. Run:

```powershell
.\infra\azure\deploy.ps1 `
  -ResourceGroupName mcpnet-prod-rg `
  -Location eastus `
  -ParametersFile .\infra\azure\main.parameters.prod.json `
  -ImageTag 2026-03-26.1
```

## Post-Deployment

- Configure the separately hosted `MyChat` app to use the deployed `McpHost` `/chat` URL.
- Ensure the `myChatCorsOrigin` parameter matches the UI origin.
- Monitor `mcp-host /health` as the public health endpoint.
