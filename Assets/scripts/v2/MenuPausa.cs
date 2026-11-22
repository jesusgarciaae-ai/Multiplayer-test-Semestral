using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuPausa : MonoBehaviour
{
    [Header("Paneles")]
    public GameObject panelPausa;
    [Tooltip("Objeto del selector de movimiento (movSelect). Opcional.")]
    public GameObject panelMovimiento;

    [Header("Botones del panel de pausa")]
    public Button botonReanudar;     // "Button"
    public Button botonMovimiento;   // "mov"
    public Button botonSalir;        // "Exit_game"
    public Button botonReiniciar;    // opcional
    [Tooltip("Botón para volver desde el panel de movimiento. Opcional.")]
    public Button botonVolverDeMovimiento;

    [Header("VR")]
    [Tooltip("Acción del botón Menu del control (XRI Left/Menu o similar).")]
    public InputActionReference accionPausa;
    [Tooltip("Normalmente la Main Camera del XR Origin.")]
    public Transform camaraJugador;
    public float distanciaAlJugador = 1.5f;

    [Header("Salir")]
    [Tooltip("Vacio o una escena que no esta en Build Profiles = la escena 0 (MenuMultijugador)")]
    public string escenaMenu = "";
    [Tooltip("Si está activo, el botón Salir cierra la aplicación en vez de ir al menú.")]
    public bool salirCierraApp = false;

    private bool juegoPausado = false;

    void Awake()
    {
        if (botonReanudar != null) botonReanudar.onClick.AddListener(ReanudarJuego);
        if (botonMovimiento != null) botonMovimiento.onClick.AddListener(AbrirPanelMovimiento);
        if (botonSalir != null) botonSalir.onClick.AddListener(Salir);
        if (botonReiniciar != null) botonReiniciar.onClick.AddListener(ReiniciarNivel);
        if (botonVolverDeMovimiento != null) botonVolverDeMovimiento.onClick.AddListener(VolverDePanelMovimiento);
    }

    void OnEnable()
    {
        if (accionPausa != null)
        {
            accionPausa.action.Enable();
            accionPausa.action.performed += OnPausaPresionada;
        }
    }

    void OnDisable()
    {
        if (accionPausa != null)
            accionPausa.action.performed -= OnPausaPresionada;
    }

    void Start()
    {
        panelPausa.SetActive(false);
        if (panelMovimiento != null) panelMovimiento.SetActive(false);
        Time.timeScale = 1f;

        if (NetworkManager.IsOnline)
            OcultarBotonesReiniciar();
    }

    // En multijugador se esconden los botones que llaman a ReiniciarNivel
    private void OcultarBotonesReiniciar()
    {
        foreach (Button b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (b == botonReiniciar) { b.gameObject.SetActive(false); continue; }

            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
            {
                if (b.onClick.GetPersistentMethodName(i) == nameof(ReiniciarNivel))
                {
                    b.gameObject.SetActive(false);
                    break;
                }
            }
        }
    }

    private void OnPausaPresionada(InputAction.CallbackContext ctx)
    {
        if (juegoPausado) ReanudarJuego();
        else PausarJuego();
    }

    public void PausarJuego()
    {
        ColocarPanelFrenteAlJugador(panelPausa.transform);
        panelPausa.SetActive(true);
        Time.timeScale = 0f;
        juegoPausado = true;
    }

    public void ReanudarJuego()
    {
        panelPausa.SetActive(false);
        if (panelMovimiento != null) panelMovimiento.SetActive(false);
        Time.timeScale = 1f;
        juegoPausado = false;
    }

    public void AbrirPanelMovimiento()
    {
        if (panelMovimiento == null) return;
        ColocarPanelFrenteAlJugador(panelMovimiento.transform);
        panelPausa.SetActive(false);
        panelMovimiento.SetActive(true);
    }

    public void VolverDePanelMovimiento()
    {
        if (panelMovimiento != null) panelMovimiento.SetActive(false);
        panelPausa.SetActive(true);
    }

    public void ReiniciarNivel()
    {
        Time.timeScale = 1f;

        // En multijugador no se puede recargar la escena (se romperia la conexion y la
        // alineacion con el otro visor). El reinicio en red se hara sincronizado mas adelante.
        if (NetworkManager.IsOnline)
        {
            Debug.LogWarning("MenuPausa: reiniciar no esta disponible en multijugador todavia.");
            ReanudarJuego();
            return;
        }

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void Salir()
    {
        Time.timeScale = 1f;

        if (salirCierraApp)
        {
            Application.Quit();
            return;
        }

        IrAlMenu(escenaMenu);
    }

    // Vuelve al menu. En multijugador primero se sale de la sala (cierra la conexion).
    // Si la escena "escenaMenu" no esta en Build Profiles, usa la escena 0 (el menu).
    public static void IrAlMenu(string escena)
    {
        Time.timeScale = 1f;

        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.LeaveSession();
            return;
        }

        if (!string.IsNullOrEmpty(escena) && Application.CanStreamedLevelBeLoaded(escena))
            SceneManager.LoadScene(escena);
        else
            SceneManager.LoadScene(0);
    }

    public bool EstaPausado()
    {
        return juegoPausado;
    }

    // Coloca el panel (Canvas en World Space) delante de la mirada, mirando al jugador.
    private void ColocarPanelFrenteAlJugador(Transform panel)
    {
        if (camaraJugador == null) return;

        Vector3 adelante = camaraJugador.forward;
        adelante.y = 0f;
        if (adelante.sqrMagnitude < 0.001f) adelante = Vector3.forward;
        adelante.Normalize();

        panel.position = camaraJugador.position + adelante * distanciaAlJugador;
        panel.rotation = Quaternion.LookRotation(panel.position - camaraJugador.position);
    }
}