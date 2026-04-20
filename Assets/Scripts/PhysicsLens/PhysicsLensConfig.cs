using UnityEngine;

[CreateAssetMenu(fileName = "PhysicsLensConfig", menuName = "MR Blueprint/Physics Lens Config")]
public sealed class PhysicsLensConfig : ScriptableObject
{
    [Header("Activation")]
    [SerializeField] private bool featureEnabled = true;

    [Header("Telemetry")]
    [SerializeField] private int maxTelemetrySamples = 512;
    [SerializeField] private float historySeconds = 7.5f;
    [SerializeField] private float textRefreshSeconds = 0.16f;
    [SerializeField] private float constraintRescanSeconds = 0.45f;
    [SerializeField] private float recentImpactSeconds = 2f;
    [SerializeField] private float restSpeedThreshold = 0.04f;
    [SerializeField] private float lowForceThreshold = 0.25f;
    [SerializeField] private float springDominanceThreshold = 1.5f;
    [SerializeField] private float hingeDominanceThreshold = 0.15f;
    [SerializeField] private float hingeLimitWarnDegrees = 18f;
    [SerializeField] private float potentialZeroPlaneY;

    [Header("Panel")]
    [SerializeField] private Vector2 compactPanelSize = new Vector2(430f, 520f);
    [SerializeField] private Vector2 expandedPanelSize = new Vector2(560f, 700f);
    [SerializeField] private float canvasWorldScale = 0.00165f;
    [SerializeField] private float panelFollowSharpness = 13f;
    [SerializeField] private float panelRotationSharpness = 15f;
    [SerializeField] private float panelHorizontalOffset = 0.38f;
    [SerializeField] private float panelVerticalOffset = 0.16f;
    [SerializeField] private float panelForwardOffset = 0.02f;
    [SerializeField] private float panelFadeSharpness = 10f;
    [SerializeField] private float minObjectClearance = 0.22f;

    [Header("Graph")]
    [SerializeField] private Vector2 compactGraphSize = new Vector2(374f, 176f);
    [SerializeField] private Vector2 expandedGraphSize = new Vector2(494f, 252f);
    [SerializeField] private float graphAutoscaleSharpness = 5f;
    [SerializeField] private float timelineBaseWidth = 4f;
    [SerializeField] private float timelineForceWidth = 8f;
    [SerializeField] private float phaseRibbonWidth = 5.5f;
    [SerializeField] private float phaseDepth = 72f;

    [Header("Colors")]
    [SerializeField] private Color panelBackground = new Color(0.035f, 0.04f, 0.042f, 0.94f);
    [SerializeField] private Color panelAccent = new Color(0.22f, 0.62f, 1f, 1f);
    [SerializeField] private Color textPrimary = new Color(0.93f, 0.97f, 1f, 1f);
    [SerializeField] private Color textSecondary = new Color(0.64f, 0.77f, 0.9f, 1f);
    [SerializeField] private Color chipBackground = new Color(0.08f, 0.12f, 0.16f, 0.92f);
    [SerializeField] private Color graphGrid = new Color(0.18f, 0.28f, 0.4f, 0.55f);
    [SerializeField] private Color gravityColor = new Color(0.25f, 0.62f, 1f, 1f);
    [SerializeField] private Color userForceColor = new Color(0.32f, 0.95f, 0.48f, 1f);
    [SerializeField] private Color springCompressionColor = new Color(0.24f, 0.62f, 1f, 1f);
    [SerializeField] private Color springRestColor = new Color(0.82f, 0.88f, 0.78f, 1f);
    [SerializeField] private Color springTensionColor = new Color(1f, 0.47f, 0.18f, 1f);
    [SerializeField] private Color hingeColor = new Color(1f, 0.78f, 0.22f, 1f);
    [SerializeField] private Color impactColor = new Color(1f, 0.22f, 0.18f, 1f);
    [SerializeField] private Color frictionColor = new Color(0.32f, 0.78f, 1f, 1f);
    [SerializeField] private Color otherColor = new Color(0.74f, 0.78f, 0.76f, 1f);

    public bool FeatureEnabled => featureEnabled;
    public int MaxTelemetrySamples => Mathf.Clamp(maxTelemetrySamples, 96, 2048);
    public float HistorySeconds => Mathf.Clamp(historySeconds, 3f, 12f);
    public float TextRefreshSeconds => Mathf.Clamp(textRefreshSeconds, 0.06f, 0.6f);
    public float ConstraintRescanSeconds => Mathf.Clamp(constraintRescanSeconds, 0.12f, 2f);
    public float RecentImpactSeconds => Mathf.Clamp(recentImpactSeconds, 0.2f, 5f);
    public float RestSpeedThreshold => Mathf.Max(0.001f, restSpeedThreshold);
    public float LowForceThreshold => Mathf.Max(0.001f, lowForceThreshold);
    public float SpringDominanceThreshold => Mathf.Max(0.01f, springDominanceThreshold);
    public float HingeDominanceThreshold => Mathf.Max(0.01f, hingeDominanceThreshold);
    public float HingeLimitWarnDegrees => Mathf.Clamp(hingeLimitWarnDegrees, 2f, 90f);
    public float PotentialZeroPlaneY => potentialZeroPlaneY;
    public Vector2 CompactPanelSize => compactPanelSize;
    public Vector2 ExpandedPanelSize => expandedPanelSize;
    public float CanvasWorldScale => Mathf.Clamp(canvasWorldScale, 0.0005f, 0.006f);
    public float PanelFollowSharpness => Mathf.Max(0.01f, panelFollowSharpness);
    public float PanelRotationSharpness => Mathf.Max(0.01f, panelRotationSharpness);
    public float PanelHorizontalOffset => Mathf.Max(0.05f, panelHorizontalOffset);
    public float PanelVerticalOffset => panelVerticalOffset;
    public float PanelForwardOffset => panelForwardOffset;
    public float PanelFadeSharpness => Mathf.Max(0.01f, panelFadeSharpness);
    public float MinObjectClearance => Mathf.Max(0.02f, minObjectClearance);
    public Vector2 CompactGraphSize => compactGraphSize;
    public Vector2 ExpandedGraphSize => expandedGraphSize;
    public float GraphAutoscaleSharpness => Mathf.Max(0.01f, graphAutoscaleSharpness);
    public float TimelineBaseWidth => Mathf.Max(0.5f, timelineBaseWidth);
    public float TimelineForceWidth => Mathf.Max(0.5f, timelineForceWidth);
    public float PhaseRibbonWidth => Mathf.Max(0.5f, phaseRibbonWidth);
    public float PhaseDepth => Mathf.Max(8f, phaseDepth);
    public Color PanelBackground => panelBackground;
    public Color PanelAccent => panelAccent;
    public Color TextPrimary => textPrimary;
    public Color TextSecondary => textSecondary;
    public Color ChipBackground => chipBackground;
    public Color GraphGrid => graphGrid;
    public Color GravityColor => gravityColor;
    public Color UserForceColor => userForceColor;
    public Color SpringCompressionColor => springCompressionColor;
    public Color SpringRestColor => springRestColor;
    public Color SpringTensionColor => springTensionColor;
    public Color HingeColor => hingeColor;
    public Color ImpactColor => impactColor;
    public Color FrictionColor => frictionColor;
    public Color OtherColor => otherColor;

    public static PhysicsLensConfig CreateRuntimeDefault()
    {
        return CreateInstance<PhysicsLensConfig>();
    }

    public Color GetDriverColor(PhysicsLensDriver driver)
    {
        switch (driver)
        {
            case PhysicsLensDriver.Gravity:
                return gravityColor;
            case PhysicsLensDriver.UserForce:
                return userForceColor;
            case PhysicsLensDriver.Spring:
                return springTensionColor;
            case PhysicsLensDriver.HingeJoint:
                return hingeColor;
            case PhysicsLensDriver.Impact:
                return impactColor;
            case PhysicsLensDriver.Friction:
                return frictionColor;
            default:
                return otherColor;
        }
    }
}
