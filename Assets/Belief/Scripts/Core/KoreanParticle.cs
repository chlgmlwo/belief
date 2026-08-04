namespace Belief.Core
{
    /// <summary>
    /// 한국어 조사(助詞)를 앞 단어의 받침에 맞춰 골라 준다.
    ///
    /// 로그 문장을 `$"{이름}가(이) …"`처럼 두 형태를 병기하는 방식으로 쓰면 화면에
    /// "상인가(이) 여관에서 시장(으)로 이동했다."처럼 나와 가독성을 크게 해친다(2026-08-05
    /// 사용자 지적). NPC/장소 이름은 전부 데이터에서 오기 때문에 문장을 미리 확정할 수 없으므로,
    /// 받침 유무를 실제로 계산해서 조사를 고른다.
    ///
    /// 판정 방식: 한글 음절(U+AC00~U+D7A3)은 (코드 - 0xAC00) % 28 이 종성 인덱스이고 0이면
    /// 받침이 없다. 한글이 아닌 문자로 끝나면(숫자·영문 등) 받침 없음으로 취급한다.
    /// </summary>
    public static class KoreanParticle
    {
        const int HangulBase = 0xAC00;
        const int HangulLast = 0xD7A3;
        const int JongseongCount = 28;
        /// <summary>종성 'ㄹ'의 인덱스 - "으로/로"만 이 값을 받침 없음과 똑같이 취급한다.</summary>
        const int JongseongRieul = 8;

        /// <summary>마지막 한글 음절의 종성 인덱스. 한글이 하나도 없거나 빈 문자열이면 -1.
        ///
        /// 끝에 붙은 괄호·구두점·공백은 건너뛰고 그 앞의 한글을 본다 - "북문(외곽)"처럼 괄호로
        /// 끝나는 이름을 그대로 판정하면 받침 없음으로 잘못 읽혀 "북문(외곽)로"가 된다(실제
        /// 스테이지 이름 형식). 읽을 때는 "…외곽"으로 끝나므로 "북문(외곽)으로"가 맞다.</summary>
        static int FinalJongseong(string word)
        {
            if (string.IsNullOrEmpty(word)) return -1;
            for (int i = word.Length - 1; i >= 0; i--)
            {
                char c = word[i];
                if (c >= HangulBase && c <= HangulLast) return (c - HangulBase) % JongseongCount;
                // 한글 음절이 아니면서 조사 판정에 영향을 주지 않는 문자만 건너뛴다.
                if (char.IsWhiteSpace(c) || char.IsPunctuation(c) || char.IsSymbol(c)) continue;
                // 숫자·영문 등으로 끝나면 발음 규칙이 달라 여기서 판정을 포기한다(받침 없음 취급).
                return -1;
            }
            return -1;
        }

        static bool HasFinalConsonant(string word)
        {
            int jong = FinalJongseong(word);
            return jong > 0;
        }

        /// <summary>주격 조사 - 받침 있으면 "이", 없으면 "가".</summary>
        public static string Subject(string word) => HasFinalConsonant(word) ? "이" : "가";

        /// <summary>목적격 조사 - 받침 있으면 "을", 없으면 "를".</summary>
        public static string Object(string word) => HasFinalConsonant(word) ? "을" : "를";

        /// <summary>보조사 - 받침 있으면 "은", 없으면 "는".</summary>
        public static string Topic(string word) => HasFinalConsonant(word) ? "은" : "는";

        /// <summary>방향 조사 - 받침이 없거나 'ㄹ' 받침이면 "로", 그 외에는 "으로".
        /// (예: 시장→시장으로, 초소→초소로, 저택 앞→저택 앞으로, 서울→서울로)</summary>
        public static string Direction(string word)
        {
            int jong = FinalJongseong(word);
            if (jong <= 0 || jong == JongseongRieul) return "로";
            return "으로";
        }

        /// <summary>"이름+주격조사"를 붙여 반환하는 축약 - 로그 문장에서 가장 많이 쓴다.</summary>
        public static string WithSubject(string word) => word + Subject(word);

        /// <summary>"이름+목적격조사"를 붙여 반환하는 축약.</summary>
        public static string WithObject(string word) => word + Object(word);

        /// <summary>"장소+방향조사"를 붙여 반환하는 축약.</summary>
        public static string WithDirection(string word) => word + Direction(word);
    }
}
