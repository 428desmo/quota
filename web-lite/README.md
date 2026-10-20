# Quota 軽量Web版（オフライン）

Unity/WebGL版から独立したHTML/CSS/JavaScript実装です。ビルドや対戦サーバーは不要です。画像・音声・説明文・商品データは、このフォルダの`assets/`へコピーしてあります。

## 起動

```sh
python3 -m http.server 8765 --directory web-lite
```

`http://127.0.0.1:8765/`を開きます。VPSでは`/quota2/`がこの版、`/quota1/`と`/quota/`がUnity版です。

最初はメニューの「ゲームをはじめる」からロビーへ進みます。3人／4人、1ラウンド／人数分／人数分×2、CPUの弱・中・強を選べます。CPUはランダム入れ替えと順序を指定した指名に対応します。「CPU模擬戦を見る」では全員CPUになり、途中ラウンドは自動で進みます。

VPSへの配備では全実行ファイルと素材からオフラインキャッシュの識別子を自動生成します。コードや素材の更新があればキャッシュも更新されます。手動で配備する場合はコピー先に対して`python3 tools/prepare_web_lite.py コピー先`を実行してください。

## オフライン対局

- 場札をタップするとノルマ札を取得します。ノルマがあるときは同じ商品か金貨をタップして1枚ずつ収集します。順番も得点に反映されます。
- 放棄・パス・配り直し・ダブル・ダブルの宣言取消に対応します。放棄しても手番は継続します。
- 個人設定の確認有無、BGM音量、SE音量はロビーとプレイ中で共通です。設定を開いてもCPUの進行を止めません。
- 称号の成立／不成立を示した後、称号チップ、ボーナス加算、当ラウンド順位、前ラウンドまでの累計との計算、総合順位の順で発表します。
- BGMは0秒から開始し、16秒以上92秒未満をWeb Audioでループします。カードSEは各0.3秒、チップSEは移動中のみ再生して0.1秒で停止します。
- 遊び方と詳細ルール、9枚・各5秒のスライドショーに対応します。

初回はService Workerが約12MiBのアプリと素材をバックグラウンド保存します。完了後は通信を切って再読み込みしても対局や説明表示を始められます。ブラウザの保存領域消去・ページ破棄には従います。対局途中の状態を再読み込み後に復元する機能はありません。設定とCPU指名は保存します。

## 検証

通常のルール・CPU・表示・音声制御のテスト：

```sh
node --test web-lite/engine.test.mjs web-lite/tests/*.test.mjs
```

Unityの実際のC#エンジン／CPUとの対局照合：

```sh
node web-lite/tests/verify-unity.mjs
QUOTA_PARITY_SEEDS=42 QUOTA_PARITY_ROUND_MODES=single,double node web-lite/tests/verify-unity.mjs
```

検証時だけUnityの`Cards.cs`、`Engine.cs`、`PythonRandom.cs`、`CpuProfiles.cs`を読み取り、Unity付属Monoで実行します。ゲームの実行時にはUnityのフォルダを参照しません。Monoの場所が違う場合は`QUOTA_MONO_ROOT`を指定します。

静的サーバーの起動後、Chromeによる画面操作テスト：

```sh
node web-lite/tests/browser.mjs
# 音声APIも実際に有効にして検証する場合
QUOTA_TEST_AUDIO=1 node web-lite/tests/browser.mjs
```

対象URLは`QUOTA_WEB_URL`、Chromeの実行ファイルは`CHROME_PATH`で変更できます。検証はブラウザ用の一時プロファイルを使用します。

検証内容と残る差分は[OFFLINE_PARITY.md](OFFLINE_PARITY.md)を参照してください。インターネット対戦は未実装です。
