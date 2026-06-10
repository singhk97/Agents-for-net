# TeamsMiddlewareSample — Command Reference

## Extension Path (Teams SDK handlers in `MyTeamsBot`)

Commands handled natively by the Teams SDK through the `TeamsExtensionMiddleware`.

| Command | Feature | Status |
|---------|---------|--------|
| `help` | Adaptive Card with FactSet listing all commands | Done |
| `cards` | Adaptive Card with `Action.Execute` invoke | Done |
| `citation` | AI label, citations, sensitivity label, feedback buttons | Done |
| `stream` | `TeamsStreamingWriter` with informative updates and chunked text | Done |
| `react` | Bot adds/removes emoji reactions via `Api.Conversations.Reactions` | Done |
| `quote` | Bot quotes its own message via `context.Quote()` | Done |
| `proactive` | Delayed proactive message via `TeamsBotApplication.SendAsync()` | Done |
| `task` | Task module: card triggers `task/fetch` then `task/submit` | Done |
| `turn context` | Access Agent SDK `ITurnContext` from a Teams SDK handler | Done |
| `sso` / `oauth` | SSO sign-in flow via `context.SignInAsync()` | Pending |
| Message extension (query) | Search-based message extension via compose box | Pending |
| Message extension (action) | Action-based message extension with task module | Pending |
| `send email` | Send email via Microsoft Graph from a Teams SDK handler | Pending |

## Host Path (Agent SDK handlers in `MyAgent`)

Commands handled by the Agent SDK's `ITurnContext`, using injected `MyTeamsBot` (`TeamsBotApplication`) for Teams-specific operations.

| Command | Feature | Status |
|---------|---------|--------|
| `agents react` | Send via Agent SDK, add/remove reaction via `TeamsBotApplication.Api` | Done |
| `agents proactive` | Proactive message via `TeamsBotApplication.SendAsync()` | Done |
| `agents citation` | Build citation with Teams SDK types, send via `TeamsBotApplication.Api` | Done |

## Background Handlers (no command needed)

| Trigger | Handler | Location | Status |
|---------|---------|----------|--------|
| Reaction on any message | `OnMessageReaction` | `MyTeamsBot` | Done |
| Feedback button click | `OnMessageSubmitFeedback` | `MyTeamsBot` | Done |
| Members added | `OnMembersAdded` | Both | Done |
| Meeting start | `OnMeetingStart` | `MyTeamsBot` | Done |
| Meeting end | `OnMeetingEnd` | `MyTeamsBot` | Done |
| Any unmatched message | Echo reply | `MyAgent` | Done |
