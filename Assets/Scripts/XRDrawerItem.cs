using UnityEngine;
using System.Collections;

public class XRDrawerItem : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private GameObject spawnPrefab;

    [Header("Animation")]
    [SerializeField] private float floatAmplitude = 0.01f;
    [SerializeField] private float floatSpeed = 1.5f;
    [SerializeField] private float idleRotateSpeed = 20f;

    [Header("Selection")]
    [SerializeField] private float selectedScaleMultiplier = 1.35f;
    [SerializeField] private float pullForwardDistance = 0.12f;
    [SerializeField] private float animDuration = 0.18f;

    private Vector3 initialLocalPos;
    private Quaternion initialLocalRot;
    private Vector3 initialLocalScale;
    private bool isSelected;
    private Coroutine animRoutine;

    public GameObject SpawnPrefab => spawnPrefab;

    private void Start()
    {
        initialLocalPos = transform.localPosition;
        initialLocalRot = transform.localRotation;
        initialLocalScale = transform.localScale;
    }

    private void Update()
    {
        if (!isSelected)
        {
            float offset = Mathf.Sin(Time.time * floatSpeed + initialLocalPos.x * 10f) * floatAmplitude;
            transform.localPosition = initialLocalPos + new Vector3(0f, offset, 0f);
            transform.Rotate(Vector3.up, idleRotateSpeed * Time.deltaTime, Space.Self);
        }
    }

    public void Select(Transform playerCamera)
    {
        if (isSelected) return;
        isSelected = true;

        if (animRoutine != null) StopCoroutine(animRoutine);
        animRoutine = StartCoroutine(AnimateSelection(playerCamera, true));
    }

    public void Deselect(Transform playerCamera)
    {
        if (!isSelected) return;
        isSelected = false;

        if (animRoutine != null) StopCoroutine(animRoutine);
        animRoutine = StartCoroutine(AnimateSelection(playerCamera, false));
    }

    private IEnumerator AnimateSelection(Transform playerCamera, bool selecting)
    {
        Vector3 startPos = transform.localPosition;
        Vector3 startScale = transform.localScale;

        Vector3 targetScale = selecting
            ? initialLocalScale * selectedScaleMultiplier
            : initialLocalScale;

        Vector3 targetPos = initialLocalPos;

        if (selecting && playerCamera != null)
        {
            Vector3 towardCamera = (playerCamera.position - transform.position).normalized;
            Vector3 worldTarget = transform.position + towardCamera * pullForwardDistance;
            targetPos = transform.parent.InverseTransformPoint(worldTarget);
        }

        float elapsed = 0f;

        while (elapsed < animDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / animDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            transform.localPosition = Vector3.Lerp(startPos, targetPos, eased);
            transform.localScale = Vector3.Lerp(startScale, targetScale, eased);

            yield return null;
        }

        transform.localPosition = targetPos;
        transform.localScale = targetScale;
    }
}