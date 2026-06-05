// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Teams.Apps;
using Microsoft.Teams.Apps.Api.Clients;
using Microsoft.Teams.Apps.Handlers;
using System.Threading;
using System.Threading.Tasks;

namespace TeamsMiddlewareSample;

/// <summary>
/// Teams SDK echo bot.  Handles messages arriving from the msteams channel
/// after <see cref="TeamsExtensionMiddleware"/> routes them here.
/// </summary>
public class MyTeamsBot : TeamsBotApplication
{
    public MyTeamsBot(ApiClient api, IHttpContextAccessor accessor, ILogger<MyTeamsBot> logger, TeamsBotApplicationOptions? options = null)
        : base(api, accessor, logger, options)
    {
        this.OnMessage(async (context, cancellationToken) =>
        {
            await context.SendAsync($"[Teams SDK] You said: {context.Activity.Text}", cancellationToken);
        });

        this.OnMembersAdded(async (context, cancellationToken) =>
        {
            await context.SendAsync("Hello from Teams SDK!", cancellationToken);
        });
    }
}
