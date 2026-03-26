# MCP .NET Parallel Stack

This folder contains a parallel .NET 8 implementation of the current Python stack:

- `McpHost/McpHost.csproj`
- `McpHost/McpHost.Application/McpHost.Application.csproj`
- `McpHost/McpHost.Infrastructure/McpHost.Infrastructure.csproj`
- `Servers/UwMcp/UwMcp.csproj`
- `Servers/UwMcp/UwMcp.Application/UwMcp.Application.csproj`
- `Servers/DevOpsMcp/DevOpsMcp.csproj`
- `Servers/DevOpsMcp/DevOpsMcp.Application/DevOpsMcp.Application.csproj`

The existing Python implementation was not modified. The Angular chat UI only received a small runtime configuration hook so it can point at a deployed backend URL outside local development.

## Architecture And Deployment Docs

- Backend design: `docs/backend-platform-design.md`
- Azure deployment: `infra/azure/README.md`

## Scope

The .NET host preserves the current HTTP API used by the Angular app:

- `POST /chat`
- `GET /sessions`
- `POST /chat/close`
- `GET /health`

The .NET MCP servers expose streamable HTTP MCP endpoints and health endpoints:

- `GET /health`
- `POST /mcp`
- `DELETE /mcp`

## Configuration

`McpHost` now reads its defaults from the `AppConfig` section in `McpHost/appsettings.json`.

```json
"AppConfig": {
  "LlmProvider": "gemini",
  "LlmModel": "gemini-2.5-flash",
  "ApiPort": 8888,
  "SessionTtlSeconds": 3600,
  "CorsOrigins": [ "*" ],
  "ServerConfigs": [
    { "Alias": "uw", "Url": "http://localhost:8001/mcp" },
    { "Alias": "devops", "Url": "http://localhost:8002/mcp" }
  ]
}
```

For existing deployment scripts, the host still supports the legacy flat environment variables below as overrides.

## Project Structure

- `McpHost`: API host
- `McpHost.Application`: host application logic and contracts
- `McpHost.Infrastructure`: host external integrations and infrastructure wiring
- `McpGateway`: shared MCP client layer
- `UwMcp`: API/MCP host
- `UwMcp.Application`: underwriting application logic
- `DevOpsMcp`: API/MCP host
- `DevOpsMcp.Application`: DevOps/TFS application logic

## Environment Variables

### Host

- `LLM_PROVIDER`
- `LLM_MODEL`
- `GEMINI_MODEL`
- `OPENAI_MODEL`
- `LLM_REASONING_EFFORT`
- `OPENAI_REASONING_EFFORT`
- `GEMINI_API_KEY`
- `OPENAI_API_KEY`
- `API_PORT`
- `SESSION_TTL_SECS`
- `CORS_ORIGINS`
- `UW_SERVER_URL`
- `DEVOPS_SERVER_URL`
- `SYSTEM_INSTRUCTION`

### UW MCP

- `PORT`
- `MCP_HTTP_HOST`
- `MCP_HTTP_PATH`
- `SUBMISSION_URL`
- `QUOTE_URL`
- `APIM_BASE_URL`
- `APIM_SUBSCRIPTION_KEY`
- `APIM_SUBSCRIPTION_KEY_VALUE`

### DevOps MCP

- `PORT`
- `MCP_HTTP_HOST`
- `MCP_HTTP_PATH`
- `TFS_BASE_URL`
- `TFS_PAT`

## Build

From this folder:

```powershell
dotnet build .\McpHost\McpHost.sln
```

## Docker

Backend Dockerfiles were added for:

- `McpHost/Dockerfile`
- `Servers/UwMcp/Dockerfile`
- `Servers/DevOpsMcp/Dockerfile`

Local backend orchestration:

```powershell
Copy-Item .env.example .env
docker compose up --build
```

`docker-compose.yml` intentionally runs only the backend services. `MyChat` remains separately hosted or can keep running locally with Angular tooling.

## Azure Container Apps

Deployment assets live under `infra/azure/`.

The intended production topology is:

- `mcp-host`: external ingress
- `uw-mcp`: internal ingress
- `devops-mcp`: internal ingress
- `MyChat`: hosted separately, pointed at the public `mcp-host` URL

## Run

Run each project separately:

```powershell
dotnet run --project .\Servers\UwMcp\UwMcp.csproj
dotnet run --project .\Servers\DevOpsMcp\DevOpsMcp.csproj
dotnet run --project .\McpHost\McpHost.csproj
```

Default ports:

- `UwMcp`: `8001`
- `DevOpsMcp`: `8002`
- `McpHost`: `AppConfig:ApiPort` in `McpHost/appsettings.json`, or `API_PORT` as an override

## Notes

- The Python stack remains the current reference implementation.
- The .NET implementation now uses the official MCP C# SDK end-to-end instead of a custom shared protocol library.
- The .NET servers currently focus on the streamable HTTP MCP path used by the existing Docker and host setup.
- The Angular UI was kept separate from the container deployment and only updated to support a runtime-configured backend URL.
