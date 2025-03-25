# Pausable

This sample demonstrates a **weird** bug when you *pause* the worklfow and then *resume* it **twice**.

## Steps to reproduce

Run the temporal server

    temporal server start-dev

Open the temporal UI dashboard.

Run the following from this directory in a separate terminal to start the worker:

    dotnet run worker

Then in another terminal, run the workflow from this directory:
    
    dotnet run workflow

In the temporal UI dashboard, click on the workflow run ID to see the details of the execution.

The workflow will *pause*. Then in another terminal, run the *resume* from this directory:
    
    dotnet run resume

The workflow will *resume* and then *pause* **again**. Then run:
    
    dotnet run resume

Immediately after you run *resume* again, click *"back to workflows"* link and then click on the workflow run ID again several times. Do this quickly.


## Video

https://github.com/user-attachments/assets/07ffca84-02f4-48f2-82a9-5a2047c7f99e
