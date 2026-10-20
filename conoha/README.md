# ConoHa VPSへの配備（AlmaLinux 10.x）

配備スクリプトとこのREADMEはGitで管理します。接続先などを記入する`config.sh`は管理対象外なので、必要に応じて別途バックアップしてください。Macから実行します。Dockerは不要です。

## 準備

```sh
cp conoha/config.example.sh conoha/config.sh
```

`config.sh`のVPS_HOSTをサーバーのIPまたはSSHの接続名に変更。必要に応じてSSH_USER、SSH_PORT、SSH_KEYを設定します。root以外の場合はパスワードなしのsudoが必要です。先に通常のsshで接続し、ホスト鍵と鍵認証を確認してください。スクリプトはパスワード入力なしで接続します。

ConoHa管理画面のセキュリティグループでSSHのポートとTCP80を許可してください。8080の公開は不要です。

## 初回設定

```sh
./conoha/init.sh
```

Nginx、Python、rsync、firewalldとSELinux設定ツールをインストールし、Quota用ユーザー・systemdサービスを作ります。Nginxは入口ページを`/`、QuotaのUnity/WebGL版を`/quota1/`、軽量Web版を`/quota2/`と`/quota/`で配信し、`/api/`を127.0.0.1:8080のPythonへ中継します。`/quota/`は軽量Web版と同じ公開ディレクトリを参照します。SELinuxは無効化せずファイルラベルと中継の許可を設定します。SSHとHTTPをfirewalldで許可します。

**Quota専用のVPS向けです。** Nginxのメイン設定を置き換えます。初回の元設定は`/etc/nginx/nginx.conf.quota-before`に保存します。他のサイトが既にあるサーバーにはそのまま使わないでください。設定値を変更した場合はinitを再実行し、続けてupdateを実行してください。既に動作中のPythonはinitでは再起動しません。

HTTPS・証明書の設定とswap作成はinitには含みません。512MBでの余裕は実際の対局で測定してください。

## HTTPS（ドメインでアクセスする場合）

DNSのAレコードがVPSを指し、ドメインからHTTPでアクセスできることを確認してから、Macで実行します。

```sh
QUOTA_CERT_EMAIL='通知先のメールアドレス' ./conoha/https.sh app1.gunsnstoneroses.com
```

メールを指定しない場合は登録せずに証明書を取得します。スクリプトはVPSでCertbotとNginxプラグインをインストールし、Let's Encryptの証明書を取得します。ドメイン宛てHTTPをHTTPSへ転送するNginx設定を追加します。IPアドレス宛てのHTTPは維持します。firewalldではHTTPSを許可し、Certbotの自動更新タイマー・更新後のNginx再読込を設定します。最後に更新のdry-runとHTTPSのAPI応答を検査します。

ConoHaのセキュリティグループでも**TCP 443の受信**を許可してください。TCP 80は証明書更新時の認証とHTTPSへの転送に必要なので残します。アプリの8080番は引き続き外部公開しません。

```sh
curl -I https://app1.gunsnstoneroses.com/
curl -I https://app1.gunsnstoneroses.com/quota/
curl -I https://app1.gunsnstoneroses.com/quota1/
curl -I https://app1.gunsnstoneroses.com/quota2/
curl -I http://app1.gunsnstoneroses.com/
```

後者はHTTPSへ301転送されます。`init.sh`を再実行しても別ファイルのHTTPS設定は残ります。`update.sh`はHTTPS設定を上書きしません。

## アプリ更新（初回の配信も同じ）

UnityからWebGLを先にビルド／エクスポートし、その後に実行します。

```sh
./conoha/update.sh
```

既定の出力先は`unity/Builds/WebGL`。別の場所はconfig.shのWEB_DIRに絶対パスで指定します。現在の非圧縮ビルドが対象で、圧縮ビルドは設定不一致を防ぐため拒否します。Unityのビルド自体は行いません。

UnityのWebファイル、`web-lite/`の軽量Web版、入口ページ、Pythonコード・商品／ランキングJSONをrsyncで転送し、新しいリリースへ公開先を切り替えます。初回の切り替え時にはNginxの入口と3つのQuota経路を検証して反映します。入口ページのソースと各ゲームの画像は`portal/`に置き、Quotaの港の画像は`visual/horizontal_base.jpg`から取り込みます。更新前にPythonの構文・データ読込を確認し、更新後にAPIとHTTPを確認。切替後に失敗した場合は以前の公開先へ戻します。

Python側のコードまたはJSONが変更された場合にサービスを再起動します。**再起動すると開催中の卓は消えます。** Webファイルだけの変更なら動作中のサービスを保持します。初回はサービスを起動します。

旧リリースは`/srv/quota/web/releases`と`/srv/quota/backend/releases`に残ります。ディスク容量を確認して適宜整理してください。currentのリンク先と実行中サービスが使うバックエンドは削除しないでください。更新中は他の更新を同時に実行しないでください。

## 確認・ログ

VPSにSSH接続して、以下で確認できます。

```sh
sudo systemctl status quota nginx
sudo journalctl -u quota -n 100 --no-pager
sudo nginx -t
free -h
df -h /srv/quota
```

ブラウザで`https://app1.gunsnstoneroses.com/`を開くと入口ページ、`/quota1/`はUnity版、`/quota2/`と`/quota/`は軽量Web版です。軽量Web版は現在オフライン対局のみ対応しています。IPアドレス宛てHTTPでも同じ構成になります。Pythonはsystemdが常駐・異常終了時の再起動を管理するためnohupは不要です。

## 初期設定が止まる場合

initは接続先と処理段階を表示します。Macで次のようにログを保存してください。

```sh
./conoha/init.sh 2>&1 | tee /tmp/quota-init.log
# SSH接続の詳細が必要な場合
QUOTA_SSH_DEBUG=1 ./conoha/init.sh 2>&1 | tee /tmp/quota-init-ssh.log
```

ConnectingのままならSSH接続段階です。接続先・SSHポート・ConoHaの受信許可を確認してください。dnf段階で止まる場合はVPSからリポジトリへの外向き通信や別のdnfプロセスによるロックを確認します。以前のinitがまだ動いていないか、VPS上で `ps -eo pid,etime,args | grep -E 'dnf|setup-server|bash -s'` と `sudo journalctl -n 80 --no-pager` を確認してください。ロックファイルを手動削除したり、処理中のdnfを強制終了したりせず、まず状態を確認します。

SSH接続開始は15秒、接続後は応答のない通信を約60秒で検出します。これはdnf処理の制限時間ではありません。再実行で初期設定をやり直せますが、以前の実行が継続している場合は同時実行を避けてください。

## 更新が止まって見える場合

updateはSSH接続・ローカル準備・VPS内コピー・Web転送・公開・API確認の段階を表示します。転送中はファイル名と進捗を表示します。初回はWebGL全量（現状約1.6GB）を送るため、回線次第で時間がかかります。rsyncは通信の無活動が120秒続けば終了します（転送全体を120秒に制限するものではありません）。

```sh
./conoha/update.sh 2>&1 | tee /tmp/quota-update.log
```

以前のupdateがまだ動いている場合は同時に再実行しないでください。まず以前のターミナルの処理を確認してください。VPS上の公開処理に入っている場合は、Macで中断してもリモート側が続いている可能性があります。

## 古いビルドの除去

更新はindex.htmlの現在のBuild参照だけを選択し、それ以外のテンプレート・StreamingAssets等は保持して転送します。元のローカル出力は削除しません。現状の転送対象は約86MBです。

以前のupdateを停止した後、`./conoha/cleanup.sh`で中断された転送の一時ディレクトリ（/tmp/quota-deploy.*）を削除できます。これらを使用中のプロセスが見つかった場合は拒否します。他の配備と同時に実行しないでください。

その後`./conoha/update.sh`を実行してください。新配備のAPI・HTTP確認成功後、保存済みWebリリースそれぞれのindex.htmlが参照しないBuildファイルを削除します。各リリースに必要なファイルは保持します。参照が解析できない／不足しているリリースは削除せず警告します。
