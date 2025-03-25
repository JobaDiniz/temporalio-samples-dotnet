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
        private bool isPaused;

        [WorkflowInit]
        public PausableWorkflow(WorkflowParameteres parameters) => this.parameters = parameters;

        [WorkflowRun]
        public async Task<JsonElement> RunAsync(WorkflowParameteres p)
        {
            ((IWorkflowInterpreterEventing)eventing).Subscribe<WorkflowPausedEvent>(OnWorkflowPaused);
            var cancellation = Workflow.CancellationToken;
            var options = new ActivityOptions
            {
                CancellationToken = cancellation,
                StartToCloseTimeout = TimeSpan.FromSeconds(120),
                RetryPolicy = new Temporalio.Common.RetryPolicy { MaximumAttempts = 3 },
            };

            var block = 1;
            await PauseAsync(parameters.WorkflowId, JsonSerializer.SerializeToElement(block));
            await WaitForResumeAsync(cancellation);

            var result = JsonDocument.Parse("{}").RootElement;
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

#pragma warning disable VSTHRD200 // Use "Async" suffix for async methods
        private async Task OnWorkflowPaused(WorkflowPausedEvent @event, CancellationToken cancellation)
#pragma warning restore VSTHRD200 // Use "Async" suffix for async methods
        {
            var options = new ActivityOptions
            {
                StartToCloseTimeout = TimeSpan.FromSeconds(10),
                CancellationToken = cancellation,
            };
            await Workflow.ExecuteActivityAsync((Activities activity) => activity.ExecutionPausedAsync(@event.WorkflowId, DateTime.UtcNow, @event.Payload), options);
        }

        private async Task PauseAsync(string workflowId, JsonElement payload)
        {
            await mutex.WaitOneAsync();
            try
            {
                isPaused = true;
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
