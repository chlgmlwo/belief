using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Belief.Core;
using Belief.Data;
using Belief.Domain;
using Belief.Events;
using Belief.Presentation.World;

namespace Belief.Presentation
{
    public enum TargetingPhase
    {
        Idle,
        AwaitingTarget,
        AwaitingConfirm
    }

    /// <summary>
    /// 카드 선택 -> 전달 대상(World 클릭) -> 정보원에게 전달 확정 흐름을 조율한다. 게임 로직은
    /// TurnSystem의 공개 메서드만 호출하고, 자체적으로 어떤 게임 상태도 바꾸지 않는다 -
    /// 순수 Presentation 조율자. 대상 선택 이후의 전달자 지정(메신저) 단계는 존재하지 않는다 -
    /// BELIEF에서 정보는 항상 정보원(Global System)을 통해서만 유통된다.
    /// </summary>
    public class TargetingController : MonoBehaviour
    {
        [SerializeField] GameInstaller installer;
        [SerializeField] WorldPresenter worldPresenter;

        public TargetingPhase Phase { get; private set; } = TargetingPhase.Idle;
        public event Action PhaseChanged;

        /// <summary>클릭이 거부되었을 때 이유를 알린다 - HUD가 구독해 잠깐 보여준다.
        /// 게임 상태는 바꾸지 않는다, 순수 알림.</summary>
        public event Action<string> InteractionRejected;

        /// <summary>튜토리얼처럼 특정 단계에서만 특정 행동을 허용해야 할 때 쓰는 선택적 필터 -
        /// 기본값 null이면 아무 제한도 없다(기존 동작과 완전히 동일). TutorialController가 활성화된
        /// 동안에만 설정되고, 종료되면 다시 null로 돌아가 원래의 자유로운 선택으로 복귀한다.
        /// CardSelectionAllowed는 TurnSystem.SelectCard를 직접 호출하는 HudPresenter.OnCardClicked가
        /// 참조한다(카드 선택 자체는 TargetingController가 아니라 TurnSystem이 소유하기 때문).</summary>
        public Func<InformationCardData, bool> CardSelectionAllowed;
        public Func<LocationData, bool> LocationSelectionAllowed;
        public Func<NpcData, bool> NpcSelectionAllowed;
        public Func<bool> DeliverAllowed;

        const string RestrictedMessage = "지금 단계에서는 다른 행동을 할 수 없습니다.";

        LocationData pendingLocation;
        NpcState pendingNpc;

        void Start()
        {
            installer.EventBus.Subscribe<CardSelectedEvent>(OnCardSelected);
            installer.EventBus.Subscribe<CardPlayedEvent>(_ => Reset());
            worldPresenter.LocationClicked += OnLocationClicked;
            worldPresenter.NpcClicked += OnNpcClicked;
        }

        void OnCardSelected(CardSelectedEvent e)
        {
            ClearPendingSelection();
            SetPhase(TargetingPhase.AwaitingTarget);
        }

        /// <summary>WorldPresenter.LocationClicked에서 호출된다. public인 이유: 다른 입력 소스
        /// (예: 튜토리얼 자동 재생)가 같은 진입점을 재사용할 수 있도록, 그리고 테스트 가능하도록.
        /// 전달이 실행되기 전까지는 몇 번을 다시 클릭하든 항상 그 장소로 대상을 (재)선택한다 -
        /// Phase로 첫 선택/재선택을 구분하지 않는다.</summary>
        public void OnLocationClicked(LocationData location)
        {
            if (LocationSelectionAllowed != null && !LocationSelectionAllowed(location))
            {
                InteractionRejected?.Invoke(RestrictedMessage);
                return;
            }

            // 카드를 안 고른 채 지도를 눌렀을 뿐이라 알릴 게 없다 - 예전엔 "먼저 카드를 선택하세요."를
            // 띄웠지만 손패가 이미 화면에 깔려 있고 시작 동작이 그것뿐이라 잔소리였다(3-85에서 삭제).
            // NPC 조사 클릭(HudPresenter.OnNpcClickedForProfile)은 이 경로와 별개로 계속 동작한다.
            var card = installer.Turns.SelectedCard;
            if (card == null) return;

            // 사람 대상 카드를 든 채로 장소를 눌렀을 뿐이다 - 예전엔 "이 카드는 사람을 대상으로
            // 전달해야 합니다"를 띄웠지만, 이제 그런 장소는 커서를 올려도 확대되지 않아
            // (WorldPresenter.RefreshTargetable) 대상이 아니라는 게 누르기 전에 이미 보인다.
            if (card.cardType != InfoCardType.Spread) return;

            // Location Mechanics V1(§7) - accessType이 제한된 장소는 "장소 전체" 직접 지정이 불가하다.
            // 여기서 먼저 걸러 확정 단계까지 가지 않게 한다 - 최종 강제는 TurnSystem.PlayCardOnLocationAsync가 담당.
            if (!installer.Turns.CanTargetLocationDirectly(location))
            {
                Debug.Log($"[Location Mechanics] Access blocked. LocationAccessType={location.accessType}, TargetMode=Location, AccessAllowed=False, BlockedReason=RestrictedLocationDirectTarget");
                InteractionRejected?.Invoke(installer.Turns.RestrictedLocationTargetMessage);
                return;
            }

            ReplacePendingTarget(location: location);
            SetPhase(TargetingPhase.AwaitingConfirm);
        }

        public void OnNpcClicked(NpcData npcData)
        {
            if (NpcSelectionAllowed != null && !NpcSelectionAllowed(npcData))
            {
                InteractionRejected?.Invoke(RestrictedMessage);
                return;
            }

            // 위 OnLocationClicked와 같은 이유로 삭제(3-85) - 카드 없이 NPC를 누르는 건 "조사"이지
            // 잘못된 조작이 아니다.
            var card = installer.Turns.SelectedCard;
            if (card == null) return;

            // 위 OnLocationClicked와 같은 이유로 경고를 없앴다 - 대상이 될 수 없는 사람은 커서를
            // 올려도 반응하지 않으므로 눌러 보고 나서 문구로 알 필요가 없다.
            if (card.cardType != InfoCardType.Deliver) return;

            if (!installer.Npcs.TryGetValue(npcData, out var npcState)) return;

            ReplacePendingTarget(npc: npcState);
            SetPhase(TargetingPhase.AwaitingConfirm);
        }

        /// <summary>전달이 실제로 진행 중인지 - LLM 판단을 기다리는 동안 참이다. Phase만으로는
        /// 이 구간을 알 수 없다(대기 중에도 AwaitingConfirm 그대로라 두 번째 확정 클릭이 통과했다).
        /// HUD가 이 값을 읽어 손패를 "사용 중"으로 잠그고, 끝나면 스스로 되돌린다.</summary>
        public bool IsDelivering { get; private set; }

        /// <summary>정보원에게 전달해 전달을 확정한다 - 대상 선택 후 반드시 거치는 유일한 확정 동작.
        /// 비동기(LLM 판단이 Timeout까지 대기할 수 있음) - Button.onClick은 async void를 그대로
        /// 받을 수 있다.
        ///
        /// <b>진행 중 재진입을 막는 것은 Phase가 아니라 <see cref="IsDelivering"/>다.</b> await 동안에도
        /// Phase는 AwaitingConfirm으로 남아 있어서, 그 사이에 확정 버튼을 다시 누르면 같은 카드가
        /// 두 번 전달됐다. 또 무슨 이유로 끝나든(정상/예외) finally에서 상태를 되돌리고 PhaseChanged를
        /// 쏘아, HUD가 잠가 둔 손패를 반드시 다시 열도록 한다.</summary>
        public async void DeliverByInformant()
        {
            if (IsDelivering) return;
            if (Phase != TargetingPhase.AwaitingConfirm) return;
            if (DeliverAllowed != null && !DeliverAllowed())
            {
                InteractionRejected?.Invoke(RestrictedMessage);
                return;
            }

            IsDelivering = true;
            PhaseChanged?.Invoke();
            try
            {
                await Execute();
            }
            finally
            {
                IsDelivering = false;
                PhaseChanged?.Invoke();
            }
        }

        async Task Execute()
        {
            if (pendingLocation != null) await installer.Turns.PlayCardOnLocationAsync(pendingLocation);
            else if (pendingNpc != null) await installer.Turns.PlayCardOnNpcAsync(pendingNpc);
            Reset();
        }

        void Reset()
        {
            ClearPendingSelection();
            SetPhase(TargetingPhase.Idle);
        }

        void ClearPendingSelection()
        {
            if (pendingLocation != null) worldPresenter.SetLocationSelected(pendingLocation, false);
            if (pendingNpc != null) worldPresenter.SetNpcSelected(pendingNpc.Data, false);
            pendingLocation = null;
            pendingNpc = null;
        }

        /// <summary>대상 선택(장소 또는 사람)을 교체한다 - 기존에 선택돼 있던 대상(장소든 사람이든)의
        /// Highlight를 먼저 해제한 뒤 새 대상만 강조한다. 전달 실행 전까지 몇 번이든 호출될 수 있다.</summary>
        void ReplacePendingTarget(LocationData location = null, NpcState npc = null)
        {
            ClearPendingSelection();
            pendingLocation = location;
            pendingNpc = npc;
            if (location != null) worldPresenter.SetLocationSelected(location, true);
            if (npc != null) worldPresenter.SetNpcSelected(npc.Data, true);
        }

        void SetPhase(TargetingPhase phase)
        {
            Phase = phase;
            PhaseChanged?.Invoke();
        }
    }
}
