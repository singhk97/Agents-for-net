// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Agents.Builder;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Teams.Core.Schema;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TeamsMiddlewareSample;

/// <summary>
/// Middleware that intercepts incoming activities and routes Teams-channel
/// traffic to a <see cref="Microsoft.Teams.Apps.TeamsBotApplication"/> instead of letting it
/// continue through the Agent SDK pipeline.
/// </summary>
/// <remarks>
/// Register this as a singleton <see cref="IMiddleware"/> in DI.  Because the
/// <see cref="Microsoft.Agents.Hosting.AspNetCore.CloudAdapter"/> constructor
/// takes <c>IMiddleware[]</c> (and .NET DI does not auto-resolve array types),
/// you must also register <c>IMiddleware[]</c> explicitly — see <c>Program.cs</c>.
/// </remarks>
public class TeamsRouterMiddleware : IMiddleware
{
    private readonly MyTeamsBot _teamsBot;
    private readonly ILogger<TeamsRouterMiddleware> _logger;

    public TeamsRouterMiddleware(MyTeamsBot teamsBot, ILogger<TeamsRouterMiddleware> logger)
    {
        _teamsBot = teamsBot ?? throw new ArgumentNullException(nameof(teamsBot));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task OnTurnAsync(ITurnContext turnContext, NextDelegate next, CancellationToken cancellationToken = default)
    {
        if (turnContext.Activity.ChannelId == Channels.Msteams)
        {
            _logger.LogDebug("TeamsRouterMiddleware: routing msteams activity {ActivityId} to Teams SDK", turnContext.Activity.Id);

            // Bridge: serialize the Agent SDK IActivity to JSON, then deserialize
            // into the Teams SDK activity model.  Both SDKs implement the same
            // Activity Protocol wire format, so the conversion is lossless.
            string activityJson = ProtocolJsonSerializer.ToJson(turnContext.Activity);
            CoreActivity coreActivity = CoreActivity.FromJsonString(activityJson);

            // Invoke the Teams SDK's OnActivity handler directly.
            // The Teams SDK's BotApplication.SendActivityAsync uses its own
            // ConversationClient to send responses — it does not need the Agent
            // SDK's ITurnContext.SendActivityAsync.
            if (_teamsBot.OnActivity != null)
            {
                await _teamsBot.OnActivity(coreActivity, cancellationToken);
            }

            // Short-circuit: do NOT call next() — Teams SDK handled this activity.
            return;
        }

        // Non-Teams channels continue to the Agent SDK pipeline.
        await next(cancellationToken);
    }
}
