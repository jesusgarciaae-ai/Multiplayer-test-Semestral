using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

// Va en un objeto de la escena del MENU. Sobrevive al cambio de escena (DontDestroyOnLoad).
public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    // Singleton
    public static NetworkManager Instance { get; private set; }

    [SerializeField]
    private GameObject _runnerPrefab;

    [Header("Escenas")]
    [Tooltip("Nombre de la escena de juego (debe estar en Build Profiles)")]
    [SerializeField] private string gameSceneName = "pofa";
    [Tooltip("Indice de la escena del menu en Build Profiles")]
    [SerializeField] private int menuSceneIndex = 0;

    [Header("Sala")]
    [Tooltip("Maximo de jugadores por sala")]
    [SerializeField] private int maxPlayers = 4;
    [Tooltip("\"us\" = US East, \"usw\" = US West, \"sa\" = Sudamerica. Vacio = la mejor region automaticamente")]
    [SerializeField] private string fixedRegion = "us";

    public NetworkRunner Runner { get; private set; }
    public string CurrentSessionName { get; private set; }

    // Codigo de la sala a la que se esta entrando (existe ANTES de que StartGame termine,
    // lo usa ColocationManager para saber que ancla compartida buscar).
    public string SessionCode { get; private set; }

    // true = presiono "Crear sala" (es el anfitrion que coloca el ancla compartida)
    public bool IsRoomCreator { get; private set; }

    // Ultimo error de conexion (lo lee el menu para mostrarlo en el canvas).
    // Vive aqui porque este objeto sobrevive al cambio de escena.
    public string LastErrorMessage { get; private set; }
    public event Action<string> ErrorRaised;

    public bool IsConnecting => _isConnecting;

    // true = hay una partida en red (o se esta conectando). false = jugando solo / en el menu
    public static bool IsOnline => Instance != null && Instance.Runner != null;

    // Evita que se presione "Crear/Unirse" dos veces mientras se conecta
    private bool _isConnecting;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
    }

    private void Start()
    {
        Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.FixedRegion = fixedRegion;
    }

    // Crear: si la sala no existe se crea (y si existe, te unes).
    public void CreateSession(string roomCode)
    {
        StartSession(roomCode, true);
    }

    // Unirse: NO puede crear salas. Si no existe, falla con "sala no encontrada".
    public void JoinSession(string roomCode)
    {
        StartSession(roomCode, false);
    }

    // Jugar solo: carga la escena de juego SIN conectarse a Photon (no necesita internet)
    public void PlayOffline()
    {
        if (_isConnecting)
            return;

        if (Runner != null)
        {
            Debug.LogWarning("PlayOffline: ya hay una partida en red, primero sal con LeaveSession().");
            return;
        }

        ClearError();
        CurrentSessionName = null;
        SessionCode = null;
        IsRoomCreator = false;
        SceneManager.LoadScene(gameSceneName);
    }

    // Salir de la sala (si hay) y volver al menu. Sirve tambien jugando solo.
    public async void LeaveSession()
    {
        if (Runner != null)
            await Runner.Shutdown();

        CurrentSessionName = null;
        SessionCode = null;
        IsRoomCreator = false;
        SceneManager.LoadScene(menuSceneIndex);
    }

    public void ClearError()
    {
        LastErrorMessage = null;
    }

    private async void StartSession(string roomCode, bool canCreate)
    {
        if (_isConnecting)
            return;

        if (string.IsNullOrWhiteSpace(roomCode))
        {
            Debug.LogWarning("El codigo de sala esta vacio.");
            RaiseError("Escribe el codigo de sala.");
            return;
        }

        if (_runnerPrefab == null)
        {
            Debug.LogError("NetworkManager: falta asignar el Runner Prefab.");
            RaiseError("Falta configurar el Runner Prefab.");
            return;
        }

        _isConnecting = true;
        ClearError();

        SessionCode = roomCode.Trim().ToUpper();
        IsRoomCreator = canCreate;

        try
        {
            CreateRunner();
            await LoadScene();
            await Connect(SessionCode, canCreate);
        }
        catch (Exception e)
        {
            Debug.LogError("Error al iniciar la sesion: " + e);
            RaiseError("No se pudo conectar.");
            SceneManager.LoadScene(menuSceneIndex);
        }
        finally
        {
            _isConnecting = false;
        }
    }

    public void CreateRunner()
    {
        Runner = Instantiate(_runnerPrefab, transform).GetComponent<NetworkRunner>();
        Runner.AddCallbacks(this);
    }

    public async Task LoadScene()
    {
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(gameSceneName);

        while (!asyncLoad.isDone)
        {
            await Task.Yield();
        }
    }

    private async Task Connect(string sessionName, bool canCreate)
    {
        Runner.ProvideInput = true;

        var sceneManager = Runner.GetComponent<NetworkSceneManagerDefault>();
        if (sceneManager == null)
            sceneManager = Runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        var args = new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = sessionName,
            SceneManager = sceneManager,
            PlayerCount = maxPlayers,
            // false al unirse: si la sala no existe, StartGame falla (GameNotFound)
            EnableClientSessionCreation = canCreate
        };

        var result = await Runner.StartGame(args);

        if (result.Ok)
        {
            CurrentSessionName = sessionName;
            Debug.Log("<<<<<<< Sesion iniciada: " + sessionName + " >>>>>>>");
        }
        else
        {
            Debug.LogError($"StartGame fallo: {result.ShutdownReason} - {result.ErrorMessage}");

            // Guardamos el mensaje ANTES de cambiar de escena
            RaiseError(GetFriendlyError(result.ShutdownReason, sessionName));

            // Regresamos al menu
            SceneManager.LoadScene(menuSceneIndex);
        }
    }

    private string GetFriendlyError(ShutdownReason reason, string sessionName)
    {
        switch (reason)
        {
            case ShutdownReason.GameNotFound:
                return "Sala no encontrada: " + sessionName + "\nRevisa el codigo.";
            case ShutdownReason.GameIsFull:
                return "La sala esta llena.";
            case ShutdownReason.GameClosed:
                return "La sala esta cerrada.";
            case ShutdownReason.PhotonCloudTimeout:
            case ShutdownReason.ConnectionTimeout:
                return "Sin conexion. Revisa tu internet.";
            default:
                return "No se pudo conectar (" + reason + ").";
        }
    }

    private void RaiseError(string message)
    {
        LastErrorMessage = message;
        ErrorRaised?.Invoke(message);
    }

    #region INetworkRunnerCallbacks
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log("<<<<<<<< A new player joined to the session >>>>>>>");
        Debug.Log("<<<<<<< PlayerID >>>>>>>>" + player.PlayerId);
        Debug.Log("<<<<<<< IsRealPlayer >>>>>>>>" + player.IsRealPlayer);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log("<<<<<<<< A player left the session >>>>>>>");
        Debug.Log("<<<<<<< PlayerID >>>>>>>>" + player.PlayerId);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log("<<<<<<< Runner Shutdown: " + shutdownReason + " >>>>>>>>");

        if (Runner == runner)
        {
            Runner = null;
        }
        if (runner != null)
        {
            Destroy(runner.gameObject);
        }
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError("OnConnectFailed: " + reason);
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning("OnDisconnectedFromServer: " + reason);
    }
    #endregion

    #region INetworkRunnerCallbacks (Unused)
    public void OnConnectedToServer(NetworkRunner runner) { }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }

    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }

    public void OnInput(NetworkRunner runner, NetworkInput input) { }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }

    public void OnSceneLoadDone(NetworkRunner runner) { }

    public void OnSceneLoadStart(NetworkRunner runner) { }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }

    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    #endregion
}
