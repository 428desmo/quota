# Quota

4種類とワイルドを集めてノルマを達成するカードゲーム。名前と絵柄はアイテムセットで差し替える。既定は交易品（香辛料・絹・茶・宝石・銀貨）。

規則の正本は `quota_rule_v1.5.md`。アイテムセットは `quota_item_sets_v1.0.json`。ターミナル版の操作は `quota_cli_spec_v1.1.md`。ブラウザ版は `quota_web_spec_v1.1.md`。

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
python -m quota.cli --item-set 海の幸
python -m quota.cli --list-item-sets
```

カードは `🎀4 絹 #14` のように、選んだセットの絵文字・数字ラベル・名前で出す。既定は交易品。

人間の席は番号で行動を選ぶ。ノルマがあるときは、集めるカードの番号を空白区切りで入れる。残りの席はCPU。
