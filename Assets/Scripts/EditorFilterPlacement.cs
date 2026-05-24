using UnityEngine;

/// <summary>
/// Labels where an Editor-play filter prefab should parent on the canonical AR face rig.
/// If missing, spawning falls back to the legacy Props attach point / face root behavior.
/// </summary>
public enum EditorFaceFilterAttach
{
    /// <summary>Use the legacy Props_Attach_Point (or nearest fallback).</summary>
    PropsFallback = 0,
    Head,
    Eyes,
    Nose,
    Mouth,
    /// <summary>Parent to the AR Default Face root (full-face decal / overlay).</summary>
    FaceSurface,
}

[DisallowMultipleComponent]
public sealed class EditorFilterPlacement : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Head hats/ears · Eyes glasses · Nose · Mouth · FaceSurface = AR face mesh root · PropsFallback = Props_Attach_Point.")]
    EditorFaceFilterAttach attachTo = EditorFaceFilterAttach.PropsFallback;

    /// <inheritdoc cref="EditorFaceFilterAttach"/>
    public EditorFaceFilterAttach AttachTo => attachTo;
}
