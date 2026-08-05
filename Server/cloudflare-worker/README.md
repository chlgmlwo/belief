# BELIEF LLM 중계 서버 (Cloudflare Workers)

게임(브라우저)과 AI 회사 서버 사이에 서는 아주 작은 중계기다.
**API 키를 아는 것은 이 서버뿐이고, 게임 빌드에는 키가 들어가지 않는다.**

```
게임(브라우저) → [이 서버: 키를 붙여 전달] → AI 회사
```

## 왜 필요한가

웹 빌드는 브라우저에서 돌기 때문에 AI 회사 서버를 직접 부를 수 없다. 세 가지가 동시에 막는다.

1. 브라우저가 CORS로 차단한다
2. 빌드에 키를 넣으면 플레이하는 사람 누구나 꺼내 쓸 수 있다 (요금은 우리가 낸다)
3. 애초에 웹 빌드에는 키를 넣어 줄 경로가 없다 — 환경 변수도 에디터 설정도 브라우저에는 존재하지 않는다

---

## 배포 방법 A — 웹 대시보드 (권장, 설치 필요 없음)

Worker가 파일 하나뿐이라 브라우저에서 붙여넣기만 하면 된다.
**이 PC는 지금 npm이 깨져 있어서(아래 "알려진 문제" 참고) 이 방법이 확실하다.**

### 1. 계정 만들기
https://dash.cloudflare.com/sign-up — 무료, 카드 등록 없음.

### 2. Worker 만들기
대시보드 → 왼쪽 **Compute (Workers)** → **Create** → **Start with Hello World** → **Deploy**
이름은 `belief-llm-proxy` 로 한다.

### 3. 코드 붙여넣기
만들어진 Worker → **Edit code** → 편집기 내용을 전부 지우고
이 폴더의 **`worker.js` 전체를 붙여넣는다** → 우측 상단 **Deploy**

### 4. API 키 넣기
Worker 화면 → **Settings** → **Variables and Secrets** → **Add**
- Type: **Secret**
- Name: `OPENAI_API_KEY`
- Value: 발급받은 API 키

저장 후 한 번 더 **Deploy**.

> Secret으로 넣어야 한다. 일반 Variable로 넣으면 대시보드에서 값이 그대로 보인다.

### 5. 주소 확인
Worker 화면 위쪽에 주소가 있다:
```
https://belief-llm-proxy.<계정이름>.workers.dev
```

---

## 배포 방법 B — 명령줄 (npm이 정상일 때)

```
cd Server/cloudflare-worker
npx wrangler login
npx wrangler secret put OPENAI_API_KEY     # 프롬프트에 키 붙여넣기
npx wrangler deploy
```

키는 Cloudflare에만 저장되고 git에는 올라가지 않는다. 이후 재배포는 `npx wrangler deploy` 만.

---

## Unity에 연결하기 (배포 방법과 무관하게 공통)

1. `Assets/Belief/Data/AI/LlmProviderConfig_Proxy.asset` 열기
   - **Endpoint** = 위에서 받은 Worker 주소
   - **Use Proxy** = 켜짐 (켜져 있어야 게임이 키를 보내지 않는다)
2. 씬의 `GameInstaller` 선택
   - **Thinker Mode** = `Llm`
   - **Llm Provider Config** = 위 애셋

설정을 빠뜨려도 게임은 죽지 않는다 — 경고를 남기고 규칙 기반으로 동작한다.

---

## 동작 확인

```
curl -X POST https://belief-llm-proxy.<계정이름>.workers.dev ^
  -H "Content-Type: application/json" ^
  -H "Origin: https://chlgmlwo.github.io" ^
  -d "{\"messages\":[{\"role\":\"user\",\"content\":\"핑\"}]}"
```

- `choices` 가 들어 있는 JSON이 오면 성공
- `Origin` 헤더를 빼면 **403이 와야 정상**이다 (아무나 못 쓰게 막고 있다는 뜻)

---

## 비용이 새지 않게 하는 장치

주소를 아는 사람이 우리 요금으로 AI를 쓸 수 있으므로 다음을 걸어 뒀다.

| 장치 | 하는 일 |
|---|---|
| Origin 허용 목록 | GitHub Pages 주소와 로컬 테스트 주소에서 온 요청만 받는다 |
| 모델 고정 | 클라이언트가 어떤 모델을 요청하든 서버가 정한 모델로 덮어쓴다 |
| 응답 길이 상한 | `max_tokens`를 서버 상한(400)으로 자른다 |
| 프롬프트 길이 상한 | 12000자를 넘으면 거절한다 |

**한계를 분명히 해두면**: `Origin` 헤더는 브라우저가 붙이는 값이라 `curl` 같은 도구로는 위조할 수 있다.
이 목록만으로 악용을 완전히 막지는 못한다. 실제로 비용을 묶어 주는 것은 **모델 고정과 토큰 상한**이다.

더 조이려면 대시보드에서:
- Workers & Pages → 해당 Worker → **Settings** : 무료 플랜은 하루 10만 요청 상한이 기본으로 걸려 있다
- Security → WAF → **Rate limiting rules** : IP당 분당 요청 수 제한

주소가 유출된 것 같으면 Worker를 지웠다가 다른 이름으로 다시 만들면 주소가 바뀐다.

---

## 다른 AI 회사로 바꾸기

`worker.js`의 `callProvider()` 하나만 고치면 된다. **Unity 쪽은 손댈 필요 없다** —
게임은 OpenAI Chat Completions 형식으로 말하고, 그 형식을 맞춰 주는 게 이 서버의 역할이다.

응답은 게임이 기대하는 형태(`choices[0].message.content`)로 돌려줘야 한다.
형태가 다르면 게임이 파싱에 실패하고 **그 판단 1회만** 규칙 기반으로 대체된다(게임이 멈추지는 않는다).

---

## 알려진 문제 — 이 PC의 npm

`npm`/`npx` 가 `Class extends value undefined is not a constructor or null` 로 실패한다.

원인: `C:\Program Files\nodejs` 심볼릭 링크가 **v20.11.1** 을 가리키는데 실제 실행되는 `node` 는
**v24.18.0** 이라, npm이 옛 버전 트리의 모듈을 불러오다 깨진다.

고치려면(선택):
```
nvm use 24.18.0
```
또는 nvm에서 v20.11.1 을 제거한 뒤 Node를 다시 설치한다.

**배포 방법 A(웹 대시보드)를 쓰면 이 문제와 무관하다.**
