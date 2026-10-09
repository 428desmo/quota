# ゲームモードとラウンドBGM

## ゲームモード

スタート画面の名前欄の上で選ぶ。選択は端末の `PlayerPrefs` に保存する。

- **オフライン**: この端末内のゲームエンジンが対局を進める。人間は1人で、残りはCPU。卓一覧と通信は使わず、観戦・通信タイムアウトの設定も出さない。
- **インターネット通信**: 卓・行動・CPU処理はサーバーが管理する。WebGLでは、Webアプリを開いたURLのオリジン（スキーム、ホスト、ポート）に `/api/` を送る。Unity EditorなどURLのない環境は `QUOTA_SERVER_URL` 環境変数を使い、未設定なら `http://133.88.122.153` に接続する。
- **ローカル通信**: 選択肢には「準備中」と表示する。現段階では選べない。ブラウザ内のUnity WebGLアプリがLAN向けHTTPサーバーになることはできない。将来、ネイティブアプリでホスト機能を実装するか、LAN内の別プロセスをサーバーにする方式を検討する。

## ラウンドBGM

原音源は `sound/play_bgm_01.mp3`。Unityで使うクリップは `unity/Assets/Resources/QuotaBgm/intro.ogg`（0:00〜0:16）と `loop.ogg`（0:16〜1:32）である。次のコマンドで原音源から再生成できる。

```bash
ffmpeg -i sound/play_bgm_01.mp3 -ss 0 -t 16 -c:a libvorbis -q:a 5 unity/Assets/Resources/QuotaBgm/intro.ogg
ffmpeg -i sound/play_bgm_01.mp3 -ss 16 -t 76 -c:a libvorbis -q:a 5 unity/Assets/Resources/QuotaBgm/loop.ogg
```

各ラウンドの開始時に冒頭を一度再生し、続けてループ区間を繰り返す。ラウンド終了、対局からの退出、スタート画面への遷移で停止する。Webブラウザの音声再生には、ブラウザ上でのタップやクリックが必要になる場合がある。
