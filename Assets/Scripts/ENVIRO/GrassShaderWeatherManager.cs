using UnityEngine;
using Enviro;

public class GrassShaderWeatherManager : MonoBehaviour
{
    [Header("Target Grass Material")]
    [Tooltip("Drag your StylizedGrass Material asset here from your Project window.")]
    public Material grassMaterial;

    [Header("Wind Settings")]
    // Clear Weather Wind
    public float clearWindAmbientStrength = 0.2f;
    public float clearWindSpeed = 3.0f;
    public float clearWindGustStrength = 0.0f;
    public float clearWindGustFreq = 4.0f;
    public float clearWindSwinging = 0.15f;

    // Cloudy Weather Wind (Mid Wind)
    public float cloudWindAmbientStrength = 0.5f;
    public float cloudWindSpeed = 4.0f;
    public float cloudWindGustStrength = 0.3f;
    public float cloudWindGustFreq = 5.0f;
    public float cloudWindSwinging = 0.2f;

    // Rain Weather Wind (Big Wind)
    public float rainWindAmbientStrength = 0.8f;
    public float rainWindSpeed = 6.0f;
    public float rainWindGustStrength = 0.7f;
    public float rainWindGustFreq = 8.0f;
    public float rainWindSwinging = 0.4f;

    // Snow Weather Wind (A bit of wind)
    public float snowWindAmbientStrength = 0.3f;
    public float snowWindSpeed = 3.5f;
    public float snowWindGustStrength = 0.1f;
    public float snowWindGustFreq = 3.0f;
    public float snowWindSwinging = 0.18f;

    [Header("Snow Color Settings")]
    [Tooltip("Base color for grass during snow.")]
    public Color snowBaseColor = Color.white; // e.g., pure white
    [Tooltip("Hue variation color for grass during snow (Alpha is Intensity).")]
    public Color snowHueVariation = new Color(0.9f, 0.9f, 0.9f, 0.05f); // Pale, low intensity

    // Internal: Store original colors to revert from snow
    private Color _originalBaseColor;
    private Color _originalHueVariation;

    // Internal: Property IDs for optimized access to shader properties
    private int _windAmbientStrengthID;
    private int _windSpeedID;
    private int _windGustStrengthID;
    private int _windGustFreqID;
    private int _windSwingingID;
    private int _baseColorID;
    private int _hueVariationID;

    // Store the last known weather type (for potential future combined logic)
    private EnviroWeatherType _lastKnownWeatherType;

    void Awake()
    {
        if (grassMaterial == null)
        {
            Debug.LogError("[GrassShaderWeatherManager] 'Grass Material' is not assigned. Please assign the 'StylizedGrass' Material asset in the Inspector!");
            enabled = false; // Disable script if material is not set
            return;
        }

        // Cache property IDs for performance (done once)
        _windAmbientStrengthID = Shader.PropertyToID("_WindAmbientStrength");
        _windSpeedID           = Shader.PropertyToID("_WindSpeed");
        _windGustStrengthID    = Shader.PropertyToID("_WindGustStrength");
        _windGustFreqID        = Shader.PropertyToID("_WindGustFreq");
        _windSwingingID        = Shader.PropertyToID("_WindSwinging");
        _baseColorID           = Shader.PropertyToID("_BaseColor");
        _hueVariationID        = Shader.PropertyToID("_HueVariation");

        // Cache the original colors from the material at Awake
        // These will be used when reverting from snow to other weather types
        _originalBaseColor     = grassMaterial.GetColor(_baseColorID);
        _originalHueVariation  = grassMaterial.GetColor(_hueVariationID);

        // Set initial last known weather type to null, will be updated on first event
        _lastKnownWeatherType = null;
    }

    void OnEnable()
    {
        // Subscribe to Enviro events when the script is enabled
        if (EnviroManager.instance != null)
        {
            EnviroManager.instance.OnWeatherChanged += OnWeatherChangedHandler;
            Debug.Log("[GrassShaderWeatherManager] Subscribed to Enviro weather events.");
        }
        else
        {
            Debug.LogError("[GrassShaderWeatherManager] EnviroManager.instance not found. Grass effects will not function.");
            enabled = false; // Disable script if Enviro is not available
        }
    }

    void OnDisable()
    {
        // Unsubscribe from Enviro events when the script is disabled
        if (EnviroManager.instance != null)
        {
            EnviroManager.instance.OnWeatherChanged -= OnWeatherChangedHandler;
            Debug.Log("[GrassShaderWeatherManager] Unsubscribed from Enviro weather events.");
        }
    }

    // Handles Enviro's OnWeatherChanged event
    private void OnWeatherChangedHandler(EnviroWeatherType currentWeatherType)
    {
        _lastKnownWeatherType = currentWeatherType; // Store the new weather type

        // Determine weather-specific wind and color values
        float currentWindAmbientStrength = clearWindAmbientStrength;
        float currentWindSpeed           = clearWindSpeed;
        float currentWindGustStrength    = clearWindGustStrength;
        float currentWindGustFreq        = clearWindGustFreq;
        float currentWindSwinging        = clearWindSwinging;
        
        Color currentBaseColor           = _originalBaseColor;
        Color currentHueVariation        = _originalHueVariation;

        if (currentWeatherType != null)
        {
            switch (currentWeatherType.name)
            {
                case "Clear Sky":
                    // Use clear defaults already defined
                    break;

                case "Cloudy 1":
                case "Cloudy 2": 
                case "Cloudy 3":
                case "Foggy":
                    currentWindAmbientStrength = cloudWindAmbientStrength;
                    currentWindSpeed           = cloudWindSpeed;
                    currentWindGustStrength    = cloudWindGustStrength;
                    currentWindGustFreq        = cloudWindGustFreq;
                    currentWindSwinging        = cloudWindSwinging;
                    break;

                case "Rain":
                    currentWindAmbientStrength = rainWindAmbientStrength;
                    currentWindSpeed           = rainWindSpeed;
                    currentWindGustStrength    = rainWindGustStrength;
                    currentWindGustFreq        = rainWindGustFreq;
                    currentWindSwinging        = rainWindSwinging;
                    break;

                case "Snow":
                    currentWindAmbientStrength = snowWindAmbientStrength;
                    currentWindSpeed           = snowWindSpeed;
                    currentWindGustStrength    = snowWindGustStrength;
                    currentWindGustFreq        = snowWindGustFreq;
                    currentWindSwinging        = snowWindSwinging;
                    
                    // Apply snow-specific colors
                    currentBaseColor    = snowBaseColor;
                    currentHueVariation = snowHueVariation;
                    break;

                default:
                    Debug.LogWarning($"[GrassShaderWeatherManager] Unhandled weather type: '{currentWeatherType.name}'. Using default clear settings for grass.");
                    break;
            }
        }
        else
        {
            Debug.LogWarning("[GrassShaderWeatherManager] currentWeatherType is null. Applying default clear weather values for grass.");
        }

        // Apply determined properties to the Material
        grassMaterial.SetFloat(_windAmbientStrengthID, currentWindAmbientStrength);
        grassMaterial.SetFloat(_windSpeedID, currentWindSpeed);
        grassMaterial.SetFloat(_windGustStrengthID, currentWindGustStrength);
        grassMaterial.SetFloat(_windGustFreqID, currentWindGustFreq);
        grassMaterial.SetFloat(_windSwingingID, currentWindSwinging);

        grassMaterial.SetColor(_baseColorID, currentBaseColor);
        grassMaterial.SetColor(_hueVariationID, currentHueVariation);

        Debug.Log($"[GrassShaderWeatherManager] Weather='{(currentWeatherType != null ? currentWeatherType.name : "N/A")}' -> "
                + $"Wind Str={currentWindAmbientStrength}, BaseColor={currentBaseColor}");
    }
}