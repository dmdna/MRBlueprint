using UnityEngine;
using System;
using System.Collections;

public class FrostedGlassPanelAnimator : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform panelTransform;
    [SerializeField] private CanvasGroup contentCanvasGroup;

    [Header("Animation")]
    [SerializeField] private float openDuration = 0.25f;
    [SerializeField] private float closedHeight = 0.002f;
    [SerializeField] private float openHeight = 1f;
    [SerializeField] private float panelWidth = 1.2f;
    [SerializeField] private float panelDepth = 0.02f;

    private Coroutine animRoutine;

    private void Awake()
    {
        if (panelTransform == null)
        {
            panelTransform = transform;
        }
    }

    public void Open()
    {
        if (animRoutine != null) StopCoroutine(animRoutine);
        animRoutine = StartCoroutine(AnimatePanel(true, null));
    }

    public void Close(Action onComplete = null)
    {
        if (animRoutine != null) StopCoroutine(animRoutine);
        animRoutine = StartCoroutine(AnimatePanel(false, onComplete));
    }

    private IEnumerator AnimatePanel(bool opening, Action onComplete)
    {
        float startHeight = panelTransform.localScale.y;
        float targetHeight = opening ? openHeight : closedHeight;

        float elapsed = 0f;

        if (opening && contentCanvasGroup != null)
        {
            contentCanvasGroup.alpha = 0f;
            contentCanvasGroup.interactable = false;
            contentCanvasGroup.blocksRaycasts = false;
        }

        while (elapsed < openDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / openDuration);
            float eased = EaseOutCubic(t);

            float currentHeight = Mathf.Lerp(startHeight, targetHeight, eased);
            panelTransform.localScale = new Vector3(panelWidth, currentHeight, panelDepth);

            if (contentCanvasGroup != null)
            {
                float contentAlpha = opening ? Mathf.InverseLerp(0.35f, 1f, t) : 1f - t;
                contentCanvasGroup.alpha = Mathf.Clamp01(contentAlpha);
            }

            yield return null;
        }

        panelTransform.localScale = new Vector3(panelWidth, targetHeight, panelDepth);

        if (contentCanvasGroup != null)
        {
            contentCanvasGroup.alpha = opening ? 1f : 0f;
            contentCanvasGroup.interactable = opening;
            contentCanvasGroup.blocksRaycasts = opening;
        }

        onComplete?.Invoke();
    }

    private float EaseOutCubic(float t)
    {
        return 1f - Mathf.Pow(1f - t, 3f);
    }
}