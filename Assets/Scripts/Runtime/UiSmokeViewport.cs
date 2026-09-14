using System;
using UnityEngine;

namespace Riverworks
{
    /// <summary>Test-only offscreen viewport that renders the world and uGUI at an exact pixel size.</summary>
    public sealed class UiSmokeViewport : IDisposable
    {
        readonly Camera camera;
        readonly Canvas canvas;
        readonly RenderTexture previousTarget;
        readonly Rect previousCameraRect;
        readonly float previousAspect;
        readonly RenderMode previousRenderMode;
        readonly Camera previousWorldCamera;
        readonly float previousPlaneDistance;
        RenderTexture target;
        bool disposed;

        public int Width { get; }
        public int Height { get; }
        public Vector2 Center => new Vector2(Width * .5f, Height * .5f);

        public UiSmokeViewport(Camera renderCamera, Canvas hudCanvas, int width, int height)
        {
            if (renderCamera == null) throw new ArgumentNullException(nameof(renderCamera));
            if (hudCanvas == null) throw new ArgumentNullException(nameof(hudCanvas));
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));

            camera = renderCamera;
            canvas = hudCanvas;
            Width = width;
            Height = height;
            previousTarget = camera.targetTexture;
            previousCameraRect = camera.rect;
            previousAspect = camera.aspect;
            previousRenderMode = canvas.renderMode;
            previousWorldCamera = canvas.worldCamera;
            previousPlaneDistance = canvas.planeDistance;

            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "UI smoke viewport " + width + "x" + height,
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            target.Create();
            if (!target.IsCreated()) throw new InvalidOperationException("Could not create UI smoke render target");

            camera.targetTexture = target;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.aspect = width / (float)height;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = Mathf.Clamp(camera.nearClipPlane + .1f,
                camera.nearClipPlane + .01f, camera.farClipPlane - .1f);
            Canvas.ForceUpdateCanvases();

            Rect pixels = canvas.pixelRect;
            if (camera.pixelWidth != width || camera.pixelHeight != height ||
                Mathf.RoundToInt(pixels.width) != width || Mathf.RoundToInt(pixels.height) != height)
            {
                Dispose();
                throw new InvalidOperationException("Offscreen UI viewport did not adopt the requested " +
                    width + "x" + height + " pixels");
            }
        }

        public Texture2D Capture()
        {
            if (disposed || target == null) throw new ObjectDisposedException(nameof(UiSmokeViewport));
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                texture.Apply(false, false);
                return texture;
            }
            finally { RenderTexture.active = previousActive; }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (canvas != null)
            {
                canvas.renderMode = previousRenderMode;
                canvas.worldCamera = previousWorldCamera;
                canvas.planeDistance = previousPlaneDistance;
            }
            if (camera != null)
            {
                camera.targetTexture = previousTarget;
                camera.rect = previousCameraRect;
                camera.aspect = previousAspect;
            }
            Canvas.ForceUpdateCanvases();
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.Destroy(target);
                target = null;
            }
        }
    }
}
