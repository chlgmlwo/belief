using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Belief.Data;
using Belief.Presentation.HUD;

namespace Belief.Presentation.Mockup
{
    /// <summary>화면 밖 숨김 CardTileView(프록시)와 화면에 보이는 HandCardMockupView 사이의 어댑터 -
    /// HandCard1~4에 하나씩 붙는다. 실제 카드 데이터/선택 로직은 프록시와 기존 HudPresenter가
    /// 그대로 소유하고(수정 없음), 여기서는 그 결과를 목업 카드 텍스트/시각 상태에 반영하고
    /// 클릭만 프록시로 중계한다.
    ///
    /// backendSelected(실제 선택 카드인지)와 visuallyExpanded(목업 카드가 실제로 올라와 있는지)를
    /// 분리해서 관리한다 - 같은 카드를 다시 눌러 접는 것은 시각 효과일 뿐 TurnSystem.SelectedCard를
    /// 바꾸지 않으므로(HudPresenter.OnCardClicked 참고), 이후 다른 이유로 RefreshAll이 다시 돌아도
    /// (여전히 backendSelected=true인 채) 자동으로 다시 펼쳐지면 안 된다.</summary>
    public class MockupCardTileAdapter : MonoBehaviour
    {
        TMP_Text titleText, securityText, chianText, descriptionText, typeValueText, targetValueText;
        (Image chip, TMP_Text label) chip1, chip2, chip3;

        HandCardMockupView mockupView;
        CardTileView currentProxy;
        Button proxyButton;

        InformationCardData lastBoundCard;
        CardHandState lastRawHandState = CardHandState.Collapsed;
        bool userCollapsedWhileSelected;

        public HandCardMockupView MockupView => mockupView;

        /// <summary>이 슬롯이 지금 보여주고 있는 카드 - 손패에서 빠진 카드가 어느 슬롯에 있었는지
        /// 찾아 소멸 연출을 재생하려고 브리지가 읽는다.</summary>
        public InformationCardData BoundCard => lastBoundCard;

        void Awake()
        {
            mockupView = GetComponent<HandCardMockupView>();
            mockupView.Clicked += OnMockupClicked;

            var texts = transform.Find("Texts");
            if (texts != null)
            {
                titleText = texts.Find("Title")?.GetComponent<TMP_Text>();
                securityText = texts.Find("Security")?.GetComponent<TMP_Text>();
                chianText = texts.Find("Chian")?.GetComponent<TMP_Text>();
                descriptionText = texts.Find("Description")?.GetComponent<TMP_Text>();
                typeValueText = texts.Find("TypeValue")?.GetComponent<TMP_Text>();
                targetValueText = texts.Find("TargetValue")?.GetComponent<TMP_Text>();
                chip1 = FindChip(texts, "Chip1");
                chip2 = FindChip(texts, "Chip2");
                chip3 = FindChip(texts, "Chip3");
            }
        }

        static (Image, TMP_Text) FindChip(Transform texts, string name)
        {
            var chip = texts.Find(name);
            if (chip == null) return (null, null);
            return (chip.GetComponent<Image>(), chip.Find("Label")?.GetComponent<TMP_Text>());
        }

        void OnDestroy()
        {
            if (mockupView != null) mockupView.Clicked -= OnMockupClicked;
        }

        void OnMockupClicked(HandCardMockupView view)
        {
            // 실제 클릭은 항상 여기 하나로만 들어온다 - 프록시의 Button.onClick을 프로그램적으로
            // 호출해 기존 CardTileView.Clicked -> HudPresenter.OnCardClicked 경로를 그대로 태운다.
            // (blocksRaycasts=false라 실제 포인터 클릭으로는 프록시 Button이 절대 눌리지 않는다 -
            // 이 호출이 유일한 트리거다.)
            if (proxyButton != null) proxyButton.onClick.Invoke();
        }

        /// <summary>매 프레임 브리지가 호출한다 - 이번 프레임 이 슬롯이 가리켜야 할 프록시를 확정한다
        /// (손패 순서/구성이 바뀌면 다른 프록시 인스턴스로 재연결될 수 있다).</summary>
        public void AssignProxy(CardTileView proxy)
        {
            if (!ReferenceEquals(proxy, currentProxy))
            {
                currentProxy = proxy;
                proxyButton = proxy != null ? proxy.GetComponent<Button>() : null;
            }

            var card = currentProxy != null ? currentProxy.BoundCard : null;
            if (!ReferenceEquals(card, lastBoundCard))
            {
                // 슬롯에 새 카드가 들어왔다(소비/재배치) - 이전 카드의 "접어둔" 기록은 더 이상 의미 없다.
                userCollapsedWhileSelected = false;
                lastBoundCard = card;
                lastRawHandState = currentProxy != null ? currentProxy.HandState : CardHandState.Collapsed;
            }

            if (card != null) BindTexts(card);
        }

        /// <summary>categoryId(영문 enum 문자열) -> 카드 아트의 "CODE" 칸에 쓸 한글 표기.</summary>
        static string CategoryKorean(string categoryId) => categoryId switch
        {
            "CRIME" => "범죄",
            "ADMIN" => "행정",
            "DISASTER" => "재난",
            "ECONOMY" => "경제",
            "MILITARY" => "군사",
            "NOBILITY" => "귀족",
            "POLITICS" => "정치",
            "PUBLIC" => "공공",
            "RELIGION" => "종교",
            "SECURITY" => "치안",
            _ => categoryId
        };

        void BindTexts(InformationCardData card)
        {
            var info = card.information;
            string categoryId = info != null ? info.categoryId : "?";
            string kind = card.cardType == InfoCardType.Spread ? "SPREAD" : "DELIVER";
            string targetLabel = card.TargetType == InformationTargetType.Place ? "PLACE" : "PEOPLE";
            string sourceName = card.source != null ? card.source.displayName : "?";

            if (titleText != null) titleText.text = info != null ? info.title : "?";
            if (descriptionText != null) descriptionText.text = info != null ? info.description : "";
            // 제목 위 헤더 - 카테고리/출처는 좌우로 맞바꿈. TYPE/TARGET은 라벨 없이 값만
            // 영문으로 표시(SPREAD/DELIVER, PLACE/PEOPLE).
            if (securityText != null) securityText.text = sourceName;
            if (typeValueText != null) typeValueText.text = kind;
            if (targetValueText != null) targetValueText.text = targetLabel;
            if (chianText != null) chianText.text = CategoryKorean(categoryId);

            var tags = info != null ? info.tags : null;
            BindChip(chip1, tags, 0);
            BindChip(chip2, tags, 1);
            BindChip(chip3, tags, 2);
        }

        static void BindChip((Image chip, TMP_Text label) slot, string[] tags, int index)
        {
            if (slot.chip == null) return;
            bool has = tags != null && index < tags.Length && !string.IsNullOrEmpty(tags[index]);
            slot.chip.gameObject.SetActive(has);
            if (has && slot.label != null) slot.label.text = tags[index];
        }

        /// <summary>이번 프레임 이 슬롯이 "펼쳐져 보여야" 하는지 계산한다. backendSelected는 실제
        /// 선택 카드 여부, userCollapsedWhileSelected는 같은 카드를 다시 눌러 접어둔 상태를 뜻한다 -
        /// 이 값은 카드가 바뀌거나(위 AssignProxy) 다른 카드가 선택될 때만 초기화된다.</summary>
        public bool ComputeDesiredExpanded(InformationCardData selectedCard)
        {
            if (currentProxy == null || lastBoundCard == null) return false;

            bool backendSelected = ReferenceEquals(lastBoundCard, selectedCard);
            var rawState = currentProxy.HandState;

            if (backendSelected)
            {
                // 여전히 선택된 채로 Expanded -> Collapsed로 바뀌는 전이는 "같은 카드 재클릭으로
                // 접기"(OnCardClicked의 시각 전용 분기) 외에는 발생하지 않는다.
                if (lastRawHandState == CardHandState.Expanded && rawState == CardHandState.Collapsed)
                    userCollapsedWhileSelected = true;
            }
            else
            {
                // 더 이상 선택된 카드가 아니면 접어둔 기록도 의미가 없다 - 나중에 다시 선택되면
                // (재선택은 항상 새로운 선택 동작이므로) 펼쳐진 채로 시작해야 한다.
                userCollapsedWhileSelected = false;
            }

            lastRawHandState = rawState;
            return backendSelected && !userCollapsedWhileSelected;
        }
    }
}
