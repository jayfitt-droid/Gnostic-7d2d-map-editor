using System.Globalization;
using MapEditor.Core.GameLink;
using UnityEngine;

namespace GnosticGameLink;

/// <summary>Captures on Unity's main thread and restores the view before reporting the result.</summary>
internal static class GameLinkCapture
{
    private const float MinimumRenderSeconds = 4f;
    private const float StableRenderSeconds = 1f;
    private const float RenderCheckInterval = 0.25f;
    private const int MaxChunkViewRadius = 12;
    internal const float MaximumRenderSeconds = 20f;

    public static string Failure => GameLinkReply.Error("picture_failed", "The game could not take the picture.");

    public static IEnumerator<object> Take(World world, EntityPlayerLocal player, GameLinkRequest request,
        Vector3i min, Vector3i max, TaskCompletionSource<string> reply)
    {
        GUIWindowManager? hud = LocalPlayerUI.GetUIForPrimaryPlayer()?.windowManager;
        Camera camera = player.playerCamera;
        if (hud == null || camera == null)
            yield break;

        Transform view = camera.transform;
        Transform parent = view.parent;
        Vector3 oldPosition = parent == null ? view.position + Origin.position : view.localPosition;
        Quaternion oldRotation = view.localRotation;
        int sibling = view.GetSiblingIndex();
        int oldMask = camera.cullingMask;
        float oldFov = camera.fieldOfView;
        float oldOverrideFov = player.OverrideFOV;
        float lastOverrideFov = player.lastOverrideFOV;
        Vector3 oldOverrideLook = player.OverrideLookAt;
        bool wasLocked = player.vp_FPCamera.Locked3rdPerson;
        bool hudHidden = false;
        bool detached = false;
        string result;
        var position = new Vector3(request.X, request.Y, request.Z);
        var look = new Vector3(request.LookX, request.LookY, request.LookZ);
        Vector3 direction = look - position;
        Vector3 up = direction.x == 0 && direction.z == 0 ? Vector3.forward : Vector3.up;
        Quaternion rotation = Quaternion.LookRotation(direction, up);
        ChunkManager chunkManager = world.m_ChunkManager;
        ChunkCluster chunks = world.ChunkCache;
        var targetChunks = TargetChunks(chunks, look, min, max);
        int lookChunkX = World.toChunkXZ(request.LookX), lookChunkZ = World.toChunkXZ(request.LookZ);
        int radiusX = Math.Max(lookChunkX - targetChunks.MinX, targetChunks.MaxX - lookChunkX);
        int radiusZ = Math.Max(lookChunkZ - targetChunks.MinZ, targetChunks.MaxZ - lookChunkZ);
        int viewRadius = Math.Min(MaxChunkViewRadius, Math.Max(radiusX, radiusZ) + 1);
        ChunkManager.ChunkObserver? observer = null;
        try
        {
            // The player normally sets which chunks get full meshes. Request those meshes at the photo target as well.
            observer = chunkManager.AddChunkObserver(look, true, viewRadius, -1);
            hudHidden = true;
            hud.TempHUDDisable();
            detached = true;
            player.SetCameraAttachedToPlayer(false, true);
            player.ShowHoldingItemLayer(false);
            float halfField = Mathf.Tan(GameLinkProtocol.PictureVerticalFov * Mathf.Deg2Rad / 2);
            float aspectScale = Mathf.Max(1, GameLinkProtocol.PictureMinimumAspect / camera.aspect);
            float pictureFov = 2 * Mathf.Atan(halfField * aspectScale) * Mathf.Rad2Deg;
            player.OverrideFOV = pictureFov;
            player.OverrideLookAt = look;
            float start = Time.realtimeSinceStartup;
            float nextCheck = MinimumRenderSeconds - StableRenderSeconds, readySince = -1f;
            bool rendered = false;
            while (Time.realtimeSinceStartup < start + MaximumRenderSeconds)
            {
                if (player == null || view == null || GameManager.Instance?.World != world || player.IsDead())
                    yield break;
                camera.fieldOfView = pictureFov;
                view.SetPositionAndRotation(position - Origin.position, rotation);
                float elapsed = Time.realtimeSinceStartup - start;
                if (elapsed >= nextCheck)
                {
                    nextCheck = elapsed + RenderCheckInterval;
                    if (!TargetChunksAreDrawn(chunks, targetChunks))
                        readySince = -1f;
                    else if (readySince < 0f)
                        readySince = elapsed;
                    else if (elapsed - readySince >= StableRenderSeconds)
                    {
                        rendered = true;
                        break;
                    }
                }
                yield return null!;
            }

            if (rendered)
            {
                yield return new WaitForEndOfFrame();
                if (player == null || view == null || GameManager.Instance?.World != world || player.IsDead())
                    yield break;
                result = SaveFrame();
            }
            else
            {
                Log.Warning($"[Gnostic Game Link] Target chunks around {look} did not finish drawing in {MaximumRenderSeconds} seconds.");
                result = GameLinkReply.Error("picture_render_timeout", "The target did not finish rendering in time.");
            }
        }
        finally
        {
            try
            {
                if (observer != null) chunkManager.RemoveChunkObserver(observer);
            }
            finally
            {
                RestoreView();
            }
        }
        reply.TrySetResult(result);

        void RestoreView()
        {
            try
            {
                if (detached && view != null)
                {
                    view.SetParent(parent, false);
                    view.SetSiblingIndex(sibling);
                    view.SetLocalPositionAndRotation(parent == null ? oldPosition - Origin.position : oldPosition, oldRotation);
                }
                if (detached && player != null)
                {
                    player.OverrideFOV = oldOverrideFov;
                    player.lastOverrideFOV = lastOverrideFov;
                    player.OverrideLookAt = oldOverrideLook;
                    if (player.vp_FPCamera != null) player.vp_FPCamera.Locked3rdPerson = wasLocked;
                }
            }
            finally
            {
                if (camera != null)
                {
                    camera.cullingMask = oldMask;
                    camera.fieldOfView = oldFov;
                }
                if (hudHidden && hud != null) hud.ReEnableHUD();
            }
        }
    }

    private static string SaveFrame()
    {
        int width = Screen.width, height = Screen.height;
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        byte[] png;
        try
        {
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            pixels.Apply();
            png = pixels.EncodeToPNG();
        }
        finally
        {
            UnityEngine.Object.Destroy(pixels);
        }
        if (png == null || png.Length == 0) return Failure;

        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "7DaysToDie", "GnosticGameLink", "Pictures");
        Directory.CreateDirectory(folder);
        string file = "gnostic-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".png";
        using var output = new FileStream(Path.Combine(folder, file), FileMode.CreateNew, FileAccess.Write);
        output.Write(png, 0, png.Length);
        return GameLinkReply.PictureSaved(file);
    }

    private static (int MinX, int MinZ, int MaxX, int MaxZ) TargetChunks(ChunkCluster chunks, Vector3 look, Vector3i min, Vector3i max)
    {
        // The game looks up the actual placed POI. A target in open terrain uses the editor's 32 m spot.
        var prefab = chunks.ChunkProvider.GetDynamicPrefabDecorator()?.GetPrefabAtPosition(look);
        var start = prefab?.boundingBoxPosition ?? new Vector3i((int)look.x - 16, 0, (int)look.z - 16);
        var size = prefab?.boundingBoxSize ?? new Vector3i(32, 1, 32);
        return (World.toChunkXZ(Math.Max(min.x, start.x)), World.toChunkXZ(Math.Max(min.z, start.z)),
            World.toChunkXZ(Math.Min(max.x, start.x + size.x - 1)), World.toChunkXZ(Math.Min(max.z, start.z + size.z - 1)));
    }

    private static bool TargetChunksAreDrawn(ChunkCluster chunks, (int MinX, int MinZ, int MaxX, int MaxZ) area)
    {
        for (int x = area.MinX; x <= area.MaxX; x++)
        for (int z = area.MinZ; z <= area.MaxZ; z++)
        {
            if (chunks.GetChunkSync(x, z) is not
                { IsDisplayed: true, InProgressRegeneration: false, InProgressCopying: false, displayState: Chunk.DisplayState.Done })
                return false;
        }
        return true;
    }
}
