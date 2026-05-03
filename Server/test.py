import argparse
import json
import math
import queue
import socket
import threading
import time
import tkinter as tk
from tkinter import ttk, messagebox

DEFAULT_HOST = "127.0.0.1"
DEFAULT_CMD_PORT = 5006
DEFAULT_LISTEN_PORT = 5005
CANVAS_SIZE = 700
PADDING = 40
SEND_INTERVAL_SEC = 1.0 / 60.0
DEFAULT_VIEW = (-0.1, 1.1, -0.1, 1.1)


class TestClientUI:
    def __init__(self, root: tk.Tk, host: str, cmd_port: int, listen_port: int):
        self.root = root
        self.root.title("CellWorld3D Test Client")
        self.root.geometry("1120x820")

        self.host_var = tk.StringVar(value=host)
        self.cmd_port_var = tk.StringVar(value=str(cmd_port))
        self.listen_port_var = tk.StringVar(value=str(listen_port))
        self.init_world_var = tk.StringVar(value="")
        self.init_step_var = tk.StringVar(value="0.1")
        self.init_render_var = tk.BooleanVar(value=False)
        self.init_protocol_var = tk.StringVar(value="HUMAN")
        self.init_patient_var = tk.StringVar(value="p0")

        self.status_var = tk.StringVar(value="Disconnected")
        self.predator_var = tk.StringVar(value="Predator: -, -, -")
        self.prey_var = tk.StringVar(value="Prey: -, -, -")
        self.state_var = tk.StringVar(value="Server state: Unknown")

        self.running = True
        self.connected_once = False
        self.last_packet_time = None
        self.last_sent_time = 0.0
        self.last_mouse_world = None
        self.server_stopped = False

        self.predator = None
        self.prey = None
        self.path = []
        self.puff_until = 0.0

        self.msg_queue: queue.Queue[tuple[str, tuple]] = queue.Queue()

        self.tx_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.rx_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.rx_sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.rx_sock.bind(("0.0.0.0", listen_port))
        self.rx_sock.settimeout(0.5)

        self.mouse_down = False
        self.last_mouse_xy = None

        self._build_ui()
        self._bind_canvas()

        self.listener = threading.Thread(target=self._listen_loop, daemon=True)
        self.listener.start()

        self.root.protocol("WM_DELETE_WINDOW", self.on_close)
        self._tick()

    def _build_ui(self):
        outer = ttk.Frame(self.root, padding=12)
        outer.pack(fill="both", expand=True)

        left = ttk.Frame(outer)
        left.pack(side="left", fill="both", expand=True)

        right = ttk.Frame(outer, width=330)
        right.pack(side="right", fill="y", padx=(12, 0))
        right.pack_propagate(False)

        ttk.Label(left, text="Predator / Prey Real-time View", font=("Segoe UI", 14, "bold")).pack(anchor="w", pady=(0, 6))
        ttk.Label(left, textvariable=self.status_var).pack(anchor="w")
        ttk.Label(left, textvariable=self.state_var).pack(anchor="w", pady=(0, 8))

        self.canvas = tk.Canvas(left, width=CANVAS_SIZE, height=CANVAS_SIZE, bg="#0b1020", highlightthickness=1, highlightbackground="#223050")
        self.canvas.pack(anchor="w")

        ttk.Label(left, textvariable=self.predator_var, font=("Consolas", 10)).pack(anchor="w", pady=(8, 0))
        ttk.Label(left, textvariable=self.prey_var, font=("Consolas", 10)).pack(anchor="w")

        ttk.Label(right, text="Connection", font=("Segoe UI", 11, "bold")).pack(anchor="w")
        self._labeled_entry(right, "Host", self.host_var)
        self._labeled_entry(right, "Command Port", self.cmd_port_var)
        self._labeled_entry(right, "Listen Port", self.listen_port_var, readonly=True)

        ttk.Separator(right, orient="horizontal").pack(fill="x", pady=10)

        ttk.Label(right, text="Init Command (i)", font=("Segoe UI", 11, "bold")).pack(anchor="w")
        self._labeled_entry(right, "world_name", self.init_world_var)
        self._labeled_entry(right, "time_step", self.init_step_var)
        self._labeled_entry(right, "protocol", self.init_protocol_var)
        self._labeled_entry(right, "patient", self.init_patient_var)
        ttk.Checkbutton(right, text="render", variable=self.init_render_var).pack(anchor="w", pady=(4, 6))
        ttk.Button(right, text="Send i", command=self.send_init).pack(fill="x")

        ttk.Separator(right, orient="horizontal").pack(fill="x", pady=10)

        ttk.Label(right, text="Server Commands", font=("Segoe UI", 11, "bold")).pack(anchor="w")

        row1 = ttk.Frame(right)
        row1.pack(fill="x", pady=(6, 4))
        ttk.Button(row1, text="Reset (r)", command=lambda: self.send_command("r")).pack(side="left", fill="x", expand=True, padx=(0, 4))
        ttk.Button(row1, text="Begin (b)", command=lambda: self.send_command("b")).pack(side="left", fill="x", expand=True, padx=(4, 0))

        row2 = ttk.Frame(right)
        row2.pack(fill="x", pady=4)
        ttk.Button(row2, text="Pause (p)", command=lambda: self.send_command("p")).pack(side="left", fill="x", expand=True, padx=(0, 4))
        ttk.Button(row2, text="Unpause (u)", command=lambda: self.send_command("u")).pack(side="left", fill="x", expand=True, padx=(4, 0))

        row3 = ttk.Frame(right)
        row3.pack(fill="x", pady=4)
        ttk.Button(row3, text="Stop+Save (s)", command=lambda: self.send_command("s")).pack(side="left", fill="x", expand=True)

        ttk.Separator(right, orient="horizontal").pack(fill="x", pady=10)

        ttk.Label(right, text="Manual", font=("Segoe UI", 11, "bold")).pack(anchor="w")
        self.manual_cmd = tk.StringVar(value="")
        ttk.Entry(right, textvariable=self.manual_cmd).pack(fill="x", pady=(4, 4))
        ttk.Button(right, text="Send Raw", command=self.send_manual).pack(fill="x")

        ttk.Label(
            right,
            text=(
                "Mouse Control\n"
                "Hold left mouse button on canvas to stream prey x,y (opcode d).\n"
                "Heading is derived from mouse motion.\n"
                "Note: opcode s now stops, saves, and exits the server."
            ),
            justify="left",
        ).pack(anchor="w", pady=(14, 0))

    def _labeled_entry(self, parent, label: str, var, readonly: bool = False):
        ttk.Label(parent, text=label).pack(anchor="w", pady=(5, 0))
        entry = ttk.Entry(parent, textvariable=var)
        entry.pack(fill="x")
        if readonly:
            entry.state(["readonly"])

    def _bind_canvas(self):
        self.canvas.bind("<ButtonPress-1>", self.on_mouse_down)
        self.canvas.bind("<ButtonRelease-1>", self.on_mouse_up)
        self.canvas.bind("<B1-Motion>", self.on_mouse_move)

    def _listen_loop(self):
        while self.running:
            try:
                data, addr = self.rx_sock.recvfrom(4096)
            except socket.timeout:
                continue
            except OSError:
                return

            try:
                text = data.decode("utf-8").strip()
            except UnicodeDecodeError:
                continue
            self.msg_queue.put(("udp", (text, addr)))

    def _tick(self):
        self._drain_queue()
        self._update_status()
        self._draw()
        self.root.after(33, self._tick)

    def _drain_queue(self):
        while True:
            try:
                kind, payload = self.msg_queue.get_nowait()
            except queue.Empty:
                break

            if kind != "udp":
                continue

            msg, _addr = payload
            if not msg:
                continue

            self.connected_once = True
            self.last_packet_time = time.time()
            op = msg[0]
            body = msg[1:]

            if op == "d":
                try:
                    x, y, r = json.loads(body)
                    self.predator = (float(x), float(y), float(r))
                    self.path.append((self.predator[0], self.predator[1]))
                    if len(self.path) > 1200:
                        self.path = self.path[-1200:]
                except Exception:
                    pass
            elif op == "s":
                states = {"0": "Unknown", "1": "Connected", "2": "Ready", "3": "Paused", "4": "Running", "5": "Stopped"}
                state_name = states.get(body, f"{body}")
                self.state_var.set(f"Server state: {state_name}")
                if body == "5":
                    self.server_stopped = True
                    self.status_var.set(
                        f"Server {self.host_var.get()}:{self.cmd_port_var.get()} | stop acknowledged (server may exit)"
                    )
                else:
                    self.server_stopped = False
            elif op == "p":
                self.puff_until = time.time() + 0.6
            elif op == "f":
                self.state_var.set("Server state: Episode Finished")

    def _update_status(self):
        if not self.connected_once:
            self.status_var.set(f"Listening on UDP {self.listen_port_var.get()} (no packets yet)")
            return

        if self.server_stopped:
            self.status_var.set(
                f"Server {self.host_var.get()}:{self.cmd_port_var.get()} | stopped/exited"
            )
            return

        age = time.time() - self.last_packet_time if self.last_packet_time else 999
        freshness = "live" if age < 1.0 else f"stale ({age:.1f}s)"
        self.status_var.set(
            f"Server {self.host_var.get()}:{self.cmd_port_var.get()} | Listen {self.listen_port_var.get()} | {freshness}"
        )

    def _world_bounds(self):
        return DEFAULT_VIEW

    def _world_to_canvas(self, x, y, bounds):
        min_x, max_x, min_y, max_y = bounds
        ww = max(max_x - min_x, 1e-9)
        wh = max(max_y - min_y, 1e-9)

        cw = CANVAS_SIZE - 2 * PADDING
        ch = CANVAS_SIZE - 2 * PADDING

        cx = PADDING + (x - min_x) / ww * cw
        cy = CANVAS_SIZE - (PADDING + (y - min_y) / wh * ch)
        return cx, cy

    def _canvas_to_world(self, cx, cy, bounds):
        min_x, max_x, min_y, max_y = bounds
        ww = max(max_x - min_x, 1e-9)
        wh = max(max_y - min_y, 1e-9)

        cw = CANVAS_SIZE - 2 * PADDING
        ch = CANVAS_SIZE - 2 * PADDING

        x = min_x + ((cx - PADDING) / cw) * ww
        y = min_y + (((CANVAS_SIZE - cy) - PADDING) / ch) * wh
        return x, y

    def _draw_heading(self, x, y, degrees, color):
        length = 18
        rad = math.radians(degrees)
        tip_x = x + math.cos(rad) * length
        tip_y = y - math.sin(rad) * length
        self.canvas.create_line(x, y, tip_x, tip_y, fill=color, width=2)

    def _draw(self):
        self.canvas.delete("all")
        self.canvas.create_rectangle(0, 0, CANVAS_SIZE, CANVAS_SIZE, fill="#0b1020", outline="")

        for i in range(1, 10):
            v = i * CANVAS_SIZE / 10
            self.canvas.create_line(v, 0, v, CANVAS_SIZE, fill="#17203a")
            self.canvas.create_line(0, v, CANVAS_SIZE, v, fill="#17203a")

        bounds = self._world_bounds()

        if len(self.path) > 1:
            projected = [self._world_to_canvas(x, y, bounds) for x, y in self.path]
            for i in range(1, len(projected)):
                self.canvas.create_line(*projected[i - 1], *projected[i], fill="#2e8bff", width=2)

        if self.predator:
            px, py, pr = self.predator
            cx, cy = self._world_to_canvas(px, py, bounds)
            predator_color = "#ff9933" if time.time() < self.puff_until else "#ff4d4d"
            self.canvas.create_oval(cx - 8, cy - 8, cx + 8, cy + 8, fill=predator_color, outline="")
            self._draw_heading(cx, cy, pr, "#ffd3a3")
            self.predator_var.set(f"Predator: x={px:.3f} y={py:.3f} r={pr:.2f}")

        if self.prey:
            px, py, pr = self.prey
            cx, cy = self._world_to_canvas(px, py, bounds)
            self.canvas.create_oval(cx - 7, cy - 7, cx + 7, cy + 7, fill="#4dff88", outline="")
            self._draw_heading(cx, cy, pr, "#beffcf")
            self.prey_var.set(f"Prey: x={px:.3f} y={py:.3f} r={pr:.2f}")

        self.canvas.create_text(10, 10, anchor="nw", fill="#dfe8ff", text="Hold left-click to stream prey position")

    def on_mouse_down(self, event):
        self.mouse_down = True
        self._send_prey_from_mouse(event, force=True)

    def on_mouse_move(self, event):
        if not self.mouse_down:
            return
        self._send_prey_from_mouse(event)

    def on_mouse_up(self, _event):
        self.mouse_down = False
        self.last_mouse_xy = None
        self.last_mouse_world = None

    def _send_prey_from_mouse(self, event, force=False):
        now = time.time()
        if not force and (now - self.last_sent_time) < SEND_INTERVAL_SEC:
            return

        bounds = self._world_bounds()
        cx = min(max(event.x, PADDING), CANVAS_SIZE - PADDING)
        cy = min(max(event.y, PADDING), CANVAS_SIZE - PADDING)
        x, y = self._canvas_to_world(cx, cy, bounds)

        heading = 0.0
        if self.last_mouse_world is not None:
            dx = x - self.last_mouse_world[0]
            dy = y - self.last_mouse_world[1]
            if abs(dx) > 1e-6 or abs(dy) > 1e-6:
                heading = math.degrees(math.atan2(dy, dx))
            elif self.prey is not None:
                heading = self.prey[2]
        elif self.prey is not None:
            heading = self.prey[2]

        self.prey = (x, y, heading)
        self.last_mouse_world = (x, y)
        self.last_sent_time = now

        payload = json.dumps([x, y, heading], separators=(",", ":"))
        self.send_command("d" + payload)

    def _target(self):
        host = self.host_var.get().strip() or DEFAULT_HOST
        try:
            port = int(self.cmd_port_var.get().strip())
        except ValueError as exc:
            raise ValueError("Command port must be an integer") from exc
        return host, port

    def send_command(self, text: str):
        try:
            host, port = self._target()
            self.tx_sock.sendto(text.encode("utf-8"), (host, port))
        except ValueError as exc:
            messagebox.showerror("Invalid Command Settings", str(exc))
        except OSError as exc:
            messagebox.showerror("Send Failed", str(exc))

    def send_init(self):
        world_name = self.init_world_var.get().strip()
        if not world_name:
            messagebox.showerror("Missing world_name", "init command requires world_name")
            return

        try:
            time_step = float(self.init_step_var.get().strip())
        except ValueError:
            messagebox.showerror("Invalid time_step", "time_step must be numeric")
            return

        payload = {
            "world_name": world_name,
            "time_step": time_step,
            "render": bool(self.init_render_var.get()),
            "protocol": self.init_protocol_var.get().strip() or "HUMAN",
            "patient": self.init_patient_var.get().strip() or "p0",
        }
        self.send_command("i" + json.dumps(payload, separators=(",", ":")))

    def send_manual(self):
        text = self.manual_cmd.get().strip()
        if text:
            self.send_command(text)

    def on_close(self):
        self.running = False
        try:
            self.rx_sock.close()
        except OSError:
            pass
        try:
            self.tx_sock.close()
        except OSError:
            pass
        self.root.destroy()


def parse_args():
    parser = argparse.ArgumentParser(description="CellWorld3D UDP test client")
    parser.add_argument("--host", default=DEFAULT_HOST, help="Server host for command socket")
    parser.add_argument("--cmd-port", type=int, default=DEFAULT_CMD_PORT, help="Server command UDP port")
    parser.add_argument("--listen-port", type=int, default=DEFAULT_LISTEN_PORT, help="Local UDP port for server telemetry")
    return parser.parse_args()


def main():
    args = parse_args()
    root = tk.Tk()
    TestClientUI(root, args.host, args.cmd_port, args.listen_port)
    root.mainloop()


if __name__ == "__main__":
    main()
