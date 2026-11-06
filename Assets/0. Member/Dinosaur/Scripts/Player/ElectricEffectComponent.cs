using UnityEngine;

public class ElectricEffectComponent : MonoBehaviour
{
    [SerializeField] private Renderer visibilitySource; //원래 모델이 숨겨지면 전기 연출도 함께 숨김
    [SerializeField] private LineRenderer[] arcs; //번개를 표시할 선
    [SerializeField] private ParticleSystem sparks; //짧게 튀는 전기 스파크
    [SerializeField] private Transform startPoint; //도구의 첫 번째 전극
    [SerializeField] private Transform endPoint; //도구의 두 번째 전극
    [SerializeField] private bool surroundBody; //대상 몸 주변의 스턴 효과 여부
    [SerializeField] private Vector3 bodyCenter = new Vector3(0f, 1f, 0f); //몸 주변 번개의 기준 위치
    [SerializeField, Min(0.01f)] private float bodyRadius = 0.4f; //몸 주변 효과의 가로 범위
    [SerializeField, Min(0f)] private float jitter = 0.03f; //번개 선의 흔들림 범위
    [SerializeField, Min(0.01f)] private float refreshInterval = 0.04f; //번개 모양을 갱신할 간격

    private bool looping; //복제된 스턴 상태가 유지되는 동안 재생
    private bool playing; //현재 파티클과 선의 표시 상태
    private float endTime; //단발 방전 효과의 종료 시각
    private float nextRefresh; //다음 번개 모양 갱신 시각

    internal void play(float seconds) //공격 승인 후 단발 방전 재생
    {
        endTime = Mathf.Max(endTime, Time.unscaledTime + Mathf.Max(0f, seconds));
    }

    internal void setLooping(bool active) //스턴 상태를 로컬 연출에 반영
    {
        looping = active;
    }

    internal void stop() //숨김·해제·씬 전환 시 즉시 연출 정리
    {
        looping = false;
        endTime = 0f;
        setPlaying(false);
    }

    private void Awake() //장착 전이나 게임 시작 시 방전하지 않음
    {
        setPlaying(false);
    }

    private void LateUpdate() //전역 난수 상태를 건드리지 않고 번개 모양만 갱신
    {
        bool visible = visibilitySource == null || (visibilitySource.enabled && visibilitySource.gameObject.activeInHierarchy); //주머니와 본인 전용 표시 구분
        bool active = visible && (looping || Time.unscaledTime < endTime); //실제 재생 여부
        setPlaying(active);
        if (!active || Time.unscaledTime < nextRefresh || arcs == null) return;
        nextRefresh = Time.unscaledTime + refreshInterval;
        float phase = Time.unscaledTime * 43f + GetInstanceID() % 97; //각 화면의 시각적 흔들림 기준
        for (int arcIndex = 0; arcIndex < arcs.Length; arcIndex++)
        {
            LineRenderer arc = arcs[arcIndex]; //이번 번개 선
            if (arc == null) continue;
            float angle = phase * 0.11f + arcIndex * 2.094f; //몸 주변의 분산 각도
            Vector3 start = surroundBody ? bodyCenter + new Vector3(Mathf.Cos(angle), -0.35f, Mathf.Sin(angle)) * bodyRadius :
                startPoint != null ? transform.InverseTransformPoint(startPoint.position) : Vector3.zero;
            Vector3 end = surroundBody ? bodyCenter + new Vector3(Mathf.Cos(angle + 1.8f), 0.7f, Mathf.Sin(angle + 1.8f)) * bodyRadius :
                endPoint != null ? transform.InverseTransformPoint(endPoint.position) : Vector3.up * 0.1f;
            for (int point = 0; point < arc.positionCount; point++)
            {
                float t = point / (float)Mathf.Max(1, arc.positionCount - 1); //시작과 끝 사이 비율
                float strength = Mathf.Sin(t * Mathf.PI) * jitter; //전극 끝은 고정하고 중간만 흔들기
                float noise = phase + arcIndex * 7.1f + point * 3.7f; //선마다 다른 흔들림
                Vector3 offset = new Vector3(Mathf.Sin(noise), Mathf.Cos(noise * 1.7f), Mathf.Sin(noise * 2.3f)) * strength; //번개 선의 작은 꺾임
                arc.SetPosition(point, Vector3.Lerp(start, end, t) + offset);
            }
        }
    }

    private void setPlaying(bool active) //렌더러 표시와 파티클 시작·종료를 함께 처리
    {
        if (arcs != null)
            foreach (LineRenderer arc in arcs) if (arc != null) arc.enabled = active;
        if (sparks != null)
        {
            if (active && !playing) sparks.Play(true);
            else if (!active) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        playing = active;
    }

    private void OnDisable() //로컬 도구 교체와 오브젝트 파괴 시 잔상 제거
    {
        stop();
    }
}
