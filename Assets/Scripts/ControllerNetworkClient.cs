using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace RoboClashController
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ControllerInputProvider))]
    public sealed class ControllerNetworkClient : MonoBehaviour
    {
        private const int Port = 17862;
        private const string HelloMessage = "{\"type\":\"hello\",\"protocol\":1}";
        private const string WelcomeMessage = "{\"type\":\"welcome\",\"protocol\":1}";
        private const string Acknowledgement = "ack";
        private const int MaximumLineLength = 512;

        private readonly object inputLock = new object();
        private readonly byte[] readBuffer = new byte[1];
        private ControllerInputProvider inputProvider;
        private ControllerInputState latestInput;
        private long jumpPressSequence;
        private long reloadPressSequence;
        private long abilityPressSequence;
        private long ultimatePressSequence;
        private TcpClient client;
        private Thread networkThread;
        private volatile bool stopping;
        private volatile bool connected;
        private volatile string status = "NOT CONNECTED";
        private int connectionGeneration;

        public bool IsConnected => connected;
        public string Status => status;

        private void Awake()
        {
            inputProvider = GetComponent<ControllerInputProvider>();
        }

        private void Update()
        {
            if (inputProvider == null)
                return;

            lock (inputLock)
                latestInput = inputProvider.GetCurrentInputSnapshot();
        }

        private void OnEnable()
        {
            if (inputProvider != null)
                inputProvider.ActionPressed += OnActionPressed;
        }

        public void Connect(string host)
        {
            if (connected || status.StartsWith("CONNECTING", StringComparison.Ordinal))
                return;

            Disconnect();
            if (!IPAddress.TryParse(host != null ? host.Trim() : string.Empty, out var address) ||
                address.AddressFamily != AddressFamily.InterNetwork)
            {
                status = "ENTER A VALID IPV4 ADDRESS";
                return;
            }

            stopping = false;
            status = $"CONNECTING TO {address}:{Port}";
            var generation = Interlocked.Increment(ref connectionGeneration);
            networkThread = new Thread(() => RunConnection(address, generation))
            {
                IsBackground = true,
                Name = "RoboClash Controller TCP Client"
            };
            networkThread.Start();
        }

        public void Disconnect()
        {
            stopping = true;
            Interlocked.Increment(ref connectionGeneration);
            connected = false;
            lock (inputLock)
            {
                latestInput = default;
                jumpPressSequence = 0;
                reloadPressSequence = 0;
                abilityPressSequence = 0;
                ultimatePressSequence = 0;
            }
            inputProvider?.ResetAllInputs();

            var activeClient = client;
            client = null;
            if (activeClient != null)
                activeClient.Close();

            if (networkThread != null && networkThread.IsAlive &&
                Thread.CurrentThread != networkThread && !networkThread.Join(500))
                Debug.LogWarning("The controller network thread did not stop within 500 ms.", this);

            networkThread = null;
            status = "NOT CONNECTED";
        }

        private void RunConnection(IPAddress address, int generation)
        {
            TcpClient localClient = null;
            try
            {
                localClient = new TcpClient(AddressFamily.InterNetwork)
                {
                    NoDelay = true,
                    SendTimeout = 1000,
                    ReceiveTimeout = 5000
                };
                if (!IsCurrentConnection(generation))
                    return;
                client = localClient;
                var connect = localClient.BeginConnect(address, Port, null, null);
                if (!connect.AsyncWaitHandle.WaitOne(5000))
                    throw new SocketException((int)SocketError.TimedOut);
                localClient.EndConnect(connect);
                if (!IsCurrentConnection(generation))
                    return;

                using (var stream = localClient.GetStream())
                {
                    WriteLine(stream, HelloMessage);
                    if (!string.Equals(ReadLine(stream), WelcomeMessage, StringComparison.Ordinal))
                        throw new InvalidOperationException("The PC did not accept this controller protocol.");

                    if (!IsCurrentConnection(generation))
                        return;
                    connected = true;
                    status = "PHONE CONNECTED";
                    long sentJumpSequence = 0;
                    long sentReloadSequence = 0;
                    long sentAbilitySequence = 0;
                    long sentUltimateSequence = 0;
                    while (IsCurrentConnection(generation))
                    {
                        ControllerInputState input;
                        long jumpSequence;
                        long reloadSequence;
                        long abilitySequence;
                        long ultimateSequence;
                        lock (inputLock)
                        {
                            input = latestInput;
                            jumpSequence = jumpPressSequence;
                            reloadSequence = reloadPressSequence;
                            abilitySequence = abilityPressSequence;
                            ultimateSequence = ultimatePressSequence;
                        }

                        WriteLine(stream, FormatInput(input,
                            jumpSequence != sentJumpSequence,
                            reloadSequence != sentReloadSequence,
                            abilitySequence != sentAbilitySequence,
                            ultimateSequence != sentUltimateSequence));
                        var acknowledgement = ReadLine(stream);
                        if (!string.Equals(acknowledgement, Acknowledgement, StringComparison.Ordinal))
                        {
                            var reason = string.IsNullOrEmpty(acknowledgement)
                                ? "The game PC stopped acknowledging controller input."
                                : "The game PC rejected controller input: " + acknowledgement;
                            throw new InvalidOperationException(reason);
                        }
                        lock (inputLock)
                        {
                            inputProvider.AcknowledgeGyroAimDelta(
                                input.GyroYawDelta, input.GyroPitchDelta);
                            latestInput.GyroYawDelta -= input.GyroYawDelta;
                            latestInput.GyroPitchDelta -= input.GyroPitchDelta;
                        }
                        sentJumpSequence = jumpSequence;
                        sentReloadSequence = reloadSequence;
                        sentAbilitySequence = abilitySequence;
                        sentUltimateSequence = ultimateSequence;
                        Thread.Sleep(16);
                    }
                }
            }
            catch (Exception exception)
            {
                if (IsCurrentConnection(generation))
                {
                    connected = false;
                    status = "CONNECTION FAILED: " + exception.Message;
                }
            }
            finally
            {
                if (generation == Volatile.Read(ref connectionGeneration))
                    connected = false;
                if (localClient != null)
                {
                    localClient.Close();
                    if (client == localClient)
                        client = null;
                }
                if (IsCurrentConnection(generation) && status == "PHONE CONNECTED")
                    status = "CONNECTION LOST";
            }
        }

        private bool IsCurrentConnection(int generation)
        {
            return !stopping && generation == Volatile.Read(ref connectionGeneration);
        }

        private string ReadLine(NetworkStream stream)
        {
            var builder = new StringBuilder();
            while (!stopping)
            {
                var read = stream.Read(readBuffer, 0, 1);
                if (read == 0)
                    return null;
                if (readBuffer[0] == (byte)'\n')
                {
                    if (builder.Length > 0 && builder[builder.Length - 1] == '\r')
                        builder.Length--;
                    return builder.ToString();
                }
                if (readBuffer[0] > 127 || builder.Length >= MaximumLineLength)
                    return null;
                builder.Append((char)readBuffer[0]);
            }

            return null;
        }

        private static void WriteLine(NetworkStream stream, string message)
        {
            var bytes = Encoding.ASCII.GetBytes(message + "\n");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static string FormatInput(ControllerInputState input, bool jumpPressed,
            bool reloadPressed, bool abilityPressed, bool ultimatePressed)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{\"type\":\"input\",\"protocol\":1,\"moveForward\":{0}," +
                "\"aimHorizontal\":{1},\"gyroTurn\":{2},\"gyroPitch\":{3}," +
                "\"gyroYawDelta\":{4},\"gyroPitchDelta\":{5}," +
                "\"shoot\":{6},\"jump\":{7},\"reload\":{8},\"ability\":{9},\"ultimate\":{10}," +
                "\"proximityNear\":{11},\"compassValid\":{12},\"compassHeading\":{13}," +
                "\"accelerometerValid\":{14},\"accelX\":{15},\"accelY\":{16},\"accelZ\":{17}," +
                "\"recenter\":{18}}}",
                FormatNumber(FiniteOrZero(input.MoveForward)),
                FormatNumber(FiniteOrZero(input.AimHorizontal)),
                FormatNumber(FiniteOrZero(input.GyroTurn)),
                FormatNumber(FiniteOrZero(input.GyroPitch)),
                FormatNumber(FiniteOrZeroUnclamped(input.GyroYawDelta)),
                FormatNumber(FiniteOrZeroUnclamped(input.GyroPitchDelta)),
                Lower(input.Shoot), Lower(input.Jump || jumpPressed),
                Lower(input.Reload || reloadPressed), Lower(input.Ability || abilityPressed),
                Lower(input.Ultimate || ultimatePressed), Lower(input.ProximityNear),
                Lower(input.CompassValid),
                FormatNumber(FiniteOrZeroUnclamped(input.CompassHeading)),
                Lower(input.AccelerometerValid),
                FormatNumber(FiniteOrZeroUnclamped(input.AccelerometerX)),
                FormatNumber(FiniteOrZeroUnclamped(input.AccelerometerY)),
                FormatNumber(FiniteOrZeroUnclamped(input.AccelerometerZ)),
                Lower(input.Recenter));
        }

        private static string FormatNumber(float value)
        {
            return value.ToString("0.##########", CultureInfo.InvariantCulture);
        }

        private static float FiniteOrZero(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(-1f, Math.Min(1f, value));
        }

        private static float FiniteOrZeroUnclamped(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static string Lower(bool value)
        {
            return value ? "true" : "false";
        }

        private void OnDisable()
        {
            if (inputProvider != null)
                inputProvider.ActionPressed -= OnActionPressed;
            Disconnect();
        }

        private void OnActionPressed(ControllerAction action)
        {
            lock (inputLock)
            {
                switch (action)
                {
                    case ControllerAction.Jump:
                        jumpPressSequence++;
                        break;
                    case ControllerAction.Reload:
                        reloadPressSequence++;
                        break;
                    case ControllerAction.Ability:
                        abilityPressSequence++;
                        break;
                    case ControllerAction.Ultimate:
                        ultimatePressSequence++;
                        break;
                }
            }
        }
    }
}
