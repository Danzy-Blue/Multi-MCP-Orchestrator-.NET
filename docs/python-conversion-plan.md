# Python Conversion Plan

## Scope

This plan covers the current `.NET` backend applications only:

- `McpHost`
- `McpGateway`
- `Servers/UwMcp`
- `Servers/DevOpsMcp`

`MyChat` is explicitly out of scope for this plan.

## Current .NET State

### McpHost

`McpHost` is the public backend entrypoint. It currently exposes:

- `GET /`
- `GET /health`
- `GET /sessions`
- `GET /prompts`
- `POST /prompts/render`
- `POST /chat`
- `POST /chat/close`

Its internal structure is layered:

- `McpHost`: API host and startup
- `McpHost.Application`: host contracts, configuration, session orchestration, MCP-facing abstractions
- `McpHost.Infrastructure`: LLM implementations, correlation plumbing, and the adapter to `McpGateway`

Important implementation detail:

- `SessionManager` owns in-memory session state and TTL cleanup.
- `McpHost.Application` now owns the host-side MCP contracts such as `IMcpServerConnectionFactory`.
- `McpHost.Infrastructure` adapts `McpGateway` into those host-side contracts through `GatewayMcpConnectionFactory`.

### McpGateway

`McpGateway` is not a deployable API. It is a shared MCP client library.

Its job is to:

- connect to remote MCP servers
- discover tools and prompts when those capabilities are exposed
- call MCP tools
- render MCP prompts
- translate MCP SDK content into gateway models

Current behavior detail:

- missing `tools/list` is treated as an empty tool catalog
- missing `prompts/list` is treated as an empty prompt catalog
- session startup still succeeds as long as the MCP connection itself succeeds

This means the Python equivalent should default to a shared package, not a standalone service.

### UwMcp

`Servers/UwMcp` is a standalone MCP HTTP server for underwriting tools.

It currently provides:

- `GET /`
- `GET /health`
- `POST /mcp`
- `DELETE /mcp`

Business logic lives in `UwMcp.Application`, mainly around UW/APIM calls and payload formatting.

### DevOpsMcp

`Servers/DevOpsMcp` is a standalone MCP HTTP server for Azure DevOps/TFS tools.

It currently provides:

- `GET /`
- `GET /health`
- `POST /mcp`
- `DELETE /mcp`

Business logic lives in `DevOpsMcp.Application`, mainly around WIQL queries, work-item retrieval, PAT handling, and result formatting.

## Recommended Python Target Shape

Preserve the current runtime boundaries:

1. `python-host`
   Replaces `McpHost`.
2. `python-gateway`
   Shared Python package replacing `McpGateway`.
3. `python-uw-mcp`
   Replaces `Servers/UwMcp`.
4. `python-devops-mcp`
   Replaces `Servers/DevOpsMcp`.

This is the cleanest conversion because it matches the current `.NET` architecture instead of introducing a redesign during the language migration.

## Proposed Folder Strategy

Use the sibling workspace already created:

- `d:\Danzy\Code\2026\MCP Pytthon`

Recommended structure:

```text
MCP Pytthon/
  host/
  gateway/
  uw_mcp/
  devops_mcp/
  docs/
  tests/
  scripts/
```

## Migration Phases

### Phase 1: Freeze backend contracts

- Capture the host HTTP contract for `/chat`, `/sessions`, `/chat/close`, `/prompts`, `/prompts/render`, and `/health`.
- Capture the MCP server contract for `/health` and `/mcp`.
- Record environment variables and appsettings behavior.
- Record expected correlation-id propagation behavior.

### Phase 2: Build the shared Python gateway package

- Implement MCP server connection management.
- Implement optional tool discovery and optional prompt discovery.
- Implement tool execution and prompt rendering.
- Keep this as a reusable package, not a web service.

### Phase 3: Convert McpHost

- Port `AppConfig` binding and legacy env-var overrides.
- Port session creation, reuse, disposal, and TTL cleanup.
- Port prompt listing and prompt rendering flow.
- Port the LLM tool-call loop for both OpenAI and Gemini.
- Preserve behavior when a connected MCP server exposes no tools and/or no prompts.
- Preserve correlation-id generation and propagation.
- Preserve the current public HTTP routes and response shapes.

### Phase 4: Convert UwMcp

- Port the streamable HTTP MCP host behavior.
- Port UW/APIM configuration handling.
- Port submission and quote operations.
- Preserve current API error formatting as much as practical.

### Phase 5: Convert DevOpsMcp

- Port the streamable HTTP MCP host behavior.
- Port PAT resolution rules.
- Port work-item fetch, search, and sprint listing logic.
- Preserve current markdown-oriented result formatting unless intentionally redesigned.

### Phase 6: Verification

- Add host unit tests for configuration, session behavior, and MCP routing.
- Add MCP server tests for underwriting and DevOps tool behavior.
- Run side-by-side comparisons between `.NET` and Python outputs.
- Verify the Python host works against both Python MCP servers through the Python gateway package.

### Phase 7: Cutover

- Update local run scripts and Docker orchestration to the Python backend.
- Keep the `.NET` stack available until Python parity is confirmed.
- Retire the `.NET` backend only after route, tool, and prompt parity is validated.

## Key Risks

- `McpHost` keeps session state in memory, so Python parity must preserve session ownership and cleanup behavior.
- The OpenAI and Gemini integrations are stateful and must preserve the existing tool-call loop semantics.
- `McpGateway` is easy to misclassify as an API; treating it as a service during migration would add an unnecessary architecture change.
- The MCP servers are thin at the transport layer but contain important formatting and downstream-auth behavior that needs to be preserved.
- Capability discovery is intentionally tolerant today, so the Python port should not regress by requiring every MCP server to implement both `tools/list` and `prompts/list`.

## Recommendation

Proceed with a backend-only Python conversion using:

1. one Python host service
2. one shared Python gateway package
3. one Python UW MCP server
4. one Python DevOps MCP server

That is the closest match to the latest `.NET` implementation and the lowest-risk migration path.
