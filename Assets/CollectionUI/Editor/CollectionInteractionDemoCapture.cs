using System.IO;
using CollectionUI.Samples;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CollectionUI.Editor
{
    /// <summary>交互页静态截图；通过公开请求入口展示 loading，不改写组件私有状态。</summary>
    public static class CollectionInteractionDemoCapture
    {
        public static void Capture()
        {
            CollectionInteractionDemoSeeder.Generate();
            string directory = Path.GetFullPath("Docs/CollectionUIVerification");
            Directory.CreateDirectory(directory);
            foreach (var size in new[]
            {
                new Vector2Int(720, 1280),
                new Vector2Int(1080, 1920),
                new Vector2Int(1080, 2400)
            }

            )
            {
                CaptureOne(directory, size.x, size.y, "idle", false);
                CaptureOne(directory, size.x, size.y, "refresh", true);
                CaptureOne(directory, size.x, size.y, "load-more", true);
            }
        }

        static void CaptureOne(string directory, int width, int height, string state, bool safeArea)
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CollectionInteractionDemoSeeder.PrefabPath));
            var cameraObject = new GameObject("InteractionCaptureCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .055f, .085f);
            var target = new RenderTexture(width, height, 24);
            target.Create();
            camera.targetTexture = target;
            var previous = RenderTexture.active;
            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                // 指定参考比例，避免批处理 Game View 尺寸干扰离屏截图。
                root.GetComponent<CanvasScaler>().enabled = false;
                canvas.scaleFactor = width / 1080f;
                var safe = root.GetComponentInChildren<DemoSafeArea>();
                safe.enabled = false;
                var rect = (RectTransform)safe.transform;
                rect.anchorMin = safeArea ? new Vector2(0, .035f) : Vector2.zero;
                rect.anchorMax = safeArea ? new Vector2(1, .94f) : Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                Canvas.ForceUpdateCanvases();
                var demo = root.GetComponent<CollectionInteractionDemo>();
                demo.Initialize();
                var scroll = demo.Collection.ScrollRect;
                if (state == "refresh")
                {
                    scroll.content.anchoredPosition = new Vector2(0, -demo.Interaction.TriggerDistance);
                    demo.Interaction.BeginRefresh();
                }
                else if (state == "load-more")
                {
                    float bottom = Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height);
                    scroll.content.anchoredPosition = new Vector2(0, bottom + demo.Interaction.TriggerDistance);
                    demo.Interaction.BeginLoadMore();
                }

                demo.Collection.RefreshVisible();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(directory, "interaction-" + width + "x" + height + "-" + state + (safeArea ? "-safe" : "") + ".png"), image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
