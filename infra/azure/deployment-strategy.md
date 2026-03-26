# Azure Container Deployment Strategy

## Goal

Deploy the backend stack to Azure Container Apps with:

- `mcp-host` as the only public entrypoint
- `uw-mcp` and `devops-mcp` reachable only inside the Container Apps environment
- `MyChat` hosted separately and configured to call the public `mcp-host` URL

## Recommended Topology

- Azure Container Registry for backend images
- One Azure Container Apps environment shared by all three backend services
- Log Analytics workspace attached to the Container Apps environment
- External ingress only on `mcp-host`
- Internal ingress on `uw-mcp` and `devops-mcp`
- Single replica for `mcp-host`
- One or two replicas for `uw-mcp` and `devops-mcp`

This matches the current codebase because `mcp-host` stores session state in memory.

## Deployment Strategy

### 1. Prepare Azure prerequisites

- Choose one resource group per environment
- Choose one Azure Container Registry per environment
- Confirm Azure CLI is authenticated to the target subscription
- Confirm the separately hosted `MyChat` origin is known for CORS

The deployment wrapper now self-installs or upgrades the `containerapp` Azure CLI extension and registers the required providers.

### 2. Create an environment-specific parameters file

Start from [`main.parameters.example.json`](d:/Danzy/Code/2026/MCP%20NET%203.0/infra/azure/main.parameters.example.json).

Set at minimum:

- `containerRegistryName`
- `myChatCorsOrigin`
- `llmProvider`
- `openAiApiKey` or `geminiApiKey`
- `apimBaseUrl`
- `submissionUrl`
- `quoteUrl`
- `apimSubscriptionKeyValue`
- `tfsBaseUrl`
- `tfsPat`

Do not reuse the example file directly for production.

### 3. Bootstrap shared infrastructure first

Run the wrapper once. It already performs the deployment in two phases:

1. Shared infrastructure bootstrap
2. Image build in ACR
3. Final Container Apps deployment

Command:

```powershell
.\infra\azure\deploy.ps1 `
  -ResourceGroupName mcpnet-prod-rg `
  -Location eastus `
  -ParametersFile .\infra\azure\main.parameters.prod.json `
  -ImageTag 2026-03-26.1
```

This strategy is correct for your repo because the Bicep template needs the ACR to exist before `az acr build` can push images.

### 4. Build images in ACR, not on the local machine

Use `az acr build` as the source of truth for deployable images.

Reason:

- Azure Container Apps runs Linux containers
- ACR build runs in Azure and avoids local Docker engine differences
- Your current workstation is in Windows container mode, which prevents local validation of Linux-based Dockerfiles

That local limitation does not block Azure deployment because `az acr build` is the actual deployment build path.

### 5. Deploy Container Apps with stable ingress boundaries

- `mcp-host`
  - external ingress
  - target port `8888`
  - `maxReplicas = 1`
- `uw-mcp`
  - internal ingress
  - target port `8001`
- `devops-mcp`
  - internal ingress
  - target port `8002`

The template currently wires `mcp-host` to the internal apps by app name:

- `http://uw-mcp/mcp`
- `http://devops-mcp/mcp`

Microsoft Learn documents app-name service discovery within the same Container Apps environment.

Source:

- https://learn.microsoft.com/en-us/azure/container-apps/connect-apps

### 6. Perform post-deployment smoke tests before switching UI traffic

Verify in this order:

1. `mcp-host /health`
2. `uw-mcp /health` from inside the environment or via Container Apps console/log checks
3. `devops-mcp /health`
4. `mcp-host /prompts`
5. `mcp-host /chat` with a simple request using the configured LLM provider

Then update the separately hosted `MyChat` runtime config to point at the deployed `mcp-host` `/chat` endpoint.

### 7. Roll forward, not in place

For deployments after the first one:

- build a new image tag
- deploy with that new tag
- validate `/health` and one real `/chat` request
- only then promote or update the UI/backend reference

Avoid mutating a previously deployed tag such as `latest` for production rollback scenarios.

## Release Gates

Before each deployment:

- `dotnet build McpHost\McpHost.sln`
- `dotnet build Servers\UwMcp\UwMcp.sln`
- `dotnet build Servers\DevOpsMcp\DevOpsMcp.sln`
- `dotnet test McpHost\McpHost.Tests\McpHost.Tests.csproj`
- `dotnet test Servers\UwMcp\UwMcp.Tests\UwMcp.Tests.csproj`
- `dotnet test Servers\DevOpsMcp\DevOpsMcp.Tests\DevOpsMcp.Tests.csproj`
- `az bicep build --file infra\azure\main.bicep`

## Current Readiness Review

### Ready

- The three backend solutions build successfully
- All backend test suites pass
- The Bicep template builds successfully
- The Azure topology matches the current session-state constraint in `mcp-host`
- Dockerfiles were updated for the new multi-project .NET layout

### Watch Items

- Local Docker validation on this workstation currently fails because Docker is running in Windows container mode while the images target Linux
- `mcp-host` is intentionally single-replica until session state is externalized
- The current template uses ACR admin credentials; this works, but managed identity would be the stronger production posture later
- Health probes are currently left to platform defaults; if startup behavior becomes slow in production, define explicit probes in the Container Apps template

## Recommended Next Action

Use an environment-specific production parameters file and perform one non-production Azure deployment first. If smoke tests pass there, reuse the exact same deployment flow and image-tagging approach for production.
