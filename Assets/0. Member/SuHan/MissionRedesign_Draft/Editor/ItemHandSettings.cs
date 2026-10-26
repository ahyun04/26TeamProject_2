using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>
/// [역할 · 에디터 전용] 변환 도구가 아이템 프리팹을 다시 만들 때 팀원이 맞춘 "손 설정"을 지킨다 (11a 명세 BC13 ~ BC15).
///  Capture: 다시 만들기 전, 그 경로의 기존 프리팹에서 루트 아이템 표시(ItemWorldView)의 손 기준 위치 · 회전과
///   (1인칭이면) 자식 `Model (모델)` 의 위치 · 회전 · 크기를 읽는다. 기존 프리팹이 없거나 아이템 표시가 없으면 null (= 도구 기본값).
///  Apply: 다시 만든 복사본에 되돌린다. 1인칭 루트에 아이템 표시가 없으면 붙인다 (그리는 대상 = 새 모델 렌더러, 충돌 없음).
///   월드 아이템은 손 기준 값만 — 모델 위치는 도구가 정한다 (바닥 맞추기 등 내 변경이 막히지 않게).
/// [이유] 변환 도구는 아이템을 매번 처음부터 만든다. 팀원이 1인칭 팔 기능(61fb143 · 6e4dc1d · bbc6aba)에 맞춰 넣은 손 따라가기와
///  손 위치가 메뉴를 돌릴 때마다 지워져(34d3496 · bc325be) 본인 화면에 든 아이템이 안 보였다 (verification.md 4장 30번).
///  11a 는 장치 몸체 때문에 이 메뉴를 다시 돌려야 해서 함께 막는다. 손 설정은 화면을 보며 맞춘 값이라 기존 값이 우선이다 (BC14).
/// [1인칭 판별] 네트워크 오브젝트가 없는 프리팹 = 1인칭 모델 (월드 아이템은 NetworkObject 가 있다).
/// [근거] 11a 명세 BC4 (접근 1), BC13 ~ BC15
/// </summary>
public sealed class ItemHandSettings
{
    private const string ModelChildName = "Model (모델)";

    private Vector3 heldPosition;
    private Vector3 heldRotation;
    private bool hasModel;
    private Vector3 modelPosition;
    private Quaternion modelRotation;
    private Vector3 modelScale;

    /// <summary>기존 프리팹의 손 설정. 기존 프리팹이 없거나 루트에 아이템 표시가 없으면 null.</summary>
    public static ItemHandSettings Capture(string prefabPath)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        ItemWorldView view = existing != null ? existing.GetComponent<ItemWorldView>() : null;

        if (view == null)
            return null;

        SerializedObject so = new SerializedObject(view);
        ItemHandSettings settings = new ItemHandSettings
        {
            heldPosition = so.FindProperty("heldPositionOffset").vector3Value,
            heldRotation = so.FindProperty("heldRotationOffset").vector3Value,
        };

        Transform model = existing.transform.Find(ModelChildName);

        // 모델 위치는 1인칭만 지킨다 — 월드 아이템의 모델 위치는 도구가 정한다
        if (model != null && existing.GetComponent<NetworkObject>() == null)
        {
            settings.hasModel = true;
            settings.modelPosition = model.localPosition;
            settings.modelRotation = model.localRotation;
            settings.modelScale = model.localScale;
        }

        return settings;
    }

    /// <summary>다시 만든 복사본에 손 설정을 되돌린다.</summary>
    public void Apply(GameObject copy)
    {
        ItemWorldView view = copy.GetComponent<ItemWorldView>();
        bool added = view == null;

        if (added)
            view = copy.AddComponent<ItemWorldView>();

        SerializedObject so = new SerializedObject(view);

        if (added)
        {
            // 1인칭 모델: 그리는 대상 = 새 모델 렌더러, 충돌 없음 (팀원이 넣은 구성과 같게)
            Renderer[] renderers = copy.GetComponentsInChildren<Renderer>(true);
            SerializedProperty rendererList = so.FindProperty("renderers");
            rendererList.arraySize = renderers.Length;

            for (int i = 0; i < renderers.Length; i++)
                rendererList.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];

            so.FindProperty("colliders").arraySize = 0;
        }

        so.FindProperty("heldPositionOffset").vector3Value = heldPosition;
        so.FindProperty("heldRotationOffset").vector3Value = heldRotation;
        so.ApplyModifiedPropertiesWithoutUndo();

        Transform model = hasModel ? copy.transform.Find(ModelChildName) : null;

        if (model != null)
        {
            model.localPosition = modelPosition;
            model.localRotation = modelRotation;
            model.localScale = modelScale;
        }
    }

    /// <summary>로그용 요약.</summary>
    public string Describe()
    {
        string model = hasModel ? $", 1인칭 모델 위치 {modelPosition} · 크기 {modelScale.x:0.##}" : string.Empty;
        return $"손 기준 위치 {heldPosition} · 회전 {heldRotation}{model}";
    }
}
