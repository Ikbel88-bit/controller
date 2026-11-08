using UnityEngine;

public class ControllerCompass : MonoBehaviour
{
    private const string OffsetKey = "controller.compass.offsetDeg";

    public bool Available { get; private set; }
    public bool LegacyInputFailed { get; private set; }
    public float RelativeHeading { get; private set; }
    public float DisplayHeading { get; private set; }

    public static int OffsetDegrees
    {
        get => PlayerPrefs.GetInt(OffsetKey, 0);
        set
        {
            PlayerPrefs.SetInt(OffsetKey, ((value % 360) + 360) % 360);
            PlayerPrefs.Save();
        }
    }

    private float reference;
    private bool hasReference;

    private void OnEnable()
    {
        try { Input.compass.enabled = true; }
        catch (System.Exception) { LegacyInputFailed = true; Available = false; }
    }

    private void OnDisable()
    {
        try { Input.compass.enabled = false; }
        catch (System.Exception) { }
    }

    private void Update()
    {
        if (LegacyInputFailed) { Available = false; return; }
        try
        {
            var c = Input.compass;
            Available = c.enabled && c.timestamp > 0 && c.headingAccuracy >= 0f;
            if (!Available) return;

            float h = c.magneticHeading;
            if (!hasReference) { reference = h; hasReference = true; }
            RelativeHeading = Mathf.DeltaAngle(reference, h);
            DisplayHeading = Mathf.Repeat(h + OffsetDegrees, 360f);
        }
        catch (System.Exception)
        {
            LegacyInputFailed = true;
            Available = false;
        }
    }

    public void Recenter()
    {
        if (LegacyInputFailed) return;
        try
        {
            if (!Input.compass.enabled) return;
            reference = Input.compass.magneticHeading;
            hasReference = true;
        }
        catch (System.Exception) { LegacyInputFailed = true; }
    }
}
