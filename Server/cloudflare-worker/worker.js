/**
 * BELIEF - LLM 중계 서버 (Cloudflare Worker)
 *
 * 왜 필요한가:
 *   웹(WebGL) 빌드는 브라우저에서 돌기 때문에 AI 회사 서버를 직접 부를 수 없다.
 *   1) 브라우저가 CORS로 차단한다
 *   2) 빌드에 API 키를 넣으면 플레이어 누구나 꺼내 쓸 수 있다(요금은 우리가 낸다)
 *   3) 애초에 웹 빌드에는 키를 넣어 줄 경로가 없다(환경 변수도 EditorPrefs도 브라우저엔 없다)
 *
 *   그래서 키를 아는 것은 이 서버 하나뿐이고, 게임은 여기로만 요청한다.
 *
 * 게임이 보내는 요청/기대하는 응답 형식은 OpenAI Chat Completions 그대로다
 * (Unity 쪽 OpenAiTransport가 이미 그 형식으로 만들고 파싱한다).
 * 그래서 응답 본문은 가공하지 않고 그대로 돌려준다 - 여기서 형태를 바꾸면 게임이 파싱에 실패해
 * 그 판단 1회가 규칙 기반으로 대체된다.
 *
 * 다른 AI 회사로 바꾸고 싶으면 callProvider()만 고치면 된다. Unity 쪽은 손댈 필요 없다.
 */

// ─── 설정 ────────────────────────────────────────────────────────────────────
// 게임을 서비스하는 주소만 허용한다. 로컬 테스트 주소도 함께 넣어 둔다.
// 주의: Origin 헤더는 브라우저가 붙이는 값이라 curl 등으로는 위조할 수 있다 - 이것만으로
// 악용을 막을 수는 없고, 아래의 모델/토큰 상한과 Cloudflare 대시보드의 Rate Limiting을 함께 써야 한다.
const ALLOWED_ORIGINS = [
  "https://chlgmlwo.github.io",
  "http://localhost:8123",
  "http://127.0.0.1:8123",
];

// 비용이 튀지 않도록 서버가 최종 결정권을 갖는 값들.
// 클라이언트가 뭘 보내든 여기서 덮어쓰거나 상한을 건다 - 빌드는 누구나 뜯어볼 수 있으므로
// "클라이언트가 보낸 값을 믿는다"는 전제를 두지 않는다.
const MODEL = "gpt-4o-mini";      // 클라이언트가 어떤 모델을 요청하든 이걸로 고정
const MAX_OUTPUT_TOKENS = 400;    // 응답 길이 상한
const MAX_PROMPT_CHARS = 12000;   // 프롬프트 길이 상한(너무 길면 거절)
const MAX_MESSAGES = 8;

const PROVIDER_URL = "https://api.openai.com/v1/chat/completions";

// ─── 진입점 ──────────────────────────────────────────────────────────────────
export default {
  async fetch(request, env) {
    const origin = request.headers.get("Origin") || "";
    const cors = corsHeaders(origin);

    // 브라우저는 본 요청 전에 OPTIONS(preflight)를 먼저 보낸다. 여기서 막히면
    // 게임에서는 그냥 "네트워크 오류"로만 보이므로 반드시 처리해야 한다.
    if (request.method === "OPTIONS") {
      return new Response(null, { status: 204, headers: cors });
    }

    if (request.method !== "POST") {
      return json({ error: { message: "POST만 허용됩니다." } }, 405, cors);
    }

    if (!isAllowedOrigin(origin)) {
      return json({ error: { message: "허용되지 않은 origin입니다." } }, 403, cors);
    }

    if (!env.OPENAI_API_KEY) {
      // 키를 안 넣고 배포한 경우. 게임은 이 응답을 받고 규칙 기반으로 대체하므로 멈추지는 않는다.
      return json({ error: { message: "서버에 API 키가 설정되지 않았습니다 (wrangler secret put OPENAI_API_KEY)." } }, 500, cors);
    }

    let body;
    try {
      body = await request.json();
    } catch {
      return json({ error: { message: "JSON 본문을 읽을 수 없습니다." } }, 400, cors);
    }

    const sanitized = sanitize(body);
    if (sanitized.error) {
      return json({ error: { message: sanitized.error } }, 400, cors);
    }

    try {
      const upstream = await callProvider(sanitized.payload, env.OPENAI_API_KEY);
      const text = await upstream.text();
      // 응답을 그대로 통과시킨다 - 게임이 기대하는 형태를 여기서 바꾸지 않는다.
      return new Response(text, {
        status: upstream.status,
        headers: { ...cors, "Content-Type": "application/json" },
      });
    } catch (e) {
      return json({ error: { message: "상위 API 호출 실패: " + e.message } }, 502, cors);
    }
  },
};

// ─── 상위 API 호출 (다른 회사로 바꾸려면 여기만 고친다) ──────────────────────
async function callProvider(payload, apiKey) {
  return fetch(PROVIDER_URL, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: "Bearer " + apiKey,
    },
    body: JSON.stringify(payload),
  });
}

// ─── 입력 정리 ───────────────────────────────────────────────────────────────
function sanitize(body) {
  const messages = Array.isArray(body?.messages) ? body.messages : null;
  if (!messages || messages.length === 0) {
    return { error: "messages가 비어 있습니다." };
  }
  if (messages.length > MAX_MESSAGES) {
    return { error: "messages가 너무 많습니다." };
  }

  let total = 0;
  const cleaned = [];
  for (const m of messages) {
    const role = m?.role === "system" || m?.role === "assistant" ? m.role : "user";
    const content = typeof m?.content === "string" ? m.content : "";
    total += content.length;
    if (total > MAX_PROMPT_CHARS) {
      return { error: "프롬프트가 너무 깁니다." };
    }
    cleaned.push({ role, content });
  }

  const payload = {
    model: MODEL,                                        // 클라이언트 값 무시
    messages: cleaned,
    max_tokens: Math.min(Number(body?.max_tokens) || MAX_OUTPUT_TOKENS, MAX_OUTPUT_TOKENS),
    temperature: clamp(Number(body?.temperature), 0, 2, 0.7),
  };

  // 게임이 JSON 형식 응답을 요구했을 때만 그대로 전달한다.
  if (body?.response_format?.type === "json_object") {
    payload.response_format = { type: "json_object" };
  }

  return { payload };
}

// ─── 유틸 ────────────────────────────────────────────────────────────────────
function isAllowedOrigin(origin) {
  return ALLOWED_ORIGINS.includes(origin);
}

function corsHeaders(origin) {
  // 허용 목록에 있는 origin만 그대로 되돌려준다. 와일드카드(*)를 쓰지 않는 이유는
  // 아무 웹페이지나 이 서버를 불러 쓰는 것을 막기 위해서다.
  const allow = isAllowedOrigin(origin) ? origin : ALLOWED_ORIGINS[0];
  return {
    "Access-Control-Allow-Origin": allow,
    "Access-Control-Allow-Methods": "POST, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type",
    "Access-Control-Max-Age": "86400",
    Vary: "Origin",
  };
}

function clamp(v, min, max, fallback) {
  if (!Number.isFinite(v)) return fallback;
  return Math.min(Math.max(v, min), max);
}

function json(obj, status, headers) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { ...headers, "Content-Type": "application/json" },
  });
}
