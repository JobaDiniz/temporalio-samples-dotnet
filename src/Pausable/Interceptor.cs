using System.Diagnostics.CodeAnalysis;
using Temporalio.Worker.Interceptors;
using Temporalio.Workflows;

namespace TemporalioSamples.Pausable
{
    public sealed class Interceptor : IWorkerInterceptor
    {
        WorkflowInboundInterceptor IWorkerInterceptor.InterceptWorkflow(WorkflowInboundInterceptor nextInterceptor)
        {
            return new WorkflowInbound(nextInterceptor);
        }

        private sealed class WorkflowInbound(WorkflowInboundInterceptor next) : WorkflowInboundInterceptor(next)
        {
            public override async Task<object?> ExecuteWorkflowAsync(ExecuteWorkflowInput workflowInput)
            {
                if (Workflow.Instance is not IWorkflowInterpreter interpreter)
                {
                    return await base.ExecuteWorkflowAsync(workflowInput);
                }

                if (!TryGetInterpreterParameters(workflowInput, out var parameters))
                {
                    return await base.ExecuteWorkflowAsync(workflowInput);
                }

                object? result = default;
                Exception? error = default;
                var subscription = interpreter.Eventing.Subscribe<WorkflowPausedEvent>(OnWorkflowPaused);
                try
                {
                    var options = new ActivityOptions
                    {
                        CancellationToken = Workflow.CancellationToken,
                        StartToCloseTimeout = TimeSpan.FromSeconds(30),
                        RetryPolicy = new Temporalio.Common.RetryPolicy { MaximumAttempts = 3 },
                    };
                    await Workflow.ExecuteActivityAsync((Activities activity) => activity.ExecutionStartedAsync(parameters.WorkflowId, DateTime.UtcNow), options);
                    result = await base.ExecuteWorkflowAsync(workflowInput);
                }
                catch (Exception ex)
                {
                    error = ex;
                    throw;
                }
                finally
                {
                    interpreter.Eventing.Unsubscribe(subscription);
                    var options = new ActivityOptions
                    {
                        StartToCloseTimeout = TimeSpan.FromSeconds(10),
                        CancellationToken = CancellationToken.None,
                    };
                    var errorDetail = error != null ? ExceptionDetail.FromException(error) : default;
                    await Workflow.ExecuteActivityAsync((Activities activity) => activity.ExecutionEndedAsync(parameters.WorkflowId, DateTime.UtcNow, result, errorDetail), options);
                }

                return result;
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

#pragma warning disable SA1204 // Static elements should appear before instance elements
            private static bool TryGetInterpreterParameters(ExecuteWorkflowInput workflowInput, [NotNullWhen(true)] out WorkflowParameteres? parameters)
#pragma warning restore SA1204 // Static elements should appear before instance elements
            {
                parameters = default;
                if (workflowInput.Args == null || workflowInput.Args.Length == 0)
                {
                    return false;
                }

                if (workflowInput.Args[0] is not WorkflowParameteres interpreterParameters)
                {
                    return false;
                }

                parameters = interpreterParameters;
                return true;
            }
        }
    }
}
