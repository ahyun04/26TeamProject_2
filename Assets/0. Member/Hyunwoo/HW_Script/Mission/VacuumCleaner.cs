using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VacuumCleaner : MonoBehaviour
{
    [SerializeField] private Transform pivot;
    [SerializeField] private float range = 2f;
    [SerializeField] private float nextDelay = 0.2f;
    [SerializeField] private LayerMask dustLayer;

    private bool isSucking;

    private void Update()
    {
        if (Input.GetMouseButtonDown(1) && !isSucking)
            StartCoroutine(SuckDusts());
    }

    private IEnumerator SuckDusts()
    {
        isSucking = true;

        Collider[] hits = Physics.OverlapSphere(
            pivot.position,
            range,
            dustLayer
        );

        List<Dust> dusts = new List<Dust>();

        foreach (Collider hit in hits)
        {
            Dust dust = hit.GetComponentInParent<Dust>();

            if (dust != null && !dusts.Contains(dust))
                dusts.Add(dust);
        }

        while (dusts.Count > 0)
        {
            int index = Random.Range(0, dusts.Count);

            Dust dust = dusts[index];
            dusts.RemoveAt(index);

            dust.Suck(pivot);

            yield return new WaitForSeconds(nextDelay);
        }

        isSucking = false;
    }
}