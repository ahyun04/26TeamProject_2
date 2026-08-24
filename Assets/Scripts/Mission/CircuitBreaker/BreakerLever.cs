using Fusion;
using System.Collections;
using UnityEngine;

/// <summary>
/// [ 차단기 레버 하나의 동작과 시각 표현 ]
///
/// 플레이어가 클릭하면 BreakerMission에 상태 변경을 요청하고,
/// BreakerMission에서 전달받은 상태에 따라
/// 레버 회전과 Shader 색상을 변경
/// </summary>
public class BreakerLever : MonoBehaviour, ITargetable, IInteractable
{
    [Header("상호작용")]
    [SerializeField] private BreakerMission breakerMission;
    [SerializeField] private int leverIndex;

    [Header("레버")]
    [SerializeField] private Transform leverTransform;
    [SerializeField] private Renderer leverRenderer;

    [Header("회전")]
    [SerializeField] private Vector3 offRotation;
    [SerializeField] private Vector3 onRotation;
    [SerializeField] private float rotateDuration = 0.2f;

    // Shader Graph의 Off/On Float Property Reference
    // MaterialPropertyBlock에서 사용할 ID로 미리 변환
    private static readonly int OffOnId = Shader.PropertyToID("_Off_On");

    // 같은 Material을 공유하면서
    // 이 Renderer의 Shader 값만 개별 변경하기 위해 사용
    private MaterialPropertyBlock propertyBlock;

    private Coroutine rotateCoroutine;
    private NetworkObject targetObject;
    private bool isInitialized;

    public NetworkObject TargetObject => targetObject;


    private void Awake()
    {
        // Renderer별 Shader 값을 변경할 PropertyBlock 생성
        propertyBlock = new MaterialPropertyBlock();

        if (breakerMission == null)
            breakerMission = GetComponentInParent<BreakerMission>();

        targetObject = GetComponentInParent<NetworkObject>();

        // 현재 Material에 _Off_On Property가 실제 존재하는지 확인
        if (!leverRenderer.sharedMaterial.HasProperty(OffOnId))
            Debug.LogError($"{name} : Shader에 _Off_On Property가 없습니다.");
    }


    /// <summary>
    /// PlayerInteraction에서 좌클릭했을 때 호출
    /// 자신이 몇 번째 레버인지 BreakerMission에 전달하여
    /// On ↔ Off 상태 변경을 요청
    /// </summary>
    public void Interact()
    {
        if (breakerMission != null)
            breakerMission.RequestToggle(leverIndex);
    }


    /// <summary>
    /// BreakerMission에서 전달받은 상태를 실제 모습에 적용
    /// true  = 초록색 + On 위치
    /// false = 빨간색 + Off 위치
    /// </summary>
    public void SetState(bool isOn)
    {
        // Material 자체를 변경하지 않고
        // 현재 Renderer의 _Off_On 값만 개별적으로 변경
        propertyBlock.SetFloat(OffOnId, isOn ? 1f : 0f);
        leverRenderer.SetPropertyBlock(propertyBlock);

        Quaternion targetRotation = Quaternion.Euler(isOn ? onRotation : offRotation);

        // 처음 상태를 적용할 때는 회전 애니메이션을 재생하지 않고
        // 바로 현재 네트워크 상태의 위치로 이동
        if (!isInitialized)
        {
            isInitialized = true;
            leverTransform.localRotation = targetRotation;
            return;
        }

        if (rotateCoroutine != null)
            StopCoroutine(rotateCoroutine);

        rotateCoroutine = StartCoroutine(RotateLever(targetRotation));
    }


    /// <summary>
    /// 현재 위치에서 목표 위치까지 레버를 부드럽게 회전
    /// </summary>
    private IEnumerator RotateLever(Quaternion targetRotation)
    {
        Quaternion startRotation = leverTransform.localRotation;
        float time = 0f;

        while (time < rotateDuration)
        {
            time += Time.deltaTime;
            leverTransform.localRotation = Quaternion.Slerp(startRotation, targetRotation, time / rotateDuration);

            yield return null;
        }

        leverTransform.localRotation = targetRotation;
        rotateCoroutine = null;
    }
}