using UnityEngine;

public class Dust : MonoBehaviour
{
    [SerializeField] private float suckTime = 0.5f;

    private Transform target;
    private Vector3 startPos;
    private Vector3 startScale;

    private float time;
    private bool isSucking;

    public void Suck(Transform target)
    {
        if (isSucking)
            return;

        this.target = target;

        startPos = transform.position;
        startScale = transform.localScale;

        time = 0f;
        isSucking = true;
    }

    private void Update()
    {
        if (!isSucking)
            return;

        time += Time.deltaTime;

        float t = time / suckTime;

        transform.position = Vector3.Lerp(startPos, target.position, t);
        transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);

        if (t >= 1f)
            gameObject.SetActive(false);
    }
}