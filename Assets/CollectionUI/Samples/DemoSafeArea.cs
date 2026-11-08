// 独立示例的安全区适配器。只读取 Unity Screen.safeArea，不依赖业务平台服务；实际项目可替换为自己的安全区父容器。
using UnityEngine;

namespace CollectionUI.Samples
{
    /// <summary>仅供示例使用的安全区控制器；接入业务时将集合放在应用自己的安全区下。</summary>
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "UniversalWaterfall.Samples", "UniversalWaterfall.Samples", "DemoSafeArea")]
    public sealed class DemoSafeArea : MonoBehaviour
    {
        void OnEnable()
        {
            Apply();
        }

        void Update()
        {
            Apply();
        }

        void Apply()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return;
            var area = Screen.safeArea;
            if (area.width <= 0 || area.height <= 0 || area.xMax > Screen.width || area.yMax > Screen.height)
                area = new Rect(0, 0, Screen.width, Screen.height);
            var r = (RectTransform)transform;
            var min = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            var max = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
            if (r.anchorMin != min || r.anchorMax != max)
            {
                r.anchorMin = min;
                r.anchorMax = max;
                r.offsetMin = r.offsetMax = Vector2.zero;
            }
        }
    }
}
