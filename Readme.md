
## Definitions

### Task

A task is a collection of jobs (shafts), and is usually processed by a dispatcher.

### Job

A job is shaft, processed by an individual agent. The dispatcher will usually assign a job to the individual agent.

## Protocol Messages

### Unicast

#### `miners.resume` (Dispatching)

- Sent from the dispatcher to all agents of the current task group.
- Instructs the agents to resume work.
- Payload is the normal vector to the mining plane.
- TODO: How do agents react?

### `miners.normal` (Dispatching)

- Sent from the dispatcher to all agents of the current task group when starting a new task.
- Shall be sent before `command`/`mine`.
- Informs about the normal vector of the mining plane.

### `command`

- Sent from the dispatcher to the agent to issue a single command.
- The parameter contains the command to execute.
  - `mine` is sent when starting a new task. TODO: Will agents ask for a shaft assignment? 

### Broadcast

#### `command:halt`

- Broadcasted from the dispatcher on channel `miners.command`.
- Instructs the agents to halt and clear state. (Emergency Stop)
- TODO: How do agents react?

#### `command:clear-storage-state`

- Broadcasted from the dispatcher on channel `miners.command`.
- Instructs the agents to clear their internal state.

#### `command:dispatch`

- Broadcasted from the dispatcher on channel `miners.command`.
- TODO
- Implements the "purge locks" functionality.

#### `command:force-finish`

- Broadcasted from the dispatcher on channel `miners.command`.
- Instructs the agents to stop work and return to base.
- Implements the recall functionality.