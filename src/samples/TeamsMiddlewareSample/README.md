# TeamsMiddlewareSample

This sample demonstrates how the **Microsoft Teams SDK** and the **Microsoft Agents SDK** can coexist in a single ASP.NET Core application, sharing a single bot registration and authentication configuration.

## Architecture

Both SDKs receive traffic through the same `/api/messages` endpoint. An Agent SDK middleware inspects each incoming activity and routes it to the appropriate SDK based on `channelId`.

```
                         ┌──────────────────────┐
   Teams / Bot Service   │  POST /api/messages   │
   ─────────────────────>│  (CloudAdapter)       │
                         └──────────┬───────────┘
                                    │
                         ┌──────────▼───────────┐
                         │TeamsExtensionMiddleware│
                         │ (IMiddleware)         │
                         └──────────┬───────────┘
                                    │
                    ┌───────────────┴───────────────┐
                    │                               │
            channelId == "msteams"          all other channels
                    │                               │
         ┌──────────▼──────────┐         ┌──────────▼──────────┐
         │  MyTeamsBot         │         │  MyAgent             │
         │  (Teams SDK)        │         │  (Agent SDK)         │
         └──────────┬──────────┘         └──────────┬──────────┘
                    │                               │
         ┌──────────▼──────────┐         ┌──────────▼──────────┐
         │  ConversationClient │         │  CloudAdapter        │
         │  + AgentSdkAuth-    │         │  (built-in outbound  │
         │    Handler          │         │   auth)              │
         └─────────────────────┘         └─────────────────────┘
                    │                               │
                    └───────────┬───────────────────┘
                                │
                    ┌───────────▼───────────┐
                    │  Agent SDK IConnections│
                    │  (shared auth config) │
                    └───────────────────────┘
```

### Request flow

1. **Inbound**: The Bot Framework Service (or Teams) sends an activity to `POST /api/messages`. The Agent SDK's `CloudAdapter` authenticates the JWT token using the `TokenValidation` config, then runs the middleware pipeline.

2. **Routing**: `TeamsExtensionMiddleware` checks `channelId`. If it is `msteams`, the activity is serialized to JSON and deserialized into the Teams SDK's `CoreActivity` model (both SDKs implement the same Activity Protocol wire format, so the conversion is lossless). The middleware then invokes `MyTeamsBot.OnActivity` and short-circuits the pipeline. For all other channels, `next()` is called and the activity reaches `MyAgent`.

3. **Outbound**: When the Teams SDK's `ConversationClient` makes outbound HTTP calls (e.g., sending a reply), the `AgentSdkAuthHandler` intercepts the request, acquires a Bearer token from the Agent SDK's `IConnections`, and sets the `Authorization` header. This means a single `Connections` / `ConnectionsMap` config drives auth for both SDKs.

## What comes from where

The Teams Extension plugs into the Agent SDK's hosting and auth infrastructure while keeping its own application-layer types.

### Teams Extension reuses from Agent SDK

| Capability | Agent SDK component | How the Teams Extension uses it |
|---|---|---|
| Inbound token validation | `AddAgentAspNetAuthentication` / JWT Bearer middleware | CloudAdapter validates the Bot Framework JWT on every incoming request before the activity reaches the Teams Extension via middleware. |
| Credential configuration | `Connections` + `ConnectionsMap` in `appsettings.json` | Single source of truth for ClientId, ClientSecret, and Scopes — no separate `AzureAd` section needed. |
| Outbound authentication | `IConnections` / `IAccessTokenProvider` | `AgentSdkAuthHandler` calls `GetTokenProvider()` then `GetAccessTokenAsync()` to add Bearer tokens to the Teams Extension's outbound HTTP calls. |
| Middleware pipeline | `CloudAdapter` + `IMiddleware` | `TeamsExtensionMiddleware` is an Agent SDK `IMiddleware` that intercepts activities before they reach `MyAgent`. |
| HTTP endpoint | `MapAgentApplicationEndpoints` (`/api/messages`) | Both SDKs share a single endpoint. The CloudAdapter receives all traffic; the middleware decides which SDK handles it. |
| Activity Protocol parsing | `CloudAdapter.ProcessAsync` | Deserializes the HTTP body into an `IActivity`, validates the protocol envelope, and extracts `ClaimsIdentity` — all before the Teams Extension sees the activity. |
| Bot identity | Shared bot registration (same ClientId) | Both SDKs operate under one Azure bot registration. No separate app registrations. |

### Teams Extension uses its own

| Capability | Teams Extension component | Notes |
|---|---|---|
| Routing & handler registration | `TeamsBotApplication.OnMessage()`, `OnMembersAdded()`, etc. | Fluent handler registration with typed contexts — independent of Agent SDK's `OnActivity()` routes. |
| Turn context | `TeamsBotApplication` context (via `ApiClient`) | The Teams Extension manages its own request-scoped context, separate from Agent SDK's `ITurnContext`. |
| Activity model | `CoreActivity` (`Microsoft.Teams.Core.Schema`) | The Teams Extension has its own activity type hierarchy with polymorphic deserialization. The middleware bridges between the two models via JSON serialization. |
| Conversation client | `ConversationClient` (`Microsoft.Teams.Core`) | Sends, updates, and deletes activities. Uses the shared `HttpClient` (with `AgentSdkAuthHandler`) for outbound auth. |
| User token client | `UserTokenClient` (`Microsoft.Teams.Core`) | Manages OAuth user tokens (sign-in, sign-out, token exchange). |
| API client facade | `ApiClient` (`Microsoft.Teams.Apps.Api.Clients`) | Top-level facade exposing `Conversations`, `Users`, `Teams`, `Meetings` sub-clients. |

#### Why the Teams Extension owns these layers

These components are where Teams-specific features live. Keeping them in the Teams Extension allows the Teams platform to evolve independently of the Agent SDK:

- **Routing & handler registration** — When Teams introduces a new activity type (e.g., suggested actions, adaptive card universal actions), the Teams Extension adds a new handler method without waiting for the Agent SDK to update its generic routing. Developers get `OnSuggestedAction()` instead of manually filtering `OnActivity()`.
- **Activity model** — Teams-specific payloads like targeted messaging, meeting events, or message extension responses require first-class schema types. `CoreActivity` and its subtypes model these natively with polymorphic deserialization, rather than relying on untyped `ChannelData` dictionaries.
- **Conversation & User Token clients** — When a feature like targeted messaging adds new API endpoints or parameters, the `ConversationClient` is updated to expose them as typed methods. The Teams Extension ships the client update alongside the feature, keeping the two in sync.
- **Turn context** — The Teams Extension's context is scoped to Teams capabilities: it knows about the current team, channel, meeting, and user identity. This richer context enables features like proactive messaging to specific users in a channel without manual plumbing.
- **API client facade** — `ApiClient` surfaces `Conversations`, `Users`, `Teams`, and `Meetings` as dedicated sub-clients. This provides a rich, discoverable development experience where Teams-specific operations are organized by domain rather than flattened into a single generic client.

### Key files

| File | Purpose |
|---|---|
| `Program.cs` | DI registration and app startup. Registers both SDKs and the routing middleware. |
| `TeamsSdkExtensions.cs` | `AddTeamsSdkWithAgentAuth<T>()` extension method that registers the Teams Extension service chain (`ConversationClient`, `UserTokenClient`, `ApiClient`, `T`) using a named `HttpClient` with `AgentSdkAuthHandler`. |
| `AgentSdkAuthHandler.cs` | `DelegatingHandler` that bridges Agent SDK auth (`IConnections` / `IAccessTokenProvider`) into the Teams Extension's outbound HTTP pipeline. |
| `TeamsExtensionMiddleware.cs` | Agent SDK `IMiddleware` that inspects `channelId` and routes `msteams` traffic to the Teams Extension. |
| `MyTeamsBot.cs` | Teams Extension bot (`TeamsBotApplication` subclass) with echo and welcome handlers. |
| `MyAgent.cs` | Agent SDK bot (`AgentApplication` subclass) with echo and welcome handlers. |

## Running locally

1. **Create a bot registration** using the Teams CLI:
   ```bash
   teams app create --name "TeamsMiddlewareSample" \
     --endpoint "https://<your-tunnel>/api/messages" \
     --env appsettings.json --json
   ```

2. **Update `appsettings.json`**: Copy the `ClientId`, `ClientSecret`, and `TenantId` from the `Teams` section (written by the CLI) into the `Connections` and `TokenValidation` sections. Set `TokenValidation.Enabled` to `true`.

3. **Start a dev tunnel**:
   ```bash
   devtunnel create my-tunnel --allow-anonymous
   devtunnel port create my-tunnel -p 3978 --protocol auto
   devtunnel host my-tunnel
   ```

4. **Run the app**:
   ```bash
   dotnet run --project src/samples/TeamsMiddlewareSample --urls http://localhost:3978
   ```

5. **Install in Teams** using the install link from step 1. Send a message — you should see `[Teams SDK] You said: ...` in the reply.
