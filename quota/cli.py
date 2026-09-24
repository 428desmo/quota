"""Terminal hotseat and CPU play."""

from __future__ import annotations

import argparse

from quota.ai import choose_action
from quota.engine import Abandon, Collect, Game, GameConfig, Pass, TakeQuota


def main() -> None:
    parser = argparse.ArgumentParser(description="Quota をターミナルで遊ぶ")
    parser.add_argument("--players", type=int, default=3, choices=range(2, 7))
    parser.add_argument("--humans", type=int, default=1, help="人間の人数。残りはCPU")
    parser.add_argument("--seed", type=int, default=None)
    parser.add_argument("--auto", action="store_true", help="全員CPUで1ゲーム進める")
    args = parser.parse_args()
    humans = 0 if args.auto else args.humans
    if humans > args.players:
        parser.error("--humans は --players 以下にしてください")
    names = []
    for i in range(args.players):
        names.append(f"あなた{i + 1}" if i < humans else f"CPU{i - humans + 1}")
    if humans == 1:
        names[0] = "あなた"
    game = Game.start(
        GameConfig(
            num_players=args.players,
            seed=args.seed,
            names=names,
            human_seats=list(range(humans)),
        )
    )
    while not game.finished:
        _print_table(game)
        player = game.players[game.current]
        if player.is_human:
            action = _ask(game)
        else:
            action = choose_action(game)
            print(f"{player.name} の手: {_action_text(game, action)}")
        game.step(action)
    _print_table(game)
    print()
    reason = "山札切れ" if game.end_reason == "DECK" else "膠着の連続"
    print(f"終了: {reason}")
    place = 1
    for group in game.ranking():
        for seat in group:
            p = game.players[seat]
            print(
                f"{place}位 {p.name}  {p.score}点  "
                f"納品{p.achieve_count}回  最高{p.max_single_score}点"
            )
        place += len(group)


def _print_table(game: Game) -> None:
    print()
    print(
        f"--- 手番 {game.turn_number}  山札 {len(game.deck)}  "
        f"連続パス {game.no_gain_streak}  "
        f"入港済み {'あり' if game.stall_flag else 'なし'} ---"
    )
    print("市場: " + " | ".join(c.label() for c in game.market))
    for i, p in enumerate(game.players):
        mark = ">" if i == game.current and not game.finished else " "
        quota = "注文なし" if p.quota is None else p.quota.label()
        held = "、".join(c.label() for c in p.collection) or "なし"
        print(f"{mark} {p.name}  {p.score}点  注文: {quota}  買い付け: {held}")


def _ask(game: Game):
    actions = game.legal_actions()
    print("行動:")
    for i, action in enumerate(actions, start=1):
        print(f"  {i}. {_action_text(game, action)}")
    while True:
        raw = input("番号> ").strip()
        if raw.isdigit() and 1 <= int(raw) <= len(actions):
            return actions[int(raw) - 1]
        print("番号を入力してください")


def _action_text(game: Game, action) -> str:
    if isinstance(action, TakeQuota):
        card = next(c for c in game.market if c.id == action.card_id)
        return f"注文する: {card.label()}"
    if isinstance(action, Collect):
        card = next(c for c in game.market if c.id == action.card_id)
        return f"買い付ける: {card.label()}"
    if isinstance(action, Abandon):
        return "注文を取り消す"
    if isinstance(action, Pass):
        return "パス"
    return str(action)


if __name__ == "__main__":
    main()
