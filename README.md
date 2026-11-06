# shirabe

SearXNG を小さいローカル LLM で引くための、**証拠の門つき**の「調べる」。

> 門が厳しすぎて、小さいモデルだとだいたい「無かった」と言います。でも嘘は言いません。

検索そのものに値打ちはありません (Perplexity も Perplexica もやっています)。この道具の芯は、モデルの答えを信じないことです。

## 何をするか

1. SearXNG で検索し、上位 3 枚のページを読む (本文だけ、Shift_JIS も読む)
2. モデルに「資料だけから答えて、根拠の一文を一字も変えずに写せ」と頼む
3. **証拠の門**: 写した一文が本当にページにあるかを機構で照合する。無ければ答えごと捨てる
4. 答えの中の数字 (時刻・値段・日付) も、全部どこかのページに無ければ捨てる
5. 通れば「答え + 根拠 + 出どころ」、通らなければ「N 枚読んだが無かった」と、足りない物を名指しで返す

1 問・1 周・3 枚・45 秒で打ち切り。DeepResearch ではありません。問いの分解も、問い直しもしません。

## 門の規則 (この版)

- 引用は、空白を落としたうえで本文に**丸ごと**含まれているときだけ通す。言い換え・記号の揺れ・1 字の違いは落ちる
- 引用は 8 文字以上 (「営業時間」だけでは何にでも当たる)
- 答えに出てくる数字の連は、読んだどれかのページに全部あること

小さいモデルは「資料に無いことは書かない」と頼んでも書きます。頼むのではなく、根拠の無い答えを機構で捨てる、という考えです。
そのぶん再現率は低い。間違った答えを言うより「見つからなかった」と言う方を選んでいます。

## 使い方

前提:

- SearXNG が動いていて、`settings.yml` の `search.formats` に `json` が入っている (既定は html だけ)
- OpenAI 互換の `/v1/chat/completions` を持つ LLM サーバー (llama.cpp の `llama-server`、LM Studio、Ollama など)
- .NET 8 SDK

```bash
dotnet run --project src/Shirabe -- "東京タワーの高さは何メートル？" \
  --searxng http://localhost:8080 --llm http://localhost:8090
```

```
答え: 333メートルです。
根拠: 「東京タワーの高さはなぜ333ｍなの？」
出どころ: タワペディア | 東京タワー - Tokyo Tower https://www.tokyotower.co.jp/plan/towerpedia/
(これ以外のことは分かっていない)
2.9 秒
  証拠あり
```

無いときは、無いと言います。

```
1 枚読んだが、証拠つきの答えは無かった
足りない: シラベ食堂 西ケ原の営業時間、および資料に記載されている「平塚亭つるをか」の営業時間
3.0 秒
  見つからない (足りない: ...)
```

引数と環境変数:

| 引数 | 環境変数 | 既定 | 意味 |
|---|---|---|---|
| `--searxng URL` | `SHIRABE_SEARXNG` | `http://localhost:8080` | SearXNG |
| `--llm URL` | `SHIRABE_LLM` | `http://localhost:8090` | OpenAI 互換の LLM (`/v1/chat/completions` を足して叩く) |
| `--model 名` | `SHIRABE_MODEL` | `default` | モデル名 (llama-server は無視する) |
| | `SHIRABE_API_KEY` | なし | Bearer に付ける |
| `--llm-extra JSON` | `SHIRABE_LLM_EXTRA` | なし | 要求の body に混ぜるフィールド。llama-server の `{"id_slot":5}` や `{"cache_prompt":false}` など |
| `--pages N` | | 3 | 読む枚数 |
| `--budget 秒` | | 45 | 全体の打ち切り |
| `--json` | | | 結果を JSON で (`Trace` に門の足跡が入る) |

終了コード: `0` = 証拠つきの答え、`1` = 見つからなかった、`2` = 使い方の誤り・失敗。

## 中身

```
src/Shirabe/
  BoundedLookup.cs   流れ (検索 → 読む → 聞く → 門)。検索・読む・聞く は口を渡すので、偽の口でテストできる
  EvidenceGate.cs    証拠の門
  LookupAnswer.cs    モデルの答えの形と台本
  HttpPageReader.cs  HTML → 本文 (main / article 優先、charset は meta からも)
  SearxngSearch.cs   SearXNG の JSON API
  OpenAiChat.cs      OpenAI 互換の口 (1 往復、temperature 0)
  Program.cs         CLI
tests/Shirabe.Tests/ NUnit 29 本 (門・形・本文抜き・流れ)
```

```bash
dotnet test tests/Shirabe.Tests
```

## 書いていないこと

門の緩め方 (どこまでの揺れを許すか)、見つからなかったときの問い直し、翌日にもう一度調べる、といった部分はこの repo にはありません。この版は「厳しいまま、1 周で止まる」物です。

個人プロジェクトの部品を、単体で動く形に切り出した物です。コメントは日本語、識別子は英語。

## License

MIT
