using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.Meta;

public class EspacioTrabajoDianasMR : MonoBehaviour
{
    [Header("Objeto Arco / Arma")]
    [Tooltip("Arrastra tu prefab u objeto Wooden Bow de la escena")]
    public GameObject arco;

    [Header("Configuración de Dianas")]
    [Tooltip("Prefab de la diana a instanciar")]
    public GameObject targetPrefab;
    
    [Tooltip("Cantidad de dianas activas simultáneas")]
    public int maxTargets = 6;
    
    [Tooltip("Separación de la superficie para evitar Z-Fighting")]
    public float wallOffset = 0.02f;
    
    [Tooltip("Distancia mínima desde el jugador para no colocar dianas pegadas al visor")]
    public float minSpawnDistance = 1.0f;

    [Tooltip("Distancia MÍNIMA entre dianas para que no aparezcan encimadas")]
    public float minDistanceBetweenTargets = 0.7f;

    [Tooltip("Ajuste de rotación del prefab si aparece acostado (ej. 90 en X)")]
    public Vector3 offsetRotacion = new Vector3(90f, 0f, 0f);

    [Header("Dificultad (movimiento de dianas)")]
    [Tooltip("Velocidad en m/s en nivel Intermedio")]
    public float velocidadIntermedio = 0.25f;
    [Tooltip("Velocidad en m/s en nivel Dificil")]
    public float velocidadDificil = 0.5f;
    [Tooltip("Que tan lejos (metros) se puede alejar cada diana de donde nacio")]
    public float radioMovimiento = 0.5f;
    [Tooltip("Radio maximo en el modo 'Probar sin Escanear' (no se conoce el tamano de la superficie)")]
    public float radioMovimientoFisico = 0.3f;

    [Header("Referencias de Juego")]
    [Tooltip("Referencia al script del temporizador")]
    public GameTimer gameTimer;

    [Header("Objetos a Ocultar para Passthrough")]
    [Tooltip("Objetos del entorno cuyos renderizadores se apagarán para ver el Passthrough")]
    public GameObject[] ocultarVisuales = new GameObject[0];

    [Tooltip("Objetos que deben permanecer visibles (ej. arco, armas, interfaz)")]
    public GameObject[] mantenerVisibles = new GameObject[0];

    [Header("Opciones de Realidad Mixta")]
    public bool modoMixto = true;
    public float distanciaPanel = 1.2f;
    public float anchoPanelMetros = 0.9f;

    private ARPlaneManager planeManager;
    private ARSession session;
    private Camera cam;

    private GameObject canvasGO;
    private TextMeshProUGUI textoEstado;
    private Button botonComenzar;
    private TextMeshProUGUI etiquetaComenzar;

    private readonly List<ARPlane> paredesDetectadas = new List<ARPlane>();
    private readonly List<ARPlane> otrasSuperficiesDetectadas = new List<ARPlane>();
    private readonly HashSet<TrackableId> idsAntesDelEscaneo = new HashSet<TrackableId>();
    
    private List<GameObject> activeTargets = new List<GameObject>();
    private string mensajeExtra = "";
    private bool capturando;
    private bool juegoIniciado;
    private float proximoSondeo;

    private static readonly Color colorBoton = new Color(0.14f, 0.20f, 0.32f, 1f);

    private void Start()
    {
        if (targetPrefab == null)
        {
            Debug.LogWarning("[MR Dianas] Falta asignar el targetPrefab.");
            enabled = false;
            return;
        }

        if (!modoMixto)
        {
            enabled = false;
            return;
        }

        if (gameTimer == null)
        {
            gameTimer = FindFirstObjectByType<GameTimer>();
        }

        planeManager = FindAnyObjectByType<ARPlaneManager>();
        session = FindAnyObjectByType<ARSession>();
        cam = Camera.main;
        if (cam == null) cam = FindAnyObjectByType<Camera>();

        ActivarPassthrough();
        PedirPermisoEscena();
        ConstruirPanel();
    }

    private void ActivarPassthrough()
    {
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);

            ARCameraManager camManager = cam.GetComponent<ARCameraManager>();
            if (camManager == null) camManager = cam.gameObject.AddComponent<ARCameraManager>();
            camManager.enabled = true;
        }

        foreach (GameObject g in ocultarVisuales)
        {
            if (g == null) continue;
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
            {
                if (EstaEnMantenerVisibles(r.transform)) continue;
                r.enabled = false;
            }
        }
    }

    private bool EstaEnMantenerVisibles(Transform t)
    {
        foreach (GameObject k in mantenerVisibles)
        {
            if (k == null) continue;
            if (t == k.transform || t.IsChildOf(k.transform)) return true;
        }
        return false;
    }

    private void PedirPermisoEscena()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const string permiso = "com.oculus.permission.USE_SCENE";
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(permiso))
            UnityEngine.Android.Permission.RequestUserPermission(permiso);
#endif
    }

    private void SolicitarEscaneo()
    {
        PedirPermisoEscena();

        if (session == null)
        {
            mensajeExtra = "No hay ARSession en la escena.";
            return;
        }

        MetaOpenXRSessionSubsystem meta = session.subsystem as MetaOpenXRSessionSubsystem;
        if (meta == null)
        {
            mensajeExtra = "El escaneo de habitación requiere visor Meta Quest.";
            return;
        }

        idsAntesDelEscaneo.Clear();
        if (planeManager != null)
        {
            foreach (ARPlane p in planeManager.trackables)
                idsAntesDelEscaneo.Add(p.trackableId);
        }

        bool ok = meta.TryRequestSceneCapture();
        capturando = ok;
        mensajeExtra = ok ? "" : "No se pudo iniciar el escaneo.";
    }

    private void Update()
    {
        OcultarPlanosVisuales();

        if (juegoIniciado)
        {
            activeTargets.RemoveAll(target => target == null);

            if (activeTargets.Count < maxTargets)
            {
                MantenerDianas();
            }
            return;
        }

        ActualizarPosicionPanel();

        if (Time.unscaledTime < proximoSondeo) return;
        proximoSondeo = Time.unscaledTime + 0.5f;

        BuscarSuperficies();
        ActualizarPanelUI();
    }

    private void ActualizarPosicionPanel()
    {
        if (canvasGO == null || cam == null) return;

        Vector3 adelante = cam.transform.forward;
        adelante.y = 0f;
        if (adelante.sqrMagnitude < 0.01f) adelante = Vector3.forward;
        adelante.Normalize();

        Vector3 posObjetivo = cam.transform.position + adelante * distanciaPanel;
        posObjetivo.y = cam.transform.position.y - 0.05f;

        Quaternion rotObjetivo = Quaternion.LookRotation(adelante);

        canvasGO.transform.position = Vector3.Lerp(canvasGO.transform.position, posObjetivo, Time.deltaTime * 6f);
        canvasGO.transform.rotation = Quaternion.Slerp(canvasGO.transform.rotation, rotObjetivo, Time.deltaTime * 6f);
    }

    private void BuscarSuperficies()
    {
        paredesDetectadas.Clear();
        otrasSuperficiesDetectadas.Clear();

        if (planeManager == null) return;

        foreach (ARPlane p in planeManager.trackables)
        {
            if (p.alignment == PlaneAlignment.Vertical)
            {
                paredesDetectadas.Add(p);
            }
            else
            {
                otrasSuperficiesDetectadas.Add(p);
            }
        }
    }

    private void PosicionarArcoEnfrente()
    {
        if (arco == null || cam == null) return;

        Vector3 adelante = cam.transform.forward;
        adelante.y = 0f;
        if (adelante.sqrMagnitude < 0.01f) adelante = Vector3.forward;
        adelante.Normalize();

        // Coloca el arco a 0.5m enfrente y 0.2m abajo de la cara
        Vector3 posArco = cam.transform.position + (adelante * 0.5f) + (Vector3.down * 0.2f);
        Quaternion rotArco = Quaternion.LookRotation(adelante);

        arco.transform.position = posArco;
        arco.transform.rotation = rotArco;
        arco.SetActive(true);

        // Reset de velocidades físicas por si tenía movimiento previo
        Rigidbody rb = arco.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void ComenzarJuego()
    {
        juegoIniciado = true;
        if (canvasGO != null) canvasGO.SetActive(false);

        PosicionarArcoEnfrente();
        MantenerDianas();

        if (gameTimer != null)
        {
            gameTimer.StartTimer();
        }
    }

    private void ComenzarPruebaFisica()
    {
        juegoIniciado = true;
        if (canvasGO != null) canvasGO.SetActive(false);

        PosicionarArcoEnfrente();
        MantenerDianasModoFuerzaBruta();

        if (gameTimer != null)
        {
            gameTimer.StartTimer();
        }
    }

    private void MantenerDianas()
    {
        int intentos = 0;
        while (activeTargets.Count < maxTargets && intentos < 40)
        {
            intentos++;

            // Regla: 70% probabilidad de paredes, 30% otras superficies (suelo, techo, mesa)
            bool buscarEnPared = (Random.value <= 0.70f && paredesDetectadas.Count > 0) || otrasSuperficiesDetectadas.Count == 0;
            List<ARPlane> listaOrigen = buscarEnPared ? paredesDetectadas : otrasSuperficiesDetectadas;

            if (listaOrigen.Count > 0)
            {
                ARPlane superficie = listaOrigen[Random.Range(0, listaOrigen.Count)];
                Vector2 tamano = superficie.size;

                Vector3 posLocal = new Vector3(
                    Random.Range(-tamano.x * 0.35f, tamano.x * 0.35f),
                    0f,
                    Random.Range(-tamano.y * 0.35f, tamano.y * 0.35f)
                );

                Vector3 normalSuperficie = superficie.normal;
                Vector3 posMundo = superficie.transform.TransformPoint(posLocal) + (normalSuperficie * wallOffset);

                if (!EsPosicionValida(posMundo)) continue;

                Quaternion rotacionBase = Quaternion.LookRotation(normalSuperficie);
                Quaternion rotacionFinal = rotacionBase * Quaternion.Euler(offsetRotacion);

                GameObject nuevaDiana = Instantiate(targetPrefab, posMundo, rotacionFinal);
                ConfigurarMovimiento(nuevaDiana, normalSuperficie, superficie, radioMovimiento);
                activeTargets.Add(nuevaDiana);
            }
            else
            {
                TrySpawnRaycastFisico();
            }
        }
    }

    private void MantenerDianasModoFuerzaBruta()
    {
        int intentos = 0;
        while (activeTargets.Count < maxTargets && intentos < 40)
        {
            intentos++;
            TrySpawnRaycastFisico();
        }
    }

    private bool TrySpawnRaycastFisico()
    {
        if (cam == null) return false;

        Vector3 direccion = Quaternion.Euler(
            Random.Range(-40f, 40f),
            Random.Range(0f, 360f),
            0f
        ) * Vector3.forward;

        Vector3 origen = cam.transform.position + (direccion * minSpawnDistance);

        if (Physics.Raycast(origen, direccion, out RaycastHit hit, 8f))
        {
            Vector3 pos = hit.point + (hit.normal * wallOffset);

            if (!EsPosicionValida(pos)) return false;

            Quaternion rotacionBase = Quaternion.LookRotation(hit.normal);
            Quaternion rotacionFinal = rotacionBase * Quaternion.Euler(offsetRotacion);

            GameObject nuevaDiana = Instantiate(targetPrefab, pos, rotacionFinal);
            ConfigurarMovimiento(nuevaDiana, hit.normal, null, Mathf.Min(radioMovimiento, radioMovimientoFisico));
            activeTargets.Add(nuevaDiana);
            return true;
        }

        return false;
    }

    private bool EsPosicionValida(Vector3 posicion)
    {
        if (cam != null && Vector3.Distance(cam.transform.position, posicion) < minSpawnDistance)
            return false;

        // Si las dianas se mueven, se dejan mas separadas para que no se encimen
        float separacion = minDistanceBetweenTargets + (VelocidadNivel() > 0f ? radioMovimiento : 0f);

        foreach (GameObject t in activeTargets)
        {
            if (t != null && Vector3.Distance(t.transform.position, posicion) < separacion)
                return false;
        }

        return true;
    }

    // ------------------------------------------------------------ DIFICULTAD

    private float VelocidadNivel()
    {
        // En multijugador, por ahora, siempre Facil
        if (NetworkManager.IsOnline) return 0f;

        switch (ConfiguracionNivel.Actual)
        {
            case Dificultad.Intermedio: return velocidadIntermedio;
            case Dificultad.Dificil: return velocidadDificil;
            default: return 0f; // Facil: no se mueven
        }
    }

    private void ConfigurarMovimiento(GameObject diana, Vector3 normal, ARPlane plano, float radio)
    {
        float velocidad = VelocidadNivel();
        if (velocidad <= 0f) return;

        DianaMovimientoAR mov = diana.GetComponent<DianaMovimientoAR>();
        if (mov == null) mov = diana.AddComponent<DianaMovimientoAR>();
        mov.Configurar(velocidad, radio, normal, plano);
    }

    private void OcultarPlanosVisuales()
    {
        if (planeManager == null) return;

        foreach (ARPlane p in planeManager.trackables)
        {
            foreach (Renderer r in p.GetComponentsInChildren<Renderer>())
                r.enabled = false;

            foreach (LineRenderer l in p.GetComponentsInChildren<LineRenderer>())
                l.enabled = false;

            Component visualizer = p.GetComponent("ARPlaneMeshVisualizer");
            if (visualizer is MonoBehaviour mb)
                mb.enabled = false;
        }
    }

    private void OnApplicationPause(bool pausa)
    {
        if (!pausa && capturando)
        {
            capturando = false;
            mensajeExtra = "Escaneo terminado. Buscando superficies...";
            proximoSondeo = 0f;
        }
    }

    // ------------------------------------------------------------ PANEL UI

    private void ActualizarPanelUI()
    {
        if (textoEstado == null) return;

        int totalSuperficies = paredesDetectadas.Count + otrasSuperficiesDetectadas.Count;
        bool haySuperficies = totalSuperficies > 0;

        botonComenzar.interactable = haySuperficies;
        etiquetaComenzar.color = haySuperficies ? Color.white : new Color(0.5f, 0.55f, 0.65f);

        string texto;
        if (capturando) 
            texto = "Sigue las instrucciones del visor y marca las superficies de tu habitación.";
        else if (haySuperficies) 
            texto = "Paredes: " + paredesDetectadas.Count + " | Otras superficies: " + otrasSuperficiesDetectadas.Count + "\nPresiona 'Comenzar Juego' para spawnear las dianas.";
        else 
            texto = "No se detectan superficies aún.\nPresiona 'Escanear Habitación' para marcar tu espacio en el visor.";

        if (!NetworkManager.IsOnline) texto += "\nNivel: " + ConfiguracionNivel.Nombre(ConfiguracionNivel.Actual);
        if (!string.IsNullOrEmpty(mensajeExtra)) texto += "\n" + mensajeExtra;
        textoEstado.text = texto;
    }

    private void ConstruirPanel()
    {
        canvasGO = new GameObject("PanelMRDianasCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;

        canvasGO.AddComponent<GraphicRaycaster>();

        System.Type raycasterType = System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit")
            ?? System.Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, UnityEngine.XR.Interaction.Toolkit");

        if (raycasterType != null)
        {
            canvasGO.AddComponent(raycasterType);
        }

        BoxCollider collider3D = canvasGO.AddComponent<BoxCollider>();
        collider3D.size = new Vector3(800f, 640f, 2f);

        RectTransform rt = canvasGO.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(800f, 640f);
        rt.localScale = Vector3.one * (anchoPanelMetros / 800f);

        Image fondo = CrearImagen(rt, "Fondo", new Color(0.04f, 0.06f, 0.11f, 0.92f));
        fondo.rectTransform.anchorMin = Vector2.zero;
        fondo.rectTransform.anchorMax = Vector2.one;
        fondo.rectTransform.offsetMin = Vector2.zero;
        fondo.rectTransform.offsetMax = Vector2.zero;

        CrearTexto(rt, "Titulo", "Configuración de Espacio AR/MR", 44, new Vector2(0f, 255f), new Vector2(760f, 80f), Color.white);
        textoEstado = CrearTexto(rt, "Estado", "", 28, new Vector2(0f, 130f), new Vector2(740f, 150f), new Color(0.75f, 0.85f, 1f));

        CrearBoton(rt, "Escanear Habitación", new Vector2(0f, 10f), new Vector2(560f, 80f), 36, SolicitarEscaneo);

        botonComenzar = CrearBoton(rt, "Comenzar Juego (Usar Entorno)", new Vector2(0f, -80f), new Vector2(560f, 80f), 32, ComenzarJuego);
        etiquetaComenzar = botonComenzar.GetComponentInChildren<TextMeshProUGUI>();

        CrearBoton(rt, "Probar sin Escanear (Física)", new Vector2(0f, -180f), new Vector2(560f, 70f), 28, ComenzarPruebaFisica);

        ActualizarPanelUI();
    }

    private static Image CrearImagen(Transform padre, string nombre, Color color)
    {
        GameObject g = new GameObject(nombre);
        g.transform.SetParent(padre, false);
        Image img = g.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI CrearTexto(Transform padre, string nombre, string texto, float tamano, Vector2 pos, Vector2 size, Color color)
    {
        GameObject g = new GameObject(nombre);
        g.transform.SetParent(padre, false);

        TextMeshProUGUI t = g.AddComponent<TextMeshProUGUI>();
        t.text = texto;
        t.fontSize = tamano;
        t.alignment = TextAlignmentOptions.Center;
        t.color = color;
        t.raycastTarget = false;

        RectTransform r = t.rectTransform;
        r.sizeDelta = size;
        r.anchoredPosition = pos;
        return t;
    }

    private static Button CrearBoton(Transform padre, string texto, Vector2 pos, Vector2 size, float fuente, UnityEngine.Events.UnityAction alHacerClic)
    {
        GameObject g = new GameObject("Boton_" + texto);
        g.transform.SetParent(padre, false);

        Image img = g.AddComponent<Image>();
        img.color = colorBoton;

        Button boton = g.AddComponent<Button>();
        boton.targetGraphic = img;
        boton.onClick.AddListener(alHacerClic);

        RectTransform r = img.rectTransform;
        r.sizeDelta = size;
        r.anchoredPosition = pos;

        CrearTexto(g.transform, "Texto", texto, fuente, Vector2.zero, size, Color.white);
        return boton;
    }
}