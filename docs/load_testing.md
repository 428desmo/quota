# 複数卓のAPI負荷試験

WebGLのダウンロードを除き、卓の作成、参加、状態取得、行動、CPU思考を測る。対局中の卓に影響するため、Mac上の試験用サーバー、または利用者のいない時間帯のVPSで実行する。VPSのサーバーを更新すると既存の卓が消えるので、VPSでの計測はバックエンド更新後に行う。

## 試験の実行

まずMacだけで試す場合は、Mac上でサーバーを起動し、別のMacのターミナルから負荷をかける。この手順ではVPSは使わない。

```sh
python3 tools/serve_unity_web.py --host 127.0.0.1 --port 8080
python3 tools/load_test_multitable.py --tables 5 --clients-per-table 3 \
  --mode cpu --duration 120 --metrics-url http://127.0.0.1:8080/api/metrics \
  --json-out /tmp/quota-load-cpu.json
```

VPSを測る場合は、Macで `./conoha/update.sh` を実行して計測機能入りの `tools/serve_unity_web.py` をVPSに配備する。既存のQuotaサービスがVPSで起動するため、VPSにSSHログインしてサーバーを手動起動する必要はない。`update.sh` は `tools/load_test_multitable.py` を転送しない。負荷発生スクリプトはMacで実行する。

VPSのNginxも含めて測るには、Macのターミナルで次のSSHトンネルを開いたままにする。VPSの公開ポートや設定を増やす必要はない。`user@vps`は自分のSSH接続先に置き換える。

```sh
ssh -N -L 18080:127.0.0.1:80 user@vps
```

別のMacのターミナルで負荷発生スクリプトを実行する。接続先 `127.0.0.1:18080` はMac側のトンネル入口で、通信はVPSのNginx（80番）を経てQuotaのPythonサーバーへ届く。

```sh
python3 tools/load_test_multitable.py --base-url http://127.0.0.1:18080 \
  --metrics-url http://127.0.0.1:18080/api/metrics \
  --tables 10 --clients-per-table 4 --mode cpu --duration 300 \
  --ramp-seconds 30 --json-out /tmp/quota-load-10-tables.json
```

`--mode cpu` はリーダーと残りの端末が観戦し、CPUだけが対局する。ラウンド間の `next_round` も送る。`--mode human` は定員までを人間席とし、自分の手番に合法な「パス」を送る。余った端末は観戦する。`--mode lobby` は募集中のまま状態取得する。どのモードでも各端末は既定で0.4秒ごとに `/api/state` を取得する。試験終了時に仮想端末は退出するが、対局中の卓はサーバーの既存仕様に従って一定時間残る。

`--ramp-seconds` は仮想端末のポーリング開始を卓ごとに指定秒数へ分散する。指定値は `--duration` より短くする。表示される毎秒リクエスト数は試験全体の平均なので、立ち上げ後の定常区間は別に評価する。

まず1卓で動作を確認し、5、10、25卓と段階的に増やす。CPU・human・lobbyをそれぞれ実行する。短い試験だけでは履歴やメモリの増加は見えないため、想定最大規模で20〜30分の継続試験も行う。`--tables` と `--clients-per-table` の積が端末数であり、0.4秒間隔なら基礎リクエスト数は概ね「端末数×2.5/秒」。例えば10卓×4端末で約100回/秒となる。

## サーバーの計測値

`GET /api/metrics` はサーバー自身からのアクセスに限定したJSONで、ゲームの名前・端末ID・行動本文は含まない。Nginx経由では接続元IPがループバックのときだけ取得できる。SSHトンネルを使わない場合、VPS上で `curl http://127.0.0.1:8080/api/metrics` とする。値はPythonプロセス起動以来の累積で、負荷試験スクリプトは開始前後の差を表示・保存する。バックエンドを再起動すると計測値も卓もリセットされる。

- `routes["GET /api/state"]`: 件数、HTTPステータス別件数、応答時間の分布と最大値、ロック待ち・保持時間、JSON化時間、送信バイト数。
- `cpu`: CPUの行動選択の回数と合計・最大時間。
- `hall`: 卓数、対局中の卓数、接続端末数、全卓の行動履歴数、診断履歴と診断対象端末数。
- `process`: Pythonの現在/最大RSS、ユーザー/システムCPU秒数、スレッド数。macOSでは現在RSSが `null` になるが、Linux VPSでは `/proc/self/statm` から得られる。

応答時間とロック待ちは固定バケットのヒストグラムで保持する。サンプルを無制限に蓄積しない。診断イベントは直近200件、診断対象端末は直近2048件に制限し、残り秒数だけの変化では診断イベントを増やさない。負荷試験のJSONには端末側集計とサーバー計測の前後値が入る。

## 結果の読み方

最初に `statuses` がすべて200、`errors` が空であることを確認する。CPU対局なら `cpu_decisions` と `action_entries` が増え、人間対局なら `/api/action` の成功件数が増える。増えなければ、負荷試験の卓が進行していない。`finished_tables` は終了まで進んだ卓の数で、短い試験では0でも異常ではない。

端末側の `p50`、`p95`、`p99` は上限値を示す粗いバケットである。たとえば `p95=≤200 ms` は95%の応答が200ms以内だった意味。0.4秒のポーリング間隔に対し、目安としてp95が200ms以下、p99が400ms以下を狙う。SSHトンネルを使う場合、端末側の時間にはMacとVPS間の往復も含まれる。サーバー側の `duration_ms_buckets` はAPI処理とレスポンス送信の時間で、ネットワーク往復は含まれない。

ロック待ちが伸びるなら全卓共通のロックが詰まっている。ロック保持時間が伸びるならCPU思考・卓状態の処理を疑う。JSON化時間と送信バイト数が増えるなら行動履歴の肥大化を疑う。サーバーの `cpu_user_seconds + cpu_system_seconds` の増加が試験秒数に近づくなら、Pythonのほぼ1コアを使い切っている。現在RSSやVPS全体の使用メモリが継続的に増え、試験終了後も戻らない場合は長時間運用を再評価する。

512MB VPSではPythonだけでなくOSとNginxの分も必要なので、`free -m`、`sudo systemctl status quota nginx`、`sudo journalctl -u quota --disk-usage` も併せて見る。試験後のRSSの安定、余裕のあるavailableメモリ、OOMやHTTPエラーがないことを確認する。合格する卓数は実測で決め、卓数だけでなく端末数と対局時間も記録する。
