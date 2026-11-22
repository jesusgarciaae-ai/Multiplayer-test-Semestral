using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.Meta;
using SerializableGuid = UnityEngine.XR.ARSubsystems.SerializableGuid;

// Va en la escena de juego (pofa), por ejemplo en el XR Origin.
//
// Alinea los visores de los jugadores que estan EN EL MISMO CUARTO:
//  1. Quien creo la sala pone un ancla en el piso (debajo de su cabeza) y la comparte
//     con un "grupo" cuyo ID sale del codigo de sala.
//  2. Quien se une carga las anclas de ese grupo.
//  3. Los dos mueven su XR Origin para que el ancla quede en (0,0,0) mirando a +Z.
//     Asi los dos visores usan el mismo sistema de coordenadas y lo que se
//     sincroniza por Fusion (avatares, flechas, dianas) coincide con el cuarto real.
//
// Requisitos: Meta Quest: Anchors activado en OpenXR (Android), ARAnchorManager en el XR Origin,
// misma red Wi-Fi y "Datos espaciales" activado en los ajustes de privacidad de los visores.
public class ColocationManager : MonoBehaviour
{
    public static ColocationManager Instance { get; private set; }

    // PlayerSpawner espera esto antes de crear el avatar.
    // Si no hay ColocationManager en la escena, no se espera.
    public static bool ReadyToSpawn => Instance == null || Instance.IsAligned;

    public bool IsAligned { get; private set; }
    public string Status { get; private set; } = "";
    public event System.Action Aligned;

    [Header("Referencias (vacias = se buscan solas)")]
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private ARAnchorManager anchorManager;

    [Header("UI (opcional)")]
    [Tooltip("Texto donde se muestra el estado de la alineacion")]
    [SerializeField] private TMP_Text statusLabel;
    [Tooltip("Si no asignas un texto, se crea un letrero pequeno frente a la camara")]
    [SerializeField] private bool createLabelIfMissing = true;
    [Tooltip("Segundos que sigue visible el letrero despues de alinear (0 = siempre visible)")]
    [SerializeField] private float hideLabelAfterSeconds = 4f;

    [Header("Tiempos")]
    [Tooltip("Segundos entre intentos de cargar el ancla del anfitrion")]
    [SerializeField] private float retryInterval = 2f;
    [Tooltip("Si pasado este tiempo no se alinea, se deja jugar sin alinear (0 = esperar para siempre)")]
    [SerializeField] private float giveUpAfterSeconds = 60f;

    [Header("Correccion continua")]
    [Tooltip("Si el visor reubica el ancla mas de esto (metros), se vuelve a alinear")]
    [SerializeField] private float realignDistance = 0.03f;
    [Tooltip("Si el ancla gira mas de esto (grados), se vuelve a alinear")]
    [SerializeField] private float realignAngle = 1f;

    [Header("Pruebas")]
    [Tooltip("En el Editor no hay anclas compartidas: se salta la alineacion")]
    [SerializeField] private bool skipInEditor = true;

    private ARAnchor sharedAnchor;
    private float nextRealignCheck;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private async void Start()
    {
        if (xrOrigin == null) xrOrigin = FindFirstObjectByType<XROrigin>();
        if (anchorManager == null && xrOrigin != null) anchorManager = xrOrigin.GetComponent<ARAnchorManager>();
        if (anchorManager == null) anchorManager = FindFirstObjectByType<ARAnchorManager>();

        NetworkManager nm = NetworkManager.Instance;

        // Jugando solo (sin sala): no hay nada que alinear ni que mostrar
        if (nm == null || string.IsNullOrEmpty(nm.SessionCode))
        {
            hideLabelAfterSeconds = 0f;
            Finish("Jugando solo: no se necesita alineacion.");
            return;
        }

        if (statusLabel == null && createLabelIfMissing)
            statusLabel = CreateLabel();

#if UNITY_EDITOR
        if (skipInEditor)
        {
            Finish("Editor: se salta la alineacion.");
            return;
        }
#endif

        if (xrOrigin == null || anchorManager == null)
        {
            Debug.LogError("ColocationManager: falta XR Origin o ARAnchorManager en la escena.");
            Finish("Sin ARAnchorManager: se juega sin alinear.");
            return;
        }

        SetStatus("Preparando anclas...");

        // Esperar a que el subsistema de anclas este corriendo
        float waited = 0f;
        while (anchorManager.subsystem == null || !anchorManager.subsystem.running)
        {
            await Awaitable.NextFrameAsync();
            waited += Time.unscaledDeltaTime;
            if (waited > 10f)
            {
                Finish("Las anclas no estan disponibles: se juega sin alinear.");
                return;
            }
        }

        if (anchorManager.subsystem is not MetaOpenXRAnchorSubsystem meta)
        {
            Finish("Este visor no soporta anclas compartidas: se juega sin alinear.");
            return;
        }

        meta.sharedAnchorsGroupId = GroupIdFromCode(nm.SessionCode);

        if (nm.IsRoomCreator)
            await HostCreateAndShare();
        else
            await GuestLoadAndAlign();
    }

    // ------------------------------------------------------------ ANFITRION

    private async Awaitable HostCreateAndShare()
    {
        // Un momento para que el tracking de la cabeza se estabilice
        await Awaitable.WaitForSecondsAsync(1f);

        Transform head = xrOrigin.Camera != null ? xrOrigin.Camera.transform : Camera.main.transform;

        // Ancla en el piso, debajo de la cabeza, girada solo hacia donde miras (sin inclinar)
        Vector3 pos = head.position;
        pos.y = xrOrigin.transform.position.y;
        Quaternion rot = Quaternion.Euler(0f, head.eulerAngles.y, 0f);

        SetStatus("Creando ancla compartida...");
        var result = await anchorManager.TryAddAnchorAsync(new Pose(pos, rot));
        if (!result.status.IsSuccess() || result.value == null)
        {
            Debug.LogError("ColocationManager: no se pudo crear el ancla: " + result.status);
            Finish("No se pudo crear el ancla: se juega sin alinear.");
            return;
        }

        sharedAnchor = result.value;

        await WaitForTracking(sharedAnchor, 5f);

        // Compartir (a veces falla los primeros intentos mientras el visor mapea el cuarto)
        float start = Time.unscaledTime;
        while (true)
        {
            SetStatus("Compartiendo ancla con los demas jugadores...");
            XRResultStatus share = await anchorManager.TryShareAnchorAsync(sharedAnchor);
            if (share.IsSuccess())
                break;

            Debug.LogWarning("ColocationManager: fallo al compartir el ancla (" + share + "). Reintentando...");

            if (GaveUp(start))
            {
                // Aun asi alineamos al anfitrion, para que su espacio sea el de referencia
                AlignTo(sharedAnchor.transform);
                Finish("No se pudo compartir el ancla. Revisa Wi-Fi y 'Datos espaciales'.");
                return;
            }

            await Awaitable.WaitForSecondsAsync(retryInterval);
        }

        AlignTo(sharedAnchor.transform);
        Finish("Ancla compartida. Esperando a los demas jugadores.");
    }

    // ------------------------------------------------------------ INVITADO

    private async Awaitable GuestLoadAndAlign()
    {
        float start = Time.unscaledTime;
        var loaded = new List<XRAnchor>();

        while (true)
        {
            SetStatus("Buscando el ancla del anfitrion...\nMirense de frente en el mismo cuarto.");

            loaded.Clear();
            XRResultStatus status = await anchorManager.TryLoadAllSharedAnchorsAsync(loaded, null);

            if (status.IsSuccess() && loaded.Count > 0)
            {
                // Si hay varias (p. ej. el anfitrion re-creo la sala), usamos la ultima
                TrackableId id = loaded[loaded.Count - 1].trackableId;
                ARAnchor anchor = await WaitForAnchorObject(id, 3f);

                if (anchor != null)
                {
                    sharedAnchor = anchor;
                    SetStatus("Ancla encontrada. Alineando...");
                    await WaitForTracking(sharedAnchor, 5f);
                    AlignTo(sharedAnchor.transform);
                    Finish("Alineado con el anfitrion.");
                    return;
                }
            }
            else if (!status.IsSuccess())
            {
                Debug.LogWarning("ColocationManager: fallo al cargar anclas (" + status + "). Reintentando...");
            }

            if (GaveUp(start))
            {
                Finish("No se encontro el ancla del anfitrion. Revisa Wi-Fi y 'Datos espaciales'.");
                return;
            }

            await Awaitable.WaitForSecondsAsync(retryInterval);
        }
    }

    // ------------------------------------------------------------ ALINEACION

    // Mueve el XR Origin para que el ancla quede en (0,0,0) mirando hacia +Z.
    // Todo lo que cuelga del XR Origin (camara, manos, planos AR) se mueve junto.
    private void AlignTo(Transform anchor)
    {
        Transform origin = xrOrigin.transform;

        Quaternion inverseYaw = Quaternion.Euler(0f, -anchor.eulerAngles.y, 0f);
        Vector3 anchorPos = anchor.position;

        Vector3 newPos = inverseYaw * (origin.position - anchorPos);
        Quaternion newRot = inverseYaw * origin.rotation;

        // Si el XR Origin tiene CharacterController (locomocion), hay que apagarlo
        // un momento; si no, deshace el cambio de posicion.
        CharacterController cc = origin.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        origin.SetPositionAndRotation(newPos, newRot);

        if (cc != null) cc.enabled = true;

        Debug.Log($"[Colocation] XR Origin alineado. Ancla estaba en {anchorPos}, yaw {anchor.eulerAngles.y:F1}");
    }

    private void Update()
    {
        // El visor puede corregir la posicion del ancla con el tiempo (relocalizacion).
        // Si se movio, volvemos a alinear para que todo siga cuadrando con el cuarto.
        if (!IsAligned || sharedAnchor == null || Time.unscaledTime < nextRealignCheck)
            return;

        nextRealignCheck = Time.unscaledTime + 1f;

        if (sharedAnchor.trackingState != TrackingState.Tracking)
            return;

        float yaw = Mathf.Abs(Mathf.DeltaAngle(0f, sharedAnchor.transform.eulerAngles.y));
        if (sharedAnchor.transform.position.magnitude > realignDistance || yaw > realignAngle)
            AlignTo(sharedAnchor.transform);
    }

    // ------------------------------------------------------------ UTILIDADES

    // El mismo codigo de sala siempre da el mismo ID de grupo
    private static SerializableGuid GroupIdFromCode(string code)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("arco-ar-" + code));
            return new SerializableGuid(new System.Guid(hash));
        }
    }

    private async Awaitable<ARAnchor> WaitForAnchorObject(TrackableId id, float timeout)
    {
        float start = Time.unscaledTime;
        while (Time.unscaledTime - start < timeout)
        {
            ARAnchor a = anchorManager.GetAnchor(id);
            if (a != null) return a;
            await Awaitable.NextFrameAsync();
        }
        return null;
    }

    private static async Awaitable WaitForTracking(ARAnchor anchor, float timeout)
    {
        float start = Time.unscaledTime;
        while (anchor != null && anchor.trackingState != TrackingState.Tracking && Time.unscaledTime - start < timeout)
            await Awaitable.NextFrameAsync();
    }

    private bool GaveUp(float start)
    {
        return giveUpAfterSeconds > 0f && Time.unscaledTime - start > giveUpAfterSeconds;
    }

    private async void Finish(string message)
    {
        SetStatus(message);
        if (IsAligned) return;
        IsAligned = true;
        Aligned?.Invoke();

        if (hideLabelAfterSeconds > 0f && statusLabel != null)
        {
            await Awaitable.WaitForSecondsAsync(hideLabelAfterSeconds);
            if (statusLabel != null) statusLabel.canvas.gameObject.SetActive(false);
        }
    }

    // Letrero pequeno pegado a la camara, abajo del centro de la vista
    private TMP_Text CreateLabel()
    {
        Camera cam = xrOrigin != null && xrOrigin.Camera != null ? xrOrigin.Camera : Camera.main;
        if (cam == null) return null;

        GameObject canvasGO = new GameObject("ColocationStatusCanvas");
        canvasGO.transform.SetParent(cam.transform, false);
        canvasGO.transform.localPosition = new Vector3(0f, -0.18f, 0.8f);
        canvasGO.transform.localScale = Vector3.one * 0.0008f;

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.GetComponent<RectTransform>().sizeDelta = new Vector2(700f, 160f);

        GameObject bg = new GameObject("Fondo");
        bg.transform.SetParent(canvasGO.transform, false);
        var img = bg.AddComponent<UnityEngine.UI.Image>();
        img.color = new Color(0.04f, 0.06f, 0.11f, 0.85f);
        img.raycastTarget = false;
        RectTransform bgRt = img.rectTransform;
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        GameObject textGO = new GameObject("Texto");
        textGO.transform.SetParent(canvasGO.transform, false);
        TextMeshProUGUI t = textGO.AddComponent<TextMeshProUGUI>();
        t.fontSize = 30;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        t.raycastTarget = false;
        RectTransform tRt = t.rectTransform;
        tRt.anchorMin = Vector2.zero;
        tRt.anchorMax = Vector2.one;
        tRt.offsetMin = new Vector2(16f, 8f);
        tRt.offsetMax = new Vector2(-16f, -8f);

        return t;
    }

    private void SetStatus(string message)
    {
        Status = message;
        Debug.Log("[Colocation] " + message);
        if (statusLabel != null) statusLabel.text = message;
    }
}
