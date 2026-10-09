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

負荷ツールは卓の準備時に使ったHTTP接続をポーリング開始前に閉じる。Nginxのアイドル接続期限（現設定15秒）より長いランプ待ちがあっても、古い接続を最初の状態取得に再利用しないためである。VPS上で試す場合、Macから最新の `tools/load_test_multitable.py` を `/tmp/quota-load-test.py` に再転送する。

10卓×4端末の120秒試験で504が消えたら、次に600秒の継続試験を行う。以前のJSONを上書きしないファイル名を使い、試験中に別のVPSターミナルで空きメモリと接続追跡件数を10秒ごとに記録する。

VPSのターミナル1で記録を始め、試験終了後にCtrl-Cで止める。

```sh
while :; do date -Is; free -m; sysctl net.netfilter.nf_conntrack_count net.netfilter.nf_conntrack_max; sleep 10; done | tee /tmp/quota-resources-10tables.log
```

別のVPSターミナル2で試験を実行する。

```sh
python3 /tmp/quota-load-test.py --base-url http://127.0.0.1:80 \
  --metrics-url http://127.0.0.1:80/api/metrics \
  --tables 10 --clients-per-table 4 --mode cpu --duration 600 \
  --ramp-seconds 30 --json-out /tmp/quota-load-10tables-600s.json
```

終了後は `errors`、504、p95/p99、`server kernel TCP` に加え、記録した `available` メモリと `nf_conntrack_count` の最大値を確認する。最後に `sudo journalctl -k --since '15 minutes ago' --no-pager | grep -i 'nf_conntrack.*table full'` でカーネルの破棄ログがないか調べる。`finished_tables=0` だけでは通信障害とは判断しない。

終了時の `/api/metrics` 取得がタイムアウトしても、ツールは3回まで再試行し、端末側の結果を `--json-out` に保存する。この場合 `server_after` は `null`、`server_after_error` に理由が入る。サーバー側の前後差は表示できないので、VPSで直接メトリクスを読み、Nginxのログとも突き合わせる。

端末側のタイムアウトは `during connect`（TCP接続）、`during send`（送信）、`during response_headers`（応答開始待ち）、`during response_body`（本文受信）の段階別に集計する。VPS上でツールを直接実行しても失敗するなら、SSHトンネルは原因から外れる。`/api/metrics` のAPI処理時間が短く、`listen_overflows` が増えない場合でも、受け付け前の接続や要求の読み込みが遅れる可能性があるため、この段階別の値を確認する。

負荷発生器は仮想端末ごとにHTTP接続を再利用する。SSHトンネルに `accept: Too many open files` が出た場合、まず負荷試験とトンネルをそれぞれCtrl-Cで終了し、Macのターミナルで `ulimit -n` を確認する。256程度なら、そのターミナルで `ulimit -n 4096` を実行してからSSHトンネルを開き直す。接続ごとに新しいトンネルを作る必要はない。このエラーがMac側のSSHに出ているときは、VPSのファイル記述子上限を変えても解消しない。

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

## 504が出た場合の切り分け

端末側に504があるのにサーバーのAPI計測に同じ件数がない場合、その要求はPythonの処理に届く前に失敗している。まずVPSのNginxエラーログを確認する。

```sh
sudo grep -E 'upstream timed out|worker_connections|Too many open files|connect\(\) failed' /var/log/nginx/error.log | tail -n 50
```

`while connecting to upstream` はNginxからPythonへの接続待ち、`while reading response header from upstream` は接続後の応答待ちを示す。既存設定の `proxy_connect_timeout` は3秒なので、約3秒の504は前者を疑う根拠になるが、ログで確認する。Pythonサーバーの接続待ち行列は128件に増やし、`/api/metrics` には `peak_inflight_api` とLinuxの `kernel_tcp.listen_overflows` / `listen_drops` を追加した。後者はVPS全体の累積カウンターなので、負荷試験スクリプトは開始前後の差を表示する。

接続待ちで504が続く場合、まずVPS上で稼働中のバージョンと待ち行列を確認する（以下は読み取り専用）。

```sh
curl -fsS http://127.0.0.1:8080/api/metrics | python3 -c 'import json,sys; m=json.load(sys.stdin); print("peak_inflight_api:",m.get("peak_inflight_api")); print("kernel_tcp:",m.get("kernel_tcp"))'
ss -ltn '( sport = :8080 )'
systemctl show quota -p MainPID -p LimitNOFILE -p NRestarts
sudo journalctl -u quota --since '15 minutes ago' --no-pager | tail -n 50
```

`peak_inflight_api` が表示されなければ旧バックエンドが動いている。`ss` の8080番LISTEN行で `Send-Q` が128程度になっているか確認する（`Recv-Q` はその時点の待ち件数）。`kernel_tcp.listen_overflows` / `listen_drops` が負荷試験中に増えるなら、接続の受け付けで溢れている。数値はホスト全体の累積なので、試験前後を比較する。バックエンドが再起動していないか、ファイル記述子上限やサービスログに異常がないかも併せて調べる。接続待ち行列が128で溢れもない場合は、Nginxと直接接続を比較して別の要因を切り分ける。

接続待ち行列の溢れがなくても、VPS内の `127.0.0.1:8080` への接続そのものがタイムアウトするなら、接続追跡も調べる。AlmaLinux 10.2 / 512MB VPSで10卓×4端末を試した際は、`nf_conntrack_max=4096` に達し、カーネルに `nf_conntrack: table full, dropping packet` が出た。試験後に `nf_conntrack_count` が小さく戻っても、試験中に満杯でなかった証拠にはならない。Nginxの504も、ローカル直結の5秒タイムアウトも、この段階でのパケット破棄と整合する。

```sh
sysctl net.netfilter.nf_conntrack_count net.netfilter.nf_conntrack_max
sudo journalctl -k --since '20 minutes ago' --no-pager | grep -i 'nf_conntrack.*table full'
# 再試験の間だけ上限を増やす。再起動すると元に戻る。
sudo sysctl -w net.netfilter.nf_conntrack_max=32768
```

その後、VPS自身からNginxの80番へ同じ10卓×4端末の試験を再実行し、504・接続タイムアウト・カーネルの `table full` が消えるか確かめる。同時に `free -m` と `nf_conntrack_count` を見て512MBのメモリ余裕も確認する。上限値を増やしても、ゲーム側のHTTP接続を毎回作り直す設計は残る。恒久策では接続の再利用を検討する。接続追跡をVPS全体で無効化するとfirewalldのステートフルな通信制御にも影響するため、この試験では行わない。

Nginxを通さずPython側だけを比較する場合は、別のSSHトンネルを `ssh -N -L 18081:127.0.0.1:8080 user@vps` で開き、負荷試験の `--base-url` と `--metrics-url` を `http://127.0.0.1:18081` に変えて実行する。直接接続では504を生成するNginxを経由しない。既存卓への影響を避けるため、対局している人がいないときに試す。
