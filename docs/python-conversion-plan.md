# Python Conversion Plan

## Current State

- `McpHost` is the public backend API. It owns chat sessions, discovers MCP tools/prompts, calls the LLM, and routes tool calls to MCP servers.
- `McpGateway` is not a standalone app. It is a shared client library used by `McpHost` to connect to remote MCP servers.
- `Servers/UwMcp` is an MCP HTTP server for underwriting tools.
- `Servers/DevOpsMcp` is an MCP HTTP server for DevOps/TFS tools.
- `MyChat` is an Angular frontend, not a `.NET` app.

## Important Decision

The requested target is "3 separate Python applications: gateway, host, and chat", but the current implementation does not map cleanly to that:

- `gateway` is currently a library, not a deployed service.
- the two deployed MCP tool services are `UwMcp` and `DevOpsMcp`.
- `chat` is currently Angular, so converting it to Python is a frontend rewrite, not a language port.

Because of that, there are two viable migration shapes.

## Recommended Target Shape

Preserve behavior first, then simplify later:

1. `python-host`
   Public HTTP API replacing `McpHost`.
2. `python-uw-mcp`
   Standalone Python MCP server replacing `Servers/UwMcp`.
3. `python-devops-mcp`
   Standalone Python MCP server replacing `Servers/DevOpsMcp`.
4. `python-chat`
   Python web UI replacing `MyChat`.
5. `python-gateway`
   Shared Python package, not an app, used by `python-host` for MCP discovery and tool execution.

This is the lowest-risk path because it preserves the current runtime boundaries and request flow.

## Alternate 3-App Shape

If you want exactly 3 Python applications named `gateway`, `host`, and `chat`, then the architecture changes:

1. `python-gateway`
   New backend service that absorbs the current MCP server responsibilities or proxies them.
2. `python-host`
   Chat/session API and LLM orchestration.
3. `python-chat`
   Python web UI.

This is feasible, but it is not a direct conversion. It is a redesign.

## Proposed Folder Strategy

Create a new sibling workspace:

- `d:\Danzy\Code\2026\MCP Pytthon`

Recommended structure inside it after approval:

```text
MCP Pytthon/
  host/
  gateway/
  chat/
  uw_mcp/
  devops_mcp/
  shared/
  docs/
  tests/
```

If you approve the strict 3-app redesign, `uw_mcp` and `devops_mcp` would be folded into `gateway`.

## Migration Phases

### Phase 1: Baseline and contracts

- Freeze the current HTTP and MCP contracts.
- Document request/response payloads for `/chat`, `/sessions`, `/chat/close`, `/prompts`, `/prompts/render`, `/health`, and `/mcp`.
- Record environment variables and secret dependencies.
- Capture current test coverage and missing cases.

### Phase 2: Python architecture skeleton

- Choose Python stack:
  - backend services: `FastAPI`
  - MCP servers: Python MCP SDK
  - chat UI: either `FastAPI + Jinja + HTMX` or a Python UI framework
- Create app skeletons, config loading, structured logging, health endpoints, and local run scripts.
- Define shared models for chat, prompts, sessions, tool calls, and server configs.

### Phase 3: Host conversion

- Port `AppConfig` and env override behavior.
- Port session lifecycle and TTL cleanup.
- Port MCP discovery, prompt lookup, and tool routing.
- Port OpenAI and Gemini integrations.
- Preserve correlation-id propagation.

### Phase 4: MCP server conversion

- Port underwriting tools and APIM integration into `python-uw-mcp`.
- Port DevOps/TFS tools and PAT handling into `python-devops-mcp`.
- Keep `/health` and `/mcp` behavior aligned with the current services.

### Phase 5: Chat conversion

- Rebuild `MyChat` as a Python-served UI.
- Preserve session reuse behavior and runtime-configurable backend URL.
- Recreate the current send-on-enter behavior and message history view.

### Phase 6: Verification and cutover

- Add integration tests across host, MCP servers, and UI.
- Run side-by-side comparisons between `.NET` and Python outputs.
- Switch local compose/dev scripts to the Python stack.
- Retire `.NET` services only after parity is confirmed.

## Risks

- Replacing Angular with Python UI is a rewrite, not a mechanical port.
- The current host keeps session state in memory; parity requires careful session ownership and cleanup in Python.
- The OpenAI and Gemini integrations are stateful and must preserve tool-call loops exactly.
- If `gateway` must become a real service, that introduces an architecture change on top of the language migration.

## Recommendation

Approve one of these before implementation starts:

1. parity-first migration to 4 Python apps plus 1 shared gateway package
2. strict 3-app redesign using `gateway`, `host`, and `chat`

I recommend option 1 first, then a later consolidation if you still want only 3 deployables.
