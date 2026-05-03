# bridge.py
import os
import time
import json
import cellworld_game as cg
from experiment_manager import ExperimentManager
from udp_bridge import UdpBridge, UdpBridgeState
from PeekingBotEvade import PeekingBotEvade

bridge = UdpBridge()
task: PeekingBotEvade = None
experiment: ExperimentManager = None
protocol: str = ""
recording: bool = False

print(f"CellworldGame is running - Experiment folder: {os.getenv("CELLWORLD_EXPERIMENT_DIR")}")
bridge.send_state(UdpBridgeState.Connected)

def puff_processing(model):
    bridge.send("p")

def finish_episode(model : PeekingBotEvade):
    if model.prey_data.goal_achieved:
        print("episode finished")
        bridge.send("f")
        bridge.send_state(UdpBridgeState.Paused)

def init_cmd(payload):
    global task
    global experiment
    global protocol
    parameters = json.loads(payload) if payload else {}
    if not isinstance(parameters, dict):
        print("INVALID INIT: payload must be a json object")
        return 
    print(parameters)
    task = PeekingBotEvade(real_time=True, 
                           world_name=parameters["world_name"], 
                           time_step=parameters["time_step"], 
                           render=parameters["render"],
                           goal_threshold=0.025)
    task.add_event_handler(event_name="puff", handler=puff_processing)
    task.add_event_handler(event_name="after_stop", handler=finish_episode)
    protocol = parameters.get("protocol", "HUMAN")
    experiment = ExperimentManager(task=task, subject=parameters.get("patient","p0"))
    task.reset()
    task.pause()
    print(f"INIT RECEIVED {parameters}")
    bridge.send_state(UdpBridgeState.Paused)

def prey_data(payload):
    global task
    prey_data = json.loads(payload)
    if not isinstance(prey_data, list) or len(prey_data) < 3:
        return 
    task.prey.state.location = prey_data[0:2]
    task.prey.state.direction = prey_data[2]

def reset_cmd(payload):
    global task
    global recording
    if task is None:
        return
    task.reset()
    print("RESET RECEIVED")
    recording = False
    if not task.paused:
        task.pause()
    bridge.send_state(UdpBridgeState.Ready)

def begin_cmd(payload):
    global task
    global recording
    if task is None:
        return
    print("BEGIN RECEIVED")
    recording = True
    if not task.paused:
        task.pause()
    bridge.send_state(UdpBridgeState.Paused)

def pause_cmd(payload):
    global task
    if task is None:
        return
    print("PAUSE RECEIVED")
    if not task.paused:
        task.pause()
    bridge.send_state(UdpBridgeState.Paused)

def unpause_cmd(payload):
    global task
    if task is None:
        return
    print("UNPAUSE RECEIVED")
    if task.paused:
        task.pause()
    bridge.send_state(UdpBridgeState.Running)

def stop_cmd(payload):
    global task
    global experiment
    global protocol
    date_str = experiment.experiment.start_time.strftime("%Y%m%d_%H%M")
    path = f"{os.getenv("CELLWORLD_EXPERIMENT_DIR")}/{protocol}_{date_str}_{experiment.experiment.subject_name}_{experiment.experiment.occlusions}.json"
    if task is None:
        return
    task.stop()
    print("STOP RECEIVED")
    print("Saving Experiment to " + path)
    experiment.save(path)
    time.sleep(.5) # give Unity time to receive the stop command before shutting down the server
    bridge.send_state(UdpBridgeState.Stopped)
    time.sleep(.5) # give Unity time to receive the stop command before shutting down the server
    print("Server shutting down...")
    exit(0)

bridge.add_handler("i", init_cmd)
bridge.add_handler("d", prey_data)
bridge.add_handler("r", reset_cmd)
bridge.add_handler("p", pause_cmd)
bridge.add_handler("u", unpause_cmd)
bridge.add_handler("s", stop_cmd)
bridge.add_handler("b", begin_cmd)

while True:
    bridge.process()
    if task is None:
        time.sleep(.025)
        continue
    task.step()
    if recording:
        experiment.step(task)
    bridge.send_predator_update(x = task.predator.state.location[0],
                                y = task.predator.state.location[1],
                                rot = task.predator.state.direction)
