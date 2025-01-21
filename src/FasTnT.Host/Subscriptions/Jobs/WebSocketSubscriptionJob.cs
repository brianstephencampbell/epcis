﻿using FasTnT.Application.Services.Notifications;
using FasTnT.Application.Services.Subscriptions;
using FasTnT.Application.Services.Subscriptions.Schedulers;
using FasTnT.Domain.Model.Events;
using FasTnT.Domain.Model.Queries;
using FasTnT.Host.Extensions;
using FasTnT.Host.Subscriptions.Formatters;
using Microsoft.EntityFrameworkCore;
using System.Net.WebSockets;

namespace FasTnT.Host.Subscriptions.Jobs;

public class WebSocketSubscriptionJob(
    WebSocket webSocket, 
    string queryName, 
    IEnumerable<QueryParameter> parameters, 
    SubscriptionScheduler scheduler,
    ICaptureListener eventListener)
{
    public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var bufferRequestIds = Array.Empty<int>();
        var lastExecutionDate = DateTime.UtcNow;
        eventListener.OnCapture += scheduler.OnRequestCaptured;

        try
        {
            while (!scheduler.Stopped)
            {
                var executionDate = DateTime.UtcNow;

                if (scheduler.IsDue())
                {
                    try
                    {
                        var minRecordDate = lastExecutionDate.Subtract(TimeSpan.FromSeconds(10));
                        var executionParameters = parameters.Union(
                        [
                            QueryParameter.Create("GE_recordTime", minRecordDate.ToString())
                        ]);

                        using var scope = serviceProvider.CreateScope();

                        var runner = scope.ServiceProvider.GetService<SubscriptionRunner>();
                        var result = await runner.ExecuteAsync(new(executionParameters, bufferRequestIds), cancellationToken);

                        if (result.Successful)
                        {
                            if (result.Events.Count != 0)
                            {
                                await SendQueryDataAsync(result.Events, cancellationToken);
                            }

                            bufferRequestIds = [.. result.RequestIds];
                            lastExecutionDate = executionDate;
                        }
                    }
                    finally
                    {
                        scheduler.SetNextExecution(executionDate);
                    }
                }

                scheduler.WaitForNotification();
            }
        }
        finally
        {
            eventListener.OnCapture -= scheduler.OnRequestCaptured;
        }
    }

    private async Task SendQueryDataAsync(List<Event> queryData, CancellationToken cancellationToken)
    {
        var response = new QueryResponse(queryName, queryData);
        var formattedResponse = JsonSubscriptionFormatter.Instance.FormatResult("ws-subscription", response);

        await webSocket.SendAsync(formattedResponse, cancellationToken);
    }
}
