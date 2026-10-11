"""A rival's active quota reduces the supply we can reasonably collect."""

from dataclasses import replace
from random import Random

from quota.cards import make_deck
from quota.cpu_observation import Observation, SeatObservation
from quota.cpu_policy import CharacterPolicy
from quota.engine import Abandon, Collect, Pass


CARDS = make_deck()


def card(suit, rank):
    return next(item for item in CARDS if item.suit == suit and item.rank == rank)


def seat(quota=None, collection=(), reshuffle=0):
    return SeatObservation(quota, tuple(collection), (), 0, 0, reshuffle, 0)


def contested_observation():
    own_quota = card("S", 13)
    rival_quota = card("S", 12)
    matching = card("S", 5)
    alternative = card("D", 4)
    visible = (own_quota, rival_quota, matching, alternative)
    remaining = [item for item in CARDS if item not in visible]
    Random(0).shuffle(remaining)
    return Observation(
        market=(matching, alternative, None, None, None, None, None),
        discard=tuple(remaining[:61]),
        players=(seat(own_quota), seat(rival_quota), seat()),
        current=0, deck_count=35, no_gain_streak=0, stall_flag=False,
        turn_order=(0, 1, 2), round_index=0, round_count=3, turn_number=0,
        plan="normal", double_stage=0,
        legal_actions=(Collect((matching.id,)), Abandon(), Pass()),
    )


def test_middle_and_strong_both_discount_same_suit_competition():
    contested = contested_observation()
    uncontested = replace(contested, players=(contested.players[0], seat(), seat()))
    for level in (1, 2):
        mind = CharacterPolicy("comeback", level)
        assert mind.completion(contested, "S", 12) < mind.completion(uncontested, "S", 12)
        assert mind.completion(contested, "S", 12, excluded_id=contested.market[0].id) < mind.completion(
            uncontested, "S", 12, excluded_id=contested.market[0].id)
    weak = CharacterPolicy("comeback", 0)
    assert weak.completion(contested, "S", 12) == weak.completion(uncontested, "S", 12)


def test_only_strong_can_abandon_with_a_matching_card_still_available():
    observation = contested_observation()
    for style in ("comeback", "denial"):
        assert isinstance(CharacterPolicy(style, 2).choose(observation), Abandon)
        assert isinstance(CharacterPolicy(style, 1).choose(observation), Collect)


def test_strong_finishes_immediately_instead_of_abandoning():
    observation = contested_observation()
    own = replace(observation.players[0], collection=tuple(
        item for item in CARDS if item.suit == "S" and item.id not in
        (observation.players[0].quota.id, observation.players[1].quota.id,
         observation.market[0].id)
    )[:11])
    observation = replace(observation, players=(own, *observation.players[1:]))
    assert not CharacterPolicy("comeback", 2).should_abandon_contested(observation)
    assert isinstance(CharacterPolicy("comeback", 2).choose(observation), Collect)
