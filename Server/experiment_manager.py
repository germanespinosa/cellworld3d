import os
import datetime
import cellworld_game as cg
import cellworld as cw
from pathlib import Path

class ExperimentManager(object):
    def __init__(self,
                 task: cg.BotEvade,
                 subject:str):
        self.task = task
        self.episode_counter = 0
        self.step_counter = 0
        self.timer = cw.Timer()
        self.temp_dir = os.getenv("CELLWORLD_EXPERIMENT_DIR")
        self.subject = subject
        self.current_episode: cw.Episode = None
        self.experiment = cw.Experiment(name="BotEvadeExperiment",
                                        world_configuration_name="hexagonal", 
                                        world_implementation_name="3d", 
                                        occlusions=task.loader.world_name,
                                        subject_name=subject)
        self.task.add_event_handler(event_name="after_reset", handler=self.start_episode)
        # self.task.add_event_handler(event_name="after_step", handler=self.step)
        self.task.add_event_handler(event_name="after_stop", handler=self.finish_episode)


    def finish_episode(self, model : cg.BotEvade):
        print(f"ExperimentManager: Episode {self.episode_counter} finished")
        self.current_episode.save(self.episode_file(self.episode_counter))
        self.current_episode = None
        self.episode_counter += 1

    def start_episode(self, model: cg.BotEvade):
        print(f"ExperimentManager: Episode {self.episode_counter} started")
        self.step_counter = 0
        self.current_episode = cw.Episode(start_time=datetime.datetime.now(), 
                                          time_stamp=self.timer.to_seconds())

    def step(self, model: cg.BotEvade):
        if self.current_episode is None:
            return
        for agent_name, agent in model.agents.items():
            step_location = cw.Location(x=agent.state.location[0], y=agent.state.location[1])
            step = cw.Step(time_stamp=self.timer.to_seconds(),
                           agent_name=agent_name,
                           frame=self.step_counter,
                           location=step_location,
                           rotation=agent.state.direction)
            
            self.current_episode.trajectories.append(step)
        if model.prey_data.puffed:
            self.current_episode.captures.append(self.step_counter)
        self.step_counter += 1

    def episode_file(self, episode_number:int):
        return Path(self.temp_dir) / f"episode_{episode_number}.json"

    def save(self, path:str):
        print(f"Saving Experiment with {self.episode_counter} episodes")
        for episode_number in range(self.episode_counter):
            episode = cw.Episode().load_from_file(self.episode_file(episode_number))
            self.experiment.episodes.append(episode)
            print(f"Episode {episode_number} saved")
        self.experiment.save(path)