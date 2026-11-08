using UnityEngine;
using UnityEngine.InputSystem;

public class ControllerProximity : MonoBehaviour
{
    public bool Available { get; private set; }
    public bool Near { get; private set; }

    private ProximitySensor sensor;

    private void OnEnable() { TryEnable(); }

    private void OnDisable()
    {
        if (sensor != null) InputSystem.DisableDevice(sensor);
    }

    private void TryEnable()
    {
        sensor = ProximitySensor.current;
        Available = sensor != null;
        if (Available) InputSystem.EnableDevice(sensor);
    }

    private void Update()
    {
        if (sensor == null) TryEnable();
        // Most phones report ~0 when near and ~5 cm when far.
        Near = Available && sensor.distance.ReadValue() <= 1f;
    }
}
