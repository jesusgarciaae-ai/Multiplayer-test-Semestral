// Guarda la dificultad elegida en el menu. Es estatica, asi que sobrevive al cambio de escena
// (y a "Reiniciar"). Si abres pofa directo en el Editor, vale Facil.
public enum Dificultad
{
    Facil,       // las dianas no se mueven
    Intermedio,  // se mueven lento
    Dificil      // se mueven un poco mas rapido
}

public static class ConfiguracionNivel
{
    public static Dificultad Actual { get; set; } = Dificultad.Facil;

    public static string Nombre(Dificultad d)
    {
        switch (d)
        {
            case Dificultad.Intermedio: return "Intermedio";
            case Dificultad.Dificil: return "Dificil";
            default: return "Facil";
        }
    }
}
