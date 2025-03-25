using System.Text.Json;
using Temporalio.Exceptions;
using Temporalio.Workflows;
using Mutex = Temporalio.Workflows.Mutex;

namespace TemporalioSamples.Pausable
{
    public record WorkflowParameteres(string WorkflowId, JsonElement? Input);

    public record ResumeResponse(string Message);

    public interface IWorkflowInterpreter
    {
        Task<JsonElement> RunAsync(WorkflowParameteres p);

        IWorkflowInterpreterEventing Eventing { get; }
    }

    [Workflow]
    public sealed class PausableWorkflow : IWorkflowInterpreter
    {
        private readonly Mutex mutex = new();
        private readonly WorkflowParameteres parameters;
        private readonly WorkflowInterpreterEventing eventing = new();
        private Queue<int>? blocks;
        private bool isPaused;

        [WorkflowInit]
        public PausableWorkflow(WorkflowParameteres parameters) => this.parameters = parameters;

        [WorkflowRun]
        public async Task<JsonElement> RunAsync(WorkflowParameteres p)
        {
            var cancellation = Workflow.CancellationToken;
            var options = new ActivityOptions
            {
                CancellationToken = cancellation,
                StartToCloseTimeout = TimeSpan.FromSeconds(120),
                RetryPolicy = new Temporalio.Common.RetryPolicy { MaximumAttempts = 3 },
            };

            //await Workflow.ExecuteActivityAsync((Activities activity) => activity.ExecutionStartedAsync(parameters.WorkflowId, DateTime.UtcNow), options);
            var definitionResponse = await Workflow.ExecuteActivityAsync((Activities a) => a.GetDefinitionAsync(parameters.WorkflowId), options);
            blocks = new Queue<int>(definitionResponse.Blocks);

            while (blocks.TryDequeue(out var block))
            {
                var blockResponse = await Workflow.ExecuteActivityAsync((Activities a) => a.ExecuteBlockAsync(block), options);
                if (definitionResponse.Pauses.Contains(block))
                {
                    await PauseAsync(parameters.WorkflowId, JsonSerializer.SerializeToElement(block));
                    await WaitForResumeAsync(cancellation);
                }
            }

            var result = JsonDocument.Parse("{}").RootElement;
            //await Workflow.ExecuteActivityAsync((Activities activity) => activity.ExecutionEndedAsync(parameters.WorkflowId, DateTime.UtcNow, result, null), options);
            return result;
        }

        IWorkflowInterpreterEventing IWorkflowInterpreter.Eventing => eventing;

        [WorkflowQuery("paused")]
        public bool IsPaused => isPaused;

        [WorkflowUpdate("resume")]
        public async Task<ResumeResponse> ResumeAsync(JsonElement? payload)
        {
            if (payload is null)
            {
                throw new ApplicationFailureException($"Failed to resume workflow '{parameters.WorkflowId}' execution '{Workflow.Info.WorkflowId}' because the '{nameof(payload)}' is required and was missing.");
            }

            await mutex.WaitOneAsync();
            try
            {
                var succeeded = await Workflow.WaitConditionAsync(() => parameters != null, TimeSpan.FromMinutes(1), Workflow.CancellationToken);
                if (!succeeded)
                {
                    throw new ApplicationFailureException($"Failed to resume workflow '{parameters.WorkflowId}' execution '{Workflow.Info.WorkflowId}' because the '{nameof(parameters)}' was not initialized.");
                }

                isPaused = false;
                await eventing.PublishAsync(new WorkflowResumedEvent(parameters.WorkflowId, payload), Workflow.CancellationToken);
                return new ResumeResponse("resumed");
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        [WorkflowUpdateValidator(nameof(ResumeAsync))]
        public void ValidateResume(JsonElement? payload)
        {
            if (IsPaused == false)
            {
                throw new ApplicationFailureException("The workflow is not paused and therefore cannot be resumed.");
            }

            if (payload is null)
            {
                throw new ApplicationFailureException($"Failed to resume workflow '{parameters.WorkflowId}' execution '{Workflow.Info.WorkflowId}' because the '{nameof(payload)}' is required and was missing.");
            }

            if (payload.Value.ValueKind != JsonValueKind.Object)
            {
                throw new ApplicationFailureException($"Failed to resume workflow '{parameters.WorkflowId}' execution '{Workflow.Info.WorkflowId}' because the '{nameof(payload)}' must be a 'object' but it's '{payload.Value.ValueKind}'.");
            }
        }

        private async Task PauseAsync(string workflowId, JsonElement payload)
        {
            await mutex.WaitOneAsync();
            try
            {
                isPaused = true;
                var options = new ActivityOptions
                {
                    StartToCloseTimeout = TimeSpan.FromSeconds(10),
                    CancellationToken = Workflow.CancellationToken,
                };
                //await Workflow.ExecuteActivityAsync((Activities activity) => activity.ExecutionPausedAsync(workflowId, DateTime.UtcNow, payload), options);
                await eventing.PublishAsync(new WorkflowPausedEvent(workflowId, payload), Workflow.CancellationToken);
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        private async Task WaitForResumeAsync(CancellationToken cancellation)
        {
            var timeout = TimeSpan.FromDays(15);
            var succeeded = await Workflow.WaitConditionAsync(() => isPaused == false, timeout, cancellation);
            if (!succeeded)
            {
                throw new ApplicationFailureException($"The workflow was paused and did not receive a continuation within {timeout}.");
            }
        }
    }
}
