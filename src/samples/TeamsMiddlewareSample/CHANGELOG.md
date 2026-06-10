# Changelog

All notable changes to the TeamsMiddlewareSample are documented here.

## 2026-06-10 — Host path: Agent SDK handlers using Teams SDK services

**What changed:**
- **MyAgent.cs**: Expanded from a plain echo bot into a dual-path showcase. `MyAgent` now injects `MyTeamsBot` (which extends `TeamsBotApplication`) and uses it to access Teams SDK capabilities from Agent SDK turn handlers.
- **`agents proactive`**: Uses `TeamsBotApplication.SendAsync()` to send a delayed proactive message from an Agent SDK handler — demonstrating that proactive messaging doesn't require a Teams SDK context.
- **`agents react`**: Sends a message via Agent SDK's `ITurnContext`, then uses `TeamsBotApplication.Api.ForServiceUrl(...).Conversations.Reactions` to add and remove a reaction on it.
- **`agents citation`**: Builds a full citation message using Teams SDK types (`TeamsActivity.CreateBuilder()`, `CitationAppearance`, `AddAIGenerated`, `AddFeedback`), then sends it via `TeamsBotApplication.Api.ForServiceUrl(...).Conversations.Activities.CreateAsync()`.
- **`turn context`** (in `MyTeamsBot`): Demonstrates the reverse — a Teams SDK handler accessing the Agent SDK `ITurnContext` via `TeamsExtensionMiddleware.CurrentTurnContext` to send a message through the Agent SDK pipeline.
- **Catch-all echo moved to Agent SDK**: The generic `OnMessage` echo handler was removed from `MyTeamsBot` and now lives in `MyAgent.OnMessageAsync`. Messages that don't match any Teams SDK route fall through the middleware naturally.

**Why:**
The previous change established the Extension path (Teams SDK handlers in `MyTeamsBot`). This change completes the dual-path architecture by implementing the Host path: Agent SDK handlers that reach into Teams SDK services via DI. The two paths coexist behind one endpoint — `HasMatchingRoute` determines the split. This proves that developers can choose per-feature which SDK owns the turn while sharing authentication, HTTP pipeline, and bot registration.

## 2026-06-10 — MyTeamsBot feature showcase and dual-path architecture

**What changed:**
- **MyTeamsBot.cs**: Expanded from a basic echo bot into a comprehensive feature showcase. Each command demonstrates a different Teams SDK capability: `help` (Adaptive Card FactSet), `cards` (Action.Execute invoke), `citation` (AI labels, citations, sensitivity label, feedback buttons), `stream` (TeamsStreamingWriter with informative updates), `react` (bot-initiated emoji reactions via API), `quote` (quoted replies), `proactive` (delayed out-of-turn message via TeamsBotApplication.SendAsync), `task` (task module fetch/submit flow). Background handlers cover message reactions, feedback submission, welcome, and meeting start/end.

**Architecture — two paths for Teams SDK features:**

The sample demonstrates two complementary ways to use Teams SDK capabilities, coexisting in a single app behind one `/api/messages` endpoint:

| Path | Entry point | Turn owner | Teams SDK access | Best for |
|------|-------------|------------|------------------|----------|
| **Extension** | `MyTeamsBot` | Teams SDK (`Context<T>`) | Native — handlers, streaming writer, routing | Features that need the full Teams SDK lifecycle |
| **Host** | `MyAgent` | Agent SDK (`ITurnContext`) | Via DI — inject `ApiClient`, `MyTeamsBot` | Agent SDK logic that needs Teams-specific APIs |

The `TeamsExtensionMiddleware.HasMatchingRoute` check determines which path an activity takes: commands registered in `MyTeamsBot` are handled by the Teams SDK; everything else falls through to `MyAgent`. Both paths share the same authentication, HTTP pipeline, and bot registration.

The Extension path commands (`help`, `cards`, `citation`, `stream`, `react`, `quote`, `proactive`, `task`) are implemented in this change. The Host path — Agent SDK handlers using injected Teams SDK services — is the next milestone.

## 2026-06-10 — Route-aware middleware: only dispatch to Teams SDK when a handler matches

**What changed:**
- **Teams SDK** (`Router.cs`): Added `IsMatch(TeamsActivity)` — returns whether any registered route matches the activity.
- **Teams SDK** (`TeamsBotApplication.cs`): Added public `HasMatchingRoute(CoreActivity)` — converts the activity and delegates to `Router.IsMatch`, keeping `Router` internal.
- **Sample** (`TeamsExtensionMiddleware.cs`): The middleware now calls `_teamsBot.HasMatchingRoute(coreActivity)` before dispatching. Activities with no matching Teams SDK route fall through to the Agent SDK pipeline via `next()`. The hardcoded `"agents"` text skip has been removed.

**Why:**
Previously the middleware routed all `msteams` activities to the Teams SDK unconditionally, with a hardcoded text check to skip specific messages. This was brittle — any activity type the Teams bot didn't handle would be silently swallowed instead of reaching the Agent SDK. The route-match check makes the middleware declarative: the bot's registered handlers determine what gets routed, and everything else flows through naturally.

## 2026-06-08 — Pass Agent SDK ITurnContext to Teams SDK handlers via Context.Properties

**What changed:**
- **Teams SDK** (`Context.cs`): Added `Dictionary<string, object?> Properties` to `Context<TActivity>` — a per-turn property bag that handlers can read and write.
- **Teams SDK** (`TeamsBotApplication.cs`): After creating the context, string-keyed entries from `HttpContext.Items` are copied into `Context.Properties`. This allows any hosting middleware to pass objects into Teams handlers without changing the `OnActivity` delegate signature.
- **Sample** (`TeamsExtensionMiddleware.cs`): Before calling `OnActivity`, the middleware stashes the Agent SDK `ITurnContext` in `HttpContext.Items` under the well-known key `"AgentSdk.TurnContext"`.
- **Sample** (`MyTeamsBot.cs`): The `OnMessage` handler retrieves the Agent SDK `ITurnContext` from `teamsContext.Properties` and confirms its presence in the echo reply.

**Why:**
The Teams Extension middleware bridges the two SDKs at the activity level (JSON serialization), but the Agent SDK's `ITurnContext` carries live runtime capabilities — `SendActivityAsync`, `Services` property bag, `TurnState` — that don't survive JSON serialization. Without this change, Teams SDK handlers have no access to the host framework's turn-scoped state. The `HttpContext.Items` → `Context.Properties` bridge passes the live object through the same HTTP request without coupling either SDK to the other's types. The Teams SDK change is generic (any string-keyed `HttpContext.Items` entry flows through), so it supports future hosting scenarios beyond the Agent SDK.

## 2026-06-05 — Initial sample: Teams SDK + Agent SDK side-by-side

**What changed:**
- Added the complete sample: `Program.cs`, `MyAgent.cs`, `MyTeamsBot.cs`, `TeamsRouterMiddleware.cs` (later renamed), `AgentSdkAuthHandler.cs`, `TeamsSdkExtensions.cs`, `AspNetExtensions.cs`, `appsettings.json`, project file, and README.
- Single ASP.NET Core app hosting both SDKs behind one `/api/messages` endpoint with shared bot registration and credentials.
- `TeamsRouterMiddleware` (Agent SDK `IMiddleware`) routes `msteams` traffic to the Teams SDK; all other channels fall through to the Agent SDK.
- `AgentSdkAuthHandler` bridges Agent SDK `IConnections`/`IAccessTokenProvider` into the Teams SDK's outbound HTTP pipeline, eliminating the need for a separate `AzureAd` configuration section.
- `AddTeamsSdkWithAgentAuth<T>()` extension method encapsulates DI registration for `ConversationClient`, `UserTokenClient`, `ApiClient`, and the bot class.

**Why:**
This sample exists to prove out the integration pattern where the Teams Extension plugs into the Agent SDK's hosting and authentication infrastructure while keeping its own application-layer types (activity model, routing, clients, context). The goal is to demonstrate that both SDKs can coexist in a single app with zero credential duplication and a single network endpoint, establishing the middleware-based boundary that lets the Teams Extension ship independently of the Agent SDK.

`be956b05`
