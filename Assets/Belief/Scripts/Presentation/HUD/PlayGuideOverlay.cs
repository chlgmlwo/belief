using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Belief.Presentation.World;

namespace Belief.Presentation.HUD
{
    /// <summary>1구역에 처음 들어왔을 때 한 번 도는 플레이 가이드. 화면을 검게 덮고 지금 설명하는
    /// 자리만 구멍을 내어 보여 주며, 정보원이 말풍선으로 설명한다.
    ///
    /// <b>구멍은 마스크나 셰이더가 아니라 검은 사각형 네 장으로 만든다</b>(설명할 자리의 위/아래/
    /// 왼쪽/오른쪽). 마스크를 쓰면 그 아래에 있는 HUD 캔버스까지 마스크 대상이 되어 원래 UI의 그리기
    /// 순서를 건드리게 되는데, 네 장이면 덮개가 이 캔버스 안에서만 끝나 아래를 전혀 손대지 않는다.
    ///
    /// 설명 대상은 좌표가 아니라 <b>실제 오브젝트</b>로 지정한다 - HUD 배치가 바뀌어도 구멍이 따라
    /// 움직인다. UI(RectTransform)와 지도 위 오브젝트(Renderer)를 모두 받아 각각의 방식으로 화면
    /// 사각형을 구한다.
    ///
    /// 브리핑 화면이 먼저 떠 있으므로 그것이 닫힐 때까지 기다렸다 시작한다.</summary>
    public class PlayGuideOverlay : MonoBehaviour
    {
        public const string CompletedPrefKey = "Belief_PlayGuideCompleted";
        public static bool IsCompleted => PlayerPrefs.GetInt(CompletedPrefKey, 0) == 1;

        /// <summary>덮개 색 - 완전히 가리지 않고 비쳐 보일 만큼만 덮는다. 설명하는 자리에는 덮개가
        /// 아예 없으므로, 이 정도 농도로도 밝기 차이가 충분히 난다.</summary>
        static readonly Color DimColor = new Color(0.04f, 0.04f, 0.04f, 0.82f);
        static readonly Color PaperColor = new Color(0.929f, 0.918f, 0.886f, 1f);
        static readonly Color InkColor = new Color(0.180f, 0.161f, 0.149f, 1f);
        static readonly Color MutedInk = new Color(0.42f, 0.39f, 0.37f, 1f);
        static readonly Color LinkColor = new Color(0.706f, 0.259f, 0.259f, 1f);

        /// <summary>구멍을 대상보다 넉넉하게 낸다. 딱 맞게 내면 테두리가 잘려 보이고, 클릭 판정이
        /// 그려진 그림보다 작은 자리(탭이 그렇다)는 정작 설명할 그림이 덮개에 가려진다.</summary>
        const float SpotlightPadding = 34f;
        const float FadeDuration = 0.22f;

        const float InformantSize = 250f;
        const float BubbleWidth = 980f;
        const float BubbleHeight = 240f;
        /// <summary>말풍선 왼쪽 끝 - 정보원 오른쪽에 바로 붙는다(정보원 x 70 + 폭).</summary>
        const float BubbleLeft = 340f;

        /// <summary>한 걸음 = 설명 한 마디 + 그때 밝힐 자리.</summary>
        class Step
        {
            public string Text;
            /// <summary>비어 있으면 구멍 없이 전체를 덮는다(인사/마무리).</summary>
            public Func<List<Transform>> Targets;
        }

        HudPresenter hud;
        WorldPresenter world;
        StageBriefingPresenter briefing;
        TMP_FontAsset font;

        readonly List<Step> steps = new List<Step>();
        int index = -1;

        Canvas canvas;
        RectTransform canvasRect;
        CanvasGroup group;
        RectTransform dimTop, dimBottom, dimLeft, dimRight;
        RectTransform informantRect, bubbleRect, tailRect;
        TMP_Text bodyText, counterText;
        GameObject skipGo;

        public void Begin(HudPresenter hud, WorldPresenter world, StageBriefingPresenter briefing,
            TMP_FontAsset font, Sprite informantSprite)
        {
            this.hud = hud;
            this.world = world;
            this.briefing = briefing;
            this.font = font;

            BuildSteps();
            BuildUI(informantSprite);
            StartCoroutine(WaitForBriefingThenStart());
        }

        /// <summary>에디터에서 한 걸음씩 그려 보기 위한 진입점 - 코루틴(브리핑 대기·페이드) 없이
        /// 화면만 만든다. 실제 플레이 경로는 <see cref="Begin"/> 하나뿐이다.</summary>
        public void BuildForPreview(HudPresenter hud, WorldPresenter world, TMP_FontAsset font, Sprite informantSprite)
        {
            this.hud = hud;
            this.world = world;
            this.font = font;
            BuildSteps();
            BuildUI(informantSprite);
            canvas.gameObject.SetActive(true);
            group.alpha = 1f;
        }

        public int StepCount => steps.Count;
        public GameObject CanvasGo => canvas != null ? canvas.gameObject : null;

        /// <summary>0부터 세는 걸음 하나를 바로 그린다(미리보기 전용).</summary>
        public void ShowStep(int i)
        {
            index = Mathf.Clamp(i, 0, steps.Count - 1) - 1;
            Next();
        }

        // ------------------------------------------------------------ 내용

        void BuildSteps()
        {
            steps.Add(new Step
            {
                Targets = null,
                Text = "정보원입니다. 현장에서 모인 정보와 소문은 저희가 움직입니다. 어디에, 누구에게 정보를 흘릴지만 정해 주십시오.",
            });

            steps.Add(new Step
            {
                Targets = () => Ui("MissionArea/GoalCard01", "MissionArea/GoalCard02"),
                Text = "이번 스테이지에서 처리해야 할 미션 목록입니다. 하나를 마치면 다음 미션으로 넘어갑니다. 현재 진행 중인 미션은 여기서 확인할 수 있습니다.",
            });

            steps.Add(new Step
            {
                Targets = () => Ui("MissionArea/MissionSummaryCard"),
                Text = "현재 미션의 성공 조건입니다. 두 조건 중 하나만 성립시키면 임무는 완료됩니다. 어떤 방법으로 조건을 만들지는 자유입니다.",
            });

            steps.Add(new Step
            {
                Targets = () => Ui("HandArea/HandCard1", "HandArea/HandCard2", "HandArea/HandCard3", "HandArea/HandCard4"),
                Text = "정보를 흘리는 방법은 두 가지입니다. SPREAD는 장소에 퍼뜨리고, DELIVER는 특정 인물에게 직접 전달합니다. 상황에 맞는 방식을 선택하십시오.",
            });

            steps.Add(new Step
            {
                Targets = WorldTargets,
                Text = "카드를 고르면 그 정보를 사용할 수 있는 대상만 선택할 수 있습니다. 대상이 표시되지 않는다면 그 카드로는 그곳에 개입할 수 없습니다.",
            });

            steps.Add(new Step
            {
                Targets = () => Ui("ProceedButton"),
                Text = "대상까지 정하면 저희가 실행합니다. 정보가 전달되면 사람들은 자신의 성향과 상황에 따라 판단하고 행동합니다. 그 결과를 지켜보십시오.",
            });

            steps.Add(new Step
            {
                Targets = () => Ui("RightPeekArea/SharedTabRoot/LogTabRoot/ClickArea",
                                   "RightPeekArea/SharedTabRoot/ProfileTabRoot/ClickArea"),
                Text = "Log에서는 그날 벌어진 일을 확인할 수 있습니다. Profile에서는 인물의 성향과 현재 믿음을 볼 수 있습니다. 다음 정보를 고를 때 참고하십시오.",
            });

            steps.Add(new Step
            {
                Targets = () => Ui("HeaderArea/TurnCard"),
                Text = "정보를 한 번 운용할 때마다 하루가 지나갑니다. 각 미션에는 제한 기간이 있습니다. 남은 기간 안에 목표를 만들어내십시오.",
            });

            steps.Add(new Step
            {
                Targets = null,
                Text = "정보를 믿게 만드는 것만이 방법은 아닙니다. 의심하게 하고, 조사하게 하고, 움직이게 만드는 것 역시 방법입니다. 원하는 방향으로 사람을 움직이십시오.",
            });
        }

        /// <summary>HUD 캔버스 안의 오브젝트를 경로로 찾는다 - 없으면 조용히 건너뛴다(그 자리만
        /// 구멍에서 빠질 뿐 가이드는 계속 돈다).</summary>
        List<Transform> Ui(params string[] paths)
        {
            var found = new List<Transform>();
            var root = HudRoot();
            if (root == null) return found;
            foreach (var p in paths)
            {
                var t = root.Find(p);
                if (t != null && t.gameObject.activeInHierarchy) found.Add(t);
            }
            return found;
        }

        /// <summary>HUD 캔버스의 루트. HudPresenter가 알려주는 값을 먼저 쓰되, 아직 Bind 전이면
        /// 씬에서 직접 찾는다 - 가이드가 언제 시작되든 설명할 자리를 놓치지 않게.</summary>
        Transform hudRoot;

        Transform HudRoot()
        {
            if (hudRoot != null) return hudRoot;
            hudRoot = hud != null ? hud.CanvasRoot : null;
            if (hudRoot != null) return hudRoot;

            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.name == "HudCanvas") { hudRoot = c.transform; break; }
            return hudRoot;
        }

        /// <summary>지도 설명용 - 장소 카드와 그 위의 사람들을 전부 감싸는 사각형이 곧 "지도"다.
        /// 사람을 빼면 지도 아래쪽에 서 있는 NPC가 덮개에 걸려, 정작 "대상을 고른다"는 설명에서
        /// 고를 대상이 안 보인다.</summary>
        List<Transform> WorldTargets()
        {
            var found = new List<Transform>();
            if (world == null) return found;
            foreach (var v in world.LocationViews.Values)
                if (v != null && v.gameObject.activeInHierarchy) found.Add(v.transform);
            foreach (var v in world.NpcViews.Values)
                if (v != null && v.gameObject.activeInHierarchy) found.Add(v.transform);
            return found;
        }

        // ------------------------------------------------------------ 진행

        IEnumerator WaitForBriefingThenStart()
        {
            // 브리핑이 화면을 덮고 있는 동안 가이드가 먼저 뜨면 둘이 겹친다.
            while (briefing != null && briefing.IsShowing) yield return null;
            yield return null;

            canvas.gameObject.SetActive(true);
            Next();
            yield return Fade(0f, 1f);
        }

        public void Next()
        {
            index++;
            if (index >= steps.Count) { StartCoroutine(FinishRoutine()); return; }

            var step = steps[index];
            counterText.text = $"{index + 1} / {steps.Count}";
            bodyText.text = step.Text;

            var targets = step.Targets != null ? step.Targets() : null;
            if (targets == null || targets.Count == 0) ApplySpotlight(null);
            else ApplySpotlight(UnionScreenRect(targets));
        }

        IEnumerator FinishRoutine()
        {
            yield return Fade(1f, 0f);
            PlayerPrefs.SetInt(CompletedPrefKey, 1);
            PlayerPrefs.Save();
            Destroy(canvas.gameObject);
            Destroy(this);
        }

        void Skip() => StartCoroutine(FinishRoutine());

        IEnumerator Fade(float from, float to)
        {
            float t = 0f;
            group.alpha = from;
            while (t < FadeDuration)
            {
                t += Time.unscaledDeltaTime;
                group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / FadeDuration));
                yield return null;
            }
            group.alpha = to;
        }

        // ------------------------------------------------------------ 구멍 뚫기

        /// <summary>대상들을 전부 감싸는 화면 사각형. UI는 RectTransform의 네 모서리를, 지도 위
        /// 오브젝트는 Renderer의 경계를 카메라로 투영해 구한다.</summary>
        Rect UnionScreenRect(List<Transform> targets)
        {
            bool any = false;
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            var corners = new Vector3[4];
            var cam = Camera.main;

            foreach (var t in targets)
            {
                if (t is RectTransform rt)
                {
                    // Overlay 캔버스면 월드 좌표가 곧 화면 좌표지만, 캔버스가 카메라 모드일 수도 있다
                    // (에디터 미리보기가 그렇게 바꿔 놓는다). 어느 쪽이든 맞도록 그 캔버스의 카메라로
                    // 투영한다 - Overlay는 카메라가 null이고, 그때 이 함수는 좌표를 그대로 돌려준다.
                    var ownerCanvas = rt.GetComponentInParent<Canvas>();
                    var uiCam = ownerCanvas != null && ownerCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                        ? ownerCanvas.worldCamera : null;

                    rt.GetWorldCorners(corners);
                    for (int i = 0; i < 4; i++)
                        Grow(RectTransformUtility.WorldToScreenPoint(uiCam, corners[i]), ref xMin, ref yMin, ref xMax, ref yMax);
                    any = true;
                    continue;
                }

                var renderers = t.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0 || cam == null) continue;
                foreach (var r in renderers)
                {
                    if (!r.enabled) continue;
                    var b = r.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = new Vector3(
                            (i & 1) == 0 ? b.min.x : b.max.x,
                            (i & 2) == 0 ? b.min.y : b.max.y,
                            (i & 4) == 0 ? b.min.z : b.max.z);
                        Grow(cam.WorldToScreenPoint(corner), ref xMin, ref yMin, ref xMax, ref yMax);
                    }
                    any = true;
                }
            }

            if (!any) return Rect.zero;
            return Rect.MinMaxRect(xMin - SpotlightPadding, yMin - SpotlightPadding,
                                   xMax + SpotlightPadding, yMax + SpotlightPadding);
        }

        static void Grow(Vector3 p, ref float xMin, ref float yMin, ref float xMax, ref float yMax)
        {
            if (p.x < xMin) xMin = p.x;
            if (p.y < yMin) yMin = p.y;
            if (p.x > xMax) xMax = p.x;
            if (p.y > yMax) yMax = p.y;
        }

        /// <summary>네 장의 덮개를 구멍 바깥에 맞춰 놓는다. hole이 null이면 위쪽 한 장으로 화면
        /// 전체를 덮고 나머지는 접는다.</summary>
        void ApplySpotlight(Rect? hole)
        {
            var full = canvasRect.rect;   // 캔버스 로컬 좌표(가운데가 0)

            if (hole == null || hole.Value.width <= 0f)
            {
                Place(dimTop, full.xMin, full.yMin, full.xMax, full.yMax);
                Place(dimBottom, 0, 0, 0, 0);
                Place(dimLeft, 0, 0, 0, 0);
                Place(dimRight, 0, 0, 0, 0);
                PlaceSpeaker(bottom: true);
                return;
            }

            var h = ToCanvas(hole.Value);
            Place(dimTop, full.xMin, h.yMax, full.xMax, full.yMax);
            Place(dimBottom, full.xMin, full.yMin, full.xMax, h.yMin);
            Place(dimLeft, full.xMin, h.yMin, h.xMin, h.yMax);
            Place(dimRight, h.xMax, h.yMin, full.xMax, h.yMax);

            // 설명 자리가 아래쪽이면 정보원이 그 위를 가리지 않도록 위로 올라간다.
            PlaceSpeaker(bottom: h.center.y > full.center.y);
        }

        Rect ToCanvas(Rect screenRect)
        {
            // 이 캔버스가 Overlay면 카메라는 null이어야 하고, 카메라 모드면 그 카메라를 줘야 한다.
            var uiCam = canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenRect.min, uiCam, out var min);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenRect.max, uiCam, out var max);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        static void Place(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Mathf.Max(0f, xMax - xMin), Mathf.Max(0f, yMax - yMin));
            rt.anchoredPosition = new Vector2((xMin + xMax) * 0.5f, (yMin + yMax) * 0.5f);
        }

        /// <summary>정보원과 말풍선을 화면 아래/위로 옮긴다 - 설명하는 자리를 가리지 않게.</summary>
        void PlaceSpeaker(bool bottom)
        {
            float y = bottom ? 40f : -40f;
            var anchor = bottom ? new Vector2(0f, 0f) : new Vector2(0f, 1f);
            var pivot = bottom ? new Vector2(0f, 0f) : new Vector2(0f, 1f);

            informantRect.anchorMin = informantRect.anchorMax = anchor;
            informantRect.pivot = pivot;
            informantRect.anchoredPosition = new Vector2(70f, y);

            // 말풍선을 정보원 키의 한가운데에 맞춘다 - 위/아래 어느 쪽으로 붙어도 꼬리가 몸통을
            // 가리키게 된다.
            float centerOffset = (InformantSize - BubbleHeight) * 0.5f;
            bubbleRect.anchorMin = bubbleRect.anchorMax = anchor;
            bubbleRect.pivot = pivot;
            bubbleRect.anchoredPosition = new Vector2(BubbleLeft, bottom ? y + centerOffset : y - centerOffset);

            // 꼬리는 말풍선 왼쪽에서 정보원 쪽을 향한다.
            tailRect.anchoredPosition = new Vector2(-14f, 0f);
        }

        // ------------------------------------------------------------ 화면 만들기

        void BuildUI(Sprite informantSprite)
        {
            var go = new GameObject("PlayGuideCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 결과 리포트(HUD 캔버스 안)보다도 위에 와야 한다.
            canvas.sortingOrder = 5000;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasRect = (RectTransform)go.transform;
            group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;

            // 클릭을 삼키는 판 - 어디를 눌러도 다음으로 넘어간다. 맨 아래에 두어도 이 캔버스의
            // sortingOrder가 높아 아래쪽 HUD로 클릭이 새지 않는다.
            var blocker = NewImage("ClickCatcher", canvasRect, new Color(0, 0, 0, 0));
            Stretch(blocker.rectTransform);
            blocker.raycastTarget = true;
            var blockerButton = blocker.gameObject.AddComponent<Button>();
            blockerButton.transition = Selectable.Transition.None;
            blockerButton.onClick.AddListener(Next);

            dimTop = NewImage("DimTop", canvasRect, DimColor).rectTransform;
            dimBottom = NewImage("DimBottom", canvasRect, DimColor).rectTransform;
            dimLeft = NewImage("DimLeft", canvasRect, DimColor).rectTransform;
            dimRight = NewImage("DimRight", canvasRect, DimColor).rectTransform;

            var informant = NewImage("Informant", canvasRect, Color.white);
            informant.sprite = informantSprite;
            informant.preserveAspect = true;
            informant.enabled = informantSprite != null;
            informantRect = informant.rectTransform;
            informantRect.sizeDelta = new Vector2(InformantSize, InformantSize);

            var tail = NewImage("Tail", canvasRect, PaperColor);
            tailRect = tail.rectTransform;
            tailRect.sizeDelta = new Vector2(34f, 34f);
            tailRect.localRotation = Quaternion.Euler(0f, 0f, 45f);

            var bubble = NewImage("Bubble", canvasRect, PaperColor);
            bubbleRect = bubble.rectTransform;
            bubbleRect.sizeDelta = new Vector2(BubbleWidth, BubbleHeight);
            tailRect.SetParent(bubbleRect, false);
            tailRect.anchorMin = tailRect.anchorMax = new Vector2(0f, 0.5f);
            tailRect.pivot = new Vector2(0.5f, 0.5f);

            var header = NewImage("Header", bubbleRect, InkColor).rectTransform;
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, 52f);
            header.anchoredPosition = Vector2.zero;

            var name = NewText("Name", header, "정보원", 26, TextAlignmentOptions.Left);
            name.color = PaperColor;
            name.fontStyle = FontStyles.Bold;
            Inset(name.rectTransform, 24f, 0f, 24f, 0f);

            counterText = NewText("Counter", header, "", 22, TextAlignmentOptions.Right);
            counterText.color = new Color(0.72f, 0.70f, 0.67f, 1f);
            Inset(counterText.rectTransform, 24f, 0f, 24f, 0f);

            bodyText = NewText("Body", bubbleRect, "", 26, TextAlignmentOptions.TopLeft);
            bodyText.color = InkColor;
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            Inset(bodyText.rectTransform, 30f, 22f, 30f, 76f);

            var hint = NewText("Hint", bubbleRect, "화면을 클릭하면 다음", 18, TextAlignmentOptions.BottomLeft);
            hint.color = MutedInk;
            Inset(hint.rectTransform, 30f, 16f, 30f, 0f);

            var skip = NewText("Skip", bubbleRect, "건너뛰기", 20, TextAlignmentOptions.BottomRight);
            skip.color = LinkColor;
            skip.raycastTarget = true;
            Inset(skip.rectTransform, 30f, 16f, 30f, 0f);
            skipGo = skip.gameObject;
            var skipButton = skipGo.AddComponent<Button>();
            skipButton.transition = Selectable.Transition.None;
            skipButton.onClick.AddListener(Skip);

            go.SetActive(false);
        }

        Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        TMP_Text NewText(string name, Transform parent, string content, float size, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.text = content;
            text.fontSize = size;
            text.alignment = align;
            text.raycastTarget = false;
            Stretch(text.rectTransform);
            return text;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void Inset(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }
    }
}
