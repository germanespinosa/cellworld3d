import socket
import json
from ast import Dict, List
from enum import Enum


class UdpBridgeState(Enum):
    Unknown = 0
    Connected = 1
    Ready = 2
    Paused = 3
    Running = 4
    Stopped = 5


class UdpBridge(object):
    def __init__(self):
        self.POSE_IP = "127.0.0.1"
        self.POSE_PORT = 5005   # Unity listens here for pose
        self.CMD_PORT = 5006    # Python listens here for commands
        self.pose_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.cmd_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.cmd_sock.bind(("0.0.0.0", self.CMD_PORT))
        self.cmd_sock.setblocking(False)
        self.handlers: Dict[List] = {}

    def add_handler(self, cmd:str, callback: callable):
        if cmd not in self.handlers:
            self.handlers[cmd] = []
        self.handlers[cmd].append(callback)

    def send_state(self, state: UdpBridgeState):
        self.send("s" + str(state.value))

    def process(self):
        global task
        while True:
            try:
                data, _ = self.cmd_sock.recvfrom(1024)
            except BlockingIOError:
                break

            try:
                msg = data.decode("utf-8").strip()
            except UnicodeDecodeError:
                print("INVALID COMMAND: payload is not valid utf-8")
                continue

            if not msg:
                print("INVALID COMMAND: empty payload")
                continue

            cmd = msg[0]
            payload = msg[1:]

            if cmd in self.handlers:
                for handler in self.handlers[cmd]:
                    handler(payload)
            else:
                print(f"UNKNOWN COMMAND: {cmd}")

    def send_predator_update(self, 
                             x:float, 
                             y:float,
                             rot:float):
            msg = "d" + json.dumps([x, y, rot], separators=(",", ":"))
            self.send(msg)

    def send(self, msg:str):
        self.pose_sock.sendto(msg.encode("utf-8"), (self.POSE_IP, self.POSE_PORT))

    def __del__(self):
        self.cmd_sock.close()
        self.pose_sock.close()