// 普通节点演示截图工具。包含逻辑窄屏、长屏、模拟安全区、固定高度，以及保留正式 CanvasScaler 的截图。
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CollectionUI.Samples;

namespace CollectionUI.Editor
{
    public static class ExpandedCollectionDemoCapture
    {
        public static void Capture()
        {
            ExpandedCollectionDemoSeeder.Generate();
            CaptureOne(720, 1280);
            CaptureOne(1080, 1920);
            CaptureOne(1080, 2400);
            CaptureOne(720, 1280, 0, 0, false, true);
            CaptureOne(1080, 1920, 120, 80);
            CaptureOne(720, 1280, 0, 0, true);
        }

        static void CaptureOne(int width, int height, int top = 0, int bottom = 0, bool fixedHeight = false, bool shippedScale = false)
        {
            string folder = Path.GetFullPath("Docs/CollectionUIVerification");
            Directory.CreateDirectory(folder);
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ExpandedCollectionDemoSeeder.PrefabPath));
            var cameraObject = new GameObject("ExpandedCaptureCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .055f, .085f);
            var target = new RenderTexture(width, height, 24);
            target.Create();
            camera.targetTexture = target;
            var before = RenderTexture.active;
            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                if (!shippedScale)
                {
                    root.GetComponent<CanvasScaler>().enabled = false;
                    canvas.scaleFactor = 1;
                }

                var safe = root.GetComponentInChildren<DemoSafeArea>();
                safe.enabled = false;
                var r = (RectTransform)safe.transform;
                r.anchorMin = new Vector2(0, (float)bottom / height);
                r.anchorMax = new Vector2(1, 1f - (float)top / height);
                r.offsetMin = r.offsetMax = Vector2.zero;
                Canvas.ForceUpdateCanvases();
                var demo = root.GetComponent<ExpandedCollectionDemo>();
                demo.Load(8);
                if (fixedHeight)
                {
                    demo.ToggleHeight();
                    demo.Load(4);
                }

                demo.Arrange();
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(folder, "expanded-" + width + "x" + height + (top > 0 ? "-safe-area" : "") + (fixedHeight ? "-fixed-height" : "") + (shippedScale ? "-canvas-scaler" : "") + ".png"), image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                RenderTexture.active = before;
                camera.targetTexture = null;
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}
