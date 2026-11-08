// 滚动演示截图工具。加载生成的 Prefab，使用临时相机和 RenderTexture 验证不同尺寸，结束后释放临时资源。
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CollectionUI.Samples;

namespace CollectionUI.Editor
{
    public static class CollectionViewDemoCapture
    {
        public static void Capture()
        {
            CollectionViewDemoSeeder.Generate();
            string directory = Path.GetFullPath("Docs/CollectionUIVerification");
            Directory.CreateDirectory(directory);
            CaptureOne(directory, 720, 1280);
            CaptureOne(directory, 1080, 1920);
            CaptureOne(directory, 1080, 2400);
            CaptureOne(directory, 1080, 1920, LayoutMode.Columns);
            CaptureOne(directory, 1080, 1920, LayoutMode.Skyline, true);
            CaptureOne(directory, 720, 1280, LayoutMode.Skyline, true, DemoScenario.Tags);
            foreach (var scenario in new[]
            {
                DemoScenario.Tags,
                DemoScenario.Products
            }

            )
            {
                CaptureOne(directory, 720, 1280, LayoutMode.Skyline, false, scenario);
                CaptureOne(directory, 1080, 1920, LayoutMode.Skyline, false, scenario);
                CaptureOne(directory, 1080, 2400, LayoutMode.Skyline, false, scenario);
            }
        }

        /// <summary>方向验收覆盖四场景、窄屏/参考屏/高屏，额外覆盖左右方向的分组边界。</summary>
        public static void CaptureDirections()
        {
            CollectionViewDemoSeeder.Generate();
            string directory = Path.GetFullPath("Docs/CollectionUIVerification");
            Directory.CreateDirectory(directory);
            foreach (DemoScenario scenario in System.Enum.GetValues(typeof(DemoScenario)))
                foreach (var direction in new[]
                {
                    LayoutDirection.LeftToRight,
                    LayoutDirection.RightToLeft
                }

                )
                {
                    CaptureOne(directory, 720, 1280, scenario: scenario, direction: direction);
                    CaptureOne(directory, 1080, 1920, scenario: scenario, direction: direction);
                    CaptureOne(directory, 1080, 2400, scenario: scenario, direction: direction);
                    CaptureOne(directory, 1080, 1920, boundary: true, scenario: scenario, direction: direction);
                }
        }

        static void CaptureOne(string directory, int width, int height, LayoutMode mode = LayoutMode.Skyline, bool boundary = false, DemoScenario? scenario = null, LayoutDirection? direction = null)
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CollectionViewDemoSeeder.PrefabPath));
            var cameraObject = new GameObject("CaptureCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .055f, .085f);
            var rt = new RenderTexture(width, height, 24);
            rt.Create();
            camera.targetTexture = rt;
            var before = RenderTexture.active;
            try
            {
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                var safe = root.GetComponentInChildren<DemoSafeArea>();
                safe.enabled = false;
                var r = (RectTransform)safe.transform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = r.offsetMax = Vector2.zero;
                Canvas.ForceUpdateCanvases();
                var demo = root.GetComponent<CollectionViewDemo>();
                if (scenario.HasValue)
                {
                    root.GetComponent<CanvasScaler>().enabled = false;
                    canvas.scaleFactor = 1;
                    Canvas.ForceUpdateCanvases();
                    demo.SetScenario(scenario.Value);
                    demo.Load(1000);
                }
                else
                {
                    demo.Load(1000);
                    demo.SetMode(mode);
                }

                if (direction.HasValue)
                    demo.SetDirection(direction.Value);
                if (boundary)
                    demo.Collection.ScrollToItem(scenario == DemoScenario.Tags && !direction.HasValue ? "item-14" : "item-199");
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = rt;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(directory, "demo-" + width + "x" + height + (scenario.HasValue ? "-" + scenario.Value.ToString().ToLowerInvariant() : mode == LayoutMode.Columns ? "-columns" : "") + (boundary ? (scenario == DemoScenario.Tags && !direction.HasValue ? "-ellipsis" : "-groups") : "") + (direction.HasValue ? "-" + direction.Value.ToString().ToLowerInvariant() : "") + ".png"), image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                RenderTexture.active = before;
                camera.targetTexture = null;
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
