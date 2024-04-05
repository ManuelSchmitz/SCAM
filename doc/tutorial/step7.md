# Step 7:

1. Undock the drone.
2. Make sure that `forward-gyro` is **not** set to _Override controls_.
3. Remotely control the drone, and fly it to a flat spot in open terrain.
4. Position the drone few meters (~10) above the ground.
5. Execute `command:create-task` on the drone's PB.

The drone should send the designated position to the dispatcher. The dispatcher creates a task, splits it into multiple jobs (shafts) and assigns the first job to the agent.

Congratulations!

[Previous Step](step6.md)