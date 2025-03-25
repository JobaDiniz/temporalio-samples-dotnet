using System.Security.Cryptography;
using System.Text.Json;
using Temporalio.Activities;

namespace TemporalioSamples.Pausable
{
#pragma warning disable CA1819 // Properties should not return arrays
    public record WorkflowDefinitionResponse(JsonElement Definition, int[] Blocks, int[] Pauses);
#pragma warning restore CA1819 // Properties should not return arrays

    /// <summary>
    /// Represents a JSON serializable exception detail.
    /// </summary>
    public record ExceptionDetail(string Type, string Message, string StackTrace)
    {
        /// <summary>
        /// Indicates whether the exception is a <see cref="CanceledFailureException"/>.
        /// </summary>
        public bool IsCanceled => Type == "CanceledFailureException";

        /// <summary>
        /// Creates an <see cref="ExceptionDetail"/> from an <see cref="Exception"/>.
        /// </summary>
        public static ExceptionDetail? FromException(Exception? ex)
        {
            if (ex == null)
            {
                return null;
            }

            var type = ex.InnerException?.GetType().Name ?? ex.GetType().Name;
            return new ExceptionDetail(type, ex.Message, ex.ToString());
        }
    }

    public class Activities
    {
        [Activity("torch.workflow.definition")]
        public async Task<WorkflowDefinitionResponse> GetDefinitionAsync(string workflowId)
        {
            var cancellation = ActivityExecutionContext.Current.CancellationToken;
            await Task.Delay(TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(3, 6)), cancellation);

            var blocks = Enumerable.Range(1, 3).ToArray();
            var definition = new { Id = workflowId, Prop1 = "prop1" };
#pragma warning disable SA1010 // Opening square brackets should be spaced correctly
            return new WorkflowDefinitionResponse(JsonSerializer.SerializeToElement(definition), blocks, [1, 2]);
#pragma warning restore SA1010 // Opening square brackets should be spaced correctly
        }

        [Activity("torch.block")]
        public async Task<int> ExecuteBlockAsync(int block)
        {
            var cancellation = ActivityExecutionContext.Current.CancellationToken;
            await Task.Delay(TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(500, 1500)), cancellation);
            return block;
        }

        [Activity("torch.webhook.execution.started")]
        public async Task ExecutionStartedAsync(string workflowId, DateTime startedTime)
        {
            var cancellation = ActivityExecutionContext.Current.CancellationToken;
            await Task.Delay(TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(500, 1500)), cancellation);
        }

        [Activity("torch.webhook.execution.ended")]
        public async Task ExecutionEndedAsync(string workflowId, DateTime endedTime, object? result, ExceptionDetail? error)
        {
            var cancellation = ActivityExecutionContext.Current.CancellationToken;
            await Task.Delay(TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(500, 1500)), cancellation);
        }

        [Activity("torch.webhook.execution.paused")]
        public async Task ExecutionPausedAsync(string workflowId, DateTime pausedTime, JsonElement? payload)
        {
            var cancellation = ActivityExecutionContext.Current.CancellationToken;
            await Task.Delay(TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(500, 1500)), cancellation);
        }
    }
}
