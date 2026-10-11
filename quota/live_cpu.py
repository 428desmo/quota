"""Live CPU binding for the six selectable characters."""

from __future__ import annotations

import random

from quota.characters import bind, mind_for
from quota.cpu_observation import observe
from quota.cpu_policy import CharacterPolicy
from quota.cpu_roster import CHARACTERS, display_name, pick_cast, validate_strength
from quota.engine import Abandon, Game


class LiveMind:
    def __init__(self, character_id: int, strength: int):
        self.character_id = character_id
        self.policy = CharacterPolicy(CHARACTERS[character_id][1], validate_strength(strength))

    def choose(self, game: Game):
        before = observe(game)
        if game.config.special_actions_rule:
            special = self.policy.special(before)
            if special == "double":
                game.declare_double()
            elif special == "reshuffle":
                game.declare_reshuffle()
        after = observe(game)
        if after != before:
            observe_transition(game, before, after, None, "declaration")
        action = self.policy.choose(after)
        if not game.is_legal(action):
            raise ValueError(f"CPU returned illegal action: {action!r}")
        return action


def bind_cast(game: Game, characters, strength: int = 1) -> None:
    chosen = iter(characters)
    for player in game.players:
        if not player.is_human:
            character_id = next(chosen)
            player.name = display_name(character_id)
            bind(player, LiveMind(character_id, strength))


def bind_replacement(player, strength: int = 1) -> None:
    if mind_for(player) is None:
        bind(player, LiveMind(random.randrange(len(CHARACTERS)), strength))


def observe_transition(game: Game, before, after, action, phase: str) -> None:
    for player in game.players:
        mind = mind_for(player)
        if isinstance(mind, LiveMind):
            mind.policy.on_transition(before, after, action, phase)
