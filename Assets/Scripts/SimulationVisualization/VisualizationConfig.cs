using UnityEngine;

[CreateAssetMenu(fileName = "SimulationVisualizationConfig", menuName = "MR Blueprint/Simulation Visualization Config")]
public sealed class VisualizationConfig : ScriptableObject
{
    [Header("Activation")]
    [SerializeField] private bool featureEnabled = true;

    [Header("Telemetry")]
    [SerializeField] private int maxTrackedBodies = 96;
    [SerializeField] private int maxTrailSamples = 192;
    [SerializeField] private float bodyRescanSeconds = 0.75f;
    [SerializeField] private float constraintRescanSeconds = 0.45f;
    [SerializeField] private float recentImpactSeconds = 1.6f;
    [SerializeField] private float lowForceThreshold = 0.35f;
    [SerializeField] private float lowSpeedThreshold = 0.04f;
    [SerializeField] private float springLoadFocusThreshold = 8f;
    [SerializeField] private float hingeLoadFocusThreshold = 0.22f;
    [SerializeField] private float hingeLimitWarningDegrees = 18f;

    [Header("Caps")]
    [SerializeField] private int maxForceSpears = 8;
    [SerializeField] private int maxVelocityRibbons = 6;
    [SerializeField] private int maxImpactMarkers = 24;
    [SerializeField] private int maxSpringOverlays = 32;
    [SerializeField] private int maxHingeOverlays = 18;

    [Header("Force Spear")]
    [SerializeField] private float forceMetersPerNewton = 0.014f;
    [SerializeField] private float forceMinLength = 0.08f;
    [SerializeField] private float forceMaxLength = 0.62f;
    [SerializeField] private float forceBaseWidth = 0.008f;
    [SerializeField] private float forceFocusWidth = 0.018f;
    [SerializeField] private float forceSmoothingSharpness = 12f;

    [Header("Velocity Ribbon")]
    [SerializeField] private float ribbonMinSpeed = 0.08f;
    [SerializeField] private float ribbonBaseWidth = 0.006f;
    [SerializeField] private float ribbonFastWidth = 0.022f;
    [SerializeField] private float ribbonSpeedForFullWidth = 2.4f;

    [Header("Impact Flash")]
    [SerializeField] private float impactMinImpulse = 0.08f;
    [SerializeField] private float impactDuration = 0.26f;
    [SerializeField] private float impactRadiusMin = 0.08f;
    [SerializeField] private float impactRadiusMax = 0.48f;

    [Header("Constraints")]
    [SerializeField] private float springAmbientWidth = 0.006f;
    [SerializeField] private float springFocusWidth = 0.018f;
    [SerializeField] private float hingeRadius = 0.14f;
    [SerializeField] private float hingeAmbientWidth = 0.005f;
    [SerializeField] private float hingeFocusWidth = 0.014f;
    [SerializeField] private float axisRodLength = 0.22f;

    [Header("Importance")]
    [SerializeField] private float focusImportanceThreshold = 1.6f;
    [SerializeField] private float selectedImportanceBoost = 4f;
    [SerializeField] private float sleepingImportanceMultiplier = 0.18f;
    [SerializeField] private float cameraDistanceForFullRelevance = 1.4f;
    [SerializeField] private float cameraDistanceForLowRelevance = 5f;

    [Header("Colors")]
    [SerializeField] private Color gravityColor = new Color(0.22f, 0.62f, 1f, 0.92f);
    [SerializeField] private Color userForceColor = new Color(0.22f, 0.96f, 0.48f, 0.95f);
    [SerializeField] private Color springCompressionColor = new Color(0.18f, 0.58f, 1f, 0.92f);
    [SerializeField] private Color springRestColor = new Color(0.74f, 0.86f, 0.78f, 0.72f);
    [SerializeField] private Color springTensionColor = new Color(1f, 0.46f, 0.16f, 0.95f);
    [SerializeField] private Color hingeColor = new Color(1f, 0.78f, 0.22f, 0.9f);
    [SerializeField] private Color hingeWarningColor = new Color(1f, 0.25f, 0.12f, 0.96f);
    [SerializeField] private Color impactColor = new Color(1f, 0.22f, 0.14f, 1f);
    [SerializeField] private Color frictionColor = new Color(0.32f, 0.78f, 1f, 0.9f);
    [SerializeField] private Color otherColor = new Color(0.76f, 0.82f, 0.78f, 0.72f);
    [SerializeField] private Color ribbonColor = new Color(0.17f, 0.92f, 0.86f, 0.78f);

    public bool FeatureEnabled => featureEnabled;
    public int MaxTrackedBodies => Mathf.Clamp(maxTrackedBodies, 8, 256);
    public int MaxTrailSamples => Mathf.Clamp(maxTrailSamples, 32, 1024);
    public float BodyRescanSeconds => Mathf.Clamp(bodyRescanSeconds, 0.2f, 4f);
    public float ConstraintRescanSeconds => Mathf.Clamp(constraintRescanSeconds, 0.15f, 4f);
    public float RecentImpactSeconds => Mathf.Clamp(recentImpactSeconds, 0.2f, 5f);
    public float LowForceThreshold => Mathf.Max(0.001f, lowForceThreshold);
    public float LowSpeedThreshold => Mathf.Max(0.001f, lowSpeedThreshold);
    public float SpringLoadFocusThreshold => Mathf.Max(0.001f, springLoadFocusThreshold);
    public float HingeLoadFocusThreshold => Mathf.Max(0.001f, hingeLoadFocusThreshold);
    public float HingeLimitWarningDegrees => Mathf.Clamp(hingeLimitWarningDegrees, 2f, 90f);
    public int MaxForceSpears => Mathf.Clamp(maxForceSpears, 1, 48);
    public int MaxVelocityRibbons => Mathf.Clamp(maxVelocityRibbons, 1, 32);
    public int MaxImpactMarkers => Mathf.Clamp(maxImpactMarkers, 4, 96);
    public int MaxSpringOverlays => Mathf.Clamp(maxSpringOverlays, 1, 96);
    public int MaxHingeOverlays => Mathf.Clamp(maxHingeOverlays, 1, 64);
    public float ForceMetersPerNewton => Mathf.Max(0.0001f, forceMetersPerNewton);
    public float ForceMinLength => Mathf.Max(0.01f, forceMinLength);
    public float ForceMaxLength => Mathf.Max(ForceMinLength, forceMaxLength);
    public float ForceBaseWidth => Mathf.Max(0.001f, forceBaseWidth);
    public float ForceFocusWidth => Mathf.Max(ForceBaseWidth, forceFocusWidth);
    public float ForceSmoothingSharpness => Mathf.Max(0.01f, forceSmoothingSharpness);
    public float RibbonMinSpeed => Mathf.Max(0.001f, ribbonMinSpeed);
    public float RibbonBaseWidth => Mathf.Max(0.001f, ribbonBaseWidth);
    public float RibbonFastWidth => Mathf.Max(RibbonBaseWidth, ribbonFastWidth);
    public float RibbonSpeedForFullWidth => Mathf.Max(0.001f, ribbonSpeedForFullWidth);
    public float ImpactMinImpulse => Mathf.Max(0.001f, impactMinImpulse);
    public float ImpactDuration => Mathf.Clamp(impactDuration, 0.08f, 1.2f);
    public float ImpactRadiusMin => Mathf.Max(0.01f, impactRadiusMin);
    public float ImpactRadiusMax => Mathf.Max(ImpactRadiusMin, impactRadiusMax);
    public float SpringAmbientWidth => Mathf.Max(0.001f, springAmbientWidth);
    public float SpringFocusWidth => Mathf.Max(SpringAmbientWidth, springFocusWidth);
    public float HingeRadius => Mathf.Max(0.03f, hingeRadius);
    public float HingeAmbientWidth => Mathf.Max(0.001f, hingeAmbientWidth);
    public float HingeFocusWidth => Mathf.Max(HingeAmbientWidth, hingeFocusWidth);
    public float AxisRodLength => Mathf.Max(0.04f, axisRodLength);
    public float FocusImportanceThreshold => Mathf.Max(0.01f, focusImportanceThreshold);
    public float SelectedImportanceBoost => Mathf.Max(0f, selectedImportanceBoost);
    public float SleepingImportanceMultiplier => Mathf.Clamp01(sleepingImportanceMultiplier);
    public float CameraDistanceForFullRelevance => Mathf.Max(0.05f, cameraDistanceForFullRelevance);
    public float CameraDistanceForLowRelevance => Mathf.Max(CameraDistanceForFullRelevance + 0.01f, cameraDistanceForLowRelevance);
    public Color GravityColor => gravityColor;
    public Color UserForceColor => userForceColor;
    public Color SpringCompressionColor => springCompressionColor;
    public Color SpringRestColor => springRestColor;
    public Color SpringTensionColor => springTensionColor;
    public Color HingeColor => hingeColor;
    public Color HingeWarningColor => hingeWarningColor;
    public Color ImpactColor => impactColor;
    public Color FrictionColor => frictionColor;
    public Color OtherColor => otherColor;
    public Color RibbonColor => ribbonColor;

    public static VisualizationConfig CreateRuntimeDefault()
    {
        return CreateInstance<VisualizationConfig>();
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
