using UnityEngine;
using UnityEngine.VFX;
using Enviro;
using System.Collections.Generic;

public class TreeVFXWeatherManager : MonoBehaviour
{
    [Header("VFX Graph Asset")]
    [Tooltip("Drag your LeavesMesh VFX Graph Asset here (e.g., Assets/VFX/LeavesMesh.vfx)")]
    public VisualEffectAsset leavesVFXAsset;

    [Header("Wind (float)")]
    public float clearWind   = 0f;
    public float cloudWind   = 0.5f;
    public float rainWind    = 1f;
    public float snowWind    = 0.3f;

    [Header("Falling Leaves (count)")]
    public int cloudLeaves   = 15;
    public int rainLeaves    = 35; // Recommended: Adjust this between 30-40
    public int snowLeaves    = 10;

    [Header("Day / Night Intensity")]
    public float dayIntensity   = 3.93f;
    public float nightIntensity = 1.5f; // Set this lower for a darker night

    [Header("Leaves Colors Gradients")]
    [Tooltip("Define the gradient for leaves during the day.")]
    public Gradient dayLeavesGradient;
    [Tooltip("Define the gradient for leaves during the night.")]
    public Gradient nightLeavesGradient;
    [Tooltip("Define the gradient for leaves during snow (e.g., pure white or pale gray).")]
    public Gradient snowLeavesGradient; // Renamed for clarity (was just 'snowGradient')

    [Header("Position Gradients")] // NEW HEADER FOR POSITION GRADIENTS
    [Tooltip("Define the position gradient for leaves during the day.")]
    public Gradient dayPositionGradient;
    [Tooltip("Define the position gradient for leaves during the night.")]
    public Gradient nightPositionGradient;
    [Tooltip("Define the position gradient for leaves during snow.")]
    public Gradient snowPositionGradient;

    // Internal state variables
    private VisualEffect[] _trees;
    private bool           _isDay = true; // Tracks current day/night state (true = Day, false = Night)
    private EnviroWeatherType _lastKnownWeatherType; // Stores the last weather type received from OnWeatherChanged

    void Start()
    {
        // 1. Find all VisualEffect components in the scene that use our specific VFX asset
        var allVFXInScene = FindObjectsOfType<VisualEffect>();
        var matchingTrees = new List<VisualEffect>();
        foreach (var vfx in allVFXInScene)
        {
            if (vfx != null && vfx.visualEffectAsset == leavesVFXAsset)
            {
                matchingTrees.Add(vfx);
            }
        }
        _trees = matchingTrees.ToArray();
        Debug.Log($"[TreeVFXWeatherManager] Found {_trees.Length} tree VFX components using '{leavesVFXAsset.name}'.");

        // 2. Initialize _isDay to a reasonable default. It will be corrected by the first OnDayTime/OnNightTime event.
        // We cannot query Enviro's current time state directly from the limited API.
        _isDay = true; // Assume day initially

        // 3. Initialize _lastKnownWeatherType to null.
        // VFX properties related to weather (wind, falling leaves) will only be updated
        // once the first OnWeatherChanged event fires. Until then, they will retain
        // their default values from the VFX Graph asset.
        _lastKnownWeatherType = null;

        // Apply initial day/night specific properties (gradient, intensity, position gradient)
        // based on the _isDay default. Weather properties will remain at VFX asset defaults.
        ApplyDayNightSpecificVFXProperties();
    }

    void OnEnable()
    {
        // Subscribe to Enviro events when the script is enabled
        if (EnviroManager.instance != null)
        {
            EnviroManager.instance.OnWeatherChanged += OnWeatherChangedHandler;
            EnviroManager.instance.OnDayTime        += OnDayTimeHandler;
            EnviroManager.instance.OnNightTime      += OnNightTimeHandler;
            Debug.Log("[TreeVFXWeatherManager] Subscribed to Enviro events.");
        }
        else
        {
            Debug.LogError("[TreeVFXWeatherManager] EnviroManager.instance not found. Weather and time-based VFX effects will not function.");
        }
    }

    void OnDisable()
    {
        // Unsubscribe from Enviro events when the script is disabled
        if (EnviroManager.instance != null)
        {
            EnviroManager.instance.OnWeatherChanged -= OnWeatherChangedHandler;
            EnviroManager.instance.OnDayTime        -= OnDayTimeHandler;
            EnviroManager.instance.OnNightTime      -= OnNightTimeHandler;
            Debug.Log("[TreeVFXWeatherManager] Unsubscribed from Enviro events.");
        }
    }

    // Handles Enviro's OnDayTime event
    private void OnDayTimeHandler()
    {
        _isDay = true;
        // If a weather event has already fired, apply all VFX properties including weather ones.
        // Otherwise, only apply day/night specific ones.
        if (_lastKnownWeatherType != null)
        {
            ApplyAllVFXProperties(_lastKnownWeatherType);
        }
        else
        {
            ApplyDayNightSpecificVFXProperties(); // Only apply day/night colors/intensity/position gradient
        }
        Debug.Log("[TreeVFXWeatherManager] Day Time detected.");
    }

    // Handles Enviro's OnNightTime event
    private void OnNightTimeHandler()
    {
        _isDay = false;
        // If a weather event has already fired, apply all VFX properties including weather ones.
        // Otherwise, only apply day/night specific ones.
        if (_lastKnownWeatherType != null)
        {
            ApplyAllVFXProperties(_lastKnownWeatherType);
        }
        else
        {
            ApplyDayNightSpecificVFXProperties(); // Only apply day/night colors/intensity/position gradient
        }
        Debug.Log("[TreeVFXWeatherManager] Night Time detected.");
    }

    // Handles Enviro's OnWeatherChanged event
    private void OnWeatherChangedHandler(EnviroWeatherType currentWeatherType)
    {
        _lastKnownWeatherType = currentWeatherType; // Store the new weather type
        ApplyAllVFXProperties(currentWeatherType);  // Apply all VFX properties
        Debug.Log($"[TreeVFXWeatherManager] Weather changed to: {currentWeatherType.name}");
    }

    /// <summary>
    /// Applies only the day/night specific properties (Intensity, Leaves Colors Gradient, Position Gradient).
    /// Used for initial setup and when only time changes before first weather event.
    /// </summary>
    private void ApplyDayNightSpecificVFXProperties()
    {
        // Determine gradients and intensity based on day/night state
        Gradient targetLeavesGradient    = _isDay ? dayLeavesGradient : nightLeavesGradient;
        Gradient targetPositionGradient  = _isDay ? dayPositionGradient : nightPositionGradient;
        float    targetIntensity         = _isDay ? dayIntensity : nightIntensity;

        foreach (var vfx in _trees)
        {
            if (vfx == null) continue;
            // Only update day/night dependent properties
            vfx.SetGradient("Leaves Colors",   targetLeavesGradient);
            vfx.SetGradient("Position Gradient", targetPositionGradient); // NEW
            vfx.SetFloat("Intensity",          targetIntensity);
        }
        Debug.Log($"[TreeVFXWeatherManager] Applied Day/Night settings (Day:{_isDay}) - Intensity:{targetIntensity}");
    }

    /// <summary>
    /// Main method to calculate and apply ALL VFX properties (wind, falling leaves, colors, intensity, position gradient)
    /// based on the provided weather type and the internally tracked day/night state.
    /// </summary>
    /// <param name="currentWeatherType">The current weather type from Enviro.</param>
    private void ApplyAllVFXProperties(EnviroWeatherType currentWeatherType)
    {
        // --- 1. Determine weather-specific values (Wind, Falling Leaves Count/Enable/Drop All) ---
        float windValue       = clearWind;
        int   fallAmountValue = 0;
        bool  enableFallValue = false;
        bool  dropAllValue    = false;
        
        // Flag to check if current weather is snow (influences gradient later)
        bool isSnowWeather = (currentWeatherType != null && currentWeatherType.name == "Snow");

        // Calculate weather-dependent properties
        if (currentWeatherType != null)
        {
            switch (currentWeatherType.name)
            {
                case "Clear Sky":
                    // Default values are already set (no wind, no falling)
                    break;

                case "Cloudy 1":
                case "Cloudy 2":
                case "Cloudy 3":
                case "Foggy":
                    windValue       = cloudWind;
                    fallAmountValue = cloudLeaves;
                    enableFallValue = true;
                    break;

                case "Rain":
                    windValue       = rainWind;
                    fallAmountValue = rainLeaves;
                    enableFallValue = true;
                    break;

                case "Snow":
                    windValue       = snowWind;
                    fallAmountValue = snowLeaves;
                    enableFallValue = true;
                    dropAllValue    = true; // Leaves should fall completely in snow
                    break;

                default:
                    Debug.LogWarning($"[TreeVFXWeatherManager] Unhandled weather type: '{currentWeatherType.name}'. Using default clear settings.");
                    break;
            }
        }
        else
        {
            Debug.LogWarning("[TreeVFXWeatherManager] currentWeatherType is null. Applying default clear weather values.");
            // Keep default clear values
        }

        // --- 2. Determine gradients (Leaves Colors & Position Gradient) and Intensity based on weather AND day/night state ---
        Gradient targetLeavesGradient;
        Gradient targetPositionGradient; // NEW variable for Position Gradient
        float    targetIntensity         = _isDay ? dayIntensity : nightIntensity;

        if (isSnowWeather)
        {
            targetLeavesGradient   = snowLeavesGradient;   // Use specific snow gradient for leaves color
            targetPositionGradient = snowPositionGradient; // Use specific snow gradient for position
        }
        else
        {
            // For non-snow weather, use day/night specific gradients
            targetLeavesGradient   = _isDay ? dayLeavesGradient : nightLeavesGradient;
            targetPositionGradient = _isDay ? dayPositionGradient : nightPositionGradient; // NEW
        }

        // --- 3. Apply all determined properties to all found VFX components ---
        foreach (var vfx in _trees)
        {
            if (vfx == null) continue; // Skip if a VFX component was destroyed from the scene

            // Set all properties on the VFX Graph using the exact exposed names
            vfx.SetFloat   ("Wind Intensity",        windValue);
            vfx.SetBool    ("Enable Falling Leaves", enableFallValue);
            vfx.SetInt     ("Falling leaves Amount", fallAmountValue);
            vfx.SetBool    ("Drop all leaves",       dropAllValue);
            
            vfx.SetGradient("Leaves Colors",         targetLeavesGradient);
            vfx.SetGradient("Position Gradient",     targetPositionGradient); // NEW
            vfx.SetFloat   ("Intensity",             targetIntensity);
        }

        Debug.Log($"[TreeVFXWeatherManager] Applied VFX properties: Weather='{(currentWeatherType != null ? currentWeatherType.name : "N/A")}' (Day:{_isDay}) → "
                + $"Wind={windValue}, Leaves={fallAmountValue}, DropAll={dropAllValue}, "
                + $"Leaves_Grad='{(isSnowWeather ? "Snow" : (_isDay ? "Day" : "Night"))}', "
                + $"Pos_Grad='{(isSnowWeather ? "Snow" : (_isDay ? "Day" : "Night"))}', " // Log for position gradient
                + $"Intensity={targetIntensity}");
    }
}