# Quota

トランプ2セットで遊ぶカードゲーム。自分でノルマを決めて、同じマークを集めて達成する。カードの商品名は香辛料・絹・茶・宝石・銀貨のままである。

規則の正本は `quota_rule_v1.3.md`。ターミナル版の操作は `quota_cli_spec_v1.1.md`。ブラウザ版は `quota_web_spec_v1.1.md`。

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

カードは `♥4 絹` のように、トランプ表記と商品名を並べて表示する。

- スペード: 香辛料
- ハート: 絹
- クラブ: 茶
- ダイヤ: 宝石
- ジョーカー: 銀貨

人間の席は番号で行動を選ぶ。ノルマがあるときは、集めるカードの番号を空白区切りで入れる。残りの席はCPU。
