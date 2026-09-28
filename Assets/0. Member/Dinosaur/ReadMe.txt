Dinosaur 작업 설명 및 사용법

- 작성일: 2026-09-28
- 환경: Unity 2022.3.58f1 / C# / Photon Fusion 2 Host Mode / Photon Voice
- 기준 구현: b6447fc — [WIP] 무기 공격 및 사망 관전 연결
- 이 문서와 ReadMe.txt는 같은 내용을 담는다. 이후 기능이나 설정을 바꾸면 함께 갱신한다.
- 아래 에셋 경로는 프로젝트 루트 기준이다.

1. 현재 상태 — 먼저 확인

일반 공격·즉사·사망·관전의 코드 연결은 반영되어 있다. 다만 실제 무기 데이터와 즉사 입력키는 미설정이므로, 현재 저장된 프로젝트에서 무기를 줍는 것만으로 공격이 활성화되지는 않는다.

- 칼·가위·드라이버 데이터에 isWeapon, attackDamage, attackInterval 값이 아직 저장되지 않았다. 현재 기본값은 각각 false, 0, 0이다.
- KillManager.instantKillKey는 현재 None이다.
- GamePlay에 살인마 개인 미션이 배정되지 않으므로 즉사 기술은 잠겨 있다. 키만 지정해도 해금되지 않는다.
- 일반 공격은 개인 미션 완료를 요구하지 않는다. 살인마 역할과 유효한 장착 무기 설정이 필요하다.
- 제안했던 공통 피해 25 / 공격 간격 0.5초 / 즉사 Q키는 확정된 게임 설정이 아니다. 기존 즉사 쿨타임 기본값은 15초다.
- 기존 캐릭터는 사망 애니메이션의 마지막 자세로 남는다. 별도 시체 프리팹과 증거 생성 기능은 구현하지 않았다. 최종 시체·증거 처리 방식은 결정 대기 중이다.

문서 추가 및 main 병합은 위 전투 설정을 확정하거나 활성화하는 작업을 포함하지 않는다.

2. 이번 작업에서 바뀐 동작

공격 판정

- 일반 공격과 즉사 기술의 요청 경로를 분리했다.
- 호스트가 살인마 역할, 생존·탈출·게임 종료 상태, 공격 간격을 검사한다.
- 일반 공격은 실제로 해당 플레이어가 보유한 아이템인지 확인하고, ItemData에 설정된 피해량을 적용한다.
- 호스트가 처리한 카메라 위치와 시점으로 대상을 다시 찾는다. 사거리 밖, 시선 뒤쪽, 벽에 가려진 대상은 맞지 않는다.
- 공격 범위 기본값은 Unity 거리 단위로 2다. 카메라 기준 광선 검사와 플레이어 간 거리 검사를 모두 통과해야 한다.
- 일반 공격이 빗나가도 공격 모션과 공격 간격은 적용된다.
- 즉사는 배정된 개인 미션이 하나 이상 있고 모두 완료되었을 때 사용할 수 있다. 미배정은 완료로 취급하지 않는다.
- 현재 즉사 판정에는 무기 장착 필수 조건이 없다. 개인 미션 해금, 생존 상태, 시선·거리, 쿨타임을 검사한다.
- 즉사와 일반 공격에는 공통 공격 간격 제한이 적용되며, 즉사는 별도의 쿨타임도 사용한다.

애니메이션·사망·관전

- Animator를 공용 플레이어의 모델 자식 Player에 연결하여 애니메이션 클립의 뼈 경로를 맞췄다.
- 기존 이동 상태를 유지하며 Attack, Dead 상태와 Attack, AttackSpeed, IsDead 파라미터를 연결했다.
- 호스트가 승인한 공격을 각 클라이언트의 캐릭터 Animator에서 재생한다.
- 사망하면 이동 및 SimpleKCC 충돌을 차단하고, 진행 중인 로컬 상호작용과 아이템 표시를 정리한다.
- 아이템 드롭은 기존 PlayerItemController의 사망 이벤트 처리를 재사용한다.
- 피격되거나 공격 모션을 재생하면 진행 중인 로컬 홀드·드래그 상호작용을 취소한다.
- 무기를 든 살인마의 좌클릭이 공격과 일반 클릭 상호작용을 동시에 실행하지 않도록 분리했다.
- 사망 후 기본 5초가 지나면 살아 있고 탈출하지 않은 플레이어를 관전한다.
- 관전 대상의 사망·탈출·퇴장 시 대상을 다시 선택하며, 대상이 없으면 현재 시점을 유지한다.

3. 실행 순서와 조작

방 참가와 게임 진행

1. 동일한 프로젝트 버전으로 두 실행 환경을 준비한다. 예: Unity Editor 1개와 실행 빌드 1개.
2. Assets/Scenes/Lobby.unity에서 한 명이 방을 만들고 다른 한 명이 같은 방에 참가한다.
3. StandBy에서 이동·시점 회전·ESC 플레이어 목록과 방장 표시를 확인한다.
4. 준비 조건을 충족한 뒤 방장이 게임 시작을 요청하여 GamePlay로 이동한다.
5. 결과 화면에서는 방장이 대기실 복귀 버튼을 누른다. 연결된 참가자들이 함께 StandBy로 이동한다.
6. 방장이 나가 세션이 종료되면 연결 종료 안내의 로비 이동 버튼을 사용한다. 게임 도중 일반 참가자가 나가면 해당 참가자는 패배 처리되고 남은 인원으로 승리 조건을 다시 판단한다.

현재 Build Settings의 활성 씬은 Main, Lobby, StandBy, GamePlay다. 실제 사용 씬 이름은 StandBy이며 StandBy_Test가 아니다. 씬 이름·경로를 바꾸면 NetworkBootstrap의 직렬화 값과 Build Settings도 함께 확인해야 한다.

기본 조작

- WASD 또는 방향키: 이동.
- 마우스 이동: 시점 회전.
- Space: 점프.
- Left Shift: 이동 중 달리기. 스태미너가 필요하다.
- Left Control: 현재 이동 코드에서는 저속 이동에 사용된다. 별도 앉기 자세나 충돌체 높이 변경을 의미하지 않는다.
- 빈손으로 아이템을 보며 좌클릭: 아이템 줍기.
- G: 들고 있는 아이템 내려놓기.
- 아이템을 들고 우클릭: 해당 아이템이 제공하는 사용 기능 실행. 모든 아이템에 사용 기능이 있는 것은 아니다.
- F 누르기/떼기: 홀드 방식 미션 상호작용 시작/종료.
- ESC: 방 상세 UI 열기/닫기.
- V: 자신의 음성 송신 음소거 전환. 상대방 소리 수신은 유지한다.
- 유효한 무기를 든 살인마의 좌클릭: 일반 공격. 현재 에셋은 무기 설정 전이므로 바로 사용할 수 없다.
- 즉사 기술: Inspector에 지정한 키. 현재는 None이며 Q는 제안 상태다.
- 관전 중 좌클릭/우클릭: 다음/이전 대상. 커서가 잠겨 있고 메뉴가 닫혀 있을 때 처리한다.

기존 연결 기능

- 공용 플레이어의 체력 기본값은 100이며, 호스트가 피해·사망 상태를 결정한다.
- 스태미너 기본값은 최대 100, 달리기 소모 초당 20, 회복 초당 15, 회복 대기 1.5초다. Inspector 저장값이 있으면 그 값이 우선한다.
- 들고 있는 아이템은 본인에게 1인칭 모델, 다른 참가자에게 손 위치를 따라가는 월드 모델로 표시된다.
- 현재 공격 모션 연결은 캐릭터 Animator 기준이다. 1인칭 전용 손·쥐기·무기 휘두르기 애니메이션은 이번 작업에서 추가하지 않았다.
- 음성은 생존자 송수신 그룹과 사망자 송수신 그룹을 구분한다. 사망자는 관전자 그룹으로 송신하며 생존자·관전자 그룹을 듣는다. 탈출자는 송신이 차단된다.

4. 전투를 활성화할 때 필요한 Inspector 설정

아래 항목은 설정 방법이다. 문서 작성 과정에서 실제 값을 변경하지 않았다.

일반 공격용 아이템

대상 데이터:

- Assets/ScriptableObjects/Data/Item/KnifeData.asset
- Assets/ScriptableObjects/Data/Item/ScissorsData.asset
- Assets/ScriptableObjects/Data/Item/ScrewdriverData.asset

무기로 사용할 데이터에서 다음을 설정한다.

1. Is Weapon (isWeapon): 체크.
2. Attack Damage (attackDamage): 확정된 양수 피해량.
3. Attack Interval (attackInterval): 확정된 양수 공격 간격(초).
4. 기존 아이템 ID와 First Person Prefab 참조를 유지한다.

세 값 중 하나라도 유효하지 않으면 무기로 판정되지 않는다. 25 피해·0.5초 간격은 이전 자동 검사의 임시 런타임 설정이었으며, 세 데이터 에셋의 현재 저장값이 아니다.

플레이어와 즉사 기술

실제 게임용 공용 프리팹은 Assets/Prefabs/Player/Player.prefab이다. 개인 폴더의 Assets/0. Member/Dinosaur/Prefab/Player.prefab과 혼동하지 않는다.

- 루트의 KillManager: Kill Range 기본 2, Kill Cooldown Seconds 기본 15, Instant Kill Key 현재 None.
- 루트의 SpectatorManager: Spectator Delay Seconds 기본 5.
- 루트의 PlayerAnimation: Animator는 자식 Player의 Animator, Attack Clip은 기존 공격 클립을 참조한다. 이번 작업에서 연결되어 있다.
- Animator Controller: Assets/0. Member/Hyunwoo/HW_Anim/Player.controller.
- 사용 클립: Assets/0. Member/Taewoo/Anim_Use/ANM_Player_Attack.anim, Assets/0. Member/Taewoo/Anim_Use/ANM_Player_Die.anim.

살인마 미션을 추가할 때는 Mission/Mission Data로 데이터를 만들고, Role Target을 Killer, Mission Type을 Personal로 지정한다. 고유 ID와 완료 횟수를 정한 뒤 GamePlay의 MissionSystem 미션 풀과 살인마 개인 미션 수를 확인한다. 실제 미니게임의 완료 요청도 MissionSystem으로 연결되어야 한다. 데이터만 만들거나 키만 지정해도 미션이 완료되는 것은 아니다.

배정된 살인마 개인 미션이 없는 상태에서는 즉사 잠금을 유지한다. 일반 공격은 해당 미션의 추가 여부와 무관하게 무기 설정을 통해 사용할 수 있다.

5. 변경 파일과 역할

최근 전투 작업은 아래 13개 파일에 반영되었다.

- Assets/Scripts/KillManager.cs: 일반 공격·즉사 요청, 호스트 판정, 공격 모션 동기화.
- Assets/Scripts/Item/ItemData.cs: 아이템별 무기 여부·피해량·공격 간격 설정.
- Assets/Scripts/Player/PlayerItemController.cs: 현재 실제 보유 중인 유효한 무기 조회.
- Assets/0. Member/Dinosaur/Scripts/Player/Playercameracontroller.cs: 호스트 조준 광선과 관전 시점 처리.
- Assets/0. Member/Dinosaur/Scripts/Player/Playerhealth.cs: 피해 상태를 통한 로컬 상호작용 취소 알림.
- Assets/Scripts/Player/Detector/PlayerInteraction.cs: 공격 입력 분리, 피격·공격·사망 시 상호작용 취소.
- Assets/Scripts/Player/Detector/PlayerTargetDetector.cs: 사망·탈출·게임 종료 후 상호작용 대상과 안내 제거.
- Assets/Scripts/Player/Animation/PlayerAnimation.cs: 공격 속도와 사망 Animator 상태 반영.
- Assets/Scripts/Player/Movement/PlayerMovement.cs: 사망 시 이동·충돌 차단.
- Assets/Scripts/DeathManager.cs: 사망 후 로컬 입력 상태 정리와 관전 전환 연결.
- Assets/Scripts/SpectatorManager.cs: 관전 전환과 대상 변경 입력·유효성 검사.
- Assets/Prefabs/Player/Player.prefab: Animator 위치와 공격 클립 참조 연결.
- Assets/0. Member/Hyunwoo/HW_Anim/Player.controller: 공격·사망 상태와 전환 조건.

기존 방 참가·씬 전환은 Dinosaur/Scripts/Networking의 NetworkBootstrap과 PlayerSpawner, 음성·스태미너는 Dinosaur/Scripts/Player의 기존 컴포넌트를 사용한다. 이번 문서 추가에서는 이 기능들을 수정하지 않았다.

6. 검증 결과와 직접 확인 순서

기존 구현 커밋에서 수행한 검사

- Unity 2022.3.58f1 컴파일과 Fusion 코드 생성 통과.
- 임시 비공개 Host/Client 2인 세션에서 자동 검사 47개 통과.
- 무기 미장착·역할 불일치·사거리 초과·시선 뒤쪽·벽 가림·공격 간격 검사.
- 일반 피해와 체력 동기화, 미션 미배정 즉사 잠금, 임시 미션 완료 후 즉사 해금.
- Host와 Client의 공격 역할을 바꿔 칼·가위·드라이버 일반 피해와 원격 참가자 사망 확인.
- 사망 시 아이템 드롭·KCC 충돌 비활성화·사망 Animator 상태·관전 대상 동기화·승패 판정 확인.
- 사망 시 관전자 음성 상태를 선택하는 코드 경로 확인. 실제 마이크 송수신 검사는 포함하지 않았다.

위 검사는 임시 무기 수치와 임시 완료 미션을 사용한 코드 검증이다. 저장된 씬을 그대로 실행해 모든 공격이 활성화된다는 의미가 아니다. 임시 검사 코드는 작업 후 프로젝트에서 제거했으며 저장소에 상시 테스트로 추가하지 않았다.

이번 문서 작업에서는 문서 내용·참조 경로·두 형식의 일치 여부와 Git 변경 범위를 확인했다. 전투 실행 검사는 다시 수행하지 않았다.

수동 플레이 확인 — 전투 설정 완료 후

1. 두 실행 환경이 같은 버전인지 확인하고 같은 방에 참가한다. Host와 Client 양쪽이 살인마가 되는 경우를 각각 확인한다.
2. 살인마가 아이템을 주웠을 때 본인의 1인칭 표시와 상대방의 손 장착 표시를 확인한다. G로 내려놓은 뒤 다시 줍는다.
3. 무기 설정이 완료된 아이템으로 시민을 정면·사거리 안에서 공격한다. 예를 들어 피해 25, 체력 100으로 설정했다면 정상적인 네 번의 피해로 사망해야 한다.
4. 벽 뒤·사거리 밖·뒤쪽 대상, 빈손, 공격 간격 이내의 중복 요청에는 피해가 적용되지 않는지 확인한다.
5. 살인마 미션이 없으면 즉사 키를 지정해도 잠겨 있어야 한다. 실제 미션을 추가했다면 배정·완료 이후에만 즉사가 가능한지 확인한다.
6. 사망자의 아이템 드롭, 상호작용 안내 제거, 사망 자세, 이동·충돌 차단을 확인한다.
7. 살인마 1명·시민 1명인 2인 게임에서는 시민 사망으로 즉시 결과 화면이 열린다. 게임 진행 중 여러 관전 대상의 좌우 전환을 확인하려면 최소 3명(살인마 1명·시민 2명)으로 시민 1명이 사망한 상황을 검사한다. 관전 전환은 기본 5초 후이며 결과 화면이나 메뉴가 열려 있으면 마우스 전환 입력은 차단된다.
8. 생존자와 사망자의 실제 음성 송수신, V 음소거, 게임 결과, 방장의 StandBy 복귀 버튼을 확인한다.

아직 남은 항목

- 칼·가위·드라이버의 최종 무기 지정·피해량·공격 간격 및 즉사 입력키 확정·저장.
- 살인마 개인 미션 데이터와 실제 완료 동작 추가.
- 별도 시체·증거의 생성 규칙과 에셋 결정. 현재는 기존 캐릭터의 사망 자세가 남는 방식이다.
- 실제 화면에서 공격·사망 모션과 손 위치, 1인칭 연출을 육안 확인.
- 현재 전투 버전에서 실제 음성 송수신을 포함한 수동 다인 플레이 확인.

7. 문서와 Git 관리

- 개인 작업 브랜치: Kwonjun.
- 설명을 갱신할 때 ReadMe.md와 ReadMe.txt를 같은 내용으로 유지한다.
- 문서의 .meta도 함께 버전 관리하고 기존 에셋의 GUID는 유지한다.
- 전투 설정을 확정한 뒤에는 데이터 에셋과 필요한 프리팹 값을 저장하고, 두 문서의 현재 상태·사용법·검증 결과를 함께 갱신한다.
