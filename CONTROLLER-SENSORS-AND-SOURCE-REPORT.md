# RoboClash Phone Controller — Sensors, Controls, and C# Source

## Scope and project

`RoboClashController` is a Unity phone app that sends touchscreen and motion-sensor input to the RoboClash PC game. It builds its runtime interface in C#, reads the device's gyroscope, compass, and optional proximity sensor, and sends protocol-version-1 JSON over TCP.

The phone does not receive or stream the game image. The PC must be reachable over the network, and the player enters the PC's IPv4 address in the controller's connection screen. Both devices should be on the same Wi-Fi network.

## System overview

```text
Touch controls                 Device sensors
  Virtual joystick               Gyroscope
  Action buttons                 Compass
  Gyro clutch                    Proximity
         \                         /
          ControllerInputProvider
                    |
            ControllerInputState
                    |
          ControllerNetworkClient
        TCP / JSON / protocol 1 / ack
                    |
       RoboClashPhoneInputServer (PC)
                    |
       Player movement, combat, and guard
```

Sensor components are added to the controller GameObject at runtime by `ControllerInputProvider` if they are not already present. The screen and touch controls are also created at runtime; they do not require a hand-built UI hierarchy.

## On-screen controls

| UI element | Input behavior |
|---|---|
| Virtual joystick | Up/down provide forward/back movement; left/right provide a horizontal movement/aim axis. The axis is clamped, dead-zone adjusted, and returned to center when released. |
| SHOOT | Held input; remains active while the touch is held. |
| JUMP, ABILITY, ULTIMATE, RELOAD | Press actions. The network client tracks press sequences so a quick tap is not missed between snapshots. |
| CLUTCH | Held gyro clutch. While held, gyro aim output and accumulated angle deltas are cleared. Releasing it recenters the compass reference. |
| RECENTER GYRO | Starts the gyro's 0.75-second software-bias calibration and recenters the compass. |
| Compass strip | A compact, clipped strip near the top-center, with moving five-degree ticks, N/E/S/W, a fixed center marker, and a translucent dark backing. It is hidden when the compass is unavailable. |
| GUARD indicator | Visible while the proximity sensor reports near. The game receives this state as the optional `proximityNear` input. |
| SETTINGS / DEBUG | Opens local sensor/input preferences and optional live diagnostics. |

The interface uses Unity UI (`Canvas`, `RectTransform`, `Image`, and legacy `Text`) and helpers in `ControllerDemoScreen`. `CreatePanel`, `CreateText`, `Place`, `SetAnchoredSize`, and `AddOutline` are shared by the screen elements.

## Sensors

### Gyroscope: primary aiming sensor

`ControllerGyroscope` enables `Input.gyro` when the device supports it and gyro input is enabled. It reads `rotationRateUnbiased`, maps the phone axes for landscape orientation, and converts sensor rates from radians to degrees per second.

At rest, the user can recenter to collect a 0.75-second software bias sample. Each axis is then dead-zone filtered, response-curve shaped, sensitivity-scaled, optionally inverted, and smoothed. The normalized `TurnInput` and `PitchInput` remain available for compatibility. In parallel, processed pre-clamp yaw/pitch rates are integrated with unscaled frame time to accumulate angle deltas in degrees, preventing rotation between network packets from being lost.

The held clutch zeros the outputs and clears accumulated deltas. Releasing it resets the raw yaw integral and recenters the compass. Gyro input and accumulated deltas are also reset while disabled, paused, unfocused, or calibrating.

```csharp
var deltaTime = Time.unscaledDeltaTime;
var yawRateDeg = rawRate - calibratedBias;
var pitchRateDeg = rawPitchRate - calibratedPitchBias;
rawYawDeg += yawRateDeg * deltaTime;

var processedYawRateDegPerSec =
    ProcessRate(yawRateDeg, yawSensitivity, invertYaw) *
    FullRateDegreesPerSecond;
var processedPitchRateDegPerSec =
    ProcessRate(pitchRateDeg, pitchSensitivity, invertPitch) *
    FullRateDegreesPerSecond;

lock (aimDeltaLock)
{
    accumulatedYawDeltaDegrees += processedYawRateDegPerSec * deltaTime;
    accumulatedPitchDeltaDegrees += processedPitchRateDegPerSec * deltaTime;
}
```

`ConsumeAimDelta` reads and clears the accumulated values under a lock. The network thread does not call Unity APIs to sample the gyroscope: the Unity main thread publishes input snapshots, and the main-thread provider acknowledges consumed deltas only after the network packet receives an acknowledgement.

### Compass: heading display and optional gyro drift assistance

`ControllerCompass` enables `Input.compass` and reports availability only when the device is enabled, has a timestamp, and reports nonnegative heading accuracy. `RelativeHeading` is the magnetic heading relative to the last recenter, wrapped to -180..180 degrees.

The gyro's optional compass assist is enabled by default and can be turned off in Settings. When the phone is relatively still (bias-corrected yaw and pitch rates each under 8 degrees/second), the gyro compares its integrated yaw against the relative compass heading. It rejects large magnetic jumps by clamping the error to ±10 degrees, gently corrects the yaw integral, and slowly adjusts the yaw bias. Compass heading is used for this correction; it does not directly command PC robot yaw.

The on-screen compass strip is intentionally different: it uses the absolute `Input.compass.magneticHeading`, not `RelativeHeading`. A fixed center mark points to the current phone heading while cardinal labels and ticks move beneath it.

### Proximity: optional held guard input

`ControllerProximity` uses Unity's Input System `ProximitySensor.current`. It reports `Near` when sensor distance is at most 1 cm (typical phone readings are around 0 cm near and 5 cm far). The controller sends this as a held boolean. If the device has no proximity sensor, `Available` remains false and the input defaults to false.

### Sensor source code

The following are the complete sensor component implementations used by the app.

[`ControllerCompass.cs`](./Assets/Scripts/ControllerCompass.cs):

```csharp
using UnityEngine;

public class ControllerCompass : MonoBehaviour
{
    public bool Available { get; private set; }
    public float RelativeHeading { get; private set; }   // degrees, 0 at last recenter

    private float reference;
    private bool hasReference;

    private void OnEnable()  { Input.compass.enabled = true; }
    private void OnDisable() { Input.compass.enabled = false; }

    private void Update()
    {
        var c = Input.compass;
        Available = c.enabled && c.timestamp > 0 && c.headingAccuracy >= 0f;
        if (!Available) return;

        float h = c.magneticHeading;
        if (!hasReference) { reference = h; hasReference = true; }
        RelativeHeading = Mathf.DeltaAngle(reference, h);
    }

    public void Recenter()
    {
        if (!Input.compass.enabled) return;
        reference = Input.compass.magneticHeading;
        hasReference = true;
    }
}
```

[`ControllerProximity.cs`](./Assets/Scripts/ControllerProximity.cs):

```csharp
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
```

## Input model and state flow

`ControllerInputState` is a value-type snapshot. The provider gathers joystick axes, held buttons, normalized gyro output, accumulated gyro angle deltas, compass state, and proximity state. Its `ApproximatelyEqual` comparison drives `InputChanged`; the network client also polls a thread-safe snapshot to avoid relying on Unity events from its background thread.

```csharp
[Serializable]
public struct ControllerInputState
{
    public float MoveForward;
    public float AimHorizontal;
    public bool Shoot;
    public bool Jump;
    public bool Ability;
    public bool Ultimate;
    public bool Reload;
    public float GyroTurn;
    public float GyroPitch;
    public float GyroYawDelta;
    public float GyroPitchDelta;
    public bool ProximityNear;
    public bool CompassValid;
    public float CompassHeading;
}
```

The provider's sensor state is:

```csharp
ProximityNear = proximitySensor != null &&
                proximitySensor.Available && proximitySensor.Near,
CompassValid = compass != null && compass.Available,
CompassHeading = compass != null && compass.Available
    ? compass.RelativeHeading
    : 0f
```

The network snapshot carries the relative compass heading. The compass strip independently reads absolute magnetic heading for orientation.

## Network protocol and reliability

The phone connects to the PC's IPv4 address on TCP port **17862**. Both devices need network connectivity between them (normally the same Wi-Fi). The client validates IPv4 input, connects with a bounded timeout, sends the protocol-1 `hello`, and waits for `welcome`. A connection is considered active only after this handshake.

After connecting, the client sends JSON input lines and waits for `ack` for every line. Following each successful acknowledgement, it removes the gyro delta included in that packet from the provider's accumulator. A failed write/read/ack does not consume that delta. The send loop waits 16 ms after each acknowledgement; actual update cadence also depends on network round-trip time. Disconnection resets input state and exposes a connection status to the UI.

Optional compass/proximity fields are appended while protocol remains version 1. Older game servers can ignore the fields; game receivers treat missing optional fields as their default values.

The serializer uses invariant culture for decimal points, round-trip float formatting for transmitted values, booleans as lowercase JSON literals, and replaces non-finite floats with zero:

```csharp
return string.Format(CultureInfo.InvariantCulture,
    "{{\"type\":\"input\",\"protocol\":1,\"moveForward\":{0:R}," +
    "\"aimHorizontal\":{1:R},\"gyroTurn\":{2:R},\"gyroPitch\":{3:R}," +
    "\"gyroYawDelta\":{4:R},\"gyroPitchDelta\":{5:R}," +
    "\"shoot\":{6},\"jump\":{7},\"reload\":{8},\"ability\":{9},\"ultimate\":{10}," +
    "\"proximityNear\":{11},\"compassValid\":{12},\"compassHeading\":{13:R}}}",
    FiniteOrZero(input.MoveForward), FiniteOrZero(input.AimHorizontal),
    FiniteOrZero(input.GyroTurn), FiniteOrZero(input.GyroPitch),
    FiniteOrZeroUnclamped(input.GyroYawDelta),
    FiniteOrZeroUnclamped(input.GyroPitchDelta),
    Lower(input.Shoot), Lower(input.Jump || jumpPressed),
    Lower(input.Reload || reloadPressed), Lower(input.Ability || abilityPressed),
    Lower(input.Ultimate || ultimatePressed), Lower(input.ProximityNear),
    Lower(input.CompassValid), FiniteOrZeroUnclamped(input.CompassHeading));
```

The full connection lifecycle, acknowledgement loop, and serializer are in [`ControllerNetworkClient.cs`](./Assets/Scripts/ControllerNetworkClient.cs). The PC-side listener is [`RoboClashPhoneInputServer.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashPhoneInputServer.cs).

## Settings and diagnostics

Local preferences use Unity `PlayerPrefs`; the UI applies them at startup and saves changes. Current defaults and bounds are:

| Preference | Default | Range / notes |
|---|---:|---|
| Gyroscope enabled | On | Device must support a gyroscope. |
| Yaw sensitivity | 2.5× | 0.1–8× |
| Pitch sensitivity | 1.8× | 0.1–8× |
| Angular dead zone | 1.5°/s | 0–10°/s |
| Response curve | 1.5 | Exponent 1–2.5 |
| Smoothing | 20 | 1–30 |
| Invert yaw / pitch | Off / Off | Independent toggles |
| Compass assist | On | Affects stationary gyro drift correction. |
| Joystick dead zone | 0.12 | 0–0.5 |
| Input diagnostics | Off | Can be toggled in Settings or Debug. |

The diagnostics display gyro rates, normalized movement/aim values, compass-relative heading, gyro compass drift error, compass-assist state, proximity availability and near/far state, and button status. Missing sensors display as unavailable rather than being assumed present.

## Compass strip implementation

The strip uses only Unity UI elements and the screen's existing helpers. It creates its tick and cardinal-label objects once, then only moves their `RectTransform`s during updates:

```csharp
private void UpdateCompassStrip()
{
    if (compassStrip == null)
        return;

    var available = compass != null && compass.Available;
    if (compassStrip.activeSelf != available)
        compassStrip.SetActive(available);
    if (!available)
        return;

    var heading = Mathf.Repeat(Input.compass.magneticHeading, 360f);
    for (var i = 0; i < compassTickMarks.Length; i++)
    {
        var position = compassTickMarks[i].anchoredPosition;
        position.x = Mathf.DeltaAngle(heading, compassTickBearings[i]) *
                     CompassPixelsPerDegree;
        compassTickMarks[i].anchoredPosition = position;
    }

    for (var i = 0; i < compassCardinalLabels.Length; i++)
    {
        var position = compassCardinalLabels[i].anchoredPosition;
        position.x = Mathf.DeltaAngle(heading, CompassCardinalBearings[i]) *
                     CompassPixelsPerDegree;
        compassCardinalLabels[i].anchoredPosition = position;
    }
}
```

The strip is roughly 60% of the 1920-reference-width canvas and 28 pixels high, anchored top-center within the safe-area root. Its `RectMask2D` clips offscreen bearings, and its center marker never moves. See [`ControllerDemoScreen.cs`](./Assets/Scripts/ControllerDemoScreen.cs) for the complete UI builder and input wiring.

## Project dependency

The Unity Input System package is declared in [`Packages/manifest.json`](./Packages/manifest.json) as `com.unity.inputsystem` version `1.20.0`. No additional packages were added for the controller or compass strip.

## Complete source inventory

These are the controller's runtime C# modules:

- [`ControllerDemoScreen.cs`](./Assets/Scripts/ControllerDemoScreen.cs) — builds the UI, settings, compass strip, connection screen, and diagnostics.
- [`ControllerInputProvider.cs`](./Assets/Scripts/ControllerInputProvider.cs) — ensures sensors exist, combines inputs, and publishes snapshots.
- [`ControllerInputState.cs`](./Assets/Scripts/ControllerInputState.cs) — input fields, action enum, and local preferences.
- [`ControllerVirtualJoystick.cs`](./Assets/Scripts/ControllerVirtualJoystick.cs) — pointer handling, axis normalization, dead zone, and recenter on release.
- [`ControllerActionButton.cs`](./Assets/Scripts/ControllerActionButton.cs) — touch down/up/exit handling and held versus press actions.
- [`ControllerGyroscope.cs`](./Assets/Scripts/ControllerGyroscope.cs) — calibration, filtering, clutch, compass drift assist, and accumulated aim deltas.
- [`ControllerCompass.cs`](./Assets/Scripts/ControllerCompass.cs) — magnetic sensor availability, relative heading, and recenter.
- [`ControllerProximity.cs`](./Assets/Scripts/ControllerProximity.cs) — optional Input System proximity sensor and held near state.
- [`ControllerNetworkClient.cs`](./Assets/Scripts/ControllerNetworkClient.cs) — TCP client, handshake, JSON snapshots, ack handling, and disconnect state.

## Verification and device limitations

Unity 6000.6.4f1 batch compilation completed successfully after the compass-strip UI change, producing `Assembly-CSharp.dll` without C# errors or warnings. The sensor scripts also compiled in Unity. Availability and heading accuracy depend on the Android device; no physical-device sensor measurements or live network session are implied by a successful compile.
