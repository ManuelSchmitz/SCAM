
## Definitions

### Task

A task is a collection of jobs (shafts), and is usually processed by a dispatcher.

### Job

A job is shaft, processed by an individual agent. The dispatcher will usually assign a job to the individual agents.

## Protocol Messages

### Unicast

#### `apck.depart.approach` (ATC)

- Sent from the dispatcher to the agent, giving permission to take off.
- Contains a departure path ("flight plan").

#### `apck.depart.complete` (ATC)

- Sent from the agent to the dispatcher.
- The dispatcher can flag the docking port as available again.

#### `apck.depart.request` (ATC)

- Sent from the agent to the dispatcher to request permission to take off.
- The agent is supposed to wait. (e.g. state `MinerState.WaitingForDocking`)
- TODO: Never used???

#### `apck.docking.approach` (ATC)

- Sent from the dispatcher to the agent, assigning a free docking port.
- Contains an approach path ("flight plan").

#### `apck.docking.request` (ATC)

- Sent from the agent to the dispatcher to request permission to land.
- The agent is supposed to wait. (e.g. state ``)
- TODO

#### `apck.docking.approach` (ATC)

- Sent from the agent to the dispatcher.
- TODO

#### `apck.depart.approach` (ATC)

- Sent from the agent to the dispatcher.
- TODO

#### `miners` (ATC)

- Sent from the dispatcher to the agent in response to a `common-airspace-ask-for-lock` broadcast, granting an airspace lock.
- The payload shall contain `common-airspace-lock-granted:<section>`.

#### `miners.handshake.reply` (Dispatching)

- Sent from the dispatcher to the agent in response to a broadcast to `miners.handshake`.
- Accepts the agent into its service.


#### `miners.normal` (Dispatching)

- Sent from the dispatcher to all agents of the current task group when starting a new task.
- Shall be sent before `command`/`mine`.
- Informs about the normal vector of the mining plane.

#### `miners.resume` (Dispatching)

- Sent from the dispatcher to all agents of the current task group.
- Instructs the agents to resume work.
- Payload is the normal vector to the mining plane.
- TODO: How do agents react?

#### `command`

- Sent from the dispatcher to the agent to issue a single command.
- The parameter contains the command to execute.
  - `mine` is sent when starting a new task. TODO: Will agents ask for a shaft assignment? 

#### `report.request` (Dispatching)

- Sent from the dispatcher to all agents to request a status report on their job.
- The status report is used for the GUI screen.
- Has no parameter.
- TODO: How do agents react?

### Broadcast

- Channel `miners.command` is used by the dispatcher to issue commands to the agents.
- Channel `miners.handshake` is used by the agents to announce their availability to potential dispatchers.
- Channel `miners` is used for air traffic control (ATC).
- Channel `miners.report` is used by the agents to transmit status reports.

#### `common-airspace-ask-for-lock` (ATC)

- Broadcasted from the agent on channel `miners`.
- Asks for takeoff permission to an airspace section.
- The dispatcher can grant the permission/lock by sending an unicast `miners` message with a `common-airspace-lock-granted` payload.

#### `common-airspace-lock-released` (ATC)

- Broadcasted from the agent on channel `miners`.
- Releases an airspace lock to the dispatcher.
- The dispatcher may grant the lock to another, waiting agent.

#### `command:halt`

- Broadcasted from the dispatcher on channel `miners.command`.
- Instructs the agents to halt and clear state. (Emergency Stop)
- TODO: How do agents react?

#### `command:clear-storage-state`

- Broadcasted from the dispatcher on channel `miners.command`.
- Instructs the agents to clear their internal state.

#### `command:dispatch` (Dispatching)

- Broadcasted from the dispatcher on channel `miners.command`.
- TODO
- Implements the "purge locks" functionality.

#### `command:force-finish` (Dispatching)

- Broadcasted from the dispatcher on channel `miners.command`.
- Instructs the agents to stop work and return to base.
- Implements the recall functionality.

#### `miners.handshake` (Dispatching)

- Broadcasted from the agent on channel `miners.handshake`.
- Agent announces itself as ready for deployment.
- Parameter is the group constraint of the agent.
- The dispatcher shall reply with unicast message `miners.handshake.reply` to accept the agent into its service.

