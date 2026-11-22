using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;

// Va en el RunnerPrefab (junto al NetworkRunner).
// IMPORTANTE: debe existir UN SOLO PlayerSpawner, si no, se spawnean avatares duplicados.
//
// En AR el jugador esta parado en su cuarto real: NO se mueve el XR Origin a spawn points.
// Ademas se espera a que ColocationManager alinee los visores antes de crear el avatar,
// para que no aparezca en un lugar equivocado y luego "salte".
public class PlayerSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField]
    private NetworkPrefabRef playerPrefab;

#if UNITY_EDITOR
    // Lo usa la herramienta Tools > Arco AR para asignar el prefab automaticamente
    public void EditorSetPlayerPrefab(NetworkPrefabRef prefab) => playerPrefab = prefab;
#endif

    private NetworkRunner _runner;
    private NetworkObject _localPlayerObject;
    private bool _pendingSpawn;

    void Start()
    {
        _runner = GetComponent<NetworkRunner>();
        if (_runner == null && NetworkManager.Instance != null)
            _runner = NetworkManager.Instance.Runner;

        if (_runner == null)
        {
            Debug.LogError("No hay Runner. Inicia el juego desde la escena del menu.");
            return;
        }

        _runner.AddCallbacks(this);

        if (_runner.IsRunning)
            _pendingSpawn = true;
    }

    void OnDestroy()
    {
        if (_runner != null)
            _runner.RemoveCallbacks(this);
    }

    void Update()
    {
        if (!_pendingSpawn || _runner == null || !_runner.IsRunning)
            return;

        // Esperar a que los visores esten alineados
        if (!ColocationManager.ReadyToSpawn)
            return;

        _pendingSpawn = false;
        SpawnLocalPlayer(_runner, _runner.LocalPlayer);
    }

    private void SpawnLocalPlayer(NetworkRunner runner, PlayerRef player)
    {
        if (!playerPrefab.IsValid)
        {
            Debug.LogError("PlayerSpawner: falta asignar el Player Prefab.");
            return;
        }

        if (_localPlayerObject != null)
            return;

        HardwareRig rig = FindFirstObjectByType<HardwareRig>();
        if (rig == null || !rig.IsValid)
            Debug.LogError("PlayerSpawner: no hay HardwareRig valido en la escena. Agregalo al XR Origin.");

        // El avatar nace donde esta tu XR Origin (NetworkRig lo mueve cada frame de todos modos)
        Vector3 spawnPosition = rig != null && rig.playerTransform != null ? rig.playerTransform.position : Vector3.zero;
        Quaternion spawnRotation = rig != null && rig.playerTransform != null ? rig.playerTransform.rotation : Quaternion.identity;

        Debug.Log($"[PlayerSpawner] Spawn del jugador {player.PlayerId} en {spawnPosition}");

        _localPlayerObject = runner.Spawn(playerPrefab, spawnPosition, spawnRotation, player);
    }

    #region INetworkRunnerCallbacks
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (player == runner.LocalPlayer)
            _pendingSpawn = true;
    }

    // Los avatares de quien se va los borra Fusion solo, si en el NetworkObject del
    // PlayerPrefab esta marcado "Destroy When State Authority Leaves".
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    #endregion

    #region Unused INetworkRunnerCallbacks
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
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
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    #endregion
}
