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
                         │ TeamsRouterMiddleware │
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

2. **Routing**: `TeamsRouterMiddleware` checks `channelId`. If it is `msteams`, the activity is serialized to JSON and deserialized into the Teams SDK's `CoreActivity` model (both SDKs implement the same Activity Protocol wire format, so the conversion is lossless). The middleware then invokes `MyTeamsBot.OnActivity` and short-circuits the pipeline. For all other channels, `next()` is called and the activity reaches `MyAgent`.

3. **Outbound**: When the Teams SDK's `ConversationClient` makes outbound HTTP calls (e.g., sending a reply), the `AgentSdkAuthHandler` intercepts the request, acquires a Bearer token from the Agent SDK's `IConnections`, and sets the `Authorization` header. This means a single `Connections` / `ConnectionsMap` config drives auth for both SDKs.

## Key files

| File | Purpose |
|---|---|
| `Program.cs` | DI registration and app startup. Registers both SDKs and the routing middleware. |
| `TeamsSdkExtensions.cs` | `AddTeamsSdkWithAgentAuth<T>()` extension method that registers the Teams SDK service chain (`ConversationClient`, `UserTokenClient`, `ApiClient`, `T`) using a named `HttpClient` with `AgentSdkAuthHandler`. |
| `AgentSdkAuthHandler.cs` | `DelegatingHandler` that bridges Agent SDK auth (`IConnections` / `IAccessTokenProvider`) into the Teams SDK's outbound HTTP pipeline. |
| `TeamsRouterMiddleware.cs` | Agent SDK `IMiddleware` that inspects `channelId` and routes `msteams` traffic to the Teams SDK. |
| `MyTeamsBot.cs` | Teams SDK bot (`TeamsBotApplication` subclass) with echo and welcome handlers. |
| `MyAgent.cs` | Agent SDK bot (`AgentApplication` subclass) with echo and welcome handlers. |

## What comes from where

The Teams SDK plugs into the Agent SDK's hosting and auth infrastructure while keeping its own application-layer types.

### Teams SDK reuses from Agent SDK

| Capability | Agent SDK component | How the Teams SDK uses it |
|---|---|---|
| Inbound token validation | `AddAgentAspNetAuthentication` / JWT Bearer middleware | CloudAdapter validates the Bot Framework JWT on every incoming request before the activity reaches the Teams SDK via middleware. |
| Credential configuration | `Connections` + `ConnectionsMap` in `appsettings.json` | Single source of truth for ClientId, ClientSecret, and Scopes — no separate `AzureAd` section needed. |
| Outbound authentication | `IConnections` / `IAccessTokenProvider` | `AgentSdkAuthHandler` calls `GetTokenProvider()` then `GetAccessTokenAsync()` to add Bearer tokens to the Teams SDK's outbound HTTP calls. |
| Middleware pipeline | `CloudAdapter` + `IMiddleware` | `TeamsRouterMiddleware` is an Agent SDK `IMiddleware` that intercepts activities before they reach `MyAgent`. |
| HTTP endpoint | `MapAgentApplicationEndpoints` (`/api/messages`) | Both SDKs share a single endpoint. The CloudAdapter receives all traffic; the middleware decides which SDK handles it. |
| Activity Protocol parsing | `CloudAdapter.ProcessAsync` | Deserializes the HTTP body into an `IActivity`, validates the protocol envelope, and extracts `ClaimsIdentity` — all before the Teams SDK sees the activity. |
| Bot identity | Shared bot registration (same ClientId) | Both SDKs operate under one Azure bot registration. No separate app registrations. |
| Async task queuing | `IActivityTaskQueue` / `HostedActivityService` | CloudAdapter queues activities for background processing (Normal delivery mode). The Teams SDK benefits from this without any extra setup. |
| Health-check endpoint | `MapAgentRootEndpoint` (`GET /`) | Returns assembly name and version — available to both SDKs. |

### Teams SDK uses its own

| Capability | Teams SDK component | Notes |
|---|---|---|
| Routing & handler registration | `TeamsBotApplication.OnMessage()`, `OnMembersAdded()`, etc. | Fluent handler registration with typed contexts — independent of Agent SDK's `OnActivity()` routes. |
| Turn context | `TeamsBotApplication` context (via `ApiClient`) | The Teams SDK manages its own request-scoped context, separate from Agent SDK's `ITurnContext`. |
| Activity model | `CoreActivity` (`Microsoft.Teams.Core.Schema`) | The Teams SDK has its own activity type hierarchy with polymorphic deserialization. The middleware bridges between the two models via JSON serialization. |
| Conversation client | `ConversationClient` (`Microsoft.Teams.Core`) | Sends, updates, and deletes activities. Uses the shared `HttpClient` (with `AgentSdkAuthHandler`) for outbound auth. |
| User token client | `UserTokenClient` (`Microsoft.Teams.Core`) | Manages OAuth user tokens (sign-in, sign-out, token exchange). |
| API client facade | `ApiClient` (`Microsoft.Teams.Apps.Api.Clients`) | Top-level facade exposing `Conversations`, `Users`, `Teams`, `Meetings` sub-clients. |
| State management | Teams SDK internal state | The Teams SDK does not use Agent SDK's `IStorage` / `ITurnState`. Each SDK manages its own state independently. |

## Shared authentication

Instead of configuring a separate `AzureAd` section for the Teams SDK, this sample reuses the Agent SDK's existing auth:

```
appsettings.json
├── TokenValidation      → Validates inbound JWT tokens (used by CloudAdapter)
├── Connections           → Defines named auth connections (ClientId, Secret, Scopes)
└── ConnectionsMap        → Maps service URLs to connections (wildcard "*" matches all)
```

The `AgentSdkAuthHandler` uses `IConnections.GetTokenProvider()` to find the right connection for each outbound request, then calls `IAccessTokenProvider.GetAccessTokenAsync()` to get a token. This eliminates duplicate credential configuration.

## IMiddleware[] registration

The `CloudAdapter` constructor accepts an optional `IMiddleware[]` parameter. .NET's built-in DI container does not auto-resolve array types from individually registered services, so `Program.cs` explicitly registers the array:

```csharp
builder.Services.AddSingleton<IMiddleware, TeamsRouterMiddleware>();
builder.Services.AddSingleton<IMiddleware[]>(sp =>
    sp.GetServices<IMiddleware>().ToArray());
```

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
