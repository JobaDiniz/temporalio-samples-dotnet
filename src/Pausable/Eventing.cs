using System.Collections.Concurrent;
using System.Text.Json;
using Temporalio.Workflows;

namespace TemporalioSamples.Pausable
{
    public interface IWorkflowInterpreterEventing
    {
        /// <summary>
        /// Subscribes a callback to a <typeparamref name="T"/> event type within the workflow.
        /// </summary>
        /// <typeparam name="T">The type of the event.</typeparam>
        /// <param name="callback">A callback to handle the event.</param>
        WorkflowEventSubscription Subscribe<T>(Func<T, CancellationToken, Task> callback)
            where T : IWorkflowInterpreterEvent;

        /// <summary>
        /// Unsubscribe from an event.
        /// </summary>
        /// <param name="subscription">The specific subscription to unsubscribe.</param>
        void Unsubscribe(WorkflowEventSubscription subscription);
    }

    /// <summary>
    /// Represents a subscription to an event that is published during the lifecycle of the workflow.
    /// </summary>
    /// <param name="callback">Callback to invoke when the event is published.</param>
    public sealed class WorkflowEventSubscription(Func<IWorkflowInterpreterEvent, CancellationToken, Task> callback)
    {
        /// <summary>
        /// The callback to be executed when the event is published.
        /// </summary>
        public Func<IWorkflowInterpreterEvent, CancellationToken, Task> Callback { get; } = callback;
    }

    /// <summary>
    /// Represents an event that is published during the lifecycle of the workflow.
    /// </summary>
#pragma warning disable CA1040 // Avoid empty interfaces
    public interface IWorkflowInterpreterEvent
    {
    }
#pragma warning restore CA1040 // Avoid empty interfaces

    /// <summary>
    /// Represents an event that is published when a workflow has paused.
    /// </summary>
    /// <param name="WorkflowId">The workflow id.</param>
    /// <param name="Payload">Any payload to pass along with this event.</param>
    public record WorkflowPausedEvent(string WorkflowId, JsonElement? Payload = null) : IWorkflowInterpreterEvent;

    /// <summary>
    /// Represents an event that is published when a workflow has resumed.
    /// </summary>
    /// <param name="WorkflowId">The workflow id.</param>
    /// <param name="Payload">Any payload to pass along with this event.</param>
    public record WorkflowResumedEvent(string WorkflowId, JsonElement? Payload = null) : IWorkflowInterpreterEvent;

    ///<inheritdoc cref="IWorkflowInterpreterEventing"/>
    public sealed class WorkflowInterpreterEventing : IWorkflowInterpreterEventing
    {
        private readonly ConcurrentDictionary<Type, List<WorkflowEventSubscription>> eventSubscriptionListLookup = new();
        private readonly ConcurrentDictionary<WorkflowEventSubscription, Type> subscriptionEventTypeLookup = new();

        /// <summary>
        /// Publishes an event to all subscribes of the <typeparamref name="T"/> event type.
        /// </summary>
        /// <typeparam name="T">The type of the event.</typeparam>
        /// <param name="event">The event.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        public async Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IWorkflowInterpreterEvent
        {
            if (eventSubscriptionListLookup.TryGetValue(typeof(T), out var subscriptions))
            {
                var pendingSubscriptionCallbacks = new List<Task>(subscriptions.Count);
                foreach (var subscription in subscriptions.ToArray())
                {
                    var pendingSubscriptionCallback = subscription.Callback(@event, cancellationToken);
                    pendingSubscriptionCallbacks.Add(pendingSubscriptionCallback);
                }
                await Task.WhenAll(pendingSubscriptionCallbacks);
            }
        }

        ///<inheritdoc cref="IWorkflowInterpreterEventing.Subscribe{T}(Func{T, CancellationToken, Task})"/>
        WorkflowEventSubscription IWorkflowInterpreterEventing.Subscribe<T>(Func<T, CancellationToken, Task> callback)
        {
            var subscription = new WorkflowEventSubscription(async (@event, ct) =>
            {
                var typedEvent = (T)@event;
                await callback(typedEvent, ct).ConfigureAwait(false);
            });

            if (eventSubscriptionListLookup.TryGetValue(typeof(T), out var subscriptions))
            {
                subscriptions.Add(subscription);
            }
            else
            {
#pragma warning disable SA1010 // Opening square brackets should be spaced correctly
                if (!eventSubscriptionListLookup.TryAdd(typeof(T), [subscription]))
                {
                    // This code only executes if we try get the subscription list and it fails, and then it is subsequently
                    // added by another thread. In this case we just add our subscription. We don't invert this logic because
                    // we don't want to allocate a list each time someone wants to subscribe to an event.
                    eventSubscriptionListLookup[typeof(T)].Add(subscription);
                }
#pragma warning restore SA1010 // Opening square brackets should be spaced correctly
            }

            subscriptionEventTypeLookup[subscription] = typeof(T);
            return subscription;
        }

        ///<inheritdoc cref="IWorkflowInterpreterEventing.Unsubscribe(WorkflowEventSubscription)"/>
        void IWorkflowInterpreterEventing.Unsubscribe(WorkflowEventSubscription subscription)
        {
            if (subscriptionEventTypeLookup.TryGetValue(subscription, out var eventType))
            {
                if (eventSubscriptionListLookup.TryGetValue(eventType, out var subscriptions))
                {
                    subscriptions.Remove(subscription);
                    subscriptionEventTypeLookup.Remove(subscription, out _);
                }
            }
        }
    }
}
