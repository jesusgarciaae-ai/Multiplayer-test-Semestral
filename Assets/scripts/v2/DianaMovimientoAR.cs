using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Lo agrega EspacioTrabajoDianasMR a cada diana cuando el nivel es Intermedio o Dificil.
/// - La diana se desliza en una direccion al azar SOBRE su superficie (pared, piso, mesa):
///   nunca se mete en la pared ni se despega de ella.
/// - Cuando llega a su destino elige otro punto al azar, dentro de un circulo alrededor de
///   donde nacio y sin salirse del plano detectado.
/// - Se mueve con un Rigidbody cinematico para que la flecha la detecte bien aunque se mueva.
/// - Usa el tiempo de fisica, asi que se detiene sola en la pausa (timeScale = 0).
/// </summary>
public class DianaMovimientoAR : MonoBehaviour
{
    [Tooltip("Metros por segundo")]
    [SerializeField] private float velocidad = 0.25f;
    [Tooltip("Que tan lejos (metros) se puede alejar de donde nacio")]
    [SerializeField] private float radio = 0.5f;
    [Tooltip("Que parte del plano puede usar (1 = hasta la orilla). Menos = deja margen")]
    [SerializeField, Range(0.3f, 1f)] private float margenPlano = 0.8f;

    private Rigidbody rb;
    private ARPlane plano;
    private Vector3 centro;
    private Vector3 ejeU, ejeV; // dos direcciones sobre la superficie
    private Vector3 destino;
    private bool configurada;

    /// <param name="normal">Normal de la superficie donde esta la diana</param>
    /// <param name="planoSuperficie">Plano AR donde nacio (puede ser null en el modo fisico)</param>
    public void Configurar(float velocidad, float radio, Vector3 normal, ARPlane planoSuperficie)
    {
        this.velocidad = velocidad;
        this.radio = radio;
        plano = planoSuperficie;
        centro = transform.position;

        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // Dos ejes perpendiculares a la normal = direcciones "sobre" la superficie
        normal.Normalize();
        ejeU = Vector3.Cross(normal, Vector3.up);
        if (ejeU.sqrMagnitude < 0.01f) ejeU = Vector3.Cross(normal, Vector3.forward); // piso / techo
        ejeU.Normalize();
        ejeV = Vector3.Cross(normal, ejeU).normalized;

        destino = NuevoDestino();
        configurada = true;
    }

    private void FixedUpdate()
    {
        if (!configurada || velocidad <= 0f) return;

        Vector3 actual = rb.position;
        Vector3 siguiente = Vector3.MoveTowards(actual, destino, velocidad * Time.fixedDeltaTime);
        rb.MovePosition(siguiente);

        if ((siguiente - destino).sqrMagnitude < 0.0001f)
            destino = NuevoDestino();
    }

    // Punto al azar dentro del circulo, en cualquier direccion, sin salirse del plano
    private Vector3 NuevoDestino()
    {
        for (int i = 0; i < 15; i++)
        {
            Vector2 r = Random.insideUnitCircle * radio;
            Vector3 p = centro + ejeU * r.x + ejeV * r.y;

            // Que no elija un destino demasiado cerca (se veria "temblando")
            if ((p - transform.position).sqrMagnitude < radio * radio * 0.09f) continue;

            if (DentroDelPlano(p)) return p;
        }
        return centro;
    }

    private bool DentroDelPlano(Vector3 p)
    {
        if (plano == null) return true; // modo fisico o el plano ya no existe

        Vector3 local = plano.transform.InverseTransformPoint(p);
        Vector2 c = plano.centerInPlaneSpace;
        Vector2 mitad = plano.size * 0.5f * margenPlano;

        return Mathf.Abs(local.x - c.x) <= mitad.x && Mathf.Abs(local.z - c.y) <= mitad.y;
    }
}
