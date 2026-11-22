
using UnityEngine;
using TMPro;

public class ContadorDianas : MonoBehaviour
{
    public TMP_Text textoContador;

    public GameObject uiVictoria;
    public GameObject uiInGame;

    private int dianasDerribadas = 0;

    private void Start()
    {
        ActualizarTexto();

        // Estado inicial de las interfaces
        uiVictoria.SetActive(false);
        uiInGame.SetActive(true);
    }

    public void SumarDiana()
    {
        dianasDerribadas++;
        ActualizarTexto();

        // Comprobar si se alcanzaron las 10 dianas
        if (dianasDerribadas >= 10)
        {
            MostrarVictoria();
        }
    }

    private void ActualizarTexto()
    {
        textoContador.text = dianasDerribadas.ToString();
    }

    private void MostrarVictoria()
    {
        uiVictoria.SetActive(true);
        uiInGame.SetActive(false);
    }
}
