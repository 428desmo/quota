# Quota

4種類とワイルドを集めてノルマを達成するカードゲーム。アイテムセットは交易品だけ。対局ごとに27品目から4種類を選び、ワイルドは金貨。

規則の正本は `quota_rule_v1.11.md`。品目は `quota_goods_v1.0.json`。ターミナル版の操作は `quota_cli_spec_v1.1.md`。ブラウザ版は `quota_web_spec_v1.1.md`。CPUの戦略は `cpu_strategy_v1.0.md`、その強さの記録は `cpu_ranking_v1.1.json`。

## CPUの強さを測る

```bash
python tools/rate_cpu.py --matches 30000 --seed 2
```

乱択の対戦でCPUキャラクターの評価点と順位を更新し、`cpu_ranking_v1.1.json` に足す。評価点は1試合あたりの獲得点で、優勝が3点、2位が2点、それ以下は0点である。

機械が空いている時間に積み上げるときは、こちらを使う。

```bash
nice -n 10 python tools/rate_loop.py --minutes 60 --batch 2000
```

一定数の試合ごとに記録を書き戻し、記録の2ファイルだけをコミットして push する。`--minutes 0` で止めるまで走る。Ctrl-C で、進行中のまとまりの分だけを捨てて終わる。詳しくは `cpu_strategy_v1.0.md` の第8節。

## ブラウザで遊ぶ

```bash
python -m quota.web
```

`http://127.0.0.1:8000` を開く。同じネットワークの他の端末からも、このマシンのアドレスの 8000 番で同じ盤面を操作できる。

## 遊び方

```bash
python -m quota.cli --players 3 --humans 1
python -m quota.cli --players 4 --humans 2 --seed 7
python -m quota.cli --auto --seed 1
```

カードは `4 胡椒 #14` のように、数字ラベルと品目名で出す。

人間の席は番号で行動を選ぶ。ノルマがあるときは、集めるカードの番号を空白区切りで入れる。残りの席はCPU。
