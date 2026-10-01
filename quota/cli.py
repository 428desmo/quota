"""Terminal hotseat and CPU play."""

from __future__ import annotations

import argparse

from quota.ai import choose_action
from quota.characters import assign_seats
from quota.cards import Card
from quota.engine import Abandon, Collect, Game, GameConfig, Pass, TakeQuota


def main() -> None:
    parser = argparse.ArgumentParser(description="Quota をターミナルで遊ぶ")
    parser.add_argument("--players", type=int, default=3, choices=range(2, 7))
    parser.add_argument("--humans", type=int, default=1, help="人間の人数。残りはCPU")
    parser.add_argument("--seed", type=int, default=None)
    parser.add_argument("--auto", action="store_true", help="全員CPUで1ゲーム進める")
    parser.add_argument("--sequence", action="store_true", help="上級ルール（並び順ボーナス）")
    parser.add_argument("--title", action="store_true", help="上級ルール（称号ボーナス）")
    parser.add_argument("--special", action="store_true", help="上級ルール（特殊アクション）")
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
            sequence_rule=args.sequence,
            title_rule=args.title,
            special_actions_rule=args.special,
            rounds=args.players if args.sequence or args.title or args.special else 1,
        )
    )
    assign_seats(game)
    theme = game.theme()
    kinds = "、".join(theme.faces[kind].name for kind in ("K1", "K2", "K3", "K4"))
    print(f"交易品: {kinds}　ワイルド: {theme.faces['WILD'].name}")
    while not game.finished:
        if game.awaiting_next_round:
            _print_round_break(game)
            game.begin_next_round()
            continue
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
                f"{place}位 {p.name}  {game.final_score(p)}点  "
                f"達成{p.achieve_count}回  最高{p.max_single_score}点"
                + (f"  並び順{game.sequence_points(p)}点" if game.config.sequence_rule else "")
                + (f"  称号{game.title_points(p)}点" if game.config.title_rule else "")
            )
        place += len(group)


def _theme(game: Game):
    return game.theme()


def _print_round_break(game: Game) -> None:
    reason = "山札切れ" if game.round_end_reason == "DECK" else "膠着の連続"
    print()
    print(f"第{game.round_index}ラウンド終了（{reason}）")
    for player in game.players:
        print(f"  {player.name}  {game.final_score(player)}点")


def _print_table(game: Game) -> None:
    print()
    round_label = f"第{game.round_index}ラウンド  " if game.round_count > 1 else ""
    print(
        f"--- {round_label}手番 {game.turn_number}  山札 {len(game.deck)}  "
        f"連続パス {game.no_gain_streak}  "
        f"膠着状態 {1 if game.stall_flag else 0} ---"
    )
    theme = _theme(game)
    print("場札: " + _market_line(game))
    for i, p in enumerate(game.players):
        mark = ">" if i == game.current and not game.finished else " "
        quota = "なし" if p.quota is None else p.quota.label(theme)
        held = "、".join(c.label(theme) for c in p.collection) or "なし"
        extra = ""
        if game.config.sequence_rule:
            extra = f"  並び順{game.sequence_points(p)}点"
        print(f"{mark} {p.name}  {game.final_score(p)}点{extra}  ノルマ: {quota}  収集: {held}")


def _ask(game: Game):
    if game.plan == "normal" and not game.turn_gain and game.config.special_actions_rule:
        player = game.players[game.current]
        options = []
        if player.double_action_left:
            options.append("d=ダブル")
        if player.reshuffle_take_left:
            options.append("r=配り直し")
        if options:
            print("特殊: " + " ".join(options) + "（空エンターで通常の行動）")
            raw = input("特殊> ").strip().lower()
            if raw == "d" and player.double_action_left:
                game.declare_double()
                print("ダブルアクション。1回目の行動です。")
            elif raw == "r" and player.reshuffle_take_left:
                game.declare_reshuffle()
                print("場を配り直した。")
                print("場札: " + _market_line(game))
    if game.plan == "double" and game.double_stage == 1 and not game.turn_gain:
        print("c でダブルを取り消せます")
    player = game.players[game.current]
    if player.quota is None:
        actions = game.legal_actions()
        print("行動:")
        for i, action in enumerate(actions, start=1):
            print(f"  {i}. {_action_text(game, action)}")
        while True:
            raw = input("番号> ").strip().lower()
            if raw == "c" and game.plan == "double" and game.double_stage == 1 and not game.turn_gain:
                game.cancel_double()
                print("ダブルアクションを取り消した。")
                return _ask(game)
            if raw.isdigit() and 1 <= int(raw) <= len(actions):
                return actions[int(raw) - 1]
            print("番号を入力してください")

    assert player.quota.rank is not None
    eligible = [
        c
        for c in game.market
        if c is not None and (c.suit == player.quota.suit or c.suit == "JOKER")
    ]
    need = player.quota.rank - 1 - len(player.collection)
    print(f"集める（残り {need} 枚まで。番号を空白区切り）:")
    for i, card in enumerate(eligible, start=1):
        print(f"  {i}. {card.label(_theme(game))}")
    print("  a. 放棄")
    print("  p. パス")
    while True:
        raw = input("番号> ").strip().lower()
        if raw == "c" and game.plan == "double" and game.double_stage == 1 and not game.turn_gain:
            game.cancel_double()
            print("ダブルアクションを取り消した。")
            return _ask(game)
        if raw == "a":
            return Abandon()
        if raw == "p":
            return Pass()
        parts = raw.split()
        if parts and all(part.isdigit() for part in parts):
            indexes = [int(part) for part in parts]
            if (
                len(indexes) == len(set(indexes))
                and all(1 <= n <= len(eligible) for n in indexes)
                and 1 <= len(indexes) <= need
            ):
                return Collect(tuple(eligible[n - 1].id for n in indexes))
        print(f"1〜{len(eligible)} から、1枚以上{need}枚まで選んでください")


def _action_text(game: Game, action) -> str:
    if isinstance(action, TakeQuota):
        card = _find(game.market, action.card_id)
        return f"ノルマ札にする: {card.label(_theme(game))}"
    if isinstance(action, Collect):
        theme = _theme(game)
        labels = "、".join(_find(game.market, card_id).label(theme) for card_id in action.card_ids)
        return f"集める: {labels}"
    if isinstance(action, Abandon):
        return "放棄"
    if isinstance(action, Pass):
        return "パス"
    return str(action)


def _market_line(game: Game) -> str:
    theme = _theme(game)
    return " | ".join("（空）" if card is None else card.label(theme) for card in game.market)


def _find(cards: list, card_id: int) -> Card:
    return next(c for c in cards if c is not None and c.id == card_id)


if __name__ == "__main__":
    main()
