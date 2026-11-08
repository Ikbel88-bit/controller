# RoboClash Phone Controller — Technical Report

> **Current report:** See [CONTROLLER-SENSORS-AND-SOURCE-REPORT.md](./CONTROLLER-SENSORS-AND-SOURCE-REPORT.md) for the current Wi-Fi connection flow, sensors, compass strip, settings, and source references. This older report contains historical USB/ADB and 50 ms transport details that no longer describe the controller.

## What the controller is

RoboClashController is a Unity Android app that turns the phone into a controller for RoboClash running on the PC. The current connection mode uses a USB cable with Android Debug Bridge (ADB) reverse forwarding; the app sends touch and gyroscope input to the PC over a forwarded TCP connection.

The controller does not stream the game to the phone. It only sends control input.

## Controls

| Phone input | Game action |
| --- | --- |
| Virtual joystick up/down | Move forward/backward |
| Virtual joystick left/right | Turn/aim horizontally |
| Phone gyroscope | Turn left/right |
| Shoot button | Fire while held |
| Jump button | Jump once per press |
| Reload button | Reload once per press |
| Ability button | Use active ability once per press |
| Ultimate button | Use ultimate once per press |

The PC game applies the joystick and gyro turning to its existing robot movement/camera architecture. The normal keyboard and mouse path remains available whenever a phone is not connected.

## How connection works

1. RoboClash starts a persistent TCP listener on port **17862** after the first scene loads. `RoboClashMainMenu` is the first enabled build scene, so the listener is available at the main menu as well as during gameplay.
2. Connect the phone to the PC by USB, enable Android USB debugging, and approve the debugging prompt.
3. On the PC, run `adb reverse tcp:17862 tcp:17862` (or invoke `adb.exe` by its full path if it is not on `PATH`).
4. Tap **CONNECT VIA USB** in the phone app. It connects to `127.0.0.1:17862`; no PC IP entry is needed. The client now accepts loopback by choosing it internally.
5. The phone and PC perform a protocol-version-1 handshake. A running game server alone does not activate phone controls.
6. Once accepted, the phone sends a JSON snapshot every 50 ms and waits for the PC's `ack` response.
7. If the connection closes or the PC stops receiving input for 0.75 seconds, the game clears phone input and returns control to PC keyboard/mouse input. The match and player state are not reset.

Each phone runs its own controller client and maintains its own TCP connection. The current PC receiver accepts up to four simultaneous clients and assigns each to a player slot in connection order; connecting another phone does not replace an existing client. The app has a five-second connection timeout. Both projects use TCP rather than WebSockets.

## Input path

```text
Touchscreen + phone gyroscope
            |
            v
ControllerVirtualJoystick / ControllerActionButton / ControllerGyroscope
            |
            v
ControllerInputProvider.CurrentInput
            |
            v
ControllerNetworkClient (TCP, JSON snapshots)
            |
            v
RoboClashPhoneInputServer (TCP listener, validates packets)
            |
            v
Existing robot movement, combat, and abilities
```

The controller UI is built by `ControllerDemoScreen` at runtime. It creates the joystick, action buttons, USB connect button, status display, settings, and diagnostics UI. The Unity scene contains the controller root; it does not need a manually wired set of UI controls.

## Key code

### 1. Input data sent by the phone

From [`ControllerInputState.cs`](./Assets/Scripts/ControllerInputState.cs):

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
}
```

The joystick's vertical axis becomes movement; its horizontal axis becomes aim. The input provider combines those values with button and gyro state:

```csharp
var state = new ControllerInputState
{
    MoveForward = joystick != null ? joystick.MoveForward : 0f,
    AimHorizontal = joystick != null ? joystick.AimHorizontal : 0f,
    Shoot = shootButton != null && shootButton.IsPressed,
    Jump = jumpButton != null && jumpButton.IsPressed,
    Ability = abilityButton != null && abilityButton.IsPressed,
    Ultimate = ultimateButton != null && ultimateButton.IsPressed,
    Reload = reloadButton != null && reloadButton.IsPressed,
    GyroTurn = gyroscope != null ? gyroscope.TurnInput : 0f
};
```

Full implementation: [`ControllerInputProvider.cs`](./Assets/Scripts/ControllerInputProvider.cs).

### 2. Joystick dead zone and normalized input

The virtual joystick clamps input to a normalized range, ignores small movements inside its dead zone, and scales the remaining movement:

```csharp
value = Vector2.ClampMagnitude(value, 1f);
if (value.magnitude < deadZone)
    value = Vector2.zero;
else if (deadZone > 0f)
    value = value.normalized * Mathf.InverseLerp(deadZone, 1f, value.magnitude);
```

Full implementation: [`ControllerVirtualJoystick.cs`](./Assets/Scripts/ControllerVirtualJoystick.cs).

### 3. Gyroscope processing

The gyro reads the phone's unbiased rotation rate, removes the calibrated resting bias and dead zone, applies sensitivity/inversion, then smooths the result:

```csharp
var rawRate = GetLandscapeYawRate(Input.gyro.rotationRateUnbiased) * Mathf.Rad2Deg;
var adjustedRate = rawRate - calibratedBias;
var magnitude = Mathf.Abs(adjustedRate);
var normalizedMagnitude = magnitude <= angularDeadZone
    ? 0f
    : (magnitude - angularDeadZone) /
      Mathf.Max(1f, FullTurnRateDegreesPerSecond - angularDeadZone);
var target = Mathf.Clamp(
    normalizedMagnitude * Mathf.Sign(adjustedRate) * sensitivity, -1f, 1f);
if (invertDirection)
    target = -target;
SmoothToward(target);
```

The app provides gyro enable, sensitivity, dead-zone, smoothing, inversion, and recenter settings. The gyroscope is optional; the joystick still provides horizontal turning.

Full implementation: [`ControllerGyroscope.cs`](./Assets/Scripts/ControllerGyroscope.cs).

### 4. Phone connection and handshake

The client connects to `IPAddress.Loopback` on TCP port 17862. The user does not enter an address; ADB reverse must already be configured. The client requires the game to reply with the expected protocol handshake before showing the controller as connected:

```csharp
private const int Port = 17862;
private const string HelloMessage = "{\"type\":\"hello\",\"protocol\":1}";
private const string WelcomeMessage = "{\"type\":\"welcome\",\"protocol\":1}";
private const string Acknowledgement = "ack";

WriteLine(stream, HelloMessage);
if (!string.Equals(ReadLine(stream), WelcomeMessage, StringComparison.Ordinal))
    throw new InvalidOperationException("The PC did not accept this controller protocol.");

connected = true;
status = "PHONE CONNECTED";
```

After the handshake, the client sends input and requires an acknowledgement for each snapshot:

```csharp
WriteLine(stream, FormatInput(input,
    jumpSequence != sentJumpSequence,
    reloadSequence != sentReloadSequence,
    abilitySequence != sentAbilitySequence,
    ultimateSequence != sentUltimateSequence));

if (!string.Equals(ReadLine(stream), Acknowledgement, StringComparison.Ordinal))
    throw new InvalidOperationException("The game PC stopped acknowledging controller input.");

Thread.Sleep(50);
```

`FormatInput` serializes movement axes, gyro, and held button values as a versioned JSON line. Jump, reload, ability, and ultimate presses also use press-sequence counters so a quick tap is not lost between snapshots.

Full implementation: [`ControllerNetworkClient.cs`](./Assets/Scripts/ControllerNetworkClient.cs).

### 5. Game-side validation and press edges

The PC validates the message type and protocol, rejects non-finite axis values, clamps axes, and turns action buttons into one-shot presses:

```csharp
if (message == null || message.type != "input" || message.protocol != ProtocolVersion ||
    !IsFinite(message.moveForward) || !IsFinite(message.aimHorizontal) ||
    !IsFinite(message.gyroTurn))
    return false;

CurrentInput = new RoboClashPhoneInput
{
    MoveForward = Mathf.Clamp(message.moveForward, -1f, 1f),
    AimHorizontal = Mathf.Clamp(message.aimHorizontal, -1f, 1f),
    GyroTurn = Mathf.Clamp(message.gyroTurn, -1f, 1f),
    Shoot = message.shoot
};

jumpPressed |= message.jump && !lastJump;
reloadPressed |= message.reload && !lastReload;
abilityPressed |= message.ability && !lastAbility;
ultimatePressed |= message.ultimate && !lastUltimate;
```

The listener thread handles network reads and queues packet strings. Unity's main thread validates and applies those packets in `Update`, keeping gameplay changes on Unity's main thread.

Full implementation in the game project: [`RoboClashPhoneInputServer.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashPhoneInputServer.cs).

### 6. Android network permission and launcher note

The previously inspected APK included `android.permission.INTERNET`, which the TCP client needs. The APK inspected during the earlier icon troubleshooting had no launchable activity. The current controller project directory has no `Assets/Plugins/Android/AndroidManifest.xml` source file, so do not treat a custom-manifest code sample as proof that a newly built APK has the launcher entry. Verify the generated APK's merged manifest after rebuilding; it should contain a Unity launcher activity with the `MAIN` and `LAUNCHER` intent filter.

## Source files

Controller app source:

- [`ControllerDemoScreen.cs`](./Assets/Scripts/ControllerDemoScreen.cs) — creates the controller interface and handles connect/disconnect UI.
- [`ControllerNetworkClient.cs`](./Assets/Scripts/ControllerNetworkClient.cs) — TCP connection, handshake, JSON input snapshots, acknowledgement, and reconnect lifecycle.
- [`ControllerInputProvider.cs`](./Assets/Scripts/ControllerInputProvider.cs) — samples and combines joystick, gyro, and button input.
- [`ControllerInputState.cs`](./Assets/Scripts/ControllerInputState.cs) — input data structure, action enum, and saved phone settings.
- [`ControllerVirtualJoystick.cs`](./Assets/Scripts/ControllerVirtualJoystick.cs) — touch joystick, axis normalization, and dead zone.
- [`ControllerGyroscope.cs`](./Assets/Scripts/ControllerGyroscope.cs) — gyro calibration and smoothed turning input.
- [`ControllerActionButton.cs`](./Assets/Scripts/ControllerActionButton.cs) — touch button press/release behavior.
- [`RoboClashControllerGenerator.cs`](./Assets/Editor/RoboClashControllerGenerator.cs) — Editor tool that builds the standalone controller scene.

Game integration source:

- [`RoboClashPhoneInputServer.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashPhoneInputServer.cs) — PC-side TCP host and input validation.
- [`RoboClashRobotMovement.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashRobotMovement.cs) — routes phone movement and turning while preserving PC controls.
- [`RoboClashRobotCombat.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashRobotCombat.cs) — phone fire and reload routing.
- [`RoboClashRobotAbilities.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashRobotAbilities.cs) — one-shot phone ability and ultimate actions.
- [`RoboClashHUD.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashHUD.cs) — displays phone connection status and active input mode.
- [`RoboClashMenuController.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashMenuController.cs) — creates and refreshes the menu connection-status text at runtime.
- [`RoboClashMatchManager.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashMatchManager.cs) — no longer creates a second gameplay-only phone server.

## Connection requirements and limits

- The controller server starts after the first game scene loads; the phone can connect while the main menu is open or during a match.
- Connect the phone to the PC by USB, enable USB debugging, and run `adb reverse tcp:17862 tcp:17862` on the PC. The phone app connects to `127.0.0.1` automatically.
- The port is fixed at **17862**. ADB reverse rules are cleared when the device disconnects or the ADB session ends, so rerun the command if needed.
- Wi-Fi IP entry is not provided by the current controller UI; using a different transport requires changing the client again.
- The server has one active controller slot at a time; a newly accepted connection replaces the previous client.
- The app explicitly requests Android Internet permission.

## Verification status

The controller and game C# assemblies were previously built successfully with `dotnet build`. The APK that was installed before the launcher troubleshooting was inspected and had no launchable Android activity, explaining why Android could update the package without showing an app icon. A new Unity Android APK and phone installation are needed to verify launchability. A real phone-to-PC connection and in-match Play Mode tests have **not** been verified. A successful C# build does not prove that a phone can connect over the user's network.

## Latest RoboClash server and automatic-input adaptation

This section documents the RoboClash-side changes made on **2026-10-25** for independent code review. The complete, authoritative implementation is in the linked C# files; excerpts below are included to make the intended behavior easy to compare with source.

### Server startup and lifetime

The server is bootstrapped once after scene loading, rather than being added by the gameplay match manager. It is hidden in the Hierarchy and marked persistent across scene changes. The subsystem callback clears the static singleton when Unity starts a new play session.

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
private static void ResetRuntimeState()
{
    Active = null;
}

[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
private static void BootstrapServer()
{
    var server = FindAnyObjectByType<RoboClashPhoneInputServer>(
        FindObjectsInactive.Include);
    if (server == null)
    {
        var serverObject = new GameObject("RoboClash Phone Input Server")
        {
            hideFlags = HideFlags.HideInHierarchy
        };
        DontDestroyOnLoad(serverObject);
        server = serverObject.AddComponent<RoboClashPhoneInputServer>();
    }
    else
    {
        server.gameObject.name = "RoboClash Phone Input Server";
        server.gameObject.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(server.gameObject);
        if (!server.gameObject.activeSelf)
            server.gameObject.SetActive(true);
        if (!server.enabled)
            server.enabled = true;
    }
}
```

Binding uses the requested address reuse option and logs the listening endpoint after startup:

```csharp
listener = new TcpListener(IPAddress.Any, Port);
listener.Server.SetSocketOption(
    SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
listener.Start();
listenerThread = new Thread(ListenForController)
{
    IsBackground = true,
    Name = "RoboClash Phone Controller Listener"
};
listenerThread.Start();
Debug.Log(
    "Phone input server listening on 0.0.0.0:17862. Enter <ip> on the phone",
    this);
```

If listener startup throws a socket, argument, or invalid-operation exception, it logs the exception and invokes shutdown instead of silently failing.

### Legacy PC address selection helper

`GetBestLocalIPv4()` remains available in the game for diagnostics and optional network integrations. The current controller client no longer reads or requests this address; it uses loopback over ADB reverse.

```csharp
if (network.OperationalStatus != OperationalStatus.Up ||
    (network.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 &&
     network.NetworkInterfaceType != NetworkInterfaceType.Ethernet) ||
    IsExcludedNetwork(network) ||
    !HasDefaultGateway(network))
    continue;

var address = GetIPv4Address(network);
if (address != null)
    return address;
```

The selected local IPv4 is cached for five seconds to avoid repeatedly enumerating interfaces while the menu and gameplay HUD refresh their status text. The startup log includes this address:

```csharp
var localAddress = GetBestLocalIPv4();
Debug.Log(
    $"Phone input server listening on 0.0.0.0:17862. Enter {localAddress} on the phone",
    this);
```

The static properties used by the menu and gameplay HUD are:

```csharp
public static bool IsPhoneActive =>
    Active != null &&
    Active.connected &&
    Active.hasValidInput &&
    Time.realtimeSinceStartup - Active.lastInputTime <= InputTimeoutSeconds;

public static string StatusText => IsPhoneActive
    ? "PHONE: CONNECTED"
    : $"PHONE: WAITING  {GetBestLocalIPv4()}:{Port}";
```

The server does **not** mark the phone active solely because a TCP client connected or completed the handshake. The Unity main thread marks `hasValidInput` only after a packet passes the existing protocol/type and finite-axis validation, and updates the receive time. If valid input is absent for 0.75 seconds, input is cleared and PC controls become active.

### Reconnect and threading

The accept loop runs on a background listener thread. For every accepted TCP client it sets `NoDelay`, advances a connection generation, replaces/closes the prior client, clears queued old packets, and starts a separate background thread for handshake and reads. Generation tags prevent a stale client thread or packet queue from overriding a newer connection. Packet validation and gameplay input state updates remain in Unity's `Update()` on the main thread.

The existing protocol-1 `hello` / `welcome` handshake, JSON packet validation, clamped axes, and rising-edge action presses (jump/reload/ability/ultimate) are retained. Full accept, replacement, and handshake code: [`RoboClashPhoneInputServer.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashPhoneInputServer.cs).

Shutdown is wired through `OnDisable`, `OnDestroy`, `OnApplicationQuit`, and `Application.quitting`. It closes the active socket, stops the listener, joins the client/listener threads (with a bounded wait), clears queued messages and resets phone input.

### PC fallback and combined controls

Movement reads phone forward/backward input only while `IsPhoneActive`; the phone joystick's horizontal axis and gyro affect yaw, not strafing. Keyboard axes are still added as backup and the combined movement vector is normalized. Mouse look remains available while the cursor is locked. `ConsumePressed(Jump)` consumes the phone's rising-edge jump action once, and that one-frame flag is passed to `ApplyMovement`; keyboard Space remains available too. Keyboard Escape continues to be read.

```csharp
var phoneServer = RoboClashPhoneInputServer.Active;
var phoneInputActive = RoboClashPhoneInputServer.IsPhoneActive;
var phoneJumpPressed = phoneInputActive &&
    phoneServer.ConsumePressed(RoboClashPhoneAction.Jump);
var input = Vector2.zero;

if (phoneInputActive)
{
    yaw += (phoneServer.CurrentInput.GyroTurn * 140f +
            phoneServer.CurrentInput.AimHorizontal * 90f) * Time.deltaTime;
    input.y = phoneServer.CurrentInput.MoveForward;
}

if (Keyboard.current != null)
{
    if (Keyboard.current.wKey.isPressed) input.y += 1f;
    if (Keyboard.current.sKey.isPressed) input.y -= 1f;
    if (Keyboard.current.dKey.isPressed) input.x += 1f;
    if (Keyboard.current.aKey.isPressed) input.x -= 1f;
}

var moveInput = new Vector3(input.x, 0f, input.y);
if (moveInput.sqrMagnitude > 1f)
    moveInput.Normalize();

var jumpPressed = phoneJumpPressed ||
    Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
ApplyMovement(desired, jumpPressed);
```

Combat checks phone firing/reload separately from the existing mouse path. The mouse cursor-lock condition applies only to mouse fire; phone fire and reload do not require a locked cursor. PC mouse fire and `R` reload remain available while phone input is active.

```csharp
if (phoneInputActive)
{
    if (phoneServer.CurrentInput.Shoot)
        Fire();
    if (phoneServer.ConsumePressed(RoboClashPhoneAction.Reload))
        Reload();
}

if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked &&
    Mouse.current.leftButton.isPressed)
    Fire();

if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
    Reload();
```

Phone ability/ultimate presses are consumed as one-shot actions; the existing Q/E keyboard checks remain active. Existing cooldown and gameplay validation methods are still used by the ability calls.

### Menu, gameplay status, and scene guards

`RoboClashMenuController` creates a small text object at runtime on its existing menu canvas, anchored at the bottom-right. It refreshes the shared server `StatusText` and active/inactive color every 0.5 seconds. The gameplay HUD uses the same text/property and refresh interval. No new serialized scene object is required.

The old `RoboClashMatchManager` code that added a phone server to the gameplay object was removed to prevent creating a second server. The existing main-menu logic that removes misplaced gameplay managers/HUDs and the existing gameplay-scene guards were not changed.

### Build and test results for this adaptation

- `dotnet build Assembly-CSharp.csproj --no-restore` — succeeded after these changes, zero warnings and zero errors.
- `dotnet build Assembly-CSharp-Editor.csproj --no-restore` — succeeded after these changes, zero warnings and zero errors.
- A real phone reconnect test, Unity Play Mode test, and live scene-transition/shutdown test were **not** run. The code compilation result is not evidence that the PC and phone successfully communicate over ADB reverse.

### Files changed for this adaptation

- [`RoboClashPhoneInputServer.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashPhoneInputServer.cs)
- [`RoboClashRobotMovement.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashRobotMovement.cs)
- [`RoboClashRobotCombat.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashRobotCombat.cs)
- [`RoboClashRobotAbilities.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashRobotAbilities.cs)
- [`RoboClashHUD.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashHUD.cs)
- [`RoboClashMenuController.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashMenuController.cs)
- [`RoboClashMatchManager.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashMatchManager.cs) (removed obsolete gameplay-only server creation)

## Gameplay HUD: health, shield, abilities, and tactical map

This section describes the gameplay-only HUD in RoboClash, based on the current source. Its runtime UI is built by `RoboClashHUD`; the HUD validates that it belongs to `Assets/Scenes/RoboClashGameplay.unity` before creating gameplay UI. It is not a HUD for the main menu.

### Health and shield (not mana)

The current game does **not** define or display a generic mana pool. The two player vitals shown at the lower-left are **Health** and **Shield**. Shield absorbs incoming damage before health, and both values are supplied by `ArenaDamageable`. The HUD displays each as current/maximum values and a normalized fill bar.

```csharp
healthText.text =
    $"HEALTH  {Mathf.CeilToInt(playerDamageable.Health)} / " +
    $"{Mathf.CeilToInt(playerDamageable.MaximumHealth)}";
shieldText.text =
    $"SHIELD  {Mathf.CeilToInt(playerDamageable.Shield)} / " +
    $"{Mathf.CeilToInt(playerDamageable.MaximumShield)}";
```

The bars use clamped current-to-maximum ratios. Health is green normally and red at 25% or lower; reaching low health also displays the low-health warning. The fill movement is smoothed each frame, while vital text and target ratios refresh when the damageable raises `VitalsChanged`.

```csharp
var healthRatio = playerDamageable.MaximumHealth > 0f
    ? Mathf.Clamp01(playerDamageable.Health / playerDamageable.MaximumHealth)
    : 0f;
var shieldRatio = playerDamageable.MaximumShield > 0f
    ? Mathf.Clamp01(playerDamageable.Shield / playerDamageable.MaximumShield)
    : 0f;
healthFill.color = healthRatio <= 0.25f ? Red : Green;
```

`ArenaDamageable.ApplyDamage` subtracts damage from shield first, then health, and raises the vitals event. Shielding/healing methods also raise the event when the value changes. The HUD subscribes while the player is bound and unsubscribes when that player is unbound.

### Ability cooldown and ultimate charge

The lower-center ability panel has two slots:

- **Ability (Q):** shows the selected robot's ability name and `READY`, `LOCKED`, `ACTIVE <seconds>`, `OFFLINE`, or remaining cooldown seconds. Its bar fills as its cooldown elapses, and reflects active duration while the ability is active.
- **Ultimate (E):** shows the selected robot's ultimate name and state. Depending on the robot, this includes remaining cooldown, charge percentage, active time, `READY`, `LOCKED`, or `OFFLINE`. Its bar represents charge when charge is required, otherwise cooldown progress; while an ultimate with a duration is active, it shows remaining active duration.

The cooldown/charge state is read from `RoboClashRobotAbilities` and the robot's `RoboClashRobotDefinition`. `IsUltimateReady` requires the cooldown to be complete, the required charge to be met (when configured), the ability to be unlocked, and the player to be alive.

```csharp
var abilityReady = playerAbilities.IsAbilityReady;
var abilityActive = playerAbilities.IsAbilityActive;
var abilityState = playerDamageable != null && playerDamageable.IsDead
    ? "OFFLINE"
    : playerAbilities.IsAbilityLocked
        ? "LOCKED"
        : abilityActive
            ? $"ACTIVE {playerAbilities.AbilityActiveRemaining:0.0}s"
            : abilityReady ? "READY"
            : $"{playerAbilities.AbilityCooldownRemaining:0.0}s";

var charge = playerAbilities.UltimateChargeRequired > 0f
    ? Mathf.Clamp01(
        playerAbilities.UltimateCharge / playerAbilities.UltimateChargeRequired) * 100f
    : 100f;
```

When a cooldown or charge state changes, the abilities component raises `StateChanged` and the HUD immediately refreshes the display. The HUD also refreshes its status on its regular UI update loop. `UltimateCharge` is an ultimate-specific charge meter, **not mana**. The combat code adds charge from damage dealt according to each robot's `ultimateChargePerDamage`; activating a charged ultimate consumes the charge. Ability and ultimate cooldown/readiness rules remain in `RoboClashRobotAbilities`.

### Tactical minimap

The minimap appears at the upper-right. It is drawn by the custom UI graphic `RoboClashMinimap` rather than by a camera or a pre-painted texture.

At configuration, it scans colliders under the match manager's arena root. It finds the floor bounds (falling back to other suitable non-trigger colliders if there is no collider tagged with `ArenaFloor`), uses those bounds to set the map center and scale, and records collider bounds for static arena geometry. Player colliders and trigger colliders are excluded from the geometry. Walls, hazards, and other obstacles receive different colors.

```csharp
var isHazard = arenaCollider.GetComponentInParent<ArenaHazard>() != null;
var isWall = arenaCollider.GetComponentInParent<ArenaWall>() != null;
shapes.Add(new MapShape
{
    Bounds = bounds,
    Color = isHazard ? HazardColor : isWall ? WallColor : ObstacleColor
});
```

The player's cyan triangle is positioned from the player's world X/Z coordinates and rotates opposite the robot's yaw to show facing. It is hidden if map data is unavailable, the player object is inactive, or the player is dead.

```csharp
var worldPosition = player.transform.position;
var localPosition = WorldToMapLocal(
    new Vector2(worldPosition.x, worldPosition.z), scale);
marker.anchoredPosition = new Vector2(
    Mathf.Clamp(localPosition.x, rect.xMin, rect.xMax),
    Mathf.Clamp(localPosition.y, rect.yMin, rect.yMax));
marker.localRotation =
    Quaternion.Euler(0f, 0f, -player.transform.eulerAngles.y);
```

The current minimap shows static arena geometry and the local player's marker. It does **not** currently draw enemy markers, teammate markers, pickups, or a mana indicator. If it cannot obtain valid arena bounds, the HUD reports `MAP UNAVAILABLE`.

### Relevant source files

- [`RoboClashHUD.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashHUD.cs) — creates the gameplay HUD, binds to the player, updates vitals/ability displays, and configures the map.
- [`RoboClashMinimap.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashMinimap.cs) — extracts arena geometry and draws the player marker.
- [`ArenaDamageable.cs`](../RoboClash/Assets/Scripts/RoboClash/ArenaDamageable.cs) — health/shield values, damage ordering, healing, and vitals events.
- [`RoboClashRobotAbilities.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashRobotAbilities.cs) — cooldowns, ability/ultimate state, readiness, and ultimate charge.
- [`RoboClashRobotDefinition.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashRobotDefinition.cs) — per-robot names, cooldowns, durations, vitals/stat data, and charge-per-damage tuning.
- [`ArenaProjectile.cs`](../RoboClash/Assets/Scripts/RoboClash/ArenaProjectile.cs) — adds ultimate charge for damage dealt by projectile hits.

## RoboClash settings screen — implementation report

### Purpose and entry point

The settings screen is a runtime-created Unity UGUI overlay, not a separate scene or a pre-authored Canvas. During gameplay, `RoboClashHUD.BuildPauseSettings()` adds one `RoboClashSettingsPanel` component to the gameplay manager and initializes its UI under the gameplay Canvas. The pause menu's **SETTINGS** button opens it; the **BACK** button closes it and returns to the pause menu.

```csharp
private void BuildPauseSettings()
{
    settingsPanel = gameObject.AddComponent<RoboClashSettingsPanel>();
    settingsPanel.Initialize(canvas.transform, font, ClosePauseSettings);
    settingsPanel.Root.SetActive(false);
}

private void OpenPauseSettings()
{
    redesignedHud?.SetSettingsVisible(true);
    settingsPanel.Show();
}

private void ClosePauseSettings()
{
    settingsPanel.Hide();
    redesignedHud?.SetSettingsVisible(false);
}
```

The redesigned HUD canvas is intentionally hidden while the game is paused/settings are visible. This prevents its higher sorting order from obscuring settings dropdown popups. `SetMatchState` also hides that HUD while paused and restores it when gameplay resumes.

### What the screen contains

The generated panel is titled **AUDIO & DISPLAY SETTINGS** and provides:

| Setting | Control / options | Behavior |
| --- | --- | --- |
| Master volume | Slider, 0–100% | Updates listener/master audio volume. |
| Music volume | Slider, 0–100% | Updates the saved music volume and notifies music listeners. |
| SFX volume | Slider, 0–100% | Updates sound-effect volume. |
| Fullscreen | Toggle | Applies fullscreen immediately. |
| Resolution | Dropdown | Lists supported display resolutions; a change requires confirmation and reverts after 10 seconds if not accepted. |
| Graphics quality | Dropdown | Uses the project's configured Unity quality-level names. |
| V-Sync | Toggle | Changes V-Sync and updates frame-rate-limit behavior. |
| Frame-rate limit | Dropdown | 30, 60, 120, 144 FPS, or unlimited. |
| Camera mode | Dropdown | First person or third person. |
| Field of view | Dropdown | 70, 80, 90, 100, or 110 degrees. |
| Mouse sensitivity | Dropdown | Very low, low, medium, high, or very high. |
| Controls | Text | Displays the keyboard control summary. |
| Reset settings | Button | Resets saved audio/display settings, camera mode, FOV, and mouse sensitivity to defaults. |

The layout and callbacks are constructed in `RoboClashSettingsPanel.BuildControls()`:

```csharp
CreateSettingLabel(panel.transform, "RESOLUTION", 4);
resolutionDropdown = CreateSettingDropdown(panel.transform, 4);
resolutionDropdown.onValueChanged.AddListener(ApplyResolution);

CreateSettingLabel(panel.transform, "CAMERA MODE", 8);
cameraModeDropdown = CreateSettingDropdown(panel.transform, 8);
cameraModeDropdown.AddOptions(new List<string> { "FIRST PERSON", "THIRD PERSON" });
cameraModeDropdown.onValueChanged.AddListener(SetCameraMode);

CreateSettingLabel(panel.transform, "FIELD OF VIEW", 9);
fovDropdown = CreateSettingDropdown(panel.transform, 9);
fovDropdown.AddOptions(new List<string> { "70", "80", "90", "100", "110" });
fovDropdown.onValueChanged.AddListener(index =>
    RoboClashSession.SaveFieldOfView(FovValues[Mathf.Clamp(index, 0, FovValues.Length - 1)]));
```

### Resolution selection and confirmation

Resolution choices come from `Screen.resolutions`, with duplicate width/height pairs removed. The currently active resolution is inserted if Unity does not return it in the supported list. Choosing a different resolution applies it temporarily and opens a confirmation overlay; the user must confirm within 10 seconds or the previous resolution/fullscreen state is restored. Closing the settings screen while this confirmation is pending also restores the previous mode.

```csharp
previousResolutionWidth = Screen.width;
previousResolutionHeight = Screen.height;
previousFullscreen = Screen.fullScreen;
RoboClashSaveService.SetResolution(resolution.x, resolution.y);
resolutionChangePending = true;
resolutionConfirmationSeconds = 10f;
ShowConfirmation(
    $"KEEP {resolution.x} x {resolution.y}? REVERTING IN 10 SECONDS.",
    ConfirmResolution, false);
```

The countdown uses `Time.unscaledDeltaTime`, so it continues while the match is paused. If the requested size is unsupported, `RoboClashSaveService.SetResolution` logs a warning instead of applying it.

### Camera mode and input-related preferences

The camera dropdown saves a `RoboClashCameraMode` value in `PlayerPrefs` through `RoboClashSession`. The player movement component reads the saved session mode and switches its controlled camera into that mode. Field of view and mouse sensitivity are also session preferences saved through `RoboClashSession`; they are separate from the JSON save object used for volume and display settings.

```csharp
private static void SetCameraMode(int index)
{
    RoboClashSession.SaveCameraMode(index == (int)RoboClashCameraMode.ThirdPerson
        ? RoboClashCameraMode.ThirdPerson
        : RoboClashCameraMode.FirstPerson);
}
```

```csharp
if (cameraMode != RoboClashSession.CameraMode)
    SetCameraMode(RoboClashSession.CameraMode);
```

The camera-mode preference is saved immediately. The camera updates from the selected preference in the player movement loop; it does not require changing scenes. If the dropdown itself cannot be opened, that points to the UI pointer/EventSystem path rather than the camera-mode setter.

### Persistence and applying settings

Audio/display values are held in `RoboClashSaveData.settings` and written to `roboclash-save.json` under `Application.persistentDataPath`. Most controls apply immediately; `Hide()` saves when leaving the settings panel. Resolution changes have the extra confirmation/revert flow. `RoboClashSaveService.ApplySettings()` validates and reapplies saved audio, display resolution/fullscreen, graphics quality, V-Sync, and frame rate at initialization.

```csharp
public void Hide()
{
    if (root == null)
        return;

    if (resolutionChangePending)
        RestorePreviousResolution();
    RoboClashSaveService.Save();
    confirmationAction = null;
    confirmationRoot?.SetActive(false);
    contentRoot.SetActive(true);
    root.SetActive(false);
}
```

Camera mode, FOV, and mouse sensitivity are stored separately using `PlayerPrefs` in `RoboClashSession`. Resetting settings calls `RoboClashSaveService.ResetSettings()` and explicitly resets those three session preferences.

### UGUI dropdowns and pointer input

Controls use Unity's legacy `UnityEngine.UI` components (`Slider`, `Toggle`, and `Dropdown`); no TextMeshPro dependency is used. Each dropdown is built with a template, mask, content layout, toggle group, and `ScrollRect`. The settings panel relies on the gameplay Canvas's `GraphicRaycaster` and the scene's repaired `InputSystemUIInputModule`.

```csharp
var dropdown = dropdownObject.AddComponent<Dropdown>();
dropdown.captionText = caption;
dropdown.template = templateRect;
dropdown.itemText = itemText;
dropdown.itemImage = item.GetComponent<Image>();

var scroll = template.AddComponent<ScrollRect>();
scroll.viewport = viewport.GetComponent<RectTransform>();
scroll.content = content.GetComponent<RectTransform>();
scroll.horizontal = false;
scroll.vertical = true;
```

The shared `RoboClashUIEventSystem.EnsureForScene()` helper selects or creates an EventSystem for the gameplay scene, ensures it has an enabled `InputSystemUIInputModule`, repairs missing default pointer actions, and disables duplicate scene EventSystems/input modules. This is the relevant input path to inspect if settings options render but mouse clicks do not select them.

### Sources and verification notes

- [`RoboClashSettingsPanel.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashSettingsPanel.cs) — builds and updates the settings overlay, controls, dropdown templates, resolution confirmation, and reset flow.
- [`RoboClashHUD.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashHUD.cs) — owns the pause menu and settings panel; invokes the redesigned HUD visibility hook around settings.
- [`RoboClashHudV2.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashHudV2.cs) — hides the higher-sorting HUD when the game is paused/settings are open.
- [`RoboClashSaveService.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashSaveService.cs) — validates, applies, and persists audio/display settings.
- [`RoboClashSession.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashSession.cs) — persists camera mode, FOV, sensitivity, and match selection preferences.
- [`RoboClashRobotMovement.cs`](../RoboClash/Assets/Scripts/RoboClash/RoboClashRobotMovement.cs) — applies the selected first-/third-person mode to the player camera.
- [`RoboClashMenuController.cs`](../RoboClash/Assets/Scripts/RoboClash/UI/RoboClashMenuController.cs) — defines `RoboClashUIEventSystem`, the Input System UI module setup used by menu and gameplay UI.

This report is source-based. The presence of working callbacks in code does not by itself confirm that dropdown clicks, display-mode changes, or camera transitions work correctly in the current Unity Play Mode or player build; test these interactions manually.
