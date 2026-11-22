using Unity.XR.CoreUtils;
using UnityEngine;

// Va en el XR Origin de la escena de juego.
// Solo guarda las referencias al cuerpo REAL del jugador (XR Origin, camara y controles).
// NetworkRig las lee para publicarlas en la red.
// Si dejas los campos vacios se buscan solos.
public class HardwareRig : MonoBehaviour
{
    [Tooltip("Vacio = el XR Origin")]
    public Transform playerTransform;

    [Tooltip("Vacio = la camara del XR Origin")]
    public Transform headTransform;

    public Transform leftHandTransform;

    public Transform rightHandTransform;

    [Header("Busqueda automatica de las manos")]
    [SerializeField] private string leftHandName = "Left Controller";
    [SerializeField] private string rightHandName = "Right Controller";

    public bool IsValid =>
        playerTransform != null && headTransform != null &&
        leftHandTransform != null && rightHandTransform != null;

    private void Awake()
    {
        AutoFill();

        if (!IsValid)
            Debug.LogError("HardwareRig: faltan referencias (XR Origin, camara o controles). Asignalas en el Inspector.");
    }

    private void AutoFill()
    {
        XROrigin origin = GetComponentInParent<XROrigin>();
        if (origin == null) origin = FindFirstObjectByType<XROrigin>();

        if (playerTransform == null)
            playerTransform = origin != null ? origin.transform : transform;

        if (headTransform == null)
        {
            if (origin != null && origin.Camera != null) headTransform = origin.Camera.transform;
            else if (Camera.main != null) headTransform = Camera.main.transform;
        }

        if (leftHandTransform == null) leftHandTransform = FindChildByName(playerTransform, leftHandName);
        if (rightHandTransform == null) rightHandTransform = FindChildByName(playerTransform, rightHandName);
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (root == null) return null;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
                return child;
        }
        return null;
    }
}
