using UnityEngine;

[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public class VoicePlaybackGainComponent : MonoBehaviour
{
    private volatile float requestedGain = 1f; //메인 스레드가 지정한 수신 증폭 배율
    private float previousGain = 1f; //오디오 스레드의 직전 증폭 배율

    internal void setGain(float gain) //기존 음량 조절을 넘는 부분만 음성 샘플에 적용
    {
        requestedGain = Mathf.Clamp(gain, 1f, GameAudio.getMaximumVolume(AudioVolumeChannel.Voice));
    }

    private void OnAudioFilterRead(float[] data, int channels) //음성만 부드럽게 증폭하고 최대 진폭을 제한
    {
        float targetGain = requestedGain; //콜백 중에는 하나의 설정값 사용
        if (channels <= 0 || data.Length < channels || (targetGain == 1f && previousGain == 1f)) return;
        int frameCount = data.Length / channels; //채널마다 같은 배율을 적용할 오디오 프레임 수
        float step = (targetGain - previousGain) / frameCount; //볼륨 변경 시 순간적인 잡음 방지
        for (int frame = 0; frame < frameCount; frame++)
        {
            float gain = previousGain + step * (frame + 1); //현재 프레임의 증폭 배율
            for (int channel = 0; channel < channels; channel++)
            {
                int index = frame * channels + channel; //현재 채널의 샘플 위치
                float sample = data[index] * gain; //최대 두 배로 증폭한 음성
                float magnitude = sample < 0f ? -sample : sample; //클리핑 검사에 사용할 진폭
                float knee = 1f - 0.1f * (gain - 1f); //배율 변경 중에도 연속적으로 이동할 피크 압축 시작점
                if (magnitude > knee && gain > 1f)
                {
                    float excess = magnitude - knee; //완만하게 압축할 피크 구간
                    float limited = knee + excess / (1f + excess / (1f - knee)); //출력 진폭이 1을 넘지 않도록 하는 소프트 리미터
                    sample = sample < 0f ? -limited : limited;
                }
                data[index] = sample;
            }
        }
        previousGain = targetGain;
    }
}
