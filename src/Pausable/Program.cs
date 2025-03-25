using System.Text.Json;
using Microsoft.Extensions.Logging;
using Temporalio.Client;
using Temporalio.Worker;
using TemporalioSamples.Pausable;

// Create a client to localhost on default namespace
var client = await TemporalClient.ConnectAsync(new("localhost:7233")
{
    LoggerFactory = LoggerFactory.Create(builder =>
        builder.
            AddSimpleConsole(options => options.TimestampFormat = "[HH:mm:ss] ").
            SetMinimumLevel(LogLevel.Information)),
});

async Task RunWorkerAsync()
{
    // Cancellation token cancelled on ctrl+c
    using var tokenSource = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        tokenSource.Cancel();
        eventArgs.Cancel = true;
    };

    // Run worker until cancelled
    Console.WriteLine("Running worker");
    var workerOptions = new TemporalWorkerOptions(taskQueue: "pausable-sample")
        .AddAllActivities(new Activities())
        .AddWorkflow<PausableWorkflow>();
#pragma warning disable SA1010 // Opening square brackets should be spaced correctly
    workerOptions.Interceptors = [new Interceptor()];
#pragma warning restore SA1010 // Opening square brackets should be spaced correctly
    using var worker = new TemporalWorker(client, workerOptions);
    try
    {
        await worker.ExecuteAsync(tokenSource.Token);
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("Worker cancelled");
    }
}

async Task ExecuteWorkflowAsync()
{
    var workflowId = "1234";
    Console.WriteLine($"Executing workflow with id={workflowId}");
    var parameters = new WorkflowParameteres(Guid.NewGuid().ToString(), null);
    var result = await client.ExecuteWorkflowAsync(
        (PausableWorkflow wf) => wf.RunAsync(parameters),
        new(id: workflowId, taskQueue: "pausable-sample"));

    Console.WriteLine($"Workflow completed with result: {result}");
}

async Task ResumeWorkflowAsync()
{
    var workflowId = "1234";
    var payload = JsonDocument.Parse("{}").RootElement;
    Console.WriteLine($"Getting the workflow handle={workflowId}");
    var handle = client.GetWorkflowHandle(workflowId);
#pragma warning disable SA1010 // Opening square brackets should be spaced correctly
    var response = await handle.ExecuteUpdateAsync<JsonElement>("resume", [payload]);
    Console.WriteLine($"Workflow resumed with response: {response}");
#pragma warning restore SA1010 // Opening square brackets should be spaced correctly
}

async Task QueryMetadataAsync()
{
    var workflowId = "1234";
    var query = "__temporal_workflow_metadata";
    Console.WriteLine($"Querying '{query}' for workflow with id={workflowId}");
    var handle = client.GetWorkflowHandle(workflowId);
    var response = await handle.QueryAsync<Temporalio.Api.Sdk.V1.WorkflowMetadata>(query, []);
    Console.WriteLine($"Query response: {response}");
}

async Task QueryAndResumeAsync()
{
    var workflowId = "1234";
    var query = "__temporal_workflow_metadata";
    var payload = JsonDocument.Parse("{}").RootElement;
    Console.WriteLine($"Querying ('{query}') and Resuming at the same time for workflow with id={workflowId}");
    var handle = client.GetWorkflowHandle(workflowId);

    var tasks = new List<Task>(2)
    {
        handle.QueryAsync<Temporalio.Api.Sdk.V1.WorkflowMetadata>(query, [])
            .ContinueWith(t => Console.WriteLine($"Query response: {t}")),
        handle.ExecuteUpdateAsync<JsonElement>("resume", [payload])
            .ContinueWith(t => Console.WriteLine($"Workflow resumed with response: {t}")),
    };

    await Task.WhenAll(tasks);
}

switch (args.ElementAtOrDefault(0) ?? "worker")
{
    case "worker":
        await RunWorkerAsync();
        break;
    case "workflow":
        await ExecuteWorkflowAsync();
        break;
    case "resume":
        await ResumeWorkflowAsync();
        break;
    case "query":
        await QueryMetadataAsync();
        break;
    case "query+resume":
        await QueryAndResumeAsync();
        break;
    default:
        throw new ArgumentException("Must pass 'worker' or 'workflow' as the single argument");
}