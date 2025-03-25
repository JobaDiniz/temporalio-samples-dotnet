# Pausable

This sample demonstrates a **weird** bug when you *pause* the worklfow and then *resume* it **twice**.

## Steps to reproduce

Run the temporal server

    temporal server start-dev

Run the following from this directory in a separate terminal to start the worker:

    dotnet run worker

Then in another terminal, run the workflow from this directory:
    
    dotnet run workflow

The workflow will eventually *"pause"* (`Workflow.WaitConditionAsync(() => isPaused == false, timeout, cancellation)`). *You do not need to wait*. In another terminal, run the *query* from this directory:

    dotnet run query

By doing this, sometimes the worker will show the error. If that does not happen, then run the *query* again:
    
    dotnet run query

### Error

```
Temporalio.Worker.WorkflowWorker[0]
      Failed completing activation on  f8468de8-7376-484a-9f8c-eaddfd3a63b7
      System.InvalidOperationException: Completion failure: Lang SDK sent us a malformed workflow completion for run (f8468de8-7376-484a-9f8c-eaddfd3a63b7): Workflow completion had a legacy query response along with other commands. This is not allowed and constitutes an error in the lang SDK. Commands: [WFCommand { variant: QueryResponse(QueryResult { query_id: "legacy_query", variant: Some(Succeeded(QuerySuccess { response: Some([eyAiZGVmaW5pdGlvbiI6IHsgInR5cGUiOiAiUGF1c2E=..cyI6IFsgeyAibmFtZSI6ICJyZXN1bWUiIH0gXSB9IH0=]) })) }), metadata: None }, WFCommand { variant: AddTimer(StartTimer { seq: 1, start_to_fire_timeout: Some(Duration { seconds: 1296000, nanos: 0 }) }), metadata: Some(UserMetadata { summary: Some([IldhaXRDb25kaXRpb25Bc3luYyI=]), details: None }) }]
         at Temporalio.Bridge.Worker.CompleteWorkflowActivationAsync(WorkflowActivationCompletion comp)
         at Temporalio.Worker.WorkflowWorker.HandleActivationAsync(IPayloadCodec codec, WorkflowActivation act)
```

## Video

https://github.com/user-attachments/assets/363e96a7-3edc-489c-bddf-6315fdda5974
