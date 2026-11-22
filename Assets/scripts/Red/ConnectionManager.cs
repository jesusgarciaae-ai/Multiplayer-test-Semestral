using TMPro;
using UnityEngine;

// Va en el Canvas del MENU. Conecta los botones "Crear sala" y "Unirse".
public class ConnectionManager : MonoBehaviour
{
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private TMP_Text roomCodeLabel; // opcional: muestra el codigo generado
    [SerializeField] private TMP_Text errorLabel;    // opcional: muestra errores de conexion
    [SerializeField] private GameObject panelNiveles; // panel Facil / Intermedio / Dificil (jugar solo)

    private void Start()
    {
        if (panelNiveles != null) panelNiveles.SetActive(false);

        if (NetworkManager.Instance == null)
        {
            Debug.LogError("ConnectionManager: no hay NetworkManager en la escena del menu.");
            return;
        }

        // Si regresamos al menu por un error, lo mostramos
        ShowError(NetworkManager.Instance.LastErrorMessage);
        NetworkManager.Instance.ErrorRaised += ShowError;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.ErrorRaised -= ShowError;
    }

    public void CreateRoom()
    {
        string code = GenerateRoomCode();

        // Mostramos el codigo para que el otro jugador lo pueda usar
        if (inputField != null) inputField.text = code;
        if (roomCodeLabel != null) roomCodeLabel.text = "Codigo de sala: " + code;
        Debug.Log("Sala creada con codigo: " + code);

        NetworkManager.Instance.CreateSession(code);
    }

    public void JoinRoom()
    {
        string code = inputField != null ? inputField.text.Trim().ToUpper() : "";

        if (string.IsNullOrEmpty(code))
        {
            Debug.LogWarning("Escribe el codigo de sala para unirte.");
            ShowError("Escribe el codigo de sala para unirte.");
            return;
        }

        NetworkManager.Instance.JoinSession(code);
    }

    // Boton "Jugar solo": muestra el panel para elegir dificultad
    public void MostrarNiveles()
    {
        if (panelNiveles == null)
        {
            PlaySolo(); // sin panel: entra directo con la dificultad actual
            return;
        }
        ShowError(null);
        panelNiveles.SetActive(true);
    }

    public void OcultarNiveles()
    {
        if (panelNiveles != null) panelNiveles.SetActive(false);
    }

    // Botones del panel: 0 = Facil, 1 = Intermedio, 2 = Dificil
    public void ElegirNivel(int nivel)
    {
        ConfiguracionNivel.Actual = (Dificultad)Mathf.Clamp(nivel, 0, 2);
        PlaySolo();
    }

    // Jugar solo, sin conectarse (no necesita internet ni companero)
    public void PlaySolo()
    {
        if (NetworkManager.Instance != null)
            NetworkManager.Instance.PlayOffline();
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene("pofa");
    }

    // Teclado virtual del menu (en el visor no siempre aparece el teclado del sistema)
    public void AddChar(string c)
    {
        if (inputField == null || inputField.text.Length >= 4) return;
        inputField.text += c;
    }

    public void DeleteChar()
    {
        if (inputField == null || inputField.text.Length == 0) return;
        inputField.text = inputField.text.Substring(0, inputField.text.Length - 1);
    }

    private void ShowError(string message)
    {
        if (errorLabel == null) return;
        errorLabel.text = message ?? "";
        errorLabel.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    private string GenerateRoomCode()
    {
        // Sin letras confusas (O/0, I/1) para que sea facil de leer en VR
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        char[] code = new char[4];
        for (int i = 0; i < code.Length; i++)
            code[i] = chars[Random.Range(0, chars.Length)];
        return new string(code);
    }
}
