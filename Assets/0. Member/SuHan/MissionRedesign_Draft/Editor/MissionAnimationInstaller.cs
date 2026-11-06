using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// [역할 · 에디터 전용, 1클릭 도구] 플레이어 애니메이터(Player.controller)에 미션 상호작용 레이어를 더한다 (14b 명세 IA7 · IA11 · IA12).
///  레이어 `Mission Interaction` (덮어쓰기 · 상체 마스크 · 기본 무게 0) 안에 상태 3개: Empty(기본) · Mission Press(버튼 클립 한 번) · Mission Turn(밸브 클립 반복).
///  파라미터 MissionPress(Trigger — 버튼 한 번) · MissionTurning(Bool — 켜져 있는 동안 반복). 무게는 PlayerAnimation 이 동작 중에만 1 로 올린다.
/// [이유] 애니메이터는 팀원 에셋이라 손으로 클릭해서 고치는 대신 메뉴로 더한다 — 결과가 늘 같고, 팀원이 컨트롤러를 새로 바꿔도 메뉴 한 번으로 다시 넣는다.
///  레이어가 이미 있으면 건너뛴다 (다시 실행해도 안전). 클립 · 마스크는 애니메이션 담당 에셋을 그대로 참조만 한다 (수정하지 않음).
/// [Write Defaults 끔] 빈 상태(Empty)가 기본값을 써 넣으면 레이어 무게가 줄어드는 0.1초 동안 팔이 기본 자세로 튈 수 있어 세 상태 모두 끈다.
/// [근거] 14b 명세 IA1 (사용자 결정 B — 팀원 파일 직접 수정, 이번만), IA3 (누르고 있는 동안 반복), IA7 · IA8 (상체 마스크), IA11 (이름 · 전이), IA12
/// </summary>
public static class MissionAnimationInstaller
{
    private const string ControllerPath = "Assets/0. Member/Hyunwoo/HW_Anim/Player.controller";
    private const string PressClipPath = "Assets/0. Member/Taewoo/Anim_Use/ANM_Player_Interaction_button.anim";
    private const string TurnClipPath = "Assets/0. Member/Taewoo/Anim_Use/ANM_Player_Interaction_velve.anim";
    private const string MaskPath = "Assets/0. Member/Taewoo/Anim_avata_mask/Upper_body_mask.mask";

    public const string LayerName = "Mission Interaction";
    public const string PressParameter = "MissionPress";
    public const string TurningParameter = "MissionTurning";

    // 전이 시간 · 종료 시점 (IA11): 버튼은 끝 직전(0.95)에 빈 상태로, 돌리기는 끝(0.98)에서 같은 상태로 다시 = 반복, 끄면 끝을 기다리지 않음
    private const float EnterSeconds = 0.1f;
    private const float PressExitTime = 0.95f;
    private const float PressExitSeconds = 0.1f;
    private const float TurnLoopExitTime = 0.98f;
    private const float TurnLoopSeconds = 0.05f;
    private const float TurnStopSeconds = 0.15f;

    [MenuItem("SuHan/Add Mission Interaction Animations")]
    public static void Apply()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimationClip pressClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(PressClipPath);
        AnimationClip turnClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(TurnClipPath);
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);

        if (controller == null || pressClip == null || turnClip == null || mask == null)
        {
            Debug.LogError($"[MissionAnimationInstaller] 에셋을 찾지 못했습니다 — 애니메이터 {(controller != null ? "있음" : "없음")} ({ControllerPath}), " +
                           $"버튼 클립 {(pressClip != null ? "있음" : "없음")}, 밸브 클립 {(turnClip != null ? "있음" : "없음")}, 마스크 {(mask != null ? "있음" : "없음")}");
            return;
        }

        if (controller.layers.Any(layer => layer.name == LayerName))
        {
            Debug.Log($"[MissionAnimationInstaller] {LayerName} 레이어가 이미 있어 건너뜁니다 ({ControllerPath}).");
            return;
        }

        AddParameterIfMissing(controller, PressParameter, AnimatorControllerParameterType.Trigger);
        AddParameterIfMissing(controller, TurningParameter, AnimatorControllerParameterType.Bool);

        // 상태 머신은 컨트롤러 파일 안에 함께 저장한다 (Base Layer 와 같은 방식)
        AnimatorStateMachine machine = new AnimatorStateMachine { name = LayerName, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(machine, controller);

        AnimatorState empty = machine.AddState("Empty", new Vector3(300f, 0f, 0f));
        AnimatorState press = machine.AddState("Mission Press", new Vector3(300f, 120f, 0f));
        AnimatorState turn = machine.AddState("Mission Turn", new Vector3(300f, -120f, 0f));
        empty.writeDefaultValues = false;
        press.writeDefaultValues = false;
        turn.writeDefaultValues = false;
        press.motion = pressClip;
        turn.motion = turnClip;
        machine.defaultState = empty;

        AddTransition(empty, press, false, 0f, EnterSeconds, AnimatorConditionMode.If, PressParameter);
        AddTransition(press, empty, true, PressExitTime, PressExitSeconds, null, null);
        AddTransition(empty, turn, false, 0f, EnterSeconds, AnimatorConditionMode.If, TurningParameter);
        AddTransition(turn, turn, true, TurnLoopExitTime, TurnLoopSeconds, AnimatorConditionMode.If, TurningParameter);
        AddTransition(turn, empty, false, 0f, TurnStopSeconds, AnimatorConditionMode.IfNot, TurningParameter);

        // 맨 아래(가장 마지막 = 가장 우선) 덮어쓰기. 무게는 동작 중에만 PlayerAnimation 이 올린다 (평소 0 → 지금 애니메이션 그대로)
        controller.AddLayer(new AnimatorControllerLayer
        {
            name = LayerName,
            stateMachine = machine,
            avatarMask = mask,
            blendingMode = AnimatorLayerBlendingMode.Override,
            defaultWeight = 0f,
        });

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log($"[MissionAnimationInstaller] {LayerName} 레이어 추가 — 버튼 {pressClip.length:0.00}초 · 밸브 {turnClip.length:0.00}초(누르고 있는 동안 반복), " +
                  $"마스크 {mask.name}, 파라미터 {PressParameter} · {TurningParameter} ({ControllerPath}). 팀원 에셋이 바뀌었으니 커밋 전에 알려 주세요.");
    }

    private static void AddParameterIfMissing(AnimatorController controller, string parameterName, AnimatorControllerParameterType type)
    {
        if (controller.parameters.Any(parameter => parameter.name == parameterName))
            return;

        controller.AddParameter(parameterName, type);
    }

    /// <param name="conditionParameter">null 이면 조건 없음 (끝나면 넘어감)</param>
    private static void AddTransition(AnimatorState from, AnimatorState to, bool hasExitTime, float exitTime, float seconds,
        AnimatorConditionMode? mode, string conditionParameter)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = hasExitTime;
        transition.exitTime = exitTime;
        transition.hasFixedDuration = true;
        transition.duration = seconds;

        if (mode.HasValue && conditionParameter != null)
            transition.AddCondition(mode.Value, 0f, conditionParameter);
    }
}
