using UnityEngine;
using Fusion;

// Datos del cuerpo que viajan por la red (cabeza, manos y XR Origin).
public struct RigData : INetworkStruct
{
    public Vector3 PlayerPosition;
    public Quaternion PlayerRotation;

    public Vector3 HeadPosition;
    public Quaternion HeadRotation;

    public Vector3 LeftHandPosition;
    public Quaternion LeftHandRotation;

    public Vector3 RightHandPosition;
    public Quaternion RightHandRotation;
}

// Sincroniza el cuerpo del jugador SIN usar NetworkTransform en los hijos:
// el dueno publica sus posiciones en "Data" y los demas las aplican a su copia.
// IMPORTANTE: quita los componentes NetworkTransform del PlayerPrefab
// (raiz, Head, LeftHand, RightHand y Body).
public class NetworkRig : NetworkBehaviour
{
    public bool IsLocalNetworkRig => Object.HasStateAuthority;

    [Header("RigComponents (si los dejas vacios se buscan solos por nombre)")]
    [SerializeField] private Transform playerTransform;    // vacio = la raiz del prefab
    [SerializeField] private Transform headTransform;      // hijo llamado "Head"
    [SerializeField] private Transform leftHandTransform;  // hijo llamado "LeftHand"
    [SerializeField] private Transform rightHandTransform; // hijo llamado "RightHand"

    [Header("Cuerpo (opcional)")]
    [Tooltip("Hijo llamado \"Body\". Se coloca solo debajo de la cabeza, no viaja por red.")]
    [SerializeField] private Transform bodyTransform;
    [Tooltip("Cuanto mas abajo que la cabeza queda el centro del cuerpo (metros)")]
    [SerializeField] private float bodyDrop = 0.5f;

    [Header("Avatar local")]
    [Tooltip("Oculta tu propio cuerpo (los demas jugadores si lo ven).")]
    [SerializeField] private bool hideLocalAvatar = true;

    [Header("Avatar remoto")]
    [Tooltip("Que tan rapido sigue el avatar de otro jugador a los datos de red. Mas alto = mas rapido.")]
    [SerializeField] private float remoteSmoothing = 25f;

    [Networked] private RigData Data { get; set; }

    private HardwareRig hardwareRig;
    private bool remoteInitialized;

    public override void Spawned()
    {
        base.Spawned();

        if (playerTransform == null) playerTransform = transform;
        if (headTransform == null) headTransform = FindChildByName("Head");
        if (leftHandTransform == null) leftHandTransform = FindChildByName("LeftHand");
        if (rightHandTransform == null) rightHandTransform = FindChildByName("RightHand");
        if (bodyTransform == null) bodyTransform = FindChildByName("Body");

        if (headTransform == null || leftHandTransform == null || rightHandTransform == null)
            Debug.LogError("NetworkRig: no encontre Head, LeftHand o RightHand en el PlayerPrefab. Asignalos en el Inspector.");

        if (IsLocalNetworkRig)
        {
            hardwareRig = FindFirstObjectByType<HardwareRig>();
            if (hardwareRig == null)
                Debug.LogError("Missing HardwareRig in the scene");

            if (hideLocalAvatar)
            {
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                    r.enabled = false;
            }
        }
        else
        {
            Debug.Log("Avatar remoto spawneado: " + Object.InputAuthority);
        }
    }

    // El dueno publica la posicion real de su XR Origin, cabeza y manos
    public override void FixedUpdateNetwork()
    {
        if (!IsLocalNetworkRig || hardwareRig == null || !hardwareRig.IsValid)
            return;

        Data = new RigData
        {
            PlayerPosition = hardwareRig.playerTransform.position,
            PlayerRotation = hardwareRig.playerTransform.rotation,

            HeadPosition = hardwareRig.headTransform.position,
            HeadRotation = hardwareRig.headTransform.rotation,

            LeftHandPosition = hardwareRig.leftHandTransform.position,
            LeftHandRotation = hardwareRig.leftHandTransform.rotation,

            RightHandPosition = hardwareRig.rightHandTransform.position,
            RightHandRotation = hardwareRig.rightHandTransform.rotation
        };
    }

    public override void Render()
    {
        if (headTransform == null || leftHandTransform == null || rightHandTransform == null)
            return;

        if (IsLocalNetworkRig)
        {
            if (hardwareRig == null || !hardwareRig.IsValid)
                return;

            // Jugador local: sigue al hardware sin retraso
            playerTransform.SetPositionAndRotation(hardwareRig.playerTransform.position, hardwareRig.playerTransform.rotation);
            headTransform.SetPositionAndRotation(hardwareRig.headTransform.position, hardwareRig.headTransform.rotation);
            leftHandTransform.SetPositionAndRotation(hardwareRig.leftHandTransform.position, hardwareRig.leftHandTransform.rotation);
            rightHandTransform.SetPositionAndRotation(hardwareRig.rightHandTransform.position, hardwareRig.rightHandTransform.rotation);
            UpdateBody();
            return;
        }

        // Jugador remoto: aplicamos los datos que llegan por red
        RigData d = Data;

        // Aun no llegaron datos del dueno (rotacion toda en cero = valor por defecto)
        if (d.PlayerRotation.x == 0f && d.PlayerRotation.y == 0f && d.PlayerRotation.z == 0f && d.PlayerRotation.w == 0f)
            return;

        if (!remoteInitialized)
        {
            // Primera vez: colocar directo, sin deslizarse desde el origen
            playerTransform.SetPositionAndRotation(d.PlayerPosition, d.PlayerRotation);
            headTransform.SetPositionAndRotation(d.HeadPosition, d.HeadRotation);
            leftHandTransform.SetPositionAndRotation(d.LeftHandPosition, d.LeftHandRotation);
            rightHandTransform.SetPositionAndRotation(d.RightHandPosition, d.RightHandRotation);
            UpdateBody();
            remoteInitialized = true;
            return;
        }

        float t = 1f - Mathf.Exp(-remoteSmoothing * Time.deltaTime);

        SmoothTo(playerTransform, d.PlayerPosition, d.PlayerRotation, t);
        SmoothTo(headTransform, d.HeadPosition, d.HeadRotation, t);
        SmoothTo(leftHandTransform, d.LeftHandPosition, d.LeftHandRotation, t);
        SmoothTo(rightHandTransform, d.RightHandPosition, d.RightHandRotation, t);
        UpdateBody();
    }

    // El cuerpo se calcula a partir de la cabeza: queda debajo de ella y gira solo
    // hacia donde mira (sin inclinarse). No necesita viajar por red.
    private void UpdateBody()
    {
        if (bodyTransform == null)
            return;

        Vector3 head = headTransform.position;
        Quaternion yaw = Quaternion.Euler(0f, headTransform.eulerAngles.y, 0f);

        bodyTransform.SetPositionAndRotation(head + Vector3.down * bodyDrop, yaw);
    }

    private static void SmoothTo(Transform target, Vector3 position, Quaternion rotation, float t)
    {
        target.SetPositionAndRotation(
            Vector3.Lerp(target.position, position, t),
            Quaternion.Slerp(target.rotation, rotation, t));
    }

    private Transform FindChildByName(string childName)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
                return child;
        }
        return null;
    }
}