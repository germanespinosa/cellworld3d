# Cellworld3D

Cellworld3D is a Unity 3D environment coupled with a Python backend, designed for behavioral experiments and simulations. Specifically, it implements a "Peeking Bot Evade" predator-prey task where an agent (prey) must reach a goal while evading a predator robot.

## Architecture

The project is split into two main components:

1. **Unity Frontend (3D Environment)**
   - Built in Unity, this component renders the 3D environment, handles player/prey movement input, and manages the visual representation of the agents.
   - It contains `CellworldGameBridge.cs`, a UDP client that communicates with the Python backend. The bridge can automatically launch the backend server when the Unity scene starts.

2. **Python Backend (Experiment Logic)**
   - Located in the `Server/` directory, the backend runs the core experiment logic using the `cellworld_game` framework (`PeekingBotEvade.py`).
   - It tracks the predator and prey dynamics, calculates line of sight, manages the "puff" mechanics (when the predator detects the prey), and records experiment trajectories into JSON files.

## Communication

The frontend and backend communicate asynchronously via a UDP bridge:
- **Python -> Unity (Port 5005)**: Sends experiment state changes, the predator's updated position/rotation, and events like "puff" (predator catches/attacks prey) and "trial finished".
- **Unity -> Python (Port 5006)**: Sends initialization commands, the prey's real-time position/rotation data, and simulation control commands (pause, unpause, stop, reset).

## Experiment Tracking

During a session, experimental data is recorded by the `ExperimentManager`. At the end of an episode, it saves a JSON log (e.g., `episode_X.json` or `HUMAN_DATE_p0_occlusions.json`). 
By default, these logs are saved to the directory specified by the `CELLWORLD_EXPERIMENT_DIR` environment variable.

## Setup & Execution

### Prerequisites
- **Unity**: Compatible Unity Editor version (the project uses standard packages, TextMeshPro, and InputSystem).
- **Python 3**: Ensure the required Python packages are installed:
  ```bash
  pip install cellworld cellworld_game pygame
  ```

### Configuration
- Set the `CELLWORLD_EXPERIMENT_DIR` environment variable to the path where you want the experiment JSON logs to be saved.
- Optionally, set `CELLWORLD_PYTHON` to specify the exact path to your Python executable if it's not in your system's PATH.

### Running the Project
1. Open the project in Unity.
2. Open the main scene. The `CellworldGameBridge` script is set to `launchPythonOnStart = true`, meaning it will automatically run `Server/Server.py` when you hit Play.
3. Hit Play in the Unity Editor. The simulation will initialize, the bridge will connect, and the experiment will begin.
