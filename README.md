# AR Face Tracking Filter (Unity)

Unity project that runs **AR face tracking** on device (Android + AR Foundation) and a **desktop Editor / Windows** mode with a **webcam**, stand-in face mesh, and **cycling face filters** (props and full-face overlays).

**Unity:** 2022.3 LTS (tested with **2022.3.62f3**; **2022.3.10f1** and newer 2022.3.x builds are expected to work).

**Render pipeline:** **Built-in Render Pipeline** (`ProjectSettings` use the default pipeline, not URP/HDRP).

---

## Quick start

1. Open this folder in **Unity Hub** and open the project in **Unity 2022.3 LTS**.
2. Open **`Assets/Scenes/ARFaceFilter.unity`**.
3. Press **Play**:
   - **`Editor Webcam Face Filter`** runs **`EditorArFaceSchoolDemo`**: webcam backdrop, optional face pose from skin tracking, and **`Next Filter`** UI to cycle **`filterPrefabs`**.
4. For a **device build**, use **File → Build Settings**, add the open scene, switch to **Android**, and **Build** (see prerequisites below).

---

## Project layout (high level)

| Area | Purpose |
|------|--------|
| `Assets/Scenes/ARFaceFilter.unity` | Main scene: camera, backdrop, AR / editor demo wiring. |
| `Assets/Scripts/EditorArFaceSchoolDemo.cs` | Editor Play: filters, webcam backdrop, stand-in face, filter UI. |
| `Assets/Scripts/EditorFilterPlacement.cs` | Attach points for filter prefabs (`FaceSurface`, nose, eyes, etc.). |
| `Assets/Scripts/WebcamSkinFacePoseEstimator.cs` | Optional 2D webcam-based pose when not using ARKit/ARCore. |
| `Assets/Shaders/FaceMaskTransparent.shader` | Full-face decal shader (`ARFaceFilter/FaceMaskTransparent`); optional **black keyed** alpha for RGB art. |
| `Assets/_BasicFaceFilter/Prefabs/` | Filters: ears, eyes, props, **`Face_Reference_Mesh`**, **`Props_Face_Waves`**, etc. |
| `Assets/_BasicFaceFilter/Materials/` | Shared materials (`PropsMaterial`, overlays, particles). |
| `Assets/_BasicFaceFilter/Textures/` | Face decals, props PBR maps, flipbooks. |

---

## Adding or changing filters

1. Duplicate an existing filter under **`Assets/_BasicFaceFilter/Prefabs/`** or create a prefab with a **`SkinnedMeshRenderer`** / **`MeshRenderer`** on the face mesh (see **`Props_Face_Waves`** + **`Face_Reference_Mesh`**).
2. Add **`EditorFilterPlacement`** on the root and set **`Attach To`** (e.g. **`FaceSurface`** for a full-face overlay, **Nose** / **Mouth** for anchored props).
3. Select the GameObject with **`EditorArFaceSchoolDemo`** and append your prefab to **`Filter Prefabs`**.
4. For **RGB art on black** (no alpha in the file), use **`ARFaceFilter/FaceMaskTransparent`** and enable **Key Black To Alpha** on the material (see **`Face_Waves_FaceOverlay`**).

---

## Custom video backdrop (Editor / stand-in)

Used with **`EditorVideoBackdrop`** and **`VideoPlayer`** in the scene.

1. Import your clip into **`Assets/`** (avoid huge files in Git if you collaborate; use LFS or keep clips local).
2. Select the video asset. For **Android** playback issues, in the importer use a supported format; for **VP8** transcoding, adjust per platform in the **Video Clip Importer** if Unity logs codec errors.
3. Assign the clip on the **`VideoPlayer`** component referenced by your backdrop / scene setup.
4. **`File → Build Settings`**: add **`ARFaceFilter`**, pick **Android**, then **Build** or **Build And Run**.

---

## Android prerequisites

- **Android SDK & NDK** installed and configured in Unity (**Edit → Preferences → External Tools**).
- **AR Foundation** packages and platform support (ARCore) as already set up in **`Packages/manifest.json`**.
- A device that supports **ARCore** for the full AR face path.

---

## License & credits

Maintained as a learning / portfolio project. Replace this section if you add a formal license or third-party notices.
