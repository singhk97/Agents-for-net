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
