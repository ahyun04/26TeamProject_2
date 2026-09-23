using System.Collections;
using UnityEngine;

namespace TrustNoOne.Missions
{
    /// <summary>
    /// [역할] 차단기 레버 하나의 "모습"만 담당: 켜짐/꺼짐 위치로 회전 + 셰이더 색(_Off_On). 2a 명세 2-7.
    ///  옛 BreakerLever 의 연출 부분을 옮겼다. 클릭은 같은 오브젝트의 StationButton 이 받는다 (이 컴포넌트는 ITargetable 이 아님).
    ///
    /// [상태는 BreakerStation 이 준다] 스테이션이 매 Render 에서 동기화된 레버 상태로 SetState 를 부른다 → 모든 피어가 같은 경로.
    /// [첫 호출은 즉시] 스폰 직후 / 늦게 들어온 플레이어는 애니메이션 없이 바로 현재 상태로 맞춘다 (옛 코드와 같음).
    /// [색] 머티리얼을 복제하지 않고 MaterialPropertyBlock 으로 이 렌더러의 _Off_On 값만 바꾼다 (켜짐 1 / 꺼짐 0, 옛 코드와 같음).
    /// </summary>
    public class BreakerLeverVisual : MonoBehaviour
    {
        private static readonly int OffOnId = Shader.PropertyToID("_Off_On");

        [SerializeField] private Transform leverTransform;
        [SerializeField] private Renderer leverRenderer;

        [Header("회전 (로컬 오일러 각)")]
        [SerializeField] private Vector3 offRotation;
        [SerializeField] private Vector3 onRotation;
        [SerializeField] private float rotateDuration = 0.2f;

        private MaterialPropertyBlock propertyBlock;
        private Coroutine rotateRoutine;
        private bool? shownState;

        /// <summary>레버 상태를 모습에 반영. 이미 같은 상태면 아무것도 하지 않는다 (매 프레임 불러도 된다).</summary>
        public void SetState(bool isOn)
        {
            if (shownState == isOn)
                return;

            bool first = shownState == null;
            shownState = isOn;

            ApplyColor(isOn);

            if (leverTransform == null)
                return;

            Quaternion target = Quaternion.Euler(isOn ? onRotation : offRotation);

            if (rotateRoutine != null)
                StopCoroutine(rotateRoutine);

            rotateRoutine = null;

            if (first || !isActiveAndEnabled || rotateDuration <= 0f)
            {
                leverTransform.localRotation = target;
                return;
            }

            rotateRoutine = StartCoroutine(Rotate(target));
        }

        private void ApplyColor(bool isOn)
        {
            if (leverRenderer == null)
                return;

            if (propertyBlock == null)
                propertyBlock = new MaterialPropertyBlock();

            leverRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(OffOnId, isOn ? 1f : 0f);
            leverRenderer.SetPropertyBlock(propertyBlock);
        }

        private IEnumerator Rotate(Quaternion target)
        {
            Quaternion start = leverTransform.localRotation;
            float time = 0f;

            while (time < rotateDuration)
            {
                time += Time.deltaTime;
                leverTransform.localRotation = Quaternion.Slerp(start, target, time / rotateDuration);
                yield return null;
            }

            leverTransform.localRotation = target;
            rotateRoutine = null;
        }

        private void OnDisable()
        {
            if (rotateRoutine != null)
                StopCoroutine(rotateRoutine);

            rotateRoutine = null;

            // 회전 도중 꺼지면 목표 위치로 맞춰 둔다
            if (leverTransform != null && shownState.HasValue)
                leverTransform.localRotation = Quaternion.Euler(shownState.Value ? onRotation : offRotation);
        }
    }
}
